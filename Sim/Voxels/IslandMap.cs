namespace IslandWar.Sim.Voxels;

/// <summary>What a column of the natural terrain is.</summary>
public enum Ground : byte
{
    Sea,
    Beach,
    Land,
}

/// <summary>
/// The plan of the natural terrain, column by column, before any blocks are made: the coastline, the height
/// of the ground, and how much of the mountains and forests each column is in. Built from the settings and
/// the seed with integer math only (fixed point, 1024 meaning 1.0), so every machine gets the same map.
/// Only the west half is stored; the east half is its mirror image.
/// </summary>
public sealed class IslandMap
{
    /// <summary>Fixed-point 1.0.</summary>
    public const int One = 1024;

    /// <summary>A chamfer distance step: 3 per block straight, 4 diagonally (close to 3 times the true distance).</summary>
    private const int Straight = 3;
    private const int Diagonal = 4;
    private const int Far = int.MaxValue / 2;

    private readonly int _sizeX;
    private readonly int _halfX;
    private readonly int _sizeZ;
    private readonly int[] _surface;
    private readonly Ground[] _ground;
    private readonly int[] _mountains;
    private readonly int[] _treeChance;

    private IslandMap(int sizeX, int sizeZ)
    {
        _sizeX = sizeX;
        _halfX = (sizeX + 1) / 2;
        _sizeZ = sizeZ;
        _surface = new int[_halfX * sizeZ];
        _ground = new Ground[_halfX * sizeZ];
        _mountains = new int[_halfX * sizeZ];
        _treeChance = new int[_halfX * sizeZ];
    }

    /// <summary>Columns 0 to this (exclusive) are the stored west half; the rest mirror them.</summary>
    public int HalfX => _halfX;

    /// <summary>Height of the top solid block of the natural ground (before trees and edits).</summary>
    public int Surface(int x, int z) => _surface[Index(x, z)];

    public Ground GroundAt(int x, int z) => _ground[Index(x, z)];

    /// <summary>How deep in the mountains a column is, 0 to <see cref="One"/>.</summary>
    public int Mountains(int x, int z) => _mountains[Index(x, z)];

    /// <summary>Chance in a thousand of a tree in the tree cell around this column.</summary>
    public int TreeChance(int x, int z) => _treeChance[Index(x, z)];

    /// <summary>The column in the stored west half that column <paramref name="x"/> copies.</summary>
    public int WestColumn(int x) => x < _halfX ? x : _sizeX - 1 - x;

    /// <summary>The column on the east island that mirrors west column <paramref name="x"/>.</summary>
    public int Mirror(int x) => _sizeX - 1 - x;

    private int Index(int x, int z) => WestColumn(x) + _halfX * z;

    public static IslandMap Build(IslandSettings settings, int seed)
    {
        var map = new IslandMap(settings.SizeX, settings.SizeZ);
        bool[] land = map.Coastline(settings, seed);
        int[] inland = map.DistanceTo(land, target: false);
        int[] offshore = map.DistanceTo(land, target: true);
        Region[] regions = settings.Regions.Select(r => new Region(r, settings)).ToArray();

        int seabed = settings.SeaLevel - settings.SeabedDepth;
        int highest = settings.SizeY - IslandSettings.TallestTree - 1;
        for (int z = 0; z < map._sizeZ; z++)
        {
            for (int x = 0; x < map._halfX; x++)
            {
                int i = x + map._halfX * z;
                if (!land[i])
                {
                    // Offshore the sea floor slopes up to just under the water at the coast.
                    int along = One - Math.Min(offshore[i], Straight * settings.CoastSlope) * One / (Straight * settings.CoastSlope);
                    map._surface[i] = seabed + (settings.SeaLevel - 1 - seabed) * Noise.SmoothStep(along) / One;
                    map._ground[i] = Ground.Sea;
                    continue;
                }

                int blocksInland = inland[i] / Straight;
                if (blocksInland <= settings.BeachWidth)
                {
                    map._surface[i] = settings.SeaLevel + settings.BeachHeight * blocksInland / settings.BeachWidth;
                    map._ground[i] = Ground.Beach;
                    continue;
                }

                // Past the beach the ground rises to its full height over the inland slope.
                int climb = Math.Min(inland[i] - Straight * settings.BeachWidth, Straight * settings.InlandSlope);
                int rise = Noise.SmoothStep(climb * One / (Straight * settings.InlandSlope));
                int rolling = settings.RollingHeight * Noise.Octaves(x, z, settings.NoiseCellSize, seed + 1) / One;
                long height = settings.LandHeight - settings.BeachHeight + rolling;
                int mountains = 0;
                int trees = settings.GrasslandTreeChance;
                foreach (Region region in regions)
                {
                    int weight = region.Weight(x, z);
                    if (weight == 0)
                    {
                        continue;
                    }

                    switch (region.Kind)
                    {
                        case "mountains":
                            height += (long)region.Height * weight / One * MountainShape(x, z, settings, seed) / One;
                            mountains = Math.Max(mountains, weight);
                            break;
                        case "hills":
                            height += (long)region.Height * weight / One * (3 * One + Noise.Octaves(x, z, settings.NoiseCellSize, seed + 2)) / (4 * One);
                            break;
                        default:
                            trees = Math.Max(trees, region.TreeChance * weight / One);
                            break;
                    }
                }

                map._surface[i] = Math.Min(highest, settings.SeaLevel + settings.BeachHeight + (int)(height * rise / One));
                map._ground[i] = Ground.Land;
                map._mountains[i] = mountains;
                map._treeChance[i] = trees;
            }
        }

        return map;
    }

