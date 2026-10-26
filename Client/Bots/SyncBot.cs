using System.Linq;
using IslandWar.Sim.Voxels;

namespace IslandWar.Client.Bots;

/// <summary>
/// Block sync bots for the automated test (see docs/testing.md):
/// <list type="bullet">
/// <item><b>builder</b> joins first, builds a tower next to the island centre, then waits for the
/// breaker to knock its top off and the pilot to bomb it.</item>
/// <item><b>breaker</b> joins late, checks the tower is already in the world it rebuilt (late join),
/// and breaks the top two blocks.</item>
/// </list>
/// Each then compares its world hash with the server's.
/// </summary>
public partial class SyncBot : TestBot
{
    private const int TowerHeight = 5;
    private const int BlocksBroken = 2;

    /// <summary>The tower goes two blocks east of the island centre, on whatever ground is there.</summary>
    private (int X, int Z) TowerColumn => (World.SizeX / 2 + 2, World.SizeZ / 2);

    protected override void Start()
    {
        if (Role == "builder")
        {
            Build();
        }
        else
        {
            Break();
        }
    }

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
            WaitUntil(() => Planks(baseY) == TowerHeight - BlocksBroken, () =>
            {
                Log("the breaker took the top off; waiting for the pilot's bomb");
                WaitUntil(() => Planks(baseY) < TowerHeight - BlocksBroken, CompareWithServer);
            });
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
}
