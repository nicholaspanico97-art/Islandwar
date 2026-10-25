using Godot;
using IslandWar.Client.Voxels;
using IslandWar.Sim.Players;
using IslandWar.Sim.Voxels;

namespace IslandWar.Client.Sandbox;

/// <summary>
/// Single-player sandbox (M1a): the client generates the island itself and edits it locally. Every
/// edit goes through <see cref="VoxelWorld.ApplyEdit"/> first and is then shown, which is the same path
/// the networked version (M1b) will take with the server in the middle.
/// </summary>
public partial class LocalSandbox : Node3D
{
    private VoxelWorld _world = null!;
    private GodotVoxelView _view = null!;
    private PlayerSettings _player = null!;
    private Camera3D _camera = null!;
    private Label _help = null!;
    private Block _selected = Block.Planks;

    public LaunchArgs Args { get; init; } = new();

    public override void _Ready()
    {
        Name = "Sandbox";

        IslandSettings terrain = IslandSettings.FromJson(FileAccess.GetFileAsString("res://data/terrain.json"));
        _player = PlayerSettings.FromJson(FileAccess.GetFileAsString("res://data/player.json"));
        int seed = Args.Seed ?? terrain.DefaultSeed;
        _world = IslandGenerator.Generate(terrain, seed);
        GD.Print($"[client] local sandbox: seed {seed}, {_world.SizeX}x{_world.SizeY}x{_world.SizeZ} blocks, hash {_world.ComputeHash():x16}");

        AddEnvironment();
        AddWater(terrain.SeaLevel);

        _view = new GodotVoxelView { World = _world };
        AddChild(_view);

        var center = new Vector3(_world.SizeX / 2f, terrain.SeaLevel, _world.SizeZ / 2f);
        _camera = new FlyCamera { Settings = _player, Position = center + new Vector3(0, 25, 60) };
        AddChild(_camera);
        _camera.LookAt(center);

        AddHud();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else if (@event is InputEventKey { Pressed: true, Echo: false } key && key.Keycode >= Key.Key1 && key.Keycode <= Key.Key5)
        {
            _selected = (Block)(key.Keycode - Key.Key1 + 1);
            UpdateHelp();
        }
        else if (@event is InputEventMouseButton { Pressed: true } click)
        {
            if (Input.MouseMode != Input.MouseModeEnum.Captured)
            {
                Input.MouseMode = Input.MouseModeEnum.Captured;
            }
            else if (click.ButtonIndex is MouseButton.Left or MouseButton.Right)
            {
                EditAtCrosshair(place: click.ButtonIndex == MouseButton.Right);
            }
        }
    }

    private void EditAtCrosshair(bool place)
    {
        Vector3 from = _camera.GlobalPosition;
        Vector3 forward = -_camera.GlobalBasis.Z;
        RaycastHit? hit = VoxelRaycast.Cast(
            _world,
            new System.Numerics.Vector3(from.X, from.Y, from.Z),
            new System.Numerics.Vector3(forward.X, forward.Y, forward.Z),
            _player.Reach);
        if (hit is not { } h)
        {
            return;
        }

        var edit = place ? new VoxelEdit(h.Before, _selected) : new VoxelEdit(h.Block, Block.Air);
        if (_world.ApplyEdit(edit))
        {
            _view.ShowEdit(edit);
        }
    }

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

    /// <summary>Water is a flat plane at sea level, not blocks (see docs/decisions.md).</summary>
    private void AddWater(int seaLevel)
    {
        float size = Mathf.Max(_world.SizeX, _world.SizeZ) * 4;
        AddChild(new MeshInstance3D
        {
            Name = "Water",
            Mesh = new PlaneMesh { Size = new Vector2(size, size) },
            Position = new Vector3(_world.SizeX / 2f, seaLevel - 0.1f, _world.SizeZ / 2f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.15f, 0.35f, 0.6f, 0.75f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                Roughness = 0.2f,
            },
        });
    }

    private void AddHud()
    {
        var hud = new CanvasLayer();
        var crosshair = new Label { Text = "+", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        crosshair.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        hud.AddChild(crosshair);
        _help = new Label { Position = new Vector2(12, 8) };
        hud.AddChild(_help);
        AddChild(hud);
        UpdateHelp();
    }

    private void UpdateHelp()
    {
        _help.Text = $"Click to capture mouse, Esc to release. WASD, Space/Ctrl, Shift: fly. Left click: break. Right click: place {_selected} (1-5 to pick).";
    }
}
