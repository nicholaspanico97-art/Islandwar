using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using IslandWar.Net;
using IslandWar.Sim.Players;
using IslandWar.Sim.Vehicles;
using IslandWar.Sim.Voxels;
using IslandWar.Sim.Weapons;

namespace IslandWar.Client.Bots;

/// <summary>
/// The plane bot for the automated test. It joins Blue, flies (noclip) to Blue's parked plane, presses E,
/// takes off, crosses to Red's island and bombs the builder's tower, aiming with the same impact
/// prediction as the bombsight, then gets out. It predicts every tick like a real client. It passes if the bomb took
/// blocks off the tower, the server's corrections to its prediction stayed tiny, and its world matches
/// the server's.
/// </summary>
public partial class PilotBot : TestBot
{
    /// <summary>Largest position correction allowed, in blocks. Client and server run the same code, so it should be ~0.</summary>
    private const float MaxCorrection = 0.05f;

    private const float BoardingDistance = 2.5f;

    /// <summary>Speed above stall at which the bot pulls up, in blocks per second.</summary>
    private const float RotateMargin = 4;

    /// <summary>Ticks of full nose-up input for the take-off rotation.</summary>
    private const int RotateTicks = 20;

    /// <summary>Bombing height above the sea: clear of the highest hills.</summary>
    private const float CruiseAboveSea = 36;

    /// <summary>Drop when the predicted impact is this close to the tower, horizontally.</summary>
    private const float AimTolerance = 1.5f;

    /// <summary>Airspeed held during the attack: fast enough to fly, slow enough to turn onto the tower.</summary>
    private const float AttackSpeed = 28;

    /// <summary>The bomb run starts this far out, so the plane is lined up well before it drops.</summary>
    private const float RunInDistance = 110;

    private const int MaxAttempts = 4;

    private enum Phase { Waiting, Approach, Boarding, TakeOff, Rotate, Attack, Falling, Leaving }

    private PredictedPlayer _player = null!;
    private Phase _phase;
    private int _phaseTicks;
    private float _maxCorrection;
    private int _attempts;
    private int _towerBefore;
    private bool _inbound;

    public PawnState Spawn { get; init; }

    private PlaneSettings Plane => Net.Data!.Plane;

    private BombSettings Bomb => Net.Data!.Bomb;

    protected override void Start()
    {
        _player = new PredictedPlayer(Spawn);
        Net.OwnStateReceived += OnServerState;
        Net.Exploded += OnExplosion;
        Enter(Phase.Approach, "flying to the plane");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_phase == Phase.Waiting)
        {
            return;
        }

