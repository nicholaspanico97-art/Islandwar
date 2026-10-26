using System.Numerics;
using IslandWar.Sim.Match;
using IslandWar.Sim.Objects;
using IslandWar.Sim.Protocol;
using IslandWar.Sim.Server;
using IslandWar.Sim.Voxels;
using IslandWar.Sim.Weapons;

namespace IslandWar.Sim.Tests;

public class StructureTests
{
    private static readonly GameData Data = GameData.Load(name => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", name)));

    [Fact]
    public void TheDataFileHasPlaceableKinds()
    {
        Assert.NotEmpty(Data.Kinds.All);
        Assert.NotNull(Data.Kinds.Get("depot"));
        Assert.Null(Data.Kinds.Get("nonsense"));
    }

    [Theory]
    [InlineData("""{ "kinds": [ { "id": "a", "name": "A" }, { "id": "a", "name": "B" } ] }""")]
    [InlineData("""{ "kinds": [ { "id": "a", "name": "A", "size": { "x": 0 } } ] }""")]
    [InlineData("""{ "kinds": [ { "id": "a", "name": "A", "max_health": 0 } ] }""")]
    [InlineData("""{ "kinds": [ { "id": "", "name": "A" } ] }""")]
    public void BadKindsAreRejected(string json)
    {
        Assert.Throws<InvalidDataException>(() => ObjectKinds.FromJson(json));
    }

    [Fact]
    public void TurningAStructureSwapsItsWidthAndDepth()
    {
        var kind = new ObjectKind { Id = "k", Name = "K", Size = new Size3 { X = 4, Y = 2, Z = 1 } };
        BlockPos[] north = GameObject.FootprintOf(kind, new BlockPos(10, 5, 10), 0).ToArray();
        BlockPos[] east = GameObject.FootprintOf(kind, new BlockPos(10, 5, 10), 3).ToArray();

        Assert.Equal(8, north.Length);
        Assert.Equal(13, north.Max(b => b.X));
        Assert.Equal(10, north.Max(b => b.Z));
        Assert.Equal(10, east.Max(b => b.X));
        Assert.Equal(13, east.Max(b => b.Z));
    }

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(1.6f, 1)]
    [InlineData(-1.6f, 3)]
    [InlineData(3.1f, 2)]
    [InlineData(6.2f, 0)]
    public void FacingIsTheNearestQuarterTurn(float yaw, int facing)
    {
        Assert.Equal(facing, ObjectRules.FacingFor(yaw));
    }

    [Fact]
    public void AStructureNeedsRoomGroundAndNobodyInTheWay()
    {
        var world = new VoxelWorld(2, 2, 2);
        for (int x = 0; x < 32; x++)
        {
            for (int z = 0; z < 32; z++)
            {
                world.SetGenerated(new BlockPos(x, 4, z), Block.Stone);
            }
        }

        ObjectKind depot = Data.Kinds.Get("depot")!;
        Vector3[] nobody = Array.Empty<Vector3>();
        Assert.Null(ObjectRules.WhyNot(world, Data.Player, depot, new BlockPos(5, 5, 5), 0, nobody));
        Assert.Equal("nothing to stand on", ObjectRules.WhyNot(world, Data.Player, depot, new BlockPos(5, 7, 5), 0, nobody));
        Assert.Equal("out of the world", ObjectRules.WhyNot(world, Data.Player, depot, new BlockPos(30, 5, 5), 0, nobody));
        Assert.Equal("someone is in the way", ObjectRules.WhyNot(world, Data.Player, depot, new BlockPos(5, 5, 5), 0, new[] { new Vector3(6.5f, 5, 6.5f) }));

        world.SetGenerated(new BlockPos(6, 6, 6), Block.Planks);
        Assert.Equal("not enough room", ObjectRules.WhyNot(world, Data.Player, depot, new BlockPos(5, 5, 5), 0, nobody));
    }

    [Fact]
    public void ObjectsAndTeamStatesSurviveTheWire()
    {
        GameObject obj = new GameObject(7, "depot", Team.Blue, 3, new BlockPos(100, 40, 200), 2, 150).With("fuel", 12).With("ore", 5);
        GameObject back = Assert.Single(WireFormat.DecodeObjects(WireFormat.EncodeObjects(new[] { obj })));

        Assert.Equal(obj with { Values = back.Values }, back);
        Assert.Equal(obj.Values, back.Values);

        TeamState state = new TeamState(Team.Red).Add("ore", 30).Add("oil", 4).Add("ore", -10);
        TeamState sent = WireFormat.DecodeTeamState(WireFormat.EncodeTeamState(state));
        Assert.Equal(20, sent.Amount("ore"));
        Assert.Equal(4, sent.Amount("oil"));
        Assert.Equal(0, sent.Amount("gold"));
    }

