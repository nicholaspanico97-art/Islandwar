using System.Numerics;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Weapons;

/// <summary>Bow and arrow tunables, from the <c>arrow</c> section of <c>data/weapons.json</c>.</summary>
public sealed record ArrowSettings
{
    /// <summary>Blocks per second as it leaves the bow.</summary>
    public float Speed { get; init; } = 40;

    public float Gravity { get; init; } = 15;

    /// <summary>Health taken from a player it hits.</summary>
    public int Damage { get; init; } = 25;

    /// <summary>Seconds between shots.</summary>
    public float DrawSeconds { get; init; } = 0.8f;

    /// <summary>An arrow still flying after this long is dropped.</summary>
    public float MaxLifetimeSeconds { get; init; } = 5;

    /// <summary>How far in front of the eye it starts, so it doesn't begin inside the shooter.</summary>
    public float SpawnAhead { get; init; } = 0.6f;

    public static ArrowSettings FromJson(string weaponsJson)
    {
        ArrowSettings arrow = DataJson.Parse<WeaponsFile>(weaponsJson, "weapons.json").Arrow ?? new ArrowSettings();
        if (arrow.Speed <= 0 || arrow.Gravity < 0 || arrow.Damage <= 0 || arrow.DrawSeconds < 0 || arrow.MaxLifetimeSeconds <= 0 || arrow.SpawnAhead < 0)
        {
            throw new InvalidDataException("weapons.json: arrow needs a positive speed, damage and lifetime, and gravity, draw_seconds and spawn_ahead of 0 or more.");
        }

        return arrow;
    }

    private sealed record WeaponsFile
    {
        public ArrowSettings? Arrow { get; init; }
    }
}

/// <summary>An arrow in flight.</summary>
public readonly record struct ArrowState(Vector3 Position, Vector3 Velocity);

/// <summary>
/// Arrow ballistics. The server flies the real arrow and decides what it hits; clients fly the same arrow
/// to draw it until the server says where it landed.
/// </summary>
public static class ArrowFlight
{
    /// <summary>The way a player looks: yaw 0 faces -Z and turns left as it grows; pitch up is positive.</summary>
    public static Vector3 Look(float yaw, float pitch) =>
        new(-MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), -MathF.Cos(yaw) * MathF.Cos(pitch));

    public static ArrowState Shoot(ArrowSettings settings, Vector3 eye, float yaw, float pitch)
    {
        Vector3 look = Look(yaw, pitch);
        return new ArrowState(eye + look * settings.SpawnAhead, look * settings.Speed);
    }

    /// <summary>
    /// One tick of flight. The move is checked block by block along its length, and the sea surface stops it.
    /// </summary>
    /// <returns>The new state, and where it landed if it hit a block or the sea this tick.</returns>
    public static (ArrowState Next, Vector3? Landed) Step(VoxelWorld world, ArrowSettings settings, int seaLevel, ArrowState arrow, float dt)
    {
        Vector3 velocity = arrow.Velocity - Vector3.UnitY * settings.Gravity * dt;
        Vector3 move = velocity * dt;
        float length = move.Length();
        if (length == 0)
        {
            return (arrow, null);
        }

        Vector3 direction = move / length;
        float? toWater = arrow.Position.Y < seaLevel ? 0
            : arrow.Position.Y + move.Y < seaLevel ? (arrow.Position.Y - seaLevel) / -direction.Y
            : null;
        float? toBlock = VoxelRaycast.Cast(world, arrow.Position, direction, length)?.Distance;
        float? stop = toBlock is { } b && toWater is { } w ? Math.Min(b, w) : toBlock ?? toWater;
        return stop is { } distance
            ? (arrow, arrow.Position + direction * distance)
            : (new ArrowState(arrow.Position + move, velocity), null);
    }

    /// <summary>
    /// Where along the segment from <paramref name="from"/> to <paramref name="to"/> (0 to 1) it first enters
    /// a standing player's box, or null if it misses.
    /// </summary>
    public static float? HitsPlayer(Vector3 from, Vector3 to, Vector3 feet, float width, float height)
    {
        Vector3 min = feet - new Vector3(width / 2, 0, width / 2);
        Vector3 max = feet + new Vector3(width / 2, height, width / 2);
        Vector3 delta = to - from;
        float enter = 0, exit = 1;
        for (int axis = 0; axis < 3; axis++)
        {
            float start = Axis(from, axis), d = Axis(delta, axis), lo = Axis(min, axis), hi = Axis(max, axis);
            if (MathF.Abs(d) < 1e-6f)
            {
                if (start < lo || start > hi)
                {
                    return null;
                }

                continue;
            }

            float t1 = (lo - start) / d, t2 = (hi - start) / d;
            enter = Math.Max(enter, Math.Min(t1, t2));
            exit = Math.Min(exit, Math.Max(t1, t2));
            if (enter > exit)
            {
                return null;
            }
        }

        return enter;
    }

    private static float Axis(Vector3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };
}
