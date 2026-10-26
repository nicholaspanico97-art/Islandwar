using System;
using Godot;
using IslandWar.Sim.Voxels;

namespace IslandWar.Net;

/// <summary>Everything a client needs to rebuild the current world when it joins.</summary>
/// <param name="EditsData">Every edit since generation, packed with <c>WireFormat.EncodeEdits</c>.</param>
/// <param name="Hash">The server's world hash, so the client can check its rebuild.</param>
public sealed record WorldInit(int Seed, GameData Data, byte[] EditsData, ulong Hash);

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

    /// <summary>Client: the world on joining.</summary>
    public event Action<WorldInit>? WorldReceived;

    /// <summary>Client: an authoritative block value from the server.</summary>
    public event Action<VoxelEdit>? EditReceived;

    /// <summary>Client: a snapshot of every player.</summary>
    public event Action<byte[]>? SnapshotReceived;

    /// <summary>Client: the server's world hash.</summary>
    public event Action<ulong>? HashReceived;

    public override void _Ready()
    {
        Name = "Replicator";
    }

    // ---- Client to server ----

    public void SendInputs(byte[] data) => RpcId(ServerId, MethodName.ReceiveInputs, data);

    public void RequestEdit(VoxelEdit edit) =>
        RpcId(ServerId, MethodName.ReceiveEditRequest, edit.Position.X, edit.Position.Y, edit.Position.Z, (int)edit.Block);

    public void RequestHash() => RpcId(ServerId, MethodName.ReceiveHashRequest);

    // ---- Server to client ----

    public void SendWorld(int peer, WorldInit world) =>
        RpcId(peer, MethodName.ReceiveWorld, world.Seed, world.Data.TerrainJson, world.Data.PlayerJson, world.Data.NetJson, world.Data.PlaneJson, world.EditsData, (long)world.Hash);

    /// <summary>Tells every client the block at a position.</summary>
    public void BroadcastEdit(VoxelEdit edit) =>
        Rpc(MethodName.ReceiveEdit, edit.Position.X, edit.Position.Y, edit.Position.Z, (int)edit.Block);

    /// <summary>Tells one client the block at a position, to undo a prediction the server rejected.</summary>
    public void SendEdit(int peer, VoxelEdit edit) =>
        RpcId(peer, MethodName.ReceiveEdit, edit.Position.X, edit.Position.Y, edit.Position.Z, (int)edit.Block);

    public void SendSnapshot(int peer, byte[] data) => RpcId(peer, MethodName.ReceiveSnapshot, data);

    public void SendHash(int peer, ulong hash) => RpcId(peer, MethodName.ReceiveHash, (long)hash);

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
    private void ReceiveWorld(int seed, string terrainJson, string playerJson, string netJson, string planeJson, byte[] edits, long hash) =>
        WorldReceived?.Invoke(new WorldInit(seed, new GameData(terrainJson, playerJson, netJson, planeJson), edits, (ulong)hash));

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveEdit(int x, int y, int z, int block) =>
        EditReceived?.Invoke(new VoxelEdit(new BlockPos(x, y, z), (Block)block));

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = SnapshotChannel)]
    private void ReceiveSnapshot(byte[] data) => SnapshotReceived?.Invoke(data);

    [Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveHash(long hash) => HashReceived?.Invoke((ulong)hash);
}
