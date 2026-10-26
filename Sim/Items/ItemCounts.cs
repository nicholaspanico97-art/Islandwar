using IslandWar.Sim.Objects;

namespace IslandWar.Sim.Items;

/// <summary>
/// Amounts of items by id, the shape of a backpack, a storage's contents and a team's stock. Always kept
/// in id order with no zero entries, so equal contents hash and encode the same everywhere.
/// </summary>
public static class ItemCounts
{
    /// <summary>A storage structure keeps its contents in its values under this prefix: <c>item:stone</c>.</summary>
    public const string StoragePrefix = "item:";

    public static readonly IReadOnlyDictionary<string, int> None = new SortedDictionary<string, int>(StringComparer.Ordinal);

    public static int Total(this IReadOnlyDictionary<string, int> counts) => counts.Values.Sum();

    public static int Of(this IReadOnlyDictionary<string, int> counts, string item) => counts.TryGetValue(item, out int count) ? count : 0;

    /// <summary>A copy with <paramref name="change"/> added to one item; it can't go below 0.</summary>
    public static IReadOnlyDictionary<string, int> Plus(this IReadOnlyDictionary<string, int> counts, string item, int change)
    {
        var result = new SortedDictionary<string, int>(counts.ToDictionary(c => c.Key, c => c.Value), StringComparer.Ordinal);
        int amount = Math.Max(0, counts.Of(item) + change);
        if (amount == 0)
        {
            result.Remove(item);
        }
        else
        {
            result[item] = amount;
        }

        return result;
    }

    /// <summary>Whether there is at least as much of every item as <paramref name="cost"/> asks.</summary>
    public static bool Covers(this IReadOnlyDictionary<string, int> counts, IReadOnlyDictionary<string, int> cost) =>
        cost.All(c => counts.Of(c.Key) >= c.Value);

    /// <summary>Everything in <paramref name="more"/> added to <paramref name="counts"/>.</summary>
    public static IReadOnlyDictionary<string, int> Sum(this IReadOnlyDictionary<string, int> counts, IReadOnlyDictionary<string, int> more) =>
        more.Aggregate(counts, (total, entry) => total.Plus(entry.Key, entry.Value));

    /// <summary>What a storage structure holds.</summary>
    public static IReadOnlyDictionary<string, int> Contents(GameObject storage)
    {
        var contents = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach ((string name, int value) in storage.Values)
        {
            if (name.StartsWith(StoragePrefix, StringComparison.Ordinal))
            {
                contents[name[StoragePrefix.Length..]] = value;
            }
        }

        return contents;
    }

    /// <summary>A copy of a storage structure with <paramref name="change"/> more (or fewer) of an item in it.</summary>
    public static GameObject WithStored(GameObject storage, string item, int change) =>
        storage.With(StoragePrefix + item, Math.Max(0, Contents(storage).Of(item) + change));
}
