using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using IslandWar.Client.Game;
using IslandWar.Net;
using IslandWar.Sim;
using IslandWar.Sim.Match;
using IslandWar.Sim.Objects;
using IslandWar.Sim.Players;
using IslandWar.Sim.Protocol;
using IslandWar.Sim.Voxels;
using IslandWar.Sim.Weapons;

namespace IslandWar.Client.Online;

/// <summary>
/// The client side of a connection, without any rendering, so the sync-test bots use it too. It rebuilds
/// the world from what the server sends on join, applies the server's edits, hands the local player's
/// server state to whoever predicts it, and keeps other players' snapshots for interpolation.
/// </summary>
public partial class NetClient : Node, IGameLink
{
    private readonly Dictionary<int, InterpolationBuffer<RemoteState>> _others = new();
    private readonly Dictionary<int, InterpolationBuffer<RemotePlane>> _planes = new();
    private readonly Dictionary<int, FallingBomb> _bombs = new();
    private Dictionary<int, RosterEntry> _roster = new();
    private double _serverTick;
    private bool _joined;

    public Replicator Replicator { get; init; } = null!;

    public GameData? Data { get; private set; }

    public VoxelWorld? World { get; private set; }

    /// <summary>Every structure, as the server last said.</summary>
    public ObjectStore? Objects { get; private set; }

    /// <summary>Our team's shared state, as the server last said.</summary>
    public TeamState? TeamState { get; private set; }

    /// <summary>True if the world rebuilt on join had the same hash as the server's.</summary>
    public bool WorldMatchedServer { get; private set; }

    /// <summary>Set before disconnecting on purpose, so losing the connection isn't treated as a failure.</summary>
    public bool LeavingOnPurpose { get; set; }

    /// <summary>The server allows noclip flying (developer mode).</summary>
    public bool DevMode { get; private set; }

    public string PresetName { get; private set; } = "";

    /// <summary>Everyone connected, by peer id, as of the server's last roster.</summary>
    public IReadOnlyDictionary<int, RosterEntry> Roster => _roster;

    /// <summary>The match as of the latest snapshot.</summary>
    public MatchStatus Match { get; private set; } = MatchStatus.Warmup;

    /// <summary>The match preset the server runs, once the world has arrived.</summary>
    public MatchPreset? Preset { get; private set; }

    /// <summary>Seconds until our team's nuke is ready: 0 when ready, negative outside phase 3.</summary>
    public float NukeReadyIn { get; private set; } = -1;

    /// <summary>The newest input number sent, so a new prediction after a world reset can carry on from it.</summary>
    public uint LastSentSeq { get; private set; }

    /// <summary>Our lasting player id (the owner recorded on blocks we place), once the server has said.</summary>
    public int MyPlayerId => _roster.TryGetValue(Multiplayer.GetUniqueId(), out RosterEntry me) ? me.PlayerId : 0;

    /// <summary>Our own team, once the server has said.</summary>
    public Team MyTeam => _roster.TryGetValue(Multiplayer.GetUniqueId(), out RosterEntry me) ? me.Team : Team.None;

    /// <summary>The world is rebuilt and the server has placed us: the game can start here.</summary>
    public event Action<PawnState>? Joined;

    /// <summary>The server changed a block (or undid one of our predictions); it's already in <see cref="World"/>.</summary>
    public event Action<VoxelEdit>? WorldChanged;

    /// <summary>The server's state for our player (and the plane they fly) after it processed the given input.</summary>
    public event Action<PawnState, uint>? OwnStateReceived;

    /// <summary>A bomb went off; its crater is already in <see cref="World"/> (and reported by <see cref="WorldChanged"/>).</summary>
    public event Action<Explosion>? Exploded;

    /// <summary>The server is about to disconnect us, and why (full, for example).</summary>
    public event Action<string>? KickedOut;

    /// <summary>
    /// A new match started and the server sent a fresh world. <see cref="World"/> is already rebuilt; whoever
    /// plays it should start over, and <see cref="Joined"/> fires again with the new spawn.
    /// </summary>
    public event Action? WorldReset;

    public override void _Ready()
    {
        Name = "NetClient";
        Replicator.WorldReceived += OnWorld;
        Replicator.EditReceived += OnEdit;
        Replicator.SnapshotReceived += OnSnapshot;
        Replicator.BombDropped += OnBombDropped;
        Replicator.ExplosionReceived += OnExplosion;
        Replicator.RosterReceived += OnRoster;
        Replicator.Kicked += OnKicked;
        Replicator.ObjectsReceived += OnObjects;
        Replicator.ObjectRemoved += OnObjectRemoved;
        Replicator.TeamStateReceived += OnTeamState;
    }

