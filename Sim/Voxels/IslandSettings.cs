namespace IslandWar.Sim.Voxels;

/// <summary>Island terrain tunables. Loaded from <c>data/terrain.json</c>; never hardcode these.</summary>
public sealed record IslandSettings
{
    /// <summary>World size in chunks.</summary>
    public int ChunksX { get; init; } = 8;
    public int ChunksY { get; init; } = 4;
    public int ChunksZ { get; init; } = 8;

    /// <summary>Block height of the water surface. Water is a flat plane, not blocks.</summary>
    public int SeaLevel { get; init; } = 16;

    /// <summary>Height of the sea floor where there is no island.</summary>
    public int SeabedDepth { get; init; } = 4;

    /// <summary>Radius of the island in blocks, measured from the world centre.</summary>
    public int IslandRadius { get; init; } = 48;

    /// <summary>
    /// Distance in blocks between the centres of two islands side by side along X, the second a mirror image
    /// of the first. 0 means a single island in the middle of the map.
    /// </summary>
    public int IslandSpacing { get; init; }

    /// <summary>Blocks above sea level at the island centre before hills are added.</summary>
    public int BaseHeight { get; init; } = 6;

    /// <summary>Extra height that noise can add on top of <see cref="BaseHeight"/>.</summary>
    public int HillHeight { get; init; } = 10;

    /// <summary>Distance between noise lattice points in blocks. Bigger means smoother hills.</summary>
    public int NoiseCellSize { get; init; } = 24;

    /// <summary>Surface blocks at or below sea level plus this many blocks are sand.</summary>
    public int BeachHeight { get; init; } = 1;

    /// <summary>Dirt depth under grass before stone starts.</summary>
    public int DirtDepth { get; init; } = 3;

    /// <summary>Seed used when the server is not given one on the command line.</summary>
    public int DefaultSeed { get; init; } = 1337;

    public static IslandSettings FromJson(string json)
    {
        IslandSettings settings = DataJson.Parse<IslandSettings>(json, "terrain.json");
        settings.Validate();
        return settings;
    }

    public void Validate()
    {
        int height = ChunksY * Chunk.Size;
        if (ChunksX <= 0 || ChunksY <= 0 || ChunksZ <= 0)
        {
            throw new InvalidDataException("World size must be at least one chunk in each direction.");
        }

        if (SeaLevel + BaseHeight + HillHeight >= height)
        {
            throw new InvalidDataException(
                $"Island peaks (sea_level + base_height + hill_height = {SeaLevel + BaseHeight + HillHeight}) must fit under the world height {height}.");
        }

        if (SeabedDepth <= 0 || SeabedDepth >= SeaLevel)
        {
            throw new InvalidDataException("seabed_depth must be between 1 and sea_level - 1.");
        }

        if (IslandRadius <= 0 || NoiseCellSize <= 0)
        {
            throw new InvalidDataException("island_radius and noise_cell_size must be positive.");
        }

        if (IslandSpacing < 0 || (IslandSpacing > 0 && IslandSpacing < 2 * IslandRadius))
        {
            throw new InvalidDataException("island_spacing must be 0 (one island) or at least twice island_radius, so the islands don't touch.");
        }

        int sizeX = ChunksX * Chunk.Size;
        int sizeZ = ChunksZ * Chunk.Size;
        int westEdge = IslandCentre(sizeX, sizeZ, 0).X - IslandRadius;
        if (westEdge < 0 || IslandRadius * 2 > sizeZ)
        {
            throw new InvalidDataException("The islands don't fit in the map: make chunks_x/chunks_z bigger, or island_radius/island_spacing smaller.");
        }
    }

    /// <summary>Number of islands: one per team when <see cref="IslandSpacing"/> is set.</summary>
    public int IslandCount => IslandSpacing > 0 ? 2 : 1;

    /// <summary>The centre column of island <paramref name="index"/> (0 is the west one) in a map of the given size.</summary>
    public (int X, int Z) IslandCentre(int sizeX, int sizeZ, int index)
    {
        if (IslandSpacing == 0)
        {
            return (sizeX / 2, sizeZ / 2);
        }

        int west = sizeX / 2 - IslandSpacing / 2;

        // The east island mirrors the west one about the map's middle, column for column.
        return (index == 0 ? west : sizeX - 1 - west, sizeZ / 2);
    }
}
