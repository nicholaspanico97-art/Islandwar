using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using IslandWar.Sim;
using IslandWar.Sim.Match;
using IslandWar.Sim.Players;
using IslandWar.Sim.Protocol;
using IslandWar.Sim.Vehicles;
using IslandWar.Sim.Voxels;
using IslandWar.Sim.Weapons;
using FileAccess = Godot.FileAccess;

namespace IslandWar;

/// <summary>
/// The tunables from <c>data/</c>. The server sends its raw JSON to each client on join, so clients
/// generate the same island and predict movement with the same numbers as the server.
/// </summary>
public sealed record GameData(string TerrainJson, string PlayerJson, string NetJson, string PlaneJson, string WeaponsJson, string PresetsJson)
{
    public IslandSettings Terrain { get; } = IslandSettings.FromJson(TerrainJson);

    public PlayerSettings Player { get; } = PlayerSettings.FromJson(PlayerJson);

    public NetSettings Net { get; } = NetSettings.FromJson(NetJson);

    public PlaneSettings Plane { get; } = CheckedPlane(PlaneJson, NetSettings.FromJson(NetJson));

    public BombSettings Bomb { get; } = BombSettings.FromJson(WeaponsJson);

    public IReadOnlyDictionary<string, MatchPreset> Presets { get; } = MatchPreset.AllFromJson(PresetsJson);

    public GameRules Rules => new(Player, Plane, Bomb, Terrain.SeaLevel);

    public static GameData Load() => new(
        FileAccess.GetFileAsString("res://data/terrain.json"),
        FileAccess.GetFileAsString("res://data/player.json"),
        FileAccess.GetFileAsString("res://data/net.json"),
        FileAccess.GetFileAsString("res://data/plane.json"),
        FileAccess.GetFileAsString("res://data/weapons.json"),
        FileAccess.GetFileAsString("res://data/presets.json"));

    /// <summary>Makes Godot's physics tick match the simulation tick rate.</summary>
    public void ApplyTickRate() => Engine.PhysicsTicksPerSecond = Net.TickRate;

    /// <summary>
    /// Where a player appears, standing on the top block of their team's island. Slot 0 is the centre;
    /// later slots go round a small ring so players joining together don't stand inside each other.
    /// </summary>
    public PlayerState SpawnIn(VoxelWorld world, Team team, int slot = 0)
    {
        const int ringSlots = 8;
        (int x, int z) = Terrain.IslandCentre(world.SizeX, world.SizeZ, team.IslandIndex());

        const int ringRadius = 3;
        if (slot > 0)
        {
            double angle = 2 * Math.PI * ((slot - 1) % ringSlots) / ringSlots;
            x += (int)Math.Round(ringRadius * Math.Cos(angle));
            z += (int)Math.Round(ringRadius * Math.Sin(angle));
        }

        int top = world.HighestSolid(x, z) ?? Terrain.SeaLevel;
        return PlayerState.At(new System.Numerics.Vector3(x + 0.5f, top + 1, z + 0.5f));
    }

    /// <summary>
    /// A team's plane starts on the water just off the far beach of its island, facing out to sea, away from
    /// the enemy: open water to take off over.
    /// </summary>
    public PlaneState PlaneSpawnIn(VoxelWorld world, Team team)
    {
        const int offshore = 6;
        (int x, int z) = Terrain.IslandCentre(world.SizeX, world.SizeZ, team.IslandIndex());
        bool west = team != Team.Blue;
        x += (west ? -1 : 1) * (Terrain.IslandRadius + offshore);
        int ground = Math.Max(Terrain.SeaLevel, (world.HighestSolid(x, z) ?? 0) + 1);

        // Heading 0 faces -Z and increases to the left, so +90 degrees faces west (-X).
        return PlaneState.Parked(new System.Numerics.Vector3(x + 0.5f, ground, z + 0.5f), west ? MathF.PI / 2 : -MathF.PI / 2);
    }

    /// <summary>The named preset, or an error listing the ones that exist.</summary>
    public MatchPreset Preset(string name) =>
        Presets.TryGetValue(name, out MatchPreset? preset)
            ? preset
            : throw new InvalidDataException($"No preset '{name}' in presets.json. Choose one of: {string.Join(", ", Presets.Keys)}.");

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
