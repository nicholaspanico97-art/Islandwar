using System.Numerics;
using IslandWar.Sim.Items;
using IslandWar.Sim.Match;
using IslandWar.Sim.Objects;
using IslandWar.Sim.Players;
using IslandWar.Sim.Protocol;
using IslandWar.Sim.Research;
using IslandWar.Sim.Server;
using IslandWar.Sim.Voxels;
using IslandWar.Sim.Weapons;

namespace IslandWar.Sim.Tests;

public class CombatTests
{
    private static readonly GameData Data = GameData.Load(name => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", name)));

    [Fact]
    public void LookingIsTheSameWayTheClientLooks()
    {
        Assert.Equal(-1, ArrowFlight.Look(0, 0).Z, 4);
        Assert.Equal(-1, ArrowFlight.Look(MathF.PI / 2, 0).X, 4);
        Assert.Equal(1, ArrowFlight.Look(0, MathF.PI / 2).Y, 4);
    }

    [Fact]
    public void AnArrowFallsAndStopsAtTheGround()
    {
        var world = new VoxelWorld(2, 2, 2);
        for (int x = 0; x < world.SizeX; x++)
        {
            for (int z = 0; z < world.SizeZ; z++)
            {
                world.SetGenerated(new BlockPos(x, 0, z), Block.Stone);
            }
        }

        var arrow = new ArrowState(new Vector3(2, 10, 2), new Vector3(20, 0, 0));
        Vector3? landed = null;
        for (int i = 0; i < 200 && landed is null; i++)
        {
            (arrow, landed) = ArrowFlight.Step(world, Data.Arrow, 0, arrow, 1 / 30f);
        }

        Assert.NotNull(landed);
        Assert.Equal(1, landed!.Value.Y, 2);
        Assert.True(landed.Value.X > 10);
    }

    [Fact]
    public void ASegmentThroughAPlayersBoxHitsAndOneBesideMisses()
    {
        var feet = new Vector3(10, 5, 10);

        // The box spans x 9.7 to 10.3, so a segment from x 8 to 12 enters it 1.7 of 4 along.
        Assert.Equal(0.425f, ArrowFlight.HitsPlayer(new Vector3(8, 6, 10), new Vector3(12, 6, 10), feet, 0.6f, 1.8f)!.Value, 3);
        Assert.Null(ArrowFlight.HitsPlayer(new Vector3(8, 6, 12), new Vector3(12, 6, 12), feet, 0.6f, 1.8f));
        Assert.Null(ArrowFlight.HitsPlayer(new Vector3(8, 8, 10), new Vector3(12, 8, 10), feet, 0.6f, 1.8f));
    }

    [Fact]
    public void ArcheryUnlocksTheBowAndArrows()
    {
        var team = new TeamState(Team.Red) { Era = 1 };

        Assert.False(Data.Techs.IsUnlocked(team, Unlock.Recipe("bow")));
        Assert.True(Data.Techs.IsUnlocked(team with { Researched = new[] { "archery" } }, Unlock.Recipe("arrows")));
    }

    [Fact]
    public void ShootingTakesABowAnArrowAndTimeToDraw()
    {
        var test = new TestServer("quick");
        ServerPlayer archer = test.Join(2, "Archer", Team.Red);

        test.Server.Combat.TryShoot(archer, 0, 0);
        Assert.Empty(test.Net.ArrowShots);

        archer.Backpack = ItemCounts.None.Plus("bow", 1).Plus("arrow", 2);
        test.Server.Combat.TryShoot(archer, 0, 0);
        test.Server.Combat.TryShoot(archer, 0, 0);
        Assert.Single(test.Net.ArrowShots);
        Assert.Equal(1, archer.Backpack.Of("arrow"));

        test.Run(test.State.Data.Net.TickRate);
        test.Server.Combat.TryShoot(archer, 0, 0);
        Assert.Equal(2, test.Net.ArrowShots.Count);
        Assert.Equal(0, archer.Backpack.Of("arrow"));
    }

    [Fact]
    public void AnArrowHurtsAnEnemyButNotATeammate()
    {
        var test = new TestServer("practice", dev: true);
        ServerPlayer archer = test.Join(2, "Archer", Team.Red);
        ServerPlayer friend = test.Join(3, "Friend", Team.Red);
        ServerPlayer enemy = test.Join(4, "Enemy", Team.Blue);
        Vector3 at = StandInFront(test, archer, friend, 4);

        Shoot(test, archer);
        Assert.Equal(100, friend.Health);

        friend.Pawn = PawnState.OnFoot(PlayerState.At(at + new Vector3(0, 0, 20)));
        enemy.Pawn = PawnState.OnFoot(PlayerState.At(at));
        test.Run(test.State.Data.Net.TickRate);
        Shoot(test, archer);

        Assert.Equal(100 - Data.Arrow.Damage, enemy.Health);
        Assert.Equal(enemy.Peer, test.Net.ArrowsLanded.Last().HitPeerId);
    }

    [Fact]
    public void ADeadPlayerDropsTheirBackpackVanishesAndComesBackAtFullHealth()
    {
        var test = new TestServer("quick");
        ServerPlayer victim = test.Join(2, "Victim", Team.Red);
        test.Join(3, "Watcher", Team.Blue);
        victim.Backpack = ItemCounts.None.Plus("log", 5).Plus("stone", 2);

        test.Server.Combat.Damage(victim, 1000, "a test");

        Assert.True(victim.Dead);
        Assert.Empty(victim.Backpack);
        GameObject bag = Assert.Single(test.State.Objects.All, o => o.Kind == "dropped_backpack");
        Assert.Equal(5, ItemCounts.Contents(bag).Of("log"));
        Assert.Equal(0, test.State.TeamStates[Team.Red].Amount("log"));

        test.Run(test.State.Data.Net.SnapshotIntervalTicks + 1);
        Assert.DoesNotContain(test.Net.Snapshots.Last(s => s.Peer == 3).Snapshot.Players, p => p.PeerId == 2);
        Snapshot own = test.Net.Snapshots.Last(s => s.Peer == 2).Snapshot;
        Assert.Equal(0, own.Health);
        Assert.True(own.RespawnIn > 0);

        test.Run((int)((Data.Player.RespawnSeconds + 0.5f) * test.State.Data.Net.TickRate));
        Assert.False(victim.Dead);
        Assert.Equal(Data.Player.MaxHealth, victim.Health);
        Assert.True(test.Net.Snapshots.Last(s => s.Peer == 2).Snapshot.RespawnIn < 0);
    }

    [Fact]
    public void AnyoneCanEmptyADroppedBackpackAndThenItsGone()
    {
        var test = new TestServer("quick", dev: true);
        ServerPlayer victim = test.Join(2, "Victim", Team.Red);
        ServerPlayer looter = test.Join(3, "Looter", Team.Blue);
        victim.Backpack = ItemCounts.None.Plus("log", 5);
        test.Server.Combat.Damage(victim, 1000, "a test");
        GameObject bag = test.State.Objects.All.Single(o => o.Kind == "dropped_backpack");
        looter.Pawn = victim.Pawn;
        looter.Backpack = ItemCounts.None.Plus("stone", 1);

        test.Server.TransferRequested(3, bag.Id, ItemCounts.StorageSlot, "stone", 1, intoStructure: true);
        Assert.Equal(1, looter.Backpack.Of("stone"));

        test.Server.TransferRequested(3, bag.Id, ItemCounts.StorageSlot, "log", 5, intoStructure: false);

        Assert.Equal(5, looter.Backpack.Of("log"));
        Assert.Null(test.State.Objects.Get(bag.Id));
        Assert.Equal(Block.Air, test.Server.World.Get(bag.Origin));
        Assert.Contains(bag.Id, test.Net.RemovedObjects);
    }

    [Fact]
    public void PlayersCantPlaceADroppedBackpack()
    {
        var test = new TestServer("practice");
        ServerPlayer player = test.Join(2, "Builder", Team.Red);
        BlockPos target = ServerTests.FreeCellBeside(test, player);

        test.Server.PlaceRequested(2, "dropped_backpack", target, 0);

        Assert.Empty(test.State.Objects.All);
    }

    [Fact]
    public void TheDeadCantBuildOrShoot()
    {
        var test = new TestServer("practice");
        ServerPlayer player = test.Join(2, "Ghost", Team.Red);
        BlockPos target = ServerTests.FreeCellBeside(test, player);
        test.Server.Combat.Damage(player, 1000, "a test");

        test.Server.EditRequested(2, new VoxelEdit(target, Block.Planks));
        test.Server.Combat.TryShoot(player, 0, 0);

        Assert.Equal(Block.Air, test.Server.World.Get(target));
        Assert.Empty(test.Net.ArrowShots);
    }

    [Fact]
    public void HealthAndRespawnSurviveTheWire()
    {
        var snapshot = new Snapshot(5, 6, Array.Empty<PlayerSnapshot>(), Array.Empty<PlaneSnapshot>(), MatchStatus.Warmup, -1, 75, 3.5f);

        Snapshot decoded = WireFormat.DecodeSnapshot(WireFormat.EncodeSnapshot(snapshot));

        Assert.Equal(75, decoded.Health);
        Assert.Equal(3.5f, decoded.RespawnIn);
    }

    /// <summary>Puts the target a few blocks north of the archer, at the same height, on a cleared floor; returns where.</summary>
    private static Vector3 StandInFront(TestServer test, ServerPlayer archer, ServerPlayer target, int blocks)
    {
        Vector3 feet = archer.Pawn.Player.Position;
        for (int z = 1; z <= blocks + 1; z++)
        {
            for (int y = 0; y < 3; y++)
            {
                test.Server.World.SetGenerated(new BlockPos((int)MathF.Floor(feet.X), (int)MathF.Floor(feet.Y) + y, (int)MathF.Floor(feet.Z) - z), Block.Air);
            }
        }

        Vector3 at = feet - new Vector3(0, 0, blocks);
        target.Pawn = PawnState.OnFoot(PlayerState.At(at));
        return at;
    }

    /// <summary>The archer shoots straight north and the arrow is given time to fly.</summary>
    private static void Shoot(TestServer test, ServerPlayer archer)
    {
        test.Server.Combat.TryShoot(archer, 0, -0.1f);
        test.Run(test.State.Data.Net.TickRate / 2);
    }
}
