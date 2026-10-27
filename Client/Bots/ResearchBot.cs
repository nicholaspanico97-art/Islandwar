using System.Collections.Generic;
using System.Linq;
using IslandWar.Sim.Items;
using IslandWar.Sim.Objects;
using IslandWar.Sim.Players;
using IslandWar.Sim.Research;
using IslandWar.Sim.Voxels;

namespace IslandWar.Client.Bots;

/// <summary>
/// The research bot for the automated match test (see docs/testing.md): alone on Red on a real-match preset,
/// so warmup never ends and nothing resets. From where it spawns it mines stone in reach by hand, one block
/// per hardness, until the team has the Stone Age points for Mining and Logging; researches both; checks that
/// placing stone is still refused (it needs Fortification); puts down a depot blueprint and delivers its stone
/// to it; then compares its game hash with the server's.
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
            PlaceBlueprint();
        });
    }

    /// <summary>A depot blueprint in reach, then our stone delivered to it: it must show on the blueprint.</summary>
    private void PlaceBlueprint()
    {
        ObjectKind depot = Net.Objects!.Kinds.Get("depot")!;
        System.Numerics.Vector3 eye = Eye!.Value;
        System.Numerics.Vector3 feet = Spawn.Player.Position;
        int reach = (int)Net.Data!.Player.Reach;
        BlockPos? spot = (
            from y in Enumerable.Range((int)eye.Y - reach, reach * 2).Reverse() // on the surface, not in the pit it mined
            from x in Enumerable.Range((int)eye.X - reach, reach * 2)
            from z in Enumerable.Range((int)eye.Z - reach, reach * 2)
            let origin = new BlockPos(x, y, z)
            where GameObject.FootprintOf(depot, origin, 0).Any(b => BuildRules.InReach(Net.Data.Player, eye, b))
                  && ObjectRules.WhyNot(World, Net.Data.Player, depot, origin, 0, new[] { feet }) is null
            select (BlockPos?)origin).FirstOrDefault();
        if (spot is not { } at)
        {
            Finish(false, "found no room for a depot in reach");
            return;
        }

        Net.RequestPlace(depot.Id, at, 0);
        WaitUntil(() => Net.Objects.At(at) is { } placed && placed.IsBlueprint(), () =>
        {
            GameObject blueprint = Net.Objects.At(at)!;
            int stone = System.Math.Min(Net.Backpack.Of("stone"), depot.Cost.Of("stone"));
            Log($"placed a depot blueprint at {at}; delivering {stone} stone");
            Net.RequestTransfer(blueprint.Id, Blueprint.DeliveredSlot, "stone", stone, true);
            WaitUntil(() => Net.Objects.Get(blueprint.Id) is { } after && Blueprint.Delivered(after).Of("stone") == stone, () =>
            {
                Log($"the blueprint now needs {string.Join(", ", Blueprint.Missing(Net.Objects.Get(blueprint.Id)!, depot, Net.Objects.Kinds).Select(m => $"{m.Value} {m.Key}"))}");
                CompareWithServer();
            });
        });
    }
}
