using System.Numerics;
using IslandWar.Sim.Objects;
using IslandWar.Sim.Physics;
using IslandWar.Sim.Players;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Vehicles;

/// <summary>
/// The ship model, run by the server for every ship and by the captain's client for prediction. A ship is a
/// vehicle like the plane (same state, snapshots and prediction) that stays on the sea surface: W rows
/// forward, S backs water, A/D turn. It stops dead against land, like a plane against a wall. Speed is kept
/// in the state's airspeed (negative going astern) and the throttle shows how hard the oars pull.
/// </summary>
public static class ShipSailing
{
    /// <summary>Standing still a ship still turns at this share of its turn rate (the oars pivot it).</summary>
    private const float TurnAtRest = 1f / 3;

    public static PlaneState Step(VoxelWorld world, ShipKind ship, int seaLevel, PlaneState state, PlayerInput input, float dt)
    {
        float oars = Math.Clamp(-input.MoveZ, -1, 1);
        float wanted = oars >= 0 ? oars * ship.MaxSpeed : oars * ship.ReverseSpeed;
        float speed = state.Airspeed;
        float change = ship.Acceleration * dt;
        speed = speed < wanted ? Math.Min(wanted, speed + change) : Math.Max(wanted, speed - change);

        float steer = Math.Clamp(input.MoveX, -1, 1);
        float authority = TurnAtRest + (1 - TurnAtRest) * Math.Min(1, Math.Abs(speed) / ship.MaxSpeed);
        // Backing water turns the bow the other way, as a rudder does.
        float heading = state.Heading - steer * ship.TurnRate * authority * (speed >= 0 ? 1 : -1) * dt;
        Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, heading);

        Vector3 velocity = Vector3.Transform(-Vector3.UnitZ, rotation) * speed;
        var box = new VoxelBox(ship.Width, ship.Height, seaLevel);
        Vector3 position = state.Position with { Y = seaLevel };
        bool hitX = box.MoveAxis(world, ref position, 0, velocity.X * dt);
        bool hitZ = box.MoveAxis(world, ref position, 2, velocity.Z * dt);
        if (hitX || hitZ)
        {
            speed = 0;
        }

        return new PlaneState(position, rotation, speed, 0, Math.Abs(oars), true, ship.Id);
    }

    /// <summary>
    /// The water a dock launches its ships into: the columns in front of its far end (the end facing the way
    /// it was placed), as wide as the ship's beam plus one and as long as the ship.
    /// </summary>
    public static IEnumerable<(int X, int Z)> Slip(ObjectKind dock, BlockPos origin, int facing, ShipKind ship)
    {
        (Vector2 front, Vector2 forward, Vector2 side) = Ends(dock, origin, facing);
        int halfWidth = (int)MathF.Ceiling(ship.Width / 2);
        for (int along = 1; along <= (int)MathF.Ceiling(ship.Length); along++)
        {
            for (int across = -halfWidth; across <= halfWidth; across++)
            {
                Vector2 cell = front + forward * along + side * across;
                yield return ((int)MathF.Floor(cell.X), (int)MathF.Floor(cell.Y));
            }
        }
    }

    /// <summary>Where a dock's new ship floats: in the middle of its slip, bow out to sea.</summary>
    public static PlaneState Launch(ObjectKind dock, BlockPos origin, int facing, ShipKind ship, int seaLevel)
    {
        (Vector2 front, Vector2 forward, _) = Ends(dock, origin, facing);
        Vector2 middle = front + forward * ((1 + MathF.Ceiling(ship.Length)) / 2);
        return PlaneState.Parked(new Vector3(middle.X, seaLevel, middle.Y), facing * MathF.PI / 2) with { Ship = ship.Id, OnGround = true };
    }

    /// <summary>Why a dock can't go here, or null: its slip must be open water, nothing built on it.</summary>
    public static string? WhyNoWater(VoxelWorld world, int seaLevel, ObjectKind dock, BlockPos origin, int facing, ShipKind ship)
    {
        foreach ((int x, int z) in Slip(dock, origin, facing, ship))
        {
            var surface = new BlockPos(x, seaLevel, z);
            if (!world.InBounds(surface) || world.Get(surface with { Y = seaLevel - 1 }) != Block.Air)
            {
                return "needs open water in front (place it at the shore, facing the sea)";
            }

            for (int y = seaLevel; y < seaLevel + (int)MathF.Ceiling(ship.Height); y++)
            {
                if (world.Get(surface with { Y = y }) != Block.Air)
                {
                    return "something is in the way in the water in front";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The nearest place to <paramref name="nearX"/>, <paramref name="nearZ"/> (within <paramref name="radius"/>
    /// blocks) where a dock fits on the ground facing open water, or null. For bots and tests; players aim.
    /// </summary>
    public static (BlockPos Origin, int Facing)? FindDockSite(VoxelWorld world, PlayerSettings player, int seaLevel, ObjectKind dock, ShipKind ship, int nearX, int nearZ, int radius)
    {
        (BlockPos, int)? best = null;
        int bestDistance = int.MaxValue;
        for (int z = nearZ - radius; z <= nearZ + radius; z++)
        {
            for (int x = nearX - radius; x <= nearX + radius; x++)
            {
                int distance = (x - nearX) * (x - nearX) + (z - nearZ) * (z - nearZ);
                if (distance >= bestDistance || world.HighestSolid(x, z) is not { } top || top < seaLevel)
                {
                    continue;
                }

                for (int facing = 0; facing < 4; facing++)
                {
                    var origin = new BlockPos(x, top + 1, z);
                    if (ObjectRules.WhyNot(world, player, dock, origin, facing, Array.Empty<Vector3>()) is null
                        && WhyNoWater(world, seaLevel, dock, origin, facing, ship) is null)
                    {
                        best = (origin, facing);
                        bestDistance = distance;
                        break;
                    }
                }
            }
        }

        return best;
    }

    /// <summary>
    /// The middle of a dock's far end edge (in x, z), the way it faces, and its right-hand side. Facing 0 is
    /// north (-Z), turning left a quarter at a time, like a plane's heading.
    /// </summary>
    private static (Vector2 Front, Vector2 Forward, Vector2 Side) Ends(ObjectKind dock, BlockPos origin, int facing)
    {
        (int sizeX, int sizeZ) = facing % 2 == 0 ? (dock.Size.X, dock.Size.Z) : (dock.Size.Z, dock.Size.X);
        var centre = new Vector2(origin.X + sizeX / 2f, origin.Z + sizeZ / 2f);
        Vector2 forward = facing switch
        {
            0 => new Vector2(0, -1),
            1 => new Vector2(-1, 0),
            2 => new Vector2(0, 1),
            _ => new Vector2(1, 0),
        };
        float halfLength = (facing % 2 == 0 ? sizeZ : sizeX) / 2f;
        // Half a block back from the edge, so stepping whole blocks forward lands on cell centres.
        Vector2 front = centre + forward * (halfLength - 0.5f);
        var side = new Vector2(-forward.Y, forward.X);
        return (front, forward, side);
    }
}
