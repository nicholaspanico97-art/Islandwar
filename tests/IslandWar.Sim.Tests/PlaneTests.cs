using System.Numerics;
using IslandWar.Sim.Players;
using IslandWar.Sim.Vehicles;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Tests;

public class PlaneFlightTests
{
    private const float Dt = 1f / 60;
    private const int SeaLevel = 8;
    private static readonly PlaneSettings Settings = new();

    /// <summary>Open sea: no blocks at all, so the water surface at y = 8 is the only ground.</summary>
    private static VoxelWorld Sea() => new(4, 2, 4);

    private static PlayerInput Controls(float pitch = 0, float roll = 0, InputButtons buttons = InputButtons.None) =>
        new(0, roll, pitch, 0, 0, buttons);

    private static PlaneState Run(VoxelWorld world, PlaneState state, PlayerInput input, float seconds)
    {
        for (int i = 0; i < (int)(seconds / Dt); i++)
        {
            state = PlaneFlight.Step(world, Settings, SeaLevel, state, input, Dt);
        }

        return state;
    }

    private static PlaneState Flying(float altitude, float airspeed, float throttle, Quaternion rotation) =>
        new(new Vector3(32, altitude, 32), rotation, airspeed, 0, throttle, false);

    /// <summary>The throttle that holds a speed on the level: thrust = drag x speed.</summary>
    private static float CruiseThrottle(float speed) => Settings.Drag * speed / Settings.Thrust;

    [Fact]
    public void AParkedPlaneStaysOnTheWater()
    {
        var parked = PlaneState.Parked(new Vector3(32, SeaLevel, 32), 0);

        PlaneState later = Run(Sea(), parked, Controls(), 2);

        Assert.True(later.OnGround);
        Assert.Equal(parked.Position.X, later.Position.X, 3);
        Assert.Equal(parked.Position.Z, later.Position.Z, 3);
        Assert.Equal(SeaLevel, later.Position.Y, 2);
    }

    [Fact]
    public void ThrottleAcceleratesAlongTheNoseWithoutLiftingOff()
    {
        var parked = PlaneState.Parked(new Vector3(32, SeaLevel, 60), 0);

        PlaneState rolling = Run(Sea(), parked, Controls(buttons: InputButtons.Sprint), 4);

        Assert.True(rolling.Airspeed > 15, $"airspeed {rolling.Airspeed}");
        Assert.True(rolling.OnGround);
        Assert.True(rolling.Position.Z < 60 - 10);
        Assert.Equal(32, rolling.Position.X, 2);
    }

    [Fact]
    public void PullingUpAtSpeedTakesOff()
    {
        var parked = PlaneState.Parked(new Vector3(32, SeaLevel, 60), 0);
        PlaneState rolling = Run(Sea(), parked, Controls(buttons: InputButtons.Sprint), 5);

        PlaneState climbing = Run(Sea(), rolling, Controls(pitch: 1, buttons: InputButtons.Sprint), 0.6f);
        PlaneState later = Run(Sea(), climbing, Controls(buttons: InputButtons.Sprint), 2);

        Assert.False(later.OnGround);
        Assert.True(later.Position.Y > SeaLevel + 5, $"altitude {later.Position.Y}");
    }

    [Fact]
    public void PullingUpTooSlowStaysOnTheGround()
    {
        var parked = PlaneState.Parked(new Vector3(32, SeaLevel, 60), 0);
        PlaneState slow = Run(Sea(), parked, Controls(buttons: InputButtons.Sprint), 1);

        PlaneState tried = Run(Sea(), slow, Controls(pitch: 1), 1);

        Assert.True(tried.OnGround);
    }

    [Fact]
    public void LevelFlightAtCruiseHoldsAltitude()
    {
        PlaneState cruising = Flying(40, 30, CruiseThrottle(30), Quaternion.Identity);

        PlaneState later = Run(Sea(), cruising, Controls(), 3);

        Assert.InRange(later.Position.Y, 39.5f, 40.5f);
        Assert.InRange(later.Airspeed, 29, 31);
    }

    [Fact]
    public void TooSlowInTheAirSinks()
    {
        PlaneState stalling = Flying(40, 5, 0, Quaternion.Identity);

        PlaneState later = Run(Sea(), stalling, Controls(), 1.5f);

        Assert.True(later.Sink > 0);
        Assert.True(later.Position.Y < 40 - 5, $"altitude {later.Position.Y}");
    }

    [Fact]
    public void ABankedPlaneTurnsTowardTheLowWing()
    {
        // Rolled 45 degrees right (right wing down).
        Quaternion bankedRight = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI / 4);
        PlaneState start = Flying(40, 30, CruiseThrottle(30), bankedRight);

        PlaneState later = Run(Sea(), start, Controls(), 1);

