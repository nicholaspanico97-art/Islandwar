using System.Numerics;
using IslandWar.Sim.Items;
using IslandWar.Sim.Players;
using IslandWar.Sim.Research;

namespace IslandWar.Sim.Server;

/// <summary>
/// Crafting: a recipe made by hand anywhere, or at a station structure of the player's team within reach.
/// Ingredients come from the backpack first, then the team's storage; the result goes into the backpack.
/// </summary>
public sealed class Crafting
{
    private readonly ServerState _state;
    private readonly Inventory _inventory;
    private readonly Research _research;

    public Crafting(ServerState state, Inventory inventory, Research research)
    {
        _state = state;
        _inventory = inventory;
        _research = research;
    }

    /// <param name="stationId">The structure it's made at, or 0 for by hand.</param>
    public void CraftRequested(int peer, string recipeId, int stationId)
    {
        if (!_state.Players.TryGetValue(peer, out ServerPlayer? player))
        {
            return;
        }

        Recipe? recipe = _state.Data.Items.RecipeFor(recipeId);
        Payment? payment = recipe is null || _state.Creative ? null : _inventory.Plan(player, recipe.Takes);
        int makes = recipe?.Makes.Values.Sum() ?? 0;
        string? reason = recipe is null ? "unknown recipe"
            : player.Pawn.InPlane ? "flying a plane"
            : !_research.Unlocked(player.Team, Unlock.Recipe(recipe.Id)) ? _research.Needs(Unlock.Recipe(recipe.Id))
            : !recipe.ByHand && !AtStation(player, recipe.Station, stationId) ? $"not at a {recipe.Station}"
            : !_state.Creative && payment is null ? "can't afford it"
            : player.Backpack.Total() - (payment?.FromBackpackTotal ?? 0) + makes > _state.Data.Items.BackpackCapacity ? "no room in the backpack"
            : null;
        if (reason is not null)
        {
            _state.Log($"[server] rejected peer {peer} crafting {recipeId}: {reason}");
            return;
        }

        if (payment is not null)
        {
            _inventory.Pay(player, payment);
        }

        foreach ((string item, int count) in recipe!.Makes)
        {
            _inventory.Give(player, item, count);
        }

        _state.Log($"[server] '{player.Name}' crafted {recipe.Id}");
    }

    private bool AtStation(ServerPlayer player, string kind, int stationId)
    {
        if (_state.Objects.Get(stationId) is not { } station || station.Kind != kind || station.Team != player.Team)
        {
            return false;
        }

        Vector3 eye = player.Pawn.Player.Position + Vector3.UnitY * _state.Data.Player.EyeHeight;
        return station.Footprint(_state.Objects.KindOf(station)).Any(b => BuildRules.InReach(_state.Data.Player, eye, b));
    }
}
