using Godot;
using IslandWar.Client.Game;
using IslandWar.Sim.Match;
using IslandWar.Sim.Players;
using IslandWar.Sim.Voxels;

namespace IslandWar.Client.Sandbox;

/// <summary>
/// Single-player sandbox (M1a): the client generates the island itself and plays it with no server.
/// The same <see cref="ClientGame"/> runs online; here it simply has no link.
/// </summary>
public partial class LocalSandbox : Node
{
    public LaunchArgs Args { get; init; } = new();

    public override void _Ready()
    {
        Name = "Sandbox";

        GameData data = GameData.Load();
        data.ApplyTickRate();
        int seed = Args.Seed ?? data.Terrain.DefaultSeed;
        VoxelWorld world = IslandGenerator.Generate(data.Terrain, seed);
        GD.Print($"[client] local sandbox: seed {seed}, {world.SizeX}x{world.SizeY}x{world.SizeZ} blocks, hash {world.ComputeHash():x16}");

        AddChild(new ClientGame
        {
            World = world,
            Terrain = data.Terrain,
            Rules = data.Rules,
            Net = data.Net,
            Spawn = PawnState.OnFoot(data.SpawnIn(world, Team.Red)),
        });
    }
}
