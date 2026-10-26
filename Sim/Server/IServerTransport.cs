using IslandWar.Sim.Protocol;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Server;

/// <summary>
/// How the server reaches its clients. The engine host implements it over the network; tests use a fake
/// that records what was sent. Every method only delivers: what to send is decided by the server.
/// </summary>
public interface IServerTransport
{
    void SendWorld(int peer, WorldInit world);

    void BroadcastRoster(byte[] roster);

    /// <summary>Tells a client why it is being dropped, then disconnects it once the message is on its way.</summary>
    void Kick(int peer, string reason);

    void Disconnect(int peer);

    /// <summary>Round trip to the peer in milliseconds, or 0 if unknown.</summary>
    int PingOf(int peer);

    void BroadcastBombDrop(BombDrop drop);

    void BroadcastExplosion(Explosion explosion);

    void BroadcastEdit(VoxelEdit edit);

    void SendEdit(int peer, VoxelEdit edit);

    void SendSnapshot(int peer, byte[] snapshot);

    void SendHash(int peer, ulong hash);
}
