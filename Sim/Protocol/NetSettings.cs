namespace IslandWar.Sim.Protocol;

/// <summary>Networking tunables. Loaded from <c>data/net.json</c>; never hardcode these.</summary>
public sealed record NetSettings
{
    /// <summary>Simulation ticks per second on server and clients. Player movement steps once per tick.</summary>
    public int TickRate { get; init; } = 60;

    /// <summary>The server sends a snapshot every this many ticks.</summary>
    public int SnapshotIntervalTicks { get; init; } = 3;

    /// <summary>Other players are drawn this many ticks in the past, between two snapshots.</summary>
    public int InterpolationDelayTicks { get; init; } = 6;

    /// <summary>How many of the newest inputs each input packet repeats, to survive packet loss.</summary>
    public int InputsPerPacket { get; init; } = 4;

    /// <summary>
    /// Most inputs the server will hold in reserve for a player. The server runs at most one input per
    /// tick on average, so a client can't move faster by sending inputs faster.
    /// </summary>
    public int MaxInputBacklog { get; init; } = 10;

    /// <summary>Block edits a player may make per second, on average. Extra requests are rejected.</summary>
    public int MaxEditsPerSecond { get; init; } = 20;

    public float TickSeconds => 1f / TickRate;

    public static NetSettings FromJson(string json)
    {
        NetSettings settings = DataJson.Parse<NetSettings>(json, "net.json");
        if (settings.TickRate <= 0 || settings.SnapshotIntervalTicks <= 0 || settings.InterpolationDelayTicks < 0
            || settings.InputsPerPacket <= 0 || settings.InputsPerPacket > WireFormat.MaxInputsPerPacket
            || settings.MaxInputBacklog <= 0 || settings.MaxEditsPerSecond <= 0)
        {
            throw new InvalidDataException($"net.json has a value out of range (inputs_per_packet must be 1 to {WireFormat.MaxInputsPerPacket}).");
        }

        return settings;
    }
}
