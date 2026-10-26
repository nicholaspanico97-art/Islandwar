using System.Numerics;
using IslandWar.Sim.Players;
using IslandWar.Sim.Vehicles;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Weapons;

/// <summary>A falling bomb.</summary>
public readonly record struct BombState(Vector3 Position, Vector3 Velocity);

/// <summary>Where a bomb hit: a block (which becomes the crater's centre), or the sea.</summary>
public readonly record struct BombImpact(Vector3 Position, BlockPos? Block)
{
    public bool InWater => Block is null;
}

/// <summary>
/// Bomb ballistics and craters. The server runs the real bomb and decides the crater. Clients run the same
/// fall to draw bombs between the drop and the server's explosion, and the pilot uses it for a bombsight.
/// </summary>
public static class BombFlight
{
    /// <summary>Released under the fuselage, moving with the plane.</summary>
    public static BombState Drop(BombSettings settings, PlaneState plane) =>
        new(plane.Position + Vector3.UnitY * settings.DropHeight, plane.Velocity);

    /// <summary>
    /// One tick of falling. The move is checked block by block along its whole length, so a fast bomb can't
    /// pass through a thin wall. The sea surface stops bombs too.
    /// </summary>
    /// <returns>The new state, and the impact if it hit something this tick.</returns>
    public static (BombState Next, BombImpact? Impact) Step(VoxelWorld world, BombSettings settings, int seaLevel, BombState bomb, float dt)
    {
        Vector3 velocity = bomb.Velocity - Vector3.UnitY * settings.Gravity * dt;
        velocity -= velocity * settings.Drag * dt;
        Vector3 move = velocity * dt;
        float length = move.Length();
        if (length == 0)
        {
            return (bomb, null);
        }

        Vector3 direction = move / length;
        float? toWater = bomb.Position.Y < seaLevel ? 0
            : bomb.Position.Y + move.Y < seaLevel ? (bomb.Position.Y - seaLevel) / -direction.Y
            : null;

        RaycastHit? hit = VoxelRaycast.Cast(world, bomb.Position, direction, length);
        if (hit is { } h && (toWater is null || h.Distance <= toWater))
        {
            return (bomb, new BombImpact(bomb.Position + direction * h.Distance, h.Block));
        }

        if (toWater is { } water)
        {
            return (bomb, new BombImpact(bomb.Position + direction * water, null));
        }

        return (new BombState(bomb.Position + move, velocity), null);
    }

    /// <summary>
    /// Destroys the crater (nothing for a splash in the sea) and returns the edits made. The radius comes from
    /// the match phase when the bomb was dropped (see MatchFlow.CraterRadius).
    /// </summary>
    /// <param name="coreFactor">How much of the radius reaches core blocks (BombSettings.CoreBlastFactor; 1 for a nuke).</param>
    public static IReadOnlyList<VoxelEdit> Explode(VoxelWorld world, int craterRadius, BombImpact impact, float coreFactor = 1) =>
        impact.Block is { } centre ? Craters.Blast(world, centre, craterRadius, coreFactor) : Array.Empty<VoxelEdit>();

    /// <summary>
    /// Where a bomb dropped now would land, or null if it's still falling after <paramref name="maxSteps"/>.
    /// The bombsight and the pilot bot use this.
    /// </summary>
    public static BombImpact? PredictImpact(VoxelWorld world, BombSettings settings, int seaLevel, BombState bomb, float dt, int maxSteps)
    {
        for (int i = 0; i < maxSteps; i++)
        {
            (bomb, BombImpact? impact) = Step(world, settings, seaLevel, bomb, dt);
            if (impact is not null)
            {
                return impact;
            }
        }

        return null;
    }
}
