using IslandWar.Sim.Voxels;

namespace IslandWar.Sim.Match;

/// <summary>
/// Team build protection (game plan: one griefer must not ruin a long match for everyone else). Players
/// can't break teammates' blocks by hand, and a team's own bombs don't crater its own half of the map.
/// </summary>
public static class Protection
{
    /// <summary>
    /// Whether a player may break a block by hand: natural blocks, their own, the enemy's, and blocks of
    /// teammates who aren't on the server (so an absent player's work can still be cleared away).
    /// </summary>
    /// <param name="ownerOnline">Whether the player with that id is connected right now.</param>
    public static bool MayBreak(BlockOwner owner, int breakerId, Team breakerTeam, Func<int, bool> ownerOnline) =>
        owner.IsNobody || owner.PlayerId == breakerId || owner.Team != breakerTeam || !ownerOnline(owner.PlayerId);

    /// <summary>
    /// Whether a point is on a team's own half of the map (Red the west half, Blue the east), where that
    /// team's bombs make no crater. Never true on a one-island map.
    /// </summary>
    public static bool IsHomeGround(Team team, float x, int mapSizeX, int islandCount) =>
        islandCount >= 2 && team switch
        {
            Team.Red => x < mapSizeX / 2f,
            Team.Blue => x >= mapSizeX / 2f,
            _ => false,
        };
}
