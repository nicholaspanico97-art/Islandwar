using System.Numerics;
using IslandWar.Sim.Items;
using IslandWar.Sim.Match;
using IslandWar.Sim.Objects;
using IslandWar.Sim.Players;
using IslandWar.Sim.Protocol;
using IslandWar.Sim.Server;
using IslandWar.Sim.Vehicles;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Tests;

/// <summary>The dock and the galley (Bronze Age piece 3).</summary>
public class ShipTests
{
    private const float Dt = 1f / 60;
    private const int SeaLevel = 8;
    private static readonly GameData Data = GameData.Load(name => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", name)));
    private static readonly ShipKind Galley = Data.Kinds.Ships["galley"];

    private static PlaneState Sail(VoxelWorld world, PlaneState state, float forward, float turn, float seconds)
    {
        var input = new PlayerInput(0, turn, -forward, 0, 0, InputButtons.None);
        for (int i = 0; i < (int)(seconds / Dt); i++)
        {
            state = ShipSailing.Step(world, Galley, SeaLevel, state, input, Dt);
        }

        return state;
    }

    private static PlaneState Afloat() => PlaneState.Parked(new Vector3(32, SeaLevel, 32), 0) with { Ship = "galley" };

    [Fact]
    public void TheDataHasADockThatBuildsGalleysBehindShipbuilding()
    {
        ObjectKind dock = Data.Kinds.Get("dock")!;
        Assert.Equal(Galley, Data.Kinds.ShipBuiltAt(dock));
        Assert.Null(Data.Kinds.ShipBuiltAt(Data.Kinds.Get("depot")!));
        Assert.Contains("Shipbuilding", Data.Techs.UnlockedBy("structure:dock"));
        Assert.Contains("Shipbuilding", Data.Techs.UnlockedBy("vehicle:galley"));
        Assert.True(Galley.Cost.Of("bronze_ingot") > 0);
    }

    [Fact]
    public void RowingReachesTopSpeedAlongTheBowOnTheSurface()
    {
        PlaneState later = Sail(new VoxelWorld(4, 2, 4), Afloat(), 1, 0, 6);

        Assert.Equal(Galley.MaxSpeed, later.Airspeed, 2);
        Assert.True(later.Position.Z < 32 - 20);
        Assert.Equal(32, later.Position.X, 3);
        Assert.Equal(SeaLevel, later.Position.Y);
        Assert.True(later.IsShip);
    }

    [Fact]
    public void BackingWaterIsSlowAndLettingGoComesToAStop()
    {
        PlaneState astern = Sail(new VoxelWorld(4, 2, 4), Afloat(), -1, 0, 4);
        Assert.Equal(-Galley.ReverseSpeed, astern.Airspeed, 2);
        Assert.True(astern.Position.Z > 32);

        PlaneState stopped = Sail(new VoxelWorld(4, 2, 4), astern, 0, 0, 3);
        Assert.Equal(0, stopped.Airspeed, 3);
    }

    [Fact]
    public void ARudderTurnsTheShipEvenAtRest()
    {
        PlaneState turned = Sail(new VoxelWorld(4, 2, 4), Afloat(), 0, -1, 1);

        // A to the left: the heading grows.
        Assert.True(turned.Heading > 0.1f);
        Assert.Equal(32, turned.Position.X, 3);
    }

    [Fact]
    public void LandStopsTheShip()
    {
        var world = new VoxelWorld(4, 2, 4);
        for (int x = 0; x < 64; x++)
        {
            world.SetGenerated(new BlockPos(x, SeaLevel, 20), Block.Sand);
        }

        PlaneState later = Sail(world, Afloat(), 1, 0, 6);

        Assert.Equal(0, later.Airspeed);
        Assert.True(later.Position.Z > 21);
    }

    [Fact]
    public void ADockNeedsOpenWaterInFront()
    {
        var test = new TestServer("quick");
        ObjectKind dock = Data.Kinds.Get("dock")!;
        (BlockPos origin, int facing) = DockSite(test);

        Assert.Null(ShipSailing.WhyNoWater(test.Server.World, test.State.Rules.SeaLevel, dock, origin, facing, Galley));
        Assert.NotNull(ShipSailing.WhyNoWater(test.Server.World, test.State.Rules.SeaLevel, dock, origin, (facing + 2) % 4, Galley));
    }

    [Fact]
    public void ADockIsLockedUntilShipbuilding()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Shipwright", Team.Red);
        (BlockPos origin, int facing) = StandBy(test, player);

        test.Server.PlaceRequested(2, "dock", origin, facing);

        Assert.Null(test.State.Objects.At(origin));
        Assert.Contains(test.Log, line => line.Contains("Shipbuilding"));
    }

