using System.Numerics;
using IslandWar.Sim.Items;
using IslandWar.Sim.Match;
using IslandWar.Sim.Objects;
using IslandWar.Sim.Server;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Tests;

/// <summary>Gathering and storage on a real-match preset (not creative).</summary>
public class InventoryTests
{
    private static readonly GameData Data = GameData.Load(name => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", name)));

    [Fact]
    public void TheItemDataLoadsAndCoversEveryNaturalBlock()
    {
        Assert.Equal("log", Data.Items.RuleFor(Block.Wood)!.Drops);
        Assert.Equal("dirt", Data.Items.RuleFor(Block.Grass)!.Drops);
        Assert.Null(Data.Items.RuleFor(Block.Leaves)!.Drops);
        Assert.Null(Data.Items.RuleFor(Block.Structure));
        Assert.Equal("log", Data.Items.ItemPlacing(Block.Wood)!.Id);
        Assert.Null(Data.Items.ItemPlacing(Block.CopperOre));
    }

    [Theory]
    [InlineData("""{ "items": [ { "id": "a", "name": "A" }, { "id": "a", "name": "B" } ] }""")]
    [InlineData("""{ "items": [ { "id": "a", "name": "A", "places": "copper_ore" } ] }""")]
    [InlineData("""{ "items": [], "blocks": { "stone": { "drops": "nothing" } } }""")]
    [InlineData("""{ "items": [], "blocks": { "granite": { } } }""")]
    [InlineData("""{ "items": [], "blocks": { } }""")]
    public void BadItemDataIsRejected(string json)
    {
        Assert.Throws<InvalidDataException>(() => ItemSettings.FromJson(json));
    }

    [Fact]
    public void TheMapHasEveryOreAndClay()
    {
        var test = new TestServer("quick");
        VoxelWorld world = test.Server.World;
        var found = new HashSet<Block>();
        foreach (Chunk chunk in world.Chunks)
        {
            if (chunk.IsUniform)
            {
                found.Add(chunk.UniformBlock);
                continue;
            }

            for (int i = 0; i < Chunk.Volume; i++)
            {
                (int x, int y, int z) = Chunk.FromIndex(i);
                found.Add(chunk.Get(x, y, z));
            }
        }

        Assert.Superset(new HashSet<Block> { Block.CopperOre, Block.TinOre, Block.IronOre, Block.Coal, Block.Clay }, found);
        Assert.DoesNotContain(Block.Ore, found);
    }

    [Fact]
    public void MiningPutsTheBlocksItemInTheBackpackAndOnlyItsOwnerHears()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Miner", Team.Red);
        test.Join(3, "Other", Team.Red);
        (BlockPos ground, Block block) = GroundBeside(test, player);
        string drop = Data.Items.RuleFor(block)!.Drops!;
        test.Net.Backpacks.Clear();

        test.Server.EditRequested(2, new VoxelEdit(ground, Block.Air));

        Assert.Equal(Block.Air, test.Server.World.Get(ground));
        Assert.Equal(1, player.Backpack.Of(drop));
        (int peer, IReadOnlyDictionary<string, int> sent) = Assert.Single(test.Net.Backpacks);
        Assert.Equal((2, 1), (peer, sent.Of(drop)));
    }

    [Fact]
    public void MiningFasterThanTheHardnessAllowsIsRefused()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Miner", Team.Red);
        (BlockPos first, _) = GroundBeside(test, player);
        BlockPos second = first with { Y = first.Y - 1 };
        test.Server.World.SetGenerated(second, Block.Stone);
        test.Server.EditRequested(2, new VoxelEdit(first, Block.Air));

        test.Server.EditRequested(2, new VoxelEdit(second, Block.Air));
        Assert.Equal(Block.Stone, test.Server.World.Get(second));
        Assert.Contains(test.Log, line => line.Contains("mining too fast"));

