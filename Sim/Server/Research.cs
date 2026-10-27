using IslandWar.Sim.Items;
using IslandWar.Sim.Match;
using IslandWar.Sim.Research;

namespace IslandWar.Sim.Server;

/// <summary>
/// Each team's research: points earned by what its players do in the era they've reached, techs bought with
/// them, and the eras those take the team into. Every other system asks here whether a team may use
/// something. Creative presets have everything unlocked and earn nothing.
/// </summary>
public sealed class Research
{
    private readonly ServerState _state;
    private readonly TeamStates _teams;
    private readonly Inventory _inventory;

    public Research(ServerState state, TeamStates teams, Inventory inventory)
    {
        _state = state;
        _teams = teams;
        _inventory = inventory;
    }

    private TechTree Tree => _state.Data.Techs;

    /// <summary>Whether a team may use something research can lock (an <see cref="Unlock"/> key).</summary>
    public bool Unlocked(Team team, string key) => _state.Creative || Tree.IsUnlocked(_teams.Of(team), key);

    /// <summary>The reason a locked thing is refused, for the log: "needs Mining research".</summary>
    public string Needs(string key) => $"needs {Tree.UnlockedBy(key)}";

    /// <summary>How many of an item one mined block gives the team.</summary>
    public int Yield(Team team, string item) => _state.Creative ? 1 : Tree.Yield(_teams.Of(team), item);

    /// <summary>Gives a team the points an action earns (mining a block, placing one, smelting an item), if any.</summary>
    /// <param name="times">How many times it happened at once (a furnace makes four bronze ingots in one go).</param>
    public void Earn(Team team, string action, string what, int times = 1)
    {
        if (_state.Creative || !Teams.Playing.Contains(team))
        {
            return;
        }

        IReadOnlyDictionary<string, int> earned = Tree.Earned(_teams.Of(team), action, what)
            .ToDictionary(e => e.Key, e => e.Value * times);
        if (earned.Count > 0)
        {
            _teams.Change(team, t => t with { Points = t.Points.Sum(earned) });
        }
    }

    /// <summary>A player asks to research a tech for their team, paying any items like crafting.</summary>
    public void ResearchRequested(int peer, string techId)
    {
        if (!_state.Players.TryGetValue(peer, out ServerPlayer? player))
        {
            return;
        }

        Tech? tech = Tree.Tech(techId);
        TeamState team = _teams.Of(player.Team);
        Payment? payment = tech is null ? null : _inventory.Plan(player, tech.Costs);
        string? reason = tech is null ? "unknown tech"
            : _state.Creative ? "everything is unlocked here"
            : Tree.WhyNot(team, tech) ?? (payment is null ? "can't afford the items" : null);
        if (reason is not null)
        {
            _state.Log($"[server] rejected peer {peer} researching {techId}: {reason}");
            return;
        }

        _inventory.Pay(player, payment!);
        TeamState after = Tree.Research(_teams.Of(player.Team), tech!, kind => HasBuilt(player.Team, kind));
        _teams.Change(player.Team, _ => after);
        _state.Log($"[server] '{player.Name}' researched {tech!.Name} for {player.Team}");
        if (after.Era > team.Era)
        {
            _state.Log($"[server] {player.Team} entered the {Tree.EraOf(after).Name}");
        }
    }

    /// <summary>A team finished a structure: if that was the last thing its era needed, it moves on.</summary>
    public void Built(Team team)
    {
        if (_state.Creative || !Teams.Playing.Contains(team))
        {
            return;
        }

        TeamState before = _teams.Of(team);
        TeamState after = Tree.Advanced(before, kind => HasBuilt(team, kind));
        if (after.Era > before.Era)
        {
            _teams.Change(team, _ => after);
            _state.Log($"[server] {team} entered the {Tree.EraOf(after).Name}");
        }
    }

    /// <summary>Whether a team has a finished structure of a kind standing.</summary>
    private bool HasBuilt(Team team, string kind) => _state.Objects.All.Any(o => o.Team == team && o.Kind == kind && !o.IsBlueprint());
}
