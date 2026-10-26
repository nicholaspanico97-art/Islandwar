using IslandWar.Sim.Match;

namespace IslandWar.Sim.Server;

/// <summary>
/// Who is connected: new connections, hellos (name, team and lasting identity), seats kept for players who
/// drop out mid-match, leaving, and dropping clients that never introduce themselves.
/// </summary>
public sealed class Sessions
{
    /// <summary>A client that connects but doesn't say hello within this many milliseconds is dropped.</summary>
    private const ulong HelloTimeoutMsec = 10_000;

    private readonly ServerState _state;
    private readonly Vehicles _vehicles;
    private readonly TeamStates _teams;

    /// <summary>Connected peers that haven't said hello yet, with when they connected.</summary>
    private readonly Dictionary<int, ulong> _pending = new();

    /// <summary>
    /// Seats of players who left during this match, by identity token, so they come back to the same team
    /// with the same name. Forgotten when the server returns to warmup.
    /// </summary>
    private readonly Dictionary<string, (string Name, Team Team)> _departed = new();

    /// <summary>Lasting player ids by identity token, for block ownership. Ids start at 1; 0 means nobody.</summary>
    private readonly Dictionary<string, int> _playerIds = new();
    private int _nextPlayerId = 1;

    public Sessions(ServerState state, Vehicles vehicles, TeamStates teams)
    {
        _state = state;
        _vehicles = vehicles;
        _teams = teams;
    }

    /// <summary>Someone left after saying hello; the match system checks whether that settles a concede vote.</summary>
    public event Action? PlayerLeft;

    public void Connected(int peer, ulong nowMsec) => _pending[peer] = nowMsec;

    /// <summary>
    /// A connected client introduced itself: give it a name and a team, and send it the world. A player who
    /// left during this match gets their old team and name back, whatever they ask for.
    /// </summary>
    public void Hello(int peer, string requestedName, Team requestedTeam, string token)
    {
        if (!_pending.Remove(peer))
        {
            return;
        }

        Dictionary<int, ServerPlayer> players = _state.Players;

        // A token already in use belongs to someone else (or another window on the same machine): this
        // joiner is a new player, with no claim on that player's seat or blocks.
        if (players.Values.Any(p => p.Token == token))
        {
            token = "";
        }

        (string Name, Team Team) seat = ("", Team.None);
        bool returning = token.Length > 0 && _departed.Remove(token, out seat);
        if (returning)
        {
            requestedName = seat.Name;
        }

        int red = _state.Count(Team.Red);
        int blue = _state.Count(Team.Blue);
        int max = _state.Preset.MaxPlayersPerTeam;
        if (red >= max && blue >= max)
        {
            Kick(peer, $"The server is full ({max} per team).");
            return;
        }

        // Developer mode lets the test bots line up on whichever side they need.
        Team team = returning ? seat.Team : Teams.Assign(red, blue, requestedTeam, _state.Options.Dev ? max : _state.Preset.MaxTeamImbalance);
        if ((team == Team.Red ? red : blue) >= max)
        {
            team = team.Other();
        }

        string name = PlayerNames.Clean(requestedName, players.Values.Select(p => p.Name));
        int playerId = token.Length > 0 && _playerIds.TryGetValue(token, out int known) ? known : _nextPlayerId++;
        if (token.Length > 0)
        {
            _playerIds[token] = playerId;
        }

        players[peer] = new ServerPlayer(peer, name, team, token, playerId, _state.NextSpawn(team), _state.Data.Net);
        _state.Net.SendWorld(peer, _state.CurrentWorld());
        _teams.SendTo(players[peer]);
        _state.BroadcastRoster();
        _state.Log($"[server] peer {peer} {(returning ? "came back" : "joined")} as '{name}' (player {playerId}) on {team}; sent the world with {_state.World.Deltas.All().Count()} edits and {_state.Objects.Count} objects");
    }

    public void Left(int peer)
    {
        _pending.Remove(peer);
        if (!_state.Players.Remove(peer, out ServerPlayer? player))
        {
            return;
        }

        if (player.Pawn.InPlane)
        {
            // A pilot who disconnects leaves the plane to glide down on its own.
            _vehicles.Abandon(player.Pawn.PlaneId);
        }

        // Keep their seat for this match in case they come back.
        if (player.Token.Length > 0 && _state.Match.Stage != MatchStage.Warmup)
        {
            _departed[player.Token] = (player.Name, player.Team);
        }

        _state.BroadcastRoster();
        _state.Log($"[server] '{player.Name}' (peer {peer}) left");
        PlayerLeft?.Invoke();
    }

    /// <summary>Back in warmup, nobody has a seat to come back to.</summary>
    public void ForgetDeparted() => _departed.Clear();

    /// <summary>Drops clients that connected but never said hello (an old version, or not this game at all).</summary>
    public void DropSilentPeers(ulong nowMsec)
    {
        foreach (int peer in _pending.Where(p => nowMsec - p.Value > HelloTimeoutMsec).Select(p => p.Key).ToList())
        {
            _pending.Remove(peer);
            _state.Log($"[server] peer {peer} never said hello; dropping it");
            _state.Net.Disconnect(peer);
        }
    }

    private void Kick(int peer, string reason)
    {
        _state.Log($"[server] kicking peer {peer}: {reason}");
        _state.Net.Kick(peer, reason);
    }
}
