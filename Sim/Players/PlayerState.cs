using System.Numerics;

namespace IslandWar.Sim.Players;

/// <summary>Everything the movement step needs to continue from. <see cref="Position"/> is at the feet.</summary>
public readonly record struct PlayerState(Vector3 Position, Vector3 Velocity, bool OnGround, bool Flying)
{
    public static PlayerState At(Vector3 feet) => new(feet, Vector3.Zero, false, false);
}
