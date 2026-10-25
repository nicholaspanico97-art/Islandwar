using System.Numerics;
using IslandWar.Sim.Players;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Tests;

public class BuildRulesTests
{
    private static readonly PlayerSettings Player = new() { Width = 0.6f, Height = 1.8f, Reach = 6 };

    private static VoxelWorld FloorWorld()
    {
        var world = new VoxelWorld(1, 1, 1);
        for (int z = 0; z < world.SizeZ; z++)
        {
            for (int x = 0; x < world.SizeX; x++)
            {
                world.SetGenerated(new BlockPos(x, 0, z), Block.Stone);
                world.SetGenerated(new BlockPos(x, 1, z), Block.Dirt);
            }
        }

        return world;
    }

    [Fact]
    public void CannotPlaceInsideAPlayer()
    {
        VoxelWorld world = FloorWorld();
        var feet = new Vector3(5.5f, 2, 5.5f);

        Assert.False(BuildRules.CanPlace(world, Player, new BlockPos(5, 2, 5), Block.Planks, new[] { feet }));
        Assert.False(BuildRules.CanPlace(world, Player, new BlockPos(5, 3, 5), Block.Planks, new[] { feet }));
        Assert.True(BuildRules.CanPlace(world, Player, new BlockPos(5, 4, 5), Block.Planks, new[] { feet }));
        Assert.True(BuildRules.CanPlace(world, Player, new BlockPos(6, 2, 5), Block.Planks, new[] { feet }));
    }

    [Fact]
    public void PlayerStraddlingABlockEdgeBlocksBothSides()
    {
        VoxelWorld world = FloorWorld();
        var feet = new Vector3(6.1f, 2, 5.5f);

        Assert.False(BuildRules.CanPlace(world, Player, new BlockPos(5, 2, 5), Block.Planks, new[] { feet }));
        Assert.False(BuildRules.CanPlace(world, Player, new BlockPos(6, 2, 5), Block.Planks, new[] { feet }));
    }

    [Fact]
    public void CannotPlaceIntoSolidOrOutOfBoundsOrAir()
    {
        VoxelWorld world = FloorWorld();
        Vector3[] nobody = Array.Empty<Vector3>();

        Assert.False(BuildRules.CanPlace(world, Player, new BlockPos(5, 1, 5), Block.Planks, nobody));
        Assert.False(BuildRules.CanPlace(world, Player, new BlockPos(-1, 2, 5), Block.Planks, nobody));
        Assert.False(BuildRules.CanPlace(world, Player, new BlockPos(5, 2, 5), Block.Air, nobody));
    }

    [Fact]
    public void BottomLayerAndAirCannotBeBroken()
    {
        VoxelWorld world = FloorWorld();

        Assert.True(BuildRules.CanBreak(world, new BlockPos(5, 1, 5)));
        Assert.False(BuildRules.CanBreak(world, new BlockPos(5, 0, 5)));
        Assert.False(BuildRules.CanBreak(world, new BlockPos(5, 2, 5)));
    }

    [Fact]
    public void ReachIsMeasuredFromTheEyeToTheBlockCentre()
    {
        var eye = new Vector3(0.5f, 10.5f, 0.5f);

        Assert.True(BuildRules.InReach(Player, eye, new BlockPos(0, 4, 0)));
        Assert.False(BuildRules.InReach(Player, eye, new BlockPos(0, 2, 0)));
    }

    [Fact]
    public void AnythingTheRaycastCanHitIsInReach()
    {
        VoxelWorld world = FloorWorld();
        var eye = new Vector3(3.2f, 4.7f, 3.9f);
        var direction = new Vector3(0.4f, -1, 0.3f);

        RaycastHit? hit = VoxelRaycast.Cast(world, eye, direction, Player.Reach);

        Assert.NotNull(hit);
        Assert.True(BuildRules.InReach(Player, eye, hit.Value.Block));
    }
}
