using System.Numerics;
using IslandWar.Sim.Items;
using IslandWar.Sim.Match;
using IslandWar.Sim.Players;
using IslandWar.Sim.Research;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Server;

/// <summary>Block edits players ask for: checked against the build rules and team protection, then replicated.</summary>
public sealed class Building
{
    private readonly ServerState _state;
    private readonly Vehicles _vehicles;
    private readonly Inventory _inventory;
    private readonly Research _research;

    public Building(ServerState state, Vehicles vehicles, Inventory inventory, Research research)
    {
        _state = state;
        _vehicles = vehicles;
        _inventory = inventory;
        _research = research;
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
    /// Mining gives the block's item (more with a yield researched) as far as the backpack has room (the rest
    /// is lost); placing uses one up. <see cref="Allowed"/> has already checked there was one to place. Either
    /// may earn the team research points.
    /// </summary>
    private void Gather(ServerPlayer player, Block before, VoxelEdit edit)
    {
        ItemSettings items = _state.Data.Items;
        if (edit.Block == Block.Air)
        {
            player.LastMinedSeconds = _state.Seconds;
            if (items.RuleFor(before)?.Drops is { } drop)
            {
                int count = _research.Yield(player.Team, drop);
                if (_inventory.Give(player, drop, count) < count)
                {
                    _state.Log($"[server] '{player.Name}' mined {before} with a full backpack: {drop} lost");
                }
            }

            _research.Earn(player.Team, PointSource.Mine, BlockNames.Of(before));
        }
        else if (items.ItemPlacing(edit.Block) is { } item)
        {
            _inventory.Take(player, item.Id, 1);
            _research.Earn(player.Team, PointSource.Place, BlockNames.Of(edit.Block));
        }
    }

    /// <summary>Whether the player's team may place a block from the hotbar (stone needs Fortification).</summary>
    private bool MayPlace(ServerPlayer player, Block block) =>
        _state.Data.Items.ItemPlacing(block) is not { } item || _research.Unlocked(player.Team, Unlock.Place(item.Id));

    /// <summary>
    /// Whether a mine comes no sooner after the last one than the block's hardness allows with the best tool
    /// the player carries.
    /// </summary>
    private bool MinedInTime(ServerPlayer player, Block block, float seconds) =>
        _state.Seconds - player.LastMinedSeconds >= seconds - _state.Data.Items.MiningSlackSeconds;

    private float? MiningSeconds(ServerPlayer player, Block block) => _state.Data.Items.MiningSeconds(block, player.Backpack);

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
            : player.Dead ? "dead"
            : player.Pawn.InPlane ? "flying a plane"
            : !player.TrySpendEdit() ? "too many edits"
            : !BuildRules.InReach(settings, eye, edit.Position) ? "out of reach"
            : edit.Block == Block.Air && !BuildRules.CanBreak(world, edit.Position) ? "can't break that"
            : edit.Block == Block.Air && !Protection.MayBreak(world.OwnerOf(edit.Position), player.PlayerId, player.Team, _state.IsOnline) ? "a teammate built that"
            : edit.Block == Block.Air && !_state.Creative && MiningSeconds(player, world.Get(edit.Position)) is null ? "needs a better tool"
            : edit.Block == Block.Air && !_state.Creative && !MinedInTime(player, world.Get(edit.Position), MiningSeconds(player, world.Get(edit.Position))!.Value) ? "mining too fast"
            : edit.Block != Block.Air && !_state.Creative && !HasItemFor(player, edit.Block) ? "nothing to place"
            : edit.Block != Block.Air && !MayPlace(player, edit.Block) ? _research.Needs(Unlock.Place(_state.Data.Items.ItemPlacing(edit.Block)!.Id))
            : edit.Block != Block.Air && !BuildRules.CanPlace(world, settings, edit.Position, edit.Block, blockers) ? "can't place there"
            : edit.Block != Block.Air && _vehicles.AnyPlaneOverlaps(edit.Position) ? "a plane is in the way"
            : null;
        return reason is null;
    }
}
