using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Tests;

public class CraterTests
{
    private static VoxelWorld SolidWorld()
    {
        var world = new VoxelWorld(2, 2, 2);
        for (int y = 0; y < world.SizeY; y++)
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
    public void BlastRemovesASphereAndNothingElse()
    {
        VoxelWorld world = SolidWorld();
        var center = new BlockPos(16, 16, 16);

        IReadOnlyList<VoxelEdit> edits = Craters.Blast(world, center, 3);

        Assert.Equal(Block.Air, world.Get(center));
        Assert.Equal(Block.Air, world.Get(new BlockPos(19, 16, 16)));
        Assert.Equal(Block.Stone, world.Get(new BlockPos(20, 16, 16)));
        Assert.Equal(Block.Stone, world.Get(new BlockPos(19, 19, 16)));
        Assert.Equal(edits.Count, world.Deltas.Count);
        Assert.All(edits, e => Assert.Equal(Block.Air, e.Block));
    }

    [Fact]
    public void BlastSkipsAirAndOutOfBoundsBlocks()
    {
        VoxelWorld world = SolidWorld();

        int first = Craters.Blast(world, new BlockPos(0, 0, 0), 2).Count;
        int second = Craters.Blast(world, new BlockPos(0, 0, 0), 2).Count;

        // Only the in-bounds octant of the sphere exists, and the second blast hits nothing.
        Assert.True(first > 0);
        Assert.Equal(0, second);
    }
}
