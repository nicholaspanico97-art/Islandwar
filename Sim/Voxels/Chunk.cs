namespace IslandWar.Sim.Voxels;

/// <summary>
/// A cube of <see cref="Size"/>³ blocks. Coordinates passed in are local to the chunk. A chunk that is all
/// one block type (open sky, deep sea floor) stores no array until something different is set in it, so a
/// big map costs memory only where there's detail.
/// </summary>
public sealed class Chunk
{
    public const int Size = 16;
    public const int Volume = Size * Size * Size;

    private Block[]? _blocks;
    private Block _uniform;

    public Chunk(ChunkPos position)
    {
        Position = position;
    }

    public ChunkPos Position { get; }

    /// <summary>True while every block is <see cref="UniformBlock"/> and no array is stored.</summary>
    public bool IsUniform => _blocks is null;

    /// <summary>The block filling the whole chunk while it is uniform.</summary>
    public Block UniformBlock => _uniform;

    public static int Index(int x, int y, int z) => x + Size * (z + Size * y);

    public static (int X, int Y, int Z) FromIndex(int index) =>
        (index % Size, index / (Size * Size), index / Size % Size);

    public Block Get(int x, int y, int z) => _blocks is null ? _uniform : _blocks[Index(x, y, z)];

    public void Set(int x, int y, int z, Block block)
    {
        if (_blocks is null)
        {
            if (block == _uniform)
            {
                return;
            }

            _blocks = new Block[Volume];
            Array.Fill(_blocks, _uniform);
        }

        _blocks[Index(x, y, z)] = block;
    }

    /// <summary>Drops the array again if every block turned out the same (after generation, say).</summary>
    public void Compact()
    {
        if (_blocks is null)
        {
            return;
        }

        Block first = _blocks[0];
        if (Array.TrueForAll(_blocks, b => b == first))
        {
            _uniform = first;
            _blocks = null;
        }
    }

    /// <summary>
    /// A 64-bit FNV-1a hash of the chunk's blocks in index order. It depends only on the blocks, not on how
    /// they're stored, so a uniform chunk and an array holding the same blocks hash the same.
    /// </summary>
    public ulong ContentHash()
    {
        if (_blocks is null)
        {
            return UniformHash(_uniform);
        }

        ulong hash = FnvOffsetBasis;
        foreach (Block block in _blocks)
        {
            hash = Mix(hash, (ushort)block);
        }

        return hash;
    }

    private const ulong FnvOffsetBasis = 14695981039346656037;
    private const ulong FnvPrime = 1099511628211;

    private static readonly Dictionary<Block, ulong> UniformHashes = new();

    private static ulong UniformHash(Block block)
    {
        lock (UniformHashes)
        {
            if (!UniformHashes.TryGetValue(block, out ulong hash))
            {
                hash = FnvOffsetBasis;
                for (int i = 0; i < Volume; i++)
                {
                    hash = Mix(hash, (ushort)block);
                }

                UniformHashes[block] = hash;
            }

            return hash;
        }
    }

    private static ulong Mix(ulong hash, ushort value)
    {
        hash = (hash ^ (byte)value) * FnvPrime;
        return (hash ^ (byte)(value >> 8)) * FnvPrime;
    }
}
