using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using IslandWar.Client.Game;
using IslandWar.Net;
using IslandWar.Sim.Players;
using IslandWar.Sim.Protocol;
using IslandWar.Sim.Voxels;

namespace IslandWar.Client.Online;

/// <summary>
/// The client side of a connection, without any rendering, so the sync-test bots use it too. It rebuilds
/// the world from what the server sends on join, applies the server's edits, hands the local player's
/// server state to whoever predicts it, and keeps other players' snapshots for interpolation.
/// </summary>
public partial class NetClient : Node, IGameLink
{
    private readonly Dictionary<int, InterpolationBuffer> _others = new();
    private double _serverTick;
    private bool _joined;

    public Replicator Replicator { get; init; } = null!;

    public GameData? Data { get; private set; }

    public VoxelWorld? World { get; private set; }

    /// <summary>True if the world rebuilt on join had the same hash as the server's.</summary>
    public bool WorldMatchedServer { get; private set; }

    /// <summary>The world is rebuilt and the server has placed us: the game can start here.</summary>
    public event Action<PlayerState>? Joined;

    /// <summary>The server changed a block (or undid one of our predictions); it's already in <see cref="World"/>.</summary>
    public event Action<VoxelEdit>? WorldChanged;

    /// <summary>The server's state for our player after it processed the given input.</summary>
    public event Action<PlayerState, uint>? OwnStateReceived;

    public override void _Ready()
    {
        Name = "NetClient";
        Replicator.WorldReceived += OnWorld;
        Replicator.EditReceived += OnEdit;
        Replicator.SnapshotReceived += OnSnapshot;
    }

    public override void _Process(double delta)
    {
        // Between snapshots, the server's clock moves on at the tick rate.
        if (Data is not null)
        {
            _serverTick += delta * Data.Net.TickRate;
        }
    }

    public void SendInputs(IReadOnlyList<PlayerInput> inputs) => Replicator.SendInputs(WireFormat.EncodeInputs(inputs));

    public void SubmitEdit(VoxelEdit edit) => Replicator.RequestEdit(edit);

    public IReadOnlyDictionary<int, RemoteState> SampleRemotePlayers()
    {
        double renderTick = _serverTick - (Data?.Net.InterpolationDelayTicks ?? 0);
        return _others.Where(o => !o.Value.IsEmpty).ToDictionary(o => o.Key, o => o.Value.Sample(renderTick));
    }

    private void OnWorld(WorldInit init)
    {
        Data = init.Data;
        Data.ApplyTickRate();
        IReadOnlyList<VoxelEdit> edits = WireFormat.DecodeEdits(init.EditsData);
        World = IslandGenerator.Rebuild(Data.Terrain, init.Seed, edits);
        ulong hash = World.ComputeHash();
        WorldMatchedServer = hash == init.Hash;
        if (WorldMatchedServer)
        {
            GD.Print($"[client] rebuilt the world: seed {init.Seed} plus {edits.Count} edits, hash {hash:x16} matches the server");
        }
        else
        {
            GD.PushError($"[client] rebuilt world hash {hash:x16} differs from the server's {init.Hash:x16}");
        }
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

        // Follow the server's clock, but only jump when it has drifted, so remote players move evenly.
        if (Math.Abs(_serverTick - snapshot.Tick) > Data!.Net.SnapshotIntervalTicks * 2)
        {
            _serverTick = snapshot.Tick;
        }

        int me = Multiplayer.GetUniqueId();
        foreach (PlayerSnapshot player in snapshot.Players)
        {
            if (player.PeerId == me)
            {
                if (!_joined)
                {
                    _joined = true;
                    Joined?.Invoke(player.State);
                }
                else
                {
                    OwnStateReceived?.Invoke(player.State, snapshot.AckSeq);
                }

                continue;
            }

            if (!_others.TryGetValue(player.PeerId, out InterpolationBuffer? buffer))
            {
                buffer = new InterpolationBuffer();
                _others[player.PeerId] = buffer;
            }

            buffer.Add(snapshot.Tick, new RemoteState(player.State.Position, player.Yaw, player.Pitch, player.State.Flying));
        }

        HashSet<int> present = snapshot.Players.Select(p => p.PeerId).ToHashSet();
        foreach (int gone in _others.Keys.Where(peer => !present.Contains(peer)).ToList())
        {
            _others.Remove(gone);
        }
    }
}
