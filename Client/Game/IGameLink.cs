using System.Collections.Generic;
using IslandWar.Sim.Match;
using IslandWar.Sim.Players;
using IslandWar.Sim.Voxels;
using IslandWar.Sim.Weapons;

namespace IslandWar.Client.Game;

/// <summary>
/// How a <see cref="ClientGame"/> talks to the server. The single-player sandbox has no link; the
/// networked client passes its connection.
/// </summary>
public interface IGameLink
{
    /// <summary>Sends the newest inputs, oldest first. Called once per tick.</summary>
    void SendInputs(IReadOnlyList<PlayerInput> inputs);

    /// <summary>Asks the server to make an edit the client has already applied as a prediction.</summary>
    void SubmitEdit(VoxelEdit edit);

    /// <summary>Other players to draw this frame, by peer id, already interpolated.</summary>
    IReadOnlyDictionary<int, RemoteState> SampleRemotePlayers();

    /// <summary>Every plane by id, already interpolated. The one the local player flies is drawn from prediction instead.</summary>
    IReadOnlyDictionary<int, RemotePlane> SampleRemotePlanes();

    /// <summary>Falling bombs (and nukes) by id, positioned for this frame.</summary>
    IReadOnlyDictionary<int, (BombState State, bool Nuke)> SampleBombs();

    /// <summary>Seconds until our team's nuke is ready: 0 when ready, negative outside phase 3.</summary>
    float NukeReadyIn { get; }

    /// <summary>Each island's core site, by island index.</summary>
    IReadOnlyList<CoreSite> Cores { get; }

    /// <summary>The server allows noclip flying (developer mode).</summary>
    bool DevMode { get; }

    /// <summary>Our team.</summary>
    Team MyTeam { get; }

    /// <summary>A player's name and team, or null if the roster hasn't caught up yet.</summary>
    RosterEntry? Player(int peer);

    /// <summary>Everyone connected, by peer id.</summary>
    IReadOnlyDictionary<int, RosterEntry> Roster { get; }

    /// <summary>The match as of the latest snapshot.</summary>
    MatchStatus Match { get; }

    /// <summary>The server's match preset.</summary>
    MatchPreset? Preset { get; }

    /// <summary>The newest input number sent; a fresh prediction carries on from it.</summary>
    uint LastSentSeq { get; }

    /// <summary>Sets or clears our vote for our team to concede.</summary>
    void SetConcedeVote(bool vote);
}
