namespace IslandWar.Sim.Voxels;

/// <summary>
/// Block types stored in the voxel world. The numeric values go over the network and into
/// edit deltas, so only append new types; never reorder or reuse a value.
/// </summary>
public enum Block : ushort
{
    Air = 0,
    Stone = 1,
    Dirt = 2,
    Grass = 3,
    Sand = 4,
    Planks = 5,

    /// <summary>
    /// Retired 2026-10-26 with the industrial core (a nuke anywhere on the enemy island wins now). Never
    /// generated or placeable; the id stays taken because ids are never reused.
    /// </summary>
    Core = 6,
}
