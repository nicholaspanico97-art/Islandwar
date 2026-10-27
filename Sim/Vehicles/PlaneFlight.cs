using System.Numerics;
using IslandWar.Sim.Physics;
using IslandWar.Sim.Players;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Vehicles;

/// <summary>
/// A plane's state, or a ship's (see <see cref="ShipSailing"/>): every vehicle goes through the plane's
/// snapshots and prediction. <see cref="Position"/> is the middle of the underside (the wheels, or the keel
/// at the waterline). The vehicle faces -Z in its own frame, with +Y up and +X to the right, like Godot.
/// </summary>
/// <param name="Airspeed">Speed along the nose.</param>
/// <param name="Sink">Extra downward speed when flying too slowly to stay up.</param>
/// <param name="Throttle">0 to 1.</param>
/// <param name="Ship">The <see cref="ShipKind.Id"/> for a ship; null or empty for the plane.</param>
public readonly record struct PlaneState(Vector3 Position, Quaternion Rotation, float Airspeed, float Sink, float Throttle, bool OnGround, string? Ship = null)
{
    public bool IsShip => !string.IsNullOrEmpty(Ship);

    public Vector3 Forward => Vector3.Transform(-Vector3.UnitZ, Rotation);

    public Vector3 Velocity => Forward * Airspeed - Vector3.UnitY * Sink;

    /// <summary>A plane parked on the ground facing <paramref name="heading"/> (radians, 0 faces -Z).</summary>
    public static PlaneState Parked(Vector3 position, float heading) =>
        new(position, Quaternion.CreateFromAxisAngle(Vector3.UnitY, heading), 0, 0, 0, true);

    /// <summary>The heading of the nose around the vertical axis, 0 facing -Z, increasing to the left.</summary>
    public float Heading => MathF.Atan2(-Forward.X, -Forward.Z);
}

/// <summary>
/// The arcade flight model, run by the server for every plane and by the pilot's client for prediction.
/// Controls, reusing the player input: W/S pitch (S pulls the nose up), A/D roll in the air and steer on
/// the ground, Shift/Ctrl throttle up/down, Space brakes on the ground. A banked plane turns on its own.
/// Not a simulation: speed lives along the nose, and lift is just "fast enough or not".
/// </summary>
public static class PlaneFlight
{
    /// <summary>Downward probe speed while on the ground, so the next step can tell it's still on the ground.</summary>
    private const float GroundProbe = 1;

    /// <summary>Below this speed, ground steering is weaker (taxiing pivots slowly).</summary>
    private const float TaxiSpeed = 5;

    /// <summary>Weakest control strength, at zero airspeed, so a stalled plane can still be pointed down.</summary>
    private const float MinAuthority = 0.15f;

    /// <param name="seaLevel">The plane treats the water surface as solid ground (see docs/decisions.md).</param>
    public static PlaneState Step(VoxelWorld world, PlaneSettings settings, int seaLevel, PlaneState state, PlayerInput input, float dt)
    {
        float throttleInput = (input.Has(InputButtons.Sprint) ? 1 : 0) - (input.Has(InputButtons.Descend) ? 1 : 0);
        float throttle = Math.Clamp(state.Throttle + throttleInput * settings.ThrottleRate * dt, 0, 1);
        float pitchInput = input.MoveZ;
        float rollInput = input.MoveX;

        Quaternion rotation = state.Rotation;
        float airspeed = state.Airspeed;
        if (state.OnGround)
        {
            // Steer the nose wheel, and lift the nose only when nearly fast enough to fly.
            float heading = state.Heading - rollInput * settings.GroundSteerRate * Math.Min(1, airspeed / TaxiSpeed) * dt;
            float pitch = MathF.Asin(Math.Clamp(state.Forward.Y, -1, 1));
            pitch += airspeed >= settings.StallSpeed * 0.9f ? pitchInput * settings.PitchRate * dt : -settings.PitchRate * dt;
            rotation = Level(heading, Math.Clamp(pitch, 0, settings.MaxGroundPitch));

            float resistance = settings.RollingFriction + (input.Has(InputButtons.Jump) ? settings.Brake : 0);
            airspeed += (throttle * settings.Thrust - settings.Drag * airspeed) * dt;
            airspeed = Math.Max(0, airspeed - resistance * dt);
        }
        else
        {
            float authority = Math.Clamp(airspeed / settings.ControlSpeed, MinAuthority, 1);
            rotation *= Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitchInput * settings.PitchRate * authority * dt);
            rotation *= Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -rollInput * settings.RollRate * authority * dt);

            // Bank-and-turn: the lower wing pulls the nose round, about the world's vertical axis.
            Vector3 right = Vector3.Transform(Vector3.UnitX, rotation);
            rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, settings.TurnRate * right.Y * authority * dt) * rotation;
            rotation = Quaternion.Normalize(rotation);

            // Climbing trades speed for height and diving gives it back.
            float noseUp = Vector3.Transform(-Vector3.UnitZ, rotation).Y;
            airspeed += (throttle * settings.Thrust - settings.Drag * airspeed - settings.Gravity * noseUp) * dt;
        }

        airspeed = Math.Clamp(airspeed, 0, settings.MaxSpeed);

        // Too slow and the wings stop holding the plane up.
        float lift = Math.Min(1, airspeed / settings.StallSpeed);
        float sink = state.OnGround ? 0
            : lift < 1 ? state.Sink + settings.Gravity * (1 - lift * lift) * dt
            : Math.Max(0, state.Sink - settings.Gravity * dt);
        sink = Math.Min(sink, settings.MaxSink);

        Vector3 velocity = Vector3.Transform(-Vector3.UnitZ, rotation) * airspeed - Vector3.UnitY * sink;
        if (state.OnGround && velocity.Y <= 0)
        {
            velocity.Y = -GroundProbe;
        }

        var box = new VoxelBox(settings.Width, settings.Height, seaLevel);
        Vector3 position = state.Position;
        bool onGround = false;
        if (box.MoveAxis(world, ref position, 1, velocity.Y * dt))
        {
            onGround = velocity.Y < 0;
            sink = 0;
        }

        // Flying into a wall stops the plane dead. There's no damage until later milestones.
        bool hitX = box.MoveAxis(world, ref position, 0, velocity.X * dt);
        bool hitZ = box.MoveAxis(world, ref position, 2, velocity.Z * dt);
        if (hitX || hitZ)
        {
            airspeed = 0;
        }

        var next = new PlaneState(position, rotation, airspeed, sink, throttle, onGround);
        if (onGround && !state.OnGround)
        {
            // Touchdown: wings level, nose no lower than flat.
            float pitch = MathF.Asin(Math.Clamp(next.Forward.Y, -1, 1));
            next = next with { Rotation = Level(next.Heading, Math.Clamp(pitch, 0, settings.MaxGroundPitch)) };
        }

        return next;
    }

    /// <summary>Wings level, nose at <paramref name="heading"/> and <paramref name="pitch"/> (radians, up positive).</summary>
    public static Quaternion Level(float heading, float pitch) =>
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, heading) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch);
}
