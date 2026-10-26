using IslandWar.Sim.Match;

namespace IslandWar.Sim.Server;

/// <summary>The match clock and how a match can end early: phases, the start and end, and concede votes.</summary>
public sealed class MatchSystem
{
    private readonly ServerState _state;

    public MatchSystem(ServerState state)
    {
        _state = state;
    }

    /// <summary>A match just started: the server resets the world for a fair start.</summary>
    public event Action? Started;

    /// <summary>The server just went back to warmup.</summary>
    public event Action? BackToWarmup;

    /// <summary>Moves the match clock on and acts on what changed.</summary>
    public void Advance()
    {
        (MatchStatus match, MatchEvent change) = MatchFlow.Advance(
            _state.Match, _state.Preset, _state.TickSeconds, _state.Count(Team.Red), _state.Count(Team.Blue));
        _state.Match = match;
        switch (change)
        {
            case MatchEvent.CountdownStarted:
                _state.Log($"[server] both teams ready: the match starts in {_state.Preset.CountdownSeconds:0} s");
                break;
            case MatchEvent.CountdownCancelled:
                _state.Log("[server] a team dropped below the minimum: back to warmup");
                break;
            case MatchEvent.MatchStarted:
                Started?.Invoke();
                ClearConcedeVotes();
                _state.Log("[server] match started: phase 1");
                break;
            case MatchEvent.PhaseChanged:
                _state.Log($"[server] phase {match.Phase}");
                break;
            case MatchEvent.MatchEnded:
                _state.Log($"[server] match over: {(match.Winner == Team.None ? "draw" : $"{match.Winner} wins")} ({match.Reason})");
                break;
            case MatchEvent.BackToWarmup:
                BackToWarmup?.Invoke();
                ClearConcedeVotes();
                _state.Log("[server] back to warmup");
                break;
        }
    }

    public void ConcedeVote(int peer, bool vote)
    {
        if (!_state.Players.TryGetValue(peer, out ServerPlayer? player) || player.ConcedeVote == vote || (vote && !_state.Match.Playing))
        {
            return;
        }

        player.ConcedeVote = vote;
        _state.Log($"[server] '{player.Name}' {(vote ? "voted" : "withdrew their vote")} to concede for {player.Team}");
        _state.BroadcastRoster();
        CheckConcede();
    }

    /// <summary>A team that has enough concede votes loses on the spot (also checked when someone leaves).</summary>
    public void CheckConcede()
    {
        if (!_state.Match.Playing)
        {
            return;
        }

        foreach (Team team in Teams.Playing)
        {
            ServerPlayer[] members = _state.Players.Values.Where(p => p.Team == team).ToArray();
            if (MatchScore.Concedes(members.Count(p => p.ConcedeVote), members.Length, _state.Preset.ConcedeFraction))
            {
                _state.Match = MatchFlow.End(_state.Match, team.Other(), EndReason.Concede);
                _state.Log($"[server] {team} conceded: {team.Other()} wins");
                return;
            }
        }
    }

    private void ClearConcedeVotes()
    {
        foreach (ServerPlayer player in _state.Players.Values)
        {
            player.ConcedeVote = false;
        }

        _state.BroadcastRoster();
    }
}
