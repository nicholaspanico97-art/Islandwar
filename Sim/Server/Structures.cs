using System.Numerics;
using IslandWar.Sim.Items;
using IslandWar.Sim.Match;
using IslandWar.Sim.Objects;
using IslandWar.Sim.Players;
using IslandWar.Sim.Protocol;
using IslandWar.Sim.Research;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Server;

/// <summary>
/// Structures: placing and removing them on request, and bomb damage. A structure's footprint blocks travel
/// to clients as ordinary edits, before the object itself, so every client's world and objects agree.
/// </summary>
public sealed class Structures
{
    private readonly ServerState _state;
    private readonly Vehicles _vehicles;
    private readonly Research _research;

    /// <summary>Set once the inventory exists (it needs the team states, which need nothing).</summary>
    public Inventory? Inventory { get; set; }

    /// <summary>Object ids are never reused, even across matches.</summary>
    private int _nextId = 1;

    public Structures(ServerState state, Vehicles vehicles, Research research)
    {
        _state = state;
        _vehicles = vehicles;
        _research = research;
    }

    public void PlaceRequested(int peer, string kindId, BlockPos origin, int facing)
    {
        if (!_state.Players.TryGetValue(peer, out ServerPlayer? player))
        {
            return;
        }

        ObjectKind? kind = _state.Data.Kinds.Get(kindId);
        Payment? payment = kind is null || _state.Creative ? null : Inventory!.Plan(player, kind.Cost);
        string? reason = kind is null ? "unknown kind"
            : kind.Loot ? "players can't place that"
            : player.Dead ? "dead"
            : player.Pawn.InPlane ? "flying a plane"
            : !_research.Unlocked(player.Team, Unlock.Structure(kind.Id)) ? _research.Needs(Unlock.Structure(kind.Id))
            : !player.TrySpendEdit() ? "too many edits"
            : !InReach(player, GameObject.FootprintOf(kind, origin, facing)) ? "out of reach"
            : ObjectRules.WhyNot(_state.World, _state.Data.Player, kind, origin, facing, Blockers())
              ?? (GameObject.FootprintOf(kind, origin, facing).Any(_vehicles.AnyPlaneOverlaps) ? "a plane is in the way"
                  : !_state.Creative && payment is null ? "can't afford it" : null);
        if (reason is not null)
        {
            _state.Log($"[server] rejected peer {peer} placing {kindId} at {origin}: {reason}");
            return;
        }

        if (payment is not null)
        {
            Inventory!.Pay(player, payment);
        }

        var placed = new GameObject(_nextId++, kind!.Id, player.Team, player.PlayerId, origin, facing, kind.MaxHealth);
        foreach (BlockPos cell in placed.Footprint(kind))
        {
            Apply(new VoxelEdit(cell, Block.Structure, placed.Owner));
        }

        _state.Objects.Set(placed);
        _state.Net.BroadcastObjects(WireFormat.EncodeObjects(new[] { placed }));
        _state.Log($"[server] peer {peer} placed a {kind.Name} (object {placed.Id}) at {origin} for {player.Team}");
    }

    public void RemoveRequested(int peer, int objectId)
    {
        if (!_state.Players.TryGetValue(peer, out ServerPlayer? player) || _state.Objects.Get(objectId) is not { } target)
        {
            return;
        }

        ObjectKind kind = _state.Objects.KindOf(target);
        string? reason = player.Pawn.InPlane ? "flying a plane"
            : !player.TrySpendEdit() ? "too many edits"
            : !InReach(player, target.Footprint(kind)) ? "out of reach"
            : !Protection.MayBreak(target.Owner, player.PlayerId, player.Team, _state.IsOnline) ? "a teammate built that"
            : ItemCounts.HasItems(target) ? "it isn't empty"
            : !_state.Creative && _state.Seconds - player.LastMinedSeconds < _state.Data.Items.StructureHardness - _state.Data.Items.MiningSlackSeconds ? "taking it down too fast"
            : null;
        if (reason is not null)
        {
            _state.Log($"[server] rejected peer {peer} removing object {objectId}: {reason}");
            return;
        }

        player.LastMinedSeconds = _state.Seconds;
        foreach (VoxelEdit edit in Clear(target))
        {
            _state.Net.BroadcastEdit(edit);
        }

        _state.Net.BroadcastObjectRemoved(target.Id);
        _state.Log($"[server] peer {peer} removed {kind.Name} {target.Id}");

        // Taking a structure down by hand gives its materials back, as much as fits.
        if (!_state.Creative && Inventory!.Refund(player, kind.Cost) is { Count: > 0 } lost)
        {
            _state.Log($"[server] '{player.Name}' had no room for {string.Join(", ", lost.Select(l => $"{l.Value} {l.Key}"))} from the {kind.Name}");
        }
    }

