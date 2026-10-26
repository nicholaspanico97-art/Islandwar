namespace IslandWar.Sim.Match;

public enum MatchStage : byte
{
    /// <summary>Waiting for players. Anything goes; the world is reset when the match starts.</summary>
    Warmup = 0,

    /// <summary>Both teams have enough players; the match starts when this runs out.</summary>
    Countdown = 1,

    Playing = 2,

    /// <summary>The result is showing; then it's back to warmup.</summary>
    Ended = 3,
}

/// <summary>Why a match ended. Values go over the network; only append.</summary>
public enum EndReason : byte
{
    None = 0,
    TimeLimit = 1,
    CoreDestroyed = 2,
    Concede = 3,
}

/// <summary>
/// Where the match stands. Every system that cares about the phase reads <see cref="Phase"/> (0 outside
/// play, then 1 to 3).
/// </summary>
/// <param name="StageSeconds">Time spent in the current stage. While playing, that's the match clock.</param>
public readonly record struct MatchStatus(MatchStage Stage, int Phase, float StageSeconds, Team Winner, EndReason Reason)
{
    public static readonly MatchStatus Warmup = new(MatchStage.Warmup, 0, 0, Team.None, EndReason.None);

    public bool Playing => Stage == MatchStage.Playing;
}

/// <summary>What changed in a step, for the server to act on (reset the world, announce, and so on).</summary>
public enum MatchEvent
{
    None,
    CountdownStarted,
    CountdownCancelled,
    MatchStarted,
    PhaseChanged,
    MatchEnded,
    BackToWarmup,
}

/// <summary>The match clock and its stages, as plain functions the server steps once per tick.</summary>
public static class MatchFlow
{
    /// <summary>Moves the match on by <paramref name="dt"/> seconds, given how many players each team has.</summary>
    public static (MatchStatus Status, MatchEvent Event) Advance(MatchStatus status, MatchPreset preset, float dt, int red, int blue)
    {
        float seconds = status.StageSeconds + dt;
        bool enoughPlayers = red >= preset.MinPlayersPerTeam && blue >= preset.MinPlayersPerTeam;
        switch (status.Stage)
        {
            case MatchStage.Warmup:
                return enoughPlayers
                    ? (status with { Stage = MatchStage.Countdown, StageSeconds = 0 }, MatchEvent.CountdownStarted)
                    : (status with { StageSeconds = seconds }, MatchEvent.None);

            case MatchStage.Countdown:
                if (!enoughPlayers)
                {
                    return (MatchStatus.Warmup, MatchEvent.CountdownCancelled);
                }

                return seconds >= preset.CountdownSeconds
                    ? (new MatchStatus(MatchStage.Playing, 1, 0, Team.None, EndReason.None), MatchEvent.MatchStarted)
                    : (status with { StageSeconds = seconds }, MatchEvent.None);

            case MatchStage.Playing:
                if (seconds >= preset.TimeLimitSeconds)
                {
                    // Piece 4 replaces the draw with a score.
                    return (End(status, Team.None, EndReason.TimeLimit), MatchEvent.MatchEnded);
                }

                int phase = PhaseAt(preset, seconds);
                return (status with { StageSeconds = seconds, Phase = phase }, phase != status.Phase ? MatchEvent.PhaseChanged : MatchEvent.None);

            default:
                return seconds >= preset.ResultsSeconds
                    ? (MatchStatus.Warmup, MatchEvent.BackToWarmup)
                    : (status with { StageSeconds = seconds }, MatchEvent.None);
        }
    }

    /// <summary>Ends the match now with a result (a destroyed core, a concession, the time limit).</summary>
    public static MatchStatus End(MatchStatus status, Team winner, EndReason reason) =>
        new(MatchStage.Ended, status.Phase, 0, winner, reason);

    public static int PhaseAt(MatchPreset preset, float playingSeconds) =>
        playingSeconds < preset.Phase2StartSeconds ? 1 : playingSeconds < preset.Phase3StartSeconds ? 2 : 3;

    /// <summary>
    /// Seconds until the next thing happens: the end of the countdown, the next phase, the time limit (in
    /// phase 3), or the return to warmup. Null in warmup.
    /// </summary>
    public static float? SecondsToNext(MatchStatus status, MatchPreset preset) => status.Stage switch
    {
        MatchStage.Countdown => preset.CountdownSeconds - status.StageSeconds,
        MatchStage.Playing => status.Phase switch
        {
            1 => preset.Phase2StartSeconds,
            2 => preset.Phase3StartSeconds,
            _ => preset.TimeLimitSeconds,
        } - status.StageSeconds,
        MatchStage.Ended => preset.ResultsSeconds - status.StageSeconds,
        _ => null,
    };

    /// <summary>
    /// The crater radius a bomb dropped now would make; 0 means bombs are locked. During play it follows the
    /// phase. Warmup is practice, so bombs work with the warmup radius; after the result, nothing.
    /// </summary>
    public static int CraterRadius(MatchStatus status, MatchPreset preset, int warmupRadius) => status.Stage switch
    {
        MatchStage.Playing => preset.CraterRadiusByPhase[Math.Clamp(status.Phase, 1, 3) - 1],
        MatchStage.Ended => 0,
        _ => warmupRadius,
    };
}
