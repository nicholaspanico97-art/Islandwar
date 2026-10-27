using IslandWar.Sim.Objects;

namespace IslandWar.Sim.Items;

/// <summary>
/// A structure that isn't built yet. In a real match placing a structure puts down its blueprint: it fills
/// its footprint like the finished structure, but does nothing (stores, smelts and crafts nothing) until
/// players have carried its whole cost to it from their backpacks. Delivered items are kept in the blueprint's
/// <c>delivered</c> slot; when they cover the cost, they're used up and the structure is built. A built dock
/// takes deliveries the same way, for the ship it builds.
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

    /// <summary>
    /// What players are carrying things to an object for: a blueprint's own cost, or the ship a built dock
    /// builds. Null for anything else.
    /// </summary>
    public static IReadOnlyDictionary<string, int>? Needs(GameObject obj, ObjectKind kind, ObjectKinds kinds) =>
        obj.IsBlueprint() ? kind.Cost : kinds.ShipBuiltAt(kind)?.Cost;

    /// <summary>What still has to be carried to an object, by item id (nothing if it needs nothing).</summary>
    public static IReadOnlyDictionary<string, int> Missing(GameObject obj, ObjectKind kind, ObjectKinds kinds)
    {
        IReadOnlyDictionary<string, int> delivered = Delivered(obj);
        return (Needs(obj, kind, kinds) ?? ItemCounts.None)
            .Aggregate(ItemCounts.None, (missing, c) => missing.Plus(c.Key, c.Value - delivered.Of(c.Key)));
    }

    /// <summary>The built structure: delivered items used up, the mark gone, full health.</summary>
    public static GameObject Finish(GameObject blueprint, ObjectKind kind) => UseUp(blueprint.With(Value, 0)) with { Health = kind.MaxHealth };

    /// <summary>A copy with the delivered items used up (a dock after launching its ship).</summary>
    public static GameObject UseUp(GameObject obj)
    {
        foreach (string item in Delivered(obj).Keys)
        {
            obj = obj.With($"{DeliveredSlot}:{item}", 0);
        }

        return obj;
    }

    /// <summary>How far along a blueprint (or a dock's ship) is, from 0 to 1, by items delivered against items needed.</summary>
    public static float Progress(GameObject obj, ObjectKind kind, ObjectKinds kinds)
    {
        int needed = (Needs(obj, kind, kinds) ?? ItemCounts.None).Values.Sum();
        return needed == 0 ? 1 : (float)Delivered(obj).Total() / needed;
    }
}
