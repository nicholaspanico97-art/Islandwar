using System.Text.Json;
using System.Text.Json.Nodes;
using IslandWar.Sim.Match;
using IslandWar.Sim.Protocol;
using IslandWar.Sim.Server;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Tests;

/// <summary>Records everything the server sends instead of sending it.</summary>
public sealed class FakeTransport : IServerTransport
{
    public List<(int Peer, WorldInit World)> Worlds { get; } = new();
    public List<IReadOnlyList<RosterEntry>> Rosters { get; } = new();
    public List<(int Peer, string Reason)> Kicks { get; } = new();
    public List<int> Disconnected { get; } = new();
    public List<VoxelEdit> Broadcast { get; } = new();
    public List<(int Peer, VoxelEdit Edit)> Corrections { get; } = new();
    public List<(int Peer, ulong Hash)> Hashes { get; } = new();
    public List<Explosion> Explosions { get; } = new();
    public List<(int Peer, Snapshot Snapshot)> Snapshots { get; } = new();

    public void SendWorld(int peer, WorldInit world) => Worlds.Add((peer, world));
    public void BroadcastRoster(byte[] roster) => Rosters.Add(WireFormat.DecodeRoster(roster));
    public void Kick(int peer, string reason) => Kicks.Add((peer, reason));
    public void Disconnect(int peer) => Disconnected.Add(peer);
    public int PingOf(int peer) => 0;
    public void BroadcastBombDrop(BombDrop drop) { }
    public void BroadcastExplosion(Explosion explosion) => Explosions.Add(explosion);
    public void BroadcastEdit(VoxelEdit edit) => Broadcast.Add(edit);
    public void SendEdit(int peer, VoxelEdit edit) => Corrections.Add((peer, edit));
    public void SendSnapshot(int peer, byte[] snapshot) => Snapshots.Add((peer, WireFormat.DecodeSnapshot(snapshot)));
    public void SendHash(int peer, ulong hash) => Hashes.Add((peer, hash));
}

/// <summary>A server on the real data files, with the islands shrunk so each test generates its world quickly.</summary>
public sealed class TestServer
{
    private static readonly Lazy<GameData> SmallData = new(() => GameData.Load(name =>
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", name));
        return name == "terrain.json" ? Shrink(json) : json;
    }));

    private ulong _now;

    public TestServer(string preset = "practice", bool dev = false)
    {
        Server = new GameServer(SmallData.Value, new ServerOptions(preset, 7, dev), Net, Log.Add);
    }

    public GameServer Server { get; }

    public FakeTransport Net { get; } = new();

    public List<string> Log { get; } = new();

    public ServerState State => Server.State;

    /// <summary>Connects a peer and says hello for it.</summary>
    public ServerPlayer Join(int peer, string name, Team team = Team.None, string token = "")
    {
        Server.PeerConnected(peer, _now);
        Server.Hello(peer, name, team, token.Length > 0 ? token : $"token-{peer}");
        return State.Players[peer];
    }

    public void Run(int ticks, ulong msecPerTick = 16)
    {
        for (int i = 0; i < ticks; i++)
        {
            _now += msecPerTick;
            Server.Tick(_now);
        }
    }

    /// <summary>The same layout at a fifth of the size (see IslandGeneratorTests.Small).</summary>
    private static string Shrink(string json)
    {
        JsonNode terrain = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!;
        terrain["chunks_x"] = 20;
        terrain["chunks_z"] = 10;
        terrain["island_length"] = 120;
        terrain["island_width"] = 60;
        terrain["strait_width"] = 40;
        terrain["coast_noise"] = 6;
        terrain["coast_slope"] = 8;
        terrain["beach_width"] = 4;
        terrain["inland_slope"] = 8;
        terrain["spawn_clearing"] = 4;
        return terrain.ToJsonString();
    }
}

public class ServerTests
{
    [Fact]
    public void AJoinerGetsTheWorldATeamAndARosterEntry()
    {
        var test = new TestServer();
        ServerPlayer player = test.Join(2, "Alice", Team.Blue);

        Assert.Equal(Team.Blue, player.Team);
        Assert.Equal(1, player.PlayerId);
        (int peer, WorldInit world) = Assert.Single(test.Net.Worlds);
        Assert.Equal(2, peer);
        Assert.Equal(test.Server.World.ComputeHash(), world.Hash);
        Assert.Contains(test.Net.Rosters[^1], e => e.PeerId == 2 && e.Name == "Alice" && e.Team == Team.Blue);
    }

    [Fact]
    public void AClientThatNeverSaysHelloIsDropped()
    {
        var test = new TestServer();
        test.Server.PeerConnected(5, 0);
        test.Run(700);

        Assert.Equal(new[] { 5 }, test.Net.Disconnected);
        Assert.Empty(test.State.Players);
    }

    [Fact]
    public void ASecondWindowWithTheSameTokenIsANewPlayer()
    {
        var test = new TestServer();
        ServerPlayer first = test.Join(2, "Alice", token: "shared");
        ServerPlayer second = test.Join(3, "Alice", token: "shared");

        Assert.NotEqual(first.PlayerId, second.PlayerId);
        Assert.Equal("Alice 2", second.Name);
    }

