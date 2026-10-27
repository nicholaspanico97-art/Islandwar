using IslandWar.Sim.Objects;

namespace IslandWar.Sim.Items;

/// <summary>
/// Amounts of items by id, the shape of a backpack, a storage's contents and a team's stock. Always kept
/// in id order with no zero entries, so equal contents hash and encode the same everywhere.
/// </summary>
public static class ItemCounts
{
    /// <summary>
    /// Structures keep items in their values as <c>slot:item</c>: a storage structure's in the <c>item</c> slot
    /// (<c>item:stone</c>), a furnace's in <c>input</c>, <c>fuel</c> and <c>output</c>.
    /// </summary>
    public const string StorageSlot = "item";
    public const string InputSlot = "input";
    public const string FuelSlot = "fuel";
    public const string OutputSlot = "output";

    public const string StoragePrefix = StorageSlot + ":";

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

    /// <summary>What a structure holds in one slot (a storage structure's contents by default).</summary>
    public static IReadOnlyDictionary<string, int> Contents(GameObject obj, string slot = StorageSlot)
    {
        string prefix = slot + ":";
        var contents = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach ((string name, int value) in obj.Values)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                contents[name[prefix.Length..]] = value;
            }
        }

        return contents;
    }

    /// <summary>Whether a structure holds any items in any slot.</summary>
    public static bool HasItems(GameObject obj) => obj.Values.Keys.Any(k => k.Contains(':'));

    /// <summary>A copy of a structure with <paramref name="change"/> more (or fewer) of an item in one slot.</summary>
    public static GameObject WithSlot(GameObject obj, string slot, string item, int change) =>
        obj.With($"{slot}:{item}", Math.Max(0, Contents(obj, slot).Of(item) + change));

    /// <summary>A copy of a storage structure with <paramref name="change"/> more (or fewer) of an item in it.</summary>
    public static GameObject WithStored(GameObject storage, string item, int change) => WithSlot(storage, StorageSlot, item, change);
}