        // Heading increases to the left, so a right turn makes it negative.
        Assert.True(later.Heading < -0.3f, $"heading {later.Heading}");
    }

    [Fact]
    public void RollAndPitchInputsTurnTheRightWay()
    {
        PlaneState start = Flying(40, 30, CruiseThrottle(30), Quaternion.Identity);

        PlaneState rolledRight = PlaneFlight.Step(Sea(), Settings, SeaLevel, start, Controls(roll: 1), Dt);
        PlaneState pulledUp = PlaneFlight.Step(Sea(), Settings, SeaLevel, start, Controls(pitch: 1), Dt);

        Assert.True(Vector3.Transform(Vector3.UnitX, rolledRight.Rotation).Y < 0, "right wing should drop");
        Assert.True(pulledUp.Forward.Y > 0, "nose should rise");
    }

    [Fact]
    public void AGentleDescentLandsLevelAndTheBrakesStopIt()
    {
        Quaternion noseDown = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.08f);
        PlaneState approach = Flying(14, 22, 0.2f, noseDown);

        PlaneState landed = Run(Sea(), approach, Controls(), 4);
        PlaneState stopped = Run(Sea(), landed, Controls(buttons: InputButtons.Jump), 4);

        Assert.True(landed.OnGround);
        Assert.Equal(0, Vector3.Transform(Vector3.UnitX, landed.Rotation).Y, 3);
        Assert.Equal(0, stopped.Airspeed, 3);
    }

    [Fact]
    public void GroundSteeringTurnsRightWithD()
    {
        var parked = PlaneState.Parked(new Vector3(32, SeaLevel, 32), 0);
        PlaneState taxiing = Run(Sea(), parked, Controls(buttons: InputButtons.Sprint), 1);

        PlaneState turned = Run(Sea(), taxiing, Controls(roll: 1), 1);

        Assert.True(turned.Heading < -0.2f, $"heading {turned.Heading}");
    }

    [Fact]
    public void AWallStopsThePlane()
    {
        VoxelWorld world = Sea();
        for (int x = 20; x < 44; x++)
        {
            for (int y = SeaLevel; y < SeaLevel + 4; y++)
            {
                world.SetGenerated(new BlockPos(x, y, 40), Block.Stone);
            }
        }

        var parked = PlaneState.Parked(new Vector3(32, SeaLevel, 60), 0);
        PlaneState crashed = Run(world, parked, Controls(buttons: InputButtons.Sprint), 6);

        Assert.True(crashed.Position.Z >= 41 + Settings.Width / 2 - 0.01f, $"z {crashed.Position.Z}");
        Assert.True(crashed.Airspeed < 5);
    }

    [Fact]
    public void DivingNeverPassesMaxSpeed()
    {
        Quaternion straightDown = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2 + 0.01f);
        PlaneState diving = Flying(1000, 30, 1, straightDown);

        PlaneState later = Run(Sea(), diving, Controls(buttons: InputButtons.Sprint), 5);

        Assert.True(later.Airspeed <= Settings.MaxSpeed);
    }

    [Fact]
    public void PlaneDataFileLoads()
    {
        PlaneSettings settings = PlaneSettings.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "plane.json")));

        Assert.True(settings.StallSpeed < settings.MaxSpeed);
    }
}

public class VehicleRulesTests
{
    private static readonly PlayerSettings Player = new();
    private static readonly PlaneSettings Plane = new();

    [Fact]
    public void EnterRangeIsMeasuredFromThePlaneBase()
    {
        var plane = PlaneState.Parked(new Vector3(10, 5, 10), 0);

        Assert.True(VehicleRules.InEnterRange(Plane, new Vector3(12, 5, 10), plane));
        Assert.False(VehicleRules.InEnterRange(Plane, new Vector3(20, 5, 10), plane));
    }

    [Fact]
    public void PilotsGetOutOnTheLeft()
    {
        var world = new VoxelWorld(2, 1, 2);
        var plane = PlaneState.Parked(new Vector3(16, 4, 16), 0); // facing -Z, so left is -X

        Vector3 exit = VehicleRules.ExitPosition(world, Player, Plane, plane);

        Assert.True(exit.X < 16 - Plane.Width / 2);
        Assert.Equal(16, exit.Z, 3);
        Assert.Equal(4, exit.Y, 3);
    }

    [Fact]
    public void ABlockedExitMovesUpUntilThereIsRoom()
    {
        var world = new VoxelWorld(2, 1, 2);
        for (int y = 4; y < 7; y++)
        {
            for (int x = 13; x < 16; x++)
            {
                world.SetGenerated(new BlockPos(x, y, 16), Block.Stone);
            }
        }

        var plane = PlaneState.Parked(new Vector3(16.5f, 4, 16.5f), 0);
        Vector3 exit = VehicleRules.ExitPosition(world, Player, Plane, plane);

        Assert.Equal(7, exit.Y, 3);
    }

    [Fact]
    public void AFlyingPawnCarriesThePlayerInTheSeat()
    {
        var world = new VoxelWorld(4, 2, 4);
        var rules = new GameRules(Player, Plane, new IslandWar.Sim.Weapons.BombSettings(), 8);
        var plane = new PlaneState(new Vector3(32, 20, 32), Quaternion.Identity, 30, 0, 0.6f, false);
        var pawn = new PawnState(PlayerState.At(plane.Position), 1, plane);

        PawnState next = Pawn.Step(world, rules, pawn, new PlayerInput(1, 0, 0, 0, 0, InputButtons.None), 1f / 60);

        Assert.True(next.InPlane);
        Assert.Equal(next.Plane.Position, next.Player.Position);
        Assert.True(next.Plane.Position.Z < 32);
    }
}
