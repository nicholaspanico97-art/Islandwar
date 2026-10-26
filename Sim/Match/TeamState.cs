using IslandWar.Sim.Items;

namespace IslandWar.Sim.Match;

/// <summary>
/// What a team has as a whole, as opposed to what each player or structure has: for now its stock, the
/// items in all its storage structures. The server keeps one per team and sends it only to that team's
/// players, so neither side sees the other's.
/// </summary>
public sealed record TeamState(Team Team)
{
    /// <summary>Amounts by item id, in id order; anything not listed is 0. Everything in the team's storage, added up.</summary>
    public IReadOnlyDictionary<string, int> Stock { get; init; } = ItemCounts.None;

    public int Amount(string resource) => Stock.Of(resource);

    /// <summary>A copy with <paramref name="change"/> added to a resource; it can't go below 0.</summary>
    public TeamState Add(string resource, int change) => this with { Stock = Stock.Plus(resource, change) };
}