    [Fact]
    public void DeliveringAGalleysCostToABuiltDockLaunchesIt()
    {
        var test = new TestServer("quick");
        ServerPlayer player = test.Join(2, "Shipwright", Team.Red);
        test.Research(Team.Red, "shipbuilding");
        GameObject dock = BuildDock(test, player);
        int vehicles = test.State.Planes.Count;
        player.Backpack = Galley.Cost;

        foreach ((string item, int count) in Galley.Cost)
        {
            test.Server.TransferRequested(2, dock.Id, Blueprint.DeliveredSlot, item, count, intoStructure: true);
        }

        ServerPlane galley = Assert.Single(test.State.Planes.Values, p => p.State.IsShip);
        Assert.Equal(vehicles + 1, test.State.Planes.Count);
        Assert.Equal(Team.Red, galley.Team);
        Assert.Equal("galley", galley.State.Ship);
        Assert.Equal(test.State.Rules.SeaLevel, galley.State.Position.Y);
        Assert.Empty(player.Backpack);
        Assert.Empty(Blueprint.Delivered(test.State.Objects.Get(dock.Id)!));
        Assert.Contains(test.Log, line => line.Contains("launched a Galley"));
    }

    [Fact]
    public void InCreativeAskingIsEnough()
    {
        var test = new TestServer("practice");
        ServerPlayer player = test.Join(2, "Shipwright", Team.Red);
        (BlockPos origin, int facing) = StandBy(test, player);
        test.Server.PlaceRequested(2, "dock", origin, facing);
        GameObject dock = test.State.Objects.At(origin)!;
        Assert.False(dock.IsBlueprint());

        test.Server.TransferRequested(2, dock.Id, Blueprint.DeliveredSlot, "planks", 1, intoStructure: true);

        Assert.Single(test.State.Planes.Values, p => p.State.IsShip);
    }

    [Fact]
    public void TheCaptainRowsTheGalleyAndStepsOffOntoTheDock()
    {
        var test = new TestServer("practice");
        ServerPlayer player = test.Join(2, "Captain", Team.Red);
        (BlockPos origin, int facing) = StandBy(test, player);
        test.Server.PlaceRequested(2, "dock", origin, facing);
        test.Server.TransferRequested(2, test.State.Objects.At(origin)!.Id, Blueprint.DeliveredSlot, "planks", 1, intoStructure: true);
        ServerPlane galley = test.State.Planes.Values.Single(p => p.State.IsShip);
        Vector3 launched = galley.State.Position;

        player.Pawn = PawnState.OnFoot(PlayerState.At(launched + Vector3.UnitY));
        test.Server.InputsReceived(2, WireFormat.EncodeInputs(new[] { new PlayerInput(1, 0, 0, 0, 0, InputButtons.Use) }));
        test.Run(2);
        Assert.Equal(galley.Id, player.Pawn.PlaneId);
        Assert.Equal(2, galley.PilotPeerId);

        // Back water a little (the bow faces open sea, the stern the dock) and get out.
        var inputs = Enumerable.Range(2, 60).Select(seq => new PlayerInput((uint)seq, 0, 1, 0, 0, InputButtons.None)).ToList();
        inputs.Add(new PlayerInput(62, 0, 0, 0, 0, InputButtons.Use));
        foreach (PlayerInput[] batch in inputs.Chunk(8))
        {
            test.Server.InputsReceived(2, WireFormat.EncodeInputs(batch));
            test.Run(batch.Length);
        }

        test.Run(4);
        Assert.False(player.Pawn.InPlane);
        Assert.NotEqual(launched, galley.State.Position);
        // On something dry: the dock or the shore, not in the sea.
        Assert.True(player.Pawn.Player.Position.Y > test.State.Rules.SeaLevel);
    }

