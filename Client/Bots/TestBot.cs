using System;
using Godot;
using IslandWar.Client.Online;
using IslandWar.Sim.Match;
using IslandWar.Sim.Voxels;

namespace IslandWar.Client.Bots;

/// <summary>
/// Shared machinery for the headless test bots: wait-then-do steps, a timeout, the world-hash check
/// against the server, and exiting with 0 (pass) or 1 (fail) for the script that runs them.
/// </summary>
public abstract partial class TestBot : Node
{
    private const double TimeoutSeconds = 150;

    private Func<bool>? _waitUntil;
    private Action? _then;
    private double _elapsed;
    private bool _finished;

    public NetClient Net { get; init; } = null!;

    public string Role { get; init; } = "";

    protected VoxelWorld World => Net.World!;

    /// <summary>Where the bot's pawn is, for <see cref="BotView"/>; null for bots that don't move.</summary>
    public virtual System.Numerics.Vector3? Eye => null;

    /// <summary>The builder's tower column: two blocks east of the centre of Red's (west) island.</summary>
    protected (int X, int Z) TowerColumn
    {
        get
        {
            (int x, int z) = Net.Data!.Terrain.IslandCentre(Team.Red.IslandIndex());
            return (x + 2, z);
        }
    }

    public override void _Ready()
    {
        Name = "TestBot";
        Net.Replicator.HashReceived += OnServerHash;
        if (!Net.WorldMatchedServer)
        {
            Finish(false, "the world rebuilt on join didn't match the server's");
            return;
        }

        Start();
    }

    public override void _Process(double delta)
    {
        if (_finished)
        {
            return;
        }

        _elapsed += delta;
        if (_elapsed > TimeoutSeconds)
        {
            Finish(false, $"timed out after {TimeoutSeconds} s");
        }
        else if (_waitUntil?.Invoke() == true)
        {
            Action? then = _then;
            _waitUntil = null;
            _then = null;
            then?.Invoke();
        }
    }

    /// <summary>Runs once the world is ready.</summary>
    protected abstract void Start();

    protected void WaitUntil(Func<bool> condition, Action then)
    {
        _waitUntil = condition;
        _then = then;
    }

    /// <summary>Waits a fixed time: for things the server answers by not doing anything, like a refused edit.</summary>
    protected void WaitSeconds(double seconds, Action then)
    {
        double until = _elapsed + seconds;
        WaitUntil(() => _elapsed >= until, then);
    }

    /// <summary>
    /// Asks the server for its game hash (world and objects); the bot passes if it equals its own. The reply shares the
    /// reliable channel with edits, so it arrives after every edit the server sent before it.
    /// </summary>
    protected void CompareWithServer()
    {
        Log("asking the server for its game hash");
        Net.Replicator.RequestHash();
    }

    protected void Finish(bool passed, string detail)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        Log($"{(passed ? "PASS" : "FAIL")}: {detail}");
        GetTree().Quit(passed ? 0 : 1);
    }

    protected void Log(string message) => GD.Print($"[bot {Role}] {message}");

    private void OnServerHash(ulong serverHash)
    {
        ulong mine = Net.ComputeHash();
        Finish(mine == serverHash, $"game hash {mine:x16}, server {serverHash:x16}");
    }
}
