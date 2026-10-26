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

    /// <summary>Tree trunks.</summary>
    Wood = 7,

    Leaves = 8,

    /// <summary>
    /// Raw ore in the stone, dense in the mountains and sparse elsewhere. One kind for now; what it yields is
    /// decided with the resources. Natural only: players can't place it.
    /// </summary>
    Ore = 9,

    /// <summary>
    /// A block filled by a structure (see <c>GameObject</c>). Never placed or broken on its own: it comes and
    /// goes with its structure, and craters leave it standing (bombs damage the structure instead).
    /// </summary>
    Structure = 10,
}
