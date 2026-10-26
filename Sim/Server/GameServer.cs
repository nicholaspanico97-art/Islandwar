using IslandWar.Sim.Match;
using IslandWar.Sim.Protocol;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Server;

/// <summary>
/// The dedicated server's game, without the engine: it owns the match, the world, every player, plane and
/// bomb. Clients send only inputs and requests. The host (ServerMain) forwards network events to the
/// methods here and calls <see cref="Tick"/> at the tick rate; everything sent back goes through the
/// <see cref="IServerTransport"/>. The work is split into systems that share one <see cref="ServerState"/>.
/// </summary>
public sealed class GameServer
{
    private readonly Sessions _sessions;
    private readonly MatchSystem _match;
    private readonly Movement _movement;
    private readonly Vehicles _vehicles;
    private readonly Weapons _weapons;
    private readonly Building _building;
    private readonly Replication _replication;

    public GameServer(GameData data, ServerOptions options, IServerTransport net, Action<string> log)
    {
        State = new ServerState(data, options, net, log);
        _vehicles = new Vehicles(State);
        _weapons = new Weapons(State);
        _sessions = new Sessions(State, _vehicles);
        _match = new MatchSystem(State);
        _movement = new Movement(State, _vehicles, _weapons);
        _building = new Building(State, _vehicles);
        _replication = new Replication(State, _weapons);

        _match.Started += ResetWorld;
        _match.BackToWarmup += _sessions.ForgetDeparted;
        _sessions.PlayerLeft += _match.CheckConcede;

        log($"[server] generated island: seed {options.Seed}, {World.SizeX}x{World.SizeY}x{World.SizeZ} blocks, hash {World.ComputeHash():x16}");
        _vehicles.ParkPlanes();
        log($"[server] warmup: the match starts {State.Preset.CountdownSeconds:0} s after both teams have {State.Preset.MinPlayersPerTeam} player(s)");
        log($"[server] preset '{options.PresetName}', up to {State.Preset.MaxPlayersPerTeam} per team{(options.Dev ? "; developer mode: noclip on, any team" : "")}");
    }

    public ServerState State { get; }

    public VoxelWorld World => State.World;

    /// <summary>One server tick: the clock, then players and planes, then bombs, then what goes out.</summary>
    public void Tick(ulong nowMsec)
    {
        State.Tick++;
        _sessions.DropSilentPeers(nowMsec);
        _match.Advance();
        _movement.Step();
        _vehicles.StepEmpty();
        _weapons.FlyBombs();
        _replication.Step();
    }

    public void PeerConnected(int peer, ulong nowMsec) => _sessions.Connected(peer, nowMsec);

    public void PeerDisconnected(int peer) => _sessions.Left(peer);

    public void Hello(int peer, string name, Team team, string token) => _sessions.Hello(peer, name, team, token);

    public void InputsReceived(int peer, byte[] data) => _movement.InputsReceived(peer, data);

    public void EditRequested(int peer, VoxelEdit edit) => _building.EditRequested(peer, edit);

    public void ConcedeVote(int peer, bool vote) => _match.ConcedeVote(peer, vote);

    public void HashRequested(int peer) => State.Net.SendHash(peer, World.ComputeHash());

    /// <summary>
    /// A fair start: the island regenerated from the seed, planes back on their moorings, bombs gone, everyone
    /// on foot at their team's spawn. Every client gets the fresh world, the same way a late joiner would.
    /// </summary>
    private void ResetWorld()
    {
        State.World = IslandGenerator.Generate(State.Data.Terrain, State.Options.Seed);
        _weapons.Reset();
        _vehicles.ResetToMoorings();
        State.ResetSpawns();
        WorldInit fresh = State.CurrentWorld();
        foreach ((int peer, ServerPlayer player) in State.Players)
        {
            player.Respawn(State.NextSpawn(player.Team));
            State.Net.SendWorld(peer, fresh);
        }
    }
}
