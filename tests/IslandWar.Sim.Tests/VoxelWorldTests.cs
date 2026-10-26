using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Tests;

public class VoxelWorldTests
{
    [Fact]
    public void BlocksReadBackAcrossChunkBoundaries()
    {
        var world = new VoxelWorld(2, 2, 2);
        var a = new BlockPos(15, 15, 15);
        var b = new BlockPos(16, 16, 16);

        world.SetGenerated(a, Block.Stone);
        world.SetGenerated(b, Block.Planks);

        Assert.Equal(Block.Stone, world.Get(a));
        Assert.Equal(Block.Planks, world.Get(b));
        Assert.Equal(Block.Air, world.Get(new BlockPos(16, 15, 15)));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(32, 0, 0)]
    [InlineData(0, 0, 32)]
    public void OutOfBoundsReadsAsAirAndCannotBeEdited(int x, int y, int z)
    {
        var world = new VoxelWorld(2, 2, 2);
        var p = new BlockPos(x, y, z);

        Assert.Equal(Block.Air, world.Get(p));
        Assert.False(world.SetGenerated(p, Block.Stone));
        Assert.False(world.ApplyEdit(new VoxelEdit(p, Block.Stone)));
        Assert.Equal(0, world.Deltas.Count);
    }

    [Fact]
    public void EditsAreRecordedButGenerationIsNot()
    {
        var world = new VoxelWorld(1, 1, 1);
        world.SetGenerated(new BlockPos(1, 1, 1), Block.Stone);

        Assert.True(world.ApplyEdit(new VoxelEdit(new BlockPos(2, 2, 2), Block.Planks)));
        Assert.False(world.ApplyEdit(new VoxelEdit(new BlockPos(2, 2, 2), Block.Planks)));

        Assert.Equal(1, world.Deltas.Count);
        Assert.Equal(new VoxelEdit(new BlockPos(2, 2, 2), Block.Planks), Assert.Single(world.Deltas.All()));
    }

    [Fact]
    public void DeltasKeepOnlyTheLatestValuePerBlock()
    {
        var world = new VoxelWorld(1, 1, 1);
        var p = new BlockPos(3, 4, 5);

        world.ApplyEdit(new VoxelEdit(p, Block.Planks));
        world.ApplyEdit(new VoxelEdit(p, Block.Stone));

        Assert.Equal(new VoxelEdit(p, Block.Stone), Assert.Single(world.Deltas.All()));
    }

    [Fact]
    public void AUniformChunkStoresNoArrayUntilSomethingDiffers()
    {
        var chunk = new Chunk(new ChunkPos(0, 0, 0));

        chunk.Set(1, 2, 3, Block.Air);
        Assert.True(chunk.IsUniform);

        chunk.Set(1, 2, 3, Block.Stone);
        Assert.False(chunk.IsUniform);
        Assert.Equal(Block.Stone, chunk.Get(1, 2, 3));
        Assert.Equal(Block.Air, chunk.Get(0, 0, 0));
    }

    [Fact]
    public void TheHashDependsOnBlocksNotOnHowChunksAreStored()
    {
        var stored = new VoxelWorld(1, 1, 1);
        var uniform = new VoxelWorld(1, 1, 1);
        var p = new BlockPos(4, 4, 4);

        // Placing and removing a block leaves an array of all air in one world, a uniform chunk in the other.
        stored.SetGenerated(p, Block.Stone);
        stored.SetGenerated(p, Block.Air);

        Assert.False(stored.Chunks[0].IsUniform);
        Assert.True(uniform.Chunks[0].IsUniform);
        Assert.Equal(uniform.ComputeHash(), stored.ComputeHash());

        stored.Compact();
        Assert.True(stored.Chunks[0].IsUniform);
    }

    [Fact]
    public void ChunkIndexRoundTrips()
    {
        for (int i = 0; i < Chunk.Volume; i++)
        {
            (int x, int y, int z) = Chunk.FromIndex(i);
            Assert.Equal(i, Chunk.Index(x, y, z));
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(15, 0)]
    [InlineData(16, 1)]
    [InlineData(-1, -1)]
    [InlineData(-16, -1)]
    [InlineData(-17, -2)]
    public void ChunkContainingUsesFloorDivision(int block, int chunk)
    {
        Assert.Equal(new ChunkPos(chunk, chunk, chunk), ChunkPos.Containing(new BlockPos(block, block, block)));
    }

    [Fact]
    public void HashChangesWhenAnyBlockChanges()
    {
        var world = new VoxelWorld(2, 1, 2);
        ulong before = world.ComputeHash();

        world.ApplyEdit(new VoxelEdit(new BlockPos(31, 0, 31), Block.Sand));

        Assert.NotEqual(before, world.ComputeHash());
    }

    [Fact]
    public void HighestSolidFindsTheTopBlockOfAColumn()
    {
        var world = new VoxelWorld(1, 2, 1);
        world.SetGenerated(new BlockPos(3, 0, 3), Block.Stone);
        world.SetGenerated(new BlockPos(3, 20, 3), Block.Planks);

        Assert.Equal(20, world.HighestSolid(3, 3));
        Assert.Null(world.HighestSolid(4, 4));
    }
}
