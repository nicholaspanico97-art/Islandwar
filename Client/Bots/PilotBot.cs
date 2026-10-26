using System;
using System.Collections.Generic;
using System.Numerics;
using IslandWar.Sim.Players;
using IslandWar.Sim.Vehicles;

namespace IslandWar.Client.Bots;

/// <summary>
/// The plane bot for the automated test. It flies (noclip) to the parked plane, presses E, takes off,
/// climbs, and presses E again to get out, predicting every tick like a real client. It passes if the
/// server's corrections to its prediction stayed tiny the whole time and its world matches the server's.
/// </summary>
public partial class PilotBot : TestBot
{
    /// <summary>Largest position correction allowed, in blocks. Client and server run the same code, so it should be ~0.</summary>
    private const float MaxCorrection = 0.05f;

    private const float ClimbHeight = 15;
    private const float BoardingDistance = 2.5f;

    /// <summary>Speed above stall at which the bot pulls up, in blocks per second.</summary>
    private const float RotateMargin = 4;

    /// <summary>Ticks of full nose-up input for the take-off rotation.</summary>
    private const int RotateTicks = 20;

    private enum Phase { Waiting, Approach, Boarding, TakeOff, Rotate, Climb, Leaving }

    private PredictedPlayer _player = null!;
    private Phase _phase;
    private int _phaseTicks;
    private float _maxCorrection;
    private float _startAltitude;

    public PawnState Spawn { get; init; }

    private PlaneSettings Plane => Net.Data!.Plane;

    protected override void Start()
    {
        _player = new PredictedPlayer(Spawn);
        Net.OwnStateReceived += OnServerState;
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

    private PlayerInput Decide()
    {
        PawnState me = _player.State;
        switch (_phase)
        {
            case Phase.Approach:
                IReadOnlyDictionary<int, RemotePlane> planes = Net.SampleRemotePlanes();
                if (!planes.TryGetValue(1, out RemotePlane plane))
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
                    _startAltitude = me.Plane.Position.Y;
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
                    Enter(Phase.Climb, me.Plane.OnGround ? "still on the ground after rotating" : "airborne; climbing");
                }

                return Input(InputButtons.Sprint, pitch: 1);

            case Phase.Climb:
                if (me.Plane.Position.Y >= _startAltitude + ClimbHeight)
                {
                    Enter(Phase.Leaving, $"climbed {me.Plane.Position.Y - _startAltitude:0.0} blocks at {me.Plane.Airspeed:0.0} blocks/s; pressing E");
                    return Input(InputButtons.Use);
                }

                return Input(InputButtons.Sprint);

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
                }

                return Input(InputButtons.None);

            default:
                return Input(InputButtons.None);
        }
    }

    private void Enter(Phase phase, string message)
    {
        _phase = phase;
        _phaseTicks = 0;
        Log(message);
    }

    private PlayerInput Input(InputButtons buttons, float pitch = 0) => new(0, 0, pitch, 0, 0, buttons);
}
