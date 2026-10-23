namespace IslandWar.Sim.Voxels;

/// <summary>A block position in world space. Y is up.</summary>
public readonly record struct BlockPos(int X, int Y, int Z)
{
    public override string ToString() => $"({X}, {Y}, {Z})";
}

/// <summary>A chunk position, measured in chunks rather than blocks.</summary>
public readonly record struct ChunkPos(int X, int Y, int Z)
{
    public static ChunkPos Containing(BlockPos p) =>
        new(FloorDiv(p.X, Chunk.Size), FloorDiv(p.Y, Chunk.Size), FloorDiv(p.Z, Chunk.Size));

    public BlockPos Origin => new(X * Chunk.Size, Y * Chunk.Size, Z * Chunk.Size);

    public override string ToString() => $"chunk({X}, {Y}, {Z})";

    private static int FloorDiv(int a, int b) => (a >= 0 ? a : a - b + 1) / b;
}

/// <summary>One block change: set the block at <see cref="Position"/> to <see cref="Block"/>.</summary>
public readonly record struct VoxelEdit(BlockPos Position, Block Block);
