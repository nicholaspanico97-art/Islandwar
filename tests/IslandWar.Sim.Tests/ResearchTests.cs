using System.Numerics;
using IslandWar.Sim.Items;
using IslandWar.Sim.Match;
using IslandWar.Sim.Objects;
using IslandWar.Sim.Players;
using IslandWar.Sim.Protocol;
using IslandWar.Sim.Research;
using IslandWar.Sim.Server;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Tests;

public class ResearchTests
{
    private static readonly GameData Data = GameData.Load(name => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", name)));

    private static TechTree Tree => Data.Techs;

    private static TeamState Fresh => new(Team.Red);

    [Fact]
    public void TheDataStartsInTheStoneAgeAndBronzeWorkingLeadsOn()
    {
        Assert.Equal("stone", Tree.Eras[0].Id);
        Assert.Equal("bronze", Tree.Eras[1].Id);
        Assert.Equal(new[] { "bronze_working" }, Tree.Eras[0].Advance);
        Assert.Equal("Stone Age", Tree.EraOf(Fresh).Name);
    }

    [Fact]
    public void WhatResearchUnlocksIsLockedUntilThenAndEverythingElseIsOpen()
    {
        Assert.True(Tree.IsUnlocked(Fresh, Unlock.Recipe("planks")));
        Assert.True(Tree.IsUnlocked(Fresh, Unlock.Structure("workbench")));
        Assert.True(Tree.IsUnlocked(Fresh, Unlock.Place("dirt")));
        Assert.False(Tree.IsUnlocked(Fresh, Unlock.Recipe("stone_pickaxe")));
        Assert.False(Tree.IsUnlocked(Fresh, Unlock.Structure("furnace")));
        Assert.False(Tree.IsUnlocked(Fresh, Unlock.Smelting("bronze_ingot")));
        Assert.False(Tree.IsUnlocked(Fresh, Unlock.Place("stone")));
        Assert.False(Tree.IsUnlocked(Fresh, Unlock.Plane));
        Assert.False(Tree.IsUnlocked(Fresh, Unlock.Nuke));
        Assert.Equal("Mining research", Tree.UnlockedBy(Unlock.Recipe("stone_pickaxe")));
        Assert.Equal("the WWI era", Tree.UnlockedBy(Unlock.Plane));
        Assert.Equal("the Cold War", Tree.UnlockedBy(Unlock.Nuke));

        TeamState miner = Fresh with { Researched = new[] { "mining" } };
        Assert.True(Tree.IsUnlocked(miner, Unlock.Recipe("stone_pickaxe")));
        Assert.True(Tree.IsUnlocked(Fresh with { Era = Tree.EraIndex("ww1") }, Unlock.Plane));
    }

    [Fact]
    public void ATechNeedsItsEraItsRequirementsAndThePoints()
    {
        Tech smelting = Tree.Tech("smelting")!;
        Tech tools = Tree.Tech("bronze_tools")!;

        Assert.Contains("Mining", Tree.WhyNot(Fresh with { Points = ItemCounts.None.Plus("stone", 100) }, smelting));
        Assert.Contains("points", Tree.WhyNot(Fresh with { Researched = new[] { "logging", "mining" } }, smelting));
        Assert.Null(Tree.WhyNot(Fresh with { Researched = new[] { "logging", "mining" }, Points = ItemCounts.None.Plus("stone", smelting.Points) }, smelting));
        Assert.Contains("Bronze Age", Tree.WhyNot(Fresh with { Points = ItemCounts.None.Plus("bronze", 100) }, tools));
    }

    [Fact]
    public void ResearchingTheLastAdvanceTechTakesTheTeamIntoTheNextEra()
    {
        TeamState team = Fresh with { Researched = new[] { "logging", "mining", "smelting" }, Points = ItemCounts.None.Plus("stone", 40) };

        TeamState after = Tree.Research(team, Tree.Tech("bronze_working")!);

        Assert.Equal(1, after.Era);
        Assert.Equal(40 - Tree.Tech("bronze_working")!.Points, after.Points.Of("stone"));
        Assert.True(after.HasResearched("bronze_working"));
        Assert.True(Tree.IsUnlocked(after, Unlock.Smelting("bronze_ingot")));
    }

    [Fact]
    public void PointsComeFromTheErasReachedOnly()
    {
        Assert.Equal(1, Tree.Earned(Fresh, PointSource.Mine, "wood").Of("stone"));
        Assert.Empty(Tree.Earned(Fresh, PointSource.Mine, "dirt"));
        Assert.Empty(Tree.Earned(Fresh, PointSource.Smelt, "bronze_ingot"));
        Assert.Equal(2, Tree.Earned(Fresh with { Era = 1 }, PointSource.Smelt, "bronze_ingot").Of("bronze"));
        Assert.Equal(1, Tree.Earned(Fresh with { Era = 1 }, PointSource.Mine, "stone").Of("stone"));
    }

    [Fact]
    public void TeamStateWithResearchSurvivesTheWire()
    {
        TeamState state = new TeamState(Team.Blue) { Era = 1, Points = ItemCounts.None.Plus("stone", 3).Plus("bronze", 12), Researched = new[] { "logging", "mining" } }
            .Add("log", 5);

        TeamState decoded = WireFormat.DecodeTeamState(WireFormat.EncodeTeamState(state));

        Assert.Equal(1, decoded.Era);
        Assert.Equal(state.Points, decoded.Points);
        Assert.Equal(state.Researched, decoded.Researched);
        Assert.Equal(5, decoded.Amount("log"));
    }

    [Theory]
    [InlineData("\"recipes\": [\"stone_pickaxe\"]", "\"recipes\": [\"steel_pickaxe\"]")]
    [InlineData("\"requires\": [\"smelting\"]", "\"requires\": [\"alchemy\"]")]
    [InlineData("\"advance\": [\"bronze_working\"]", "\"advance\": [\"bronze_tools\"]")]
    [InlineData("\"mine\": { \"wood\": 1", "\"mine\": { \"gold\": 1")]
    public void BadTechsAreRejected(string from, string to)
    {
        string techs = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "techs.json"));
        Assert.Contains(from, techs);

