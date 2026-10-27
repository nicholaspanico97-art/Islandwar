using System;
using System.Linq;
using System.Numerics;
using IslandWar.Sim.Items;
using IslandWar.Sim.Objects;
using IslandWar.Sim.Players;
using IslandWar.Sim.Vehicles;
using IslandWar.Sim.Voxels;

namespace IslandWar.Client.Bots;

/// <summary>
/// The sailor bot for the automated match test (see docs/testing.md), alone on Red on the practice preset in
/// developer mode (creative: the dock is built at once and a galley needs nothing; noclip to get about). It
/// flies to the shore by its plane's mooring, puts down a dock facing the sea, asks it for a galley, boards
/// it, rows out and turns, gets off onto dry land, and checks its prediction agreed with the server all the
/// way; then compares its game hash with the server's.
/// </summary>
public partial class SailorBot : TestBot
{
    /// <summary>Largest gap allowed between the predicted and the server's position, outside getting in and out.</summary>
    private const float MaxCorrection = 0.05f;

    private enum Phase { FlyingToShore, Launching, FlyingToShip, Boarding, Rowing, Turning, Drifting, GettingOff, Done }

    private PredictedPlayer _player = null!;
    private Phase _phase;
    private double _phaseSeconds;
    private BlockPos _dockOrigin;
    private int _dockFacing;
    private Vector3 _shore;
    private int _shipId;
    private Vector3 _launchedAt;
    private float _maxCorrection;
    private float _topSpeed;

    public PawnState Spawn { get; init; }

    public override Vector3? Eye => _player?.State.InPlane == true ? _player.State.Plane.Position : _player?.State.Player.Position;

    private ShipKind Galley => Net.Data!.Kinds.Ships["galley"];

    protected override void Start()
    {
        _player = new PredictedPlayer(Spawn);
        Net.OwnStateReceived += (server, ack) =>
        {
            bool wasAboard = _player.State.InPlane;
            float correction = _player.Reconcile(World, Net.Data!.Rules, server, ack, Net.Data.Net.TickSeconds);
            if (wasAboard == _player.State.InPlane && _phase is not (Phase.Boarding or Phase.GettingOff))
            {
                _maxCorrection = Math.Max(_maxCorrection, correction);
            }
        };

        ObjectKind dock = Net.Data!.Kinds.Get("dock")!;
        PlaneState mooring = Net.Data.PlaneSpawnIn(World, Net.MyTeam);
        if (ShipSailing.FindDockSite(World, Net.Data.Player, Net.Data.Rules.SeaLevel, dock, Galley, (int)mooring.Position.X, (int)mooring.Position.Z - 8, 12) is not { } site)
        {
            Finish(false, "found no dock site by the mooring");
            return;
        }

        (_dockOrigin, _dockFacing) = site;
        BlockPos[] footprint = GameObject.FootprintOf(dock, _dockOrigin, _dockFacing).ToArray();
        var centre = new Vector3((float)footprint.Average(b => b.X) + 0.5f, _dockOrigin.Y, (float)footprint.Average(b => b.Z) + 0.5f);
        Vector3 inland = _dockFacing switch { 0 => Vector3.UnitZ, 1 => Vector3.UnitX, 2 => -Vector3.UnitZ, _ => -Vector3.UnitX };
        _shore = centre + inland * 4 + Vector3.UnitY * 2;
        Log($"on {Net.MyTeam}; flying to a dock site at {_dockOrigin}, facing {_dockFacing}");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_player is null || _phase == Phase.Done)
        {
            return;
        }

