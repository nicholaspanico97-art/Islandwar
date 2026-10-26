using System.Numerics;
using IslandWar.Sim.Players;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Objects;

/// <summary>Where a structure may go. Clients check before asking; the server checks again before placing.</summary>
public static class ObjectRules
{
    /// <summary>
    /// Why a structure can't go at <paramref name="origin"/>, or null if it can: every block of its footprint
    /// must be in the world, above the unbreakable layer and empty, with solid ground under at least one of
    /// them, and no player standing in the way.
    /// </summary>
    public static string? WhyNot(VoxelWorld world, PlayerSettings player, ObjectKind kind, BlockPos origin, int facing, IEnumerable<Vector3> playerFeet)
    {
        if (facing is < 0 or > 3)
        {
            return "bad facing";
        }

        BlockPos[] footprint = GameObject.FootprintOf(kind, origin, facing).ToArray();
        if (footprint.Any(b => !world.InBounds(b) || b.Y <= VoxelWorld.UnbreakableLayer))
        {
            return "out of the world";
        }

        if (footprint.Any(b => world.Get(b) != Block.Air))
        {
            return "not enough room";
        }

        if (!footprint.Any(b => b.Y == origin.Y && world.Get(b with { Y = b.Y - 1 }) != Block.Air))
        {
            return "nothing to stand on";
        }

        Vector3[] feet = playerFeet.ToArray();
        if (footprint.Any(b => feet.Any(f => BuildRules.Overlaps(player, f, b))))
        {
            return "someone is in the way";
        }

        return null;
    }

    /// <summary>
    /// The footprint origin for a structure placed against the block face a player aims at: centred on
    /// <paramref name="aimed"/> (the empty cell in front of that face) along x and z, standing on its floor.
    /// </summary>
    public static BlockPos OriginFor(ObjectKind kind, BlockPos aimed, int facing)
    {
        (int sizeX, int sizeZ) = facing % 2 == 0 ? (kind.Size.X, kind.Size.Z) : (kind.Size.Z, kind.Size.X);
        return new BlockPos(aimed.X - (sizeX - 1) / 2, aimed.Y, aimed.Z - (sizeZ - 1) / 2);
    }

    /// <summary>The quarter turn closest to a yaw (radians, 0 facing -Z, increasing to the left).</summary>
    public static int FacingFor(float yaw)
    {
        int quarter = (int)MathF.Round(yaw / (MathF.PI / 2));
        return ((quarter % 4) + 4) % 4;
    }
}
