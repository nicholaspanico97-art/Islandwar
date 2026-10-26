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

    /// <summary>Falling bombs by id, positioned for this frame.</summary>
    IReadOnlyDictionary<int, BombState> SampleBombs();

    /// <summary>The server allows noclip flying (developer mode).</summary>
    bool DevMode { get; }

    /// <summary>Our team.</summary>
    Team MyTeam { get; }

    /// <summary>A player's name and team, or null if the roster hasn't caught up yet.</summary>
    RosterEntry? Player(int peer);
}
