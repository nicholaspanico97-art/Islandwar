using System.Collections.Generic;
using System.Linq;
using IslandWar.Sim.Items;
using IslandWar.Sim.Players;
using IslandWar.Sim.Research;
using IslandWar.Sim.Voxels;

namespace IslandWar.Client.Bots;

/// <summary>
/// The research bot for the automated match test (see docs/testing.md): alone on Red on a real-match preset,
/// so warmup never ends and nothing resets. From where it spawns it mines stone in reach by hand, one block
/// per hardness, until the team has the Stone Age points for Mining and Logging; researches both; checks that
/// placing stone is still refused (it needs Fortification); then compares its game hash with the server's.
/// </summary>
public partial class ResearchBot : TestBot
{
    /// <summary>A little over the server's mining slack, so no mine is refused as too fast.</summary>
    private const double MiningMarginSeconds = 0.35;

    /// <summary>How long the server gets to refuse placing stone.</summary>
    private const double RefusalSeconds = 1.5;

    private static readonly string[] Techs = { "mining", "logging" };

    private readonly Queue<BlockPos> _toMine = new();

    public PawnState Spawn { get; init; }

    public override System.Numerics.Vector3? Eye => Spawn.Player.Position + System.Numerics.Vector3.UnitY * Net.Data!.Player.EyeHeight;

    private TechTree Tree => Net.Data!.Techs;

    private int Needed => Techs.Sum(t => Tree.Tech(t)!.Points);

    private int Points => Net.TeamState?.Points.Of(Tree.Eras[0].Id) ?? 0;

    protected override void Start()
    {
        System.Numerics.Vector3 eye = Eye!.Value;
        int reach = (int)Net.Data!.Player.Reach;
        var feet = new BlockPos((int)eye.X, (int)eye.Y, (int)eye.Z);
        IEnumerable<BlockPos> near =
            from y in Enumerable.Range(feet.Y - reach, reach)
            from x in Enumerable.Range(feet.X - reach, reach * 2 + 1)
            from z in Enumerable.Range(feet.Z - reach, reach * 2 + 1)
            select new BlockPos(x, y, z);
        foreach (BlockPos block in near.Where(b => World.Get(b) == Block.Stone && BuildRules.InReach(Net.Data.Player, eye, b))
                     .OrderByDescending(b => b.Y).Take(Needed + 4))
        {
            _toMine.Enqueue(block);
        }

        Log($"on {Net.MyTeam}; {_toMine.Count} stone blocks in reach, mining for {Needed} Stone Age points");
        WaitUntil(() => Net.TeamState is not null, MineNext);
    }

    private void MineNext()
    {
        if (Points >= Needed)
        {
            Research(0);
            return;
        }

        if (!_toMine.TryDequeue(out BlockPos block))
        {
            Finish(false, $"ran out of stone in reach with {Points} of {Needed} points");
            return;
        }

        float seconds = Net.Data!.Items.MiningSeconds(Block.Stone, Net.Backpack) ?? 0;
        WaitSeconds(seconds + MiningMarginSeconds, () =>
        {
            Net.SubmitEdit(new VoxelEdit(block, Block.Air));
            MineNext();
        });
    }

    private void Research(int index)
    {
        if (index == Techs.Length)
        {
            Log($"researched {string.Join(" and ", Techs)}; {Points} Stone Age points left");
            TryPlacingStone();
            return;
        }

        string tech = Techs[index];
        Log($"{Points} Stone Age points; researching {tech}");
        Net.RequestResearch(tech);
        WaitUntil(() => Net.TeamState!.HasResearched(tech), () => Research(index + 1));
    }

    /// <summary>Stone in the backpack, but no Fortification yet: the server must put the air back.</summary>
    private void TryPlacingStone()
    {
        if (Net.Backpack.Of("stone") == 0 || Tree.IsUnlocked(Net.TeamState!, Unlock.Place("stone")))
        {
            Finish(false, "expected stone in the backpack and stone placing still locked");
            return;
        }

        System.Numerics.Vector3 eye = Eye!.Value;
        var spot = new BlockPos((int)eye.X + 2, (int)eye.Y, (int)eye.Z);
        if (World.Get(spot) != Block.Air)
        {
            Finish(false, $"expected air at {spot} to try placing stone");
            return;
        }

        Net.SubmitEdit(new VoxelEdit(spot, Block.Stone));
        WaitSeconds(RefusalSeconds, () =>
        {
            if (World.Get(spot) == Block.Stone)
            {
                Finish(false, "placing stone was allowed before Fortification");
                return;
            }

            Log("placing stone was refused (needs Fortification)");
            CompareWithServer();
        });
    }
}