    /// <summary>
    /// Craggy ridges: the noise folded about its middle, so its mid values become sharp crests, and squared to
    /// steepen them. A third of the height is a floor under the crests so the range stays one mass.
    /// </summary>
    private static int MountainShape(int x, int z, IslandSettings settings, int seed)
    {
        int broad = Ridge(Noise.Value(x, z, settings.NoiseCellSize * 3 / 2, seed + 3));
        int fine = Ridge(Noise.Value(x, z, Math.Max(1, settings.NoiseCellSize * 3 / 4), seed + 4));
        int ridges = (2 * broad + fine) / 3;
        return One / 3 + 2 * ridges / 3;

        static int Ridge(int n)
        {
            int folded = One - Math.Abs(2 * n - One);
            return folded * folded / One;
        }
    }

    /// <summary>
    /// Which columns are land: inside the outline, with the coast pushed around by noise (the point is looked
    /// up a little way off, a different way at each spot), so the shore wanders instead of running straight.
    /// </summary>
    private bool[] Coastline(IslandSettings settings, int seed)
    {
        int count = settings.Outline.Count;
        int[] us = settings.Outline.Select(p => Fixed(p.U)).ToArray();
        int[] norths = settings.Outline.Select(p => Fixed(p.North)).ToArray();
        int[] souths = settings.Outline.Select(p => Fixed(p.South)).ToArray();

        var land = new bool[_halfX * _sizeZ];
        for (int z = 0; z < _sizeZ; z++)
        {
            for (int x = 0; x < _halfX; x++)
            {
                int shiftX = settings.CoastNoise * (2 * Noise.Octaves(x, z, settings.NoiseCellSize, seed + 5) - One) / One;
                int shiftZ = settings.CoastNoise * (2 * Noise.Octaves(x, z, settings.NoiseCellSize, seed + 6) - One) / One;
                int u = (x + shiftX - settings.Margin) * One / settings.IslandLength;
                int v = (z + shiftZ - settings.IslandZ0) * One / settings.IslandWidth;
                if (u < us[0] || u > us[count - 1])
                {
                    continue;
                }

                int next = 1;
                while (next < count - 1 && us[next] < u)
                {
                    next++;
                }

                int span = Math.Max(1, us[next] - us[next - 1]);
                int t = (u - us[next - 1]) * One / span;
                int north = norths[next - 1] + (norths[next] - norths[next - 1]) * t / One;
                int south = souths[next - 1] + (souths[next] - souths[next - 1]) * t / One;
                land[x + _halfX * z] = v >= north && v <= south;
            }
        }

        return land;
    }

    /// <summary>
    /// For each column, the chamfer distance (see <see cref="Straight"/>) to the nearest column whose land flag
    /// equals <paramref name="target"/>: two sweeps, forward then back, each taking the best of the neighbours
    /// already visited.
    /// </summary>
    private int[] DistanceTo(bool[] land, bool target)
    {
        var distance = new int[land.Length];
        for (int i = 0; i < land.Length; i++)
        {
            distance[i] = land[i] == target ? 0 : Far;
        }

        for (int z = 0; z < _sizeZ; z++)
        {
            for (int x = 0; x < _halfX; x++)
            {
                Relax(distance, x, z, -1, 0, Straight);
                Relax(distance, x, z, 0, -1, Straight);
                Relax(distance, x, z, -1, -1, Diagonal);
                Relax(distance, x, z, 1, -1, Diagonal);
            }
        }

        for (int z = _sizeZ - 1; z >= 0; z--)
        {
            for (int x = _halfX - 1; x >= 0; x--)
            {
                Relax(distance, x, z, 1, 0, Straight);
                Relax(distance, x, z, 0, 1, Straight);
                Relax(distance, x, z, 1, 1, Diagonal);
                Relax(distance, x, z, -1, 1, Diagonal);
            }
        }

        return distance;
    }

