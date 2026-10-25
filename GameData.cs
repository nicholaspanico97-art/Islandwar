using Godot;
using IslandWar.Sim.Players;
using IslandWar.Sim.Protocol;
using IslandWar.Sim.Voxels;

namespace IslandWar;

/// <summary>
/// The tunables from <c>data/</c>. The server sends its raw JSON to each client on join, so clients
/// generate the same island and predict movement with the same numbers as the server.
/// </summary>
public sealed record GameData(string TerrainJson, string PlayerJson, string NetJson)
{
    public IslandSettings Terrain { get; } = IslandSettings.FromJson(TerrainJson);

    public PlayerSettings Player { get; } = PlayerSettings.FromJson(PlayerJson);

    public NetSettings Net { get; } = NetSettings.FromJson(NetJson);

    public static GameData Load() => new(
        FileAccess.GetFileAsString("res://data/terrain.json"),
        FileAccess.GetFileAsString("res://data/player.json"),
        FileAccess.GetFileAsString("res://data/net.json"));

    /// <summary>Makes Godot's physics tick match the simulation tick rate.</summary>
    public void ApplyTickRate() => Engine.PhysicsTicksPerSecond = Net.TickRate;

    /// <summary>Where players appear: standing on the top block at the centre of the island.</summary>
    public PlayerState SpawnIn(VoxelWorld world)
    {
        int x = world.SizeX / 2;
        int z = world.SizeZ / 2;
        int top = world.HighestSolid(x, z) ?? Terrain.SeaLevel;
        return PlayerState.At(new System.Numerics.Vector3(x + 0.5f, top + 1, z + 0.5f));
    }
}
