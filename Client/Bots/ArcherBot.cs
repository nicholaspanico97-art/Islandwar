using System;
using System.Linq;
using System.Numerics;
using IslandWar.Sim.Match;
using IslandWar.Sim.Players;
using IslandWar.Sim.Weapons;

namespace IslandWar.Client.Bots;

/// <summary>
/// Combat bots for the automated match test (see docs/testing.md), on the practice preset in developer mode
/// (creative, so the bow needs no items; no match, so nothing resets):
/// <list type="bullet">
/// <item><b>archer</b> (Red) flies with noclip to hover a few blocks from Blue's player, then shoots at it
/// until it dies, and waits for it to come back.</item>
/// <item><b>target</b> (Blue) stands at its spawn, and checks it dies and then respawns at full health.</item>
/// </list>
/// Each then compares its game hash with the server's.
/// </summary>
public partial class ArcherBot : TestBot
{
    /// <summary>Where the archer hovers, from the target's feet: up and to the south.</summary>
    private static readonly Vector3 HoverOffset = new(0, 4, 8);

    private PredictedPlayer? _player;
    private int? _targetPeer;
    private bool _hovering;
    private bool _targetDied;
    private double _nextShot;
    private double _clock;

    public PawnState Spawn { get; init; }

    public override Vector3? Eye => _player?.State.Player.Position + Vector3.UnitY * Net.Data!.Player.EyeHeight;

    protected override void Start()
    {
        if (Role == "target")
        {
            Log($"on {Net.MyTeam}; standing still");
            WaitUntil(() => Net.RespawnIn >= 0, () =>
            {
                Log($"died (health {Net.Health}); back in {Net.RespawnIn:0.0} s");
                WaitUntil(() => Net.RespawnIn < 0 && Net.Health == Net.Data!.Player.MaxHealth, () =>
                {
                    Log($"respawned with {Net.Health} health");
                    CompareWithServer();
                });
            });
            return;
        }

        _player = new PredictedPlayer(Spawn);
        Net.OwnStateReceived += (server, ack) => _player.Reconcile(World, Net.Data!.Rules, server, ack, Net.Data.Net.TickSeconds);
        Log($"on {Net.MyTeam}; flying to the other team's player");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_player is null)
        {
            return;
        }

        _clock += delta;
        _player.Predict(World, Net.Data!.Rules, Decide(), Net.Data.Net.TickSeconds);
        Net.SendInputs(_player.Latest(Net.Data.Net.InputsPerPacket));
    }

    private PlayerInput Decide()
    {
        _targetPeer ??= Net.Roster.Values.Where(p => p.Team == Net.MyTeam.Other()).Select(p => (int?)p.PeerId).FirstOrDefault();
        Vector3 me = _player!.State.Player.Position;
        if (_targetPeer is not { } peer || !Net.SampleRemotePlayers().TryGetValue(peer, out RemoteState target))
        {
            // Not in sight: before the first death, still looking; after it, dead until it respawns.
            if (_targetDied || !_hovering)
            {
                return Hover();
            }

            _targetDied = true;
            Log($"the target is down after {Net.MyArrowHits} hits; waiting for it to come back");
            return Hover();
        }

        if (_targetDied)
        {
            _targetDied = false;
            Log("the target is back");
            CompareWithServer();
            return Hover();
        }

        Vector3 toHover = target.Position + HoverOffset - me;
        if (!_hovering && toHover.Length() > 1)
        {
            InputButtons vertical = toHover.Y > 0.5f ? InputButtons.Jump : toHover.Y < -0.5f ? InputButtons.Descend : InputButtons.None;
            return new PlayerInput(0, 0, -1, MathF.Atan2(-toHover.X, -toHover.Z), 0, InputButtons.Fly | vertical);
        }

        if (!_hovering)
        {
            _hovering = true;
            Log("in range; shooting");
        }

        // Aim at the chest, a little high for the drop over the distance.
        Vector3 eye = me + Vector3.UnitY * Net.Data!.Player.EyeHeight;
        Vector3 aim = target.Position + Vector3.UnitY - eye;
        float flat = MathF.Sqrt(aim.X * aim.X + aim.Z * aim.Z);
        ArrowSettings arrow = Net.Data.Arrow;
        float drop = 0.5f * arrow.Gravity * MathF.Pow(aim.Length() / arrow.Speed, 2);
        float yaw = MathF.Atan2(-aim.X, -aim.Z);
        float pitch = MathF.Atan2(aim.Y + drop, flat);
        bool fire = _clock >= _nextShot;
        if (fire)
        {
            _nextShot = _clock + arrow.DrawSeconds + 0.2;
        }

        return new PlayerInput(0, 0, 0, yaw, pitch, InputButtons.Fly | (fire ? InputButtons.Fire : InputButtons.None));
    }

    private static PlayerInput Hover() => new(0, 0, 0, 0, 0, InputButtons.Fly);
}
