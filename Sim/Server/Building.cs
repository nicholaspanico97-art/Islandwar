using System.Numerics;
using IslandWar.Sim.Items;
using IslandWar.Sim.Match;
using IslandWar.Sim.Players;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Server;

/// <summary>Block edits players ask for: checked against the build rules and team protection, then replicated.</summary>
public sealed class Building
{
    private readonly ServerState _state;
    private readonly Vehicles _vehicles;
    private readonly Inventory _inventory;

    public Building(ServerState state, Vehicles vehicles, Inventory inventory)
    {
        _state = state;
        _vehicles = vehicles;
        _inventory = inventory;
    }

    public void EditRequested(int peer, VoxelEdit edit)
    {
        if (!_state.Players.TryGetValue(peer, out ServerPlayer? player))
        {
            return;
        }

        // The server decides who placed it; whatever the client sent about owners is ignored.
        VoxelWorld world = _state.World;
        edit = edit with { Owner = edit.Block == Block.Air ? default : new BlockOwner(player.PlayerId, player.Team) };
        Block before = world.InBounds(edit.Position) ? world.Get(edit.Position) : Block.Air;
        if (Allowed(player, edit, out string? reason) && world.ApplyEdit(edit))
        {
            _state.Net.BroadcastEdit(edit);
            _state.Log($"[server] peer {peer} set {edit.Position} to {edit.Block}");
            if (!_state.Creative)
            {
                Gather(player, before, edit);
            }

            return;
        }

        // Undo the client's prediction by telling it what's really there.
        if (world.InBounds(edit.Position))
        {
            _state.Net.SendEdit(peer, new VoxelEdit(edit.Position, world.Get(edit.Position), world.OwnerOf(edit.Position)));
        }

        if (reason is not null)
        {
            _state.Log($"[server] rejected peer {peer} setting {edit.Position} to {edit.Block}: {reason}");
        }
    }

    /// <summary>
    /// Mining gives the block's item if it fits in the backpack (otherwise it's lost); placing uses one up.
    /// <see cref="Allowed"/> has already checked there was one to place.
    /// </summary>
    private void Gather(ServerPlayer player, Block before, VoxelEdit edit)
    {
        ItemSettings items = _state.Data.Items;
        if (edit.Block == Block.Air)
        {
            player.LastMinedSeconds = _state.Seconds;
            if (items.RuleFor(before)?.Drops is { } drop && _inventory.Give(player, drop, 1) == 0)
            {
                _state.Log($"[server] '{player.Name}' mined {before} with a full backpack: the {drop} is lost");
            }
        }
        else if (items.ItemPlacing(edit.Block) is { } item)
        {
            _inventory.Take(player, item.Id, 1);
        }
    }

    /// <summary>Whether a mine comes no sooner after the last one than the block's hardness allows.</summary>
    private bool MinedInTime(ServerPlayer player, Block block)
    {
        float hardness = _state.Data.Items.RuleFor(block)?.Hardness ?? 0;
        return _state.Seconds - player.LastMinedSeconds >= hardness - _state.Data.Items.MiningSlackSeconds;
    }

    private bool HasItemFor(ServerPlayer player, Block block) =>
        _state.Data.Items.ItemPlacing(block) is { } item && player.Backpack.Of(item.Id) > 0;

    private bool Allowed(ServerPlayer player, VoxelEdit edit, out string? reason)
    {
        VoxelWorld world = _state.World;
        PlayerSettings settings = _state.Data.Player;
        PlayerState body = player.Pawn.Player;
        Vector3 eye = body.Position + Vector3.UnitY * settings.EyeHeight;
        IEnumerable<Vector3> blockers = _state.Players.Values
            .Where(p => !p.Pawn.InPlane && !p.Pawn.Player.Flying)
            .Select(p => p.Pawn.Player.Position);

        reason = !Enum.IsDefined(edit.Block) ? "unknown block type"
            : player.Pawn.InPlane ? "flying a plane"
            : !player.TrySpendEdit() ? "too many edits"
            : !BuildRules.InReach(settings, eye, edit.Position) ? "out of reach"
            : edit.Block == Block.Air && !BuildRules.CanBreak(world, edit.Position) ? "can't break that"
            : edit.Block == Block.Air && !Protection.MayBreak(world.OwnerOf(edit.Position), player.PlayerId, player.Team, _state.IsOnline) ? "a teammate built that"
            : edit.Block == Block.Air && !_state.Creative && !MinedInTime(player, world.Get(edit.Position)) ? "mining too fast"
            : edit.Block != Block.Air && !_state.Creative && !HasItemFor(player, edit.Block) ? "nothing to place"
            : edit.Block != Block.Air && !BuildRules.CanPlace(world, settings, edit.Position, edit.Block, blockers) ? "can't place there"
            : edit.Block != Block.Air && _vehicles.AnyPlaneOverlaps(edit.Position) ? "a plane is in the way"
            : null;
        return reason is null;
    }
}
