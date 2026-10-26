using System.Numerics;
using IslandWar.Sim.Match;
using IslandWar.Sim.Players;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Tests;

public class CoreTests
{
    private static readonly IslandSettings Settings =
        IslandSettings.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "terrain.json")));

    private const int Seed = 7;

    [Fact]
    public void EachIslandGetsAFullCoreOnALevelFoundation()
    {
        VoxelWorld world = IslandGenerator.Generate(Settings, Seed);
        IReadOnlyList<CoreSite> cores = IslandGenerator.CoreSites(Settings, Seed);

        Assert.Equal(2, cores.Count);
        foreach (CoreSite core in cores)
        {
            Assert.Equal(core.TotalBlocks, core.StandingBlocks(world));
            for (int z = core.MinZ; z < core.MinZ + core.Size; z++)
            {
                for (int x = core.MinX; x < core.MinX + core.Size; x++)
                {
                    // Solid all the way down from the core: no gaps under it.
                    Assert.NotEqual(Block.Air, world.Get(new BlockPos(x, core.BaseY - 1, z)));
                }
            }
        }
    }

    [Fact]
    public void TheCoresMirrorEachOther()
    {
        IReadOnlyList<CoreSite> cores = IslandGenerator.CoreSites(Settings, Seed);
        int sizeX = Settings.ChunksX * Chunk.Size;

        Assert.Equal(cores[0].BaseY, cores[1].BaseY);
        Assert.Equal(sizeX - (cores[0].MinX + cores[0].Size), cores[1].MinX);
        Assert.Equal(cores[0].MinZ, cores[1].MinZ);
    }

    [Fact]
    public void PlayersCantBreakOrPlaceCoreBlocks()
    {
        VoxelWorld world = IslandGenerator.Generate(Settings, Seed);
        CoreSite core = IslandGenerator.CoreSites(Settings, Seed)[0];
        var top = new BlockPos(core.MinX, core.BaseY + core.Height - 1, core.MinZ);

        Assert.False(BuildRules.CanBreak(world, top));
        Assert.False(BuildRules.Placeable(Block.Core));
        Assert.False(BuildRules.CanPlace(world, new PlayerSettings(), top with { Y = top.Y + 1 }, Block.Core, Array.Empty<Vector3>()));
    }

    [Fact]
    public void BombsOnlyChipTheHardenedCore()
    {
        VoxelWorld world = IslandGenerator.Generate(Settings, Seed);
        CoreSite core = IslandGenerator.CoreSites(Settings, Seed)[0];
        var centreTop = new BlockPos(core.MinX + core.Size / 2, core.BaseY + core.Height - 1, core.MinZ + core.Size / 2);

        Craters.Blast(world, centreTop, 5, coreFactor: 0.5f);
        int afterBomb = core.StandingBlocks(world);

        Assert.True(afterBomb < core.TotalBlocks, "a direct hit should take some core blocks");
        Assert.True(afterBomb > core.TotalBlocks / 2, $"a hardened core keeps most of itself after one bomb, kept {afterBomb}/{core.TotalBlocks}");
    }

    [Fact]
    public void ANukeOnTheCoreFlattensIt()
    {
        VoxelWorld world = IslandGenerator.Generate(Settings, Seed);
        CoreSite core = IslandGenerator.CoreSites(Settings, Seed)[0];
        var centreTop = new BlockPos(core.MinX + core.Size / 2, core.BaseY + core.Height - 1, core.MinZ + core.Size / 2);

        Craters.Blast(world, centreTop, 9);

        Assert.True(core.StandingBlocks(world) < core.TotalBlocks / 10);
    }

    [Fact]
    public void NukeHitsCountNearTheFootprintOnly()
    {
        var core = new CoreSite(0, 100, 30, 50, 7, 5);

        Assert.True(core.IsHitBy(new Vector3(103.5f, 35, 53.5f), margin: 3));
        Assert.True(core.IsHitBy(new Vector3(98, 20, 53), margin: 3));
        Assert.False(core.IsHitBy(new Vector3(115, 30, 53), margin: 3));
        Assert.False(core.IsHitBy(new Vector3(103, 60, 53), margin: 3));
    }

    [Fact]
    public void TheNukeArrivesWithPhaseThreeAndThenOnACooldown()
    {
        var preset = new MatchPreset { Phase2StartMinutes = 1, Phase3StartMinutes = 2, TimeLimitMinutes = 10, NukeCooldownMinutes = 3 };
        MatchStatus At(int phase, float seconds) => new(MatchStage.Playing, phase, seconds, Team.None, EndReason.None);

        Assert.True(MatchFlow.NukeReadyIn(MatchStatus.Warmup, preset, null) < 0);
        Assert.True(MatchFlow.NukeReadyIn(At(2, 100), preset, null) < 0);
        Assert.Equal(0, MatchFlow.NukeReadyIn(At(3, 130), preset, null));
        Assert.Equal(170, MatchFlow.NukeReadyIn(At(3, 140), preset, lastDropSeconds: 130));
        Assert.Equal(0, MatchFlow.NukeReadyIn(At(3, 320), preset, lastDropSeconds: 130));
    }
}
