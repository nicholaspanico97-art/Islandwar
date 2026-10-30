using IslandWar.Sim.Items;
using IslandWar.Sim.Objects;
using IslandWar.Sim.Protocol;
using IslandWar.Sim.Research;

namespace IslandWar.Sim.Server;

/// <summary>
/// Furnaces: twice a second each one heats while fuel burns and cools when it doesn't, and smelts what's in
/// its two input slots while it's at least as hot as the recipe needs. Fuel in the fuel slot is lit as soon
/// as the last item has burnt, whether or not anything is smelting, so a fed furnace stays hot. Once a
/// furnace is as hot as its fuel can make it, the fuel only burns as fast as needed to make up the heat it
/// loses, so a more efficient furnace uses less. Changed furnaces go out to everyone, like any object change.
/// </summary>
public sealed class Furnaces
{
    /// <summary>
    /// A furnace's values: milliseconds left on the item burning (flat out) and which fuel it is (its number),
    /// milliseconds of progress on the one smelting, and its heat above ambient in tenths of a degree.
    /// </summary>
    public const string BurnValue = "burn_ms";
    public const string BurningValue = "burn_fuel";
    public const string ProgressValue = "progress_ms";
    public const string HeatValue = "heat_dc";

    /// <summary>A furnace with no fire cools at least this fast (degrees per second), so it gets back to ambient.</summary>
    private const float MinCooling = 0.5f;

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

    /// <summary>A furnace's temperature in degrees C.</summary>
    public static float Temperature(GameObject furnace, ItemSettings items) => items.AmbientTemperature + furnace.Values.Of(HeatValue) / 10f;

    /// <summary>What's in a furnace's two input slots together.</summary>
    public static IReadOnlyDictionary<string, int> Inputs(GameObject furnace) =>
        ItemCounts.Contents(furnace, ItemCounts.InputSlot).Sum(ItemCounts.Contents(furnace, ItemCounts.Input2Slot));

    /// <summary>
    /// What a furnace would smelt now (hot enough or not): the first recipe (in data order) its team may use
    /// whose ingredients are all in its inputs, or null.
    /// </summary>
    public static SmeltingRecipe? RecipeIn(GameObject furnace, ItemSettings items, Func<SmeltingRecipe, bool>? allowed = null)
    {
        IReadOnlyDictionary<string, int> input = Inputs(furnace);
        return items.Smelting.FirstOrDefault(r => input.Covers(r.Takes) && (allowed?.Invoke(r) ?? true));
    }

    /// <summary>
    /// One step <paramref name="elapsedMs"/> long: heating or cooling, burning fuel, and smelting. The furnace as
    /// it is after, or null if nothing changed.
    /// </summary>
    /// <param name="allowed">Whether the furnace's team may use a recipe (bronze needs research); null for all.</param>
    public static GameObject? Smelt(GameObject furnace, SmelterSpec smelter, ItemSettings items, int elapsedMs, Func<SmeltingRecipe, bool>? allowed = null)
    {
        float seconds = elapsedMs / 1000f;
        float ambient = items.AmbientTemperature;
        float temperature = Temperature(furnace, items);
        GameObject after = furnace;

        // Light the next item of fuel once the last has burnt out.
        int burn = furnace.Values.Of(BurnValue);
        string? burning = burn > 0 ? items.FuelNumbered(furnace.Values.Of(BurningValue)) : null;
        if (burning is null && ItemCounts.Contents(furnace, ItemCounts.FuelSlot).Keys.FirstOrDefault(items.IsFuel) is { } fuel)
        {
            after = ItemCounts.WithSlot(after, ItemCounts.FuelSlot, fuel, -1);
            burning = fuel;
            burn = (int)Math.Round(items.Fuel[fuel].Seconds * 1000);
        }

        // Heat lost to the air, then as much heat from the fire as it takes to get back up to what the fuel can
        // reach. The fuel burns only for the share of the step its heat was needed.
        float above = temperature - ambient;
        float cooled = Math.Max(ambient, temperature - Math.Max(smelter.HeatLoss * above, burning is null ? MinCooling : 0) * seconds);
        float next = cooled;
        if (burning is not null)
        {
            FuelSpec spec = items.Fuel[burning];
            float hottest = Math.Min(smelter.MaxTemperature, spec.Flame);
            float gain = smelter.Efficiency * spec.Heat * seconds;
            if (cooled < hottest)
            {
                next = Math.Min(hottest, cooled + gain);
                burn -= (int)Math.Round(elapsedMs * (next - cooled) / gain);
            }
        }

        // Smelting moves on while it's hot enough and there's room for the result.
        int progress = furnace.Values.Of(ProgressValue);
        if (RecipeIn(after, items, allowed) is { } recipe && next >= recipe.Temperature
            && ItemCounts.Contents(after, ItemCounts.OutputSlot).Total() + recipe.Count <= smelter.SlotCapacity)
        {
            progress += elapsedMs;
            if (progress >= recipe.Seconds * 1000)
            {
                after = TakeInputs(after, recipe.Takes);
                after = ItemCounts.WithSlot(after, ItemCounts.OutputSlot, recipe.Makes, recipe.Count);
                progress = 0;
            }
        }

        bool lit = burning is not null && burn > 0;
        after = after
            .With(BurnValue, lit ? burn : 0)
            .With(BurningValue, lit ? items.FuelNumber(burning!) : 0)
            .With(ProgressValue, progress)
            .With(HeatValue, (int)Math.Round((next - ambient) * 10));
        return after.Values.SequenceEqual(furnace.Values) ? null : after;
    }

    /// <summary>A recipe's ingredients taken out of the inputs, from the first slot before the second.</summary>
    private static GameObject TakeInputs(GameObject furnace, IReadOnlyDictionary<string, int> takes)
    {
        foreach ((string item, int count) in takes)
        {
            int fromFirst = Math.Min(count, ItemCounts.Contents(furnace, ItemCounts.InputSlot).Of(item));
            furnace = ItemCounts.WithSlot(furnace, ItemCounts.InputSlot, item, -fromFirst);
            furnace = ItemCounts.WithSlot(furnace, ItemCounts.Input2Slot, item, -(count - fromFirst));
        }

        return furnace;
    }
}
