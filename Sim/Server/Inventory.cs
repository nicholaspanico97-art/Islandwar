using System.Numerics;
using IslandWar.Sim.Items;
using IslandWar.Sim.Match;
using IslandWar.Sim.Objects;
using IslandWar.Sim.Players;
using IslandWar.Sim.Protocol;

namespace IslandWar.Sim.Server;

/// <summary>
/// What players carry and what teams store: each player's backpack (sent only to them), moving items
/// between a backpack and a team storage structure, and each team's stock, which is everything in its
/// storage structures added up.
/// </summary>
public sealed class Inventory
{
    private readonly ServerState _state;
    private readonly TeamStates _teams;

    /// <summary>Set once the structures exist, to take away dropped backpacks once they're emptied.</summary>
    public Structures? Structures { get; set; }

    public Inventory(ServerState state, TeamStates teams)
    {
        _state = state;
        _teams = teams;
    }

    private ItemSettings Items => _state.Data.Items;

    /// <summary>Adds as many of an item as fit in the backpack and tells the player; returns how many fitted.</summary>
    public int Give(ServerPlayer player, string item, int count)
    {
        int fits = Math.Min(count, Items.BackpackCapacity - player.Backpack.Total());
        if (fits > 0)
        {
            player.Backpack = player.Backpack.Plus(item, fits);
            SendBackpack(player);
        }

        return fits;
    }

    /// <summary>Takes items out of the backpack if it holds enough, and tells the player.</summary>
    public bool Take(ServerPlayer player, string item, int count)
    {
        if (player.Backpack.Of(item) < count)
        {
            return false;
        }

        player.Backpack = player.Backpack.Plus(item, -count);
        SendBackpack(player);
        return true;
    }

    public void SendBackpack(ServerPlayer player) => _state.Net.SendBackpack(player.Peer, WireFormat.EncodeCounts(player.Backpack));

    /// <summary>
    /// Moves up to <paramref name="count"/> of an item between a player's backpack and a slot of one of their
    /// team's structures (a storage's <c>item</c> slot, or a furnace's input, fuel or output), as much as is
    /// there and as fits on the other side. Delivering to a blueprint or a dock (its <c>delivered</c> slot) only
    /// goes in, only what it still needs, and builds the structure or launches the ship once nothing is missing
    /// (in creative, the first delivery does, with nothing needed).
    /// </summary>
    public void TransferRequested(int peer, int objectId, string slot, string item, int count, bool intoStructure)
    {
        if (!_state.Players.TryGetValue(peer, out ServerPlayer? player) || _state.Objects.Get(objectId) is not { } target || count <= 0)
        {
            return;
        }

        ObjectKind kind = _state.Objects.KindOf(target);
        IReadOnlyDictionary<string, int> held = ItemCounts.Contents(target, slot);
        string? reason = target.Team != player.Team && !kind.Loot ? "the other team's"
            : player.Dead ? "dead"
            : intoStructure && kind.Loot ? "nothing goes into a dropped backpack"
            : player.Pawn.InPlane ? "flying a plane"
            : !InReach(player, target, kind) ? "out of reach"
            : Items.Item(item) is null ? "unknown item"
            : target.IsBlueprint() && slot != Blueprint.DeliveredSlot ? "it isn't built yet"
            : slot == Blueprint.DeliveredSlot && Blueprint.Needs(target, kind, _state.Objects.Kinds) is null ? "it isn't being built"
            : slot == Blueprint.DeliveredSlot && !intoStructure ? "deliveries stay in"
            : slot == Blueprint.DeliveredSlot && _state.Creative ? null
            : slot == Blueprint.DeliveredSlot && Blueprint.Missing(target, kind, _state.Objects.Kinds).Of(item) == 0 ? $"it doesn't need {item}"
            : slot != Blueprint.DeliveredSlot && SlotCapacity(kind, slot) is null ? $"no {slot} slot"
            : intoStructure && !Accepts(slot, item, kind) ? $"{item} doesn't go in the {slot} of a {kind.Name}"
            : intoStructure && slot is ItemCounts.InputSlot or ItemCounts.Input2Slot && held.Keys.Any(k => k != item) ? $"the {slot} already holds something else"
            : null;
        if (reason is not null)
        {
            _state.Log($"[server] rejected peer {peer} moving {item} {(intoStructure ? "into" : "out of")} the {slot} of object {objectId}: {reason}");
            return;
        }

        if (slot == Blueprint.DeliveredSlot && _state.Creative)
        {
            // Creative needs nothing: asking is enough.
            Structures?.Complete(target);
            return;
        }

        int room = slot == Blueprint.DeliveredSlot ? Blueprint.Missing(target, kind, _state.Objects.Kinds).Of(item) : SlotCapacity(kind, slot)!.Value - held.Total();
        int moved = intoStructure
            ? Math.Min(Math.Min(count, player.Backpack.Of(item)), room)
            : Math.Min(Math.Min(count, held.Of(item)), Items.BackpackCapacity - player.Backpack.Total());
        if (moved <= 0)
        {
            return;
        }

        player.Backpack = player.Backpack.Plus(item, intoStructure ? -moved : moved);
        GameObject changed = ItemCounts.WithSlot(target, slot, item, intoStructure ? moved : -moved);
        _state.Objects.Set(changed);
        _state.Net.BroadcastObjects(WireFormat.EncodeObjects(new[] { changed }));
        SendBackpack(player);
        _state.Log($"[server] '{player.Name}' moved {moved} {item} {(intoStructure ? "into" : "out of")} {kind.Name} {target.Id} ({slot})");
        if (slot == Blueprint.DeliveredSlot && Blueprint.Missing(changed, kind, _state.Objects.Kinds).Count == 0)
        {
            Structures?.Complete(changed);
        }
        else if (kind.Loot && !ItemCounts.HasItems(changed))
        {
            Structures?.Remove(changed);
        }
        else if (kind.IsStorage)
        {
            RecountStock(player.Team);
        }
    }

