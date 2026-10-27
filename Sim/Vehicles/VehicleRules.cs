using System.Numerics;
using IslandWar.Sim.Physics;
using IslandWar.Sim.Players;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Vehicles;

/// <summary>Getting in and out of planes and ships. The server decides; the client uses these to show a prompt.</summary>
public static class VehicleRules
{
    /// <summary>How far above the exit spot to look for room before giving up and using the plane's own position.</summary>
    private const int ExitSearchHeight = 16;

    public static bool InEnterRange(PlaneSettings plane, Vector3 playerFeet, PlaneState state) =>
        Vector3.DistanceSquared(playerFeet, state.Position) <= plane.EnterDistance * plane.EnterDistance;

    /// <summary>Whether a player is close enough to get into a plane or aboard a ship.</summary>
    public static bool InEnterRange(GameRules rules, Vector3 playerFeet, PlaneState state)
    {
        float reach = state.IsShip ? rules.Ship(state.Ship!).EnterDistance : rules.Plane.EnterDistance;
        return Vector3.DistanceSquared(playerFeet, state.Position) <= reach * reach;
    }

    /// <summary>
    /// Where a captain stands after leaving a ship: on the nearest dry footing (land or a dock) within
    /// <paramref name="search"/> blocks of the hull, or else in the water beside it.
    /// </summary>
    public static Vector3 ShipExitPosition(VoxelWorld world, PlayerSettings player, ShipKind ship, PlaneState state, int search)
    {
        var box = new VoxelBox(player.Width, player.Height);
        int reach = (int)MathF.Ceiling(MathF.Max(ship.Width, ship.Length) / 2) + search;
        int cx = (int)MathF.Floor(state.Position.X), cz = (int)MathF.Floor(state.Position.Z);
        int sea = (int)state.Position.Y;
        Vector3? best = null;
        float bestDistance = float.MaxValue;
        for (int z = cz - reach; z <= cz + reach; z++)
        {
            for (int x = cx - reach; x <= cx + reach; x++)
            {
                if (world.HighestSolid(x, z) is not { } top || top < sea || top > sea + 3)
                {
                    continue;
                }

                var feet = new Vector3(x + 0.5f, top + 1, z + 0.5f);
                float distance = Vector2.DistanceSquared(new Vector2(feet.X, feet.Z), new Vector2(state.Position.X, state.Position.Z));
                if (distance < bestDistance && box.Fits(world, feet))
                {
                    best = feet;
                    bestDistance = distance;
                }
            }
        }

        if (best is { } dry)
        {
            return dry;
        }

        float heading = state.Heading;
        var left = new Vector3(-MathF.Cos(heading), 0, MathF.Sin(heading));
        return state.Position + left * (ship.Width / 2 + player.Width) + Vector3.UnitY * ship.Height;
    }

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
