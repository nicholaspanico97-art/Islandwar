using System.Collections.Generic;
using System.Linq;
using IslandWar.Sim.Match;
using IslandWar.Sim.Voxels;

namespace IslandWar.Client.Bots;

/// <summary>
/// The match-flow bot (role <c>watcher</c>) for the automated match test, run on the fast <c>test</c> preset
/// with one watcher per team. It places a block in warmup, then follows the match: the start must reset the
/// world (fresh island, block gone, hash matching the server's), the phases must come in order, the time
/// limit must end it, and the server must go back to warmup. Then it compares world hashes as usual.
/// </summary>
public partial class MatchBot : TestBot
{
    private readonly List<string> _seen = new();
    private bool _worldReset;
    private BlockPos _warmupBlock;
    private EndReason _endReason;

    protected override void Start()
    {
        Net.WorldReset += () => _worldReset = true;
        if (Net.Match.Stage is not (MatchStage.Warmup or MatchStage.Countdown))
        {
            Finish(false, $"joined during {Net.Match.Stage}; the test needs a fresh server");
            return;
        }

        // Something to be wiped by the reset: a plank next to the middle of our own island.
        (int x, int z) = Net.Data!.Terrain.IslandCentre(Net.MyTeam.IslandIndex());
        _warmupBlock = new BlockPos(x + 2, World.HighestSolid(x + 2, z)!.Value + 1, z);
        Net.SubmitEdit(new VoxelEdit(_warmupBlock, Block.Planks));
        Log($"on {Net.MyTeam}; placed a plank at {_warmupBlock} during warmup");
        WaitUntil(() => World.Get(_warmupBlock) == Block.Planks, () =>
        {
            Log("the plank is in; waiting for the match to start");
            WaitUntil(() => Net.Match.Stage == MatchStage.Playing, CheckReset);
        });
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        string now = Net.Match.Stage == MatchStage.Playing ? $"phase {Net.Match.Phase}" : Net.Match.Stage.ToString();
        if (_seen.Count == 0 || _seen[^1] != now)
        {
            _seen.Add(now);
        }
    }

    private void CheckReset()
    {
        // The fresh world and the first snapshot of the match can arrive in either order; give it a moment.
        WaitUntil(() => _worldReset, () =>
        {
            if (!Net.WorldMatchedServer || World.Deltas.Count != 0 || World.Get(_warmupBlock) != Block.Air)
            {
                Finish(false, $"the match started but the world wasn't reset (edits {World.Deltas.Count}, block {World.Get(_warmupBlock)})");
                return;
            }

            Log("the match started and the island was reset");
            WaitUntil(() => Net.Match.Stage == MatchStage.Ended, () =>
            {
                _endReason = Net.Match.Reason;
                Log($"the match ended: {(Net.Match.Winner == Team.None ? "draw" : Net.Match.Winner + " wins")} ({Net.Match.Reason})");
                WaitUntil(() => Net.Match.Stage is MatchStage.Warmup or MatchStage.Countdown, CheckSequence);
            });
        });
    }

    private void CheckSequence()
    {
        // The stages seen, in order, from the plank onward. A countdown may show up again at the end, because
        // both watchers are still connected when the server returns to warmup.
        string[] expected = { "phase 1", "phase 2", "phase 3", "Ended" };
        string[] seen = _seen.SkipWhile(s => s != "phase 1").Take(expected.Length).ToArray();
        Log($"stages seen: {string.Join(" -> ", _seen)}");
        if (!seen.SequenceEqual(expected) || _endReason != EndReason.TimeLimit)
        {
            Finish(false, $"expected {string.Join(" -> ", expected)}, ending on the time limit");
            return;
        }

        CompareWithServer();
    }
}
