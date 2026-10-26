namespace IslandWar.Sim.Voxels;

/// <summary>
/// Generates the two islands from a seed. Every client regenerates the terrain locally from the seed and
/// then applies the server's edit deltas, so generation must give identical blocks on every machine. It
/// therefore uses integer math only (fixed point, with 1024 meaning 1.0) and no floating point. The
/// west half is generated and copied column for column, mirrored, onto the east half, so neither team gets
/// better ground.
/// </summary>
public static class IslandGenerator
{
    private const int One = IslandMap.One;

    /// <summary>Of every thousand blocks in an ore clump, this many are ore; the rest stay stone.</summary>
    private const int OreInClump = 600;

    public static VoxelWorld Generate(IslandSettings settings, int seed)
    {
        settings.Validate();
        IslandMap map = IslandMap.Build(settings, seed);
        var world = new VoxelWorld(settings.ChunksX, settings.ChunksY, settings.ChunksZ);
        for (int z = 0; z < world.SizeZ; z++)
        {
            for (int x = 0; x < map.HalfX; x++)
            {
                FillColumn(world, settings, map, seed, x, z);
            }
        }

        PlantTrees(world, settings, map, seed);
        world.Compact();
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

    /// <summary>Fills west column (x, z) and its mirror on the east island.</summary>
    private static void FillColumn(VoxelWorld world, IslandSettings settings, IslandMap map, int seed, int x, int z)
    {
        int surface = map.Surface(x, z);
        Ground ground = map.GroundAt(x, z);
        bool rock = ground == Ground.Land && surface >= settings.SeaLevel + settings.RockLine;
        int oreChance = settings.OreChanceElsewhere
                        + (settings.OreChanceMountains - settings.OreChanceElsewhere) * map.Mountains(x, z) / One;
        int mirror = map.Mirror(x);

        for (int y = 0; y <= surface; y++)
        {
            Block block;
            if (ground != Ground.Land || rock)
            {
                // Sea floor and beaches are sand over stone; high mountain tops are bare stone.
                block = !rock && y > surface - settings.DirtDepth ? Block.Sand : Block.Stone;
            }
            else if (y == surface)
            {
                block = Block.Grass;
            }
            else
            {
                block = y > surface - settings.DirtDepth - 1 ? Block.Dirt : Block.Stone;
            }

            // Ore only under the islands, and never in the unbreakable bottom layer.
            if (block == Block.Stone && ground != Ground.Sea && y > VoxelWorld.UnbreakableLayer && IsOre(settings, seed, oreChance, x, y, z))
            {
                block = Block.Ore;
            }

            world.SetGenerated(new BlockPos(x, y, z), block);
            if (mirror != x)
            {
                world.SetGenerated(new BlockPos(mirror, y, z), block);
            }
        }
    }

    /// <summary>Stone is split into clumps; a clump holds ore by chance, and then most of its blocks are ore.</summary>
    private static bool IsOre(IslandSettings settings, int seed, int clumpChance, int x, int y, int z)
    {
        int size = settings.OreClumpSize;
        return Noise.Hash(x / size, y / size, z / size, seed + 20) % 1000 < clumpChance
               && Noise.Hash(x, y, z, seed + 21) % 1000 < OreInClump;
    }

    /// <summary>
    /// At most one tree per tree cell, on grass, by the chance the map gives (high in forests). The trunk stands
    /// at least two blocks in from the cell's edges, so leaves never reach a neighbouring cell's tree.
    /// </summary>
    private static void PlantTrees(VoxelWorld world, IslandSettings settings, IslandMap map, int seed)
    {
        int cell = settings.TreeCellSize;
        (int spawnX, int spawnZ) = settings.IslandCentre(0);
        long clearing = (long)settings.SpawnClearing * settings.SpawnClearing;
        for (int cellZ = 0; (cellZ + 1) * cell <= world.SizeZ; cellZ++)
        {
            for (int cellX = 0; (cellX + 1) * cell <= map.HalfX; cellX++)
            {
                int pick = Noise.Hash(cellX, 0, cellZ, seed + 30);
                int x = cellX * cell + 2 + pick % (cell - 4);
                int z = cellZ * cell + 2 + pick / 7 % (cell - 4);
                if (map.GroundAt(x, z) != Ground.Land
                    || pick / 49 % 1000 >= map.TreeChance(x, z)
                    || (long)(x - spawnX) * (x - spawnX) + (long)(z - spawnZ) * (z - spawnZ) < clearing)
                {
                    continue;
                }

                int ground = map.Surface(x, z);
                if (world.Get(new BlockPos(x, ground, z)) != Block.Grass)
                {
                    continue;
                }

                int trunk = 4 + pick / 49000 % 3;
                PlantTree(world, x, ground, z, trunk);
                PlantTree(world, map.Mirror(x), ground, z, trunk);
            }
        }
    }

    /// <summary>A trunk with two wide layers of leaves round its top and a small cap above.</summary>
    private static void PlantTree(VoxelWorld world, int x, int ground, int z, int trunk)
    {
        int top = ground + trunk;
        for (int y = ground + 1; y <= top; y++)
        {
            world.SetGenerated(new BlockPos(x, y, z), Block.Wood);
        }

        for (int y = top - 1; y <= top + 1; y++)
        {
            int radius = y <= top ? 2 : 1;
            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    // Corners cut off, so the crown looks round.
                    if (Math.Abs(dx) == radius && Math.Abs(dz) == radius)
                    {
                        continue;
                    }

                    var at = new BlockPos(x + dx, y, z + dz);
                    if (world.Get(at) == Block.Air)
                    {
                        world.SetGenerated(at, Block.Leaves);
                    }
                }
            }
        }
    }
}
