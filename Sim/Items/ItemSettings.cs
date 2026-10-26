using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Items;

/// <summary>One kind of item, from <c>data/items.json</c>.</summary>
public sealed record ItemKind
{
    /// <summary>Stable name used on the wire and in object values; never rename one.</summary>
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    /// <summary>How clients draw it until there are icons, as <c>#rrggbb</c>.</summary>
    public string Color { get; init; } = "#808080";

    /// <summary>The block name it puts down when placed, or null for a raw material.</summary>
    public string? Places { get; init; }
}

/// <summary>Mining one block: how long it takes by hand and what it gives.</summary>
public sealed record BlockRule
{
    /// <summary>Seconds to mine it by hand.</summary>
    public float Hardness { get; init; } = 1;

    /// <summary>The item it drops, or null for nothing.</summary>
    public string? Drops { get; init; }
}

/// <summary>
/// Items and mining, from <c>data/items.json</c>: what each item is, what each block drops and how hard it
/// is, and how much a player can carry. Loaded once and checked so every name refers to something real.
/// </summary>
public sealed record ItemSettings
{
    public int BackpackCapacity { get; init; } = 40;

    public float MiningSlackSeconds { get; init; } = 0.25f;

    public float StructureHardness { get; init; } = 1.5f;

    public IReadOnlyList<ItemKind> Items { get; init; } = Array.Empty<ItemKind>();

    public IReadOnlyDictionary<string, BlockRule> Blocks { get; init; } = new Dictionary<string, BlockRule>();

    private Dictionary<string, ItemKind> _items = new();
    private Dictionary<Block, BlockRule> _rules = new();
    private Dictionary<Block, ItemKind> _placing = new();

    public ItemKind? Item(string id) => _items.TryGetValue(id, out ItemKind? item) ? item : null;

    /// <summary>The mining rule for a block, or null for one that can't be mined (air, structures).</summary>
    public BlockRule? RuleFor(Block block) => _rules.TryGetValue(block, out BlockRule? rule) ? rule : null;

    /// <summary>The item that places a block, or null if no item does.</summary>
    public ItemKind? ItemPlacing(Block block) => _placing.TryGetValue(block, out ItemKind? item) ? item : null;

    /// <summary>The block an item places, or null.</summary>
    public Block? BlockPlacedBy(ItemKind item) => item.Places is { } name ? BlockNames.Parse(name, "items.json") : null;

    public static ItemSettings FromJson(string json)
    {
        ItemSettings settings = DataJson.Parse<ItemSettings>(json, "items.json");
        settings.Index();
        return settings;
    }

    private void Index()
    {
        if (BackpackCapacity <= 0 || MiningSlackSeconds < 0 || StructureHardness < 0)
        {
            throw new InvalidDataException("items.json: backpack_capacity must be positive, mining_slack_seconds and structure_hardness not negative.");
        }

        _items = new Dictionary<string, ItemKind>(StringComparer.Ordinal);
        foreach (ItemKind item in Items)
        {
            if (item.Id.Length == 0 || item.Name.Length == 0 || !_items.TryAdd(item.Id, item))
            {
                throw new InvalidDataException($"items.json: every item needs a unique id and a name ('{item.Id}').");
            }

            if (BlockPlacedBy(item) is { } block)
            {
                if (!Players.BuildRules.Placeable(block) || !_placing.TryAdd(block, item))
                {
                    throw new InvalidDataException($"items.json: '{item.Id}' places '{item.Places}', which isn't placeable or is placed by another item.");
                }
            }
        }

        _rules = new Dictionary<Block, BlockRule>();
        foreach ((string name, BlockRule rule) in Blocks)
        {
            Block block = BlockNames.Parse(name, "items.json");
            if (rule.Hardness < 0 || (rule.Drops is { } drop && !_items.ContainsKey(drop)))
            {
                throw new InvalidDataException($"items.json: block '{name}' needs a hardness of 0 or more and drops an item that exists.");
            }

            _rules[block] = rule;
        }

        // Every block players can meet must say how it's mined.
        foreach (Block block in Enum.GetValues<Block>())
        {
            bool natural = block is not (Block.Air or Block.Core or Block.Ore or Block.Structure);
            if (natural && !_rules.ContainsKey(block))
            {
                throw new InvalidDataException($"items.json: blocks has no entry for '{BlockNames.Of(block)}'.");
            }
        }
    }
}
