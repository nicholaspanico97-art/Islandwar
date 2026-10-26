using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using IslandWar.Net;
using IslandWar.Sim;
using IslandWar.Sim.Match;
using IslandWar.Sim.Players;
using IslandWar.Sim.Protocol;
using IslandWar.Sim.Vehicles;
using IslandWar.Sim.Voxels;
using IslandWar.Sim.Weapons;

namespace IslandWar.Server;

/// <summary>
/// Dedicated server: owns the match, the world, every player, plane and bomb. Clients send only inputs and edit
/// requests. The server runs each player (or the plane they fly) from their inputs, checks every edit
/// against the build rules, drops and flies bombs, blasts their craters, and sends the results back as
/// edits, snapshots, drops and explosions.
/// </summary>
public partial class ServerMain : Node
{
    private const int MaxClients = 32;

    /// <summary>A client that connects but doesn't say hello within this many milliseconds is dropped.</summary>
    private const ulong HelloTimeoutMsec = 10_000;

    private readonly Dictionary<int, ServerPlayer> _players = new();
    private readonly Dictionary<int, ServerPlane> _planes = new();
    private readonly List<ServerBomb> _bombs = new();

    /// <summary>Connected peers that haven't said hello yet, with when they connected.</summary>
    private readonly Dictionary<int, ulong> _pending = new();
    private readonly Dictionary<Team, int> _spawnSlots = new() { [Team.Red] = 0, [Team.Blue] = 0 };
    private MatchPreset _preset = null!;
    private MatchStatus _match = MatchStatus.Warmup;

    /// <summary>
    /// Seats of players who left during this match, by identity token, so they come back to the same team
    /// with the same name. Forgotten when the server returns to warmup.
    /// </summary>
    private readonly Dictionary<string, (string Name, Team Team)> _departed = new();

    /// <summary>Lasting player ids by identity token, for block ownership. Ids start at 1; 0 means nobody.</summary>
    private readonly Dictionary<string, int> _playerIds = new();
    private int _nextPlayerId = 1;

    /// <summary>The roster goes out on every change and also this often, to refresh everyone's ping.</summary>
    private const uint RosterRefreshTicks = 120;

    /// <summary>When each team last dropped its nuke, on the match clock; null for not yet this match.</summary>
    private readonly Dictionary<Team, float?> _lastNukeDrop = new() { [Team.Red] = null, [Team.Blue] = null };
    private GameData _data = null!;
    private GameRules _rules = null!;
    private Replicator _net = null!;
    private int _seed;
    private int _nextBombId = 1;
    private uint _tick;

    public LaunchArgs Args { get; init; } = new() { IsServer = true };

    public VoxelWorld World { get; private set; } = null!;

    public override void _Ready()
    {
        // Same name as the client's root node, so RPC paths match (see Replicator).
        Name = "Game";

        try
        {
            _data = GameData.Load();
            _preset = _data.Preset(Args.Preset);
        }
        catch (InvalidDataException e)
        {
            GD.PushError($"[server] bad data file: {e.Message}");
            SetPhysicsProcess(false);
            GetTree().Quit(1);
            return;
        }

        _data.ApplyTickRate();
        _rules = _data.Rules;
        _seed = Args.Seed ?? _data.Terrain.DefaultSeed;
        World = IslandGenerator.Generate(_data.Terrain, _seed);
        GD.Print($"[server] generated island: seed {_seed}, {World.SizeX}x{World.SizeY}x{World.SizeZ} blocks, hash {World.ComputeHash():x16}");

        // One plane per team, parked off its own island. Ids start at 1; 0 means "no plane".
        foreach (Team team in Teams.Playing)
        {
            var plane = new ServerPlane(_planes.Count + 1, team, _data.PlaneSpawnIn(World, team));
            _planes[plane.Id] = plane;
            GD.Print($"[server] {team} plane {plane.Id} parked at {plane.State.Position}");
        }

        GD.Print($"[server] warmup: the match starts {_preset.CountdownSeconds:0} s after both teams have {_preset.MinPlayersPerTeam} player(s)");

        GD.Print($"[server] preset '{Args.Preset}', up to {_preset.MaxPlayersPerTeam} per team{(Args.Dev ? "; developer mode: noclip on, any team" : "")}");

        _net = new Replicator();
        AddChild(_net);
        _net.InputsReceived += OnInputs;
        _net.EditRequested += OnEditRequested;
        _net.HashRequested += peer => _net.SendHash(peer, World.ComputeHash());
        _net.HelloReceived += OnHello;
        _net.ConcedeVoteReceived += OnConcedeVote;

        var peer = new ENetMultiplayerPeer();
        Error error = peer.CreateServer(Args.Port, MaxClients);
        if (error != Error.Ok)
        {
            GD.PushError($"[server] could not listen on port {Args.Port}: {error}");
            GetTree().Quit(1);
            return;
        }

        Multiplayer.MultiplayerPeer = peer;
        Multiplayer.PeerConnected += id => _pending[(int)id] = Time.GetTicksMsec();
        Multiplayer.PeerDisconnected += id => Leave((int)id);
        GD.Print($"[server] listening on port {Args.Port}");
    }

