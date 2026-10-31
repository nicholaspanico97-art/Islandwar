using System;
using Godot;
using IslandWar.Sim;
using IslandWar.Sim.Match;
using IslandWar.Sim.Protocol;
using IslandWar.Sim.Voxels;
using IslandWar.Sim.Weapons;

namespace IslandWar.Net;

/// <summary>
/// The one node that sends and receives game messages. Godot calls an RPC on the node at the same path on
/// the other side, so server and client both create this at <c>/root/Main/Game/Replicator</c>. Methods
/// here only move bytes; ServerMain and NetClient decide what they mean. Anything a client sends is
/// untrusted, so its receivers check that they run on the server and report the sender's peer id.
/// </summary>
public partial class Replicator : Node
{
    public const int ServerId = 1;

    // Reliable messages (world, edits, hashes) share channel 0, so a hash reply arrives after every edit
    // sent before it. Inputs and snapshots are frequent and replaced by the next one, so they're unreliable.
    private const int InputChannel = 1;
    private const int SnapshotChannel = 2;

    /// <summary>Server: a client's input packet (peer id, bytes).</summary>
    public event Action<int, byte[]>? InputsReceived;

    /// <summary>Server: a client asks for a block change.</summary>
    public event Action<int, VoxelEdit>? EditRequested;

    /// <summary>Server: a client asks for the world hash (the sync test uses this).</summary>
    public event Action<int>? HashRequested;

    /// <summary>
    /// Server: a newly connected client introduces itself (peer id, requested name, requested team, and its
    /// identity token, which lets a player who drops out get their seat back).
    /// </summary>
    public event Action<int, string, Team, string>? HelloReceived;

    /// <summary>Server: a player sets or clears their vote to concede.</summary>
    public event Action<int, bool>? ConcedeVoteReceived;

    /// <summary>Server: a player asks to place a structure (peer id, kind, footprint origin, facing).</summary>
    public event Action<int, string, BlockPos, int>? PlaceRequested;

    /// <summary>Server: a player asks to remove a structure (peer id, object id).</summary>
    public event Action<int, int>? RemoveRequested;

    /// <summary>Server: a player moves items between their backpack and a structure's slot (peer id, object id, slot, item, count, into the structure).</summary>
    public event Action<int, int, string, string, int, bool>? TransferRequested;

    /// <summary>Server: a player asks to craft (peer id, recipe id, station object id or 0 by hand).</summary>
    public event Action<int, string, int>? CraftRequested;

    /// <summary>Server: a player asks to research a tech for their team (peer id, tech id).</summary>
    public event Action<int, string>? ResearchRequested;

    /// <summary>Server: developer mode, a player asks for items (peer id, item id, count; negative takes away).</summary>
    public event Action<int, string, int>? GiveRequested;

    /// <summary>Client: everyone's name and team, sent whenever it changes.</summary>
    public event Action<byte[]>? RosterReceived;

    /// <summary>Client: the server is about to disconnect us, and why.</summary>
    public event Action<string>? Kicked;

    /// <summary>Client: the world on joining.</summary>
    public event Action<WorldInit>? WorldReceived;

    /// <summary>Client: an authoritative block value from the server.</summary>
    public event Action<VoxelEdit>? EditReceived;

    /// <summary>Client: a snapshot of every player.</summary>
    public event Action<byte[]>? SnapshotReceived;

    /// <summary>Client: the server's world hash.</summary>
    public event Action<ulong>? HashReceived;

    /// <summary>Client: someone dropped a bomb.</summary>
    public event Action<BombDrop>? BombDropped;

    /// <summary>Client: a bomb exploded, with its crater.</summary>
    public event Action<Explosion>? ExplosionReceived;

    /// <summary>Client: an arrow left a bow.</summary>
    public event Action<ArrowShot>? ArrowShotReceived;

    /// <summary>Client: an arrow stopped.</summary>
    public event Action<ArrowLanded>? ArrowLandedReceived;

    /// <summary>Client: objects that appeared or changed (packed).</summary>
    public event Action<byte[]>? ObjectsReceived;

    /// <summary>Client: an object is gone.</summary>
    public event Action<int>? ObjectRemoved;

    /// <summary>Client: our team's state (packed).</summary>
    public event Action<byte[]>? TeamStateReceived;

    /// <summary>Client: our backpack (packed).</summary>
    public event Action<byte[]>? BackpackReceived;

    public override void _Ready()
    {
        Name = "Replicator";
    }

    // ---- Client to server ----

    public void SendInputs(byte[] data) => RpcId(ServerId, MethodName.ReceiveInputs, data);

    public void RequestEdit(VoxelEdit edit) =>
        RpcId(ServerId, MethodName.ReceiveEditRequest, edit.Position.X, edit.Position.Y, edit.Position.Z, (int)edit.Block);

    public void RequestHash() => RpcId(ServerId, MethodName.ReceiveHashRequest);

