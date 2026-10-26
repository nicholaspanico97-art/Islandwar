using IslandWar.Sim.Match;

namespace IslandWar.Sim.Voxels;

/// <summary>
/// Who placed a block: the server's player id (stable for a player across reconnects) and their team. The
/// default value means nobody: natural terrain, cores, and anything placed outside a game.
/// </summary>
public readonly record struct BlockOwner(int PlayerId, Team Team)
{
    public bool IsNobody => PlayerId == 0;
}
