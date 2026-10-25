using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using IslandWar.Client.Ui;
using IslandWar.Client.Voxels;
using IslandWar.Sim.Players;
using IslandWar.Sim.Protocol;
using IslandWar.Sim.Voxels;

namespace IslandWar.Client.Game;

/// <summary>
/// What a player sees and controls: the rendered world, the first-person camera, the HUD and the other
/// players. The local player moves by Sim prediction every tick. With a <see cref="Link"/> the inputs
/// and edits also go to the server, which can correct them; without one (the sandbox) the prediction
/// is simply the truth.
/// </summary>
public partial class ClientGame : Node3D
{
    /// <summary>Corrections bigger than this snap instead of being smoothed (a respawn, say).</summary>
    private const float SnapDistance = 2;

    /// <summary>How fast a smoothed correction fades out, per second.</summary>
    private const float SmoothingRate = 15;

    private readonly Dictionary<int, RemoteAvatar> _avatars = new();
    private GodotVoxelView _view = null!;
    private BlockHighlight _highlight = null!;
    private Hotbar _hotbar = null!;
    private Camera3D _camera = null!;
    private Node3D _others = null!;
    private PredictedPlayer _player = null!;
    private Vector3 _previousPosition;
    private Vector3 _renderOffset;
    private float _yaw;
    private float _pitch;
    private bool _wantsFlying;

    public VoxelWorld World { get; init; } = null!;

    public IslandSettings Terrain { get; init; } = null!;

    public PlayerSettings Settings { get; init; } = null!;

    public NetSettings Net { get; init; } = null!;

    /// <summary>Where the local player starts. The server's spawn when online.</summary>
    public PlayerState Spawn { get; init; }

    public IGameLink? Link { get; init; }

    public override void _Ready()
    {
        Name = "ClientGame";

        AddEnvironment();
        AddSea();
        _view = new GodotVoxelView { World = World };
        AddChild(_view);
        _highlight = new BlockHighlight();
        AddChild(_highlight);
        _others = new Node3D { Name = "OtherPlayers" };
        AddChild(_others);

        _player = new PredictedPlayer(Spawn);
        _previousPosition = ToGodot(Spawn.Position);
        _camera = new Camera3D { Fov = 80 };
        AddChild(_camera);
        _camera.MakeCurrent();

        AddHud();
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <summary>Draws a block change the client didn't predict: another player's, or a server correction.</summary>
    public void ShowWorldChange(VoxelEdit edit) => _view.ShowEdit(edit);

    /// <summary>
    /// The server's state for the local player after it processed input <paramref name="ack"/>. Small
    /// corrections are blended out over a few frames; big ones snap.
    /// </summary>
    public void Reconcile(PlayerState server, uint ack)
    {
        Vector3 before = ToGodot(_player.State.Position);
        _player.Reconcile(World, Settings, server, ack, Net.TickSeconds);
        Vector3 shift = ToGodot(_player.State.Position) - before;
        _previousPosition += shift;
        if (shift.Length() > SnapDistance)
        {
            _renderOffset = Vector3.Zero;
        }
        else
        {
            _renderOffset -= shift;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        if (@event is InputEventMouseMotion motion && captured)
        {
            _yaw -= Mathf.DegToRad(motion.Relative.X * Settings.LookSensitivity);
            _pitch = Mathf.Clamp(_pitch - Mathf.DegToRad(motion.Relative.Y * Settings.LookSensitivity), -Mathf.Pi / 2 + 0.01f, Mathf.Pi / 2 - 0.01f);
        }
        else if (@event.IsActionPressed(InputActions.ReleaseMouse))
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else if (!captured)
        {
            // While the mouse is free, a click only recaptures it; it doesn't edit.
            if (@event is InputEventMouseButton { Pressed: true })
            {
                Input.MouseMode = Input.MouseModeEnum.Captured;
                GetViewport().SetInputAsHandled();
            }
        }
        else if (@event.IsActionPressed(InputActions.ToggleFly))
        {
            _wantsFlying = !_wantsFlying;
        }
        else if (@event.IsActionPressed(InputActions.Break))
        {
            Break();
        }
        else if (@event.IsActionPressed(InputActions.Place))
        {
            Place();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        _previousPosition = ToGodot(_player.State.Position);
        PlayerInput sent = _player.Predict(World, Settings, ReadInput(), Net.TickSeconds);
        Link?.SendInputs(_player.Latest(Net.InputsPerPacket));

        // Online, the server decides respawns and the correction arrives with the next snapshot.
        if (Link is null && _player.State.Position.Y < Settings.RespawnBelowY)
        {
            _player.Reconcile(World, Settings, Spawn, sent.Seq, Net.TickSeconds);
            _previousPosition = ToGodot(Spawn.Position);
        }

        _highlight.ShowAt(Aim()?.Block);
    }

    public override void _Process(double delta)
    {
        _renderOffset *= Mathf.Exp(-SmoothingRate * (float)delta);
        float fraction = (float)Engine.GetPhysicsInterpolationFraction();
        Vector3 feet = _previousPosition.Lerp(ToGodot(_player.State.Position), fraction) + _renderOffset;
        _camera.GlobalPosition = feet + Vector3.Up * Settings.EyeHeight;
        _camera.Rotation = new Vector3(_pitch, _yaw, 0);

        if (Link is not null)
        {
            ShowOtherPlayers(Link.SampleRemotePlayers());
        }
    }

    private PlayerInput ReadInput()
    {
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        Vector2 move = captured
            ? Input.GetVector(InputActions.MoveLeft, InputActions.MoveRight, InputActions.MoveForward, InputActions.MoveBack)
            : Vector2.Zero;
        InputButtons buttons = InputButtons.None;
        if (captured && Input.IsActionPressed(InputActions.Jump))
        {
            buttons |= InputButtons.Jump;
        }

        if (captured && Input.IsActionPressed(InputActions.Sprint))
        {
            buttons |= InputButtons.Sprint;
        }

        if (captured && Input.IsActionPressed(InputActions.Descend))
        {
            buttons |= InputButtons.Descend;
        }

        if (_wantsFlying)
        {
            buttons |= InputButtons.Fly;
        }

        return new PlayerInput(0, move.X, move.Y, _yaw, _pitch, buttons);
    }

    private RaycastHit? Aim()
    {
        Vector3 eye = ToGodot(_player.State.Position) + Vector3.Up * Settings.EyeHeight;
        Vector3 look = Basis.FromEuler(new Vector3(_pitch, _yaw, 0)) * Vector3.Forward;
        return VoxelRaycast.Cast(World, ToSim(eye), ToSim(look), Settings.Reach);
    }

    private void Break()
    {
        if (Aim() is { } hit && BuildRules.CanBreak(World, hit.Block))
        {
            Predict(new VoxelEdit(hit.Block, Block.Air));
        }
    }

    private void Place()
    {
        // Flying turns collisions off, so the player's own box doesn't block placement then. Other players
        // are left to the server, which knows where they really are.
        System.Numerics.Vector3[] blockers = _player.State.Flying ? Array.Empty<System.Numerics.Vector3>() : new[] { _player.State.Position };
        if (Aim() is { } hit && BuildRules.CanPlace(World, Settings, hit.Before, _hotbar.Selected, blockers))
        {
            Predict(new VoxelEdit(hit.Before, _hotbar.Selected));
        }
    }

    /// <summary>Applies an edit at once. Online, the server confirms it or sends back the real block.</summary>
    private void Predict(VoxelEdit edit)
    {
        if (World.ApplyEdit(edit))
        {
            _view.ShowEdit(edit);
            Link?.SubmitEdit(edit);
        }
    }

    private void ShowOtherPlayers(IReadOnlyDictionary<int, RemoteState> others)
    {
        foreach ((int peer, RemoteState state) in others)
        {
            if (!_avatars.TryGetValue(peer, out RemoteAvatar? avatar))
            {
                avatar = new RemoteAvatar { PeerId = peer, Settings = Settings };
                _others.AddChild(avatar);
                _avatars[peer] = avatar;
            }

            avatar.ShowState(state);
        }

        foreach (int gone in _avatars.Keys.Where(peer => !others.ContainsKey(peer)).ToList())
        {
            _avatars[gone].QueueFree();
            _avatars.Remove(gone);
        }
    }

    private static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    private static System.Numerics.Vector3 ToSim(Vector3 v) => new(v.X, v.Y, v.Z);

    private void AddEnvironment()
    {
        var sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() };
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = sky,
                AmbientLightSource = Godot.Environment.AmbientSource.Sky,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
            },
        });