    [Fact]
    public void PlacingABlockInReachIsAppliedAndBroadcastWithItsOwner()
    {
        var test = new TestServer();
        ServerPlayer player = test.Join(2, "Builder", Team.Red);
        BlockPos target = FreeCellBeside(test, player);

        test.Server.EditRequested(2, new VoxelEdit(target, Block.Planks));

        Assert.Equal(Block.Planks, test.Server.World.Get(target));
        VoxelEdit sent = Assert.Single(test.Net.Broadcast);
        Assert.Equal(new BlockOwner(player.PlayerId, Team.Red), sent.Owner);
    }

    [Fact]
    public void AnEditOutOfReachIsUndoneForTheSenderOnly()
    {
        var test = new TestServer();
        ServerPlayer player = test.Join(2, "Builder", Team.Red);
        BlockPos far = FreeCellBeside(test, player) with { Y = test.Server.World.SizeY - 1 };

        test.Server.EditRequested(2, new VoxelEdit(far, Block.Planks));

        Assert.Empty(test.Net.Broadcast);
        Assert.Equal((2, new VoxelEdit(far, Block.Air)), Assert.Single(test.Net.Corrections));
        Assert.Contains(test.Log, line => line.Contains("out of reach"));
    }

    [Fact]
    public void TeammatesCantBreakEachOthersBlocksButTheEnemyCan()
    {
        var test = new TestServer(dev: true);
        ServerPlayer builder = test.Join(2, "Builder", Team.Red);
        test.Join(3, "Mate", Team.Red);
        test.Join(4, "Enemy", Team.Blue);
        BlockPos target = FreeCellBeside(test, builder);
        test.Server.EditRequested(2, new VoxelEdit(target, Block.Planks));

        // Everyone stands at the builder's spot, so reach isn't what decides it.
        foreach (ServerPlayer player in test.State.Players.Values)
        {
            player.Pawn = builder.Pawn;
        }

        test.Server.EditRequested(3, new VoxelEdit(target, Block.Air));
        Assert.Equal(Block.Planks, test.Server.World.Get(target));

        test.Server.EditRequested(4, new VoxelEdit(target, Block.Air));
        Assert.Equal(Block.Air, test.Server.World.Get(target));
    }

    [Fact]
    public void ThePlayerWhoLeftMidMatchComesBackToTheirTeamAndName()
    {
        var test = new TestServer("test", dev: true);
        test.Join(2, "Red", Team.Red, "red-token");
        test.Join(3, "Blue", Team.Blue, "blue-token");
        test.Run(60 * 15);
        Assert.True(test.State.Match.Playing, $"expected the match to be on, got {test.State.Match.Stage}");

        test.Server.PeerDisconnected(2);
        ServerPlayer back = test.Join(9, "Someone else", Team.Blue, "red-token");

        Assert.Equal(Team.Red, back.Team);
        Assert.Equal("Red", back.Name);
        Assert.Contains(test.Log, line => line.Contains("came back as 'Red'"));
    }

    [Fact]
    public void ATeamWhoseEveryMemberVotesConcedesTheMatch()
    {
        var test = new TestServer("test", dev: true);
        test.Join(2, "Red", Team.Red);
        test.Join(3, "Blue", Team.Blue);
        test.Run(60 * 15);

        test.Server.ConcedeVote(3, true);

        Assert.Equal(MatchStage.Ended, test.State.Match.Stage);
        Assert.Equal(Team.Red, test.State.Match.Winner);
        Assert.Equal(EndReason.Concede, test.State.Match.Reason);
    }

    [Fact]
    public void SnapshotsGoToEveryPlayerWithTheirOwnNukeTimer()
    {
        var test = new TestServer();
        test.Join(2, "Alice", Team.Red);
        test.Join(3, "Bob", Team.Blue);
        test.Run(test.State.Data.Net.SnapshotIntervalTicks);

        Assert.Equal(new[] { 2, 3 }, test.Net.Snapshots.Select(s => s.Peer).Order());
        Assert.All(test.Net.Snapshots, s => Assert.Equal(2, s.Snapshot.Players.Count));
        Assert.All(test.Net.Snapshots, s => Assert.Equal(2, s.Snapshot.Planes.Count));
    }

    [Fact]
    public void TheHashReplyIsTheWorldHash()
    {
        var test = new TestServer();
        test.Join(2, "Alice");
        test.Server.HashRequested(2);

        Assert.Equal((2, test.Server.World.ComputeHash()), Assert.Single(test.Net.Hashes));
    }

    /// <summary>An air cell next to the player's head, in easy reach.</summary>
    private static BlockPos FreeCellBeside(TestServer test, ServerPlayer player)
    {
        System.Numerics.Vector3 feet = player.Pawn.Player.Position;
        var cell = new BlockPos((int)MathF.Floor(feet.X) + 2, (int)MathF.Floor(feet.Y) + 1, (int)MathF.Floor(feet.Z));
        Assert.Equal(Block.Air, test.Server.World.Get(cell));
        return cell;
    }
}
