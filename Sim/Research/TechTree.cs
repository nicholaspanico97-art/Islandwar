using IslandWar.Sim.Items;
using IslandWar.Sim.Match;
using IslandWar.Sim.Objects;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Research;

/// <summary>What a tech, or reaching an era, makes available. Anything named here is locked until then.</summary>
public sealed record Unlocks
{
    public IReadOnlyList<string> Recipes { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Structures { get; init; } = Array.Empty<string>();

    /// <summary>Smelting recipes, by the item they make.</summary>
    public IReadOnlyList<string> Smelting { get; init; } = Array.Empty<string>();

    /// <summary>Items that may be placed as blocks from the hotbar.</summary>
    public IReadOnlyList<string> Places { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Vehicles { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Weapons { get; init; } = Array.Empty<string>();

    /// <summary>Mined drops multiplied, by item: prospecting doubles iron ore.</summary>
    public IReadOnlyDictionary<string, int> Yield { get; init; } = new Dictionary<string, int>();

    /// <summary>Everything this locks, as <see cref="Unlock"/> keys (yields aren't locks: without them you just get less).</summary>
    public IEnumerable<string> Keys =>
        Recipes.Select(Unlock.Recipe).Concat(Structures.Select(Unlock.Structure)).Concat(Smelting.Select(Unlock.Smelting))
            .Concat(Places.Select(Unlock.Place)).Concat(Vehicles.Select(Unlock.Vehicle)).Concat(Weapons.Select(Unlock.Weapon));
}

/// <summary>The names of things that research can lock, one key per thing: <c>recipe:stone_axe</c>, <c>structure:furnace</c>.</summary>
public static class Unlock
{
    public static string Recipe(string id) => "recipe:" + id;

    public static string Structure(string kind) => "structure:" + kind;

    public static string Smelting(string makes) => "smelting:" + makes;

    public static string Place(string item) => "place:" + item;

    public static string Vehicle(string id) => "vehicle:" + id;

    public static string Weapon(string id) => "weapon:" + id;

    public const string Plane = "vehicle:plane";

    public const string Nuke = "weapon:nuke";
}

/// <summary>What earns research points: the action, then block name or item id, then points.</summary>
public static class PointSource
{
    public const string Mine = "mine";
    public const string Place = "place";
    public const string Smelt = "smelt";
}

/// <summary>One era, from <c>data/techs.json</c>.</summary>
public sealed record Era
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    /// <summary>The average time a team should spend in it: a balancing target.</summary>
    public float Minutes { get; init; }

    /// <summary>What earns this era's points: action (<see cref="PointSource"/>), then block or item, then points.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Points { get; init; } = new Dictionary<string, IReadOnlyDictionary<string, int>>();

    /// <summary>The techs that take a team on to the next era once all are researched; empty when it can't go on yet.</summary>
    public IReadOnlyList<string> Advance { get; init; } = Array.Empty<string>();

    /// <summary>What reaching the era gives without research.</summary>
    public Unlocks Unlocks { get; init; } = new();
}

/// <summary>One tech, from <c>data/techs.json</c>.</summary>
public sealed record Tech
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    /// <summary>The era it belongs to: researched with that era's points, once the team has reached it.</summary>
    public string Era { get; init; } = "";

    public int Points { get; init; }

    /// <summary>Items it costs as well, paid like crafting.</summary>
    public IReadOnlyDictionary<string, int> Costs { get; init; } = new Dictionary<string, int>();

    public IReadOnlyList<string> Requires { get; init; } = Array.Empty<string>();

    public Unlocks Unlocks { get; init; } = new();
}

/// <summary>
/// The eras and techs from <c>data/techs.json</c>, and the rules for them as plain functions of a
/// <see cref="TeamState"/>: what's unlocked, what a tech needs, what an action earns, and researching. The
/// server and clients share it, so a client can grey out what the server would refuse.
/// </summary>
public sealed record TechTree
{
    public IReadOnlyList<Era> Eras { get; init; } = Array.Empty<Era>();

    public IReadOnlyList<Tech> Techs { get; init; } = Array.Empty<Tech>();

    /// <summary>For each locked thing, what unlocks it: <c>tech:id</c> or <c>era:id</c>.</summary>
    private Dictionary<string, List<string>> _sources = new();

