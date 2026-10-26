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

    /// <summary>
    /// Server: developer mode. Players may fly (noclip) and pick any team regardless of balance. The test
    /// bots need both. Off for real matches.
    /// </summary>
    public bool Dev { get; init; }

    /// <summary>Server: the match preset from data/presets.json.</summary>
    public string Preset { get; init; } = "compressed";

    /// <summary>Client: the player name to ask for. Null means the one saved in the menu, or a default.</summary>
    public string? Name { get; init; }

    /// <summary>Client: the team to ask for (red or blue). Null lets the server choose.</summary>
    public string? Team { get; init; }

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
                "--dev" => result with { Dev = true },
                "--preset" => result with { Preset = Required(key, value) },
                "--name" => result with { Name = Required(key, value) },
                "--team" => result with { Team = value is "red" or "blue" ? value : throw new ArgumentException("--team must be red or blue.") },
                "--bot" => result with { Bot = value is "builder" or "breaker" or "pilot" ? value : throw new ArgumentException("--bot must be builder, breaker or pilot.") },
                _ => throw new ArgumentException(
                    $"Unknown argument '{arg}'. Expected --server, --port=N, --seed=N, --dev, --preset=NAME, --connect=HOST[:PORT], --name=NAME, --team=red|blue or --bot=builder|breaker|pilot."),
            };
        }

        return result;
    }

    private static string Required(string key, string? value) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{key} needs a value, for example {key}=something.") : value;

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
