using System;
using System.Globalization;

namespace IslandWar;

/// <summary>
/// Command-line options. Godot passes everything after a bare <c>--</c> to the game, for example
/// <c>godot --headless --path . -- --server --port=7777</c>.
/// </summary>
public sealed record LaunchArgs
{
    public const int DefaultPort = 7777;

    /// <summary>Run as the dedicated server instead of a client.</summary>
    public bool IsServer { get; init; }

    /// <summary>Port the server listens on, or the client connects to.</summary>
    public int Port { get; init; } = DefaultPort;

    /// <summary>Server address for a client. Null means the client does not connect.</summary>
    public string? ConnectHost { get; init; }

    /// <summary>Run as a headless test bot instead of a player (needs --connect). See Client/Bots.</summary>
    public string? Bot { get; init; }

    /// <summary>Terrain seed for the server or the local sandbox. Null means use the default from data/terrain.json.</summary>
    public int? Seed { get; init; }

    public static LaunchArgs Parse(string[] args)
    {
        var result = new LaunchArgs();
        foreach (string arg in args)
        {
            (string key, string? value) = Split(arg);
            result = key switch
            {
                "--server" => result with { IsServer = true },
                "--port" => result with { Port = ParseInt(key, value) },
                "--seed" => result with { Seed = ParseInt(key, value) },
                "--connect" => WithConnect(result, value),
                "--bot" => result with { Bot = value is "builder" or "breaker" ? value : throw new ArgumentException("--bot must be builder or breaker.") },
                _ => throw new ArgumentException($"Unknown argument '{arg}'. Expected --server, --port=N, --seed=N, --connect=HOST[:PORT] or --bot=builder|breaker."),
            };
        }

        return result;
    }

    private static LaunchArgs WithConnect(LaunchArgs args, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("--connect needs an address, for example --connect=127.0.0.1:7777.");
        }

        int colon = value.LastIndexOf(':');
        if (colon < 0)
        {
            return args with { ConnectHost = value };
        }

        return args with { ConnectHost = value[..colon], Port = ParseInt("--connect", value[(colon + 1)..]) };
    }

    private static (string Key, string? Value) Split(string arg)
    {
        int equals = arg.IndexOf('=');
        return equals < 0 ? (arg, null) : (arg[..equals], arg[(equals + 1)..]);
    }

    private static int ParseInt(string key, string? value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
        {
            throw new ArgumentException($"{key} needs a whole number, got '{value}'.");
        }

        return number;
    }
}
