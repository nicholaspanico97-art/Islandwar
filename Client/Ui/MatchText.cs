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
            // A preset that needs more players than a team may hold never starts: the practice server.
            MatchStage.Warmup when preset.MinPlayersPerTeam > preset.MaxPlayersPerTeam =>
                "Practice: no match on this server. Anything goes.",
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
            return (Result(now), Reason(now));
        }

        return null;
    }

    public static string Result(MatchStatus match) => match.Winner == Team.None ? "Draw" : $"{match.Winner} team wins";

    private static string Reason(MatchStatus match) => match.Reason switch
    {
        EndReason.TimeLimit => match.Winner == Team.None
            ? "Time's up, with the cores level."
            : $"Time's up: {match.Winner} kept more of its core.",
        EndReason.CoreDestroyed => $"{match.Winner}'s nuke hit the {match.Winner.Other()} core.",
        EndReason.Concede => $"{match.Winner.Other()} conceded.",
        _ => "",
    };

    /// <summary>Both cores' health, for the line under the match status, with who's ahead on the time-limit score.</summary>
    public static string Cores(int red, int blue, int total)
    {
        if (total == 0)
        {
            return "";
        }

        Team ahead = MatchScore.TimeLimitWinner(red, blue);
        return $"Red core {100 * red / total}%    Blue core {100 * blue / total}%" + (ahead == Team.None ? "" : $"    ({ahead} ahead at the time limit)");
    }

    /// <summary>Our team's concede votes, or empty when nobody has voted.</summary>
    public static string Concede(int votes, int teamSize, float fraction) =>
        votes == 0 ? "" : $"Your team: {votes} of {MatchScore.ConcedeVotesNeeded(teamSize, fraction)} votes to concede (F10 to vote)";

    /// <summary>The nuke line in the flight readout.</summary>
    public static string Nuke(float readyIn) =>
        readyIn < 0 ? "Nuke: phase 3"
        : readyIn == 0 ? "NUKE READY: right click over the enemy core"
        : $"Nuke in {Clock(readyIn)}";

    private static string Clock(float seconds)
    {
        int whole = Math.Max(0, (int)MathF.Ceiling(seconds));
        return $"{whole / 60}:{whole % 60:00}";
    }

    private static string Seconds(float seconds) => $"{Math.Max(0, (int)MathF.Ceiling(seconds))} s";
}
