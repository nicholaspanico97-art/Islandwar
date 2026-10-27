using IslandWar.Sim.Match;

namespace IslandWar.Sim.Tests;

public class MatchFlowTests
{
    private static readonly MatchPreset Preset = new()
    {
        MinPlayersPerTeam = 1,
        CountdownSeconds = 10,
        Phase2StartMinutes = 1,
        Phase3StartMinutes = 2,
        TimeLimitMinutes = 3,
        ResultsSeconds = 5,
        CraterRadiusByPhase = new[] { 0, 3, 5 },
        // Only creative presets keep the timed phases.
        Creative = true,
    };

    /// <summary>Steps the match in one-second ticks, collecting the events.</summary>
    private static (MatchStatus Status, List<MatchEvent> Events) Run(MatchStatus status, float seconds, int red, int blue)
    {
        var events = new List<MatchEvent>();
        for (int i = 0; i < seconds; i++)
        {
            (status, MatchEvent e) = MatchFlow.Advance(status, Preset, 1, red, blue);
            if (e != MatchEvent.None)
            {
                events.Add(e);
            }
        }

        return (status, events);
    }

    [Fact]
    public void WarmupWaitsForBothTeams()
    {
        (MatchStatus status, List<MatchEvent> events) = Run(MatchStatus.Warmup, 60, red: 3, blue: 0);

        Assert.Equal(MatchStage.Warmup, status.Stage);
        Assert.Empty(events);
    }

    [Fact]
    public void AFullMatchRunsThroughEveryStageAndBack()
    {
        (MatchStatus status, List<MatchEvent> events) = Run(MatchStatus.Warmup, 1 + 10 + 180 + 5, red: 1, blue: 1);

        Assert.Equal(new[]
        {
            MatchEvent.CountdownStarted, MatchEvent.MatchStarted, MatchEvent.PhaseChanged, MatchEvent.PhaseChanged,
            MatchEvent.MatchEnded, MatchEvent.BackToWarmup,
        }, events);
        Assert.Equal(MatchStage.Warmup, status.Stage);
    }

    [Fact]
    public void PhasesFollowThePresetTimes()
    {
        var playing = new MatchStatus(MatchStage.Playing, 1, 0, Team.None, EndReason.None);

        Assert.Equal(1, Run(playing, 59, 1, 1).Status.Phase);
        Assert.Equal(2, Run(playing, 61, 1, 1).Status.Phase);
        Assert.Equal(3, Run(playing, 121, 1, 1).Status.Phase);
    }

    [Fact]
    public void LosingAPlayerCancelsTheCountdown()
    {
        (MatchStatus counting, _) = Run(MatchStatus.Warmup, 3, 1, 1);

        (MatchStatus status, MatchEvent e) = MatchFlow.Advance(counting, Preset, 1, red: 1, blue: 0);

        Assert.Equal(MatchStage.Countdown, counting.Stage);
        Assert.Equal(MatchStage.Warmup, status.Stage);
        Assert.Equal(MatchEvent.CountdownCancelled, e);
    }

    [Fact]
    public void TheTimeLimitEndsTheMatch()
    {
        var late = new MatchStatus(MatchStage.Playing, 3, 179.5f, Team.None, EndReason.None);

        (MatchStatus status, MatchEvent e) = MatchFlow.Advance(late, Preset, 1, 1, 1);

        Assert.Equal(MatchEvent.MatchEnded, e);
        Assert.Equal(MatchStage.Ended, status.Stage);
        Assert.Equal(EndReason.TimeLimit, status.Reason);
    }

    [Fact]
    public void AMatchCanBeEndedEarlyWithAWinner()
    {
        var playing = new MatchStatus(MatchStage.Playing, 2, 80, Team.None, EndReason.None);

        MatchStatus ended = MatchFlow.End(playing, Team.Blue, EndReason.NukeHit);

        Assert.Equal(MatchStage.Ended, ended.Stage);
        Assert.Equal(Team.Blue, ended.Winner);
        Assert.Equal(0, ended.StageSeconds);
    }

    [Fact]
    public void BombsAreLockedInPhaseOneAndGrowLater()
    {
        Assert.Equal(4, MatchFlow.CraterRadius(MatchStatus.Warmup, Preset, warmupRadius: 4));
        Assert.Equal(0, MatchFlow.CraterRadius(new MatchStatus(MatchStage.Playing, 1, 10, Team.None, EndReason.None), Preset, 4));
        Assert.Equal(3, MatchFlow.CraterRadius(new MatchStatus(MatchStage.Playing, 2, 70, Team.None, EndReason.None), Preset, 4));
        Assert.Equal(5, MatchFlow.CraterRadius(new MatchStatus(MatchStage.Playing, 3, 130, Team.None, EndReason.None), Preset, 4));
        Assert.Equal(0, MatchFlow.CraterRadius(new MatchStatus(MatchStage.Ended, 3, 1, Team.Red, EndReason.TimeLimit), Preset, 4));
    }

    [Fact]
    public void SecondsToNextCountsDownToTheNextPhase()
    {
        Assert.Null(MatchFlow.SecondsToNext(MatchStatus.Warmup, Preset));
        Assert.Equal(50, MatchFlow.SecondsToNext(new MatchStatus(MatchStage.Playing, 1, 10, Team.None, EndReason.None), Preset));
        Assert.Equal(30, MatchFlow.SecondsToNext(new MatchStatus(MatchStage.Playing, 3, 150, Team.None, EndReason.None), Preset));
    }

    [Theory]
    [InlineData(Team.Red, 250, false, true)]
    [InlineData(Team.Red, 100, false, false)]
    [InlineData(Team.Red, 250, true, false)]
    [InlineData(Team.Blue, 20, false, true)]
    public void ANukeWinsAnywhereOnTheEnemyIslandButNotInTheSea(Team dropper, float x, bool inWater, bool wins)
    {
        var impact = new IslandWar.Sim.Weapons.BombImpact(new System.Numerics.Vector3(x, 20, 80), inWater ? null : new IslandWar.Sim.Voxels.BlockPos((int)x, 19, 80));

        Assert.Equal(wins, MatchFlow.NukeWins(dropper, impact, mapSizeX: 320, islandCount: 2));
    }

    [Theory]
    [InlineData("""{ "bad": { "creative": true, "phase2_start_minutes": 10, "phase3_start_minutes": 5 } }""")]
    [InlineData("""{ "bad": { "time_limit_minutes": 0 } }""")]
    [InlineData("""{ "bad": { "crater_radius_by_phase": [1, 2] } }""")]
    [InlineData("""{ "bad": { "min_players_per_team": 0 } }""")]
    public void BadPresetsAreRejected(string json)
    {
        Assert.Throws<InvalidDataException>(() => MatchPreset.AllFromJson(json));
    }
}