    public override void _PhysicsProcess(double delta)
    {
        _tick++;
        NetSettings net = _data.Net;
        DropSilentPeers();
        AdvanceMatch(net.TickSeconds);
        foreach ((int peer, ServerPlayer player) in _players)
        {
            player.Refill(net);
            while (player.TryTakeInput(out PlayerInput input))
            {
                // Noclip is a developer convenience; in a real match the server just ignores it.
                if (!Args.Dev)
                {
                    input = input with { Buttons = input.Buttons & ~InputButtons.Fly };
                }

                // Getting in or out happens before the input's movement, and the client replays it the same way.
                if (input.Has(InputButtons.Use))
                {
                    ToggleSeat(peer, player);
                }

                player.Pawn = Pawn.Step(World, _rules, player.Pawn, input, net.TickSeconds);
                if (player.Pawn.InPlane)
                {
                    ServerPlane plane = _planes[player.Pawn.PlaneId];
                    plane.State = player.Pawn.Plane;
                    if (input.Has(InputButtons.Fire))
                    {
                        TryDropBomb(peer, plane);
                    }

                    if (input.Has(InputButtons.FireNuke))
                    {
                        TryDropNuke(peer, plane);
                    }
                }

                player.Processed(input);
            }

            if (!player.Pawn.InPlane && player.Pawn.Player.Position.Y < _data.Player.RespawnBelowY)
            {
                player.Pawn = PawnState.OnFoot(player.Spawn);
            }
        }

        // Planes nobody is flying still fall, coast and stop.
        var idle = new PlayerInput(0, 0, 0, 0, 0, InputButtons.None);
        foreach (ServerPlane plane in _planes.Values.Where(p => p.PilotPeerId == 0))
        {
            plane.State = PlaneFlight.Step(World, _rules.Plane, _rules.SeaLevel, plane.State, idle, net.TickSeconds);
        }

        FlyBombs(net);
        if (_tick % net.SnapshotIntervalTicks == 0)
        {
            SendSnapshots();
        }

        if (_tick % RosterRefreshTicks == 0 && _players.Count > 0)
        {
            BroadcastRoster();
        }
    }

    /// <summary>Drops a bomb if the phase allows bombs, the plane is airborne and its bomb bay has reloaded.</summary>
    private void TryDropBomb(int peer, ServerPlane plane)
    {
        uint cooldown = (uint)Math.Ceiling(_rules.Bomb.CooldownSeconds * _data.Net.TickRate);
        int radius = MatchFlow.CraterRadius(_match, _preset, _rules.Bomb.CraterRadius);
        if (radius == 0 || plane.State.OnGround || (plane.LastDropTick is { } last && _tick - last < cooldown))
        {
            return;
        }

        plane.LastDropTick = _tick;
        Drop(peer, plane, radius, nuke: false);
    }

    /// <summary>Drops the team's nuke if it's phase 3, the team has one ready, and the plane is airborne.</summary>
    private void TryDropNuke(int peer, ServerPlane plane)
    {
        if (plane.State.OnGround || MatchFlow.NukeReadyIn(_match, _preset, _lastNukeDrop[plane.Team]) != 0)
        {
            return;
        }

        _lastNukeDrop[plane.Team] = _match.StageSeconds;
        Drop(peer, plane, _rules.Bomb.NukeCraterRadius, nuke: true);
    }

