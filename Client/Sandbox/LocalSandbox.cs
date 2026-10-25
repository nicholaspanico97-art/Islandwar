using Godot;
using IslandWar.Client.Ui;
using IslandWar.Client.Voxels;
using IslandWar.Sim.Players;
using IslandWar.Sim.Voxels;

namespace IslandWar.Client.Sandbox;

/// <summary>
/// Single-player sandbox (M1a): the client generates the island itself and edits it locally. Every
/// edit is checked with <see cref="BuildRules"/>, applied with <see cref="VoxelWorld.ApplyEdit"/> and
/// then shown, which is the same path the networked version (M1b) will take with the server in the middle.
/// </summary>
public partial class LocalSandbox : Node3D
{
    /// <summary>Falling this far below the world floor respawns the player.</summary>
    private const float FallLimit = -16;

    private VoxelWorld _world = null!;
    private GodotVoxelView _view = null!;
    private PlayerSettings _settings = null!;
    private PlayerController _player = null!;
    private BlockHighlight _highlight = null!;
    private Hotbar _hotbar = null!;
    private Vector3 _spawn;

    public LaunchArgs Args { get; init; } = new();

    public override void _Ready()
    {
        Name = "Sandbox";

        IslandSettings terrain = IslandSettings.FromJson(FileAccess.GetFileAsString("res://data/terrain.json"));
        _settings = PlayerSettings.FromJson(FileAccess.GetFileAsString("res://data/player.json"));
        int seed = Args.Seed ?? terrain.DefaultSeed;
        _world = IslandGenerator.Generate(terrain, seed);
        GD.Print($"[client] local sandbox: seed {seed}, {_world.SizeX}x{_world.SizeY}x{_world.SizeZ} blocks, hash {_world.ComputeHash():x16}");

        AddEnvironment();
        AddSea(terrain);

        _view = new GodotVoxelView { World = _world };
        AddChild(_view);
        _highlight = new BlockHighlight();
        AddChild(_highlight);

        int cx = _world.SizeX / 2;
        int cz = _world.SizeZ / 2;
        _spawn = new Vector3(cx + 0.5f, (_world.HighestSolid(cx, cz) ?? terrain.SeaLevel) + 1, cz + 0.5f);
        _player = new PlayerController { Settings = _settings };
        AddChild(_player);
        _player.Teleport(_spawn, 0);

        AddHud();
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _PhysicsProcess(double delta)
    {
        // Hold the player in place until the ground under the spawn has collision, or they'd fall through.
        if (_player.Frozen && _view.IsAreaMeshed(new Aabb(_spawn - new Vector3(2, 3, 2), new Vector3(4, 5, 4))))
        {
            _player.Frozen = false;
        }

        if (_player.GlobalPosition.Y < FallLimit)
        {
            _player.Teleport(_spawn, 0);
        }

        _highlight.ShowAt(Aim()?.Block);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(InputActions.ReleaseMouse))
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else if (Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            // While the mouse is free, a click only recaptures it; it doesn't edit.
            if (@event is InputEventMouseButton { Pressed: true })
            {
                Input.MouseMode = Input.MouseModeEnum.Captured;
                GetViewport().SetInputAsHandled();
            }
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

    private RaycastHit? Aim() =>
        VoxelRaycast.Cast(_world, ToSim(_player.EyePosition), ToSim(_player.LookDirection), _settings.Reach);

    private void Break()
    {
        if (Aim() is { } hit && BuildRules.CanBreak(_world, hit.Block))
        {
            Apply(new VoxelEdit(hit.Block, Block.Air));
        }
    }

    private void Place()
    {
        // Flying turns collisions off, so the player's own box doesn't block placement then.
        System.Numerics.Vector3[] blockers = _player.Flying ? System.Array.Empty<System.Numerics.Vector3>() : new[] { ToSim(_player.GlobalPosition) };
        if (Aim() is { } hit && BuildRules.CanPlace(_world, _settings, hit.Before, _hotbar.Selected, blockers))
        {
            Apply(new VoxelEdit(hit.Before, _hotbar.Selected));
        }
    }

    private void Apply(VoxelEdit edit)
    {
        if (_world.ApplyEdit(edit))
        {
            _view.ShowEdit(edit);
        }
    }

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
    private void AddSea(IslandSettings terrain)
    {
        float size = Mathf.Max(_world.SizeX, _world.SizeZ) * 8;
        var centre = new Vector3(_world.SizeX / 2f, 0, _world.SizeZ / 2f);

        // Just under the top face of the seabed blocks, so inside the map the blocks cover it.
        float seabedTop = terrain.SeaLevel - terrain.SeabedDepth + 1;
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
            Position = centre + Vector3.Up * (terrain.SeaLevel - 0.1f),
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
