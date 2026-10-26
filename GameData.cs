using System;
using System.IO;
using Godot;
using IslandWar.Sim;
using IslandWar.Sim.Players;
using IslandWar.Sim.Protocol;
using IslandWar.Sim.Vehicles;
using IslandWar.Sim.Voxels;
using FileAccess = Godot.FileAccess;

namespace IslandWar;

/// <summary>
/// The tunables from <c>data/</c>. The server sends its raw JSON to each client on join, so clients
/// generate the same island and predict movement with the same numbers as the server.
/// </summary>
public sealed record GameData(string TerrainJson, string PlayerJson, string NetJson, string PlaneJson)
{
    public IslandSettings Terrain { get; } = IslandSettings.FromJson(TerrainJson);

    public PlayerSettings Player { get; } = PlayerSettings.FromJson(PlayerJson);

    public NetSettings Net { get; } = NetSettings.FromJson(NetJson);

    public PlaneSettings Plane { get; } = CheckedPlane(PlaneJson, NetSettings.FromJson(NetJson));

    public GameRules Rules => new(Player, Plane, Terrain.SeaLevel);

    public static GameData Load() => new(
        FileAccess.GetFileAsString("res://data/terrain.json"),
        FileAccess.GetFileAsString("res://data/player.json"),
        FileAccess.GetFileAsString("res://data/net.json"),
        FileAccess.GetFileAsString("res://data/plane.json"));

    /// <summary>Makes Godot's physics tick match the simulation tick rate.</summary>
    public void ApplyTickRate() => Engine.PhysicsTicksPerSecond = Net.TickRate;

    /// <summary>
    /// Where a player appears, standing on the top block. Slot 0 is the centre of the island; later slots
    /// go round a small ring so players joining together don't stand inside each other.
    /// </summary>
    public PlayerState SpawnIn(VoxelWorld world, int slot = 0)
    {
        const int ringRadius = 3;
        const int ringSlots = 8;
        int x = world.SizeX / 2;
        int z = world.SizeZ / 2;
        if (slot > 0)
        {
            double angle = 2 * Math.PI * ((slot - 1) % ringSlots) / ringSlots;
            x += (int)Math.Round(ringRadius * Math.Cos(angle));
            z += (int)Math.Round(ringRadius * Math.Sin(angle));
        }

        int top = world.HighestSolid(x, z) ?? Terrain.SeaLevel;
        return PlayerState.At(new System.Numerics.Vector3(x + 0.5f, top + 1, z + 0.5f));
    }

    /// <summary>The plane starts on the water just off the south beach, facing out to sea: open water to take off over.</summary>
    public PlaneState PlaneSpawnIn(VoxelWorld world)
    {
        const int offshore = 6;
        int x = world.SizeX / 2;
        int z = world.SizeZ / 2 + Terrain.IslandRadius + offshore;
        int ground = Math.Max(Terrain.SeaLevel, (world.HighestSolid(x, z) ?? 0) + 1);
        return PlaneState.Parked(new System.Numerics.Vector3(x + 0.5f, ground, z + 0.5f), MathF.PI);
    }

    /// <summary>
    /// A plane may not cross more than one block per tick, or the block collision could skip one. Sink only
    /// builds up below stall speed, so the fastest move is max_speed, or stall_speed plus max_sink.
    /// </summary>
    private static PlaneSettings CheckedPlane(string json, NetSettings net)
    {
        PlaneSettings plane = PlaneSettings.FromJson(json);
        if (Math.Max(plane.MaxSpeed, plane.StallSpeed + plane.MaxSink) >= net.TickRate)
        {
            throw new InvalidDataException($"plane max_speed, and stall_speed + max_sink, must stay under tick_rate ({net.TickRate}).");
        }

        return plane;
    }
}
