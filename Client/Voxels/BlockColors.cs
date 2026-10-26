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
        Block.Ore => new Color(0.78f, 0.45f, 0.20f),
        _ => new Color(1, 0, 1), // magenta: a new block type without a colour yet
    };
}
