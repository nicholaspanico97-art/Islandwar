using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using IslandWar.Client.Online;
using IslandWar.Sim.Voxels;

namespace IslandWar.Client.Bots;

/// <summary>
/// A headless client for the automated sync test (see docs/testing.md). Two roles:
/// <list type="bullet">
/// <item><b>builder</b> joins first, builds a tower next to the island centre, then waits for the
/// breaker to knock its top off.</item>
/// <item><b>breaker</b> joins late, checks the tower is already in the world it rebuilt (late join),
/// and breaks the top two blocks.</item>
/// </list>
/// Each bot then asks the server for its world hash and exits 0 if it matches its own, 1 otherwise.
/// The hash reply shares the reliable channel with edits, so it arrives after every edit sent before it.
/// </summary>
public partial class SyncBot : Node
{
    private const int TowerHeight = 5;
    private const int BlocksBroken = 2;
    private const double TimeoutSeconds = 60;

    private Func<bool>? _waitUntil;
    private Action? _then;
    private double _elapsed;
    private bool _finished;

    public NetClient Net { get; init; } = null!;

    public string Role { get; init; } = "builder";

    public override void _Ready()
    {
        Name = "SyncBot";
        Net.Replicator.HashReceived += OnServerHash;
        if (!Net.WorldMatchedServer)
        {
            Finish(false, "the world rebuilt on join didn't match the server's");
            return;
        }

        if (Role == "builder")
        {
            Build();
        }
        else
        {
            Break();
        }
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

    private VoxelWorld World => Net.World!;

    /// <summary>The tower goes two blocks east of the island centre, on whatever ground is there.</summary>
    private (int X, int Z) TowerColumn => (World.SizeX / 2 + 2, World.SizeZ / 2);

    private int Planks(int baseY) => Enumerable.Range(baseY, TowerHeight).Count(y => World.Get(new BlockPos(TowerColumn.X, y, TowerColumn.Z)) == Block.Planks);

    private void Build()
    {
        (int x, int z) = TowerColumn;
        int baseY = World.HighestSolid(x, z)!.Value + 1;
        for (int y = baseY; y < baseY + TowerHeight; y++)
        {
            Net.SubmitEdit(new VoxelEdit(new BlockPos(x, y, z), Block.Planks));
        }

        Log($"asked to build a {TowerHeight}-block tower at ({x}, {baseY}..{baseY + TowerHeight - 1}, {z})");
        WaitUntil(() => Planks(baseY) == TowerHeight, () =>
        {
            // The orchestrating script starts the breaker when it sees this line.
            Log("built the tower; waiting for the breaker");
            WaitUntil(() => Planks(baseY) == TowerHeight - BlocksBroken, CompareWithServer);
        });
    }

    private void Break()
    {
        (int x, int z) = TowerColumn;
        int top = World.HighestSolid(x, z)!.Value;
        if (World.Get(new BlockPos(x, top, z)) != Block.Planks)
        {
            Finish(false, $"joined late but there is no tower at ({x}, {z}): late join lost the builder's edits");
            return;
        }

        int baseY = top - TowerHeight + 1;
        if (Planks(baseY) != TowerHeight)
        {
            Finish(false, $"joined late and found {Planks(baseY)} tower blocks, expected {TowerHeight}");
            return;
        }

        Log($"joined late and found the {TowerHeight}-block tower; breaking the top {BlocksBroken}");
        for (int y = top; y > top - BlocksBroken; y--)
        {
            Net.SubmitEdit(new VoxelEdit(new BlockPos(x, y, z), Block.Air));
        }

        WaitUntil(() => Planks(baseY) == TowerHeight - BlocksBroken, CompareWithServer);
    }

    private void CompareWithServer()
    {
        Log("asking the server for its world hash");
        Net.Replicator.RequestHash();
    }

    private void OnServerHash(ulong serverHash)
    {
        ulong mine = World.ComputeHash();
        Finish(mine == serverHash, $"world hash {mine:x16}, server {serverHash:x16}");
    }

    private void WaitUntil(Func<bool> condition, Action then)
    {
        _waitUntil = condition;
        _then = then;
    }

    private void Finish(bool passed, string detail)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        Log($"{(passed ? "PASS" : "FAIL")}: {detail}");
        GetTree().Quit(passed ? 0 : 1);
    }

    private void Log(string message) => GD.Print($"[bot {Role}] {message}");
}
