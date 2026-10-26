using System.Numerics;
using IslandWar.Sim.Vehicles;
using IslandWar.Sim.Voxels;
using IslandWar.Sim.Weapons;

namespace IslandWar.Sim.Tests;

public class BombTests
{
    private const float Dt = 1f / 60;
    private const int SeaLevel = 8;
    private static readonly BombSettings Settings = new() { Gravity = 15, Drag = 0.05f, CraterRadius = 3 };

    /// <summary>A 4x2x4-chunk world with land (top face at y = 12) in the half where z is under 32, sea elsewhere.</summary>
    private static VoxelWorld LandAndSea()
    {
        var world = new VoxelWorld(4, 2, 4);
        for (int y = 0; y < 12; y++)
        {
            for (int z = 0; z < 32; z++)
            {
                for (int x = 0; x < world.SizeX; x++)
                {
                    world.SetGenerated(new BlockPos(x, y, z), Block.Stone);
                }
            }
        }

        return world;
    }

    private static (BombState Bomb, BombImpact? Impact, int Ticks) Fall(VoxelWorld world, BombState bomb)
    {
        for (int i = 1; i < 2000; i++)
        {
            (bomb, BombImpact? impact) = BombFlight.Step(world, Settings, SeaLevel, bomb, Dt);
            if (impact is not null)
            {
                return (bomb, impact, i);
            }
        }

        return (bomb, null, 2000);
    }

    [Fact]
    public void ADroppedBombFallsOntoTheBlockBelow()
    {
        var bomb = new BombState(new Vector3(20.5f, 40, 10.5f), Vector3.Zero);

        (_, BombImpact? impact, int ticks) = Fall(LandAndSea(), bomb);

        Assert.NotNull(impact);
        Assert.Equal(new BlockPos(20, 11, 10), impact.Value.Block);
        Assert.Equal(12, impact.Value.Position.Y, 2);

        // About sqrt(2h/g) seconds for a 28-block fall, a little more with drag.
        float expected = MathF.Sqrt(2 * 28 / Settings.Gravity) / Dt;
        Assert.InRange(ticks, expected - 3, expected + 12);
    }

    [Fact]
    public void ABombKeepsThePlanesForwardSpeed()
    {
        var bomb = new BombState(new Vector3(20.5f, 40, 30.5f), new Vector3(0, 0, -25));

        (_, BombImpact? impact, _) = Fall(LandAndSea(), bomb);

        // A ~1.9 s fall at ~25 blocks/s carries it well over 40 blocks forward (toward -Z).
        Assert.NotNull(impact);
        Assert.True(impact.Value.Position.Z < 30 - 40, $"landed at z {impact.Value.Position.Z}");
    }

    [Fact]
    public void TheSeaStopsABombWithoutACrater()
    {
        VoxelWorld world = LandAndSea();
        var bomb = new BombState(new Vector3(20.5f, 40, 50.5f), Vector3.Zero);

        (_, BombImpact? impact, _) = Fall(world, bomb);

        Assert.True(impact!.Value.InWater);
        Assert.Equal(SeaLevel, impact.Value.Position.Y, 2);
        Assert.Empty(BombFlight.Explode(world, Settings, impact.Value));
    }

    [Fact]
    public void AFastBombCantPassThroughAThinWall()
    {
        VoxelWorld world = LandAndSea();
        for (int y = 12; y < 30; y++)
        {
            for (int x = 0; x < world.SizeX; x++)
            {
                world.SetGenerated(new BlockPos(x, y, 20), Block.Planks);
            }
        }

        var bomb = new BombState(new Vector3(20.5f, 25, 28.5f), new Vector3(0, 0, -200));

        (_, BombImpact? impact, _) = Fall(world, bomb);

        Assert.Equal(20, impact!.Value.Block!.Value.Z);
    }

    [Fact]
    public void AnImpactBlastsACraterAroundTheHitBlock()
    {
        VoxelWorld world = LandAndSea();
        var bomb = new BombState(new Vector3(20.5f, 40, 10.5f), Vector3.Zero);
        (_, BombImpact? impact, _) = Fall(world, bomb);

        IReadOnlyList<VoxelEdit> edits = BombFlight.Explode(world, Settings, impact!.Value);

        Assert.NotEmpty(edits);
        Assert.Equal(Block.Air, world.Get(new BlockPos(20, 11, 10)));
        Assert.Equal(Block.Air, world.Get(new BlockPos(20, 8, 10)));
        Assert.Equal(Block.Stone, world.Get(new BlockPos(20, 7, 10)));
        Assert.Equal(edits.Count, world.Deltas.Count);
    }

    [Fact]
    public void PredictedImpactMatchesStepping()
    {
        VoxelWorld world = LandAndSea();
        var bomb = new BombState(new Vector3(20.5f, 40, 40.5f), new Vector3(3, 2, -22));

        BombImpact? predicted = BombFlight.PredictImpact(world, Settings, SeaLevel, bomb, Dt, 2000);
        (_, BombImpact? actual, _) = Fall(world, bomb);

        Assert.Equal(actual, predicted);
    }

    [Fact]
    public void BombsDropFromUnderThePlaneAtItsVelocity()
    {
        var plane = new PlaneState(new Vector3(5, 30, 5), Quaternion.Identity, 28, 0, 1, false);

        BombState bomb = BombFlight.Drop(Settings, plane);

        Assert.Equal(plane.Position.Y + Settings.DropHeight, bomb.Position.Y, 4);
        Assert.Equal(plane.Velocity, bomb.Velocity);
    }

    [Fact]
    public void WeaponsDataFileLoads()
    {
        BombSettings settings = BombSettings.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "weapons.json")));

        Assert.True(settings.CraterRadius > 0);
    }
}
