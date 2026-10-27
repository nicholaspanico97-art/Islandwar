using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using IslandWar.Sim.Items;

namespace IslandWar.Client.Ui;

/// <summary>
/// The window that opens on a furnace (E): your backpack on the left, with buttons to put what smelts in to
/// smelt and what burns in as fuel; the furnace's three slots on the right, each item with a Take button;
/// and a line saying what it's doing. The server does the smelting and the moving.
/// </summary>
public partial class FurnacePanel : PanelContainer
{
    private VBoxContainer _backpack = null!;
    private VBoxContainer _slots = null!;
    private Label _title = null!;
    private Label _status = null!;
    private string _shown = "";

    /// <summary>The player clicked to move an item: slot, item id, how many, and whether into the furnace.</summary>
    public event Action<string, string, int, bool>? TransferClicked;

    /// <summary>The furnace being shown, or null while closed.</summary>
    public int? FurnaceId { get; private set; }

    public override void _Ready()
    {
        Name = "FurnacePanel";
        Visible = false;
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.06f, 0.08f, 0.88f),
            ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = 16, ContentMarginBottom = 16,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
        });

        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 12);
        AddChild(layout);
        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _title.AddThemeFontSizeOverride("font_size", 22);
        layout.AddChild(_title);
        _status = new Label { HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(1, 0.8f, 0.5f) };
        layout.AddChild(_status);

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 40);
        layout.AddChild(columns);
        _backpack = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
        _slots = new VBoxContainer { CustomMinimumSize = new Vector2(280, 0) };
        columns.AddChild(_backpack);
        columns.AddChild(_slots);
        layout.AddChild(new Label { Text = "E or Esc to close", HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(1, 1, 1, 0.6f) });

        SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        GrowHorizontal = GrowDirection.Both;
        GrowVertical = GrowDirection.Both;
    }

    public void Open(int furnaceId)
    {
        FurnaceId = furnaceId;
        _shown = "";
        Visible = true;
    }

    public void Close()
    {
        FurnaceId = null;
        Visible = false;
    }

    /// <summary>Redraws the rows if anything on them changed; the status line every time.</summary>
    /// <param name="slots">The furnace's input, fuel and output, by slot name.</param>
    public void Show(string title, string status, IReadOnlyDictionary<string, int> backpack, IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> slots,
        int slotCapacity, ItemSettings items, string furnaceKind)
    {
        _status.Text = status;
        string content = $"{title}|{string.Join(",", backpack)}|{string.Join(";", slots.Values.Select(s => string.Join(",", s)))}";
        if (content == _shown)
        {
            return;
        }

        _shown = content;
        _title.Text = title;
        Clear(_backpack);
        Heading(_backpack, $"Backpack  {backpack.Total()} / {items.BackpackCapacity}");
        foreach ((string item, int count) in backpack)
        {
            bool smelts = items.SmeltingFor(item) is { } recipe && recipe.SmeltsIn(furnaceKind);
            bool burns = items.IsFuel(item);
            if (!smelts && !burns)
            {
                continue;
            }

            HBoxContainer row = Row(items, item, count);
            if (smelts)
            {
                row.AddChild(Button("Smelt", () => TransferClicked?.Invoke(ItemCounts.InputSlot, item, count, true)));
            }

            if (burns)
            {
                row.AddChild(Button("Fuel", () => TransferClicked?.Invoke(ItemCounts.FuelSlot, item, count, true)));
            }

            _backpack.AddChild(row);
        }

        if (_backpack.GetChildCount() == 1)
        {
            _backpack.AddChild(new Label { Text = "(nothing that smelts or burns)", Modulate = new Color(1, 1, 1, 0.5f) });
        }

        Clear(_slots);
        foreach ((string slot, string heading) in new[] { (ItemCounts.InputSlot, "To smelt"), (ItemCounts.FuelSlot, "Fuel"), (ItemCounts.OutputSlot, "Done") })
        {
            IReadOnlyDictionary<string, int> held = slots.TryGetValue(slot, out IReadOnlyDictionary<string, int>? found) ? found : ItemCounts.None;
            Heading(_slots, $"{heading}  {held.Total()} / {slotCapacity}");
            foreach ((string item, int count) in held)
            {
                HBoxContainer row = Row(items, item, count);
                row.AddChild(Button("Take", () => TransferClicked?.Invoke(slot, item, count, false)));
                _slots.AddChild(row);
            }
        }
    }

    private static void Clear(Container column)
    {
        foreach (Node old in column.GetChildren())
        {
            column.RemoveChild(old);
            old.QueueFree();
        }
    }

    private static void Heading(Container column, string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 18);
        column.AddChild(label);
    }

    private static HBoxContainer Row(ItemSettings items, string item, int count)
    {
        ItemKind? kind = items.Item(item);
        var row = new HBoxContainer();
        row.AddChild(new ColorRect { Color = kind is null ? Colors.Magenta : new Color(kind.Color), CustomMinimumSize = new Vector2(18, 18) });
        row.AddChild(new Label { Text = $"{kind?.Name ?? item}  {count}", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        return row;
    }

    private static Button Button(string text, Action pressed)
    {
        var button = new Button { Text = text };
        button.Pressed += pressed;
        return button;
    }
}
