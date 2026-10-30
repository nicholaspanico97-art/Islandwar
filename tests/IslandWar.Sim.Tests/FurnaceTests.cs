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
    public void AColdEmptyFurnaceDoesNothingButFuelLightsAndHeatsItAnyway()
    {
        GameObject oreOnly = ItemCounts.WithSlot(Furnace(), ItemCounts.InputSlot, "copper_ore", 3);
        Assert.Null(Furnaces.Smelt(oreOnly, Smelter, Data.Items, 500));

        GameObject fuelOnly = Run(ItemCounts.WithSlot(Furnace(), ItemCounts.FuelSlot, "coal", 3), 10);
        Assert.True(Furnaces.Temperature(fuelOnly, Data.Items) > 300);
        Assert.Equal(2, ItemCounts.Contents(fuelOnly, ItemCounts.FuelSlot).Of("coal"));
    }

    [Fact]
    public void CopperNeedsCharcoalHeatLogsAloneNeverGetThere()
    {
        GameObject logs = ItemCounts.WithSlot(ItemCounts.WithSlot(Furnace(), ItemCounts.InputSlot, "copper_ore", 2), ItemCounts.FuelSlot, "log", 20);
        logs = Run(logs, 120);
        Assert.Empty(ItemCounts.Contents(logs, ItemCounts.OutputSlot));
        Assert.True(Furnaces.Temperature(logs, Data.Items) < Data.Items.SmeltingFor("copper_ore")!.Temperature);

        GameObject charcoal = ItemCounts.WithSlot(ItemCounts.WithSlot(Furnace(), ItemCounts.InputSlot, "copper_ore", 2), ItemCounts.FuelSlot, "charcoal", 2);
        (GameObject done, float seconds) = RunUntil(charcoal, f => ItemCounts.Contents(f, ItemCounts.OutputSlot).Of("copper_ingot") == 1, 120);
        // About half a minute to heat up, then the five seconds of smelting.
        Assert.InRange(seconds, 25, 45);
        Assert.True(Furnaces.Temperature(done, Data.Items) >= 1100);
    }

    [Fact]
    public void LogsAreEnoughForCharcoalAndTin()
    {
        GameObject furnace = ItemCounts.WithSlot(ItemCounts.WithSlot(Furnace(), ItemCounts.InputSlot, "tin_ore", 1), ItemCounts.FuelSlot, "log", 8);
        furnace = ItemCounts.WithSlot(furnace, ItemCounts.Input2Slot, "log", 1);

        furnace = Run(furnace, 90);

        Assert.Equal(1, ItemCounts.Contents(furnace, ItemCounts.OutputSlot).Of("tin_ingot"));
        Assert.Equal(1, ItemCounts.Contents(furnace, ItemCounts.OutputSlot).Of("charcoal"));
    }

    [Fact]
    public void AFurnaceStaysAtItsHottestAndCoolsSlowlyWhenTheFireGoesOut()
    {
        GameObject hot = Run(ItemCounts.WithSlot(Furnace(), ItemCounts.FuelSlot, "charcoal", 3), 60);
        Assert.Equal(Smelter.MaxTemperature, Furnaces.Temperature(hot, Data.Items), 0);

        // The fire goes out: nothing burning and no fuel left.
        GameObject unfed = ItemCounts.WithSlot(hot, ItemCounts.FuelSlot, "charcoal", -99).With(Furnaces.BurnValue, 0).With(Furnaces.BurningValue, 0);
        GameObject cooling = Run(unfed, 20);
        float after20 = Furnaces.Temperature(cooling, Data.Items);
        Assert.InRange(after20, 900, 1150);

        GameObject cold = Run(cooling, 900);
        Assert.Equal(Data.Items.AmbientTemperature, Furnaces.Temperature(cold, Data.Items), 0);
        Assert.Null(Furnaces.Smelt(cold, Smelter, Data.Items, 500));
    }

    [Fact]
    public void AHotFurnaceOnlyBurnsWhatItNeedsAndABetterOneNeedsLess()
    {
        SmelterSpec blast = Data.Kinds.Get("blast_furnace")!.Smelter!;
        float FlatOutSeconds(SmelterSpec smelter)
        {
            GameObject furnace = ItemCounts.WithSlot(Furnace(), ItemCounts.FuelSlot, "charcoal", 4);
            furnace = Run(furnace, 60, smelter);
            int before = furnace.Values.Of(Furnaces.BurnValue);
            furnace = Run(furnace, 10, smelter);
            return (before - furnace.Values.Of(Furnaces.BurnValue)) / 1000f;
        }

        float ordinary = FlatOutSeconds(Smelter);
        float better = FlatOutSeconds(blast);
        // Holding temperature burns well under the 10 s of fuel it would take flat out.
        Assert.InRange(ordinary, 0.5f, 5);
        Assert.True(better < ordinary);
    }

    [Fact]
    public void BronzeTakesCopperInOneSlotAndTinInTheOther()
    {
        GameObject furnace = ItemCounts.WithSlot(Furnace(), ItemCounts.InputSlot, "copper_ingot", 3);
        furnace = ItemCounts.WithSlot(furnace, ItemCounts.Input2Slot, "tin_ingot", 1);
        furnace = ItemCounts.WithSlot(furnace, ItemCounts.FuelSlot, "charcoal", 2);

        furnace = Run(furnace, 60);

        Assert.Equal(4, ItemCounts.Contents(furnace, ItemCounts.OutputSlot).Of("bronze_ingot"));
        Assert.Empty(Furnaces.Inputs(furnace));
    }

    [Fact]
    public void AFullOutputStopsTheFurnace()
    {
        GameObject furnace = ItemCounts.WithSlot(Furnace(), ItemCounts.InputSlot, "tin_ore", 1);
        furnace = ItemCounts.WithSlot(furnace, ItemCounts.FuelSlot, "coal", 1);
        furnace = ItemCounts.WithSlot(furnace, ItemCounts.OutputSlot, "charcoal", Smelter.SlotCapacity);

        furnace = Run(furnace, 60);

        Assert.Equal(1, ItemCounts.Contents(furnace, ItemCounts.InputSlot).Of("tin_ore"));
        Assert.Equal(Smelter.SlotCapacity, ItemCounts.Contents(furnace, ItemCounts.OutputSlot).Total());
    }

    /// <summary>Half-second steps for <paramref name="seconds"/>.</summary>
    private static GameObject Run(GameObject furnace, float seconds, SmelterSpec? smelter = null) =>
        RunUntil(furnace, _ => false, seconds, smelter).Furnace;

    private static (GameObject Furnace, float Seconds) RunUntil(GameObject furnace, Func<GameObject, bool> done, float seconds, SmelterSpec? smelter = null)
    {
        for (int ms = 500; ms <= seconds * 1000; ms += 500)
        {
            furnace = Furnaces.Smelt(furnace, smelter ?? Smelter, Data.Items, 500) ?? furnace;
            if (done(furnace))
            {
                return (furnace, ms / 1000f);
            }
        }

        return (furnace, seconds);
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
        // Coal heats it to copper's 1100 degrees in under half a minute; then five seconds of smelting.
        test.Run(32 * test.State.Data.Net.TickRate);

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

        // Each input slot holds one kind of item; the second takes another.
        player.Backpack = player.Backpack.Plus("tin_ore", 2);
        test.Server.TransferRequested(2, furnace.Id, ItemCounts.InputSlot, "tin_ore", 2, intoStructure: true);
        Assert.Contains(test.Log, line => line.Contains("already holds something else"));
        test.Server.TransferRequested(2, furnace.Id, ItemCounts.Input2Slot, "tin_ore", 2, intoStructure: true);
        Assert.Equal(0, player.Backpack.Of("tin_ore"));
    }

    [Fact]
    public void AFurnaceWithAnythingInItCantBeTakenDown()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Smith", Team.Red);
        test.Research(Team.Red, "mining", "logging", "smelting");
        GameObject furnace = InventoryTests.PlaceCrate(test, player, "furnace");
        test.State.Objects.Set(ItemCounts.WithSlot(furnace, ItemCounts.InputSlot, "copper_ore", 1));
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
