namespace IslandWar.Sim.Voxels;

/// <summary>
/// Generates one island from a seed. Every client regenerates the terrain locally from the seed
/// and then applies the server's edit deltas, so generation must give identical blocks on every
/// machine. It therefore uses integer math only (fixed point, with 1024 meaning 1.0) and no
/// floating point.
/// </summary>
public static class IslandGenerator
{
    private const int One = 1024;

    public static VoxelWorld Generate(IslandSettings settings, int seed)
    {
        settings.Validate();
        var world = new VoxelWorld(settings.ChunksX, settings.ChunksY, settings.ChunksZ);
        for (int z = 0; z < world.SizeZ; z++)
        {
            for (int x = 0; x < world.SizeX; x++)
            {
                FillColumn(world, settings, x, z, SurfaceHeight(settings, world, seed, x, z));
            }
        }

        return world;
    }

    /// <summary>Rebuilds the current world for a late joiner: terrain from the seed, then the deltas.</summary>
    public static VoxelWorld Rebuild(IslandSettings settings, int seed, IEnumerable<VoxelEdit> deltas)
    {
        VoxelWorld world = Generate(settings, seed);
        foreach (VoxelEdit edit in deltas)
        {
            world.ApplyEdit(edit);
        }

        return world;
    }

    /// <summary>Height of the top solid block of the natural terrain in a column (before any edits).</summary>
    public static int SurfaceHeight(IslandSettings settings, VoxelWorld world, int seed, int x, int z) =>
        SurfaceHeight(settings, world.SizeX, world.SizeZ, seed, x, z);

    private static int SurfaceHeight(IslandSettings settings, int sizeX, int sizeZ, int seed, int x, int z)
    {
        int seabed = settings.SeaLevel - settings.SeabedDepth;

        // With two islands, the east one is the west one mirrored, so neither team gets better ground.
        if (settings.IslandSpacing > 0 && x >= sizeX / 2)
        {
            x = sizeX - 1 - x;
        }

        (int centreX, int centreZ) = settings.IslandCentre(sizeX, sizeZ, 0);
        long dx = x - centreX;
        long dz = z - centreZ;
        long radius = settings.IslandRadius;
        long falloff = (dx * dx + dz * dz) * One / (radius * radius);
        if (falloff >= One)
        {
            return seabed;
        }

        // Two octaves of value noise, weighted 2:1 toward the broad one.
        int noise = (2 * ValueNoise(x, z, settings.NoiseCellSize, seed)
                     + ValueNoise(x, z, Math.Max(1, settings.NoiseCellSize / 2), seed + 1)) / 3;

        // Peak height above the seabed, faded to zero at the island edge.
        long peak = settings.SeabedDepth + settings.BaseHeight + (long)settings.HillHeight * noise / One;
        long fade = One - falloff;
        return seabed + (int)(peak * fade / One);
    }

    private static void FillColumn(VoxelWorld world, IslandSettings settings, int x, int z, int surface)
    {
        bool beach = surface <= settings.SeaLevel + settings.BeachHeight;
        for (int y = 0; y <= surface; y++)
        {
            Block block;
            if (beach)
            {
                block = y > surface - settings.DirtDepth ? Block.Sand : Block.Stone;
            }
            else if (y == surface)
            {
                block = Block.Grass;
            }
            else
            {
                block = y > surface - settings.DirtDepth - 1 ? Block.Dirt : Block.Stone;
            }

            world.SetGenerated(new BlockPos(x, y, z), block);
        }
    }

    /// <summary>Smoothly interpolated random values on a square lattice. Returns 0 to 1023.</summary>
    private static int ValueNoise(int x, int z, int cellSize, int seed)
    {
        int cellX = FloorDiv(x, cellSize);
        int cellZ = FloorDiv(z, cellSize);
        int tx = SmoothStep((x - cellX * cellSize) * One / cellSize);
        int tz = SmoothStep((z - cellZ * cellSize) * One / cellSize);

        int a = LatticeValue(cellX, cellZ, seed);
        int b = LatticeValue(cellX + 1, cellZ, seed);
        int c = LatticeValue(cellX, cellZ + 1, seed);
        int d = LatticeValue(cellX + 1, cellZ + 1, seed);

        int top = a + (b - a) * tx / One;
        int bottom = c + (d - c) * tx / One;
        return top + (bottom - top) * tz / One;
    }

    /// <summary>3t² - 2t³ in fixed point.</summary>
    private static int SmoothStep(int t) => (int)((long)t * t * (3 * One - 2 * t) / ((long)One * One));

    /// <summary>A hash of a lattice point, reduced to 0 to 1023.</summary>
    private static int LatticeValue(int x, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)seed * 0x9E3779B1u;
            h ^= (uint)x * 0x85EBCA77u;
            h = (h ^ (h >> 15)) * 0xC2B2AE3Du;
            h ^= (uint)z * 0x27D4EB2Fu;
            h = (h ^ (h >> 13)) * 0x165667B1u;
            h ^= h >> 16;
            return (int)(h & (One - 1));
        }
    }

    private static int FloorDiv(int a, int b) => (a >= 0 ? a : a - b + 1) / b;
}
