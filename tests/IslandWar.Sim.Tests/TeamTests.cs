using IslandWar.Sim.Match;

namespace IslandWar.Sim.Tests;

public class TeamTests
{
    [Theory]
    [InlineData(0, 0, Team.None, Team.Red)]
    [InlineData(1, 0, Team.None, Team.Blue)]
    [InlineData(1, 1, Team.None, Team.Red)]
    [InlineData(0, 0, Team.Blue, Team.Blue)]
    [InlineData(1, 0, Team.Red, Team.Blue)]
    [InlineData(1, 1, Team.Red, Team.Red)]
    [InlineData(2, 3, Team.Blue, Team.Red)]
    public void JoinersFillTheSmallerTeamUnlessTheirChoiceKeepsItFair(int red, int blue, Team requested, Team expected)
    {
        Assert.Equal(expected, Teams.Assign(red, blue, requested, maxImbalance: 1));
    }

    [Fact]
    public void AnyImbalanceAllowedLetsEveryoneChoose()
    {
        Assert.Equal(Team.Red, Teams.Assign(5, 0, Team.Red, maxImbalance: int.MaxValue / 2));
    }

    [Fact]
    public void TeamsKnowTheirIslandAndOpponent()
    {
        Assert.Equal(0, Team.Red.IslandIndex());
        Assert.Equal(1, Team.Blue.IslandIndex());
        Assert.Equal(Team.Blue, Team.Red.Other());
        Assert.Equal(Team.Blue, Teams.Parse(" Blue "));
        Assert.Equal(Team.None, Teams.Parse("green"));
    }

    [Fact]
    public void PresetsDataFileLoadsBothPresets()
    {
        IReadOnlyDictionary<string, MatchPreset> presets = MatchPreset.AllFromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "presets.json")));

        Assert.Contains("compressed", presets.Keys);
        Assert.Contains("full", presets.Keys);
        Assert.Contains("practice", presets.Keys);
        Assert.Contains("test", presets.Keys);
        Assert.True(presets["full"].MaxPlayersPerTeam > presets["compressed"].MaxPlayersPerTeam);
    }
}

public class PlayerNameTests
{
    [Theory]
    [InlineData("  Nick  ", "Nick")]
    [InlineData("", "Player")]
    [InlineData(null, "Player")]
    [InlineData("<b>bold</b>\n", "bboldb")]
    [InlineData("A name that is much too long", "A name that is m")]
    public void NamesAreCleanedUp(string? requested, string expected)
    {
        Assert.Equal(expected, PlayerNames.Clean(requested, Array.Empty<string>()));
    }

    [Fact]
    public void TakenNamesGetANumber()
    {
        Assert.Equal("Nick 2", PlayerNames.Clean("Nick", new[] { "nick" }));
        Assert.Equal("Nick 3", PlayerNames.Clean("Nick", new[] { "Nick", "Nick 2" }));
        Assert.Equal("ABCDEFGHIJKLMN 2", PlayerNames.Clean("ABCDEFGHIJKLMNOP", new[] { "ABCDEFGHIJKLMNOP" }));
    }

    [Fact]
    public void RosterRoundTrips()
    {
        var roster = new[] { new RosterEntry(5, "Nick", Team.Red), new RosterEntry(9, "Sam 2", Team.Blue) };

        Assert.Equal(roster, IslandWar.Sim.Protocol.WireFormat.DecodeRoster(IslandWar.Sim.Protocol.WireFormat.EncodeRoster(roster)));
    }
}
