namespace IslandWar.Sim.Voxels;

/// <summary>
/// The authoritative block data for a bounded map. Whatever renders the world (godot_voxel or a
/// custom mesher) is a view of this. The world starts at block (0, 0, 0); anything outside the
/// bounds reads as air and cannot be edited.
/// </summary>
public sealed class VoxelWorld
{
    private readonly Chunk[] _chunks;

    public VoxelWorld(int chunksX, int chunksY, int chunksZ)
    {
        if (chunksX <= 0 || chunksY <= 0 || chunksZ <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunksX), "World must be at least one chunk in each direction.");
        }

        ChunksX = chunksX;
        ChunksY = chunksY;
        ChunksZ = chunksZ;
        _chunks = new Chunk[chunksX * chunksY * chunksZ];
        for (int y = 0; y < chunksY; y++)
        {
            for (int z = 0; z < chunksZ; z++)
            {
                for (int x = 0; x < chunksX; x++)
                {
                    _chunks[ChunkIndex(x, y, z)] = new Chunk(new ChunkPos(x, y, z));
                }
            }
        }
    }

    public int ChunksX { get; }
    public int ChunksY { get; }
    public int ChunksZ { get; }

    public int SizeX => ChunksX * Chunk.Size;
    public int SizeY => ChunksY * Chunk.Size;
    public int SizeZ => ChunksZ * Chunk.Size;

    /// <summary>Player edits applied with <see cref="ApplyEdit"/>. Terrain generation is not recorded.</summary>
    public ChunkDeltas Deltas { get; } = new();

    /// <summary>Chunks in a fixed order (Y, then Z, then X).</summary>
    public IReadOnlyList<Chunk> Chunks => _chunks;

    public bool InBounds(BlockPos p) =>
        p.X >= 0 && p.X < SizeX && p.Y >= 0 && p.Y < SizeY && p.Z >= 0 && p.Z < SizeZ;

    public Chunk? GetChunk(ChunkPos c) =>
        c.X >= 0 && c.X < ChunksX && c.Y >= 0 && c.Y < ChunksY && c.Z >= 0 && c.Z < ChunksZ
            ? _chunks[ChunkIndex(c.X, c.Y, c.Z)]
            : null;

    public Block Get(BlockPos p)
    {
        if (!InBounds(p))
        {
            return Block.Air;
        }

        return ChunkAt(p).Get(p.X % Chunk.Size, p.Y % Chunk.Size, p.Z % Chunk.Size);
    }

    /// <summary>Y of the highest solid block in a column, or null if the column is all air.</summary>
    public int? HighestSolid(int x, int z)
    {
        for (int y = SizeY - 1; y >= 0; y--)
        {
            if (Get(new BlockPos(x, y, z)) != Block.Air)
            {
                return y;
            }
        }

        return null;
    }

    /// <summary>
    /// Sets a block without recording it as an edit. Use for terrain generation only; gameplay
    /// changes go through <see cref="ApplyEdit"/> so late joiners receive them.
    /// </summary>
    /// <returns>False if the position is out of bounds.</returns>
    public bool SetGenerated(BlockPos p, Block block)
    {
        if (!InBounds(p))
        {
            return false;
        }

        ChunkAt(p).Set(p.X % Chunk.Size, p.Y % Chunk.Size, p.Z % Chunk.Size, block);
        return true;
    }

    /// <summary>Applies a gameplay edit and records it in <see cref="Deltas"/>.</summary>
    /// <returns>True if the block changed. Out-of-bounds edits and no-op edits return false.</returns>
    public bool ApplyEdit(VoxelEdit edit)
    {
        if (!InBounds(edit.Position) || Get(edit.Position) == edit.Block)
        {
            return false;
        }

        SetGenerated(edit.Position, edit.Block);
        Deltas.Record(edit);
        return true;
    }

    /// <summary>
    /// A 64-bit FNV-1a hash of every block. Two worlds with the same hash hold the same blocks;
    /// the sync tests compare server and client worlds this way.
    /// </summary>
    public ulong ComputeHash()
    {
        const ulong offsetBasis = 14695981039346656037;
        const ulong prime = 1099511628211;

        ulong hash = offsetBasis;
        foreach (Chunk chunk in _chunks)
        {
            foreach (Block block in chunk.Blocks)
            {
                ushort value = (ushort)block;
                hash = (hash ^ (byte)value) * prime;
                hash = (hash ^ (byte)(value >> 8)) * prime;
            }
        }

        return hash;
    }

    private int ChunkIndex(int x, int y, int z) => x + ChunksX * (z + ChunksZ * y);

    private Chunk ChunkAt(BlockPos p) => _chunks[ChunkIndex(p.X / Chunk.Size, p.Y / Chunk.Size, p.Z / Chunk.Size)];
}
