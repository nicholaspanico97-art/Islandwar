namespace IslandWar.Sim.Match;

/// <summary>
/// One match preset from <c>data/presets.json</c>: Full (12v12, hours), Compressed (4v4, about 45 minutes),
/// and helpers for practice and tests. Never hardcode these.
/// </summary>
public sealed record MatchPreset
{
    public int MaxPlayersPerTeam { get; init; } = 4;

    /// <summary>A player may choose a team only if it leaves that team at most this many players ahead.</summary>
    public int MaxTeamImbalance { get; init; } = 1;

    /// <summary>The countdown to the match starts once both teams have at least this many players.</summary>
    public int MinPlayersPerTeam { get; init; } = 1;

    /// <summary>Length of the countdown before the match starts. Dropping below the minimum cancels it.</summary>
    public float CountdownSeconds { get; init; } = 10;

    /// <summary>When phase 2 begins, in minutes from the start. Phase 1 is everything before it.</summary>
    public float Phase2StartMinutes { get; init; } = 8;

    /// <summary>When phase 3 begins, in minutes from the start.</summary>
    public float Phase3StartMinutes { get; init; } = 25;

    /// <summary>The match ends here at the latest.</summary>
    public float TimeLimitMinutes { get; init; } = 45;

    /// <summary>How long the result stays up before the server goes back to warmup.</summary>
    public float ResultsSeconds { get; init; } = 20;

    /// <summary>
    /// Bomb crater radius in phases 1, 2 and 3. 0 means no bombs in that phase. The clock drives escalation
    /// (design pillar 1): early raids sting, late ones wreck.
    /// </summary>
    public int[] CraterRadiusByPhase { get; init; } = { 0, 3, 5 };

    /// <summary>
    /// Each team gets a nuke when phase 3 starts, then another this many minutes after dropping one. The stub
    /// until the research and enrichment pipeline (M4).
    /// </summary>
    public float NukeCooldownMinutes { get; init; } = 4;

    /// <summary>
    /// Free building for practice and the automated tests: blocks place without items, mine at once and give
    /// nothing, as in Minecraft's creative mode. Real matches gather and carry everything.
    /// </summary>
    public bool Creative { get; init; }

    /// <summary>A team concedes once this share of its players (rounded up) have voted to.</summary>
    public float ConcedeFraction { get; init; } = 0.66f;

    public float Phase2StartSeconds => Phase2StartMinutes * 60;

    public float Phase3StartSeconds => Phase3StartMinutes * 60;

    public float TimeLimitSeconds => TimeLimitMinutes * 60;

    public static IReadOnlyDictionary<string, MatchPreset> AllFromJson(string json)
    {
        Dictionary<string, MatchPreset> presets = DataJson.Parse<Dictionary<string, MatchPreset>>(json, "presets.json");
        foreach ((string name, MatchPreset preset) in presets)
        {
            preset.Validate(name);
        }

        return presets;
    }

    public void Validate(string name)
    {
        if (MaxPlayersPerTeam <= 0 || MaxTeamImbalance < 0 || MinPlayersPerTeam <= 0 || CountdownSeconds < 0 || ResultsSeconds < 0 || NukeCooldownMinutes < 0 || ConcedeFraction <= 0 || ConcedeFraction > 1)
        {
            throw new InvalidDataException($"Preset '{name}' in presets.json has a value out of range.");
        }

        if (!(Phase2StartMinutes > 0 && Phase3StartMinutes > Phase2StartMinutes && TimeLimitMinutes > Phase3StartMinutes))
        {
            throw new InvalidDataException($"Preset '{name}': phases must start in order before the time limit (0 < phase 2 < phase 3 < time limit).");
        }

        if (CraterRadiusByPhase.Length != 3 || CraterRadiusByPhase.Any(r => r < 0))
        {
            throw new InvalidDataException($"Preset '{name}': crater_radius_by_phase needs three radii, 0 or more.");
        }
    }
}