    public Tech? Tech(string id) => Techs.FirstOrDefault(t => t.Id == id);

    public int EraIndex(string id) => Eras.ToList().FindIndex(e => e.Id == id);

    public Era EraOf(TeamState team) => Eras[Math.Clamp(team.Era, 0, Eras.Count - 1)];

    /// <summary>Whether a team may use something: it's open to all, or the team has researched or reached what unlocks it.</summary>
    public bool IsUnlocked(TeamState team, string key) => !_sources.TryGetValue(key, out List<string>? sources) || sources.Any(s => Has(team, s));

    /// <summary>What unlocks something, for telling a player: "Mining research" or "the WWI era". Null if it's open.</summary>
    public string? UnlockedBy(string key)
    {
        if (!_sources.TryGetValue(key, out List<string>? sources))
        {
            return null;
        }

        string source = sources[0];
        string id = source[(source.IndexOf(':') + 1)..];
        string era = Eras[Math.Max(0, EraIndex(id))].Name;
        return source.StartsWith("tech:", StringComparison.Ordinal) ? $"{Tech(id)?.Name} research"
            : era.EndsWith(" Age", StringComparison.Ordinal) || era.EndsWith(" War", StringComparison.Ordinal) ? $"the {era}"
            : $"the {era} era";
    }

    /// <summary>How many of an item one mined block gives: its yields multiplied, from what the team has.</summary>
    public int Yield(TeamState team, string item)
    {
        int yield = 1;
        foreach ((string source, Unlocks unlocks) in AllUnlocks())
        {
            if (unlocks.Yield.TryGetValue(item, out int times) && Has(team, source))
            {
                yield *= times;
            }
        }

        return yield;
    }

    /// <summary>The points an action earns a team, by era: one entry per era it has reached that counts it.</summary>
    public IReadOnlyDictionary<string, int> Earned(TeamState team, string action, string what)
    {
        var earned = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i <= Math.Min(team.Era, Eras.Count - 1); i++)
        {
            if (Eras[i].Points.TryGetValue(action, out IReadOnlyDictionary<string, int>? sources) && sources.TryGetValue(what, out int points) && points > 0)
            {
                earned[Eras[i].Id] = points;
            }
        }

        return earned;
    }

    /// <summary>
    /// Why a team can't research a tech yet, or null if it can. Item costs aren't checked here: they're paid
    /// like crafting, from a player's backpack and the team's storage.
    /// </summary>
    public string? WhyNot(TeamState team, Tech tech)
    {
        int era = EraIndex(tech.Era);
        if (team.HasResearched(tech.Id))
        {
            return "already researched";
        }

        if (era > team.Era)
        {
            return $"needs the {Eras[era].Name}";
        }

        if (tech.Requires.FirstOrDefault(r => !team.HasResearched(r)) is { } missing)
        {
            return $"needs {Tech(missing)?.Name}";
        }

        int points = team.Points.Of(tech.Era);
        return points < tech.Points ? $"needs {tech.Points} {Eras[era].Name} points (has {points})" : null;
    }

    /// <summary>
    /// The team after researching a tech it may research: the points spent, the tech added, and the next era
    /// reached if that was the last of its era's advance techs.
    /// </summary>
    public TeamState Research(TeamState team, Tech tech)
    {
        TeamState after = team with
        {
            Points = team.Points.Plus(tech.Era, -tech.Points),
            Researched = team.Researched.Append(tech.Id).Distinct().OrderBy(t => t, StringComparer.Ordinal).ToList(),
        };
        Era current = EraOf(after);
        return current.Advance.Count > 0 && current.Advance.All(after.HasResearched) && after.Era < Eras.Count - 1
            ? after with { Era = after.Era + 1 }
            : after;
    }

    /// <summary>Plain words for what a tech unlocks, for the research window.</summary>
    public static string Describe(Unlocks unlocks, ItemSettings items, ObjectKinds kinds)
    {
        string item(string id) => items.Item(id)?.Name ?? id;
        var parts = new List<string>();
        parts.AddRange(unlocks.Recipes.Select(r => items.RecipeFor(r)?.Makes.Keys.FirstOrDefault() is { } made ? item(made) : r));
        parts.AddRange(unlocks.Structures.Select(s => kinds.Get(s)?.Name ?? s));
        parts.AddRange(unlocks.Smelting.Select(s => $"smelting {item(s)}"));
        parts.AddRange(unlocks.Places.Select(p => $"placing {item(p)}"));
        parts.AddRange(unlocks.Vehicles.Concat(unlocks.Weapons));
        parts.AddRange(unlocks.Yield.Select(y => $"{y.Value}x {item(y.Key)} from mining"));
        return string.Join(", ", parts);
    }

