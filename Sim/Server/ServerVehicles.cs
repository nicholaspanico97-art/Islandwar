using IslandWar.Sim.Match;
using IslandWar.Sim.Vehicles;
using IslandWar.Sim.Weapons;

namespace IslandWar.Sim.Server;

/// <summary>A plane as the server sees it.</summary>
public sealed class ServerPlane
{
    public ServerPlane(int id, Team team, PlaneState state)
    {
        Id = id;
        Team = team;
        State = state;
    }

    public int Id { get; }

    /// <summary>Only this team's players can fly it.</summary>
    public Team Team { get; }

    public PlaneState State { get; set; }

    /// <summary>The peer flying it, or 0.</summary>
    public int PilotPeerId { get; set; }

    /// <summary>When this plane last dropped a bomb, for the reload time.</summary>
    public uint? LastDropTick { get; set; }
}

/// <summary>A bomb (or the nuke) between its drop and its impact.</summary>
public sealed class ServerBomb
{
    public ServerBomb(int id, int ownerPeerId, uint spawnTick, BombState state, int craterRadius, Team team, bool nuke)
    {
        Id = id;
        OwnerPeerId = ownerPeerId;
        SpawnTick = spawnTick;
        State = state;
        CraterRadius = craterRadius;
        Team = team;
        Nuke = nuke;
    }

    /// <summary>The team whose plane dropped it.</summary>
    public Team Team { get; }

    public bool Nuke { get; }

    /// <summary>Set by the match phase when the bomb was dropped.</summary>
    public int CraterRadius { get; }

    public int Id { get; }

    public int OwnerPeerId { get; }

    public uint SpawnTick { get; }

    public BombState State { get; set; }
}
