using IslandWar.Sim.Vehicles;
using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Players;

/// <summary>
/// What a player controls: their body on foot, or a plane they're flying. While flying, the body rides in
/// the seat at the plane's position.
/// </summary>
/// <param name="PlaneId">The plane being flown, or 0 on foot.</param>
/// <param name="Plane">That plane's state; unused on foot.</param>
public readonly record struct PawnState(PlayerState Player, int PlaneId, PlaneState Plane)
{
    public bool InPlane => PlaneId != 0;

    public static PawnState OnFoot(PlayerState player) => new(player, 0, default);
}

/// <summary>One tick of whatever the player controls. Getting in and out is the server's call, not this step's.</summary>
public static class Pawn
{
    public static PawnState Step(VoxelWorld world, GameRules rules, PawnState pawn, PlayerInput input, float dt)
    {
        if (!pawn.InPlane)
        {
            return pawn with { Player = PlayerMovement.Step(world, rules.Player, pawn.Player, input, dt) };
        }

        PlaneState plane = PlaneFlight.Step(world, rules.Plane, rules.SeaLevel, pawn.Plane, input, dt);
        return pawn with { Plane = plane, Player = PlayerState.At(plane.Position) };
    }
}