        _phaseSeconds += delta;
        _player.Predict(World, Net.Data!.Rules, Decide(), Net.Data.Net.TickSeconds);
        Net.SendInputs(_player.Latest(Net.Data.Net.InputsPerPacket));
    }

    private void Enter(Phase phase, string message)
    {
        _phase = phase;
        _phaseSeconds = 0;
        Log(message);
    }

    private PlayerInput Decide()
    {
        PawnState me = _player.State;
        switch (_phase)
        {
            case Phase.FlyingToShore when FlyTo(_shore) is { } input:
                return input;
            case Phase.FlyingToShore:
                Net.RequestPlace("dock", _dockOrigin, _dockFacing);
                Enter(Phase.Launching, "at the shore; placing the dock");
                WaitUntil(() => Net.Objects!.At(_dockOrigin) is not null, () =>
                {
                    Log("the dock is up; asking for a galley");
                    Net.RequestTransfer(Net.Objects!.At(_dockOrigin)!.Id, Blueprint.DeliveredSlot, Net.Data!.Items.Items[0].Id, 1, true);
                });
                return Hover();
            case Phase.Launching:
                if (Net.SampleRemotePlanes().FirstOrDefault(p => p.Value.Ship is not null && p.Value.Team == Net.MyTeam) is { Value.Ship: not null } ship)
                {
                    _shipId = ship.Key;
                    _launchedAt = ship.Value.Position;
                    Enter(Phase.FlyingToShip, $"galley {ship.Key} launched at {ship.Value.Position}; going aboard");
                }

                return Hover();
            case Phase.FlyingToShip when FlyTo(_launchedAt + Vector3.UnitY * 1.5f) is { } input:
                return input;
            case Phase.FlyingToShip:
                Enter(Phase.Boarding, "beside the galley; pressing E");
                return Press(InputButtons.Use);
            case Phase.Boarding when me.InPlane:
                Enter(Phase.Rowing, $"aboard vehicle {me.PlaneId}; rowing out");
                return Row(1, 0);
            case Phase.Boarding:
                return _phaseSeconds > 3 ? Fail("couldn't get aboard") : Hover();
            case Phase.Rowing:
                _topSpeed = Math.Max(_topSpeed, me.Plane.Airspeed);
                if (_phaseSeconds < 5)
                {
                    return Row(1, 0);
                }

                Enter(Phase.Turning, $"rowed {Vector3.Distance(_launchedAt, me.Plane.Position):0.0} blocks at up to {_topSpeed:0.0} blocks/s; turning");
                return Row(1, -1);
            case Phase.Turning when _phaseSeconds < 3:
                return Row(1, -1);
            case Phase.Turning:
                Enter(Phase.Drifting, $"heading {me.Plane.Heading:0.00} after turning; resting the oars");
                return Row(0, 0);
            case Phase.Drifting when _phaseSeconds < 4:
                return Row(0, 0);
            case Phase.Drifting:
                Enter(Phase.GettingOff, $"stopped at speed {me.Plane.Airspeed:0.00}; pressing E");
                return Press(InputButtons.Use);
            case Phase.GettingOff when !me.InPlane:
                _phase = Phase.Done;
                Log($"off the galley at {me.Player.Position}; largest correction from the server {_maxCorrection:0.0000} blocks");
                if (_maxCorrection > MaxCorrection)
                {
                    Finish(false, $"the prediction was corrected by {_maxCorrection:0.000} blocks");
                }
                else
                {
                    CompareWithServer();
                }

                return Row(0, 0);
            case Phase.GettingOff:
                return _phaseSeconds > 3 ? Fail("couldn't get off") : Row(0, 0);
            default:
                return Hover();
        }
    }

    /// <summary>Noclip towards a point; null once there.</summary>
    private PlayerInput? FlyTo(Vector3 target)
    {
        Vector3 to = target - _player.State.Player.Position;
        if (to.Length() < 0.7f)
        {
            return null;
        }

        InputButtons vertical = to.Y > 0.3f ? InputButtons.Jump : to.Y < -0.3f ? InputButtons.Descend : InputButtons.None;
        bool level = MathF.Sqrt(to.X * to.X + to.Z * to.Z) < 0.3f;
        return new PlayerInput(0, 0, level ? 0 : -1, MathF.Atan2(-to.X, -to.Z), 0, InputButtons.Fly | vertical);
    }

    private static PlayerInput Hover() => new(0, 0, 0, 0, 0, InputButtons.Fly);

    private static PlayerInput Press(InputButtons button) => new(0, 0, 0, 0, 0, InputButtons.Fly | button);

    /// <summary>Oars forward (1) or astern (-1), and the rudder (A is -1).</summary>
    private static PlayerInput Row(float forward, float turn) => new(0, turn, -forward, 0, 0, InputButtons.None);

    private PlayerInput Fail(string why)
    {
        _phase = Phase.Done;
        Finish(false, why);
        return Hover();
    }
}