    private void Drop(int peer, ServerPlane plane, int craterRadius, bool nuke)
    {
        var bomb = new ServerBomb(_nextBombId++, peer, _tick, BombFlight.Drop(_rules.Bomb, plane.State), craterRadius, plane.Team, nuke);
        _bombs.Add(bomb);
        _net.BroadcastBombDrop(new BombDrop(bomb.Id, peer, bomb.SpawnTick, bomb.State, nuke));
        GD.Print($"[server] peer {peer} ({plane.Team}) dropped {(nuke ? "THE NUKE" : "bomb")} {bomb.Id} at {bomb.State.Position}");
    }

    /// <summary>Moves every falling bomb one tick and explodes the ones that hit something.</summary>
    private void FlyBombs(NetSettings net)
    {
        uint maxTicks = (uint)(_rules.Bomb.MaxLifetimeSeconds * net.TickRate);
        for (int i = _bombs.Count - 1; i >= 0; i--)
        {
            ServerBomb bomb = _bombs[i];

            // A bomb starts falling the tick after its drop; clients count ticks the same way.
            if (bomb.SpawnTick == _tick)
            {
                continue;
            }

            (BombState next, BombImpact? impact) = BombFlight.Step(World, _rules.Bomb, _rules.SeaLevel, bomb.State, net.TickSeconds);
            bomb.State = next;
            if (impact is { } hit)
            {
                // No friendly fire: a team's bombs make no crater on its own half of the map.
                bool harmless = !hit.InWater && Protection.IsHomeGround(bomb.Team, hit.Position.X, World.SizeX, _data.Terrain.IslandCount);
                IReadOnlyList<VoxelEdit> crater = harmless ? Array.Empty<VoxelEdit>() : BombFlight.Explode(World, bomb.CraterRadius, hit);
                _net.BroadcastExplosion(new Explosion(bomb.Id, hit.Position, hit.InWater, WireFormat.EncodeEdits(crater.ToList()), bomb.CraterRadius, bomb.Nuke, harmless));
                string what = bomb.Nuke ? "the nuke" : $"bomb {bomb.Id}";
                GD.Print(hit.InWater ? $"[server] {what} splashed into the sea"
                    : harmless ? $"[server] {what} landed on {bomb.Team}'s own half: no crater (no friendly fire)"
                    : $"[server] {what} hit {hit.Block}: crater of {crater.Count} blocks");
                if (bomb.Nuke && !harmless)
                {
                    CheckNukeHit(bomb, hit);
                }

                _bombs.RemoveAt(i);
            }
            else if (_tick - bomb.SpawnTick > maxTicks)
            {
                _bombs.RemoveAt(i);
            }
        }
    }

    private void SendSnapshots()
    {
        PlayerSnapshot[] players = _players
            .Select(p => new PlayerSnapshot(p.Key, p.Value.Pawn.Player, p.Value.Yaw, p.Value.Pitch, p.Value.Pawn.PlaneId))
            .ToArray();
        PlaneSnapshot[] planes = _planes.Values.Select(p => new PlaneSnapshot(p.Id, p.State, p.PilotPeerId, p.Team)).ToArray();
        foreach ((int peer, ServerPlayer player) in _players)
        {
            float nukeReadyIn = MatchFlow.NukeReadyIn(_match, _preset, _lastNukeDrop[player.Team]);
            _net.SendSnapshot(peer, WireFormat.EncodeSnapshot(new Snapshot(_tick, player.AckSeq, players, planes, _match, nukeReadyIn)));
        }
    }

    /// <summary>Moves the match clock on and acts on what changed.</summary>
    private void AdvanceMatch(float dt)
    {
        (_match, MatchEvent change) = MatchFlow.Advance(
            _match, _preset, dt, _players.Values.Count(p => p.Team == Team.Red), _players.Values.Count(p => p.Team == Team.Blue));
        switch (change)
        {
            case MatchEvent.CountdownStarted:
                GD.Print($"[server] both teams ready: the match starts in {_preset.CountdownSeconds:0} s");
                break;
            case MatchEvent.CountdownCancelled:
                GD.Print("[server] a team dropped below the minimum: back to warmup");
                break;
            case MatchEvent.MatchStarted:
                ResetWorld();
                ClearConcedeVotes();
                GD.Print("[server] match started: phase 1");
                break;
            case MatchEvent.PhaseChanged:
                GD.Print($"[server] phase {_match.Phase}");
                break;
            case MatchEvent.MatchEnded:
                GD.Print($"[server] match over: {(_match.Winner == Team.None ? "draw" : $"{_match.Winner} wins")} ({_match.Reason})");
                break;
            case MatchEvent.BackToWarmup:
                _departed.Clear();
                ClearConcedeVotes();
                GD.Print("[server] back to warmup");
                break;
        }
    }

