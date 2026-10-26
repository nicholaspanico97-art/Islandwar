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

    /// <summary>What it mines with, if it's a tool.</summary>
    public ToolSpec? Tool { get; init; }
}

/// <summary>A tool: what kind (axe, pickaxe), how good (tier) and how much faster it mines than bare hands.</summary>
public sealed record ToolSpec
{
    public string Kind { get; init; } = "";

    public int Tier { get; init; } = 1;

    public float Speed { get; init; } = 1;
}

/// <summary>One crafting recipe: what it takes and makes, and where it's made.</summary>
public sealed record Recipe
{
    public string Id { get; init; } = "";

    /// <summary>"hand" for the crafting window anywhere, or the id of the structure kind it's made at.</summary>
    public string Station { get; init; } = "hand";

    public IReadOnlyDictionary<string, int> Takes { get; init; } = new Dictionary<string, int>();

    public IReadOnlyDictionary<string, int> Makes { get; init; } = new Dictionary<string, int>();

    public bool ByHand => Station == "hand";
}

/// <summary>Mining one block: how long it takes by hand and what it gives.</summary>
public sealed record BlockRule
{
    /// <summary>Seconds to mine it by hand.</summary>
    public float Hardness { get; init; } = 1;

    /// <summary>The item it drops, or null for nothing.</summary>
    public string? Drops { get; init; }

    /// <summary>The kind of tool that mines it faster (axe, pickaxe), or null.</summary>
    public string? Tool { get; init; }

    /// <summary>The tier of that tool needed to mine it at all; 0 means bare hands will do.</summary>
    public int Tier { get; init; }
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

    public IReadOnlyList<Recipe> Recipes { get; init; } = Array.Empty<Recipe>();

    private Dictionary<string, ItemKind> _items = new();
    private Dictionary<Block, BlockRule> _rules = new();
    private Dictionary<Block, ItemKind> _placing = new();

    public ItemKind? Item(string id) => _items.TryGetValue(id, out ItemKind? item) ? item : null;

    /// <summary>The mining rule for a block, or null for one that can't be mined (air, structures).</summary>
    public BlockRule? RuleFor(Block block) => _rules.TryGetValue(block, out BlockRule? rule) ? rule : null;

    /// <summary>The item that places a block, or null if no item does.</summary>
    public ItemKind? ItemPlacing(Block block) => _placing.TryGetValue(block, out ItemKind? item) ? item : null;

    public Recipe? RecipeFor(string id) => Recipes.FirstOrDefault(r => r.Id == id);

    /// <summary>
    /// Seconds to mine a block with what a player carries: its hardness, divided by the speed of the best
    /// matching tool carried. Null if it needs a better tool than they have.
    /// </summary>
    public float? MiningSeconds(Block block, IReadOnlyDictionary<string, int> carried)
    {
        if (RuleFor(block) is not { } rule)
        {
            return null;
        }

        ToolSpec? best = rule.Tool is null
            ? null
            : carried.Keys.Select(Item).Select(i => i?.Tool).Where(t => t is not null && t.Kind == rule.Tool)
                .OrderByDescending(t => t!.Tier).ThenByDescending(t => t!.Speed).FirstOrDefault();
        if (rule.Tier > 0 && (best is null || best.Tier < rule.Tier))
        {
            return null;
        }

        return rule.Hardness / (best?.Speed ?? 1);
    }

    /// <summary>The weakest tool that can mine a block, for telling a player what they need; null if hands will do.</summary>
    public ItemKind? ToolNeededFor(Block block) =>
        RuleFor(block) is { Tier: > 0 } rule
            ? Items.Where(i => i.Tool is { } t && t.Kind == rule.Tool && t.Tier >= rule.Tier).OrderBy(i => i.Tool!.Tier).FirstOrDefault()
            : null;

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
            // A tier with no tool yet is fine: that tool comes in a later era (iron needs the Bronze Age's pickaxe).
            if (rule.Hardness < 0 || (rule.Drops is { } drop && !_items.ContainsKey(drop)) || rule.Tier < 0 || (rule.Tier > 0 && rule.Tool is null))
            {
                throw new InvalidDataException(
                    $"items.json: block '{name}' needs a hardness of 0 or more, to drop an item that exists, and (with a tier) the kind of tool it needs.");
            }

            _rules[block] = rule;
        }

        foreach (ItemKind item in Items)
        {
            if (item.Tool is { } tool && (tool.Kind.Length == 0 || tool.Tier < 1 || tool.Speed <= 0))
            {
                throw new InvalidDataException($"items.json: tool '{item.Id}' needs a kind, a tier of 1 or more and a positive speed.");
            }
        }

        var recipeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (Recipe recipe in Recipes)
        {
            bool known(IReadOnlyDictionary<string, int> counts) => counts.Count > 0 && counts.All(c => _items.ContainsKey(c.Key) && c.Value > 0);
            if (recipe.Id.Length == 0 || !recipeIds.Add(recipe.Id) || !known(recipe.Takes) || !known(recipe.Makes) || recipe.Station.Length == 0)
            {
                throw new InvalidDataException($"items.json: recipe '{recipe.Id}' needs a unique id, a station, and positive amounts of items that exist.");
            }
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
