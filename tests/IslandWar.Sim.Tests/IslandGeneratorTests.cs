using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Tests;

public class IslandGeneratorTests
{
    private static readonly IslandSettings Settings =
        IslandSettings.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "terrain.json")));

    [Fact]
    public void TerrainDataFileLoads()
    {
        Assert.True(Settings.ChunksX > 0);
        Assert.True(Settings.IslandRadius > 0);
    }

    [Fact]
    public void SameSeedGivesSameWorld()
    {
        Assert.Equal(
            IslandGenerator.Generate(Settings, 42).ComputeHash(),
            IslandGenerator.Generate(Settings, 42).ComputeHash());
    }

    [Fact]
    public void DifferentSeedsGiveDifferentWorlds()
    {
        Assert.NotEqual(
            IslandGenerator.Generate(Settings, 1).ComputeHash(),
            IslandGenerator.Generate(Settings, 2).ComputeHash());
    }

    [Fact]
    public void EachIslandRisesAboveSeaInItsMiddleAndNotAtTheEdges()
    {
        VoxelWorld world = IslandGenerator.Generate(Settings, 7);
        for (int island = 0; island < Settings.IslandCount; island++)
        {
            (int x, int z) = Settings.IslandCentre(world.SizeX, world.SizeZ, island);
            int middle = IslandGenerator.SurfaceHeight(Settings, world, 7, x, z);

            Assert.True(middle > Settings.SeaLevel, $"island {island} surface {middle} should be above sea level {Settings.SeaLevel}");
            Assert.Equal(Block.Grass, world.Get(new BlockPos(x, middle, z)));
            Assert.Equal(Block.Air, world.Get(new BlockPos(x, middle + 1, z)));
        }

        Assert.Equal(Settings.SeaLevel - Settings.SeabedDepth, IslandGenerator.SurfaceHeight(Settings, world, 7, 0, 0));
    }

    [Fact]
    public void TheDataFileMakesTwoIslandsWithOpenWaterBetween()
    {
        VoxelWorld world = IslandGenerator.Generate(Settings, 7);

        Assert.Equal(2, Settings.IslandCount);
        Assert.Equal(Settings.SeaLevel - Settings.SeabedDepth, IslandGenerator.SurfaceHeight(Settings, world, 7, world.SizeX / 2, world.SizeZ / 2));
    }

    [Fact]
    public void TheEastIslandMirrorsTheWestOne()
    {
        VoxelWorld world = IslandGenerator.Generate(Settings, 42);

        for (int z = 0; z < world.SizeZ; z += 7)
        {
            for (int x = 0; x < world.SizeX / 2; x += 5)
            {
                Assert.Equal(world.HighestSolid(x, z), world.HighestSolid(world.SizeX - 1 - x, z));
            }
        }
    }

    [Fact]
    public void ZeroSpacingStillMakesOneCentredIsland()
    {
        IslandSettings single = Settings with { ChunksX = 8, ChunksZ = 8, IslandSpacing = 0 };
        VoxelWorld world = IslandGenerator.Generate(single, 7);

        Assert.Equal(1, single.IslandCount);
        Assert.True(IslandGenerator.SurfaceHeight(single, world, 7, 64, 64) > single.SeaLevel);
    }

    [Fact]
    public void LateJoinerRebuildsTheSameWorldFromSeedAndDeltas()
    {
        const int seed = 99;
        VoxelWorld server = IslandGenerator.Generate(Settings, seed);
        int surface = IslandGenerator.SurfaceHeight(Settings, server, seed, 64, 64);

        // Someone builds a small tower, then a bomb takes a chunk out of it and the ground.
        for (int y = 1; y <= 8; y++)
        {
            server.ApplyEdit(new VoxelEdit(new BlockPos(64, surface + y, 64), Block.Planks));
        }

        Craters.Blast(server, new BlockPos(64, surface + 2, 64), 3);

        VoxelWorld lateJoiner = IslandGenerator.Rebuild(Settings, seed, server.Deltas.All());

        Assert.Equal(server.ComputeHash(), lateJoiner.ComputeHash());
    }

    [Theory]
    [InlineData("""{ "chunks_y": 1, "sea_level": 10, "base_height": 4, "hill_height": 4 }""")]
    [InlineData("""{ "seabed_depth": 0 }""")]
    [InlineData("""{ "island_radius": 0 }""")]
    [InlineData("""{ "island_radius": 48, "island_spacing": 60 }""")]
    [InlineData("""{ "chunks_x": 8, "island_radius": 48, "island_spacing": 176 }""")]
    public void InvalidSettingsAreRejected(string json)
    {
        Assert.Throws<InvalidDataException>(() => IslandSettings.FromJson(json));
    }
}
