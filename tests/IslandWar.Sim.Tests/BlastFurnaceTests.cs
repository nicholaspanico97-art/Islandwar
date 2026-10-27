using System.Numerics;
using IslandWar.Sim.Items;
using IslandWar.Sim.Match;
using IslandWar.Sim.Objects;
using IslandWar.Sim.Research;
using IslandWar.Sim.Server;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Tests;

/// <summary>The blast furnace and the step into the Iron Age (Bronze Age piece 4).</summary>
public class BlastFurnaceTests
{
    private static readonly GameData Data = GameData.Load(name => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", name)));

    private static TechTree Tree => Data.Techs;

    [Fact]
    public void TheTreeFollowsTheDevelopersNotes()
    {
        Assert.Equal(new[] { "prospecting", "fortification" }, Tree.Tech("blast_furnace")!.Requires);
        Assert.Equal(new[] { "bronze_tools" }, Tree.Tech("shipbuilding")!.Requires);
        Era bronze = Tree.Eras[1];
        Assert.Equal(new[] { "blast_furnace" }, bronze.Advance);
        Assert.Equal(new[] { "blast_furnace" }, bronze.AdvanceBuilds);
        Assert.Equal(new[] { "blast_furnace" }, Data.Items.SmeltingFor("iron_ore")!.Smelters);
        Assert.Contains("Smithing", Tree.UnlockedBy(Unlock.Recipe("iron_pickaxe")));
    }

    [Fact]
    public void AnEraThatNeedsABuildingWaitsForIt()
    {
        var team = new TeamState(Team.Red) { Era = 1, Researched = new List<string> { "blast_furnace" } };

        Assert.Equal(1, Tree.Advanced(team).Era);
        Assert.Equal(1, Tree.Advanced(team, kind => kind == "furnace").Era);
        Assert.Equal(2, Tree.Advanced(team, kind => kind == "blast_furnace").Era);
    }

    [Fact]
    public void ResearchingTheBlastFurnaceAloneDoesntStartTheIronAgeButBuildingOneDoes()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Smith", Team.Red);
        test.Research(Team.Red, "mining", "logging", "smelting", "bronze_working", "bronze_tools", "prospecting", "fortification");
        Assert.Equal(1, test.State.TeamStates[Team.Red].Era);
        test.State.TeamStates[Team.Red] = test.State.TeamStates[Team.Red] with { Points = ItemCounts.None.Plus("bronze", 35) };

        test.Server.ResearchRequested(2, "blast_furnace");
        Assert.True(test.State.TeamStates[Team.Red].HasResearched("blast_furnace"));
        Assert.Equal(1, test.State.TeamStates[Team.Red].Era);

        GameObject blueprint = PlaceBlastFurnace(test, player);
        Assert.True(blueprint.IsBlueprint());
        Assert.Equal(1, test.State.TeamStates[Team.Red].Era);

        Deliver(test, player, blueprint);
        Assert.Equal(2, test.State.TeamStates[Team.Red].Era);
        Assert.Contains(test.Log, line => line.Contains("Red entered the Iron Age"));
    }

    [Fact]
    public void IronOreOnlySmeltsInABlastFurnace()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Smith", Team.Red);
        test.Research(Team.Red, "mining", "logging", "smelting", "bronze_working", "bronze_tools", "prospecting", "fortification", "blast_furnace");
        GameObject furnace = InventoryTests.PlaceCrate(test, player, "furnace");
        player.Backpack = ItemCounts.None.Plus("iron_ore", 3);

        test.Server.TransferRequested(2, furnace.Id, ItemCounts.InputSlot, "iron_ore", 3, intoStructure: true);
        Assert.Equal(3, player.Backpack.Of("iron_ore"));
        Assert.Contains(test.Log, line => line.Contains("doesn't go in the input of a Furnace"));