    public override void _ExitTree()
    {
        // The replicator outlives this connection; stop listening to it.
        Replicator.WorldReceived -= OnWorld;
        Replicator.EditReceived -= OnEdit;
        Replicator.SnapshotReceived -= OnSnapshot;
        Replicator.BombDropped -= OnBombDropped;
        Replicator.ExplosionReceived -= OnExplosion;
        Replicator.RosterReceived -= OnRoster;
        Replicator.Kicked -= OnKicked;
        Replicator.ObjectsReceived -= OnObjects;
        Replicator.ObjectRemoved -= OnObjectRemoved;
        Replicator.TeamStateReceived -= OnTeamState;
    }

    public override void _Process(double delta)
    {
        // Between snapshots, the server's clock moves on at the tick rate.
        if (Data is not null)
        {
            _serverTick += delta * Data.Net.TickRate;
        }
    }

    public void SendInputs(IReadOnlyList<PlayerInput> inputs)
    {
        if (inputs.Count > 0)
        {
            LastSentSeq = Math.Max(LastSentSeq, inputs[^1].Seq);
        }

        Replicator.SendInputs(WireFormat.EncodeInputs(inputs));
    }

    public void SubmitEdit(VoxelEdit edit) => Replicator.RequestEdit(edit);

    public void RequestPlace(string kind, BlockPos origin, int facing) => Replicator.RequestPlace(kind, origin, facing);

    public void RequestRemove(int objectId) => Replicator.RequestRemove(objectId);

    /// <summary>What we compare with the server's hash: our world and our objects.</summary>
    public ulong ComputeHash() => GameHash.Of(World!, Objects!);

    public IReadOnlyDictionary<int, RemoteState> SampleRemotePlayers() => Sample(_others);

    public IReadOnlyDictionary<int, RemotePlane> SampleRemotePlanes() => Sample(_planes);

    /// <summary>
    /// Falling bombs, simulated here from their drop. Other players' bombs are shown at the same delay as
    /// their planes; our own at the present, to line up with our predicted plane.
    /// </summary>
    public IReadOnlyDictionary<int, (BombState State, bool Nuke)> SampleBombs()
    {
        if (Data is null || World is null)
        {
            return new Dictionary<int, (BombState, bool)>();
        }

        int me = Multiplayer.GetUniqueId();
        double delayed = _serverTick - Data.Net.InterpolationDelayTicks;
        return _bombs.ToDictionary(
            b => b.Key,
            b => (b.Value.At(b.Value.Drop.OwnerPeerId == me ? _serverTick : delayed, World, Data.Rules, Data.Net.TickSeconds), b.Value.Drop.Nuke));
    }

    private IReadOnlyDictionary<int, T> Sample<T>(Dictionary<int, InterpolationBuffer<T>> buffers)
    {
        double renderTick = _serverTick - (Data?.Net.InterpolationDelayTicks ?? 0);
        return buffers.Where(b => !b.Value.IsEmpty).ToDictionary(b => b.Key, b => b.Value.Sample(renderTick));
    }

    /// <summary>Introduces us to the server, which then assigns a team (or gives our old seat back) and sends the world.</summary>
    public void SayHello(string name, Team team, string token) => Replicator.SayHello(name, team, token);

    public void SetConcedeVote(bool vote) => Replicator.SetConcedeVote(vote);

    public RosterEntry? Player(int peer) => _roster.TryGetValue(peer, out RosterEntry entry) ? entry : null;

    private void OnWorld(WorldInit init)
    {
        bool reset = World is not null;
        Data = init.Data;
        DevMode = init.DevMode;
        PresetName = init.PresetName;
        Preset = Data.Presets.TryGetValue(init.PresetName, out MatchPreset? preset) ? preset : null;
        Data.ApplyTickRate();
        IReadOnlyList<VoxelEdit> edits = WireFormat.DecodeEdits(init.EditsData);
        World = IslandGenerator.Rebuild(Data.Terrain, init.Seed, edits);
        Objects = new ObjectStore(Data.Kinds);
        IReadOnlyList<GameObject> objects = WireFormat.DecodeObjects(init.ObjectsData);
        foreach (GameObject obj in objects)
        {
            Objects.Set(obj);
        }

        ulong hash = ComputeHash();
        WorldMatchedServer = hash == init.Hash;
        if (WorldMatchedServer)
        {
            GD.Print($"[client] rebuilt the world: seed {init.Seed} plus {edits.Count} edits and {objects.Count} objects, hash {hash:x16} matches the server");
        }
        else
        {
            GD.PushError($"[client] rebuilt world hash {hash:x16} differs from the server's {init.Hash:x16}");
        }

        if (reset)
        {
            // Everything from the last match is gone; wait for our new spawn in the next snapshot.
            _joined = false;
            _others.Clear();
            _planes.Clear();
            _bombs.Clear();
            WorldReset?.Invoke();
        }
    }

    private void OnBombDropped(BombDrop drop) => _bombs[drop.Id] = new FallingBomb(drop);

    private void OnRoster(byte[] data) => _roster = WireFormat.DecodeRoster(data).ToDictionary(e => e.PeerId);

    private void OnKicked(string reason) => KickedOut?.Invoke(reason);

