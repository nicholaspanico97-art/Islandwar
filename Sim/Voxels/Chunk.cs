namespace IslandWar.Sim.Voxels;

/// <summary>A cube of <see cref="Size"/>³ blocks. Coordinates passed in are local to the chunk.</summary>
public sealed class Chunk
{
    public const int Size = 16;
    public const int Volume = Size * Size * Size;

    private readonly Block[] _blocks = new Block[Volume];

    public Chunk(ChunkPos position)
    {
        Position = position;
    }

    public ChunkPos Position { get; }

    public ReadOnlySpan<Block> Blocks => _blocks;

    public static int Index(int x, int y, int z) => x + Size * (z + Size * y);

    public static (int X, int Y, int Z) FromIndex(int index) =>
        (index % Size, index / (Size * Size), index / Size % Size);

    public Block Get(int x, int y, int z) => _blocks[Index(x, y, z)];

    public void Set(int x, int y, int z, Block block) => _blocks[Index(x, y, z)] = block;
}
