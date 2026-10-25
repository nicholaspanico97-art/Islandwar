using System.Numerics;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Tests;

public class VoxelRaycastTests
{
    /// <summary>A 2x2x2-chunk world with a stone floor at y = 0..3.</summary>
    private static VoxelWorld FloorWorld()
    {
        var world = new VoxelWorld(2, 2, 2);
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

    [Fact]
    public void LookingStraightDownHitsTheFloorAndPlacesOnTop()
    {
        VoxelWorld world = FloorWorld();

        RaycastHit? hit = VoxelRaycast.Cast(world, new Vector3(5.5f, 10.5f, 7.5f), -Vector3.UnitY, 20);

        Assert.NotNull(hit);
        Assert.Equal(new BlockPos(5, 3, 7), hit.Value.Block);
        Assert.Equal(new BlockPos(5, 4, 7), hit.Value.Before);
        Assert.Equal(6.5f, hit.Value.Distance, 3);
    }

    [Fact]
    public void HittingAWallFromTheSidePlacesAgainstThatFace()
    {
        VoxelWorld world = FloorWorld();
        world.SetGenerated(new BlockPos(10, 4, 5), Block.Planks);

        RaycastHit? hit = VoxelRaycast.Cast(world, new Vector3(5.5f, 4.5f, 5.5f), Vector3.UnitX, 20);

        Assert.NotNull(hit);
        Assert.Equal(new BlockPos(10, 4, 5), hit.Value.Block);
        Assert.Equal(new BlockPos(9, 4, 5), hit.Value.Before);
    }

    [Fact]
    public void DiagonalRayFindsTheBlockItCrosses()
    {
        VoxelWorld world = FloorWorld();

        RaycastHit? hit = VoxelRaycast.Cast(world, new Vector3(2.2f, 8.7f, 2.4f), new Vector3(1, -1, 0.5f), 20);

        Assert.NotNull(hit);
        Assert.Equal(3, hit.Value.Block.Y);
        Assert.Equal(4, hit.Value.Before.Y);
        Assert.Equal(hit.Value.Block.X, hit.Value.Before.X);
        Assert.Equal(hit.Value.Block.Z, hit.Value.Before.Z);
    }

    [Fact]
    public void ReturnsNullWhenNothingIsInReach()
    {
        VoxelWorld world = FloorWorld();

        Assert.Null(VoxelRaycast.Cast(world, new Vector3(5.5f, 20.5f, 5.5f), -Vector3.UnitY, 5));
        Assert.Null(VoxelRaycast.Cast(world, new Vector3(5.5f, 20.5f, 5.5f), Vector3.UnitY, 100));
        Assert.Null(VoxelRaycast.Cast(world, new Vector3(5.5f, 20.5f, 5.5f), Vector3.Zero, 100));
    }

    [Fact]
    public void StartingInsideABlockHitsThatBlock()
    {
        VoxelWorld world = FloorWorld();

        RaycastHit? hit = VoxelRaycast.Cast(world, new Vector3(5.5f, 2.5f, 5.5f), Vector3.UnitX, 5);

        Assert.Equal(new BlockPos(5, 2, 5), hit?.Block);
    }
}
