using System.Numerics;
using IslandWar.Sim.Players;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Tests;

public class PlayerMovementTests
{
    private const float Dt = 1f / 60;

    private static readonly PlayerSettings Settings = new()
    {
        Width = 0.6f, Height = 1.8f, WalkSpeed = 5, SprintMultiplier = 1.6f, JumpVelocity = 7.5f, Gravity = 24, MaxFallSpeed = 50, FlySpeed = 15,
    };

    /// <summary>A 2x1x2-chunk world with a solid floor whose top face is at y = 4.</summary>
    private static VoxelWorld FloorWorld()
    {
        var world = new VoxelWorld(2, 1, 2);
        for (int y = 0; y < 4; y++)
        {
            for (int z = 0; z < world.SizeZ; z++)
            {
                for (int x = 0; x < world.SizeX; x++)
                {
                    world.SetGenerated(new BlockPos(x, y, z), Block.Stone);
                }
            }
        }

        return world;
    }

    private static PlayerState Run(VoxelWorld world, PlayerState state, PlayerInput input, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            state = PlayerMovement.Step(world, Settings, state, input with { Seq = (uint)i }, Dt);
        }

        return state;
    }

    private static PlayerInput Idle => new(0, 0, 0, 0, 0, InputButtons.None);

    [Fact]
    public void FallsAndLandsOnTheFloor()
    {
        PlayerState state = Run(FloorWorld(), PlayerState.At(new Vector3(8.5f, 10, 8.5f)), Idle, 120);

        Assert.True(state.OnGround);
        Assert.Equal(4, state.Position.Y, 2);
        Assert.Equal(0, state.Velocity.Y);
    }

    [Fact]
    public void StandingStillStaysOnTheGround()
    {
        VoxelWorld world = FloorWorld();
        PlayerState landed = Run(world, PlayerState.At(new Vector3(8.5f, 4.5f, 8.5f)), Idle, 60);

        PlayerState later = Run(world, landed, Idle, 60);

        Assert.True(later.OnGround);
        Assert.Equal(landed.Position, later.Position);
    }

    [Fact]
    public void ForwardAtYawZeroMovesTowardMinusZAtWalkSpeed()
    {
        VoxelWorld world = FloorWorld();
        PlayerState start = Run(world, PlayerState.At(new Vector3(16.5f, 4.5f, 20.5f)), Idle, 30);

        PlayerState end = Run(world, start, Idle with { MoveZ = -1 }, 60);

        Assert.Equal(start.Position.X, end.Position.X, 3);
        Assert.Equal(start.Position.Z - 5, end.Position.Z, 1);
    }

    [Fact]
    public void YawTurnsTheMoveDirectionLikeGodot()
    {
        // Godot: rotating -Z by +90 degrees about +Y faces -X.
        Vector3 wish = PlayerMovement.WishDirection(new PlayerInput(0, 0, -1, MathF.PI / 2, 0, InputButtons.None));

        Assert.Equal(-1, wish.X, 4);
        Assert.Equal(0, wish.Z, 4);
    }

    [Fact]
    public void AWallStopsThePlayerAtItsFace()
    {
        VoxelWorld world = FloorWorld();
        for (int y = 4; y < 6; y++)
        {
            world.SetGenerated(new BlockPos(10, y, 8), Block.Planks);
        }

        PlayerState start = Run(world, PlayerState.At(new Vector3(8.5f, 4.5f, 8.5f)), Idle, 30);
        PlayerState end = Run(world, start, Idle with { MoveX = 1 }, 60);

        Assert.Equal(10 - Settings.Width / 2, end.Position.X, 2);
        Assert.Equal(0, end.Velocity.X);
    }

    [Fact]
    public void JumpPeaksAtVelocitySquaredOverTwoG()
    {
        VoxelWorld world = FloorWorld();
        PlayerState state = Run(world, PlayerState.At(new Vector3(8.5f, 4.5f, 8.5f)), Idle, 30);
        float ground = state.Position.Y;

        float peak = ground;
        state = PlayerMovement.Step(world, Settings, state, Idle with { Buttons = InputButtons.Jump }, Dt);
        for (int i = 0; i < 60; i++)
        {
            state = PlayerMovement.Step(world, Settings, state, Idle, Dt);
            peak = MathF.Max(peak, state.Position.Y);
        }

        float expected = Settings.JumpVelocity * Settings.JumpVelocity / (2 * Settings.Gravity);
        Assert.InRange(peak - ground, expected - 0.1f, expected + 0.1f);
        Assert.True(peak - ground > 1, "a jump must clear a one-block step");
        Assert.True(state.OnGround);
    }

    [Fact]
    public void JumpingWhileWalkingClimbsAOneBlockStep()
    {
        VoxelWorld world = FloorWorld();
        for (int z = 0; z < world.SizeZ; z++)
        {
            for (int x = 10; x < world.SizeX; x++)
            {
                world.SetGenerated(new BlockPos(x, 4, z), Block.Dirt);
            }
        }

        PlayerState start = Run(world, PlayerState.At(new Vector3(8.5f, 4.5f, 8.5f)), Idle, 30);
        PlayerState end = Run(world, start, Idle with { MoveX = 1, Buttons = InputButtons.Jump }, 90);

        Assert.True(end.Position.X > 11);
        Assert.True(end.Position.Y >= 5 - 0.01f);
    }

    [Fact]
    public void ACeilingStopsAJump()
    {
        VoxelWorld world = FloorWorld();
        world.SetGenerated(new BlockPos(8, 6, 8), Block.Stone);
        PlayerState state = Run(world, PlayerState.At(new Vector3(8.5f, 4.05f, 8.5f)), Idle, 30);

        float peak = 0;
        state = PlayerMovement.Step(world, Settings, state, Idle with { Buttons = InputButtons.Jump }, Dt);
        for (int i = 0; i < 30; i++)
        {
            state = PlayerMovement.Step(world, Settings, state, Idle, Dt);
            peak = MathF.Max(peak, state.Position.Y + Settings.Height);
        }

        Assert.True(peak <= 6, $"head reached {peak}, above the ceiling at 6");
    }

    [Fact]
    public void FlyingIgnoresBlocksAndGravity()
    {
        VoxelWorld world = FloorWorld();
        var start = PlayerState.At(new Vector3(8.5f, 2, 8.5f)); // inside the floor

        PlayerState hover = Run(world, start, Idle with { Buttons = InputButtons.Fly }, 30);
        PlayerState up = Run(world, start, Idle with { Buttons = InputButtons.Fly | InputButtons.Jump }, 60);

        Assert.Equal(start.Position, hover.Position);
        Assert.True(hover.Flying);
        Assert.Equal(2 + Settings.FlySpeed, up.Position.Y, 1);
    }

    [Fact]
    public void FallingOffTheMapKeepsFallingAtMaxSpeed()
    {
        var world = new VoxelWorld(1, 1, 1);

        PlayerState state = Run(world, PlayerState.At(new Vector3(-5, 0, -5)), Idle, 240);

        Assert.Equal(-Settings.MaxFallSpeed, state.Velocity.Y, 3);
        Assert.False(state.OnGround);
    }
}