        _phaseTicks++;
        _player.Predict(World, Net.Data!.Rules, Decide(), Net.Data.Net.TickSeconds);
        Net.SendInputs(_player.Latest(Net.Data.Net.InputsPerPacket));
    }

    private void OnServerState(PawnState server, uint ack)
    {
        bool wasInPlane = _player.State.InPlane;
        float correction = _player.Reconcile(World, Net.Data!.Rules, server, ack, Net.Data.Net.TickSeconds);

        // Getting in or out moves the player by design; everything else should agree with the prediction.
        if (wasInPlane == _player.State.InPlane && correction > _maxCorrection)
        {
            _maxCorrection = correction;
        }
    }

    private void OnExplosion(Explosion explosion)
    {
        if (_phase != Phase.Falling)
        {
            return;
        }

        int now = TowerBlocks();
        Log($"bomb hit {(explosion.InWater ? "the sea" : $"({explosion.Position.X:0.0}, {explosion.Position.Y:0.0}, {explosion.Position.Z:0.0})")}; tower blocks {_towerBefore} -> {now}");
        if (now < _towerBefore)
        {
            Enter(Phase.Leaving, "tower hit; pressing E");
        }
        else if (++_attempts >= MaxAttempts)
        {
            Finish(false, $"missed the tower {MaxAttempts} times");
        }
        else
        {
            Enter(Phase.Attack, "missed; going round again");
        }
    }

    private PlayerInput Decide()
    {
        PawnState me = _player.State;
        switch (_phase)
        {
            case Phase.Approach:
                // Our team's plane; only that one lets us in.
                RemotePlane? ours = Net.SampleRemotePlanes().Values.Where(p => p.Team == Net.MyTeam).Cast<RemotePlane?>().FirstOrDefault();
                if (ours is not { } plane)
                {
                    return Input(InputButtons.Fly);
                }

                Vector3 toPlane = plane.Position + Vector3.UnitY - me.Player.Position;
                if (toPlane.Length() < BoardingDistance)
                {
                    Enter(Phase.Boarding, "at the plane; pressing E");
                    return Input(InputButtons.Use);
                }

                InputButtons vertical = toPlane.Y > 0.5f ? InputButtons.Jump : toPlane.Y < -0.5f ? InputButtons.Descend : InputButtons.None;
                return new PlayerInput(0, 0, -1, MathF.Atan2(-toPlane.X, -toPlane.Z), 0, InputButtons.Fly | vertical);

            case Phase.Boarding:
                if (me.InPlane)
                {
                    Enter(Phase.TakeOff, $"in plane {me.PlaneId}; full throttle");
                }

                return Input(InputButtons.None);

            case Phase.TakeOff:
                if (me.Plane.Airspeed >= Plane.StallSpeed + RotateMargin)
                {
                    Enter(Phase.Rotate, $"at {me.Plane.Airspeed:0.0} blocks/s; pulling up");
                }

                return Input(InputButtons.Sprint);

            case Phase.Rotate:
                if (_phaseTicks >= RotateTicks)
                {
                    Enter(Phase.Attack, me.Plane.OnGround ? "still on the ground after rotating" : "airborne; heading for the tower");
                }

                return Input(InputButtons.Sprint, pitch: 1);

            case Phase.Attack:
                return Attack(me.Plane);

            case Phase.Falling:
                return Autopilot(me.Plane, fire: false);

            case Phase.Leaving:
                if (!me.InPlane)
                {
                    _phase = Phase.Waiting;
                    Log($"got out at height {me.Player.Position.Y:0.0}; largest correction from the server {_maxCorrection:0.0000} blocks");
                    if (_maxCorrection > MaxCorrection)
                    {
                        Finish(false, $"the server corrected the prediction by {_maxCorrection:0.000} blocks (limit {MaxCorrection})");
                    }
                    else
                    {
                        CompareWithServer();
                    }

                    return Input(InputButtons.None);
                }

                return _phaseTicks == 1 ? Input(InputButtons.Use) : Input(InputButtons.None);

            default:
                return Input(InputButtons.None);
        }
    }

    /// <summary>Flies at the tower and drops when the bomb would land on it.</summary>
    private PlayerInput Attack(PlaneState plane)
    {
        Vector3 target = TowerTarget();
        BombImpact? impact = BombFlight.PredictImpact(World, Bomb, Net.Data!.Rules.SeaLevel, BombFlight.Drop(Bomb, plane), Net.Data.Net.TickSeconds, 600);
        bool onTarget = impact is { } hit && Horizontal(hit.Position - target).Length() < AimTolerance;
        if (_phaseTicks % 300 == 0)
        {
            string miss = impact is { } i ? $"{Horizontal(i.Position - target).Length():0.0}" : "none";
            Log($"attacking: at ({plane.Position.X:0}, {plane.Position.Y:0}, {plane.Position.Z:0}), heading {plane.Heading:0.00}, "
                + $"speed {plane.Airspeed:0.0}, {Horizontal(target - plane.Position).Length():0} blocks from the tower, bomb would miss by {miss}");
        }
        if (onTarget)
        {
            _towerBefore = TowerBlocks();
            Enter(Phase.Falling, $"bombs away at height {plane.Position.Y:0.0}, {Horizontal(target - plane.Position).Length():0.0} blocks short of the tower");
        }

        return Autopilot(plane, onTarget);
    }

    /// <summary>
    /// A bombing pattern: fly straight out until well clear of the tower, turn back, and run in lined up.
    /// Steering is by banking (a banked plane turns on its own), with the throttle holding a moderate speed
    /// so the turn is tight enough. Roughly proportional control on heading, bank, pitch and speed.
    /// </summary>
    private PlayerInput Autopilot(PlaneState plane, bool fire)
    {
        Vector3 toTarget = Horizontal(TowerTarget() - plane.Position);
        float distance = toTarget.Length();
        float headingError = MathF.IEEERemainder(MathF.Atan2(-toTarget.X, -toTarget.Z) - plane.Heading, 2 * MathF.PI);

        // Past the tower (or too close to turn onto it): head out again before the next run.
        if (_inbound && distance < RunInDistance / 2 && MathF.Abs(headingError) > 1.5f)
        {
            _inbound = false;
        }
        else if (!_inbound && distance >= RunInDistance)
        {
            _inbound = true;
        }

        // Bank is positive with the left wing down; rolling right (+X input) lowers it.
        float bank = MathF.Asin(Math.Clamp(Vector3.Transform(Vector3.UnitX, plane.Rotation).Y, -1, 1));
        float wantedBank = _inbound ? Math.Clamp(headingError * 1.5f, -0.85f, 0.85f) : 0;
        float roll = Math.Clamp((bank - wantedBank) * 3, -1, 1);

        float pitch = MathF.Asin(Math.Clamp(plane.Forward.Y, -1, 1));
        float wantedPitch = Math.Clamp((Net.Data!.Rules.SeaLevel + CruiseAboveSea - plane.Position.Y) * 0.04f, -0.25f, 0.3f);
        float pitchInput = Math.Clamp((wantedPitch - pitch) * 4, -1, 1);

        InputButtons throttle = plane.Airspeed < AttackSpeed - 1 ? InputButtons.Sprint
            : plane.Airspeed > AttackSpeed + 1 ? InputButtons.Descend
            : InputButtons.None;
        return new PlayerInput(0, roll, pitchInput, 0, 0, throttle | (fire ? InputButtons.Fire : InputButtons.None));
    }

    private Vector3 TowerTarget()
    {
        (int x, int z) = TowerColumn;
        return new Vector3(x + 0.5f, World.HighestSolid(x, z)!.Value + 1, z + 0.5f);
    }

    /// <summary>Planks left in the tower column, counted from the ground up.</summary>
    private int TowerBlocks()
    {
        (int x, int z) = TowerColumn;
        return Enumerable.Range(0, World.SizeY).Count(y => World.Get(new BlockPos(x, y, z)) == Block.Planks);
    }

    private void Enter(Phase phase, string message)
    {
        _phase = phase;
        _phaseTicks = 0;
        Log(message);
    }

    private static Vector3 Horizontal(Vector3 v) => v with { Y = 0 };

    private static PlayerInput Input(InputButtons buttons, float pitch = 0) => new(0, 0, pitch, 0, 0, buttons);
}
