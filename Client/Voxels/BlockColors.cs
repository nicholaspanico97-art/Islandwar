using Godot;
using IslandWar.Sim.Voxels;

namespace IslandWar.Client.Voxels;

/// <summary>Placeholder block colours until there are textures. Used by the renderer and the hotbar.</summary>
public static class BlockColors
{
    public static Color Of(Block block) => block switch
    {
        Block.Stone => new Color(0.50f, 0.50f, 0.52f),
        Block.Dirt => new Color(0.47f, 0.33f, 0.21f),
        Block.Grass => new Color(0.33f, 0.62f, 0.25f),
        Block.Sand => new Color(0.86f, 0.80f, 0.57f),
        Block.Planks => new Color(0.72f, 0.55f, 0.33f),
        Block.Wood => new Color(0.36f, 0.25f, 0.14f),
        Block.Leaves => new Color(0.18f, 0.42f, 0.16f),
        Block.CopperOre => new Color(0.80f, 0.47f, 0.24f),
        Block.TinOre => new Color(0.74f, 0.76f, 0.80f),
        Block.IronOre => new Color(0.62f, 0.42f, 0.36f),
        Block.Coal => new Color(0.16f, 0.16f, 0.17f),
        Block.Clay => new Color(0.62f, 0.64f, 0.70f),
        Block.Structure => new Color(0.25f, 0.25f, 0.27f), // hidden under the structure's own model
        _ => new Color(1, 0, 1), // magenta: a new block type without a colour yet
    };
}
