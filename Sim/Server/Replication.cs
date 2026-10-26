using IslandWar.Sim.Protocol;

namespace IslandWar.Sim.Server;

/// <summary>What goes out on a timer: snapshots of every player and plane, and the roster with fresh pings.</summary>
public sealed class Replication
{
    /// <summary>The roster goes out on every change and also this often, to refresh everyone's ping.</summary>
    private const uint RosterRefreshTicks = 120;

    private readonly ServerState _state;
    private readonly Weapons _weapons;

    public Replication(ServerState state, Weapons weapons)
    {
        _state = state;
        _weapons = weapons;
    }

    public void Step()
    {
        if (_state.Tick % _state.Data.Net.SnapshotIntervalTicks == 0)
        {
            SendSnapshots();
        }

        if (_state.Tick % RosterRefreshTicks == 0 && _state.Players.Count > 0)
        {
            _state.BroadcastRoster();
        }
    }

    private void SendSnapshots()
    {
        PlayerSnapshot[] players = _state.Players
            .Select(p => new PlayerSnapshot(p.Key, p.Value.Pawn.Player, p.Value.Yaw, p.Value.Pitch, p.Value.Pawn.PlaneId))
            .ToArray();
        PlaneSnapshot[] planes = _state.Planes.Values.Select(p => new PlaneSnapshot(p.Id, p.State, p.PilotPeerId, p.Team)).ToArray();
        foreach ((int peer, ServerPlayer player) in _state.Players)
        {
            var snapshot = new Snapshot(_state.Tick, player.AckSeq, players, planes, _state.Match, _weapons.NukeReadyIn(player.Team));
            _state.Net.SendSnapshot(peer, WireFormat.EncodeSnapshot(snapshot));
        }
    }
}
