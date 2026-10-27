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
        HashSet<int> dead = _state.Players.Where(p => p.Value.Dead).Select(p => p.Key).ToHashSet();
        foreach ((int peer, ServerPlayer player) in _state.Players)
        {
            // The dead vanish for everyone else until they respawn; their own client still gets them.
            PlayerSnapshot[] seen = dead.Count == 0 ? players : players.Where(p => p.PeerId == peer || !dead.Contains(p.PeerId)).ToArray();
            float respawnIn = player.DeadUntil is { } until ? (float)Math.Max(0, until - _state.Seconds) : -1;
            var snapshot = new Snapshot(_state.Tick, player.AckSeq, seen, planes, _state.Match, _weapons.NukeReadyIn(player.Team), player.Health, respawnIn);
            _state.Net.SendSnapshot(peer, WireFormat.EncodeSnapshot(snapshot));
        }
    }
}
