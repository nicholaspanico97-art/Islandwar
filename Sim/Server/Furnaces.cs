using IslandWar.Sim.Items;
using IslandWar.Sim.Objects;
using IslandWar.Sim.Protocol;

namespace IslandWar.Sim.Server;

/// <summary>
/// Furnaces smelting: twice a second each furnace with something to smelt, room for the result and fuel
/// (burning, or in its fuel slot to light) moves on. A burning item lasts its fuel seconds; fuel only burns
/// while something is smelting. Changed furnaces go out to everyone, like any object change.
/// </summary>
public sealed class Furnaces
{
    /// <summary>A furnace's values: milliseconds left on the item burning, and of progress on the one smelting.</summary>
    public const string BurnValue = "burn_ms";
    public const string ProgressValue = "progress_ms";

    private readonly ServerState _state;

    public Furnaces(ServerState state)
    {
        _state = state;
    }

    private int StepTicks => Math.Max(1, _state.Data.Net.TickRate / 2);

    public void Step()
    {
        if (_state.Tick % StepTicks != 0)
        {
            return;
        }

        int elapsedMs = (int)Math.Round(StepTicks * _state.TickSeconds * 1000);
        var changed = new List<GameObject>();
        foreach (GameObject furnace in _state.Objects.All.ToList())
        {
            ObjectKind kind = _state.Objects.KindOf(furnace);
            if (kind.Smelter is { } smelter && Smelt(furnace, smelter, _state.Data.Items, elapsedMs) is { } after)
            {
                _state.Objects.Set(after);
                changed.Add(after);
            }
        }

        if (changed.Count > 0)
        {
            _state.Net.BroadcastObjects(WireFormat.EncodeObjects(changed));
        }
    }

    /// <summary>
    /// One step of smelting <paramref name="elapsedMs"/> long: the furnace as it is after, or null if nothing
    /// happened (nothing to smelt, no fuel, or no room for the result).
    /// </summary>
    public static GameObject? Smelt(GameObject furnace, SmelterSpec smelter, ItemSettings items, int elapsedMs)
    {
        IReadOnlyDictionary<string, int> input = ItemCounts.Contents(furnace, ItemCounts.InputSlot);
        SmeltingRecipe? recipe = input.Keys.Select(items.SmeltingFor).FirstOrDefault(r => r is not null);
        if (recipe is null || ItemCounts.Contents(furnace, ItemCounts.OutputSlot).Total() >= smelter.SlotCapacity)
        {
            return null;
        }

        GameObject after = furnace;
        int burn = furnace.Values.Of(BurnValue);
        if (burn <= 0)
        {
            // Light the next item of fuel, if there is one.
            if (ItemCounts.Contents(furnace, ItemCounts.FuelSlot).Keys.FirstOrDefault(items.IsFuel) is not { } fuel)
            {
                return null;
            }

            after = ItemCounts.WithSlot(after, ItemCounts.FuelSlot, fuel, -1);
            burn += (int)Math.Round(items.Fuel[fuel] * 1000);
        }

        int progress = furnace.Values.Of(ProgressValue) + elapsedMs;
        burn -= elapsedMs;
        if (progress >= recipe.Seconds * 1000)
        {
            after = ItemCounts.WithSlot(after, ItemCounts.InputSlot, recipe.Takes, -1);
            after = ItemCounts.WithSlot(after, ItemCounts.OutputSlot, recipe.Makes, 1);
            progress = 0;
        }

        return after.With(BurnValue, Math.Max(0, burn)).With(ProgressValue, progress);
    }
}
