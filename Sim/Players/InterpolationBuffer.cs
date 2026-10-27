using System.Numerics;
using IslandWar.Sim.Match;

namespace IslandWar.Sim.Players;

/// <summary>A remote player's state at one server tick, as shown to other clients.</summary>
/// <param name="InPlane">Flying a plane: the plane is drawn instead of the body.</param>
public readonly record struct RemoteState(Vector3 Position, float Yaw, float Pitch, bool Flying, bool InPlane)
{
    public static RemoteState Lerp(RemoteState a, RemoteState b, float t) => new(
        Vector3.Lerp(a.Position, b.Position, t),
        InterpolationBuffers.LerpAngle(a.Yaw, b.Yaw, t),
        a.Pitch + (b.Pitch - a.Pitch) * t,
        t < 0.5f ? a.Flying : b.Flying,
        t < 0.5f ? a.InPlane : b.InPlane);
}

/// <summary>A plane as other clients draw it.</summary>
/// <param name="Ship">The ship kind, or null for a plane.</param>
public readonly record struct RemotePlane(Vector3 Position, Quaternion Rotation, float Throttle, int PilotPeerId, Team Team = Team.None, string? Ship = null)
{
    public static RemotePlane Lerp(RemotePlane a, RemotePlane b, float t) => new(
        Vector3.Lerp(a.Position, b.Position, t),
        Quaternion.Slerp(a.Rotation, b.Rotation, t),
        a.Throttle + (b.Throttle - a.Throttle) * t,
        t < 0.5f ? a.PilotPeerId : b.PilotPeerId,
        b.Team,
        b.Ship);
}

/// <summary>
/// Recent server snapshots of one remote thing (a player or a plane). Remote things are drawn slightly in
/// the past, between two snapshots, so they move smoothly even though snapshots arrive only every few ticks.
/// </summary>
public sealed class InterpolationBuffer<T>
{
    private const int Capacity = 32;

    private readonly List<(uint Tick, T State)> _samples = new();
    private readonly Func<T, T, float, T> _lerp;

    public InterpolationBuffer(Func<T, T, float, T> lerp)
    {
        _lerp = lerp;
    }

    public bool IsEmpty => _samples.Count == 0;

    public void Add(uint tick, T state)
    {
        if (_samples.Count > 0 && tick <= _samples[^1].Tick)
        {
            return;
        }

        _samples.Add((tick, state));
        if (_samples.Count > Capacity)
        {
            _samples.RemoveAt(0);
        }
    }

    /// <summary>
    /// The state at a (fractional) server tick. Before the first sample it holds the first; after the last
    /// it holds the last rather than guessing ahead.
    /// </summary>
    public T Sample(double tick)
    {
        if (_samples.Count == 0)
        {
            throw new InvalidOperationException("No snapshots yet.");
        }

        if (tick <= _samples[0].Tick)
        {
            return _samples[0].State;
        }

        for (int i = 1; i < _samples.Count; i++)
        {
            if (tick <= _samples[i].Tick)
            {
                (uint t0, T a) = _samples[i - 1];
                (uint t1, T b) = _samples[i];
                return _lerp(a, b, (float)((tick - t0) / (t1 - t0)));
            }
        }

        return _samples[^1].State;
    }
}

public static class InterpolationBuffers
{
    public static InterpolationBuffer<RemoteState> ForPlayer() => new(RemoteState.Lerp);

    public static InterpolationBuffer<RemotePlane> ForPlane() => new(RemotePlane.Lerp);

    /// <summary>Interpolates the short way round, so turning past 180 degrees doesn't spin the long way.</summary>
    public static float LerpAngle(float from, float to, float t)
    {
        float difference = MathF.IEEERemainder(to - from, 2 * MathF.PI);
        return from + difference * t;
    }
}