        var sun = new DirectionalLight3D { ShadowEnabled = true };
        AddChild(sun);
        sun.RotationDegrees = new Vector3(-55, -30, 0);
    }

    /// <summary>
    /// Water is a flat plane at sea level, not blocks (see docs/decisions.md). A sand-coloured floor
    /// continues the seabed past the world's edge, so the map's square boundary doesn't show through.
    /// </summary>
    private void AddSea()
    {
        float size = Mathf.Max(World.SizeX, World.SizeZ) * 8;
        var centre = new Vector3(World.SizeX / 2f, 0, World.SizeZ / 2f);

        // Just under the top face of the seabed blocks, so inside the map the blocks cover it.
        float seabedTop = Terrain.SeaLevel - Terrain.SeabedDepth + 1;
        AddChild(new MeshInstance3D
        {
            Name = "SeabedBeyondMap",
            Mesh = new PlaneMesh { Size = new Vector2(size, size) },
            Position = centre + Vector3.Up * (seabedTop - 0.02f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = BlockColors.Of(Block.Sand) },
        });

        AddChild(new MeshInstance3D
        {
            Name = "Water",
            Mesh = new PlaneMesh { Size = new Vector2(size, size) },
            Position = centre + Vector3.Up * (Terrain.SeaLevel - 0.1f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.08f, 0.25f, 0.5f, 0.85f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                Roughness = 0.2f,
            },
        });
    }

    private void AddHud()
    {
        var hud = new CanvasLayer { Name = "Hud" };
        AddChild(hud);
        hud.AddChild(new Crosshair());
        _hotbar = new Hotbar();
        hud.AddChild(_hotbar);
        var help = new Label
        {
            Position = new Vector2(12, 8),
            Text = "WASD move, Space jump, Shift sprint, F fly (Space/Ctrl up/down). Left click break, right click place. 1-5 or wheel: block. Esc frees the mouse.",
        };
        help.AddThemeColorOverride("font_outline_color", Colors.Black);
        help.AddThemeConstantOverride("outline_size", 4);
        hud.AddChild(help);
    }
}
