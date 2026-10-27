using IslandWar.Sim.Items;
using IslandWar.Sim.Match;
using IslandWar.Sim.Objects;
using IslandWar.Sim.Server;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Tests;

public class FurnaceTests
{
    private static readonly GameData Data = GameData.Load(name => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", name)));

    private static SmelterSpec Smelter => Data.Kinds.Get("furnace")!.Smelter!;

    private static GameObject Furnace() => new(1, "furnace", Team.Red, 1, new BlockPos(5, 5, 5), 0, 120);

    [Fact]
    public void TheDataHasAFurnaceFuelsAndSmelting()
    {
        Assert.NotNull(Data.Kinds.Get("furnace")!.Smelter);
        Assert.Equal("copper_ingot", Data.Items.SmeltingFor("copper_ore")!.Makes);
        Assert.Null(Data.Items.SmeltingFor("stone"));
        Assert.True(Data.Items.IsFuel("coal"));
        Assert.False(Data.Items.IsFuel("stone"));
    }

    [Fact]
    public void NothingHappensWithoutFuelOrWithoutSomethingToSmelt()
    {
        GameObject oreOnly = ItemCounts.WithSlot(Furnace(), ItemCounts.InputSlot, "copper_ore", 3);
        GameObject fuelOnly = ItemCounts.WithSlot(Furnace(), ItemCounts.FuelSlot, "coal", 3);

        Assert.Null(Furnaces.Smelt(oreOnly, Smelter, Data.Items, 500));
        Assert.Null(Furnaces.Smelt(fuelOnly, Smelter, Data.Items, 500));
    }

    [Fact]
    public void OreTurnsIntoIngotsAsFuelBurns()
    {
        GameObject furnace = ItemCounts.WithSlot(Furnace(), ItemCounts.InputSlot, "copper_ore", 2);
        furnace = ItemCounts.WithSlot(furnace, ItemCounts.FuelSlot, "log", 1);
        float seconds = Data.Items.SmeltingFor("copper_ore")!.Seconds;

        for (int ms = 0; ms < seconds * 1000; ms += 500)
        {
            furnace = Furnaces.Smelt(furnace, Smelter, Data.Items, 500)!;
        }

        Assert.Equal(1, ItemCounts.Contents(furnace, ItemCounts.OutputSlot).Of("copper_ingot"));
        Assert.Equal(1, ItemCounts.Contents(furnace, ItemCounts.InputSlot).Of("copper_ore"));
        Assert.Empty(ItemCounts.Contents(furnace, ItemCounts.FuelSlot));
        Assert.Equal((int)((Data.Items.Fuel["log"] - seconds) * 1000), furnace.Values.Of(Furnaces.BurnValue));
    }

    [Fact]
    public void AFullOutputStopsTheFurnace()
    {
        GameObject furnace = ItemCounts.WithSlot(Furnace(), ItemCounts.InputSlot, "tin_ore", 1);
        furnace = ItemCounts.WithSlot(furnace, ItemCounts.FuelSlot, "coal", 1);
        furnace = ItemCounts.WithSlot(furnace, ItemCounts.OutputSlot, "charcoal", Smelter.SlotCapacity);

        Assert.Null(Furnaces.Smelt(furnace, Smelter, Data.Items, 500));
    }

    [Fact]
    public void AFurnaceOnTheServerSmeltsAndEveryoneSeesIt()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Smith", Team.Red);
        test.Research(Team.Red, "mining", "logging", "smelting");
        GameObject furnace = InventoryTests.PlaceCrate(test, player, "furnace");
        player.Backpack = ItemCounts.None.Plus("copper_ore", 2).Plus("coal", 1);

        test.Server.TransferRequested(2, furnace.Id, ItemCounts.InputSlot, "copper_ore", 2, intoStructure: true);
        test.Server.TransferRequested(2, furnace.Id, ItemCounts.FuelSlot, "coal", 1, intoStructure: true);
        test.Net.Objects.Clear();
        test.Run((int)(5.6f * test.State.Data.Net.TickRate));

        GameObject now = test.State.Objects.Get(furnace.Id)!;
        Assert.Equal(1, ItemCounts.Contents(now, ItemCounts.OutputSlot).Of("copper_ingot"));
        Assert.NotEmpty(test.Net.Objects);
        Assert.Equal(1, ItemCounts.Contents(test.Net.Objects.Last(o => o.Id == furnace.Id), ItemCounts.OutputSlot).Of("copper_ingot"));

        test.Server.TransferRequested(2, furnace.Id, ItemCounts.OutputSlot, "copper_ingot", 5, intoStructure: false);
        Assert.Equal(1, player.Backpack.Of("copper_ingot"));
    }

    [Fact]
    public void OnlyWhatSmeltsOrBurnsGoesIn()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Smith", Team.Red);
        test.Research(Team.Red, "mining", "logging", "smelting");
        GameObject furnace = InventoryTests.PlaceCrate(test, player, "furnace");
        player.Backpack = ItemCounts.None.Plus("stone", 5).Plus("log", 5).Plus("copper_ingot", 1);

        test.Server.TransferRequested(2, furnace.Id, ItemCounts.InputSlot, "stone", 5, intoStructure: true);
        test.Server.TransferRequested(2, furnace.Id, ItemCounts.FuelSlot, "stone", 5, intoStructure: true);
        test.Server.TransferRequested(2, furnace.Id, ItemCounts.OutputSlot, "copper_ingot", 1, intoStructure: true);
        test.Server.TransferRequested(2, furnace.Id, ItemCounts.StorageSlot, "log", 1, intoStructure: true);
        Assert.Equal(5, player.Backpack.Of("stone"));
        Assert.Equal(1, player.Backpack.Of("copper_ingot"));
        Assert.Equal(5, player.Backpack.Of("log"));

        // Logs both smelt (into charcoal) and burn.
        test.Server.TransferRequested(2, furnace.Id, ItemCounts.InputSlot, "log", 2, intoStructure: true);
        test.Server.TransferRequested(2, furnace.Id, ItemCounts.FuelSlot, "log", 2, intoStructure: true);
        Assert.Equal(1, player.Backpack.Of("log"));
    }

    [Fact]
    public void AFurnaceWithAnythingInItCantBeTakenDown()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Smith", Team.Red);
        test.Research(Team.Red, "mining", "logging", "smelting");
        GameObject furnace = InventoryTests.PlaceCrate(test, player, "furnace");
        test.State.Objects.Set(ItemCounts.WithSlot(furnace, ItemCounts.FuelSlot, "coal", 1));
        test.Run(test.State.Data.Net.TickRate * 2);

        test.Server.RemoveRequested(2, furnace.Id);

        Assert.NotNull(test.State.Objects.Get(furnace.Id));
        Assert.Contains(test.Log, line => line.Contains("isn't empty"));
    }

    [Fact]
    public void WhatsInAFurnaceDoesntCountAsTeamStock()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Smith", Team.Red);
        test.Research(Team.Red, "mining", "logging", "smelting");
        GameObject furnace = InventoryTests.PlaceCrate(test, player, "furnace");
        player.Backpack = ItemCounts.None.Plus("coal", 3);

        test.Server.TransferRequested(2, furnace.Id, ItemCounts.FuelSlot, "coal", 3, intoStructure: true);

        Assert.Equal(0, test.State.TeamStates[Team.Red].Amount("coal"));
    }
}
