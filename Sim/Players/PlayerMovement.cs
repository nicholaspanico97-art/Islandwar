using System.Numerics;
using IslandWar.Sim.Physics;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Players;

/// <summary>
/// Walking, jumping and flying against the block grid. The server runs this for every player and each
/// client runs it for its own player (prediction), so both get the same answer from the same inputs.
/// The player is a box: <see cref="PlayerSettings.Width"/> wide and deep, centred on the feet position.
/// </summary>
public static class PlayerMovement
{
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

        var box = new VoxelBox(settings.Width, settings.Height);
        Vector3 position = state.Position;
        bool onGround = false;
        if (box.MoveAxis(world, ref position, 1, velocity.Y * dt))
        {
            onGround = velocity.Y < 0;
            velocity.Y = 0;
        }

        if (box.MoveAxis(world, ref position, 0, velocity.X * dt))
        {
            velocity.X = 0;
        }

        if (box.MoveAxis(world, ref position, 2, velocity.Z * dt))
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

    private static Vector3 LimitLength(Vector3 v, float max)
    {
        float length = v.Length();
        return length > max ? v * (max / length) : v;
    }
}
