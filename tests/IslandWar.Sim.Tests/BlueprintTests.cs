using IslandWar.Sim.Items;
using IslandWar.Sim.Match;
using IslandWar.Sim.Objects;
using IslandWar.Sim.Server;

namespace IslandWar.Sim.Tests;

/// <summary>Building by blueprint (Bronze Age piece 2): placing is free, players haul the cost to it.</summary>
public class BlueprintTests
{
    [Fact]
    public void PlacingInARealMatchPutsDownAnEmptyBlueprintForFree()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Builder", Team.Red);

        GameObject placed = InventoryTests.PlaceBlueprint(test, player, "depot");

        ObjectKind depot = test.State.Objects.KindOf(placed);
        Assert.True(placed.IsBlueprint());
        Assert.Equal(depot.MaxHealth / Blueprint.HealthDivisor, placed.Health);
        Assert.Equal(depot.Cost, Blueprint.Missing(placed, depot, test.State.Objects.Kinds));
        Assert.Empty(player.Backpack);
        Assert.True(test.Net.Objects.Last().IsBlueprint());
    }

    [Fact]
    public void CreativeBuildsAtOnce()
    {
        var test = new TestServer("practice");
        ServerPlayer player = test.Join(2, "Builder", Team.Red);

        Assert.False(InventoryTests.PlaceBlueprint(test, player, "depot").IsBlueprint());
    }

    [Fact]
    public void DeliveriesTakeOnlyWhatsStillNeededAndTheLastOneBuildsIt()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Builder", Team.Red);
        GameObject placed = InventoryTests.PlaceBlueprint(test, player, "crate");
        ObjectKind crate = test.State.Objects.KindOf(placed);
        int planks = crate.Cost["planks"];
        player.Backpack = ItemCounts.None.Plus("planks", planks + 3).Plus("stone", 5);

        test.Server.TransferRequested(2, placed.Id, Blueprint.DeliveredSlot, "stone", 5, intoStructure: true);
        Assert.Equal(5, player.Backpack.Of("stone"));
        Assert.Contains(test.Log, line => line.Contains("doesn't need stone"));

        test.Server.TransferRequested(2, placed.Id, Blueprint.DeliveredSlot, "planks", planks - 1, intoStructure: true);
        GameObject partway = test.State.Objects.Get(placed.Id)!;
        Assert.True(partway.IsBlueprint());
        Assert.Equal(1, Blueprint.Missing(partway, crate, test.State.Objects.Kinds).Of("planks"));

        test.Server.TransferRequested(2, placed.Id, Blueprint.DeliveredSlot, "planks", 10, intoStructure: true);
        GameObject built = test.State.Objects.Get(placed.Id)!;
        Assert.False(built.IsBlueprint());
        Assert.False(ItemCounts.HasItems(built));
        Assert.Equal(crate.MaxHealth, built.Health);
        Assert.Equal(3, player.Backpack.Of("planks"));
        Assert.Contains(test.Log, line => line.Contains("Red's Crate") && line.Contains("is built"));
    }

    [Fact]
    public void ABlueprintDoesNothingUntilItsBuilt()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Builder", Team.Red);
        GameObject placed = InventoryTests.PlaceBlueprint(test, player, "crate");
        player.Backpack = ItemCounts.None.Plus("stone", 5);

        test.Server.TransferRequested(2, placed.Id, ItemCounts.StorageSlot, "stone", 5, intoStructure: true);

        Assert.Equal(5, player.Backpack.Of("stone"));
        Assert.Contains(test.Log, line => line.Contains("isn't built yet"));
    }

    [Fact]
    public void TheOtherTeamCantDeliverAndDeliveriesDontComeBackOut()
    {
        var test = new TestServer("quick", dev: true);
        ServerPlayer owner = test.Join(2, "Red", Team.Red);
        GameObject placed = InventoryTests.PlaceBlueprint(test, owner, "crate");
        ServerPlayer enemy = test.Join(3, "Blue", Team.Blue);
        enemy.Pawn = owner.Pawn;
        enemy.Backpack = ItemCounts.None.Plus("planks", 2);
        owner.Backpack = ItemCounts.None.Plus("planks", 2);

        test.Server.TransferRequested(3, placed.Id, Blueprint.DeliveredSlot, "planks", 2, intoStructure: true);
        Assert.Equal(2, enemy.Backpack.Of("planks"));

        test.Server.TransferRequested(2, placed.Id, Blueprint.DeliveredSlot, "planks", 2, intoStructure: true);
        test.Server.TransferRequested(2, placed.Id, Blueprint.DeliveredSlot, "planks", 2, intoStructure: false);
        Assert.Equal(0, owner.Backpack.Of("planks"));
        Assert.Contains(test.Log, line => line.Contains("deliveries stay in"));
    }

    [Fact]
    public void TakingDownABlueprintGivesBackWhatWasDelivered()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Builder", Team.Red);
        GameObject placed = InventoryTests.PlaceBlueprint(test, player, "crate");
        player.Backpack = ItemCounts.None.Plus("planks", 2);
        test.Server.TransferRequested(2, placed.Id, Blueprint.DeliveredSlot, "planks", 2, intoStructure: true);
        Assert.Empty(player.Backpack);

        test.Run((int)(test.State.Data.Items.StructureHardness * test.State.Data.Net.TickRate) + 1);
        test.Server.RemoveRequested(2, placed.Id);

        Assert.Null(test.State.Objects.Get(placed.Id));
        Assert.Equal(2, player.Backpack.Of("planks"));
    }

    [Fact]
    public void ABuiltStructureGivesItsWholeCostBackWhenTakenDown()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Builder", Team.Red);
        GameObject crate = InventoryTests.PlaceCrate(test, player);
        Assert.Empty(player.Backpack);

        test.Run((int)(test.State.Data.Items.StructureHardness * test.State.Data.Net.TickRate) + 1);
        test.Server.RemoveRequested(2, crate.Id);

        Assert.True(player.Backpack.Covers(test.State.Objects.Kinds.Get("crate")!.Cost));
    }

    [Fact]
    public void ABlueprintFurnaceDoesntSmelt()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Builder", Team.Red);
        test.Research(Team.Red, "mining", "logging", "smelting");
        GameObject furnace = InventoryTests.PlaceBlueprint(test, player, "furnace");
        test.State.Objects.Set(ItemCounts.WithSlot(ItemCounts.WithSlot(furnace, ItemCounts.InputSlot, "copper_ore", 4), ItemCounts.FuelSlot, "log", 4));

        test.Run(test.State.Data.Net.TickRate * 20);

        Assert.Empty(ItemCounts.Contents(test.State.Objects.Get(furnace.Id)!, ItemCounts.OutputSlot));
    }
}
