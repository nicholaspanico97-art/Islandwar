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

    /// <summary>A team's stock is what its storage structures hold; call after any of them changes or goes.</summary>
    public void RecountStock(Team team)
    {
        IReadOnlyDictionary<string, int> stock = _state.Objects.All
            .Where(o => o.Team == team && _state.Objects.KindOf(o).IsStorage)
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

    private bool InReach(ServerPlayer player, GameObject storage, ObjectKind kind)
    {
        Vector3 eye = player.Pawn.Player.Position + Vector3.UnitY * _state.Data.Player.EyeHeight;
        return storage.Footprint(kind).Any(b => BuildRules.InReach(_state.Data.Player, eye, b));
    }
}