        // Stone takes 1.5 s by hand.
        test.Run((int)(1.5f * test.State.Data.Net.TickRate));
        test.Server.EditRequested(2, new VoxelEdit(second, Block.Air));
        Assert.Equal(Block.Air, test.Server.World.Get(second));
        Assert.Equal(1, player.Backpack.Of("stone"));
    }

    [Fact]
    public void PlacingABlockUsesOneFromTheBackpack()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Builder", Team.Red);
        BlockPos target = ServerTests.FreeCellBeside(test, player);

        test.Server.EditRequested(2, new VoxelEdit(target, Block.Stone));
        Assert.Equal(Block.Air, test.Server.World.Get(target));
        Assert.Contains(test.Log, line => line.Contains("nothing to place"));

        player.Backpack = player.Backpack.Plus("stone", 2);
        test.Server.EditRequested(2, new VoxelEdit(target, Block.Stone));
        Assert.Equal(Block.Stone, test.Server.World.Get(target));
        Assert.Equal(1, player.Backpack.Of("stone"));
    }

    [Fact]
    public void WithAFullBackpackTheMinedItemIsLost()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Miner", Team.Red);
        player.Backpack = ItemCounts.None.Plus("stone", Data.Items.BackpackCapacity);
        (BlockPos ground, _) = GroundBeside(test, player);

        test.Server.EditRequested(2, new VoxelEdit(ground, Block.Air));

        Assert.Equal(Block.Air, test.Server.World.Get(ground));
        Assert.Equal(Data.Items.BackpackCapacity, player.Backpack.Total());
        Assert.Contains(test.Log, line => line.Contains("is lost"));
    }

    [Fact]
    public void CreativeModeNeedsNoItemsAndGivesNone()
    {
        var test = new TestServer("practice");
        ServerPlayer player = test.Join(2, "Builder", Team.Red);
        BlockPos target = ServerTests.FreeCellBeside(test, player);

        test.Server.EditRequested(2, new VoxelEdit(target, Block.Stone));
        test.Server.EditRequested(2, new VoxelEdit(target, Block.Air));

        Assert.Equal(Block.Air, test.Server.World.Get(target));
        Assert.Empty(player.Backpack);
    }

    [Fact]
    public void ItemsMoveIntoTeamStorageAndBackAndMakeTheTeamsStock()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Hauler", Team.Red);
        GameObject crate = PlaceCrate(test, player);
        player.Backpack = ItemCounts.None.Plus("copper_ore", 10);
        test.Net.TeamStates.Clear();

        test.Server.TransferRequested(2, crate.Id, "item", "copper_ore", 7, intoStructure: true);

        Assert.Equal(3, player.Backpack.Of("copper_ore"));
        Assert.Equal(7, ItemCounts.Contents(test.State.Objects.Get(crate.Id)!).Of("copper_ore"));
        Assert.Equal(7, test.Net.TeamStates.Last().State.Amount("copper_ore"));
        Assert.Equal(7, ItemCounts.Contents(test.Net.Objects.Last()).Of("copper_ore"));

        test.Server.TransferRequested(2, crate.Id, "item", "copper_ore", 100, intoStructure: false);

        Assert.Equal(10, player.Backpack.Of("copper_ore"));
        Assert.Empty(ItemCounts.Contents(test.State.Objects.Get(crate.Id)!));
        Assert.Equal(0, test.State.TeamStates[Team.Red].Amount("copper_ore"));
    }

    [Fact]
    public void TransfersStopAtWhatFits()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Hauler", Team.Red);
        GameObject crate = PlaceCrate(test, player);
        ObjectKind kind = test.State.Objects.KindOf(crate);
        test.State.Objects.Set(ItemCounts.WithStored(crate, "stone", kind.Capacity - 3));
        player.Backpack = ItemCounts.None.Plus("stone", 10);

        test.Server.TransferRequested(2, crate.Id, "item", "stone", 10, intoStructure: true);
        Assert.Equal(7, player.Backpack.Of("stone"));
        Assert.Equal(kind.Capacity, ItemCounts.Contents(test.State.Objects.Get(crate.Id)!).Total());

        player.Backpack = ItemCounts.None.Plus("dirt", Data.Items.BackpackCapacity - 2);
        test.Server.TransferRequested(2, crate.Id, "item", "stone", 10, intoStructure: false);
        Assert.Equal(2, player.Backpack.Of("stone"));
    }

    [Fact]
    public void TheOtherTeamCantUseYourStorage()
    {
        var test = new TestServer("quick", dev: true);
        ServerPlayer owner = test.Join(2, "Red", Team.Red);
        GameObject crate = PlaceCrate(test, owner);
        ServerPlayer enemy = test.Join(3, "Blue", Team.Blue);
        enemy.Pawn = owner.Pawn;
        enemy.Backpack = ItemCounts.None.Plus("stone", 5);

        test.Server.TransferRequested(3, crate.Id, "item", "stone", 5, intoStructure: true);

        Assert.Equal(5, enemy.Backpack.Of("stone"));
        Assert.Contains(test.Log, line => line.Contains("the other team's"));
    }

    [Fact]
    public void StorageThatIsntEmptyCantBeTakenDown()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Hauler", Team.Red);
        GameObject crate = PlaceCrate(test, player);
        test.State.Objects.Set(ItemCounts.WithStored(crate, "stone", 1));

        test.Server.RemoveRequested(2, crate.Id);

        Assert.NotNull(test.State.Objects.Get(crate.Id));
        Assert.Contains(test.Log, line => line.Contains("isn't empty"));
    }

    [Fact]
    public void DestroyedStorageTakesItsContentsWithIt()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Hauler", Team.Red);
        GameObject crate = PlaceCrate(test, player);
        player.Backpack = ItemCounts.None.Plus("tin_ore", 4);
        test.Server.TransferRequested(2, crate.Id, "item", "tin_ore", 4, intoStructure: true);
        Assert.Equal(4, test.State.TeamStates[Team.Red].Amount("tin_ore"));

        BlastResult blast = test.Server.Structures.Blast(crate.Origin, 2, 10_000);
        test.Server.Structures.Announce(blast);

        Assert.Single(blast.Destroyed);
        Assert.Equal(0, test.State.TeamStates[Team.Red].Amount("tin_ore"));
    }

    [Fact]
    public void APlayerWhoComesBackMidMatchStillHasTheirBackpack()
    {
        var test = new TestServer("quick", dev: true);
        ServerPlayer red = test.Join(2, "Red", Team.Red, "red-token");
        test.Join(3, "Blue", Team.Blue);
        test.Run((int)((test.State.Preset.CountdownSeconds + 1) * test.State.Data.Net.TickRate));
        Assert.True(test.State.Match.Playing);
        test.State.Players[2].Backpack = ItemCounts.None.Plus("log", 6);

        test.Server.PeerDisconnected(2);
        ServerPlayer back = test.Join(4, "Red", Team.Red, "red-token");

        Assert.Equal(6, back.Backpack.Of("log"));
        Assert.Equal(6, test.Net.Backpacks.Last(b => b.Peer == 4).Counts.Of("log"));
    }

    [Fact]
    public void ANewMatchEmptiesEveryBackpack()
    {
        var test = new TestServer("quick", dev: true);
        ServerPlayer red = test.Join(2, "Red", Team.Red);
        test.Join(3, "Blue", Team.Blue);
        red.Backpack = ItemCounts.None.Plus("log", 6);

        test.Run((int)((test.State.Preset.CountdownSeconds + 1) * test.State.Data.Net.TickRate));

        Assert.True(test.State.Match.Playing);
        Assert.Empty(test.State.Players[2].Backpack);
    }

    /// <summary>A natural block in the ground next to the player, in easy reach.</summary>
    private static (BlockPos Position, Block Block) GroundBeside(TestServer test, ServerPlayer player)
    {
        Vector3 feet = player.Pawn.Player.Position;
        var cell = new BlockPos((int)MathF.Floor(feet.X) + 1, (int)MathF.Floor(feet.Y) - 1, (int)MathF.Floor(feet.Z));
        Block block = test.Server.World.Get(cell);
        Assert.NotNull(Data.Items.RuleFor(block)?.Drops);
        return (cell, block);
    }

    /// <summary>A crate just beside the player, on ground levelled for it, paid for with planks given to them first.</summary>
    internal static GameObject PlaceCrate(TestServer test, ServerPlayer player, string kind = "crate")
    {
        player.Backpack = player.Backpack.Sum(test.State.Data.Kinds.Get(kind)!.Cost);
        Vector3 feet = player.Pawn.Player.Position;
        var origin = new BlockPos((int)MathF.Floor(feet.X) + 2, (int)MathF.Floor(feet.Y), (int)MathF.Floor(feet.Z));
        test.Server.World.SetGenerated(origin, Block.Air);
        test.Server.World.SetGenerated(origin with { Y = origin.Y - 1 }, Block.Stone);
        test.Server.PlaceRequested(player.Peer, kind, origin, 0);
        return test.State.Objects.At(origin) ?? throw new InvalidOperationException(string.Join("\n", test.Log));
    }
}
