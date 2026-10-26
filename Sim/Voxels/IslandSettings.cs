namespace IslandWar.Sim.Voxels;

/// <summary>
/// One point of the island outline: at <see cref="U"/> along the island (0 its far end, 1 the end facing the
/// strait), land runs from <see cref="North"/> to <see cref="South"/> across it (0 the north side of the
/// island's box, 1 the south side). The outline is drawn straight between points, then roughened by noise.
/// </summary>
public sealed record CoastPoint
{
    public float U { get; init; }

    public float North { get; init; }

    public float South { get; init; }
}

/// <summary>
/// One area of the island with its own landscape, placed by its centre and radii as fractions of the island
/// box (<see cref="U"/> along it from the far end, <see cref="V"/> across it from the north). Its effect is
/// strongest at the centre and fades out smoothly towards the edge.
/// </summary>
public sealed record IslandRegion
{
    /// <summary>"mountains" (craggy ridges), "hills" (one rounded hill) or "forest".</summary>
    public string Kind { get; init; } = "";

    public float U { get; init; }

    public float V { get; init; }

    public float RadiusU { get; init; }

    public float RadiusV { get; init; }

    /// <summary>Mountains and hills: extra height at the region's centre, in blocks.</summary>
    public int Height { get; init; }

    /// <summary>Forests: chance in a thousand of a tree in each tree cell at the region's centre.</summary>
    public int TreeChance { get; init; }
}

/// <summary>
/// One kind of ore: how many stone clumps in a thousand hold it at the heart of the mountains and away from
/// them (the chance blends between the two with the mountains' strength).
/// </summary>
public sealed record OreVein
{
    /// <summary>The ore's block name, such as <c>copper_ore</c>.</summary>
    public string Block { get; init; } = "";

    public int ChanceMountains { get; init; }

    public int ChanceElsewhere { get; init; }

    private Block? _kind;

    /// <summary>The block itself (looked up once; the generator asks for it per block of stone).</summary>
    public Block Kind => _kind ??= BlockNames.Parse(Block, "terrain.json");
}

/// <summary>
/// Island terrain tunables. Loaded from <c>data/terrain.json</c>; never hardcode these. The map holds two
/// identical islands side by side along X, the east one a mirror image of the west one, laid out from the
/// developer's sketch (docs/island-sketch.png): the far ends are the mountains, the near ends face each
/// other across the strait.
/// </summary>
public sealed record IslandSettings
{
    /// <summary>World size in chunks (1 chunk = 16 blocks).</summary>
    public int ChunksX { get; init; } = 93;
    public int ChunksY { get; init; } = 8;
    public int ChunksZ { get; init; } = 25;

    /// <summary>Block height of the water surface. Water is a flat plane, not blocks.</summary>
    public int SeaLevel { get; init; } = 32;

    /// <summary>How far below sea level the open sea floor is.</summary>
    public int SeabedDepth { get; init; } = 6;

    /// <summary>Length of each island's box along X, in blocks (600 is about two minutes' walk).</summary>
    public int IslandLength { get; init; } = 600;

    /// <summary>Width of each island's box along Z, in blocks.</summary>
    public int IslandWidth { get; init; } = 300;

    /// <summary>Open water between the two islands' boxes, in blocks.</summary>
    public int StraitWidth { get; init; } = 160;

    /// <summary>The island's shape, from the sketch. Needs at least two points, in order of <c>u</c>.</summary>
    public IReadOnlyList<CoastPoint> Outline { get; init; } = Array.Empty<CoastPoint>();

    /// <summary>How far the coastline wanders in or out from the outline, in blocks.</summary>
    public int CoastNoise { get; init; } = 16;

    /// <summary>Width of the underwater slope from the sea floor up to the shore, in blocks.</summary>
    public int CoastSlope { get; init; } = 20;

    /// <summary>Width of the sand beach around the island, in blocks.</summary>
    public int BeachWidth { get; init; } = 12;

    /// <summary>Height of the beach's inland edge above sea level.</summary>
    public int BeachHeight { get; init; } = 2;

    /// <summary>Distance inland from the beach over which the ground rises to its full height, in blocks.</summary>
    public int InlandSlope { get; init; } = 24;

    /// <summary>Height of open grassland above sea level, before rolling noise.</summary>
    public int LandHeight { get; init; } = 4;

    /// <summary>Extra height the gentle rolling noise can add anywhere inland.</summary>
    public int RollingHeight { get; init; } = 6;

    /// <summary>Distance between noise lattice points in blocks. Bigger means smoother land.</summary>
    public int NoiseCellSize { get; init; } = 32;

    /// <summary>Dirt depth under grass before stone starts.</summary>
    public int DirtDepth { get; init; } = 3;

    /// <summary>Ground this many blocks above sea level is bare rock instead of grass.</summary>
    public int RockLine { get; init; } = 26;

    /// <summary>Mountains, hills and forests, from the sketch.</summary>
    public IReadOnlyList<IslandRegion> Regions { get; init; } = Array.Empty<IslandRegion>();

    /// <summary>The kinds of ore under the islands. A clump of stone holds at most one kind.</summary>
    public IReadOnlyList<OreVein> Ores { get; init; } = Array.Empty<OreVein>();

    /// <summary>Chance in a thousand that a clump of beach sand is clay.</summary>
    public int ClayChance { get; init; } = 60;

    /// <summary>Ore comes in clumps of up to this many blocks across.</summary>
    public int OreClumpSize { get; init; } = 3;