    [Fact]
    public void ANewMatchSinksEveryShipButKeepsThePlanes()
    {
        var test = new TestServer("quick", dev: true);
        ServerPlayer red = test.Join(2, "Red", Team.Red);
        test.Join(3, "Blue", Team.Blue);
        test.Server.Vehicles.Launch(Team.Red, Galley, Afloat());
        int planes = test.State.Planes.Values.Count(p => !p.State.IsShip);

        test.Run((int)((test.State.Preset.CountdownSeconds + 1) * test.State.Data.Net.TickRate));

        Assert.True(test.State.Match.Playing);
        Assert.DoesNotContain(test.State.Planes.Values, p => p.State.IsShip);
        Assert.Equal(planes, test.State.Planes.Count);
    }

    [Fact]
    public void ShipsTravelInSnapshotsWithTheirKind()
    {
        var snapshot = new Snapshot(1, 0, Array.Empty<PlayerSnapshot>(), new[]
        {
            new PlaneSnapshot(1, PlaneState.Parked(new Vector3(5, 32, 6), 1), 0, Team.Red),
            new PlaneSnapshot(2, Afloat(), 7, Team.Blue),
        });

        Snapshot decoded = WireFormat.DecodeSnapshot(WireFormat.EncodeSnapshot(snapshot));

        Assert.False(decoded.Planes[0].State.IsShip);
        Assert.Equal("galley", decoded.Planes[1].State.Ship);
    }

    /// <summary>Where a dock fits on the Red island's south shore, near the plane's mooring.</summary>
    private static (BlockPos Origin, int Facing) DockSite(TestServer test)
    {
        PlaneState mooring = test.State.Data.PlaneSpawnIn(test.Server.World, Team.Red);
        return ShipSailing.FindDockSite(test.Server.World, test.State.Data.Player, test.State.Rules.SeaLevel, Data.Kinds.Get("dock")!, Galley,
                   (int)mooring.Position.X, (int)mooring.Position.Z - 8, 12)
               ?? throw new InvalidOperationException("no dock site near the Red mooring");
    }

    /// <summary>A dock site, with the player standing on the shore just behind it, within reach.</summary>
    private static (BlockPos Origin, int Facing) StandBy(TestServer test, ServerPlayer player)
    {
        (BlockPos origin, int facing) = DockSite(test);
        ObjectKind dock = Data.Kinds.Get("dock")!;
        BlockPos[] footprint = GameObject.FootprintOf(dock, origin, facing).ToArray();
        // The footprint cell furthest from the water, then one more step inland, flying so nothing is in the way.
        Vector3 centre = new((float)footprint.Average(b => b.X) + 0.5f, origin.Y, (float)footprint.Average(b => b.Z) + 0.5f);
        Vector3 inland = facing switch { 0 => Vector3.UnitZ, 1 => Vector3.UnitX, 2 => -Vector3.UnitZ, _ => -Vector3.UnitX };
        Vector3 spot = centre + inland * 4 + Vector3.UnitY * 2;
        player.Pawn = PawnState.OnFoot(PlayerState.At(spot) with { Flying = true });
        return (origin, facing);
    }

    private static GameObject BuildDock(TestServer test, ServerPlayer player)
    {
        (BlockPos origin, int facing) = StandBy(test, player);
        test.Server.PlaceRequested(player.Peer, "dock", origin, facing);
        GameObject placed = test.State.Objects.At(origin) ?? throw new InvalidOperationException(string.Join("\n", test.Log));
        player.Backpack = Data.Kinds.Get("dock")!.Cost;
        foreach ((string item, int count) in Data.Kinds.Get("dock")!.Cost)
        {
            test.Server.TransferRequested(player.Peer, placed.Id, Blueprint.DeliveredSlot, item, count, intoStructure: true);
        }

        GameObject built = test.State.Objects.Get(placed.Id)!;
        Assert.False(built.IsBlueprint());
        return built;
    }
}
