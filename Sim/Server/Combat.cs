using System.Numerics;
using IslandWar.Sim.Items;
using IslandWar.Sim.Match;
using IslandWar.Sim.Players;
using IslandWar.Sim.Protocol;
using IslandWar.Sim.Voxels;
using IslandWar.Sim.Weapons;

namespace IslandWar.Sim.Server;

/// <summary>An arrow the server is flying.</summary>
public sealed class ServerArrow(int id, int peer, Team team, uint spawnTick, ArrowState state)
{
    public int Id { get; } = id;

    public int Peer { get; } = peer;

    public Team Team { get; } = team;

    public uint SpawnTick { get; } = spawnTick;

    public ArrowState State { get; set; } = state;
}

/// <summary>
/// Players getting hurt: health, dying (what they carried is left in a dropped backpack), coming back at
/// their island, and bows and arrows. Arrows hit players of the other team only and do no block damage.
/// </summary>
public sealed class Combat
{
    private readonly ServerState _state;
    private readonly Structures _structures;
    private readonly Inventory _inventory;
    private readonly List<ServerArrow> _arrows = new();
    private int _nextArrowId = 1;

    public Combat(ServerState state, Structures structures, Inventory inventory)
    {
        _state = state;
        _structures = structures;
        _inventory = inventory;
    }

    public IReadOnlyList<ServerArrow> Arrows => _arrows;

    private ArrowSettings Arrow => _state.Data.Arrow;

    /// <summary>
    /// Shoots an arrow the way the player looks, if they're alive on foot with a bow and an arrow (creative:
    /// no items needed) and the bow has been drawn again since the last shot.
    /// </summary>
    public void TryShoot(ServerPlayer player, float yaw, float pitch)
    {
        bool armed = _state.Creative || (player.Backpack.Of("bow") > 0 && player.Backpack.Of("arrow") > 0);
        if (player.Dead || player.Pawn.InPlane || !armed || _state.Seconds - player.LastShotSeconds < Arrow.DrawSeconds - _state.Data.Items.MiningSlackSeconds)
        {
            return;
        }

        player.LastShotSeconds = _state.Seconds;
        if (!_state.Creative)
        {
            _inventory.Take(player, "arrow", 1);
        }

        Vector3 eye = player.Pawn.Player.Position + Vector3.UnitY * _state.Data.Player.EyeHeight;
        var arrow = new ServerArrow(_nextArrowId++, player.Peer, player.Team, _state.Tick, ArrowFlight.Shoot(Arrow, eye, yaw, pitch));
        _arrows.Add(arrow);
        _state.Net.BroadcastArrowShot(new ArrowShot(arrow.Id, arrow.Peer, arrow.SpawnTick, arrow.State));
    }

    /// <summary>Moves every arrow one tick: into an enemy player (who's hurt), a block or the sea (where it stops), or on.</summary>
    public void FlyArrows()
    {
        uint maxTicks = (uint)(Arrow.MaxLifetimeSeconds * _state.Data.Net.TickRate);
        PlayerSettings body = _state.Data.Player;
        for (int i = _arrows.Count - 1; i >= 0; i--)
        {
            ServerArrow arrow = _arrows[i];

            // An arrow starts flying the tick after it's shot; clients count ticks the same way.
            if (arrow.SpawnTick == _state.Tick)
            {
                continue;
            }

            (ArrowState next, Vector3? landed) = ArrowFlight.Step(_state.World, Arrow, _state.Rules.SeaLevel, arrow.State, _state.TickSeconds);
            Vector3 from = arrow.State.Position;
            Vector3 to = landed ?? next.Position;
            ServerPlayer? struck = null;
            float at = float.MaxValue;
            foreach (ServerPlayer target in _state.Players.Values.Where(p => p.Team != arrow.Team && !p.Dead && !p.Pawn.InPlane))
            {
                if (ArrowFlight.HitsPlayer(from, to, target.Pawn.Player.Position, body.Width, body.Height) is { } t && t < at)
                {
                    (struck, at) = (target, t);
                }
            }

            if (struck is not null)
            {
                Land(i, Vector3.Lerp(from, to, at), struck.Peer);
                Damage(struck, Arrow.Damage, $"an arrow from peer {arrow.Peer}");
            }
            else if (landed is { } stop)
            {
                Land(i, stop, 0);
            }
            else if (_state.Tick - arrow.SpawnTick > maxTicks)
            {
                Land(i, next.Position, 0);
            }
            else
            {
                arrow.State = next;
            }
        }
    }

    /// <summary>Takes health from a living player; at 0 they die.</summary>
    public void Damage(ServerPlayer victim, int amount, string cause)
    {
        if (victim.Dead || amount <= 0)
        {
            return;
        }

        victim.Health = Math.Max(0, victim.Health - amount);
        _state.Log($"[server] '{victim.Name}' took {amount} damage from {cause}: {victim.Health} health left");
        if (victim.Health == 0)
        {
            Die(victim);
        }
    }

    /// <summary>Brings back dead players whose wait is over, at their island with full health.</summary>
    public void StepRespawns()
    {
        foreach (ServerPlayer player in _state.Players.Values.Where(p => p.DeadUntil is { } until && _state.Seconds >= until))
        {
            player.Respawn(_state.NextSpawn(player.Team));
            player.Health = _state.Data.Player.MaxHealth;
            player.DeadUntil = null;
            _state.Log($"[server] '{player.Name}' respawned at {player.Pawn.Player.Position}");
        }
    }

    /// <summary>No arrows in the air and everyone alive at full health (a fresh match).</summary>
    public void Reset()
    {
        _arrows.Clear();
        foreach (ServerPlayer player in _state.Players.Values)
        {
            player.Health = _state.Data.Player.MaxHealth;
            player.DeadUntil = null;
        }
    }

    private void Die(ServerPlayer victim)
    {
        victim.DeadUntil = _state.Seconds + _state.Data.Player.RespawnSeconds;
        string dropped = "nothing to drop";
        if (victim.Backpack.Count > 0)
        {
            Vector3 feet = victim.Pawn.Player.Position;
            var cell = new BlockPos((int)MathF.Floor(feet.X), (int)MathF.Floor(feet.Y), (int)MathF.Floor(feet.Z));
            dropped = _structures.DropLoot(cell, victim, victim.Backpack) is { } bag ? $"dropped backpack {bag.Id} at {bag.Origin}" : "nowhere to drop it: lost";
            victim.Backpack = ItemCounts.None;
            _inventory.SendBackpack(victim);
        }

        _state.Log($"[server] '{victim.Name}' died ({dropped}); back in {_state.Data.Player.RespawnSeconds:0} s");
    }

    private void Land(int index, Vector3 position, int hitPeer)
    {
        ServerArrow arrow = _arrows[index];
        _arrows.RemoveAt(index);
        _state.Net.BroadcastArrowLanded(new ArrowLanded(arrow.Id, position, hitPeer));
    }
}
