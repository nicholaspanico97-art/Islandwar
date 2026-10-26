using IslandWar.Sim.Match;
using IslandWar.Sim.Objects;
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
        Objects = new ObjectStore(data.Kinds);
        foreach (Team team in Teams.Playing)
        {
            TeamStates[team] = new TeamState(team);
        }
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

    /// <summary>Structures and anything else placed in the world, besides blocks.</summary>
    public ObjectStore Objects { get; }

    /// <summary>What each team has as a whole.</summary>
    public Dictionary<Team, TeamState> TeamStates { get; } = new();

    public Dictionary<int, ServerPlane> Planes { get; } = new();

    public List<ServerBomb> Bombs { get; } = new();

    public MatchStatus Match { get; set; } = MatchStatus.Warmup;

    /// <summary>Server ticks since start.</summary>
    public uint Tick { get; set; }

    public float TickSeconds => Data.Net.TickSeconds;

    /// <summary>Seconds since the server started, by its tick.</summary>
    public double Seconds => Tick * (double)Data.Net.TickSeconds;

    /// <summary>The preset builds freely (practice, tests): no items needed or gathered, instant mining.</summary>
    public bool Creative => Preset.Creative;

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

    /// <summary>What clients check their copy against: the world and every object.</summary>
    public ulong ComputeHash() => GameHash.Of(World, Objects);

    /// <summary>The world as it is now, for a joiner: the seed, every edit, every object and the hash to check against.</summary>
    public WorldInit CurrentWorld() =>
        new(Options.Seed, Data, WireFormat.EncodeEdits(World.EditsWithOwners().ToList()), WireFormat.EncodeObjects(Objects.All.ToList()),
            ComputeHash(), Options.Dev, Options.PresetName);

    public void BroadcastRoster() =>
        Net.BroadcastRoster(WireFormat.EncodeRoster(Players
            .Select(p => new RosterEntry(p.Key, p.Value.Name, p.Value.Team, p.Value.ConcedeVote, Net.PingOf(p.Key), p.Value.PlayerId))
            .ToList()));
}
