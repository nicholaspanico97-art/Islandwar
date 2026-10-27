using System.Numerics;
using IslandWar.Sim.Match;
using IslandWar.Sim.Players;
using IslandWar.Sim.Research;
using IslandWar.Sim.Vehicles;

namespace IslandWar.Sim.Server;

/// <summary>The planes: one per team on its mooring, getting in and out, and flying on with nobody aboard.</summary>
public sealed class Vehicles
{
    private static readonly PlayerInput Idle = new(0, 0, 0, 0, 0, InputButtons.None);

    private readonly ServerState _state;
    private readonly Research _research;

    public Vehicles(ServerState state, Research research)
    {
        _state = state;
        _research = research;
    }

    /// <summary>One plane per team, parked off its own island. Ids start at 1; 0 means "no plane".</summary>
    public void ParkPlanes()
    {
        foreach (Team team in Teams.Playing)
        {
            var plane = new ServerPlane(_state.Planes.Count + 1, team, _state.Data.PlaneSpawnIn(_state.World, team));
            _state.Planes[plane.Id] = plane;
            _state.Log($"[server] {team} plane {plane.Id} parked at {plane.State.Position}");
        }
    }

    /// <summary>Every plane back on its mooring, empty and reloaded (a fresh match).</summary>
    public void ResetToMoorings()
    {
        foreach (ServerPlane plane in _state.Planes.Values)
        {
            plane.State = _state.Data.PlaneSpawnIn(_state.World, plane.Team);
            plane.PilotPeerId = 0;
            plane.LastDropTick = null;
        }
    }

    /// <summary>Gets a pilot out, or a player into the nearest free plane of their team in reach.</summary>
    public void ToggleSeat(ServerPlayer player)
    {
        GameRules rules = _state.Rules;
        if (player.Pawn.InPlane)
        {
            ServerPlane plane = _state.Planes[player.Pawn.PlaneId];
            Vector3 exit = VehicleRules.ExitPosition(_state.World, rules.Player, rules.Plane, plane.State);
            plane.PilotPeerId = 0;
            plane.State = plane.State with { Throttle = 0 };
            player.Pawn = PawnState.OnFoot(PlayerState.At(exit));
            _state.Log($"[server] peer {player.Peer} got out of plane {plane.Id}");
            return;
        }

        Vector3 feet = player.Pawn.Player.Position;
        ServerPlane? nearest = _state.Planes.Values
            .Where(p => p.PilotPeerId == 0 && p.Team == player.Team && VehicleRules.InEnterRange(rules.Plane, feet, p.State))
            .OrderBy(p => Vector3.DistanceSquared(feet, p.State.Position))
            .FirstOrDefault();
        if (nearest is null)
        {
            return;
        }

        if (!_research.Unlocked(player.Team, Unlock.Plane))
        {
            _state.Log($"[server] rejected peer {player.Peer} getting into plane {nearest.Id}: {_research.Needs(Unlock.Plane)}");
            return;
        }

        nearest.PilotPeerId = player.Peer;
        player.Pawn = new PawnState(PlayerState.At(nearest.State.Position), nearest.Id, nearest.State);
        _state.Log($"[server] peer {player.Peer} got into plane {nearest.Id}");
    }

    /// <summary>The pilot is gone: the plane glides down on its own.</summary>
    public void Abandon(int planeId)
    {
        ServerPlane plane = _state.Planes[planeId];
        plane.PilotPeerId = 0;
        plane.State = plane.State with { Throttle = 0 };
    }

    /// <summary>Planes nobody is flying still fall, coast and stop.</summary>
    public void StepEmpty()
    {
        foreach (ServerPlane plane in _state.Planes.Values.Where(p => p.PilotPeerId == 0))
        {
            plane.State = PlaneFlight.Step(_state.World, _state.Rules.Plane, _state.Rules.SeaLevel, plane.State, Idle, _state.TickSeconds);
        }
    }

    /// <summary>Something solid may not be put where a plane is.</summary>
    public bool AnyPlaneOverlaps(Voxels.BlockPos block) =>
        _state.Planes.Values.Any(p => BuildRules.OverlapsBox(p.State.Position, _state.Rules.Plane.Width, _state.Rules.Plane.Height, block));
}
