using System;
using IslandWar.Sim.Match;

namespace IslandWar.Client.Ui;

/// <summary>The words for the match: the status line at the top of the screen and the big announcements.</summary>
public static class MatchText
{
    /// <param name="red">Players on Red now.</param>
    /// <param name="blue">Players on Blue now.</param>
    public static string Status(MatchStatus match, MatchPreset preset, int red, int blue)
    {
        float next = MatchFlow.SecondsToNext(match, preset) ?? 0;
        return match.Stage switch
        {
            MatchStage.Warmup =>
                $"Warmup: the match starts when both teams have {preset.MinPlayersPerTeam} (Red {red}, Blue {blue}). Anything goes; the island resets at the start.",
            MatchStage.Countdown => $"Match starts in {Seconds(next)}",
            MatchStage.Playing => match.Phase switch
            {
                1 => $"Phase 1 of 3: scouting, no bombs | phase 2 in {Clock(next)}",
                2 => $"Phase 2 of 3: bombs (crater {preset.CraterRadiusByPhase[1]}) | phase 3 in {Clock(next)}",
                _ => $"Phase 3 of 3: heavy bombs (crater {preset.CraterRadiusByPhase[2]}) | time limit in {Clock(next)}",
            },
            _ => $"{Result(match)} | next warmup in {Seconds(next)}",
        };
    }

    /// <summary>A big announcement when the match moves on, or null when nothing worth announcing changed.</summary>
    public static (string Title, string Detail)? Announcement(MatchStatus before, MatchStatus now, MatchPreset preset)
    {
        if (now.Stage == MatchStage.Playing && (before.Stage != MatchStage.Playing || before.Phase != now.Phase))
        {
            return now.Phase switch
            {
                1 => ("The match has started", "Phase 1: scouting only. Bombs unlock in phase 2."),
                2 => ("Phase 2", $"Bombs unlocked: crater radius {preset.CraterRadiusByPhase[1]}."),
                _ => ("Phase 3", $"Heavy bombs: crater radius {preset.CraterRadiusByPhase[2]}."),
            };
        }

        if (now.Stage == MatchStage.Ended && before.Stage != MatchStage.Ended)
        {
            return (Result(now), Reason(now.Reason));
        }

        return null;
    }

    public static string Result(MatchStatus match) => match.Winner == Team.None ? "Draw" : $"{match.Winner} team wins";

    private static string Reason(EndReason reason) => reason switch
    {
        EndReason.TimeLimit => "The time limit was reached.",
        EndReason.CoreDestroyed => "An industrial core was destroyed.",
        EndReason.Concede => "A team conceded.",
        _ => "",
    };

    private static string Clock(float seconds)
    {
        int whole = Math.Max(0, (int)MathF.Ceiling(seconds));
        return $"{whole / 60}:{whole % 60:00}";
    }

    private static string Seconds(float seconds) => $"{Math.Max(0, (int)MathF.Ceiling(seconds))} s";
}
