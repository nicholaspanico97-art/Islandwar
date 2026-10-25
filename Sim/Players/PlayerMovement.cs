using System.Numerics;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Players;

/// <summary>
/// Walking, jumping and flying against the block grid. The server runs this for every player and each
/// client runs it for its own player (prediction), so both get the same answer from the same inputs.
/// The player is a box: <see cref="PlayerSettings.Width"/> wide and deep, centred on the feet position.
/// </summary>
public static class PlayerMovement
{
    // Gap kept between the box and a block face after a collision, so the next step starts clear of it.
    private const float Skin = 1e-4f;

    // Shrinks the box slightly when checking overlaps, so touching a face doesn't count as overlapping it.
    private const float Epsilon = 1e-5f;

    public static PlayerState Step(VoxelWorld world, PlayerSettings settings, PlayerState state, PlayerInput input, float dt)
    {
        Vector3 wish = WishDirection(input);
        float boost = input.Has(InputButtons.Sprint) ? settings.SprintMultiplier : 1;

        if (input.Has(InputButtons.Fly))
        {
            // Flying ignores collisions (noclip). See docs/decisions.md.
            float vertical = (input.Has(InputButtons.Jump) ? 1 : 0) - (input.Has(InputButtons.Descend) ? 1 : 0);
            Vector3 fly = LimitLength(wish + Vector3.UnitY * vertical, 1) * settings.FlySpeed * boost;
            return new PlayerState(state.Position + fly * dt, fly, false, true);
        }

        var velocity = new Vector3(wish.X * settings.WalkSpeed * boost, state.Velocity.Y, wish.Z * settings.WalkSpeed * boost);
        if (state.OnGround && input.Has(InputButtons.Jump))
        {
            velocity.Y = settings.JumpVelocity;
        }
        else
        {
            // Gravity applies on the ground too: the small downward move hits the floor every step,
            // which is how the step knows the player is still standing.
            velocity.Y = MathF.Max(velocity.Y - settings.Gravity * dt, -settings.MaxFallSpeed);
        }

        Vector3 position = state.Position;
        bool onGround = false;
        if (MoveAxis(world, settings, ref position, 1, velocity.Y * dt))
        {
            onGround = velocity.Y < 0;
            velocity.Y = 0;
        }

        if (MoveAxis(world, settings, ref position, 0, velocity.X * dt))
        {
            velocity.X = 0;
        }

        if (MoveAxis(world, settings, ref position, 2, velocity.Z * dt))
        {
            velocity.Z = 0;
        }

        return new PlayerState(position, velocity, onGround, false);
    }

    /// <summary>Move input turned to face <see cref="PlayerInput.Yaw"/>, at most length 1. Matches Godot's rotation about +Y.</summary>
    public static Vector3 WishDirection(PlayerInput input)
    {
        float sin = MathF.Sin(input.Yaw);
        float cos = MathF.Cos(input.Yaw);
        var wish = new Vector3(cos * input.MoveX + sin * input.MoveZ, 0, -sin * input.MoveX + cos * input.MoveZ);
        return LimitLength(wish, 1);
    }

    /// <summary>
    /// Moves along one axis (0 = X, 1 = Y, 2 = Z), stopping against the first solid block.
    /// Moves per step are under one block, so a block can't be skipped.
    /// </summary>
    /// <returns>True if a block stopped the move.</returns>
    private static bool MoveAxis(VoxelWorld world, PlayerSettings settings, ref Vector3 position, int axis, float delta)
    {
        if (delta == 0)
        {
            return false;
        }

        Vector3 moved = position;
        Set(ref moved, axis, Get(position, axis) + delta);
        (Vector3 min, Vector3 max) = Box(settings, moved);

        int x0 = Floor(min.X + Epsilon), x1 = Floor(max.X - Epsilon);
        int y0 = Floor(min.Y + Epsilon), y1 = Floor(max.Y - Epsilon);
        int z0 = Floor(min.Z + Epsilon), z1 = Floor(max.Z - Epsilon);

        // The nearest blocking layer along the move: the lowest index when moving up, the highest when moving down.
        int? blocking = null;
        for (int y = y0; y <= y1; y++)
        {
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (world.Get(new BlockPos(x, y, z)) == Block.Air)
                    {
                        continue;
                    }

                    int layer = axis switch { 0 => x, 1 => y, _ => z };
                    blocking = blocking is null ? layer : delta > 0 ? Math.Min(blocking.Value, layer) : Math.Max(blocking.Value, layer);
                }
            }
        }

        if (blocking is null)
        {
            position = moved;
            return false;
        }

        // Rest against the face of the blocking layer. Box offsets: up is Height above the feet;
        // sideways it's half the width either way.
        float below = axis == 1 ? 0 : settings.Width / 2;
        float above = axis == 1 ? settings.Height : settings.Width / 2;
        float resting = delta > 0 ? blocking.Value - above - Skin : blocking.Value + 1 + below + Skin;
        Set(ref position, axis, resting);
        return true;
    }

    private static (Vector3 Min, Vector3 Max) Box(PlayerSettings settings, Vector3 feet)
    {
        float half = settings.Width / 2;
        return (new Vector3(feet.X - half, feet.Y, feet.Z - half), new Vector3(feet.X + half, feet.Y + settings.Height, feet.Z + half));
    }

    private static int Floor(float value) => (int)MathF.Floor(value);

    private static float Get(Vector3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    private static void Set(ref Vector3 v, int axis, float value)
    {
        switch (axis)
        {
            case 0: v.X = value; break;
            case 1: v.Y = value; break;
            default: v.Z = value; break;
        }
    }

    private static Vector3 LimitLength(Vector3 v, float max)
    {
        float length = v.Length();
        return length > max ? v * (max / length) : v;
    }
}
