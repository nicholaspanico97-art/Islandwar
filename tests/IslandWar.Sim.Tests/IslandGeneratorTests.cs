using System.Diagnostics;
using IslandWar.Sim.Voxels;
using Xunit.Abstractions;

namespace IslandWar.Sim.Tests;

public class IslandGeneratorTests
{
    private const int Seed = 1337;

    private static readonly IslandSettings Settings =
        IslandSettings.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "terrain.json")));

    /// <summary>The real layout shrunk to a fifth, so the tests that generate many worlds stay quick.</summary>
    private static readonly IslandSettings Small = Settings with
    {
        ChunksX = 20, ChunksY = 8, ChunksZ = 10,
        IslandLength = 120, IslandWidth = 60, StraitWidth = 40,
        CoastNoise = 6, CoastSlope = 8, BeachWidth = 4, InlandSlope = 8, SpawnClearing = 4,
    };

    /// <summary>The full-size world from the data file, generated once for the tests that look at it.</summary>
    private static readonly Lazy<(VoxelWorld World, IslandMap Map, long Milliseconds)> Real = new(() =>
    {
        var clock = Stopwatch.StartNew();
        VoxelWorld world = IslandGenerator.Generate(Settings, Seed);
        long milliseconds = clock.ElapsedMilliseconds;
        return (world, IslandMap.Build(Settings, Seed), milliseconds);
    });

    private readonly ITestOutputHelper _output;

    public IslandGeneratorTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void TerrainDataFileLoads()
    {
        Assert.Equal(2, Settings.IslandCount);
        Assert.True(Settings.Outline.Count >= 2);
        Assert.Contains(Settings.Regions, r => r.Kind == "mountains");
        Assert.Contains(Settings.Regions, r => r.Kind == "hills");
        Assert.Contains(Settings.Regions, r => r.Kind == "forest");
        Small.Validate();
    }

    [Fact]
    public void SameSeedGivesSameWorld()
    {
        Assert.Equal(
            IslandGenerator.Generate(Small, 42).ComputeHash(),
            IslandGenerator.Generate(Small, 42).ComputeHash());
    }

    [Fact]
    public void DifferentSeedsGiveDifferentWorlds()
    {
        Assert.NotEqual(
            IslandGenerator.Generate(Small, 1).ComputeHash(),
            IslandGenerator.Generate(Small, 2).ComputeHash());
    }

    [Fact]
    public void TheFullMapGeneratesInReasonableTime()
    {
        _output.WriteLine($"Generated {Settings.SizeX} x {Settings.SizeY} x {Settings.SizeZ} in {Real.Value.Milliseconds} ms");
        Assert.True(Real.Value.Milliseconds < 10_000, $"generation took {Real.Value.Milliseconds} ms");
    }

    [Fact]
    public void EachIslandHasClearGrassAboveTheSeaAtItsCentre()
    {
        (VoxelWorld world, IslandMap map, _) = Real.Value;
        for (int island = 0; island < Settings.IslandCount; island++)
        {
            (int x, int z) = Settings.IslandCentre(island);
            int middle = map.Surface(x, z);

            Assert.True(middle > Settings.SeaLevel, $"island {island} surface {middle} should be above sea level {Settings.SeaLevel}");
            Assert.Equal(Block.Grass, world.Get(new BlockPos(x, middle, z)));
            Assert.Equal(middle, world.HighestSolid(x, z));
        }
    }

    [Fact]
    public void ThereIsOpenWaterBetweenTheIslandsAndRoundTheEdges()
    {
        IslandMap map = Real.Value.Map;
        int centreZ = Settings.SizeZ / 2;

        Assert.Equal(Ground.Sea, map.GroundAt(Settings.SizeX / 2, centreZ));
        Assert.Equal(Ground.Sea, map.GroundAt(0, centreZ));
        Assert.Equal(Ground.Sea, map.GroundAt(Settings.SizeX - 1, centreZ));
        for (int x = 0; x < Settings.SizeX; x += 4)
        {
            Assert.Equal(Ground.Sea, map.GroundAt(x, 0));
            Assert.Equal(Ground.Sea, map.GroundAt(x, Settings.SizeZ - 1));
        }
    }

    [Fact]
    public void AnIslandTakesAboutTwoMinutesToWalkAcross()
    {
        IslandMap map = Real.Value.Map;
        (_, int z) = Settings.IslandCentre(0);
        int[] land = Enumerable.Range(0, map.HalfX).Where(x => map.GroundAt(x, z) != Ground.Sea).ToArray();
        int length = land.Max() - land.Min() + 1;
        const float walkSpeed = 5;

        _output.WriteLine($"West island is {length} blocks long through its centre: {length / walkSpeed:0} s at walking speed");
        Assert.InRange(length / walkSpeed, 100, 140);
    }

    [Fact]
    public void TheEastIslandMirrorsTheWestOneBlockForBlock()
    {
        VoxelWorld world = IslandGenerator.Generate(Small, 42);
        for (int y = 0; y < world.SizeY; y++)
        {
            for (int z = 0; z < world.SizeZ; z++)
            {
                for (int x = 0; x < world.SizeX / 2; x++)
                {
                    if (world.Get(new BlockPos(x, y, z)) != world.Get(new BlockPos(world.SizeX - 1 - x, y, z)))
                    {
                        Assert.Fail($"({x}, {y}, {z}) differs from its mirror");
                    }
                }
            }
        }
    }

    [Fact]
    public void TheMountainsAreHighAndFullOfOreAndTheForestsAreFullOfTrees()
    {
        (VoxelWorld world, IslandMap map, _) = Real.Value;
        int peak = 0;
        long mountainOre = 0, mountainStone = 0, plainOre = 0, plainStone = 0;
        int forestTrees = 0, forestColumns = 0, plainTrees = 0, plainColumns = 0;
        int forestChance = Settings.Regions.Where(r => r.Kind == "forest").Max(r => r.TreeChance);

        for (int z = 0; z < world.SizeZ; z++)
        {
            for (int x = 0; x < map.HalfX; x++)
            {
                if (map.GroundAt(x, z) != Ground.Land)
                {
                    continue;
                }

                bool mountain = map.Mountains(x, z) > IslandMap.One * 3 / 4;
                bool plain = map.Mountains(x, z) == 0;
                peak = Math.Max(peak, map.Surface(x, z));
                for (int y = 1; y <= map.Surface(x, z); y++)
                {
                    Block block = world.Get(new BlockPos(x, y, z));
                    bool rock = block is Block.Stone or Block.Ore;
                    mountainStone += mountain && rock ? 1 : 0;
                    mountainOre += mountain && block == Block.Ore ? 1 : 0;
                    plainStone += plain && rock ? 1 : 0;
                    plainOre += plain && block == Block.Ore ? 1 : 0;
                }

                bool trunk = world.Get(new BlockPos(x, map.Surface(x, z) + 1, z)) == Block.Wood;
                if (map.TreeChance(x, z) > forestChance / 2)
                {
                    forestColumns++;
                    forestTrees += trunk ? 1 : 0;
                }
                else if (map.TreeChance(x, z) == Settings.GrasslandTreeChance)
                {
                    plainColumns++;
                    plainTrees += trunk ? 1 : 0;
                }
            }
        }

        double mountainShare = (double)mountainOre / mountainStone;
        double plainShare = (double)plainOre / plainStone;
        double forestDensity = (double)forestTrees / forestColumns;
        double plainDensity = (double)plainTrees / plainColumns;
        _output.WriteLine($"peak {peak}; ore {mountainShare:P2} of mountain stone, {plainShare:P2} elsewhere; trees per column {forestDensity:F4} in forest, {plainDensity:F4} elsewhere");

        Assert.True(peak >= Settings.SeaLevel + 40, $"the mountains peak at {peak}");
        Assert.True(plainOre > 0, "some ore away from the mountains");
        Assert.True(mountainShare > 4 * plainShare, "ore much denser in the mountains");
        Assert.True(plainTrees > 0, "a few trees on the grassland");
        Assert.True(forestDensity > 10 * plainDensity, "trees much denser in the forests");
    }

    [Fact]
    public void LateJoinerRebuildsTheSameWorldFromSeedAndDeltas()
    {
        const int seed = 99;
        VoxelWorld server = IslandGenerator.Generate(Small, seed);
        (int x, int z) = Small.IslandCentre(0);
        int surface = IslandMap.Build(Small, seed).Surface(x, z);

        // Someone builds a small tower, then a bomb takes a chunk out of it and the ground.
        for (int y = 1; y <= 8; y++)
        {
            server.ApplyEdit(new VoxelEdit(new BlockPos(x, surface + y, z), Block.Planks));
        }

        Craters.Blast(server, new BlockPos(x, surface + 2, z), 3);

        VoxelWorld lateJoiner = IslandGenerator.Rebuild(Small, seed, server.Deltas.All());

        Assert.Equal(server.ComputeHash(), lateJoiner.ComputeHash());
    }

    /// <summary>The valid settings from the data file with one thing broken in each.</summary>
    private static readonly Dictionary<string, IslandSettings> Broken = new()
    {
        ["too low for the mountains"] = Settings with { ChunksY = 4 },
        ["no sea floor"] = Settings with { SeabedDepth = 0 },
        ["no island"] = Settings with { IslandLength = 0 },
        ["trees too close"] = Settings with { TreeCellSize = 5 },
        ["map too narrow"] = Settings with { ChunksX = 60 },
        ["one outline point"] = Settings with { Outline = new[] { new CoastPoint { U = 0, North = 0.2f, South = 0.8f } } },
        ["outline inside out"] = Settings with
        {
            Outline = new[] { new CoastPoint { U = 0, North = 0.8f, South = 0.2f }, new CoastPoint { U = 1, North = 0.2f, South = 0.8f } },
        },
        ["unknown region"] = Settings with { Regions = new[] { new IslandRegion { Kind = "volcano", U = 0.5f, V = 0.5f, RadiusU = 0.1f, RadiusV = 0.1f } } },
    };

    public static TheoryData<string> BrokenNames => new(Broken.Keys);

    [Theory]
    [MemberData(nameof(BrokenNames))]
    public void InvalidSettingsAreRejected(string name)
    {
        Assert.Throws<InvalidDataException>(Broken[name].Validate);
    }

    [Fact]
    public void BadDataFileJsonIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => IslandSettings.FromJson("""{ "seabed_depth": 0 }"""));
    }
}
