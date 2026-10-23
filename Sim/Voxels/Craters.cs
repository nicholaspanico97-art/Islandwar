namespace IslandWar.Sim.Voxels;

/// <summary>Block removal from explosions. The server runs this and replicates the returned edits.</summary>
public static class Craters
{
    /// <summary>Removes every solid block within <paramref name="radius"/> of <paramref name="center"/>.</summary>
    /// <returns>The edits that were applied, in a fixed order (Y, then Z, then X).</returns>
    public static IReadOnlyList<VoxelEdit> Blast(VoxelWorld world, BlockPos center, int radius)
    {
        var edits = new List<VoxelEdit>();
        if (radius < 0)
        {
            return edits;
        }

        int radiusSquared = radius * radius;
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy + dz * dz > radiusSquared)
                    {
                        continue;
                    }

                    var edit = new VoxelEdit(new BlockPos(center.X + dx, center.Y + dy, center.Z + dz), Block.Air);
                    if (world.ApplyEdit(edit))
                    {
                        edits.Add(edit);
                    }
                }
            }
        }

        return edits;
    }
}
