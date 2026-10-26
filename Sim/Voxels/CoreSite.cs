using System.Numerics;

namespace IslandWar.Sim.Voxels;

/// <summary>
/// Where an island's industrial core stands: a box of <see cref="Block.Core"/> blocks on a levelled
/// foundation at the island's centre. Generated with the terrain, so every client knows it from the seed.
/// </summary>
/// <param name="Island">The island index (see IslandSettings.IslandCentre and Team.IslandIndex).</param>
public readonly record struct CoreSite(int Island, int MinX, int BaseY, int MinZ, int Size, int Height)
{
    public int TotalBlocks => Size * Size * Height;

    public Vector3 Centre => new(MinX + Size / 2f, BaseY + Height / 2f, MinZ + Size / 2f);

    public bool Contains(BlockPos p) =>
        p.X >= MinX && p.X < MinX + Size && p.Y >= BaseY && p.Y < BaseY + Height && p.Z >= MinZ && p.Z < MinZ + Size;

    /// <summary>Core blocks still standing: the core's health.</summary>
    public int StandingBlocks(VoxelWorld world)
    {
        int count = 0;
        for (int y = BaseY; y < BaseY + Height; y++)
        {
            for (int z = MinZ; z < MinZ + Size; z++)
            {
                for (int x = MinX; x < MinX + Size; x++)
                {
                    if (world.Get(new BlockPos(x, y, z)) == Block.Core)
                    {
                        count++;
                    }
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Whether an impact counts as hitting the core: over the core's footprint, widened by
    /// <paramref name="margin"/> blocks, and no higher than that above its top. A core blasted down to
    /// nothing can still be hit, because the site is what counts.
    /// </summary>
    public bool IsHitBy(Vector3 impact, float margin) =>
        impact.X >= MinX - margin && impact.X <= MinX + Size + margin
        && impact.Z >= MinZ - margin && impact.Z <= MinZ + Size + margin
        && impact.Y <= BaseY + Height + margin;
}
