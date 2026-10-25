using System.Numerics;

namespace IslandWar.Sim.Voxels;

/// <summary>The first solid block a ray hits, and the air block it entered from.</summary>
/// <param name="Block">The solid block that was hit. Breaking removes this one.</param>
/// <param name="Before">The cell the ray was in just before the hit. Placing fills this one.</param>
/// <param name="Distance">Distance from the ray origin to where it entered <paramref name="Block"/>.</param>
public readonly record struct RaycastHit(BlockPos Block, BlockPos Before, float Distance);

/// <summary>Block picking against the Sim world, so clients and the server agree on what was aimed at.</summary>
public static class VoxelRaycast
{
    /// <summary>
    /// Walks the grid cell by cell along the ray (Amanatides and Woo) and returns the first solid block
    /// within <paramref name="maxDistance"/>, or null. Block (x, y, z) fills the unit cube from (x, y, z)
    /// to (x + 1, y + 1, z + 1).
    /// </summary>
    public static RaycastHit? Cast(VoxelWorld world, Vector3 origin, Vector3 direction, float maxDistance)
    {
        if (direction.LengthSquared() < 1e-12f || maxDistance <= 0)
        {
            return null;
        }

        direction = Vector3.Normalize(direction);
        var cell = new BlockPos((int)MathF.Floor(origin.X), (int)MathF.Floor(origin.Y), (int)MathF.Floor(origin.Z));
        if (world.Get(cell) != Block.Air)
        {
            return new RaycastHit(cell, cell, 0);
        }

        (int stepX, float nextX, float deltaX) = Axis(origin.X, direction.X);
        (int stepY, float nextY, float deltaY) = Axis(origin.Y, direction.Y);
        (int stepZ, float nextZ, float deltaZ) = Axis(origin.Z, direction.Z);

        while (true)
        {
            BlockPos before = cell;
            float distance;
            if (nextX <= nextY && nextX <= nextZ)
            {
                distance = nextX;
                cell = cell with { X = cell.X + stepX };
                nextX += deltaX;
            }
            else if (nextY <= nextZ)
            {
                distance = nextY;
                cell = cell with { Y = cell.Y + stepY };
                nextY += deltaY;
            }
            else
            {
                distance = nextZ;
                cell = cell with { Z = cell.Z + stepZ };
                nextZ += deltaZ;
            }

            if (distance > maxDistance)
            {
                return null;
            }

            if (world.Get(cell) != Block.Air)
            {
                return new RaycastHit(cell, before, distance);
            }
        }
    }

    /// <summary>Step direction, distance to the first cell boundary, and distance between boundaries on one axis.</summary>
    private static (int Step, float Next, float Delta) Axis(float origin, float direction)
    {
        if (direction == 0)
        {
            return (0, float.PositiveInfinity, float.PositiveInfinity);
        }

        float delta = MathF.Abs(1 / direction);
        float cellStart = MathF.Floor(origin);
        return direction > 0
            ? (1, (cellStart + 1 - origin) * delta, delta)
            : (-1, (origin - cellStart) * delta, delta);
    }
}
