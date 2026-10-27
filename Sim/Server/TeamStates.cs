using IslandWar.Sim.Items;
using IslandWar.Sim.Match;
using IslandWar.Sim.Protocol;

namespace IslandWar.Sim.Server;

/// <summary>Each team's shared state: kept here, changed through <see cref="Change"/>, and sent only to its own players.</summary>
public sealed class TeamStates
{
    private readonly ServerState _state;

    public TeamStates(ServerState state)
    {
        _state = state;
    }

    public TeamState Of(Team team) => _state.TeamStates[team];

    /// <summary>Replaces a team's state and tells its players.</summary>
    public void Change(Team team, Func<TeamState, TeamState> change)
    {
        _state.TeamStates[team] = change(Of(team));
        foreach (ServerPlayer player in _state.Players.Values.Where(p => p.Team == team))
        {
            SendTo(player);
        }
    }

    /// <summary>A player's own team's state, for a joiner or after a reset.</summary>
    public void SendTo(ServerPlayer player) =>
        _state.Net.SendTeamState(player.Peer, WireFormat.EncodeTeamState(Of(player.Team)));

    /// <summary>
    /// Every team starts from nothing (a fresh match), or from a playtest preset's techs and points. Players
    /// hear of it with the new world.
    /// </summary>
    public void Reset()
    {
        MatchPreset preset = _state.Preset;
        foreach (Team team in Teams.Playing)
        {
            var start = new TeamState(team)
            {
                Researched = preset.StartResearch.Distinct().Order(StringComparer.Ordinal).ToList(),
                Points = preset.StartPoints.Aggregate(ItemCounts.None, (points, p) => points.Plus(p.Key, p.Value)),
            };
            for (TeamState next = _state.Data.Techs.Advanced(start); next.Era > start.Era; next = _state.Data.Techs.Advanced(start))
            {
                start = next;
            }

            _state.TeamStates[team] = start;
        }
    }
}
