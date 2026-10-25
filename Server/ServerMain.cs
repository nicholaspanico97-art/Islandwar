using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using IslandWar.Net;
using IslandWar.Sim.Players;
using IslandWar.Sim.Protocol;
using IslandWar.Sim.Voxels;

namespace IslandWar.Server;

/// <summary>
/// Dedicated server: owns the world and every player. Clients send only inputs and edit requests. The
/// server runs each player's movement from their inputs, checks every edit against the build rules,
/// and sends the results back as edits and snapshots.
/// </summary>
public partial class ServerMain : Node
{
    private const int MaxClients = 32;

    private readonly Dictionary<int, ServerPlayer> _players = new();
    private GameData _data = null!;
    private Replicator _net = null!;
    private int _seed;
    private int _joins;
    private uint _tick;

    public LaunchArgs Args { get; init; } = new() { IsServer = true };

    public VoxelWorld World { get; private set; } = null!;

    public override void _Ready()
    {
        // Same name as the client's root node, so RPC paths match (see Replicator).
        Name = "Game";

        _data = GameData.Load();
        _data.ApplyTickRate();
        _seed = Args.Seed ?? _data.Terrain.DefaultSeed;
        World = IslandGenerator.Generate(_data.Terrain, _seed);
        GD.Print($"[server] generated island: seed {_seed}, {World.SizeX}x{World.SizeY}x{World.SizeZ} blocks, hash {World.ComputeHash():x16}");

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
        foreach (ServerPlayer player in _players.Values)
        {
            player.Advance(World, _data.Player, net);
            if (player.State.Position.Y < _data.Player.RespawnBelowY)
            {
                player.State = player.Spawn;
            }
        }

        if (_tick % net.SnapshotIntervalTicks == 0)
        {
            PlayerSnapshot[] everyone = _players.Select(p => new PlayerSnapshot(p.Key, p.Value.State, p.Value.Yaw, p.Value.Pitch)).ToArray();
            foreach ((int peer, ServerPlayer player) in _players)
            {
                _net.SendSnapshot(peer, WireFormat.EncodeSnapshot(new Snapshot(_tick, player.AckSeq, everyone)));
            }
        }
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
        _players.Remove(peer);
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

        if (Allowed(peer, player, edit, out string? reason) && World.ApplyEdit(edit))
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

    private bool Allowed(int peer, ServerPlayer player, VoxelEdit edit, out string? reason)
    {
        PlayerSettings settings = _data.Player;
        System.Numerics.Vector3 eye = player.State.Position + System.Numerics.Vector3.UnitY * settings.EyeHeight;
        IEnumerable<System.Numerics.Vector3> blockers = _players.Values.Where(p => !p.State.Flying).Select(p => p.State.Position);

        reason = !Enum.IsDefined(edit.Block) ? "unknown block type"
            : !player.TrySpendEdit() ? "too many edits"
            : !BuildRules.InReach(settings, eye, edit.Position) ? "out of reach"
            : edit.Block == Block.Air && !BuildRules.CanBreak(World, edit.Position) ? "can't break that"
            : edit.Block != Block.Air && !BuildRules.CanPlace(World, settings, edit.Position, edit.Block, blockers) ? "can't place there"
            : null;
        return reason is null;
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
            State = spawn;
            _editBudget = net.MaxEditsPerSecond;
        }

        /// <summary>Where this player appeared, and where they come back after falling off the map.</summary>
        public PlayerState Spawn { get; }

        public PlayerState State { get; set; }

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
        /// Runs queued inputs for one tick. The budget grows by one input per tick up to the backlog limit,
        /// so a client sending inputs faster than real time can't move faster than everyone else.
        /// </summary>
        public void Advance(VoxelWorld world, PlayerSettings settings, NetSettings net)
        {
            _inputBudget = Math.Min(_inputBudget + 1, net.MaxInputBacklog);
            _editBudget = Math.Min(_editBudget + (float)net.MaxEditsPerSecond / net.TickRate, net.MaxEditsPerSecond);
            while (_inputs.Count > 0 && _inputBudget >= 1)
            {
                PlayerInput input = _inputs.Dequeue();
                State = PlayerMovement.Step(world, settings, State, input, net.TickSeconds);
                AckSeq = input.Seq;
                Yaw = input.Yaw;
                Pitch = input.Pitch;
                _inputBudget--;
            }
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
