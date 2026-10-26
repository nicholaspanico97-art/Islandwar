using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using IslandWar.Client.Voxels;
using IslandWar.Sim.Objects;
using IslandWar.Sim.Players;
using IslandWar.Sim.Voxels;

namespace IslandWar.Client.Ui;

/// <summary>One hotbar slot: a block to place, or a kind of structure.</summary>
public readonly record struct HotbarItem(Block Block, ObjectKind? Kind = null)
{
    public bool IsStructure => Kind is not null;

    public string Label => Kind?.Name ?? Block.ToString();

    public Color Color => Kind is { } kind ? new Color(kind.Color) : BlockColors.Of(Block);
}

/// <summary>
/// Row of slots along the bottom of the screen: every block type a player may place, then every kind of
/// structure the server offers, so new ones appear here automatically. Number keys pick a slot (0 is the
/// tenth), the mouse wheel cycles. Structure slots are marked with a frame inside the swatch.
/// </summary>
public partial class Hotbar : HBoxContainer
{
    private const float SlotSize = 56;

    private static readonly Color Frame = new(0, 0, 0, 0.55f);
    private static readonly Color Highlight = new(1, 1, 1, 0.95f);

    private readonly List<Panel> _slots = new();
    private int _selected;

    /// <summary>Kinds of structure to offer after the blocks; none in the offline sandbox.</summary>
    public IReadOnlyList<ObjectKind> Kinds { get; init; } = Array.Empty<ObjectKind>();

    /// <summary>The blocks to offer; every placeable block when null (creative).</summary>
    public IReadOnlyList<Block>? Blocks { get; init; }

    private readonly List<Label> _counts = new();
    private string _shownCounts = "";

    public IReadOnlyList<HotbarItem> Items { get; private set; } = Array.Empty<HotbarItem>();

    public HotbarItem Selected => Items[_selected];

    public override void _Ready()
    {
        Name = "Hotbar";
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 6);
        Items = (Blocks ?? Enum.GetValues<Block>().Where(BuildRules.Placeable).Order().ToArray()).Select(b => new HotbarItem(b))
            .Concat(Kinds.Select(k => new HotbarItem(Block.Structure, k)))
            .ToArray();
        for (int i = 0; i < Items.Count; i++)
        {
            AddChild(CreateSlot(i, Items[i]));
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
            Select((_selected + 1) % Items.Count);
        }
        else if (@event.IsActionPressed(InputActions.HotbarPrevious))
        {
            Select((_selected + Items.Count - 1) % Items.Count);
        }
        else
        {
            for (int i = 0; i < Math.Min(Items.Count, InputActions.HotbarKeys); i++)
            {
                if (@event.IsActionPressed(InputActions.HotbarSlot(i)))
                {
                    Select(i);
                }
            }
        }
    }

    /// <summary>
    /// How many of each slot's block the player carries, or null to show no counts (creative). A slot with
    /// none left is dimmed.
    /// </summary>
    public void ShowCounts(Func<HotbarItem, int?> count)
    {
        int?[] counts = Items.Select(count).ToArray();
        string shown = string.Join(",", counts);
        if (shown == _shownCounts)
        {
            return;
        }

        _shownCounts = shown;
        for (int i = 0; i < counts.Length; i++)
        {
            _counts[i].Text = counts[i] is { } n ? n.ToString() : "";
            _slots[i].Modulate = counts[i] == 0 ? new Color(1, 1, 1, 0.45f) : Colors.White;
        }
    }

    public void Select(int index)
    {
        _selected = index;
        Refresh();
    }

    private Panel CreateSlot(int index, HotbarItem item)
    {
        var slot = new Panel { CustomMinimumSize = new Vector2(SlotSize, SlotSize), MouseFilter = MouseFilterEnum.Ignore, TooltipText = item.Label };
        var swatch = new ColorRect { Color = item.Color, MouseFilter = MouseFilterEnum.Ignore };
        swatch.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, 8);
        slot.AddChild(swatch);
        if (item.IsStructure)
        {
            var inner = new ReferenceRect { BorderColor = new Color(1, 1, 1, 0.8f), BorderWidth = 2, EditorOnly = false, MouseFilter = MouseFilterEnum.Ignore };
            inner.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, 14);
            slot.AddChild(inner);
        }

        var number = new Label { Text = index < InputActions.HotbarKeys ? ((index + 1) % 10).ToString() : "", Position = new Vector2(4, 0) };
        number.AddThemeColorOverride("font_outline_color", Colors.Black);
        number.AddThemeConstantOverride("outline_size", 4);
        slot.AddChild(number);
        var count = new Label { HorizontalAlignment = HorizontalAlignment.Right, Position = new Vector2(4, SlotSize - 26), Size = new Vector2(SlotSize - 8, 22) };
        count.AddThemeColorOverride("font_outline_color", Colors.Black);
        count.AddThemeConstantOverride("outline_size", 4);
        slot.AddChild(count);
        _counts.Add(count);
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
