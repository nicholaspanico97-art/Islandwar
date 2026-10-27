using IslandWar.Sim.Vehicles;

namespace IslandWar.Sim.Objects;

/// <summary>A size in blocks.</summary>
public sealed record Size3
{
    public int X { get; init; } = 1;
    public int Y { get; init; } = 1;
    public int Z { get; init; } = 1;
}

/// <summary>A structure that smelts: how many items each of its slots (to smelt, fuel, done) holds.</summary>
public sealed record SmelterSpec
{
    public int SlotCapacity { get; init; } = 32;
}

/// <summary>One kind of object players can place, from <c>data/structures.json</c>.</summary>
public sealed record ObjectKind
{
    /// <summary>Stable name used on the wire and in saves; never rename one.</summary>
    public string Id { get; init; } = "";

    /// <summary>What players see.</summary>
    public string Name { get; init; } = "";

    /// <summary>The blocks it fills, facing north (facing 0). Facing east or west swaps X and Z.</summary>
    public Size3 Size { get; init; } = new();

    public int MaxHealth { get; init; } = 100;

    /// <summary>How many items it stores, of any mix; 0 for a structure that isn't storage.</summary>
    public int Capacity { get; init; }

    public bool IsStorage => Capacity > 0;

    /// <summary>Set for a furnace: it smelts what's put in it while fuel burns.</summary>
    public SmelterSpec? Smelter { get; init; }

    /// <summary>What placing one takes, by item id.</summary>
    public IReadOnlyDictionary<string, int> Cost { get; init; } = new Dictionary<string, int>();

    /// <summary>
    /// Left by the game, not placed by players: a dead player's dropped backpack. Anyone may take from it,
    /// nothing goes in, it isn't team stock, and it goes once empty.
    /// </summary>
    public bool Loot { get; init; }

    /// <summary>Set for a dock: it builds ships, launched into the water in front of it.</summary>
    public DockSpec? Dock { get; init; }

    /// <summary>How clients draw it until there are models, as <c>#rrggbb</c>.</summary>
    public string Color { get; init; } = "#808080";
}

/// <summary>Every placeable kind, by id.</summary>
public sealed class ObjectKinds
{
    /// <summary>The largest a structure can be along any side, in blocks.</summary>
    public const int MaxSide = 16;

    private readonly Dictionary<string, ObjectKind> _byId;

    public ObjectKinds(IEnumerable<ObjectKind> kinds, IEnumerable<ShipKind>? ships = null)
    {
        Ships = new Dictionary<string, ShipKind>(StringComparer.Ordinal);
        foreach (ShipKind ship in ships ?? Array.Empty<ShipKind>())
        {
            if (!Ships.TryAdd(ship.Id, ship))
            {
                throw new InvalidDataException($"structures.json: two ships are called '{ship.Id}'.");
            }
        }

        _byId = new Dictionary<string, ObjectKind>(StringComparer.Ordinal);
        foreach (ObjectKind kind in kinds)
        {
            if (kind.Id.Length == 0 || kind.Name.Length == 0 || !_byId.TryAdd(kind.Id, kind))
            {
                throw new InvalidDataException($"structures.json: every kind needs a unique id and a name ('{kind.Id}').");
            }

            if (kind.Dock is { } dock && (dock.Ships.Count == 0 || dock.Ships.Any(s => !Ships.ContainsKey(s))))
            {
                throw new InvalidDataException($"structures.json: dock '{kind.Id}' must build ships from the ships list.");
            }

            if (kind.Smelter is { SlotCapacity: <= 0 })
            {
                throw new InvalidDataException($"structures.json: '{kind.Id}' needs a positive smelter slot_capacity.");
            }

            if (kind.Cost.Any(c => c.Value <= 0))
            {
                throw new InvalidDataException($"structures.json: '{kind.Id}' has a cost that isn't positive.");
            }

            if (kind.Size.X is < 1 or > MaxSide || kind.Size.Y is < 1 or > MaxSide || kind.Size.Z is < 1 or > MaxSide || kind.MaxHealth <= 0 || kind.Capacity < 0)
            {
                throw new InvalidDataException($"structures.json: '{kind.Id}' needs sides of 1 to {MaxSide} blocks, positive max_health and a capacity of 0 or more.");
            }
        }

        All = _byId.Values.ToArray();
    }

    /// <summary>In the order the data file lists them (the hotbar's order).</summary>
    public IReadOnlyList<ObjectKind> All { get; }

    public ObjectKind? Get(string id) => _byId.TryGetValue(id, out ObjectKind? kind) ? kind : null;

    /// <summary>Every kind of ship, by id.</summary>
    public Dictionary<string, ShipKind> Ships { get; }

    /// <summary>The ship a dock builds (the first it lists, until there's a choice), or null for any other kind.</summary>
    public ShipKind? ShipBuiltAt(ObjectKind kind) => kind.Dock is { } dock ? Ships[dock.Ships[0]] : null;

    public static ObjectKinds FromJson(string json)
    {
        KindsFile file = DataJson.Parse<KindsFile>(json, "structures.json");
        return new ObjectKinds(file.Kinds, file.Ships);
    }

    private sealed record KindsFile
    {
        public IReadOnlyList<ObjectKind> Kinds { get; init; } = Array.Empty<ObjectKind>();

        public IReadOnlyList<ShipKind> Ships { get; init; } = Array.Empty<ShipKind>();
    }
}