    public void SayHello(string name, Team team, string token) => RpcId(ServerId, MethodName.ReceiveHello, name, (int)team, token);

    public void SetConcedeVote(bool vote) => RpcId(ServerId, MethodName.ReceiveConcedeVote, vote);

    public void RequestPlace(string kind, BlockPos origin, int facing) =>
        RpcId(ServerId, MethodName.ReceivePlaceRequest, kind, origin.X, origin.Y, origin.Z, facing);

    public void RequestRemove(int objectId) => RpcId(ServerId, MethodName.ReceiveRemoveRequest, objectId);

    public void RequestCraft(string recipeId, int stationId) => RpcId(ServerId, MethodName.ReceiveCraftRequest, recipeId, stationId);

    public void RequestResearch(string techId) => RpcId(ServerId, MethodName.ReceiveResearchRequest, techId);

    public void RequestGive(string item, int count) => RpcId(ServerId, MethodName.ReceiveGiveRequest, item, count);

    public void RequestTransfer(int objectId, string slot, string item, int count, bool intoStructure) =>
        RpcId(ServerId, MethodName.ReceiveTransferRequest, objectId, slot, item, count, intoStructure);

    // ---- Server to client ----

    public void SendWorld(int peer, WorldInit world) =>
        RpcId(peer, MethodName.ReceiveWorld, world.Seed, world.Data.TerrainJson, world.Data.PlayerJson, world.Data.NetJson, world.Data.PlaneJson,
            world.Data.WeaponsJson, world.Data.PresetsJson, world.Data.StructuresJson, world.Data.ItemsJson, world.Data.TechsJson, world.EditsData, world.ObjectsData, (long)world.Hash, world.DevMode, world.PresetName);

    public void BroadcastRoster(byte[] roster) => Rpc(MethodName.ReceiveRoster, roster);

    public void Kick(int peer, string reason) => RpcId(peer, MethodName.ReceiveKick, reason);

    // Drops and explosions are reliable and share channel 0 with edits, so a crater never arrives before
    // the edits that came first, and a hash reply follows every crater.
    public void BroadcastBombDrop(BombDrop drop) =>
        Rpc(MethodName.ReceiveBombDrop, drop.Id, drop.OwnerPeerId, (long)drop.Tick, ToGodot(drop.State.Position), ToGodot(drop.State.Velocity), drop.Nuke);

    public void BroadcastExplosion(Explosion explosion) =>
        Rpc(MethodName.ReceiveExplosion, explosion.BombId, ToGodot(explosion.Position), explosion.InWater, explosion.EditsData, explosion.CraterRadius, explosion.Nuke, explosion.Harmless);

    // Arrows share the reliable channel with edits too; they're few.
    public void BroadcastArrowShot(ArrowShot shot) =>
        Rpc(MethodName.ReceiveArrowShot, shot.Id, shot.OwnerPeerId, (long)shot.Tick, ToGodot(shot.State.Position), ToGodot(shot.State.Velocity));

    public void BroadcastArrowLanded(ArrowLanded landed) => Rpc(MethodName.ReceiveArrowLanded, landed.Id, ToGodot(landed.Position), landed.HitPeerId);

    /// <summary>Tells every client the block (and who placed it) at a position.</summary>
    public void BroadcastEdit(VoxelEdit edit) =>
        Rpc(MethodName.ReceiveEdit, edit.Position.X, edit.Position.Y, edit.Position.Z, (int)edit.Block, edit.Owner.PlayerId, (int)edit.Owner.Team);

    /// <summary>Tells one client the block at a position, to undo a prediction the server rejected.</summary>
    public void SendEdit(int peer, VoxelEdit edit) =>
        RpcId(peer, MethodName.ReceiveEdit, edit.Position.X, edit.Position.Y, edit.Position.Z, (int)edit.Block, edit.Owner.PlayerId, (int)edit.Owner.Team);

    public void SendSnapshot(int peer, byte[] data) => RpcId(peer, MethodName.ReceiveSnapshot, data);

    public void SendHash(int peer, ulong hash) => RpcId(peer, MethodName.ReceiveHash, (long)hash);

    // Objects share the reliable channel with edits: a structure's footprint blocks arrive before the object.
    public void BroadcastObjects(byte[] objects) => Rpc(MethodName.ReceiveObjects, objects);

    public void BroadcastObjectRemoved(int id) => Rpc(MethodName.ReceiveObjectRemoved, id);

    public void SendTeamState(int peer, byte[] state) => RpcId(peer, MethodName.ReceiveTeamState, state);

    public void SendBackpack(int peer, byte[] backpack) => RpcId(peer, MethodName.ReceiveBackpack, backpack);

