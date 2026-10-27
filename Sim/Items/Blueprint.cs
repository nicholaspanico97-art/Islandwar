using IslandWar.Sim.Objects;

namespace IslandWar.Sim.Items;

/// <summary>
/// A structure that isn't built yet. In a real match placing a structure puts down its blueprint: it fills
/// its footprint like the finished structure, but does nothing (stores, smelts and crafts nothing) until
/// players have carried its whole cost to it from their backpacks. Delivered items are kept in the blueprint's
/// <c>delivered</c> slot; when they cover the cost, they're used up and the structure is built.
/// </summary>
public static class Blueprint
{
    /// <summary>The value that marks an unbuilt structure.</summary>
    public const string Value = "blueprint";

    /// <summary>The slot that holds what's been delivered so far.</summary>
    public const string DeliveredSlot = "delivered";

    /// <summary>A blueprint starts with this share of the finished structure's health.</summary>
    public const int HealthDivisor = 4;

    public static bool IsBlueprint(this GameObject obj) => obj.Values.ContainsKey(Value);

    /// <summary>A fresh blueprint of a kind: marked, empty, with a quarter of the health.</summary>
    public static GameObject Start(GameObject placed, ObjectKind kind) =>
        placed.With(Value, 1) with { Health = Math.Max(1, kind.MaxHealth / HealthDivisor) };

    public static IReadOnlyDictionary<string, int> Delivered(GameObject blueprint) => ItemCounts.Contents(blueprint, DeliveredSlot);

    /// <summary>What still has to be carried to a blueprint, by item id (nothing once it's built).</summary>
    public static IReadOnlyDictionary<string, int> Missing(GameObject blueprint, ObjectKind kind)
    {
        if (!blueprint.IsBlueprint())
        {
            return ItemCounts.None;
        }

        IReadOnlyDictionary<string, int> delivered = Delivered(blueprint);
        return kind.Cost.Aggregate(ItemCounts.None, (missing, c) => missing.Plus(c.Key, c.Value - delivered.Of(c.Key)));
    }

    /// <summary>The built structure: delivered items used up, the mark gone, full health.</summary>
    public static GameObject Finish(GameObject blueprint, ObjectKind kind)
    {
        GameObject built = blueprint.With(Value, 0);
        foreach (string item in Delivered(blueprint).Keys)
        {
            built = built.With($"{DeliveredSlot}:{item}", 0);
        }

        return built with { Health = kind.MaxHealth };
    }

    /// <summary>How far along a blueprint is, from 0 to 1, by items delivered against items needed.</summary>
    public static float Progress(GameObject blueprint, ObjectKind kind)
    {
        int needed = kind.Cost.Values.Sum();
        return needed == 0 ? 1 : (float)Delivered(blueprint).Total() / needed;
    }
}
