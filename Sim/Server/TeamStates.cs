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

    /// <summary>Every team starts from nothing (a fresh match). Players hear of it with the new world.</summary>
    public void Reset()
    {
        foreach (Team team in Teams.Playing)
        {
            _state.TeamStates[team] = new TeamState(team);
        }
    }
}