        Assert.Throws<InvalidDataException>(() => TechTree.FromJson(techs.Replace(from, to), Data.Items, Data.Kinds));
    }

    [Fact]
    public void ChoppingAndMiningEarnStoneAgePointsForTheTeam()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Chopper", Team.Red);
        BlockPos log = Beside(test, player, Block.Wood);

        test.Server.EditRequested(2, new VoxelEdit(log, Block.Air));

        Assert.Equal(1, test.State.TeamStates[Team.Red].Points.Of("stone"));
        Assert.Equal(1, test.Net.TeamStates.Last(t => t.Peer == 2).State.Points.Of("stone"));
        Assert.Equal(0, test.State.TeamStates[Team.Blue].Points.Of("stone"));
    }

    [Fact]
    public void CreativeEarnsNothingAndHasEverything()
    {
        var test = new TestServer("practice");
        ServerPlayer player = test.Join(2, "Builder", Team.Red);
        BlockPos log = Beside(test, player, Block.Wood);

        test.Server.EditRequested(2, new VoxelEdit(log, Block.Air));

        Assert.Empty(test.State.TeamStates[Team.Red].Points);
        Assert.True(test.Server.Research.Unlocked(Team.Red, Unlock.Structure("furnace")));
        Assert.True(test.Server.Research.Unlocked(Team.Red, Unlock.Plane));
    }

    [Fact]
    public void APlayerResearchesForTheTeamWithItsPoints()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Thinker", Team.Red);

        test.Server.ResearchRequested(2, "mining");
        Assert.False(test.State.TeamStates[Team.Red].HasResearched("mining"));
        Assert.Contains(test.Log, line => line.Contains("needs 8 Stone Age points"));

        GivePoints(test, Team.Red, "stone", 10);
        test.Server.ResearchRequested(2, "mining");

        TeamState red = test.State.TeamStates[Team.Red];
        Assert.True(red.HasResearched("mining"));
        Assert.Equal(2, red.Points.Of("stone"));
        Assert.True(test.Net.TeamStates.Last(t => t.Peer == 2).State.HasResearched("mining"));
    }

    [Fact]
    public void BronzeWorkingCostsIngotsAndStartsTheBronzeAge()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Smith", Team.Red);
        test.Research(Team.Red, "mining", "logging", "smelting");
        GivePoints(test, Team.Red, "stone", 30);

        test.Server.ResearchRequested(2, "bronze_working");
        Assert.Contains(test.Log, line => line.Contains("can't afford"));

        player.Backpack = ItemCounts.None.Plus("copper_ingot", 6).Plus("tin_ingot", 3);
        test.Server.ResearchRequested(2, "bronze_working");

        Assert.Equal(1, test.State.TeamStates[Team.Red].Era);
        Assert.Equal(1, player.Backpack.Of("tin_ingot"));
        Assert.Equal(0, player.Backpack.Of("copper_ingot"));
        Assert.Contains(test.Log, line => line.Contains("Red entered the Bronze Age"));
    }

    [Fact]
    public void LockedRecipesAndStructuresAreRefused()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Crafter", Team.Red);
        GameObject bench = InventoryTests.PlaceCrate(test, player, "workbench");
        player.Backpack = ItemCounts.None.Plus("stone", 3).Plus("planks", 2);

        test.Server.CraftRequested(2, "stone_pickaxe", bench.Id);

        Assert.Equal(0, player.Backpack.Of("stone_pickaxe"));
        Assert.Contains(test.Log, line => line.Contains("needs Mining research"));
        InventoryTests.PlaceCrate(test, player, "furnace");
        Assert.DoesNotContain(test.State.Objects.All, o => o.Kind == "furnace");
        Assert.Contains(test.Log, line => line.Contains("needs Smelting research"));
    }

    [Fact]
    public void StoneBlocksNeedFortificationAndPlacingThemEarnsBronzePoints()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Mason", Team.Red);
        BlockPos target = ServerTests.FreeCellBeside(test, player);
        player.Backpack = ItemCounts.None.Plus("stone", 5);

        test.Server.EditRequested(2, new VoxelEdit(target, Block.Stone));
        Assert.Equal(Block.Air, test.Server.World.Get(target));
        Assert.Contains(test.Log, line => line.Contains("needs Fortification research"));

        test.Research(Team.Red, "fortification");
        test.Server.EditRequested(2, new VoxelEdit(target, Block.Stone));

        Assert.Equal(Block.Stone, test.Server.World.Get(target));
        Assert.Equal(1, test.State.TeamStates[Team.Red].Points.Of("bronze"));
    }

    [Fact]
    public void BronzeSmeltsFromCopperAndTinOnceResearchedAndEarnsPoints()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Smith", Team.Red);
        test.Research(Team.Red, "mining", "logging", "smelting");
        GameObject furnace = InventoryTests.PlaceCrate(test, player, "furnace");
        player.Backpack = ItemCounts.None.Plus("copper_ingot", 3).Plus("tin_ingot", 1).Plus("coal", 1);
        test.Server.TransferRequested(2, furnace.Id, ItemCounts.InputSlot, "copper_ingot", 3, intoStructure: true);
        test.Server.TransferRequested(2, furnace.Id, ItemCounts.InputSlot, "tin_ingot", 1, intoStructure: true);
        test.Server.TransferRequested(2, furnace.Id, ItemCounts.FuelSlot, "coal", 1, intoStructure: true);

        test.Run(12 * test.State.Data.Net.TickRate);
        Assert.Empty(ItemCounts.Contents(test.State.Objects.Get(furnace.Id)!, ItemCounts.OutputSlot));

        test.Research(Team.Red, "bronze_working");
        test.Run(11 * test.State.Data.Net.TickRate);

        GameObject now = test.State.Objects.Get(furnace.Id)!;
        Assert.Equal(4, ItemCounts.Contents(now, ItemCounts.OutputSlot).Of("bronze_ingot"));
        Assert.Empty(ItemCounts.Contents(now, ItemCounts.InputSlot));
        Assert.Equal(8, test.State.TeamStates[Team.Red].Points.Of("bronze"));
    }

    [Fact]
    public void ProspectingDoublesIronOre()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Miner", Team.Red);
        player.Backpack = ItemCounts.None.Plus("bronze_pickaxe", 1);
        test.Research(Team.Red, "bronze_working", "bronze_tools", "prospecting");
        BlockPos ore = Beside(test, player, Block.IronOre);

        test.Server.EditRequested(2, new VoxelEdit(ore, Block.Air));

        Assert.Equal(2, player.Backpack.Of("iron_ore"));
    }

    [Fact]
    public void ThePlaneWaitsForWwiInARealMatch()
    {
        Assert.Equal(0, GetIntoPlane("quick").PilotPeerId);
        Assert.NotEqual(0, GetIntoPlane("practice").PilotPeerId);
    }

    [Fact]
    public void ARealMatchHasNoTimedPhasesAndNoNukeBeforeTheColdWar()
    {
        var test = new TestServer("quick");
        test.Join(2, "Red", Team.Red);
        test.Join(3, "Blue", Team.Blue);
        test.Run((int)((test.State.Preset.CountdownSeconds + 1) * test.State.Data.Net.TickRate));

        Assert.True(test.State.Match.Playing);
        Assert.Equal(3, test.State.Match.Phase);
        Assert.True(test.Net.Snapshots.Last().Snapshot.NukeReadyIn < 0);
    }

    /// <summary>Puts a block of a kind just beside and below the player's feet, to mine.</summary>
    private static BlockPos Beside(TestServer test, ServerPlayer player, Block block)
    {
        Vector3 feet = player.Pawn.Player.Position;
        var cell = new BlockPos((int)MathF.Floor(feet.X) + 1, (int)MathF.Floor(feet.Y) - 1, (int)MathF.Floor(feet.Z));
        test.Server.World.SetGenerated(cell, block);
        return cell;
    }

    private static void GivePoints(TestServer test, Team team, string era, int points) =>
        test.State.TeamStates[team] = test.State.TeamStates[team] with { Points = test.State.TeamStates[team].Points.Plus(era, points) };

    /// <summary>A Red player stands at the Red plane and presses E; the plane as it is after.</summary>
    private static ServerPlane GetIntoPlane(string preset)
    {
        var test = new TestServer(preset);
        ServerPlayer player = test.Join(2, "Pilot", Team.Red);
        ServerPlane plane = test.State.Planes.Values.First(p => p.Team == Team.Red);
        player.Pawn = PawnState.OnFoot(PlayerState.At(plane.State.Position));
        test.Server.InputsReceived(2, WireFormat.EncodeInputs(new[] { new PlayerInput(1, 0, 0, 0, 0, InputButtons.Use) }));
        test.Run(2);
        return plane;
    }
}
