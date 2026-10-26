using System.Numerics;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Players;

/// <summary>
/// Whether a player may break or place a block. Clients check these before sending an edit, and the
/// server checks them again before applying it (from M1b).
/// </summary>
public static class BuildRules
{
    public static bool CanBreak(VoxelWorld world, BlockPos target) =>
        target.Y > VoxelWorld.UnbreakableLayer && world.Get(target) is not (Block.Air or Block.Structure);

    /// <summary>Blocks a player can place: everything but air, natural-only blocks, structure blocks and retired block types.</summary>
    public static bool Placeable(Block block) => block is not (Block.Air or Block.Core or Block.Structure) && !BlockNames.IsOre(block);

    /// <summary>
    /// A block can go into an in-bounds air cell that no player's collision box overlaps.
    /// <paramref name="playerFeet"/> holds the feet position of every player that could be in the way.
    /// </summary>
    public static bool CanPlace(VoxelWorld world, PlayerSettings player, BlockPos target, Block block, IEnumerable<Vector3> playerFeet)
    {
        if (!Placeable(block) || !world.InBounds(target) || world.Get(target) != Block.Air)
        {
            return false;
        }

        return !playerFeet.Any(feet => Overlaps(player, feet, target));
    }

    /// <summary>
    /// True if the block's centre is within reach of the eye, with half a block's diagonal of slack
    /// so that anything the reach-limited raycast can hit also passes.
    /// </summary>
    public static bool InReach(PlayerSettings player, Vector3 eye, BlockPos target)
    {
        var centre = new Vector3(target.X + 0.5f, target.Y + 0.5f, target.Z + 0.5f);
        float limit = player.Reach + 0.87f;
        return Vector3.DistanceSquared(eye, centre) <= limit * limit;
    }

    /// <summary>Whether the player's box (feet at <paramref name="feet"/>, centred horizontally) overlaps a block.</summary>
    public static bool Overlaps(PlayerSettings player, Vector3 feet, BlockPos block) => OverlapsBox(feet, player.Width, player.Height, block);

    /// <summary>Whether an upright box (base centre at <paramref name="basePosition"/>) overlaps a block.</summary>
    public static bool OverlapsBox(Vector3 basePosition, float width, float height, BlockPos block)
    {
        float half = width / 2;
        return basePosition.X - half < block.X + 1 && basePosition.X + half > block.X
            && basePosition.Y < block.Y + 1 && basePosition.Y + height > block.Y
            && basePosition.Z - half < block.Z + 1 && basePosition.Z + half > block.Z;
    }
}
