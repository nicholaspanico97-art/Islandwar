using IslandWar.Sim.Match;

namespace IslandWar.Sim.Tests;

public class MatchScoreTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(4, 3)]
    [InlineData(12, 8)]
    public void TwoThirdsOfTheTeamMustVoteToConcede(int teamSize, int needed)
    {
        Assert.Equal(needed, MatchScore.ConcedeVotesNeeded(teamSize, 0.66f));
    }

    [Fact]
    public void AnEmptyTeamCantConcede()
    {
        Assert.False(MatchScore.Concedes(0, 0, 0.66f));
        Assert.True(MatchScore.Concedes(1, 1, 0.66f));
        Assert.False(MatchScore.Concedes(1, 3, 0.66f));
    }
}
