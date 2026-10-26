using Godot;
using IslandWar.Client.Game;
using IslandWar.Client.Online;
using IslandWar.Client.Voxels;
using IslandWar.Sim.Match;
using IslandWar.Sim.Voxels;

namespace IslandWar.Client.Bots;

/// <summary>
/// When a bot runs in a window (without <c>--headless</c>) it draws what it sees: the terrain streamed in
/// around its pawn, from a camera above and behind. Only for watching or recording bot runs; the bots
/// themselves never look at it.
/// </summary>
public partial class BotView : Node3D
{
    private static readonly Vector3 CameraOffset = new(0, 30, 50);

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

        ViewSettings settings = ViewSettings.FromJson(FileAccess.GetFileAsString("res://data/view.json"));
        AddChild(new Scenery { World = Net.World!, Terrain = Net.Data!.Terrain, View = settings });
        _view = new GodotVoxelView { World = Net.World!, Settings = settings, ViewDistance = settings.FlyingViewDistance };
        AddChild(_view);
    }

    private void ShowEdit(VoxelEdit edit)
    {
        if (_view?.World == Net.World)
        {
            _view?.ShowEdit(edit);
        }
    }
}
