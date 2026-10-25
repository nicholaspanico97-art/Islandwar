using IslandWar.Sim.Players;

namespace IslandWar.Sim.Tests;

public class PlayerSettingsTests
{
    [Fact]
    public void PlayerDataFileLoads()
    {
        PlayerSettings settings = PlayerSettings.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "player.json")));

        Assert.True(settings.Reach > 0);
        Assert.True(settings.JumpVelocity > 0);
    }

    [Fact]
    public void NonPositiveValuesAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => PlayerSettings.FromJson("""{ "reach": 0 }"""));
    }
}