    /// <summary>How many items a slot of a kind of structure holds, or null if it has no such slot.</summary>
    public static int? SlotCapacity(ObjectKind kind, string slot) => slot switch
    {
        ItemCounts.StorageSlot when kind.IsStorage => kind.Capacity,
        ItemCounts.InputSlot or ItemCounts.Input2Slot or ItemCounts.FuelSlot or ItemCounts.OutputSlot when kind.Smelter is { } smelter => smelter.SlotCapacity,
        _ => null,
    };

    /// <summary>What a player may put in a slot: anything in storage, things that smelt (in that kind of furnace) or burn, nothing in its output.</summary>
    private bool Accepts(string slot, string item, ObjectKind kind) => slot switch
    {
        ItemCounts.InputSlot or ItemCounts.Input2Slot => Items.SmeltingFor(item) is { } recipe && recipe.SmeltsIn(kind.Id),
        ItemCounts.FuelSlot => Items.IsFuel(item),
        ItemCounts.OutputSlot => false,
        _ => true,
    };

    /// <summary>Everything a player can spend: their backpack and their team's storage.</summary>
    public IReadOnlyDictionary<string, int> Available(ServerPlayer player) => player.Backpack.Sum(_teams.Of(player.Team).Stock);

    /// <summary>
    /// How a cost would be paid: from the backpack first, then from the team's storage structures in id order.
    /// Null if the player and their team can't afford it.
    /// </summary>
    public Payment? Plan(ServerPlayer player, IReadOnlyDictionary<string, int> cost)
    {
        if (!Available(player).Covers(cost))
        {
            return null;
        }

        var fromBackpack = new Dictionary<string, int>();
        var fromStorage = new List<(GameObject Storage, string Item, int Count)>();
        foreach ((string item, int needed) in cost)
        {
            int own = Math.Min(needed, player.Backpack.Of(item));
            if (own > 0)
            {
                fromBackpack[item] = own;
            }

            int rest = needed - own;
            foreach (GameObject storage in TeamStorage(player.Team))
            {
                if (rest == 0)
                {
                    break;
                }

                int taken = Math.Min(rest, ItemCounts.Contents(storage).Of(item));
                if (taken > 0)
                {
                    fromStorage.Add((storage, item, taken));
                    rest -= taken;
                }
            }
        }

        return new Payment(fromBackpack, fromStorage);
    }

