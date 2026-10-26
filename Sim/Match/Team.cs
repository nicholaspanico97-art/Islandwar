namespace IslandWar.Sim.Match;

/// <summary>The two sides. Red holds the west island, Blue the east one. Values go over the network.</summary>
public enum Team : byte
{
    None = 0,
    Red = 1,
    Blue = 2,
}

public static class Teams
{
    public static readonly Team[] Playing = { Team.Red, Team.Blue };

    public static Team Other(this Team team) => team switch
    {
        Team.Red => Team.Blue,
        Team.Blue => Team.Red,
        _ => Team.None,
    };

    /// <summary>Which island a team holds: 0 is the west one (see IslandSettings.IslandCentre).</summary>
    public static int IslandIndex(this Team team) => team == Team.Blue ? 1 : 0;

    public static Team Parse(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "red" => Team.Red,
        "blue" => Team.Blue,
        _ => Team.None,
    };

    /// <summary>
    /// Picks a team for a joining player. A requested team is honoured unless it would leave that team more
    /// than <paramref name="maxImbalance"/> players ahead; otherwise the smaller team, Red on a tie.
    /// </summary>
    public static Team Assign(int red, int blue, Team requested, int maxImbalance)
    {
        if (requested == Team.Red && red + 1 - blue <= maxImbalance)
        {
            return Team.Red;
        }

        if (requested == Team.Blue && blue + 1 - red <= maxImbalance)
        {
            return Team.Blue;
        }

        return blue < red ? Team.Blue : Team.Red;
    }
}