    [Fact]
    public void TheObjectHashChangesWithAnyValue()
    {
        var store = new ObjectStore(Data.Kinds);
        var obj = new GameObject(1, "crate", Team.Red, 1, new BlockPos(1, 1, 1), 0, 60);
        store.Set(obj);
        ulong before = store.ComputeHash();
        store.Set(obj.With("ore", 1));

        Assert.NotEqual(before, store.ComputeHash());
        Assert.Equal(1, store.At(new BlockPos(1, 1, 1))!.Id);
    }

    [Fact]
    public void APlacedStructureFillsItsFootprintAndEveryoneHearsOfIt()
    {
        var test = new TestServer();
        ServerPlayer player = test.Join(2, "Builder", Team.Red);
        (ObjectKind depot, BlockPos origin) = PlaceDepot(test, player);

        GameObject placed = Assert.Single(test.Net.Objects);
        Assert.Equal(("depot", Team.Red, player.PlayerId, origin, depot.MaxHealth), (placed.Kind, placed.Team, placed.OwnerPlayerId, placed.Origin, placed.Health));
        Assert.All(placed.Footprint(depot), b => Assert.Equal(Block.Structure, test.Server.World.Get(b)));
        Assert.All(test.Net.Broadcast, e => Assert.Equal((Block.Structure, placed.Owner), (e.Block, e.Owner)));
        Assert.Equal(depot.Size.X * depot.Size.Y * depot.Size.Z, test.Net.Broadcast.Count);
        Assert.Equal(placed.Id, test.State.Objects.At(origin)!.Id);
    }

    [Fact]
    public void AStructuresBlocksCantBeBrokenOneByOne()
    {
        var test = new TestServer();
        ServerPlayer player = test.Join(2, "Builder", Team.Red);
        (_, BlockPos origin) = PlaceDepot(test, player);

        test.Server.EditRequested(2, new VoxelEdit(origin, Block.Air));

        Assert.Equal(Block.Structure, test.Server.World.Get(origin));
    }

    [Fact]
    public void ALateJoinerGetsTheObjectsAndTheSameHash()
    {
        var test = new TestServer();
        ServerPlayer player = test.Join(2, "Builder", Team.Red);
        PlaceDepot(test, player);
        test.Join(3, "Late", Team.Red);

        WorldInit init = test.Net.Worlds.Single(w => w.Peer == 3).World;
        VoxelWorld world = IslandGenerator.Rebuild(init.Data.Terrain, init.Seed, WireFormat.DecodeEdits(init.EditsData));
        var objects = new ObjectStore(init.Data.Kinds);
        foreach (GameObject obj in WireFormat.DecodeObjects(init.ObjectsData))
        {
            objects.Set(obj);
        }

        // The test levelled the ground by hand (not as edits), so do the same here.
        foreach (GameObject obj in objects.All)
        {
            foreach (BlockPos cell in obj.Footprint(objects.KindOf(obj)))
            {
                world.SetGenerated(cell with { Y = obj.Origin.Y - 1 }, test.Server.World.Get(cell with { Y = obj.Origin.Y - 1 }));
            }
        }

        Assert.Equal(1, objects.Count);
        Assert.Equal(test.State.ComputeHash(), GameHash.Of(world, objects));
    }

    [Fact]
    public void RemovingAStructureFollowsTeamProtection()
    {
        var test = new TestServer(dev: true);
        ServerPlayer builder = test.Join(2, "Builder", Team.Red);
        (ObjectKind depot, BlockPos origin) = PlaceDepot(test, builder);
        test.Join(3, "Mate", Team.Red);
        test.Join(4, "Enemy", Team.Blue);
        int id = test.Net.Objects.Single().Id;
        foreach (ServerPlayer player in test.State.Players.Values)
        {
            player.Pawn = builder.Pawn;
        }

        test.Server.RemoveRequested(3, id);
        Assert.Equal(1, test.State.Objects.Count);

        test.Server.RemoveRequested(4, id);
        Assert.Equal(0, test.State.Objects.Count);
        Assert.Equal(new[] { id }, test.Net.RemovedObjects);
        Assert.All(GameObject.FootprintOf(depot, origin, 0), b => Assert.Equal(Block.Air, test.Server.World.Get(b)));
    }

