using System.Numerics;
using IslandWar.Sim.Items;
using IslandWar.Sim.Match;
using IslandWar.Sim.Objects;
using IslandWar.Sim.Server;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Tests;

/// <summary>Recipes, tools and structure costs on a real-match preset (not creative).</summary>
public class CraftingTests
{
    private static readonly GameData Data = GameData.Load(name => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", name)));

    [Fact]
    public void ToolsSpeedUpTheirBlocksAndOreNeedsAPickaxe()
    {
        IReadOnlyDictionary<string, int> hands = ItemCounts.None;
        IReadOnlyDictionary<string, int> pickaxe = hands.Plus("stone_pickaxe", 1);
        IReadOnlyDictionary<string, int> both = pickaxe.Plus("stone_axe", 1);

        Assert.Equal(1.5f, Data.Items.MiningSeconds(Block.Stone, hands));
        Assert.Equal(0.75f, Data.Items.MiningSeconds(Block.Stone, pickaxe));
        Assert.Equal(2.0f, Data.Items.MiningSeconds(Block.Wood, pickaxe));
        Assert.Equal(0.8f, Data.Items.MiningSeconds(Block.Wood, both));
        Assert.Null(Data.Items.MiningSeconds(Block.CopperOre, hands));
        Assert.Equal(1.5f, Data.Items.MiningSeconds(Block.CopperOre, pickaxe));

        // Iron needs the Bronze Age's pickaxe, which doesn't exist yet.
        Assert.Null(Data.Items.MiningSeconds(Block.IronOre, both));
        Assert.Equal("stone_pickaxe", Data.Items.ToolNeededFor(Block.TinOre)!.Id);
        Assert.Null(Data.Items.ToolNeededFor(Block.Dirt));
    }

    [Theory]
    [InlineData("""{ "items": [ { "id": "a", "name": "A" } ], "blocks": { }, "recipes": [ { "id": "r", "takes": { "b": 1 }, "makes": { "a": 1 } } ] }""")]
    [InlineData("""{ "items": [ { "id": "a", "name": "A" } ], "blocks": { }, "recipes": [ { "id": "r", "takes": { "a": 0 }, "makes": { "a": 1 } } ] }""")]
    [InlineData("""{ "items": [ { "id": "a", "name": "A", "tool": { "kind": "axe", "speed": 0 } } ], "blocks": { } }""")]
    public void BadRecipesAndToolsAreRejected(string json)
    {
        Assert.Throws<InvalidDataException>(() => ItemSettings.FromJson(json));
    }

    [Fact]
    public void ARecipeMadeAtAStructureThatDoesntExistIsRejected()
    {
        string items = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "items.json"))
            .Replace("\"station\": \"workbench\"", "\"station\": \"forge\"");

        Assert.Throws<InvalidDataException>(() => GameData.Load(name =>
            name == "items.json" ? items : File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", name))));
    }

    [Fact]
    public void LogsBecomePlanksByHand()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Crafter", Team.Red);
        player.Backpack = ItemCounts.None.Plus("log", 2);

        test.Server.CraftRequested(2, "planks", 0);

        Assert.Equal(1, player.Backpack.Of("log"));
        Assert.Equal(4, player.Backpack.Of("planks"));
        Assert.Equal(4, test.Net.Backpacks.Last().Counts.Of("planks"));
    }

    [Fact]
    public void CraftingNeedsTheIngredients()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Crafter", Team.Red);

        test.Server.CraftRequested(2, "planks", 0);

