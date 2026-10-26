using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using IslandWar.Client.Voxels;
using IslandWar.Sim.Players;
using IslandWar.Sim.Voxels;

namespace IslandWar.Client.Ui;

/// <summary>
/// Row of block slots along the bottom of the screen. Every block type a player may place gets a slot, so
/// new block types appear here automatically. Number keys pick a slot, the mouse wheel cycles.
/// </summary>
public partial class Hotbar : HBoxContainer
{
    private const float SlotSize = 56;

    private static readonly Color Frame = new(0, 0, 0, 0.55f);
    private static readonly Color Highlight = new(1, 1, 1, 0.95f);

    private readonly List<Panel> _slots = new();
    private int _selected;

    public IReadOnlyList<Block> Blocks { get; } = Enum.GetValues<Block>().Where(BuildRules.Placeable).Order().ToArray();

    public Block Selected => Blocks[_selected];

    public override void _Ready()
    {
        Name = "Hotbar";
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 6);
        for (int i = 0; i < Blocks.Count; i++)
        {
            AddChild(CreateSlot(i, Blocks[i]));
        }

        // Anchor after the slots exist so the preset can size the row, then lift it off the screen edge.
        SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom, LayoutPresetMode.Minsize, 12);
        GrowHorizontal = GrowDirection.Both;
        GrowVertical = GrowDirection.Begin;
        Refresh();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(InputActions.HotbarNext))
        {
            Select((_selected + 1) % Blocks.Count);
        }
        else if (@event.IsActionPressed(InputActions.HotbarPrevious))
        {
            Select((_selected + Blocks.Count - 1) % Blocks.Count);
        }
        else
        {
            for (int i = 0; i < Math.Min(Blocks.Count, InputActions.HotbarKeys); i++)
            {
                if (@event.IsActionPressed(InputActions.HotbarSlot(i)))
                {
                    Select(i);
                }
            }
        }
    }

    public void Select(int index)
    {
        _selected = index;
        Refresh();
    }

    private Panel CreateSlot(int index, Block block)
    {
        var slot = new Panel { CustomMinimumSize = new Vector2(SlotSize, SlotSize), MouseFilter = MouseFilterEnum.Ignore, TooltipText = block.ToString() };
        var swatch = new ColorRect { Color = BlockColors.Of(block), MouseFilter = MouseFilterEnum.Ignore };
        swatch.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, 8);
        slot.AddChild(swatch);
        var number = new Label { Text = (index + 1).ToString(), Position = new Vector2(4, 0) };
        number.AddThemeColorOverride("font_outline_color", Colors.Black);
        number.AddThemeConstantOverride("outline_size", 4);
        slot.AddChild(number);
        _slots.Add(slot);
        return slot;
    }

    private void Refresh()
    {
        for (int i = 0; i < _slots.Count; i++)
        {
            bool selected = i == _selected;
            _slots[i].AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = Frame,
                BorderColor = selected ? Highlight : Frame,
                BorderWidthBottom = 3, BorderWidthTop = 3, BorderWidthLeft = 3, BorderWidthRight = 3,
            });
        }
    }
}
