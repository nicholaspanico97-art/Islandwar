using System.Numerics;

namespace IslandWar.Sim.Players;

/// <summary>A remote player's state at one server tick, as shown to other clients.</summary>
public readonly record struct RemoteState(Vector3 Position, float Yaw, float Pitch, bool Flying);

/// <summary>
/// Recent server snapshots of one remote player. Other players are drawn slightly in the past, between
/// two snapshots, so their movement looks smooth even though snapshots arrive only every few ticks.
/// </summary>
public sealed class InterpolationBuffer
{
    private const int Capacity = 32;

    private readonly List<(uint Tick, RemoteState State)> _samples = new();

    public bool IsEmpty => _samples.Count == 0;

    public void Add(uint tick, RemoteState state)
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
    public RemoteState Sample(double tick)
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
                (uint t0, RemoteState a) = _samples[i - 1];
                (uint t1, RemoteState b) = _samples[i];
                float t = (float)((tick - t0) / (t1 - t0));
                return new RemoteState(
                    Vector3.Lerp(a.Position, b.Position, t),
                    LerpAngle(a.Yaw, b.Yaw, t),
                    a.Pitch + (b.Pitch - a.Pitch) * t,
                    t < 0.5f ? a.Flying : b.Flying);
            }
        }

        return _samples[^1].State;
    }

    /// <summary>Interpolates the short way round, so turning past 180 degrees doesn't spin the long way.</summary>
    public static float LerpAngle(float from, float to, float t)
    {
        float difference = MathF.IEEERemainder(to - from, 2 * MathF.PI);
        return from + difference * t;
    }
}
