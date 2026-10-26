using System.Linq;
using IslandWar.Sim.Voxels;

namespace IslandWar.Client.Bots;

/// <summary>
/// Block sync bots for the automated test (see docs/testing.md), both on Red:
/// <list type="bullet">
/// <item><b>builder</b> joins first and builds a 5-block tower just east of Red's core. It waits for the
/// breaker's work and then for the pilot's bomb.</item>
/// <item><b>breaker</b> joins late and checks the tower is already in the world it rebuilt (late join).
/// It tries to break the builder's top block, which team build protection must refuse; then it adds two
/// planks of its own on top and breaks the upper one, which it may.</item>
/// </list>
/// Each then compares its world hash, which covers block owners too, with the server's.
/// </summary>
public partial class SyncBot : TestBot
{
    private const int TowerHeight = 5;

    /// <summary>How long the breaker gives the server to refuse a protected break.</summary>
    private const double RefusalSeconds = 1.5;

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

    private BlockPos At(int y) => new(TowerColumn.X, y, TowerColumn.Z);

    /// <summary>Planks in the tower column from <paramref name="baseY"/> up, including anything added on top.</summary>
    private int Planks(int baseY) => Enumerable.Range(baseY, TowerHeight + 3).Count(y => World.Get(At(y)) == Block.Planks);

    private void Build()
    {
        int baseY = World.HighestSolid(TowerColumn.X, TowerColumn.Z)!.Value + 1;
        for (int y = baseY; y < baseY + TowerHeight; y++)
        {
            Net.SubmitEdit(new VoxelEdit(At(y), Block.Planks));
        }

        Log($"asked to build a {TowerHeight}-block tower at ({TowerColumn.X}, {baseY}..{baseY + TowerHeight - 1}, {TowerColumn.Z})");
        WaitUntil(() => Planks(baseY) == TowerHeight, () =>
        {
            // The orchestrating script starts the breaker when it sees this line.
            Log("built the tower; waiting for the breaker");

            // The breaker's end state: our 5, plus its block on top, with the one above that broken again.
            WaitUntil(() => Planks(baseY) == TowerHeight + 1 && World.Get(At(baseY + TowerHeight)) == Block.Planks, () =>
            {
                Log("the breaker's plank is on top; waiting for the pilot's bomb");
                WaitUntil(() => Planks(baseY) < TowerHeight + 1, CompareWithServer);
            });
        });
    }

    private void Break()
    {
        int top = World.HighestSolid(TowerColumn.X, TowerColumn.Z)!.Value;
        int baseY = top - TowerHeight + 1;
        if (World.Get(At(top)) != Block.Planks || Planks(baseY) != TowerHeight)
        {
            Finish(false, $"joined late and found {Planks(baseY)} tower blocks, expected {TowerHeight}: late join lost the builder's edits");
            return;
        }

        BlockOwner owner = World.OwnerOf(At(top));
        if (owner.IsNobody || owner.PlayerId == Net.MyPlayerId || owner.Team != Net.MyTeam)
        {
            Finish(false, $"the tower should belong to a teammate, but its owner is {owner}");
            return;
        }

        Log($"joined late and found the {TowerHeight}-block tower; trying to break a teammate's block");
        Net.SubmitEdit(new VoxelEdit(At(top), Block.Air));
        WaitSeconds(RefusalSeconds, () =>
        {
            if (World.Get(At(top)) != Block.Planks)
            {
                Finish(false, "broke a teammate's block: team build protection didn't hold");
                return;
            }

            Log("the teammate's block is protected; adding two planks of our own and breaking one");
            Net.SubmitEdit(new VoxelEdit(At(top + 1), Block.Planks));
            Net.SubmitEdit(new VoxelEdit(At(top + 2), Block.Planks));
            WaitUntil(() => World.Get(At(top + 2)) == Block.Planks && World.OwnerOf(At(top + 2)).PlayerId == Net.MyPlayerId, () =>
            {
                Net.SubmitEdit(new VoxelEdit(At(top + 2), Block.Air));
                WaitUntil(() => World.Get(At(top + 2)) == Block.Air, CompareWithServer);
            });
        });
    }
}
