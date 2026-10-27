using IslandWar.Sim.Items;
using IslandWar.Sim.Match;
using IslandWar.Sim.Players;
using IslandWar.Sim.Protocol;

namespace IslandWar.Sim.Server;

/// <summary>One connected player as the server sees them.</summary>
public sealed class ServerPlayer
{
    private readonly Queue<PlayerInput> _inputs = new();
    private uint _lastQueuedSeq;
    private float _inputBudget;
    private float _editBudget;

    public ServerPlayer(int peer, string name, Team team, string token, int playerId, PlayerState spawn, NetSettings net)
    {
        Peer = peer;
        Name = name;
        Team = team;
        Token = token;
        PlayerId = playerId;
        Spawn = spawn;
        Pawn = PawnState.OnFoot(spawn);
        _editBudget = net.MaxEditsPerSecond;
    }

    /// <summary>The network peer id of this connection.</summary>
    public int Peer { get; }

    public string Name { get; }

    public Team Team { get; }

    /// <summary>The client's identity token, for getting this seat back after a disconnect.</summary>
    public string Token { get; }

    /// <summary>Lasting id (same token, same id), recorded as the owner of the blocks they place.</summary>
    public int PlayerId { get; }

    public bool ConcedeVote { get; set; }

    /// <summary>What they carry, by item id (sent only to them).</summary>
    public IReadOnlyDictionary<string, int> Backpack { get; set; } = ItemCounts.None;

    /// <summary>When they last mined a block, on the server clock, so mining can't go faster than hardness allows.</summary>
    public double LastMinedSeconds { get; set; } = double.NegativeInfinity;

    /// <summary>Where this player appeared, and where they come back after falling off the map.</summary>
    public PlayerState Spawn { get; private set; }

    public PawnState Pawn { get; set; }

    /// <summary>Health; at 0 the player is dead until <see cref="DeadUntil"/>.</summary>
    public int Health { get; set; } = 100;

    /// <summary>When a dead player comes back, on the server clock; null while alive.</summary>
    public double? DeadUntil { get; set; }

    public bool Dead => DeadUntil is not null;

    /// <summary>When they last shot an arrow, on the server clock.</summary>
    public double LastShotSeconds { get; set; } = double.NegativeInfinity;

    /// <summary>The newest input processed, sent back in snapshots so the client can reconcile.</summary>
    public uint AckSeq { get; private set; }

    public float Yaw { get; private set; }

    public float Pitch { get; private set; }

    /// <summary>Queues new inputs. Packets repeat recent inputs, so anything already queued is skipped.</summary>
    public void Receive(IReadOnlyList<PlayerInput> inputs, NetSettings net)
    {
        foreach (PlayerInput input in inputs)
        {
            if (input.Seq > _lastQueuedSeq && _inputs.Count < net.MaxInputBacklog * 2)
            {
                _inputs.Enqueue(input);
                _lastQueuedSeq = input.Seq;
            }
        }
    }

    /// <summary>
    /// Called once per tick. The input budget grows by one input per tick up to the backlog limit, so a
    /// client sending inputs faster than real time can't move faster than everyone else.
    /// </summary>
    public void Refill(NetSettings net)
    {
        _inputBudget = Math.Min(_inputBudget + 1, net.MaxInputBacklog);
        _editBudget = Math.Min(_editBudget + (float)net.MaxEditsPerSecond / net.TickRate, net.MaxEditsPerSecond);
    }

    /// <summary>
    /// Puts the player on foot at a new spawn for a fresh match. Queued inputs from before are dropped;
    /// the sequence numbers carry on, so the client's next inputs are still accepted.
    /// </summary>
    public void Respawn(PlayerState spawn)
    {
        Spawn = spawn;
        Pawn = PawnState.OnFoot(spawn);
        _inputs.Clear();
    }

    public bool TryTakeInput(out PlayerInput input)
    {
        if (_inputs.Count == 0 || _inputBudget < 1)
        {
            input = default;
            return false;
        }

        _inputBudget--;
        input = _inputs.Dequeue();
        return true;
    }

    public void Processed(PlayerInput input)
    {
        AckSeq = input.Seq;
        Yaw = input.Yaw;
        Pitch = input.Pitch;
    }

    public bool TrySpendEdit()
    {
        if (_editBudget < 1)
        {
            return false;
        }

        _editBudget--;
        return true;
    }
}
