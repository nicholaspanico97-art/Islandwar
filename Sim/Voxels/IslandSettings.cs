using System.Text.Json;

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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static IslandSettings FromJson(string json)
    {
        IslandSettings settings = JsonSerializer.Deserialize<IslandSettings>(json, JsonOptions)
            ?? throw new InvalidDataException("Terrain settings JSON was empty.");
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
    }
}
