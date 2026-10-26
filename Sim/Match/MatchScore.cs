namespace IslandWar.Sim.Match;

/// <summary>
/// How matches that aren't won by a nuke are decided. For now only by concession; reaching the time limit
/// is a draw until a score is designed (see docs/decisions.md).
/// </summary>
public static class MatchScore
{
    /// <summary>Votes a team needs to concede: <paramref name="fraction"/> of its players, rounded up, and at least one.</summary>
    public static int ConcedeVotesNeeded(int teamSize, float fraction) =>
        Math.Max(1, (int)Math.Ceiling(teamSize * fraction - 1e-6));

    public static bool Concedes(int votes, int teamSize, float fraction) =>
        teamSize > 0 && votes >= ConcedeVotesNeeded(teamSize, fraction);
}
