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
    public void IslandRisesAboveSeaInTheMiddleAndNotAtTheEdges()
    {
        VoxelWorld world = IslandGenerator.Generate(Settings, 7);
        int middle = IslandGenerator.SurfaceHeight(Settings, world, 7, world.SizeX / 2, world.SizeZ / 2);
        int corner = IslandGenerator.SurfaceHeight(Settings, world, 7, 0, 0);

        Assert.True(middle > Settings.SeaLevel, $"middle surface {middle} should be above sea level {Settings.SeaLevel}");
        Assert.Equal(Settings.SeaLevel - Settings.SeabedDepth, corner);
        Assert.Equal(Block.Grass, world.Get(new BlockPos(world.SizeX / 2, middle, world.SizeZ / 2)));
        Assert.Equal(Block.Air, world.Get(new BlockPos(world.SizeX / 2, middle + 1, world.SizeZ / 2)));
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
    public void InvalidSettingsAreRejected(string json)
    {
        Assert.Throws<InvalidDataException>(() => IslandSettings.FromJson(json));
    }
}
