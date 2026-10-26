using IslandWar.Sim.Match;

namespace IslandWar.Sim.Tests;

public class MatchScoreTests
{
    [Theory]
    [InlineData(245, 200, Team.Red)]
    [InlineData(10, 11, Team.Blue)]
    [InlineData(245, 245, Team.None)]
    [InlineData(0, 0, Team.None)]
    public void TheTeamThatKeptMoreCoreWinsAtTheTimeLimit(int red, int blue, Team expected)
    {
        Assert.Equal(expected, MatchScore.TimeLimitWinner(red, blue));
    }

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
