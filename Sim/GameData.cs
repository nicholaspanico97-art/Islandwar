using IslandWar.Sim.Items;
using IslandWar.Sim.Match;
using IslandWar.Sim.Objects;
using IslandWar.Sim.Players;
using IslandWar.Sim.Protocol;
using IslandWar.Sim.Research;
using IslandWar.Sim.Vehicles;
using IslandWar.Sim.Voxels;
using IslandWar.Sim.Weapons;

namespace IslandWar.Sim;

/// <summary>
/// The tunables from <c>data/</c>. The server sends its raw JSON to each client on join, so clients
/// generate the same island and predict movement with the same numbers as the server.
/// </summary>
public sealed record GameData(string TerrainJson, string PlayerJson, string NetJson, string PlaneJson, string WeaponsJson, string PresetsJson, string StructuresJson, string ItemsJson, string TechsJson)
{
    public IslandSettings Terrain { get; } = IslandSettings.FromJson(TerrainJson);

    public PlayerSettings Player { get; } = PlayerSettings.FromJson(PlayerJson);

    public NetSettings Net { get; } = NetSettings.FromJson(NetJson);

    public PlaneSettings Plane { get; } = CheckedPlane(PlaneJson, NetSettings.FromJson(NetJson));

    public BombSettings Bomb { get; } = BombSettings.FromJson(WeaponsJson);

    public ArrowSettings Arrow { get; } = ArrowSettings.FromJson(WeaponsJson);

    public IReadOnlyDictionary<string, MatchPreset> Presets { get; } = MatchPreset.AllFromJson(PresetsJson);

    /// <summary>Items, what each block drops and how long it takes to mine.</summary>
    public ItemSettings Items => _economy.Items;

    /// <summary>The kinds of structure players can place.</summary>
    public ObjectKinds Kinds => _economy.Kinds;

    /// <summary>The eras and techs, and what research unlocks.</summary>
    public TechTree Techs => _economy.Techs;

    /// <summary>Items, structures and techs, loaded together so the names each uses from the others can be checked.</summary>
    private readonly (ItemSettings Items, ObjectKinds Kinds, TechTree Techs) _economy = LoadEconomy(ItemsJson, StructuresJson, TechsJson);

    public GameRules Rules => new(Player, Plane, Bomb, Terrain.SeaLevel);

    /// <summary>Reads every data file through <paramref name="read"/>, which gets a file name such as <c>terrain.json</c>.</summary>
    public static GameData Load(Func<string, string> read) => new(
        read("terrain.json"),
        read("player.json"),
        read("net.json"),
        read("plane.json"),
        read("weapons.json"),
        read("presets.json"),
        read("structures.json"),
        read("items.json"),
        read("techs.json"));

    /// <summary>
    /// Where a player appears, standing on the top block of their team's island. Slot 0 is the centre;
    /// later slots go round a small ring so players joining together don't stand inside each other.
    /// </summary>
    public PlayerState SpawnIn(VoxelWorld world, Team team, int slot = 0)
    {
        const int ringSlots = 8;
        (int x, int z) = Terrain.IslandCentre(team.IslandIndex());

        const int ringRadius = 3;
        if (slot > 0)
        {
            double angle = 2 * Math.PI * ((slot - 1) % ringSlots) / ringSlots;
            x += (int)Math.Round(ringRadius * Math.Cos(angle));
            z += (int)Math.Round(ringRadius * Math.Sin(angle));
        }

        int top = world.HighestSolid(x, z) ?? Terrain.SeaLevel;
        return PlayerState.At(new System.Numerics.Vector3(x + 0.5f, top + 1, z + 0.5f));
    }

    /// <summary>
    /// A team's plane starts on the water just off the south beach, level with its island's centre, facing
    /// out to sea: open water to take off over, out of sight of the strait.
    /// </summary>
    public PlaneState PlaneSpawnIn(VoxelWorld world, Team team)
    {
        const int offshore = 6;
        (int x, int z) = Terrain.IslandCentre(team.IslandIndex());
        while (z < world.SizeZ - offshore - 1 && (world.HighestSolid(x, z) ?? 0) >= Terrain.SeaLevel)
        {
            z++;
        }

        z += offshore;
        int ground = Math.Max(Terrain.SeaLevel, (world.HighestSolid(x, z) ?? 0) + 1);

        // Heading 0 faces -Z (north) and increases to the left, so 180 degrees faces south (+Z).
        return PlaneState.Parked(new System.Numerics.Vector3(x + 0.5f, ground, z + 0.5f), MathF.PI);
    }

    /// <summary>The named preset, or an error listing the ones that exist.</summary>
    public MatchPreset Preset(string name) =>
        Presets.TryGetValue(name, out MatchPreset? preset)
            ? preset
            : throw new InvalidDataException($"No preset '{name}' in presets.json. Choose one of: {string.Join(", ", Presets.Keys)}.");

    /// <summary>
    /// A plane may not cross more than one block per tick, or the block collision could skip one. Sink only
    /// builds up below stall speed, so the fastest move is max_speed, or stall_speed plus max_sink.
    /// </summary>
    private static PlaneSettings CheckedPlane(string json, NetSettings net)
    {
        PlaneSettings plane = PlaneSettings.FromJson(json);
        if (Math.Max(plane.MaxSpeed, plane.StallSpeed + plane.MaxSink) >= net.TickRate)
        {
            throw new InvalidDataException($"plane max_speed, and stall_speed + max_sink, must stay under tick_rate ({net.TickRate}).");
        }

        return plane;
    }

    /// <summary>Loads items, structures and techs, and checks recipe stations, structure costs and unlocks name things that exist.</summary>
    private static (ItemSettings, ObjectKinds, TechTree) LoadEconomy(string itemsJson, string structuresJson, string techsJson)
    {
        ItemSettings items = ItemSettings.FromJson(itemsJson);
        ObjectKinds kinds = ObjectKinds.FromJson(structuresJson);
        foreach (Recipe recipe in items.Recipes.Where(r => !r.ByHand && kinds.Get(r.Station) is null))
        {
            throw new InvalidDataException($"items.json: recipe '{recipe.Id}' is made at '{recipe.Station}', which isn't a structure in structures.json.");
        }

        foreach (ObjectKind kind in kinds.All.Where(k => k.Cost.Keys.Any(item => items.Item(item) is null)))
        {
            throw new InvalidDataException($"structures.json: '{kind.Id}' costs an item that isn't in items.json.");
        }

        return (items, kinds, TechTree.FromJson(techsJson, items, kinds));
    }
}
