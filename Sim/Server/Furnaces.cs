using IslandWar.Sim.Items;
using IslandWar.Sim.Objects;
using IslandWar.Sim.Protocol;
using IslandWar.Sim.Research;

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
    private readonly Research _research;

    public Furnaces(ServerState state, Research research)
    {
        _state = state;
        _research = research;
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
            if (kind.Smelter is not { } smelter || furnace.IsBlueprint())
            {
                continue;
            }

            bool allowed(SmeltingRecipe recipe) => recipe.SmeltsIn(furnace.Kind) && _research.Unlocked(furnace.Team, Unlock.Smelting(recipe.Makes));
            if (Smelt(furnace, smelter, _state.Data.Items, elapsedMs, allowed) is { } after)
            {
                _state.Objects.Set(after);
                changed.Add(after);
                if (RecipeIn(furnace, _state.Data.Items, allowed) is { } recipe
                    && ItemCounts.Contents(after, ItemCounts.OutputSlot).Of(recipe.Makes) > ItemCounts.Contents(furnace, ItemCounts.OutputSlot).Of(recipe.Makes))
                {
                    _research.Earn(furnace.Team, PointSource.Smelt, recipe.Makes, recipe.Count);
                }
            }
        }

        if (changed.Count > 0)
        {
            _state.Net.BroadcastObjects(WireFormat.EncodeObjects(changed));
        }
    }

    /// <summary>
    /// What a furnace would smelt now: the first recipe (in data order) its team may use whose ingredients are
    /// all in its input, or null.
    /// </summary>
    public static SmeltingRecipe? RecipeIn(GameObject furnace, ItemSettings items, Func<SmeltingRecipe, bool>? allowed = null)
    {
        IReadOnlyDictionary<string, int> input = ItemCounts.Contents(furnace, ItemCounts.InputSlot);
        return items.Smelting.FirstOrDefault(r => input.Covers(r.Takes) && (allowed?.Invoke(r) ?? true));
    }

    /// <summary>
    /// One step of smelting <paramref name="elapsedMs"/> long: the furnace as it is after, or null if nothing
    /// happened (nothing it may smelt, no fuel, or no room for the result).
    /// </summary>
    /// <param name="allowed">Whether the furnace's team may use a recipe (bronze needs research); null for all.</param>
    public static GameObject? Smelt(GameObject furnace, SmelterSpec smelter, ItemSettings items, int elapsedMs, Func<SmeltingRecipe, bool>? allowed = null)
    {
        SmeltingRecipe? recipe = RecipeIn(furnace, items, allowed);
        if (recipe is null || ItemCounts.Contents(furnace, ItemCounts.OutputSlot).Total() + recipe.Count > smelter.SlotCapacity)
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
            foreach ((string item, int count) in recipe.Takes)
            {
                after = ItemCounts.WithSlot(after, ItemCounts.InputSlot, item, -count);
            }

            after = ItemCounts.WithSlot(after, ItemCounts.OutputSlot, recipe.Makes, recipe.Count);
            progress = 0;
        }

        return after.With(BurnValue, Math.Max(0, burn)).With(ProgressValue, progress);
    }
}
