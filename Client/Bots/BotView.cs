using System.Collections.Generic;
using System.Linq;
using Godot;
using IslandWar.Client.Game;
using IslandWar.Client.Online;
using IslandWar.Client.Voxels;
using IslandWar.Sim.Match;
using IslandWar.Sim.Players;
using IslandWar.Sim.Voxels;

namespace IslandWar.Client.Bots;

/// <summary>
/// When a bot runs in a window (without <c>--headless</c>) it draws what it sees: the terrain, structures and
/// vehicles around its pawn, from a camera above and behind. Only for watching or recording bot runs; the bots
/// themselves never look at it.
/// </summary>
public partial class BotView : Node3D
{
    private static readonly Vector3 CameraOffset = new(0, 30, 50);

    private readonly Dictionary<int, IVehicleModel> _vehicles = new();
    private GodotVoxelView? _view;
    private Camera3D _camera = null!;

    public TestBot Bot { get; init; } = null!;

    public NetClient Net { get; init; } = null!;

    public override void _Ready()
    {
        Name = "BotView";
        _camera = new Camera3D { Current = true };
        AddChild(_camera);
        Net.WorldChanged += ShowEdit;
    }

    public override void _ExitTree()
    {
        Net.WorldChanged -= ShowEdit;
    }

    public override void _Process(double delta)
    {
        // A new match brings a new world; draw that one instead.
        if (Net.World is null || Net.Data is null)
        {
            return;
        }

        if (_view?.World != Net.World)
        {
            Rebuild();
        }

        Vector3 focus;
        if (Bot.Eye is { } eye)
        {
            focus = new Vector3(eye.X, eye.Y, eye.Z);
        }
        else
        {
            (int x, int z) = Net.Data.Terrain.IslandCentre(Net.MyTeam.IslandIndex());
            focus = new Vector3(x, Net.World.HighestSolid(x, z) ?? Net.Data.Terrain.SeaLevel, z);
        }

        _view!.Focus = focus;
        _camera.LookAtFromPosition(focus + CameraOffset, focus);
        ShowVehicles(delta);
    }

    private void ShowVehicles(double delta)
    {
        IReadOnlyDictionary<int, RemotePlane> planes = Net.SampleRemotePlanes();
        foreach ((int id, RemotePlane plane) in planes)
        {
            if (!_vehicles.TryGetValue(id, out IVehicleModel? model))
            {
                model = plane.Ship is { } ship ? new ShipModel { PlaneId = id, Kind = Net.Data!.Rules.Ship(ship) } : new PlaneModel { PlaneId = id };
                AddChild((Node)model);
                _vehicles[id] = model;
            }

            model.SetTeam(plane.Team);
            model.ShowState(new Vector3(plane.Position.X, plane.Position.Y, plane.Position.Z),
                new Quaternion(plane.Rotation.X, plane.Rotation.Y, plane.Rotation.Z, plane.Rotation.W), plane.Throttle, delta);
        }

        foreach (int gone in _vehicles.Keys.Where(id => !planes.ContainsKey(id)).ToList())
        {
            ((Node)_vehicles[gone]).QueueFree();
            _vehicles.Remove(gone);
        }
    }

    private void Rebuild()
    {
        foreach (Node child in GetChildren())
        {
            if (child != _camera)
            {
                child.QueueFree();
            }
        }

        _vehicles.Clear();

        ViewSettings settings = ViewSettings.FromJson(FileAccess.GetFileAsString("res://data/view.json"));
        AddChild(new Scenery { World = Net.World!, Terrain = Net.Data!.Terrain, View = settings });
        _view = new GodotVoxelView { World = Net.World!, Settings = settings, ViewDistance = settings.FlyingViewDistance };
        AddChild(_view);
        AddChild(new ObjectViews { Objects = Net.Objects });
    }

    private void ShowEdit(VoxelEdit edit)
    {
        if (_view?.World == Net.World)
        {
            _view?.ShowEdit(edit);
        }
    }
}