    private bool Has(TeamState team, string source)
    {
        string id = source[(source.IndexOf(':') + 1)..];
        return source.StartsWith("tech:", StringComparison.Ordinal) ? team.HasResearched(id) : EraIndex(id) <= team.Era;
    }

    private IEnumerable<(string Source, Unlocks Unlocks)> AllUnlocks() =>
        Eras.Select(e => ("era:" + e.Id, e.Unlocks)).Concat(Techs.Select(t => ("tech:" + t.Id, t.Unlocks)));

    /// <summary>Loads <c>techs.json</c> and checks every name in it against the items and structures.</summary>
    public static TechTree FromJson(string json, ItemSettings items, ObjectKinds kinds)
    {
        TechTree tree = DataJson.Parse<TechTree>(json, "techs.json");
        tree.Index(items, kinds);
        return tree;
    }

    private void Index(ItemSettings items, ObjectKinds kinds)
    {
        static InvalidDataException Bad(string message) => new($"techs.json: {message}");
        if (Eras.Count == 0 || Eras.Select(e => e.Id).Distinct().Count() != Eras.Count || Eras.Any(e => e.Id.Length == 0 || e.Name.Length == 0 || e.Minutes < 0))
        {
            throw Bad("needs at least one era, each with a unique id, a name and minutes of 0 or more.");
        }

        if (Techs.Select(t => t.Id).Distinct().Count() != Techs.Count)
        {
            throw Bad("every tech needs a unique id.");
        }

        foreach (Tech tech in Techs)
        {
            if (tech.Id.Length == 0 || tech.Name.Length == 0 || EraIndex(tech.Era) < 0 || tech.Points < 0
                || tech.Costs.Any(c => items.Item(c.Key) is null || c.Value <= 0)
                || tech.Requires.Any(r => Tech(r) is not { } required || EraIndex(required.Era) > EraIndex(tech.Era)))
            {
                throw Bad($"tech '{tech.Id}' needs a name, an era that exists, points of 0 or more, item costs that exist, and requirements from its own era or earlier.");
            }
        }

        foreach (Era era in Eras)
        {
            if (era.Advance.Any(a => Tech(a)?.Era != era.Id))
            {
                throw Bad($"era '{era.Id}' advances with a tech that isn't one of its own.");
            }

            foreach ((string action, IReadOnlyDictionary<string, int> sources) in era.Points)
            {
                bool known = action switch
                {
                    PointSource.Mine or PointSource.Place => sources.Keys.All(b => Enum.GetValues<Block>().Any(block => BlockNames.Of(block) == b)),
                    PointSource.Smelt => sources.Keys.All(i => items.Item(i) is not null),
                    _ => false,
                };
                if (!known)
                {
                    throw Bad($"era '{era.Id}': points for '{action}' must be mine or place (by block) or smelt (by item), naming things that exist.");
                }
            }
        }

        _sources = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach ((string source, Unlocks unlocks) in AllUnlocks())
        {
            bool known = unlocks.Recipes.All(r => items.RecipeFor(r) is not null)
                && unlocks.Structures.All(s => kinds.Get(s) is not null)
                && unlocks.Smelting.All(s => items.Smelting.Any(r => r.Makes == s))
                && unlocks.Places.All(p => items.Item(p)?.Places is not null)
                && unlocks.Vehicles.All(v => v == "plane")
                && unlocks.Weapons.All(w => w == "nuke")
                && unlocks.Yield.All(y => items.Item(y.Key) is not null && y.Value >= 1);
            if (!known)
            {
                throw Bad($"'{source}' unlocks something that doesn't exist (vehicles: plane; weapons: nuke).");
            }

            foreach (string key in unlocks.Keys)
            {
                if (!_sources.TryGetValue(key, out List<string>? list))
                {
                    _sources[key] = list = new List<string>();
                }

                list.Add(source);
            }
        }
    }
}