    /// <summary>
    /// A fair start: the island regenerated from the seed, planes back on their moorings, bombs gone, everyone
    /// on foot at their team's spawn. Every client gets the fresh world, the same way a late joiner would.
    /// </summary>
    private void ResetWorld()
    {
        World = IslandGenerator.Generate(_data.Terrain, _seed);
        _bombs.Clear();
        foreach (Team team in Teams.Playing)
        {
            _lastNukeDrop[team] = null;
        }
        foreach (ServerPlane plane in _planes.Values)
        {
            plane.State = _data.PlaneSpawnIn(World, plane.Team);
            plane.PilotPeerId = 0;
            plane.LastDropTick = null;
        }

        foreach (Team team in Teams.Playing)
        {
            _spawnSlots[team] = 0;
        }

        var fresh = new WorldInit(_seed, _data, WireFormat.EncodeEdits(Array.Empty<VoxelEdit>()), World.ComputeHash(), Args.Dev, Args.Preset);
        foreach ((int peer, ServerPlayer player) in _players)
        {
            player.Respawn(_data.SpawnIn(World, player.Team, _spawnSlots[player.Team]++));
            _net.SendWorld(peer, fresh);
        }
    }

    /// <summary>Gets a pilot out, or a player into the nearest free plane of their team in reach.</summary>
    private void ToggleSeat(int peer, ServerPlayer player)
    {
        if (player.Pawn.InPlane)
        {
            ServerPlane plane = _planes[player.Pawn.PlaneId];
            System.Numerics.Vector3 exit = VehicleRules.ExitPosition(World, _rules.Player, _rules.Plane, plane.State);
            plane.PilotPeerId = 0;
            plane.State = plane.State with { Throttle = 0 };
            player.Pawn = PawnState.OnFoot(PlayerState.At(exit));
            GD.Print($"[server] peer {peer} got out of plane {plane.Id}");
            return;
        }

        System.Numerics.Vector3 feet = player.Pawn.Player.Position;
        ServerPlane? nearest = _planes.Values
            .Where(p => p.PilotPeerId == 0 && p.Team == player.Team && VehicleRules.InEnterRange(_rules.Plane, feet, p.State))
            .OrderBy(p => System.Numerics.Vector3.DistanceSquared(feet, p.State.Position))
            .FirstOrDefault();
        if (nearest is null)
        {
            return;
        }

        nearest.PilotPeerId = peer;
        player.Pawn = new PawnState(PlayerState.At(nearest.State.Position), nearest.Id, nearest.State);
        GD.Print($"[server] peer {peer} got into plane {nearest.Id}");
    }

    /// <summary>
    /// A connected client introduced itself: give it a name and a team, and send it the world. A player who
    /// left during this match gets their old team and name back, whatever they ask for.
    /// </summary>
    private void OnHello(int peer, string requestedName, Team requestedTeam, string token)
    {
        if (!_pending.Remove(peer))
        {
            return;
        }

        // A token already in use belongs to someone else (or another window on the same machine): this
        // joiner is a new player, with no claim on that player's seat or blocks.
        if (_players.Values.Any(p => p.Token == token))
        {
            token = "";
        }

        (string Name, Team Team) seat = ("", Team.None);
        bool returning = token.Length > 0 && _departed.Remove(token, out seat);
        if (returning)
        {
            requestedName = seat.Name;
        }

        int red = _players.Values.Count(p => p.Team == Team.Red);
        int blue = _players.Values.Count(p => p.Team == Team.Blue);
        int max = _preset.MaxPlayersPerTeam;
        if (red >= max && blue >= max)
        {
            Kick(peer, $"The server is full ({max} per team).");
            return;
        }

        // Developer mode lets the test bots line up on whichever side they need.
        Team team = returning ? seat.Team : Teams.Assign(red, blue, requestedTeam, Args.Dev ? max : _preset.MaxTeamImbalance);
        if ((team == Team.Red ? red : blue) >= max)
        {
            team = team.Other();
        }

        string name = PlayerNames.Clean(requestedName, _players.Values.Select(p => p.Name));
        int playerId = token.Length > 0 && _playerIds.TryGetValue(token, out int known) ? known : _nextPlayerId++;
        if (token.Length > 0)
        {
            _playerIds[token] = playerId;
        }

        _players[peer] = new ServerPlayer(name, team, token, playerId, _data.SpawnIn(World, team, _spawnSlots[team]++), _data.Net);
        List<VoxelEdit> edits = World.EditsWithOwners().ToList();
        _net.SendWorld(peer, new WorldInit(_seed, _data, WireFormat.EncodeEdits(edits), World.ComputeHash(), Args.Dev, Args.Preset));
        BroadcastRoster();
        GD.Print($"[server] peer {peer} {(returning ? "came back" : "joined")} as '{name}' (player {playerId}) on {team}; sent the world with {edits.Count} edits");
    }