    /// <summary>Takes a planned payment and tells everyone concerned.</summary>
    public void Pay(ServerPlayer player, Payment payment)
    {
        foreach ((string item, int count) in payment.FromBackpack)
        {
            player.Backpack = player.Backpack.Plus(item, -count);
        }

        foreach (int id in payment.FromStorage.Select(s => s.Storage.Id).Distinct())
        {
            GameObject storage = _state.Objects.Get(id)!;
            foreach ((_, string item, int count) in payment.FromStorage.Where(s => s.Storage.Id == id))
            {
                storage = ItemCounts.WithStored(storage, item, -count);
            }

            _state.Objects.Set(storage);
            _state.Net.BroadcastObjects(WireFormat.EncodeObjects(new[] { storage }));
        }

        SendBackpack(player);
        if (payment.FromStorage.Count > 0)
        {
            RecountStock(player.Team);
        }
    }

    /// <summary>Gives items back into the backpack, as many as fit; returns what didn't.</summary>
    public IReadOnlyDictionary<string, int> Refund(ServerPlayer player, IReadOnlyDictionary<string, int> items)
    {
        IReadOnlyDictionary<string, int> lost = ItemCounts.None;
        foreach ((string item, int count) in items)
        {
            int fitted = Give(player, item, count);
            lost = lost.Plus(item, count - fitted);
        }

        return lost;
    }

    /// <summary>A team's stock is what its storage structures hold; call after any of them changes or goes.</summary>
    public void RecountStock(Team team)
    {
        IReadOnlyDictionary<string, int> stock = TeamStorage(team)
            .Aggregate(ItemCounts.None, (total, o) => total.Sum(ItemCounts.Contents(o)));
        if (!stock.SequenceEqual(_teams.Of(team).Stock))
        {
            _teams.Change(team, s => s with { Stock = stock });
        }
    }

    /// <summary>
    /// Developer mode only: puts items in a player's backpack out of thin air (as many as fit), or takes them
    /// away with a negative count. For testing.
    /// </summary>
    public void GiveRequested(int peer, string item, int count)
    {
        if (!_state.Players.TryGetValue(peer, out ServerPlayer? player))
        {
            return;
        }

        string? reason = !_state.Options.Dev ? "not in developer mode" : Items.Item(item) is null ? "unknown item" : count == 0 ? "nothing asked for" : null;
        if (reason is not null)
        {
            _state.Log($"[server] rejected peer {peer} asking for {count} {item}: {reason}");
            return;
        }

        if (count > 0)
        {
            _state.Log($"[server] developer mode: '{player.Name}' got {Give(player, item, count)} {item}");
            return;
        }

        int gone = Math.Min(-count, player.Backpack.Of(item));
        Take(player, item, gone);
        _state.Log($"[server] developer mode: '{player.Name}' dropped {gone} {item}");
    }

    /// <summary>Everyone starts a match with an empty backpack (or a playtest preset's starting kit).</summary>
    public void Reset()
    {
        foreach (ServerPlayer player in _state.Players.Values)
        {
            player.Backpack = StartingBackpack;
            SendBackpack(player);
        }
    }

    /// <summary>What a player carries on joining or at a match's start: nothing, or a playtest preset's kit.</summary>
    public IReadOnlyDictionary<string, int> StartingBackpack => ItemCounts.None.Sum(_state.Preset.StartBackpack);

    /// <summary>A team's built storage structures; a dropped backpack isn't one, whoever's it was.</summary>
    private IEnumerable<GameObject> TeamStorage(Team team) =>
        _state.Objects.All.Where(o => o.Team == team && !o.IsBlueprint() && _state.Objects.KindOf(o) is { IsStorage: true, Loot: false });

    private bool InReach(ServerPlayer player, GameObject storage, ObjectKind kind)
    {
        Vector3 eye = player.Pawn.Player.Position + Vector3.UnitY * _state.Data.Player.EyeHeight;
        return storage.Footprint(kind).Any(b => BuildRules.InReach(_state.Data.Player, eye, b));
    }
}

/// <summary>How a cost is to be paid: some from the backpack, the rest from team storage structures.</summary>
public sealed record Payment(IReadOnlyDictionary<string, int> FromBackpack, IReadOnlyList<(GameObject Storage, string Item, int Count)> FromStorage)
{
    public int FromBackpackTotal => FromBackpack.Values.Sum();
}