    /// <summary>Trees stand on a grid of this many blocks, at most one per cell, so they never touch.</summary>
    public int TreeCellSize { get; init; } = 7;

    /// <summary>Chance in a thousand of a tree in each cell of open grassland.</summary>
    public int GrasslandTreeChance { get; init; } = 15;

    /// <summary>No trees within this many blocks of the island centre, where players spawn.</summary>
    public int SpawnClearing { get; init; } = 16;

    /// <summary>Seed used when the server is not given one on the command line.</summary>
    public int DefaultSeed { get; init; } = 1337;

    /// <summary>One island per team.</summary>
    public int IslandCount => 2;

    public int SizeX => ChunksX * Chunk.Size;
    public int SizeY => ChunksY * Chunk.Size;
    public int SizeZ => ChunksZ * Chunk.Size;

    /// <summary>Open water between the map's west edge and the west island's box (and the same in the east).</summary>
    public int Margin => (SizeX - 2 * IslandLength - StraitWidth) / 2;

    /// <summary>Where the islands' boxes start along Z.</summary>
    public int IslandZ0 => (SizeZ - IslandWidth) / 2;

    /// <summary>The tallest tree, top leaves included, above the ground it stands on.</summary>
    public const int TallestTree = 8;

    public static IslandSettings FromJson(string json)
    {
        IslandSettings settings = DataJson.Parse<IslandSettings>(json, "terrain.json");
        settings.Validate();
        return settings;
    }

    public void Validate()
    {
        if (ChunksX <= 0 || ChunksY <= 0 || ChunksZ <= 0)
        {
            throw new InvalidDataException("World size must be at least one chunk in each direction.");
        }

        if (SeabedDepth <= 0 || SeabedDepth >= SeaLevel)
        {
            throw new InvalidDataException("seabed_depth must be between 1 and sea_level - 1.");
        }

        if (IslandLength <= 0 || IslandWidth <= 0 || StraitWidth < 0 || CoastNoise < 0 || CoastSlope <= 0 || BeachWidth <= 0
            || InlandSlope <= 0 || NoiseCellSize <= 0 || DirtDepth <= 0 || OreClumpSize <= 0)
        {
            throw new InvalidDataException(
                "island_length, island_width, coast_slope, beach_width, inland_slope, noise_cell_size, dirt_depth and ore_clump_size must be positive; strait_width and coast_noise not negative.");
        }

        if (TreeCellSize < 7)
        {
            throw new InvalidDataException("tree_cell_size must be at least 7, so neighbouring trees' leaves never touch.");
        }

        if (Margin < CoastNoise + CoastSlope || IslandZ0 < CoastNoise + CoastSlope)
        {
            throw new InvalidDataException(
                "The islands don't fit in the map with room for the coast: chunks_x * 16 must be at least 2 * island_length + strait_width "
                + "+ 2 * (coast_noise + coast_slope), and chunks_z * 16 at least island_width + 2 * (coast_noise + coast_slope).");
        }

        if (Outline.Count < 2 || Outline.Zip(Outline.Skip(1)).Any(pair => pair.Second.U < pair.First.U)
            || Outline.Any(p => p.U is < 0 or > 1 || p.North is < 0 or > 1 || p.South is < 0 or > 1 || p.South < p.North))
        {
            throw new InvalidDataException("outline needs at least two points in order of u, with 0 <= north <= south <= 1 and u between 0 and 1.");
        }

        foreach (OreVein ore in Ores)
        {
            if (!BlockNames.IsOre(ore.Kind) || ore.Kind == Voxels.Block.Ore || ore.ChanceMountains is < 0 or > 1000 || ore.ChanceElsewhere is < 0 or > 1000)
            {
                throw new InvalidDataException($"ores: '{ore.Block}' needs to be an ore block, with chances of 0 to 1000.");
            }
        }

        if (Ores.Sum(o => Math.Max(o.ChanceMountains, o.ChanceElsewhere)) > 1000 || ClayChance is < 0 or > 1000)
        {
            throw new InvalidDataException("ores: the chances may add up to at most 1000 (a clump holds one kind), and clay_chance must be 0 to 1000.");
        }

        foreach (IslandRegion region in Regions)
        {
            if (region.Kind is not ("mountains" or "hills" or "forest") || region.RadiusU <= 0 || region.RadiusV <= 0
                || region.Height < 0 || region.TreeChance is < 0 or > 1000)
            {
                throw new InvalidDataException(
                    $"Region '{region.Kind}' needs kind mountains, hills or forest, positive radii, a height of 0 or more and a tree chance of 0 to 1000.");
            }
        }

        // Where regions overlap the generator clips the ground to fit; this catches a region that is simply too tall.
        int tallest = SeaLevel + LandHeight + RollingHeight + Regions.Select(r => r.Height).DefaultIfEmpty(0).Max() + TallestTree;
        if (tallest >= SizeY)
        {
            throw new InvalidDataException(
                $"The tallest region's ground plus a tree ({tallest}) must fit under the world height {SizeY}: add chunks_y or lower the region.");
        }
    }

    /// <summary>The centre column of island <paramref name="index"/> (0 is the west one), mirrored for the east one.</summary>
    public (int X, int Z) IslandCentre(int index)
    {
        int west = Margin + IslandLength / 2;
        return (index == 0 ? west : SizeX - 1 - west, IslandZ0 + IslandWidth / 2);
    }
}
