using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using IslandWar.Net;
using IslandWar.Sim;
using IslandWar.Sim.Players;
using IslandWar.Sim.Protocol;
using IslandWar.Sim.Vehicles;
using IslandWar.Sim.Voxels;
using IslandWar.Sim.Weapons;

namespace IslandWar.Server;

/// <summary>
/// Dedicated server: owns the world, every player, plane and bomb. Clients send only inputs and edit
/// requests. The server runs each player (or the plane they fly) from their inputs, checks every edit
/// against the build rules, drops and flies bombs, blasts their craters, and sends the results back as
/// edits, snapshots, drops and explosions.
/// </summary>
public partial class ServerMain : Node
{
    private const int MaxClients = 32;

    private readonly Dictionary<int, ServerPlayer> _players = new();
    private readonly Dictionary<int, ServerPlane> _planes = new();
    private readonly List<ServerBomb> _bombs = new();
    private GameData _data = null!;
    private GameRules _rules = null!;
    private Replicator _net = null!;
    private int _seed;
    private int _joins;
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

        // M1c has one plane. Ids start at 1; 0 means "no plane".
        var plane = new ServerPlane(1, _data.PlaneSpawnIn(World));
        _planes[plane.Id] = plane;
        GD.Print($"[server] plane {plane.Id} parked at {plane.State.Position}");

        _net = new Replicator();
        AddChild(_net);
        _net.InputsReceived += OnInputs;
        _net.EditRequested += OnEditRequested;
        _net.HashRequested += peer => _net.SendHash(peer, World.ComputeHash());

        var peer = new ENetMultiplayerPeer();
        Error error = peer.CreateServer(Args.Port, MaxClients);
        if (error != Error.Ok)
        {
            GD.PushError($"[server] could not listen on port {Args.Port}: {error}");
            GetTree().Quit(1);
            return;
        }

        Multiplayer.MultiplayerPeer = peer;
        Multiplayer.PeerConnected += id => Join((int)id);
        Multiplayer.PeerDisconnected += id => Leave((int)id);
        GD.Print($"[server] listening on port {Args.Port}");
    }

    public override void _PhysicsProcess(double delta)
    {
        _tick++;
        NetSettings net = _data.Net;
        foreach ((int peer, ServerPlayer player) in _players)
        {
            player.Refill(net);
            while (player.TryTakeInput(out PlayerInput input))
            {
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
    }

    /// <summary>Drops a bomb if the plane is airborne and its bomb bay has reloaded.</summary>
    private void TryDropBomb(int peer, ServerPlane plane)
    {
        uint cooldown = (uint)Math.Ceiling(_rules.Bomb.CooldownSeconds * _data.Net.TickRate);
        if (plane.State.OnGround || (plane.LastDropTick is { } last && _tick - last < cooldown))
        {
            return;
        }

        plane.LastDropTick = _tick;
        var bomb = new ServerBomb(_nextBombId++, peer, _tick, BombFlight.Drop(_rules.Bomb, plane.State));
        _bombs.Add(bomb);
        _net.BroadcastBombDrop(new BombDrop(bomb.Id, peer, bomb.SpawnTick, bomb.State));
        GD.Print($"[server] peer {peer} dropped bomb {bomb.Id} at {bomb.State.Position}");
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
                IReadOnlyList<VoxelEdit> crater = BombFlight.Explode(World, _rules.Bomb, hit);
                _net.BroadcastExplosion(new Explosion(bomb.Id, hit.Position, hit.InWater, WireFormat.EncodeEdits(crater.ToList())));
                GD.Print(hit.InWater
                    ? $"[server] bomb {bomb.Id} splashed into the sea"
                    : $"[server] bomb {bomb.Id} hit {hit.Block}: crater of {crater.Count} blocks");
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
        PlaneSnapshot[] planes = _planes.Values.Select(p => new PlaneSnapshot(p.Id, p.State, p.PilotPeerId)).ToArray();
        foreach ((int peer, ServerPlayer player) in _players)
        {
            _net.SendSnapshot(peer, WireFormat.EncodeSnapshot(new Snapshot(_tick, player.AckSeq, players, planes)));
        }
    }

    /// <summary>Gets a pilot out, or a player into the nearest free plane in reach.</summary>
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
            .Where(p => p.PilotPeerId == 0 && VehicleRules.InEnterRange(_rules.Plane, feet, p.State))
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

    private void Join(int peer)
    {
        _players[peer] = new ServerPlayer(_data.SpawnIn(World, _joins++), _data.Net);
        List<VoxelEdit> edits = World.Deltas.All().ToList();
        _net.SendWorld(peer, new WorldInit(_seed, _data, WireFormat.EncodeEdits(edits), World.ComputeHash()));
        GD.Print($"[server] peer {peer} connected; sent the world with {edits.Count} edits");
    }

    private void Leave(int peer)
    {
        if (_players.Remove(peer, out ServerPlayer? player) && player.Pawn.InPlane)
        {
            // A pilot who disconnects leaves the plane to glide down on its own.
            ServerPlane plane = _planes[player.Pawn.PlaneId];
            plane.PilotPeerId = 0;
            plane.State = plane.State with { Throttle = 0 };
        }

        GD.Print($"[server] peer {peer} disconnected");
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

        if (Allowed(player, edit, out string? reason) && World.ApplyEdit(edit))
        {
            _net.BroadcastEdit(edit);
            GD.Print($"[server] peer {peer} set {edit.Position} to {edit.Block}");
            return;
        }

        // Undo the client's prediction by telling it what's really there.
        if (World.InBounds(edit.Position))
        {
            _net.SendEdit(peer, new VoxelEdit(edit.Position, World.Get(edit.Position)));
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
            : edit.Block != Block.Air && !BuildRules.CanPlace(World, settings, edit.Position, edit.Block, blockers) ? "can't place there"
            : edit.Block != Block.Air && _planes.Values.Any(p => BuildRules.OverlapsBox(p.State.Position, _rules.Plane.Width, _rules.Plane.Height, edit.Position)) ? "a plane is in the way"
            : null;
        return reason is null;
    }

    private sealed class ServerPlane
    {
        public ServerPlane(int id, PlaneState state)
        {
            Id = id;
            State = state;
        }

        public int Id { get; }

        public PlaneState State { get; set; }

        /// <summary>The peer flying it, or 0.</summary>
        public int PilotPeerId { get; set; }

        /// <summary>When this plane last dropped a bomb, for the reload time.</summary>
        public uint? LastDropTick { get; set; }
    }

    private sealed class ServerBomb
    {
        public ServerBomb(int id, int ownerPeerId, uint spawnTick, BombState state)
        {
            Id = id;
            OwnerPeerId = ownerPeerId;
            SpawnTick = spawnTick;
            State = state;
        }

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

        public ServerPlayer(PlayerState spawn, NetSettings net)
        {
            Spawn = spawn;
            Pawn = PawnState.OnFoot(spawn);
            _editBudget = net.MaxEditsPerSecond;
        }

        /// <summary>Where this player appeared, and where they come back after falling off the map.</summary>
        public PlayerState Spawn { get; }

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
