using System.Numerics;
using IslandWar.Sim.Physics;
using IslandWar.Sim.Players;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Vehicles;

/// <summary>Getting in and out of planes. The server decides; the client uses these to show a prompt.</summary>
public static class VehicleRules
{
    /// <summary>How far above the exit spot to look for room before giving up and using the plane's own position.</summary>
    private const int ExitSearchHeight = 16;

    public static bool InEnterRange(PlaneSettings plane, Vector3 playerFeet, PlaneState state) =>
        Vector3.DistanceSquared(playerFeet, state.Position) <= plane.EnterDistance * plane.EnterDistance;

    /// <summary>
    /// Where a pilot stands after getting out: beside the left wing root, raised until the player's box has
    /// room. In the air that's beside the plane, and the player falls.
    /// </summary>
    public static Vector3 ExitPosition(VoxelWorld world, PlayerSettings player, PlaneSettings plane, PlaneState state)
    {
        float heading = state.Heading;
        var left = new Vector3(-MathF.Cos(heading), 0, MathF.Sin(heading));
        Vector3 spot = state.Position + left * (plane.Width / 2 + player.Width);
        var box = new VoxelBox(player.Width, player.Height);
        for (int up = 0; up <= ExitSearchHeight; up++)
        {
            Vector3 candidate = spot + Vector3.UnitY * up;
            if (box.Fits(world, candidate))
            {
                return candidate;
            }
        }

        return state.Position + Vector3.UnitY * plane.Height;
    }
}
