using System;
using System.Collections.Generic;
using Godot;
using IslandWar.Sim.Items;

namespace IslandWar.Client.Ui;

/// <summary>
/// The developer-mode give window (G): every item in the game as a grid. Click one for ten in the backpack,
/// shift-click for one; the backpack's usual limit still applies. "Empty backpack" clears it. The server only
/// does it in developer mode.
/// </summary>
public partial class GivePanel : PanelContainer
{
    private const int CellSize = 60;
    private const int Columns = 10;

    private GridContainer _grid = null!;
    private Label _backpack = null!;
    private ItemSettings? _shownItems;

    /// <summary>The player asked for an item: its id and how many.</summary>
    public event Action<string, int>? GiveClicked;

    /// <summary>The player asked to empty their backpack.</summary>
    public event Action? EmptyClicked;

    public bool IsOpen => Visible;

    public override void _Ready()
    {
        Name = "GivePanel";
        Visible = false;
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.06f, 0.08f, 0.92f),
            ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = 16, ContentMarginBottom = 16,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
        });

        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 12);
        AddChild(layout);
        var title = new Label { Text = "GIVE ITEMS  (developer mode)", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 26);
        layout.AddChild(title);
        _grid = new GridContainer { Columns = Columns };
        _grid.AddThemeConstantOverride("h_separation", 4);
        _grid.AddThemeConstantOverride("v_separation", 4);
        layout.AddChild(_grid);

        var footer = new HBoxContainer();
        _backpack = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center };
        footer.AddChild(_backpack);
        var empty = new Button { Text = "Empty backpack", FocusMode = FocusModeEnum.None };
        empty.Pressed += () => EmptyClicked?.Invoke();
        footer.AddChild(empty);
        layout.AddChild(footer);
        layout.AddChild(new Label
        {
            Text = "Click an item for 10, shift-click for 1. G or Esc closes.",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(1, 1, 1, 0.6f),
        });

        SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        GrowHorizontal = GrowDirection.Both;
        GrowVertical = GrowDirection.Both;
    }

    public void Open() => Visible = true;

    public void Close() => Visible = false;

    public void Show(ItemSettings items, IReadOnlyDictionary<string, int> backpack)
    {
        _backpack.Text = $"Backpack  {backpack.Total()} / {items.BackpackCapacity}";
        if (ReferenceEquals(items, _shownItems))
        {
            return;
        }

        _shownItems = items;
        foreach (Node old in _grid.GetChildren())
        {
            _grid.RemoveChild(old);
            old.QueueFree();
        }

        foreach (ItemKind item in items.Items)
        {
            var cell = new ItemSlot("", CellSize) { ShowCount = false };
            cell.Show(ItemCounts.None.Plus(item.Id, 1), items);
            cell.Pressed += () => GiveClicked?.Invoke(item.Id, Input.IsKeyPressed(Key.Shift) ? 1 : 10);
            _grid.AddChild(cell);
        }
    }
}