    [Fact]
    public void BombsWearStructuresDownThenDestroyThemWithoutCrateringThem()
    {
        var test = new TestServer();
        ServerPlayer player = test.Join(2, "Builder", Team.Blue);
        (ObjectKind depot, BlockPos origin) = PlaceDepot(test, player);
        BombSettings bomb = test.State.Rules.Bomb;
        Structures structures = test.Server.Structures;
        int hits = (depot.MaxHealth + bomb.StructureDamage - 1) / bomb.StructureDamage;

        for (int i = 1; i < hits; i++)
        {
            BlastResult result = structures.Blast(origin, 2, bomb.StructureDamage);
            Assert.Equal(depot.MaxHealth - i * bomb.StructureDamage, Assert.Single(result.Damaged).Health);
            Assert.Equal(Block.Structure, test.Server.World.Get(origin));
        }

        BlastResult last = structures.Blast(origin, 2, bomb.StructureDamage);
        Assert.Single(last.Destroyed);
        Assert.Equal(depot.Size.X * depot.Size.Y * depot.Size.Z, last.Edits.Count);
        Assert.Equal(Block.Air, test.Server.World.Get(origin));
        Assert.Equal(0, test.State.Objects.Count);
    }

    [Fact]
    public void ABlastOutOfReachLeavesAStructureAlone()
    {
        var test = new TestServer();
        ServerPlayer player = test.Join(2, "Builder", Team.Blue);
        (_, BlockPos origin) = PlaceDepot(test, player);

        BlastResult result = test.Server.Structures.Blast(origin with { X = origin.X - 10 }, 3, 50);

        Assert.Empty(result.Damaged);
        Assert.Empty(result.Destroyed);
    }

    [Fact]
    public void CratersLeaveStructureBlocksStanding()
    {
        var world = new VoxelWorld(1, 1, 1);
        world.SetGenerated(new BlockPos(8, 8, 8), Block.Structure);
        world.SetGenerated(new BlockPos(9, 8, 8), Block.Stone);

        Craters.Blast(world, new BlockPos(8, 8, 8), 2);

        Assert.Equal(Block.Structure, world.Get(new BlockPos(8, 8, 8)));
        Assert.Equal(Block.Air, world.Get(new BlockPos(9, 8, 8)));
    }

    [Fact]
    public void ATeamsStateGoesOnlyToItsOwnPlayers()
    {
        var test = new TestServer(dev: true);
        test.Join(2, "Red", Team.Red);
        test.Join(3, "Blue", Team.Blue);
        test.Net.TeamStates.Clear();

        test.Server.Teams.Change(Team.Red, s => s.Add("ore", 5));

        (int peer, TeamState state) = Assert.Single(test.Net.TeamStates);
        Assert.Equal(2, peer);
        Assert.Equal(5, state.Amount("ore"));
    }

    [Fact]
    public void AJoinerGetsTheirTeamsState()
    {
        var test = new TestServer(dev: true);
        test.Join(2, "Red", Team.Red);

        Assert.Equal((2, Team.Red), test.Net.TeamStates.Select(t => (t.Peer, t.State.Team)).Single());
    }

    /// <summary>Places a depot just beside the player, on ground levelled for it.</summary>
    private static (ObjectKind Kind, BlockPos Origin) PlaceDepot(TestServer test, ServerPlayer player)
    {
        ObjectKind depot = test.State.Data.Kinds.Get("depot")!;
        Vector3 feet = player.Pawn.Player.Position;
        var origin = new BlockPos((int)MathF.Floor(feet.X) + 2, (int)MathF.Floor(feet.Y), (int)MathF.Floor(feet.Z) - 1);

        // Level the ground under it so the test doesn't depend on the terrain.
        foreach (BlockPos cell in GameObject.FootprintOf(depot, origin, 0))
        {
            test.Server.World.SetGenerated(cell, Block.Air);
            test.Server.World.SetGenerated(cell with { Y = origin.Y - 1 }, Block.Stone);
        }

        test.Server.PlaceRequested(player.Peer, "depot", origin, 0);
        Assert.Contains(test.Log, line => line.Contains("placed a Depot"));
        return (depot, origin);
    }
}
