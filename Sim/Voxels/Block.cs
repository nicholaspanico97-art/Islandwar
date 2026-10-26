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
    /// Retired 2026-10-26 when ore split into kinds (copper, tin, iron, coal). Never generated or placeable;
    /// the id stays taken because ids are never reused.
    /// </summary>
    Ore = 9,

    /// <summary>
    /// A block filled by a structure (see <c>GameObject</c>). Never placed or broken on its own: it comes and
    /// goes with its structure, and craters leave it standing (bombs damage the structure instead).
    /// </summary>
    Structure = 10,

    // Stone Age resources (2026-10-26). Ores are natural only: players can't place them.
    CopperOre = 11,
    TinOre = 12,
    IronOre = 13,
    Coal = 14,

    /// <summary>Found in clumps in the beach sand.</summary>
    Clay = 15,
}

/// <summary>Block names as the data files write them: the enum name in snake_case (<c>copper_ore</c>).</summary>
public static class BlockNames
{
    public static string Of(Block block) => System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(block.ToString());

    public static Block Parse(string name, string file)
    {
        foreach (Block block in Enum.GetValues<Block>())
        {
            if (Of(block) == name)
            {
                return block;
            }
        }

        throw new InvalidDataException($"{file}: '{name}' is not a block. Blocks are: {string.Join(", ", Enum.GetValues<Block>().Select(Of))}.");
    }

    /// <summary>Ores: natural blocks found in the stone, which players can mine but not place.</summary>
    public static bool IsOre(Block block) => block is Block.Ore or Block.CopperOre or Block.TinOre or Block.IronOre or Block.Coal;
}
