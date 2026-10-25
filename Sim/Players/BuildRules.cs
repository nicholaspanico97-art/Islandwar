using System.Numerics;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Players;

/// <summary>
/// Whether a player may break or place a block. Clients check these before sending an edit, and the
/// server checks them again before applying it (from M1b).
/// </summary>
public static class BuildRules
{
    /// <summary>The bottom layer can't be broken, so nobody digs out of the world.</summary>
    public const int UnbreakableLayer = 0;

    public static bool CanBreak(VoxelWorld world, BlockPos target) =>
        target.Y > UnbreakableLayer && world.Get(target) != Block.Air;

    /// <summary>
    /// A block can go into an in-bounds air cell that no player's collision box overlaps.
    /// <paramref name="playerFeet"/> holds the feet position of every player that could be in the way.
    /// </summary>
    public static bool CanPlace(VoxelWorld world, PlayerSettings player, BlockPos target, Block block, IEnumerable<Vector3> playerFeet)
    {
        if (block == Block.Air || !world.InBounds(target) || world.Get(target) != Block.Air)
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
    public static bool Overlaps(PlayerSettings player, Vector3 feet, BlockPos block)
    {
        float half = player.Width / 2;
        return feet.X - half < block.X + 1 && feet.X + half > block.X
            && feet.Y < block.Y + 1 && feet.Y + player.Height > block.Y
            && feet.Z - half < block.Z + 1 && feet.Z + half > block.Z;
    }
}
