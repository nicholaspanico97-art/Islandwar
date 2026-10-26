namespace IslandWar.Sim.Match;

/// <summary>
/// One match preset from <c>data/presets.json</c>: Full (12v12, hours) or Compressed (4v4, about 45 minutes).
/// M2 grows this one piece at a time; never hardcode these.
/// </summary>
public sealed record MatchPreset
{
    public int MaxPlayersPerTeam { get; init; } = 4;

    /// <summary>A player may choose a team only if it leaves that team at most this many players ahead.</summary>
    public int MaxTeamImbalance { get; init; } = 1;

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
        if (MaxPlayersPerTeam <= 0 || MaxTeamImbalance < 0)
        {
            throw new InvalidDataException($"Preset '{name}' in presets.json has a value out of range.");
        }
    }
}