        Assert.Empty(player.Backpack);
        Assert.Contains(test.Log, line => line.Contains("can't afford it"));
    }

    [Fact]
    public void ToolsAreMadeAtYourTeamsWorkbenchInReach()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Crafter", Team.Red);
        GameObject bench = InventoryTests.PlaceCrate(test, player, "workbench");
        player.Backpack = ItemCounts.None.Plus("stone", 3).Plus("planks", 2);
        test.Research(Team.Red, "mining");

        test.Server.CraftRequested(2, "stone_pickaxe", 0);
        Assert.Equal(0, player.Backpack.Of("stone_pickaxe"));
        Assert.Contains(test.Log, line => line.Contains("not at a workbench"));

        test.Server.CraftRequested(2, "stone_pickaxe", bench.Id);
        Assert.Equal(1, player.Backpack.Of("stone_pickaxe"));
        Assert.Equal(0, player.Backpack.Of("stone"));
    }

    [Fact]
    public void TheOtherTeamsWorkbenchWontDo()
    {
        var test = new TestServer("quick", dev: true);
        ServerPlayer red = test.Join(2, "Red", Team.Red);
        GameObject bench = InventoryTests.PlaceCrate(test, red, "workbench");
        ServerPlayer blue = test.Join(3, "Blue", Team.Blue);
        blue.Pawn = red.Pawn;
        blue.Backpack = ItemCounts.None.Plus("stone", 3).Plus("planks", 2);

        test.Server.CraftRequested(3, "stone_axe", bench.Id);

        Assert.Equal(0, blue.Backpack.Of("stone_axe"));
    }

    [Fact]
    public void IngredientsComeFromTheBackpackFirstThenTeamStorage()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Crafter", Team.Red);
        GameObject crate = InventoryTests.PlaceCrate(test, player);
        player.Backpack = ItemCounts.None.Plus("log", 3);
        test.Server.TransferRequested(2, crate.Id, "item", "log", 3, intoStructure: true);
        player.Backpack = ItemCounts.None.Plus("log", 1);

        test.Server.CraftRequested(2, "planks", 0);
        Assert.Equal((0, 3), (player.Backpack.Of("log"), ItemCounts.Contents(test.State.Objects.Get(crate.Id)!).Of("log")));

        test.Server.CraftRequested(2, "planks", 0);
        Assert.Equal(2, ItemCounts.Contents(test.State.Objects.Get(crate.Id)!).Of("log"));
        Assert.Equal(2, test.State.TeamStates[Team.Red].Amount("log"));
        Assert.Equal(8, player.Backpack.Of("planks"));
    }

    [Fact]
    public void CraftingStopsWhenTheResultWontFit()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Crafter", Team.Red);
        player.Backpack = ItemCounts.None.Plus("log", 1).Plus("dirt", Data.Items.BackpackCapacity - 2);

        test.Server.CraftRequested(2, "planks", 0);

        Assert.Equal(1, player.Backpack.Of("log"));
        Assert.Contains(test.Log, line => line.Contains("no room"));
    }

    [Fact]
    public void StructuresCostTheirMaterialsAndGiveThemBackWhenTakenDown()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Builder", Team.Red);
        ObjectKind crate = Data.Kinds.Get("crate")!;
        Vector3 feet = player.Pawn.Player.Position;
        var origin = new BlockPos((int)MathF.Floor(feet.X) + 2, (int)MathF.Floor(feet.Y), (int)MathF.Floor(feet.Z));
        test.Server.World.SetGenerated(origin, Block.Air);
        test.Server.World.SetGenerated(origin with { Y = origin.Y - 1 }, Block.Stone);

        test.Server.PlaceRequested(2, "crate", origin, 0);
        Assert.Equal(0, test.State.Objects.Count);
        Assert.Contains(test.Log, line => line.Contains("can't afford it"));

        player.Backpack = ItemCounts.None.Sum(crate.Cost);
        test.Server.PlaceRequested(2, "crate", origin, 0);
        Assert.Equal(1, test.State.Objects.Count);
        Assert.Empty(player.Backpack);

        test.Run((int)(Data.Items.StructureHardness * test.State.Data.Net.TickRate) + 1);
        test.Server.RemoveRequested(2, test.State.Objects.All.Single().Id);
        Assert.Equal(0, test.State.Objects.Count);
        Assert.True(player.Backpack.Covers(crate.Cost));
    }

    [Fact]
    public void MiningOreWithoutAPickaxeIsRefusedAndWithOneItsFaster()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Miner", Team.Red);
        Vector3 feet = player.Pawn.Player.Position;
        var ore = new BlockPos((int)MathF.Floor(feet.X) + 1, (int)MathF.Floor(feet.Y) - 1, (int)MathF.Floor(feet.Z));
        test.Server.World.SetGenerated(ore, Block.CopperOre);

        test.Server.EditRequested(2, new VoxelEdit(ore, Block.Air));
        Assert.Equal(Block.CopperOre, test.Server.World.Get(ore));
        Assert.Contains(test.Log, line => line.Contains("needs a better tool"));

        player.Backpack = ItemCounts.None.Plus("stone_pickaxe", 1);
        test.Server.EditRequested(2, new VoxelEdit(ore, Block.Air));
        Assert.Equal(Block.Air, test.Server.World.Get(ore));
        Assert.Equal(1, player.Backpack.Of("copper_ore"));
    }

    [Fact]
    public void CreativeCraftingAndBuildingAreFree()
    {
        var test = new TestServer("practice");
        ServerPlayer player = test.Join(2, "Builder", Team.Red);

        test.Server.CraftRequested(2, "planks", 0);

        Assert.Equal(4, player.Backpack.Of("planks"));
    }
}
