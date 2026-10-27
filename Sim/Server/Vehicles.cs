using System.Numerics;
using IslandWar.Sim.Match;
using IslandWar.Sim.Players;
using IslandWar.Sim.Research;
using IslandWar.Sim.Vehicles;

namespace IslandWar.Sim.Server;

/// <summary>
/// The vehicles: one plane per team on its mooring, ships launched from docks, getting in and out, and
/// moving on with nobody aboard. Ships go through the same plane state and snapshots (see <see cref="ShipSailing"/>).
/// </summary>
public sealed class Vehicles
{
    private static readonly PlayerInput Idle = new(0, 0, 0, 0, 0, InputButtons.None);

    /// <summary>How far round a ship to look for somewhere dry to step off.</summary>
    private const int LandingSearch = 4;

    private readonly ServerState _state;
    private readonly Research _research;

    /// <summary>Vehicle ids are never reused, even across matches.</summary>
    private int _nextId = 1;

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
            var plane = new ServerPlane(_nextId++, team, _state.Data.PlaneSpawnIn(_state.World, team));
            _state.Planes[plane.Id] = plane;
            _state.Log($"[server] {team} plane {plane.Id} parked at {plane.State.Position}");
        }
    }

    /// <summary>A new ship for a team, floating where its dock launched it.</summary>
    public ServerPlane Launch(Team team, ShipKind ship, PlaneState at)
    {
        var launched = new ServerPlane(_nextId++, team, at with { Ship = ship.Id });
        _state.Planes[launched.Id] = launched;
        _state.Log($"[server] {team} launched a {ship.Name} (vehicle {launched.Id}) at {at.Position}");
        return launched;
    }

    /// <summary>Every plane back on its mooring, empty and reloaded, and no ships (a fresh match: the docks are gone too).</summary>
    public void ResetToMoorings()
    {
        foreach (int ship in _state.Planes.Values.Where(p => p.State.IsShip).Select(p => p.Id).ToList())
        {
            _state.Planes.Remove(ship);
        }

        foreach (ServerPlane plane in _state.Planes.Values)
        {
            plane.State = _state.Data.PlaneSpawnIn(_state.World, plane.Team);
            plane.PilotPeerId = 0;
            plane.LastDropTick = null;
        }
    }

    /// <summary>Gets a pilot or captain out, or a player into the nearest free vehicle of their team in reach.</summary>
    public void ToggleSeat(ServerPlayer player)
    {
        GameRules rules = _state.Rules;
        if (player.Pawn.InPlane)
        {
            ServerPlane plane = _state.Planes[player.Pawn.PlaneId];
            Vector3 exit = plane.State.IsShip
                ? VehicleRules.ShipExitPosition(_state.World, rules.Player, rules.Ship(plane.State.Ship!), plane.State, LandingSearch)
                : VehicleRules.ExitPosition(_state.World, rules.Player, rules.Plane, plane.State);
            plane.PilotPeerId = 0;
            plane.State = plane.State with { Throttle = 0 };
            player.Pawn = PawnState.OnFoot(PlayerState.At(exit));
            _state.Log($"[server] peer {player.Peer} got out of {(plane.State.IsShip ? "ship" : "plane")} {plane.Id}");
            return;
        }

        Vector3 feet = player.Pawn.Player.Position;
        ServerPlane? nearest = _state.Planes.Values
            .Where(p => p.PilotPeerId == 0 && p.Team == player.Team && VehicleRules.InEnterRange(rules, feet, p.State))
            .OrderBy(p => Vector3.DistanceSquared(feet, p.State.Position))
            .FirstOrDefault();
        if (nearest is null)
        {
            return;
        }

        if (!nearest.State.IsShip && !_research.Unlocked(player.Team, Unlock.Plane))
        {
            _state.Log($"[server] rejected peer {player.Peer} getting into plane {nearest.Id}: {_research.Needs(Unlock.Plane)}");
            return;
        }

        nearest.PilotPeerId = player.Peer;
        player.Pawn = new PawnState(PlayerState.At(nearest.State.Position), nearest.Id, nearest.State);
        _state.Log($"[server] peer {player.Peer} got into {(nearest.State.IsShip ? "ship" : "plane")} {nearest.Id}");
    }

    /// <summary>The pilot is gone: the plane glides down on its own.</summary>
    public void Abandon(int planeId)
    {
        ServerPlane plane = _state.Planes[planeId];
        plane.PilotPeerId = 0;
        plane.State = plane.State with { Throttle = 0 };
    }

    /// <summary>Vehicles nobody is at the controls of still fall, coast and stop.</summary>
    public void StepEmpty()
    {
        foreach (ServerPlane plane in _state.Planes.Values.Where(p => p.PilotPeerId == 0))
        {
            plane.State = plane.State.IsShip
                ? ShipSailing.Step(_state.World, _state.Rules.Ship(plane.State.Ship!), _state.Rules.SeaLevel, plane.State, Idle, _state.TickSeconds)
                : PlaneFlight.Step(_state.World, _state.Rules.Plane, _state.Rules.SeaLevel, plane.State, Idle, _state.TickSeconds);
        }
    }

    /// <summary>Something solid may not be put where a vehicle is.</summary>
    public bool AnyPlaneOverlaps(Voxels.BlockPos block) =>
        _state.Planes.Values.Any(p =>
        {
            (float width, float height) = p.State.IsShip
                ? (_state.Rules.Ship(p.State.Ship!).Width, _state.Rules.Ship(p.State.Ship!).Height)
                : (_state.Rules.Plane.Width, _state.Rules.Plane.Height);
            return BuildRules.OverlapsBox(p.State.Position, width, height, block);
        });
}