    private void Relax(int[] distance, int x, int z, int dx, int dz, int step)
    {
        int nx = x + dx;
        int nz = z + dz;
        if (nx < 0 || nx >= _halfX || nz < 0 || nz >= _sizeZ)
        {
            return;
        }

        int i = x + _halfX * z;
        int through = distance[nx + _halfX * nz] + step;
        if (through < distance[i])
        {
            distance[i] = through;
        }
    }

    private static int Fixed(float value) => (int)MathF.Round(value * One);

    /// <summary>A region in block coordinates on the west island.</summary>
    private sealed class Region
    {
        private readonly int _centreX;
        private readonly int _centreZ;
        private readonly int _radiusX;
        private readonly int _radiusZ;

        public Region(IslandRegion region, IslandSettings settings)
        {
            Kind = region.Kind;
            Height = region.Height;
            TreeChance = region.TreeChance;
            _centreX = settings.Margin + Fixed(region.U) * settings.IslandLength / One;
            _centreZ = settings.IslandZ0 + Fixed(region.V) * settings.IslandWidth / One;
            _radiusX = Math.Max(1, Fixed(region.RadiusU) * settings.IslandLength / One);
            _radiusZ = Math.Max(1, Fixed(region.RadiusV) * settings.IslandWidth / One);
        }

        public string Kind { get; }

        public int Height { get; }

        public int TreeChance { get; }

        /// <summary>1 (<see cref="One"/>) at the centre, falling smoothly to 0 at the edge of the ellipse.</summary>
        public int Weight(int x, int z)
        {
            long dx = x - _centreX;
            long dz = z - _centreZ;
            if (Math.Abs(dx) >= _radiusX || Math.Abs(dz) >= _radiusZ)
            {
                return 0;
            }

            long reach = dx * dx * One / ((long)_radiusX * _radiusX) + dz * dz * One / ((long)_radiusZ * _radiusZ);
            return reach >= One ? 0 : Noise.SmoothStep(One - (int)reach);
        }
    }
}

/// <summary>Integer value noise and hashes for terrain generation; 1024 means 1.0.</summary>
public static class Noise
{
    private const int One = IslandMap.One;

    /// <summary>Two octaves of value noise, weighted 2:1 toward the broad one. Returns 0 to 1023.</summary>
    public static int Octaves(int x, int z, int cellSize, int seed) =>
        (2 * Value(x, z, cellSize, seed) + Value(x, z, Math.Max(1, cellSize / 2), seed + 1000)) / 3;

    /// <summary>Smoothly interpolated random values on a square lattice. Returns 0 to 1023.</summary>
    public static int Value(int x, int z, int cellSize, int seed)
    {
        int cellX = FloorDiv(x, cellSize);
        int cellZ = FloorDiv(z, cellSize);
        int tx = SmoothStep((x - cellX * cellSize) * One / cellSize);
        int tz = SmoothStep((z - cellZ * cellSize) * One / cellSize);

        int a = Hash(cellX, 0, cellZ, seed) & (One - 1);
        int b = Hash(cellX + 1, 0, cellZ, seed) & (One - 1);
        int c = Hash(cellX, 0, cellZ + 1, seed) & (One - 1);
        int d = Hash(cellX + 1, 0, cellZ + 1, seed) & (One - 1);

        int top = a + (b - a) * tx / One;
        int bottom = c + (d - c) * tx / One;
        return top + (bottom - top) * tz / One;
    }

    /// <summary>3t² - 2t³ in fixed point, for t from 0 to 1024.</summary>
    public static int SmoothStep(int t) => (int)((long)t * t * (3 * One - 2 * t) / ((long)One * One));

    /// <summary>A well-mixed non-negative hash of a lattice point.</summary>
    public static int Hash(int x, int y, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)seed * 0x9E3779B1u;
            h ^= (uint)x * 0x85EBCA77u;
            h = (h ^ (h >> 15)) * 0xC2B2AE3Du;
            h ^= (uint)y * 0x7FEB352Du;
            h = (h ^ (h >> 15)) * 0x846CA68Bu;
            h ^= (uint)z * 0x27D4EB2Fu;
            h = (h ^ (h >> 13)) * 0x165667B1u;
            h ^= h >> 16;
            return (int)(h & 0x7FFFFFFF);
        }
    }

    public static int FloorDiv(int a, int b) => (a >= 0 ? a : a - b + 1) / b;
}