    private void OnObjects(byte[] data)
    {
        if (Objects is null)
        {
            return;
        }

        try
        {
            foreach (GameObject obj in WireFormat.DecodeObjects(data))
            {
                Objects.Set(obj);
            }
        }
        catch (InvalidDataException e)
        {
            GD.PushWarning($"[client] bad object message: {e.Message}");
        }
    }

    private void OnObjectRemoved(int id) => Objects?.Remove(id);

    private void OnTeamState(byte[] data) => TeamState = WireFormat.DecodeTeamState(data);

    private void OnExplosion(Explosion explosion)
    {
        _bombs.Remove(explosion.BombId);
        if (World is null)
        {
            return;
        }

        foreach (VoxelEdit edit in WireFormat.DecodeEdits(explosion.EditsData))
        {
            OnEdit(edit);
        }

        Exploded?.Invoke(explosion);
    }

    private void OnEdit(VoxelEdit edit)
    {
        if (World is not null && World.ApplyEdit(edit))
        {
            WorldChanged?.Invoke(edit);
        }
    }

    private void OnSnapshot(byte[] data)
    {
        if (World is null)
        {
            return;
        }

        Snapshot snapshot;
        try
        {
            snapshot = WireFormat.DecodeSnapshot(data);
        }
        catch (InvalidDataException e)
        {
            GD.PushWarning($"[client] bad snapshot: {e.Message}");
            return;
        }

        Match = snapshot.Match;
        NukeReadyIn = snapshot.NukeReadyIn;

        // Follow the server's clock, but only jump when it has drifted, so remote players move evenly.
        if (Math.Abs(_serverTick - snapshot.Tick) > Data!.Net.SnapshotIntervalTicks * 2)
        {
            _serverTick = snapshot.Tick;
        }

        int me = Multiplayer.GetUniqueId();
        Dictionary<int, PlaneSnapshot> planes = snapshot.Planes.ToDictionary(p => p.Id);
        foreach (PlayerSnapshot player in snapshot.Players)
        {
            if (player.PeerId == me)
            {
                PawnState pawn = planes.TryGetValue(player.PlaneId, out PlaneSnapshot plane)
                    ? new PawnState(player.State, plane.Id, plane.State)
                    : PawnState.OnFoot(player.State);
                if (!_joined)
                {
                    _joined = true;
                    Joined?.Invoke(pawn);
                }
                else
                {
                    OwnStateReceived?.Invoke(pawn, snapshot.AckSeq);
                }

                continue;
            }

            Buffer(_others, player.PeerId, InterpolationBuffers.ForPlayer)
                .Add(snapshot.Tick, new RemoteState(player.State.Position, player.Yaw, player.Pitch, player.State.Flying, player.PlaneId != 0));
        }

        foreach (PlaneSnapshot plane in snapshot.Planes)
        {
            Buffer(_planes, plane.Id, InterpolationBuffers.ForPlane)
                .Add(snapshot.Tick, new RemotePlane(plane.State.Position, plane.State.Rotation, plane.State.Throttle, plane.PilotPeerId, plane.Team));
        }

        Prune(_others, snapshot.Players.Select(p => p.PeerId).ToHashSet());
        Prune(_planes, planes.Keys.ToHashSet());
    }

    private static InterpolationBuffer<T> Buffer<T>(Dictionary<int, InterpolationBuffer<T>> buffers, int id, Func<InterpolationBuffer<T>> create)
    {
        if (!buffers.TryGetValue(id, out InterpolationBuffer<T>? buffer))
        {
            buffer = create();
            buffers[id] = buffer;
        }

        return buffer;
    }

    /// <summary>Forgets players who left and planes that are gone.</summary>
    private static void Prune<T>(Dictionary<int, InterpolationBuffer<T>> buffers, HashSet<int> present)
    {
        foreach (int gone in buffers.Keys.Where(id => !present.Contains(id)).ToList())
        {
            buffers.Remove(gone);
        }
    }

    /// <summary>
    /// A bomb between its drop and the server's explosion. Steps forward only as far as asked, and stops
    /// where it would hit something, waiting there for the server to say what really happened.
    /// </summary>
    private sealed class FallingBomb
    {
        private BombState _state;
        private uint _tick;
        private bool _landed;

        public FallingBomb(BombDrop drop)
        {
            Drop = drop;
            _state = drop.State;
            _tick = drop.Tick;
        }

        public BombDrop Drop { get; }

        public BombState At(double tick, VoxelWorld world, Sim.GameRules rules, float dt)
        {
            while (!_landed && _tick + 1 <= tick)
            {
                (BombState next, BombImpact? impact) = BombFlight.Step(world, rules.Bomb, rules.SeaLevel, _state, dt);
                _tick++;
                if (impact is { } hit)
                {
                    _state = next with { Position = hit.Position };
                    _landed = true;
                }
                else
                {
                    _state = next;
                }
            }

            return _state;
        }
    }
}
