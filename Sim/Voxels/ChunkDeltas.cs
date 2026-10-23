namespace IslandWar.Sim.Voxels;

/// <summary>
/// Every block changed since the world was generated, grouped by chunk, keeping only the latest
/// value per block. A late joiner rebuilds the current world from the terrain seed plus these
/// deltas, and the server can send them chunk by chunk.
/// </summary>
public sealed class ChunkDeltas
{
    private static readonly IReadOnlyDictionary<int, Block> Empty = new Dictionary<int, Block>();

    private readonly Dictionary<ChunkPos, Dictionary<int, Block>> _byChunk = new();

    /// <summary>Number of distinct blocks that have been edited.</summary>
    public int Count { get; private set; }

    public IEnumerable<ChunkPos> EditedChunks => _byChunk.Keys;

    public void Record(VoxelEdit edit)
    {
        ChunkPos chunk = ChunkPos.Containing(edit.Position);
        if (!_byChunk.TryGetValue(chunk, out Dictionary<int, Block>? blocks))
        {
            blocks = new Dictionary<int, Block>();
            _byChunk[chunk] = blocks;
        }

        BlockPos origin = chunk.Origin;
        int index = Chunk.Index(edit.Position.X - origin.X, edit.Position.Y - origin.Y, edit.Position.Z - origin.Z);
        if (!blocks.ContainsKey(index))
        {
            Count++;
        }

        blocks[index] = edit.Block;
    }

    /// <summary>Edited blocks in one chunk, keyed by <see cref="Chunk.Index"/>.</summary>
    public IReadOnlyDictionary<int, Block> ForChunk(ChunkPos chunk) =>
        _byChunk.TryGetValue(chunk, out Dictionary<int, Block>? blocks) ? blocks : Empty;

    /// <summary>All deltas as world-space edits, ready to replay onto a freshly generated world.</summary>
    public IEnumerable<VoxelEdit> All()
    {
        foreach ((ChunkPos chunk, Dictionary<int, Block> blocks) in _byChunk)
        {
            BlockPos origin = chunk.Origin;
            foreach ((int index, Block block) in blocks)
            {
                (int x, int y, int z) = Chunk.FromIndex(index);
                yield return new VoxelEdit(new BlockPos(origin.X + x, origin.Y + y, origin.Z + z), block);
            }
        }
    }
}
