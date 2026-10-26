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
    /// Moves up to <paramref name="count"/> of an item between a player's backpack and one of their team's
    /// storage structures, as much as is there and as fits on the other side.
    /// </summary>
    public void TransferRequested(int peer, int storageId, string item, int count, bool intoStorage)
    {
        if (!_state.Players.TryGetValue(peer, out ServerPlayer? player) || _state.Objects.Get(storageId) is not { } storage || count <= 0)
        {
            return;
        }

        ObjectKind kind = _state.Objects.KindOf(storage);
        IReadOnlyDictionary<string, int> stored = ItemCounts.Contents(storage);
        string? reason = !kind.IsStorage ? "not storage"
            : storage.Team != player.Team ? "the other team's"
            : player.Pawn.InPlane ? "flying a plane"
            : !InReach(player, storage, kind) ? "out of reach"
            : Items.Item(item) is null ? "unknown item"
            : null;
        if (reason is not null)
        {
            _state.Log($"[server] rejected peer {peer} moving {item} {(intoStorage ? "into" : "out of")} object {storageId}: {reason}");
            return;
        }

        int moved = intoStorage
            ? Math.Min(Math.Min(count, player.Backpack.Of(item)), kind.Capacity - stored.Total())
            : Math.Min(Math.Min(count, stored.Of(item)), Items.BackpackCapacity - player.Backpack.Total());
        if (moved <= 0)
        {
            return;
        }

        player.Backpack = player.Backpack.Plus(item, intoStorage ? -moved : moved);
        GameObject changed = ItemCounts.WithStored(storage, item, intoStorage ? moved : -moved);
        _state.Objects.Set(changed);
        _state.Net.BroadcastObjects(WireFormat.EncodeObjects(new[] { changed }));
        SendBackpack(player);
        RecountStock(player.Team);
        _state.Log($"[server] '{player.Name}' moved {moved} {item} {(intoStorage ? "into" : "out of")} {kind.Name} {storage.Id}");
    }

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

    /// <summary>Everyone starts a match with an empty backpack.</summary>
    public void Reset()
    {
        foreach (ServerPlayer player in _state.Players.Values)
        {
            player.Backpack = ItemCounts.None;
            SendBackpack(player);
        }
    }

    private IEnumerable<GameObject> TeamStorage(Team team) =>
        _state.Objects.All.Where(o => o.Team == team && _state.Objects.KindOf(o).IsStorage);

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
