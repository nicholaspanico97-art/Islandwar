namespace IslandWar.Sim.Match;

/// <summary>How matches that aren't won by a nuke are decided.</summary>
public static class MatchScore
{
    /// <summary>
    /// At the time limit, the team that kept more of its core standing wins; level cores are a draw. Core
    /// damage from conventional raids is what counts, so raids matter even without a nuke.
    /// </summary>
    public static Team TimeLimitWinner(int redCoreStanding, int blueCoreStanding) =>
        redCoreStanding > blueCoreStanding ? Team.Red
        : blueCoreStanding > redCoreStanding ? Team.Blue
        : Team.None;

    /// <summary>Votes a team needs to concede: <paramref name="fraction"/> of its players, rounded up, and at least one.</summary>
    public static int ConcedeVotesNeeded(int teamSize, float fraction) =>
        Math.Max(1, (int)Math.Ceiling(teamSize * fraction - 1e-6));

    public static bool Concedes(int votes, int teamSize, float fraction) =>
        teamSize > 0 && votes >= ConcedeVotesNeeded(teamSize, fraction);
}
