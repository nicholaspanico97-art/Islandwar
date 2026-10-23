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
}
