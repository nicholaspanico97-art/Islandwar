using System.Numerics;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Players;

/// <summary>
/// The local player's predicted movement, on foot or flying. Each tick's input is applied at once and kept
/// until the server acknowledges it. When a server state arrives, the prediction restarts from it and
/// replays the inputs the server hasn't processed yet, so the player only sees a correction when client
/// and server disagree. Getting in or out of a plane isn't predicted; it shows up in the server's state.
/// </summary>
public sealed class PredictedPlayer
{
    /// <summary>Inputs kept for replay. Older ones are dropped; at 60 ticks a second this is two seconds.</summary>
    public const int MaxPending = 120;

    private readonly List<PlayerInput> _pending = new();
    private uint _lastSeq;
    private uint _lastAck;

    public PredictedPlayer(PawnState start)
    {
        State = start;
    }

    public PawnState State { get; private set; }

    public int PendingCount => _pending.Count;

    /// <summary>Numbers the input, applies it and keeps it for replay.</summary>
    /// <returns>The input with its sequence number, ready to send to the server.</returns>
    public PlayerInput Predict(VoxelWorld world, GameRules rules, PlayerInput input, float dt)
    {
        input = input with { Seq = ++_lastSeq };
        State = Pawn.Step(world, rules, State, input, dt);
        _pending.Add(input);
        if (_pending.Count > MaxPending)
        {
            _pending.RemoveAt(0);
        }

        return input;
    }

    /// <summary>The newest inputs, oldest first, for sending. Repeating a few covers lost packets.</summary>
    public IReadOnlyList<PlayerInput> Latest(int count)
    {
        int take = Math.Min(count, _pending.Count);
        return _pending.GetRange(_pending.Count - take, take);
    }

    /// <summary>
    /// Restarts from the server's state after it processed input <paramref name="ack"/>, then replays newer
    /// inputs. Acknowledgements older than one already applied are ignored.
    /// </summary>
    /// <returns>How far the predicted position moved because of the correction.</returns>
    public float Reconcile(VoxelWorld world, GameRules rules, PawnState server, uint ack, float dt)
    {
        if (ack < _lastAck)
        {
            return 0;
        }

        _lastAck = ack;
        _pending.RemoveAll(input => input.Seq <= ack);
        Vector3 before = State.Player.Position;
        PawnState state = server;
        foreach (PlayerInput input in _pending)
        {
            state = Pawn.Step(world, rules, state, input, dt);
        }

        State = state;
        return Vector3.Distance(before, state.Player.Position);
    }
}
