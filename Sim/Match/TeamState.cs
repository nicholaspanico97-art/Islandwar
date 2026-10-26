namespace IslandWar.Sim.Match;

/// <summary>
/// What a team has as a whole, as opposed to what each player or structure has: for now a stock of named
/// resources. The server keeps one per team and sends it only to that team's players, so neither side
/// sees the other's. What goes in the stock comes with the resources and the tech tree.
/// </summary>
public sealed record TeamState(Team Team)
{
    private static readonly IReadOnlyDictionary<string, int> Nothing = new SortedDictionary<string, int>(StringComparer.Ordinal);

    /// <summary>Amounts by resource name, in name order; anything not listed is 0.</summary>
    public IReadOnlyDictionary<string, int> Stock { get; init; } = Nothing;

    public int Amount(string resource) => Stock.TryGetValue(resource, out int amount) ? amount : 0;

    /// <summary>A copy with <paramref name="change"/> added to a resource; it can't go below 0.</summary>
    public TeamState Add(string resource, int change)
    {
        var stock = new SortedDictionary<string, int>(Stock.ToDictionary(s => s.Key, s => s.Value), StringComparer.Ordinal);
        int amount = Math.Max(0, Amount(resource) + change);
        if (amount == 0)
        {
            stock.Remove(resource);
        }
        else
        {
            stock[resource] = amount;
        }

        return this with { Stock = stock };
    }
}
