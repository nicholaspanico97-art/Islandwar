using IslandWar.Sim.Match;
using IslandWar.Sim.Players;
using IslandWar.Sim.Protocol;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Server;

/// <summary>How a server was started.</summary>
/// <param name="Dev">Developer mode: noclip flying, and bots may pick either team.</param>
public sealed record ServerOptions(string PresetName, int Seed, bool Dev);

/// <summary>
/// Everything the server's systems share: the data, the world, the players, planes and bombs, and the
/// match. Each system owns its part of the rules and reads or changes this state; none of them talk to the
/// engine, only to <see cref="Net"/> and <see cref="Log"/>.
/// </summary>
public sealed class ServerState
{
    private readonly Dictionary<Team, int> _spawnSlots = new() { [Team.Red] = 0, [Team.Blue] = 0 };

    public ServerState(GameData data, ServerOptions options, IServerTransport net, Action<string> log)
    {
        Data = data;
        Options = options;
        Preset = data.Preset(options.PresetName);
        Net = net;
        Log = log;
        World = IslandGenerator.Generate(data.Terrain, options.Seed);
    }

    public GameData Data { get; }

    public GameRules Rules => Data.Rules;

    public ServerOptions Options { get; }

    public MatchPreset Preset { get; }

    public IServerTransport Net { get; }

    public Action<string> Log { get; }

    public VoxelWorld World { get; set; }

    /// <summary>Everyone who has said hello, by peer id.</summary>
    public Dictionary<int, ServerPlayer> Players { get; } = new();

    public Dictionary<int, ServerPlane> Planes { get; } = new();

    public List<ServerBomb> Bombs { get; } = new();

    public MatchStatus Match { get; set; } = MatchStatus.Warmup;

    /// <summary>Server ticks since start.</summary>
    public uint Tick { get; set; }

    public float TickSeconds => Data.Net.TickSeconds;

    public int Count(Team team) => Players.Values.Count(p => p.Team == team);

    public bool IsOnline(int playerId) => Players.Values.Any(p => p.PlayerId == playerId);

    /// <summary>The next free place to stand on a team's island.</summary>
    public PlayerState NextSpawn(Team team) => Data.SpawnIn(World, team, _spawnSlots[team]++);

    /// <summary>Starts every team's spawn ring from the centre again (a fresh match).</summary>
    public void ResetSpawns()
    {
        foreach (Team team in Teams.Playing)
        {
            _spawnSlots[team] = 0;
        }
    }

    /// <summary>The world as it is now, for a joiner: the seed, every edit and the hash to check against.</summary>
    public WorldInit CurrentWorld() =>
        new(Options.Seed, Data, WireFormat.EncodeEdits(World.EditsWithOwners().ToList()), World.ComputeHash(), Options.Dev, Options.PresetName);

    public void BroadcastRoster() =>
        Net.BroadcastRoster(WireFormat.EncodeRoster(Players
            .Select(p => new RosterEntry(p.Key, p.Value.Name, p.Value.Team, p.Value.ConcedeVote, Net.PingOf(p.Key), p.Value.PlayerId))
            .ToList()));
}
