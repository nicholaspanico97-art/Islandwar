using IslandWar.Sim.Match;
using IslandWar.Sim.Protocol;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Tests;

public class ProtectionTests
{
    private static readonly BlockOwner Sam = new(1, Team.Red);
    private static readonly BlockOwner Alex = new(2, Team.Blue);
    private static bool Online(int id) => true;
    private static bool Offline(int id) => false;

    [Fact]
    public void TeammatesCantBreakEachOthersBlocksWhileTheOwnerIsOnline()
    {
        Assert.False(Protection.MayBreak(Sam, breakerId: 3, Team.Red, Online));
        Assert.True(Protection.MayBreak(Sam, breakerId: 3, Team.Red, Offline));
    }

    [Fact]
    public void OwnEnemyAndNaturalBlocksCanBeBroken()
    {
        Assert.True(Protection.MayBreak(Sam, breakerId: 1, Team.Red, Online));
        Assert.True(Protection.MayBreak(Alex, breakerId: 1, Team.Red, Online));
        Assert.True(Protection.MayBreak(default, breakerId: 1, Team.Red, Online));
    }

    [Theory]
    [InlineData(Team.Red, 10, true)]
    [InlineData(Team.Red, 200, false)]
    [InlineData(Team.Blue, 200, true)]
    [InlineData(Team.Blue, 159.9f, false)]
    public void EachTeamsHomeGroundIsItsHalfOfTheMap(Team team, float x, bool home)
    {
        Assert.Equal(home, Protection.IsHomeGround(team, x, mapSizeX: 320, islandCount: 2));
    }

    [Fact]
    public void AOneIslandMapHasNoHomeGround()
    {
        Assert.False(Protection.IsHomeGround(Team.Red, 10, mapSizeX: 128, islandCount: 1));
    }

    [Fact]
    public void PlacedBlocksRememberTheirOwnerUntilTheyreGone()
    {
        var world = new VoxelWorld(1, 1, 1);
        var p = new BlockPos(3, 3, 3);

        world.ApplyEdit(new VoxelEdit(p, Block.Planks, Sam));
        Assert.Equal(Sam, world.OwnerOf(p));

        world.ApplyEdit(new VoxelEdit(p, Block.Air));
        Assert.True(world.OwnerOf(p).IsNobody);
        Assert.Empty(world.Owners);
    }

    [Fact]
    public void TheHashCoversOwners()
    {
        var a = new VoxelWorld(1, 1, 1);
        var b = new VoxelWorld(1, 1, 1);
        a.ApplyEdit(new VoxelEdit(new BlockPos(1, 1, 1), Block.Planks, Sam));
        b.ApplyEdit(new VoxelEdit(new BlockPos(1, 1, 1), Block.Planks, Alex));

        Assert.NotEqual(a.ComputeHash(), b.ComputeHash());
    }

    [Fact]
    public void ALateJoinerGetsTheOwnersToo()
    {
        var server = new VoxelWorld(1, 1, 1);
        server.ApplyEdit(new VoxelEdit(new BlockPos(1, 1, 1), Block.Planks, Sam));
        server.ApplyEdit(new VoxelEdit(new BlockPos(2, 1, 1), Block.Stone, Alex));
        server.ApplyEdit(new VoxelEdit(new BlockPos(2, 1, 1), Block.Air));

        var joiner = new VoxelWorld(1, 1, 1);
        foreach (VoxelEdit edit in WireFormat.DecodeEdits(WireFormat.EncodeEdits(server.EditsWithOwners().ToList())))
        {
            joiner.ApplyEdit(edit);
        }

        Assert.Equal(server.ComputeHash(), joiner.ComputeHash());
        Assert.Equal(Sam, joiner.OwnerOf(new BlockPos(1, 1, 1)));
    }
}