    // ---- Receivers ----

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = InputChannel)]
    private void ReceiveInputs(byte[] data)
    {
        if (Multiplayer.IsServer())
        {
            InputsReceived?.Invoke(Multiplayer.GetRemoteSenderId(), data);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveEditRequest(int x, int y, int z, int block)
    {
        if (Multiplayer.IsServer())
        {
            EditRequested?.Invoke(Multiplayer.GetRemoteSenderId(), new VoxelEdit(new BlockPos(x, y, z), (Block)block));
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveHashRequest()
    {
        if (Multiplayer.IsServer())
        {
            HashRequested?.Invoke(Multiplayer.GetRemoteSenderId());
        }
    }

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveWorld(int seed, string terrainJson, string playerJson, string netJson, string planeJson, string weaponsJson, string presetsJson,
        string structuresJson, string itemsJson, string techsJson, byte[] edits, byte[] objects, long hash, bool devMode, string presetName) =>
        WorldReceived?.Invoke(new WorldInit(seed, new GameData(terrainJson, playerJson, netJson, planeJson, weaponsJson, presetsJson, structuresJson, itemsJson, techsJson),
            edits, objects, (ulong)hash, devMode, presetName));

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveHello(string name, int team, string token)
    {
        if (Multiplayer.IsServer())
        {
            HelloReceived?.Invoke(Multiplayer.GetRemoteSenderId(), name, Enum.IsDefined((Team)team) ? (Team)team : Team.None, token);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveConcedeVote(bool vote)
    {
        if (Multiplayer.IsServer())
        {
            ConcedeVoteReceived?.Invoke(Multiplayer.GetRemoteSenderId(), vote);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceivePlaceRequest(string kind, int x, int y, int z, int facing)
    {
        if (Multiplayer.IsServer())
        {
            PlaceRequested?.Invoke(Multiplayer.GetRemoteSenderId(), kind, new BlockPos(x, y, z), facing);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveRemoveRequest(int objectId)
    {
        if (Multiplayer.IsServer())
        {
            RemoveRequested?.Invoke(Multiplayer.GetRemoteSenderId(), objectId);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveTransferRequest(int objectId, string slot, string item, int count, bool intoStructure)
    {
        if (Multiplayer.IsServer())
        {
            TransferRequested?.Invoke(Multiplayer.GetRemoteSenderId(), objectId, slot, item, count, intoStructure);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveCraftRequest(string recipeId, int stationId)
    {
        if (Multiplayer.IsServer())
        {
            CraftRequested?.Invoke(Multiplayer.GetRemoteSenderId(), recipeId, stationId);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveResearchRequest(string techId)
    {
        if (Multiplayer.IsServer())
        {
            ResearchRequested?.Invoke(Multiplayer.GetRemoteSenderId(), techId);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveGiveRequest(string item, int count)
    {
        if (Multiplayer.IsServer())
        {
            GiveRequested?.Invoke(Multiplayer.GetRemoteSenderId(), item, count);
        }
    }

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveBackpack(byte[] backpack) => BackpackReceived?.Invoke(backpack);

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveObjects(byte[] objects) => ObjectsReceived?.Invoke(objects);

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveObjectRemoved(int id) => ObjectRemoved?.Invoke(id);

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveTeamState(byte[] state) => TeamStateReceived?.Invoke(state);

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveRoster(byte[] roster) => RosterReceived?.Invoke(roster);

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveKick(string reason) => Kicked?.Invoke(reason);

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveBombDrop(int id, int owner, long tick, Vector3 position, Vector3 velocity, bool nuke) =>
        BombDropped?.Invoke(new BombDrop(id, owner, (uint)tick, new BombState(ToSim(position), ToSim(velocity)), nuke));

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveExplosion(int bombId, Vector3 position, bool inWater, byte[] edits, int craterRadius, bool nuke, bool harmless) =>
        ExplosionReceived?.Invoke(new Explosion(bombId, ToSim(position), inWater, edits, craterRadius, nuke, harmless));

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveArrowShot(int id, int owner, long tick, Vector3 position, Vector3 velocity) =>
        ArrowShotReceived?.Invoke(new ArrowShot(id, owner, (uint)tick, new ArrowState(ToSim(position), ToSim(velocity))));

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveArrowLanded(int id, Vector3 position, int hitPeer) => ArrowLandedReceived?.Invoke(new ArrowLanded(id, ToSim(position), hitPeer));

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveEdit(int x, int y, int z, int block, int ownerId, int ownerTeam) =>
        EditReceived?.Invoke(new VoxelEdit(new BlockPos(x, y, z), (Block)block, new BlockOwner(ownerId, (Team)ownerTeam)));

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = SnapshotChannel)]
    private void ReceiveSnapshot(byte[] data) => SnapshotReceived?.Invoke(data);

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveHash(long hash) => HashReceived?.Invoke((ulong)hash);

    private static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    private static System.Numerics.Vector3 ToSim(Vector3 v) => new(v.X, v.Y, v.Z);
}
