using IslandWar.Sim.Match;
using IslandWar.Sim.Protocol;
using IslandWar.Sim.Voxels;
using IslandWar.Sim.Weapons;

namespace IslandWar.Sim.Server;

/// <summary>Bombs and the nuke: dropping them, flying them, blasting craters, and a nuke ending the match.</summary>
public sealed class Weapons
{
    private readonly ServerState _state;

    /// <summary>When each team last dropped its nuke, on the match clock; null for not yet this match.</summary>
    private readonly Dictionary<Team, float?> _lastNukeDrop = new() { [Team.Red] = null, [Team.Blue] = null };
    private int _nextBombId = 1;

    public Weapons(ServerState state)
    {
        _state = state;
    }

    /// <summary>Seconds until a team's nuke is ready: 0 when ready, negative outside phase 3.</summary>
    public float NukeReadyIn(Team team) => MatchFlow.NukeReadyIn(_state.Match, _state.Preset, _lastNukeDrop[team]);

    /// <summary>No bombs in the air and every nuke unused (a fresh match).</summary>
    public void Reset()
    {
        _state.Bombs.Clear();
        foreach (Team team in Teams.Playing)
        {
            _lastNukeDrop[team] = null;
        }
    }

    /// <summary>Drops a bomb if the phase allows bombs, the plane is airborne and its bomb bay has reloaded.</summary>
    public void TryDropBomb(ServerPlayer pilot, ServerPlane plane)
    {
        uint cooldown = (uint)Math.Ceiling(_state.Rules.Bomb.CooldownSeconds * _state.Data.Net.TickRate);
        int radius = MatchFlow.CraterRadius(_state.Match, _state.Preset, _state.Rules.Bomb.CraterRadius);
        if (radius == 0 || plane.State.OnGround || (plane.LastDropTick is { } last && _state.Tick - last < cooldown))
        {
            return;
        }

        plane.LastDropTick = _state.Tick;
        Drop(pilot.Peer, plane, radius, nuke: false);
    }

    /// <summary>Drops the team's nuke if it's phase 3, the team has one ready, and the plane is airborne.</summary>
    public void TryDropNuke(ServerPlayer pilot, ServerPlane plane)
    {
        if (plane.State.OnGround || NukeReadyIn(plane.Team) != 0)
        {
            return;
        }

        _lastNukeDrop[plane.Team] = _state.Match.StageSeconds;
        Drop(pilot.Peer, plane, _state.Rules.Bomb.NukeCraterRadius, nuke: true);
    }

    /// <summary>Moves every falling bomb one tick and explodes the ones that hit something.</summary>
    public void FlyBombs()
    {
        List<ServerBomb> bombs = _state.Bombs;
        uint maxTicks = (uint)(_state.Rules.Bomb.MaxLifetimeSeconds * _state.Data.Net.TickRate);
        for (int i = bombs.Count - 1; i >= 0; i--)
        {
            ServerBomb bomb = bombs[i];

            // A bomb starts falling the tick after its drop; clients count ticks the same way.
            if (bomb.SpawnTick == _state.Tick)
            {
                continue;
            }

            (BombState next, BombImpact? impact) = BombFlight.Step(_state.World, _state.Rules.Bomb, _state.Rules.SeaLevel, bomb.State, _state.TickSeconds);
            bomb.State = next;
            if (impact is { } hit)
            {
                Explode(bomb, hit);
                bombs.RemoveAt(i);
            }
            else if (_state.Tick - bomb.SpawnTick > maxTicks)
            {
                bombs.RemoveAt(i);
            }
        }
    }

    private void Drop(int peer, ServerPlane plane, int craterRadius, bool nuke)
    {
        var bomb = new ServerBomb(_nextBombId++, peer, _state.Tick, BombFlight.Drop(_state.Rules.Bomb, plane.State), craterRadius, plane.Team, nuke);
        _state.Bombs.Add(bomb);
        _state.Net.BroadcastBombDrop(new BombDrop(bomb.Id, peer, bomb.SpawnTick, bomb.State, nuke));
        _state.Log($"[server] peer {peer} ({plane.Team}) dropped {(nuke ? "THE NUKE" : "bomb")} {bomb.Id} at {bomb.State.Position}");
    }

    private void Explode(ServerBomb bomb, BombImpact hit)
    {
        // No friendly fire: a team's bombs make no crater on its own half of the map.
        VoxelWorld world = _state.World;
        bool harmless = !hit.InWater && Protection.IsHomeGround(bomb.Team, hit.Position.X, world.SizeX, _state.Data.Terrain.IslandCount);
        IReadOnlyList<VoxelEdit> crater = harmless ? Array.Empty<VoxelEdit>() : BombFlight.Explode(world, bomb.CraterRadius, hit);
        _state.Net.BroadcastExplosion(new Explosion(bomb.Id, hit.Position, hit.InWater, WireFormat.EncodeEdits(crater.ToList()), bomb.CraterRadius, bomb.Nuke, harmless));
        string what = bomb.Nuke ? "the nuke" : $"bomb {bomb.Id}";
        _state.Log(hit.InWater ? $"[server] {what} splashed into the sea"
            : harmless ? $"[server] {what} landed on {bomb.Team}'s own half: no crater (no friendly fire)"
            : $"[server] {what} hit {hit.Block}: crater of {crater.Count} blocks");
        if (bomb.Nuke && !harmless)
        {
            CheckNukeHit(bomb, hit);
        }
    }

    /// <summary>A nuke that lands anywhere on the enemy island wins the match for the team that dropped it.</summary>
    private void CheckNukeHit(ServerBomb nuke, BombImpact hit)
    {
        Team enemy = nuke.Team.Other();
        if (!_state.Match.Playing || !MatchFlow.NukeWins(nuke.Team, hit, _state.World.SizeX, _state.Data.Terrain.IslandCount))
        {
            _state.Log($"[server] {nuke.Team}'s nuke missed the {enemy} island");
            return;
        }

        _state.Match = MatchFlow.End(_state.Match, nuke.Team, EndReason.NukeHit);
        _state.Log($"[server] {nuke.Team}'s nuke hit the {enemy} island: {nuke.Team} wins");
    }
}