    /// <summary>
    /// Leaves a dead player's items in a dropped backpack at <paramref name="cell"/>, or the first free cell
    /// up to two above it. Null if there's no room (the items are lost).
    /// </summary>
    public GameObject? DropLoot(BlockPos cell, ServerPlayer owner, IReadOnlyDictionary<string, int> items)
    {
        ObjectKind? kind = _state.Data.Kinds.All.FirstOrDefault(k => k.Loot);
        BlockPos? free = Enumerable.Range(0, 3).Select(up => cell with { Y = cell.Y + up })
            .Cast<BlockPos?>()
            .FirstOrDefault(c => _state.World.InBounds(c!.Value) && _state.World.Get(c.Value) == Block.Air);
        if (kind is null || free is not { } origin)
        {
            return null;
        }

        var bag = new GameObject(_nextId++, kind.Id, owner.Team, owner.PlayerId, origin, 0, kind.MaxHealth);
        foreach ((string item, int count) in items)
        {
            bag = ItemCounts.WithStored(bag, item, count);
        }

        Apply(new VoxelEdit(origin, Block.Structure, bag.Owner));
        _state.Objects.Set(bag);
        _state.Net.BroadcastObjects(WireFormat.EncodeObjects(new[] { bag }));
        return bag;
    }

    /// <summary>Takes an object away at once (an emptied dropped backpack) and tells everyone.</summary>
    public void Remove(GameObject target)
    {
        foreach (VoxelEdit edit in Clear(target))
        {
            _state.Net.BroadcastEdit(edit);
        }

        _state.Net.BroadcastObjectRemoved(target.Id);
    }

    /// <summary>
    /// Takes <paramref name="damage"/> off every structure with a block within <paramref name="radius"/> of
    /// <paramref name="centre"/>. Destroyed ones are cleared from the world at once; their footprints are
    /// returned to go out with the explosion, and <see cref="Announce"/> then tells clients about the objects.
    /// </summary>
    public BlastResult Blast(BlockPos centre, int radius, int damage)
    {
        long reach = (long)radius * radius + radius;
        var damaged = new List<GameObject>();
        var destroyed = new List<GameObject>();
        var edits = new List<VoxelEdit>();
        foreach (GameObject target in _state.Objects.All.ToList())
        {
            ObjectKind kind = _state.Objects.KindOf(target);
            if (!target.Footprint(kind).Any(b => Squared(b, centre) <= reach))
            {
                continue;
            }

            GameObject hurt = target with { Health = target.Health - damage };
            if (hurt.Health > 0)
            {
                _state.Objects.Set(hurt);
                damaged.Add(hurt);
                continue;
            }

            edits.AddRange(Clear(target));
            destroyed.Add(target);
        }

        return new BlastResult(damaged, destroyed, edits);
    }

    /// <summary>Sends what a blast did to objects, after the explosion message carrying the cleared blocks.</summary>
    public void Announce(BlastResult result)
    {
        if (result.Damaged.Count > 0)
        {
            _state.Net.BroadcastObjects(WireFormat.EncodeObjects(result.Damaged));
        }

        foreach (GameObject gone in result.Destroyed)
        {
            _state.Net.BroadcastObjectRemoved(gone.Id);
            _state.Log($"[server] {_state.Objects.Kinds.Get(gone.Kind)?.Name ?? gone.Kind} {gone.Id} ({gone.Team}) was destroyed");
        }

        // Whatever destroyed storage held is gone with it.
        foreach (Team team in result.Destroyed.Select(o => o.Team).Distinct())
        {
            Inventory?.RecountStock(team);
        }

        foreach (GameObject hurt in result.Damaged)
        {
            _state.Log($"[server] {_state.Objects.Kinds.Get(hurt.Kind)?.Name ?? hurt.Kind} {hurt.Id} ({hurt.Team}) is down to {hurt.Health} health");
        }
    }

    /// <summary>No objects (a fresh match; the new world has none of their blocks either).</summary>
    public void Reset() => _state.Objects.Clear();

    /// <summary>Removes an object and empties its footprint, returning the edits made.</summary>
    private List<VoxelEdit> Clear(GameObject target)
    {
        var edits = new List<VoxelEdit>();
        foreach (BlockPos cell in target.Footprint(_state.Objects.KindOf(target)))
        {
            var edit = new VoxelEdit(cell, Block.Air);
            if (_state.World.ApplyEdit(edit))
            {
                edits.Add(edit);
            }
        }

        _state.Objects.Remove(target.Id);
        return edits;
    }

    private void Apply(VoxelEdit edit)
    {
        if (_state.World.ApplyEdit(edit))
        {
            _state.Net.BroadcastEdit(edit);
        }
    }

    private bool InReach(ServerPlayer player, IEnumerable<BlockPos> footprint)
    {
        Vector3 eye = player.Pawn.Player.Position + Vector3.UnitY * _state.Data.Player.EyeHeight;
        return footprint.Any(b => BuildRules.InReach(_state.Data.Player, eye, b));
    }

    private IEnumerable<Vector3> Blockers() =>
        _state.Players.Values.Where(p => !p.Pawn.InPlane && !p.Pawn.Player.Flying).Select(p => p.Pawn.Player.Position);

    private static long Squared(BlockPos a, BlockPos b) =>
        (long)(a.X - b.X) * (a.X - b.X) + (long)(a.Y - b.Y) * (a.Y - b.Y) + (long)(a.Z - b.Z) * (a.Z - b.Z);
}

/// <summary>What one blast did to structures.</summary>
/// <param name="Edits">The destroyed structures' footprints, already cleared from the world.</param>
public sealed record BlastResult(IReadOnlyList<GameObject> Damaged, IReadOnlyList<GameObject> Destroyed, IReadOnlyList<VoxelEdit> Edits)
{
    public static readonly BlastResult None = new(Array.Empty<GameObject>(), Array.Empty<GameObject>(), Array.Empty<VoxelEdit>());
}