    private void Leave(int peer)
    {
        _pending.Remove(peer);
        if (!_players.Remove(peer, out ServerPlayer? player))
        {
            return;
        }

        if (player.Pawn.InPlane)
        {
            // A pilot who disconnects leaves the plane to glide down on its own.
            ServerPlane plane = _planes[player.Pawn.PlaneId];
            plane.PilotPeerId = 0;
            plane.State = plane.State with { Throttle = 0 };
        }

        // Keep their seat for this match in case they come back.
        if (player.Token.Length > 0 && _match.Stage != MatchStage.Warmup)
        {
            _departed[player.Token] = (player.Name, player.Team);
        }

        BroadcastRoster();
        GD.Print($"[server] '{player.Name}' (peer {peer}) left");
        CheckConcede();
    }

    private void BroadcastRoster() =>
        _net.BroadcastRoster(WireFormat.EncodeRoster(_players
            .Select(p => new RosterEntry(p.Key, p.Value.Name, p.Value.Team, p.Value.ConcedeVote, PingOf(p.Key), p.Value.PlayerId))
            .ToList()));

    private bool IsOnline(int playerId) => _players.Values.Any(p => p.PlayerId == playerId);

    private int PingOf(int peer) =>
        Multiplayer.MultiplayerPeer is ENetMultiplayerPeer enet && enet.GetPeer(peer) is { } connection
            ? (int)connection.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTime)
            : 0;

    private void OnConcedeVote(int peer, bool vote)
    {
        if (!_players.TryGetValue(peer, out ServerPlayer? player) || player.ConcedeVote == vote || (vote && !_match.Playing))
        {
            return;
        }

        player.ConcedeVote = vote;
        GD.Print($"[server] '{player.Name}' {(vote ? "voted" : "withdrew their vote")} to concede for {player.Team}");
        BroadcastRoster();
        CheckConcede();
    }

    /// <summary>A team that has enough concede votes loses on the spot.</summary>
    private void CheckConcede()
    {
        if (!_match.Playing)
        {
            return;
        }

        foreach (Team team in Teams.Playing)
        {
            ServerPlayer[] members = _players.Values.Where(p => p.Team == team).ToArray();
            if (MatchScore.Concedes(members.Count(p => p.ConcedeVote), members.Length, _preset.ConcedeFraction))
            {
                _match = MatchFlow.End(_match, team.Other(), EndReason.Concede);
                GD.Print($"[server] {team} conceded: {team.Other()} wins");
                return;
            }
        }
    }

    private void ClearConcedeVotes()
    {
        foreach (ServerPlayer player in _players.Values)
        {
            player.ConcedeVote = false;
        }

        BroadcastRoster();
    }

    /// <summary>Drops clients that connected but never said hello (an old version, or not this game at all).</summary>
    private void DropSilentPeers()
    {
        ulong now = Time.GetTicksMsec();
        foreach (int peer in _pending.Where(p => now - p.Value > HelloTimeoutMsec).Select(p => p.Key).ToList())
        {
            _pending.Remove(peer);
            GD.Print($"[server] peer {peer} never said hello; dropping it");
            Multiplayer.MultiplayerPeer.DisconnectPeer(peer);
        }
    }

    /// <summary>Tells a client why, then disconnects it a moment later so the message gets there first.</summary>
    private void Kick(int peer, string reason)
    {
        GD.Print($"[server] kicking peer {peer}: {reason}");
        _net.Kick(peer, reason);
        GetTree().CreateTimer(0.5).Timeout += () => Multiplayer.MultiplayerPeer?.DisconnectPeer(peer);
    }

    private void OnInputs(int peer, byte[] data)
    {
        if (!_players.TryGetValue(peer, out ServerPlayer? player))
        {
            return;
        }

        try
        {
            player.Receive(WireFormat.DecodeInputs(data), _data.Net);
        }
        catch (InvalidDataException e)
        {
            GD.PushWarning($"[server] bad input packet from peer {peer}: {e.Message}");
        }
    }

    private void OnEditRequested(int peer, VoxelEdit edit)
    {
        if (!_players.TryGetValue(peer, out ServerPlayer? player))
        {
            return;
        }

        // The server decides who placed it; whatever the client sent about owners is ignored.
        edit = edit with { Owner = edit.Block == Block.Air ? default : new BlockOwner(player.PlayerId, player.Team) };
        if (Allowed(player, edit, out string? reason) && World.ApplyEdit(edit))
        {
            _net.BroadcastEdit(edit);
            GD.Print($"[server] peer {peer} set {edit.Position} to {edit.Block}");
            return;
        }

        // Undo the client's prediction by telling it what's really there.
        if (World.InBounds(edit.Position))
        {
            _net.SendEdit(peer, new VoxelEdit(edit.Position, World.Get(edit.Position), World.OwnerOf(edit.Position)));
        }

        if (reason is not null)
        {
            GD.Print($"[server] rejected peer {peer} setting {edit.Position} to {edit.Block}: {reason}");
        }
    }

    private bool Allowed(ServerPlayer player, VoxelEdit edit, out string? reason)
    {
        PlayerSettings settings = _data.Player;
        PlayerState body = player.Pawn.Player;
        System.Numerics.Vector3 eye = body.Position + System.Numerics.Vector3.UnitY * settings.EyeHeight;
        IEnumerable<System.Numerics.Vector3> blockers = _players.Values
            .Where(p => !p.Pawn.InPlane && !p.Pawn.Player.Flying)
            .Select(p => p.Pawn.Player.Position);

        reason = !Enum.IsDefined(edit.Block) ? "unknown block type"
            : player.Pawn.InPlane ? "flying a plane"
            : !player.TrySpendEdit() ? "too many edits"
            : !BuildRules.InReach(settings, eye, edit.Position) ? "out of reach"
            : edit.Block == Block.Air && !BuildRules.CanBreak(World, edit.Position) ? "can't break that"
            : edit.Block == Block.Air && !Protection.MayBreak(World.OwnerOf(edit.Position), player.PlayerId, player.Team, IsOnline) ? "a teammate built that"
            : edit.Block != Block.Air && !BuildRules.CanPlace(World, settings, edit.Position, edit.Block, blockers) ? "can't place there"
            : edit.Block != Block.Air && _planes.Values.Any(p => BuildRules.OverlapsBox(p.State.Position, _rules.Plane.Width, _rules.Plane.Height, edit.Position)) ? "a plane is in the way"
            : null;
        return reason is null;
    }

    /// <summary>A nuke that lands anywhere on the enemy island wins the match for the team that dropped it.</summary>
    private void CheckNukeHit(ServerBomb nuke, BombImpact hit)
    {
        Team enemy = nuke.Team.Other();
        if (!_match.Playing || !MatchFlow.NukeWins(nuke.Team, hit, World.SizeX, _data.Terrain.IslandCount))
        {
            GD.Print($"[server] {nuke.Team}'s nuke missed the {enemy} island");
            return;
        }

        _match = MatchFlow.End(_match, nuke.Team, EndReason.NukeHit);
        GD.Print($"[server] {nuke.Team}'s nuke hit the {enemy} island: {nuke.Team} wins");
    }

    private sealed class ServerPlane
    {
        public ServerPlane(int id, Team team, PlaneState state)
        {
            Id = id;
            Team = team;
            State = state;
        }

        public int Id { get; }

        /// <summary>Only this team's players can fly it.</summary>
        public Team Team { get; }

        public PlaneState State { get; set; }

        /// <summary>The peer flying it, or 0.</summary>
        public int PilotPeerId { get; set; }

        /// <summary>When this plane last dropped a bomb, for the reload time.</summary>
        public uint? LastDropTick { get; set; }
    }

    private sealed class ServerBomb
    {
        public ServerBomb(int id, int ownerPeerId, uint spawnTick, BombState state, int craterRadius, Team team, bool nuke)
        {
            Id = id;
            OwnerPeerId = ownerPeerId;
            SpawnTick = spawnTick;
            State = state;
            CraterRadius = craterRadius;
            Team = team;
            Nuke = nuke;
        }

        /// <summary>The team whose plane dropped it.</summary>
        public Team Team { get; }

        public bool Nuke { get; }

        /// <summary>Set by the match phase when the bomb was dropped.</summary>
        public int CraterRadius { get; }

        public int Id { get; }

        public int OwnerPeerId { get; }

        public uint SpawnTick { get; }

        public BombState State { get; set; }
    }

    /// <summary>One connected player as the server sees them.</summary>
    private sealed class ServerPlayer
    {
        private readonly Queue<PlayerInput> _inputs = new();
        private uint _lastQueuedSeq;
        private float _inputBudget;
        private float _editBudget;

        public ServerPlayer(string name, Team team, string token, int playerId, PlayerState spawn, NetSettings net)
        {
            Name = name;
            Team = team;
            Token = token;
            PlayerId = playerId;
            Spawn = spawn;
            Pawn = PawnState.OnFoot(spawn);
            _editBudget = net.MaxEditsPerSecond;
        }

        public string Name { get; }

        public Team Team { get; }

        /// <summary>The client's identity token, for getting this seat back after a disconnect.</summary>
        public string Token { get; }

        /// <summary>Lasting id (same token, same id), recorded as the owner of the blocks they place.</summary>
        public int PlayerId { get; }

        public bool ConcedeVote { get; set; }

        /// <summary>Where this player appeared, and where they come back after falling off the map.</summary>
        public PlayerState Spawn { get; private set; }

        public PawnState Pawn { get; set; }

        /// <summary>The newest input processed, sent back in snapshots so the client can reconcile.</summary>
        public uint AckSeq { get; private set; }

        public float Yaw { get; private set; }

        public float Pitch { get; private set; }

        /// <summary>Queues new inputs. Packets repeat recent inputs, so anything already queued is skipped.</summary>
        public void Receive(IReadOnlyList<PlayerInput> inputs, NetSettings net)
        {
            foreach (PlayerInput input in inputs)
            {
                if (input.Seq > _lastQueuedSeq && _inputs.Count < net.MaxInputBacklog * 2)
                {
                    _inputs.Enqueue(input);
                    _lastQueuedSeq = input.Seq;
                }
            }
        }

        /// <summary>
        /// Called once per tick. The input budget grows by one input per tick up to the backlog limit, so a
        /// client sending inputs faster than real time can't move faster than everyone else.
        /// </summary>
        public void Refill(NetSettings net)
        {
            _inputBudget = Math.Min(_inputBudget + 1, net.MaxInputBacklog);
            _editBudget = Math.Min(_editBudget + (float)net.MaxEditsPerSecond / net.TickRate, net.MaxEditsPerSecond);
        }

        /// <summary>
        /// Puts the player on foot at a new spawn for a fresh match. Queued inputs from before are dropped;
        /// the sequence numbers carry on, so the client's next inputs are still accepted.
        /// </summary>
        public void Respawn(PlayerState spawn)
        {
            Spawn = spawn;
            Pawn = PawnState.OnFoot(spawn);
            _inputs.Clear();
        }

        public bool TryTakeInput(out PlayerInput input)
        {
            if (_inputs.Count == 0 || _inputBudget < 1)
            {
                input = default;
                return false;
            }

            _inputBudget--;
            input = _inputs.Dequeue();
            return true;
        }

        public void Processed(PlayerInput input)
        {
            AckSeq = input.Seq;
            Yaw = input.Yaw;
            Pitch = input.Pitch;
        }

        public bool TrySpendEdit()
        {
            if (_editBudget < 1)
            {
                return false;
            }

            _editBudget--;
            return true;
        }
    }
}