        test.Server.Structures.Remove(furnace);
        GameObject blast = PlaceBlastFurnace(test, player);
        Deliver(test, player, blast);
        player.Backpack = ItemCounts.None.Plus("iron_ore", 3).Plus("charcoal", 2);
        test.Server.TransferRequested(2, blast.Id, ItemCounts.InputSlot, "iron_ore", 3, intoStructure: true);
        test.Server.TransferRequested(2, blast.Id, ItemCounts.FuelSlot, "charcoal", 2, intoStructure: true);

        test.Run(test.State.Data.Net.TickRate * 30);

        Assert.Equal(3, ItemCounts.Contents(test.State.Objects.Get(blast.Id)!, ItemCounts.OutputSlot).Of("iron_ingot"));
        Assert.True(test.State.TeamStates[Team.Red].Points.Of("iron") >= 6);
    }

    [Fact]
    public void MakingIronToolsEarnsIronAgePoints()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Smith", Team.Red);
        test.Research(Team.Red, "mining", "logging", "smithing");
        GameObject bench = InventoryTests.PlaceCrate(test, player, "workbench");
        player.Backpack = ItemCounts.None.Plus("iron_ingot", 3).Plus("planks", 2);

        test.Server.CraftRequested(2, "iron_pickaxe", bench.Id);

        Assert.Equal(1, player.Backpack.Of("iron_pickaxe"));
        Assert.Equal(4, test.State.TeamStates[Team.Red].Points.Of("iron"));
    }

    /// <summary>A blast furnace blueprint beside the player, with the ground under it levelled and the space cleared.</summary>
    private static GameObject PlaceBlastFurnace(TestServer test, ServerPlayer player)
    {
        ObjectKind kind = test.State.Data.Kinds.Get("blast_furnace")!;
        Vector3 feet = player.Pawn.Player.Position;
        var origin = new BlockPos((int)MathF.Floor(feet.X) + 2, (int)MathF.Floor(feet.Y), (int)MathF.Floor(feet.Z));
        foreach (BlockPos cell in GameObject.FootprintOf(kind, origin, 0))
        {
            test.Server.World.SetGenerated(cell, Block.Air);
            test.Server.World.SetGenerated(cell with { Y = origin.Y - 1 }, Block.Stone);
        }

        test.Server.PlaceRequested(player.Peer, kind.Id, origin, 0);
        return test.State.Objects.At(origin) ?? throw new InvalidOperationException(string.Join("\n", test.Log));
    }

    private static void Deliver(TestServer test, ServerPlayer player, GameObject blueprint)
    {
        IReadOnlyDictionary<string, int> cost = test.State.Data.Kinds.Get(blueprint.Kind)!.Cost;
        player.Backpack = cost;
        foreach ((string item, int count) in cost)
        {
            test.Server.TransferRequested(player.Peer, blueprint.Id, Blueprint.DeliveredSlot, item, count, intoStructure: true);
        }

        Assert.False(test.State.Objects.Get(blueprint.Id)!.IsBlueprint(), string.Join("\n", test.Log));
    }
}

/// <summary>The bronze playtest preset: real rules, but a head start.</summary>
public class PlaytestPresetTests
{
    [Fact]
    public void TheBronzePresetStartsInTheBronzeAgeWithAKitAndAStockedDepot()
    {
        var test = new TestServer("bronze");
        ServerPlayer player = test.Join(2, "Tester", Team.Red);

        TeamState red = test.State.TeamStates[Team.Red];
        Assert.Equal(1, red.Era);
        Assert.True(red.HasResearched("prospecting"));
        Assert.Equal(60, red.Points.Of("bronze"));
        Assert.Equal(1, player.Backpack.Of("bow"));
        Assert.False(test.State.Creative);
        foreach (Team team in Teams.Playing)
        {
            GameObject depot = Assert.Single(test.State.Objects.All, o => o.Team == team);
            Assert.Equal(30, ItemCounts.Contents(depot).Of("bronze_ingot"));
            Assert.Equal(200, test.State.TeamStates[team].Amount("planks"));
        }
    }
}
