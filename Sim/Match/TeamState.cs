using IslandWar.Sim.Items;

namespace IslandWar.Sim.Match;

/// <summary>
/// What a team has as a whole, as opposed to what each player or structure has: its stock (the items in all
/// its storage structures), its era, its research points and the techs it has researched. The server keeps
/// one per team and sends it only to that team's players, so neither side sees the other's.
/// </summary>
public sealed record TeamState(Team Team)
{
    /// <summary>Amounts by item id, in id order; anything not listed is 0. Everything in the team's storage, added up.</summary>
    public IReadOnlyDictionary<string, int> Stock { get; init; } = ItemCounts.None;

    /// <summary>The era the team has reached, as an index into the eras in <c>techs.json</c> (0 is the first).</summary>
    public int Era { get; init; }

    /// <summary>Unspent research points by era id: each era has its own pool.</summary>
    public IReadOnlyDictionary<string, int> Points { get; init; } = ItemCounts.None;

    /// <summary>The techs researched, by id, in id order.</summary>
    public IReadOnlyList<string> Researched { get; init; } = Array.Empty<string>();

    public int Amount(string resource) => Stock.Of(resource);

    public bool HasResearched(string tech) => Researched.Contains(tech);

    /// <summary>A copy with <paramref name="change"/> added to a resource; it can't go below 0.</summary>
    public TeamState Add(string resource, int change) => this with { Stock = Stock.Plus(resource, change) };
}
