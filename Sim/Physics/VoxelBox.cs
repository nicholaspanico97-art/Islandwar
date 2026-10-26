using System.Numerics;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Physics;

/// <summary>
/// An upright box moving through the block grid, one axis at a time. The box is centred on its position
/// horizontally and rises <see cref="Height"/> above it, so the position is the middle of its base.
/// </summary>
/// <param name="Width">Width and depth of the box.</param>
/// <param name="Height">Height of the box above its position.</param>
/// <param name="FloorY">Every cell below this height counts as solid. The plane uses sea level so the water holds it up.</param>
public readonly record struct VoxelBox(float Width, float Height, int FloorY = int.MinValue)
{
    // Gap kept between the box and a block face after a collision, so the next move starts clear of it.
    private const float Skin = 1e-4f;

    // Shrinks the box slightly when checking overlaps, so touching a face doesn't count as overlapping it.
    private const float Epsilon = 1e-5f;

    /// <summary>
    /// Moves along one axis (0 = X, 1 = Y, 2 = Z), stopping against the first solid cell. A move must be
    /// under one block, so a block can't be skipped.
    /// </summary>
    /// <returns>True if a cell stopped the move.</returns>
    public bool MoveAxis(VoxelWorld world, ref Vector3 position, int axis, float delta)
    {
        if (delta == 0)
        {
            return false;
        }

        Vector3 moved = position;
        Set(ref moved, axis, Get(position, axis) + delta);
        float half = Width / 2;
        int x0 = Floor(moved.X - half + Epsilon), x1 = Floor(moved.X + half - Epsilon);
        int y0 = Floor(moved.Y + Epsilon), y1 = Floor(moved.Y + Height - Epsilon);
        int z0 = Floor(moved.Z - half + Epsilon), z1 = Floor(moved.Z + half - Epsilon);

        // The nearest blocking layer along the move: the lowest index when moving up, the highest when moving down.
        int? blocking = null;
        for (int y = y0; y <= y1; y++)
        {
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (!IsSolid(world, x, y, z))
                    {
                        continue;
                    }

                    int layer = axis switch { 0 => x, 1 => y, _ => z };
                    blocking = blocking is null ? layer : delta > 0 ? Math.Min(blocking.Value, layer) : Math.Max(blocking.Value, layer);
                }
            }
        }

        if (blocking is null)
        {
            position = moved;
            return false;
        }

        // Rest against the face of the blocking layer.
        float below = axis == 1 ? 0 : half;
        float above = axis == 1 ? Height : half;
        float resting = delta > 0 ? blocking.Value - above - Skin : blocking.Value + 1 + below + Skin;
        Set(ref position, axis, resting);
        return true;
    }

    /// <summary>True if the box at <paramref name="position"/> overlaps no solid cell.</summary>
    public bool Fits(VoxelWorld world, Vector3 position)
    {
        float half = Width / 2;
        for (int y = Floor(position.Y + Epsilon); y <= Floor(position.Y + Height - Epsilon); y++)
        {
            for (int z = Floor(position.Z - half + Epsilon); z <= Floor(position.Z + half - Epsilon); z++)
            {
                for (int x = Floor(position.X - half + Epsilon); x <= Floor(position.X + half - Epsilon); x++)
                {
                    if (IsSolid(world, x, y, z))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private bool IsSolid(VoxelWorld world, int x, int y, int z) => y < FloorY || world.Get(new BlockPos(x, y, z)) != Block.Air;

    private static int Floor(float value) => (int)MathF.Floor(value);

    private static float Get(Vector3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    private static void Set(ref Vector3 v, int axis, float value)
    {
        switch (axis)
        {
            case 0: v.X = value; break;
            case 1: v.Y = value; break;
            default: v.Z = value; break;
        }
    }
}
