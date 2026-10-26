using System.IO;
using Godot;
using IslandWar.Net;
using IslandWar.Sim;
using IslandWar.Sim.Protocol;
using IslandWar.Sim.Server;
using IslandWar.Sim.Voxels;

namespace IslandWar.Server;

/// <summary>
/// The dedicated server's engine host. The game itself is <see cref="GameServer"/> (plain C#, in Sim); this
/// node opens the network port, forwards what clients send to it, runs its tick at the physics rate, and
/// carries what it sends back over the <see cref="Replicator"/>.
/// </summary>
public partial class ServerMain : Node, IServerTransport
{
    private const int MaxClients = 32;

    private GameServer _game = null!;
    private Replicator _net = null!;

    public LaunchArgs Args { get; init; } = new() { IsServer = true };

    public override void _Ready()
    {
        // Same name as the client's root node, so RPC paths match (see Replicator).
        Name = "Game";

        GameData data;
        try
        {
            data = GameFiles.LoadData();
            data.Preset(Args.Preset);
        }
        catch (InvalidDataException e)
        {
            GD.PushError($"[server] bad data file: {e.Message}");
            SetPhysicsProcess(false);
            GetTree().Quit(1);
            return;
        }

        data.ApplyTickRate();
        _net = new Replicator();
        AddChild(_net);
        _game = new GameServer(data, new ServerOptions(Args.Preset, Args.Seed ?? data.Terrain.DefaultSeed, Args.Dev), this, message => GD.Print(message));

        _net.InputsReceived += _game.InputsReceived;
        _net.EditRequested += _game.EditRequested;
        _net.HashRequested += _game.HashRequested;
        _net.HelloReceived += _game.Hello;
        _net.ConcedeVoteReceived += _game.ConcedeVote;

        var peer = new ENetMultiplayerPeer();
        Error error = peer.CreateServer(Args.Port, MaxClients);
        if (error != Error.Ok)
        {
            GD.PushError($"[server] could not listen on port {Args.Port}: {error}");
            SetPhysicsProcess(false);
            GetTree().Quit(1);
            return;
        }

        Multiplayer.MultiplayerPeer = peer;
        Multiplayer.PeerConnected += id => _game.PeerConnected((int)id, Time.GetTicksMsec());
        Multiplayer.PeerDisconnected += id => _game.PeerDisconnected((int)id);
        GD.Print($"[server] listening on port {Args.Port}");
    }

    public override void _PhysicsProcess(double delta) => _game.Tick(Time.GetTicksMsec());

    // ---- IServerTransport ----

    public void SendWorld(int peer, WorldInit world) => _net.SendWorld(peer, world);

    public void BroadcastRoster(byte[] roster) => _net.BroadcastRoster(roster);

    /// <summary>Tells a client why, then disconnects it a moment later so the message gets there first.</summary>
    public void Kick(int peer, string reason)
    {
        _net.Kick(peer, reason);
        GetTree().CreateTimer(0.5).Timeout += () => Multiplayer.MultiplayerPeer?.DisconnectPeer(peer);
    }

    public void Disconnect(int peer) => Multiplayer.MultiplayerPeer.DisconnectPeer(peer);

    public int PingOf(int peer) =>
        Multiplayer.MultiplayerPeer is ENetMultiplayerPeer enet && enet.GetPeer(peer) is { } connection
            ? (int)connection.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTime)
            : 0;

    public void BroadcastBombDrop(BombDrop drop) => _net.BroadcastBombDrop(drop);

    public void BroadcastExplosion(Explosion explosion) => _net.BroadcastExplosion(explosion);

    public void BroadcastEdit(VoxelEdit edit) => _net.BroadcastEdit(edit);

    public void SendEdit(int peer, VoxelEdit edit) => _net.SendEdit(peer, edit);

    public void SendSnapshot(int peer, byte[] snapshot) => _net.SendSnapshot(peer, snapshot);

    public void SendHash(int peer, ulong hash) => _net.SendHash(peer, hash);
}
