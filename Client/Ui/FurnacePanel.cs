using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using IslandWar.Sim.Items;

namespace IslandWar.Client.Ui;

/// <summary>What a furnace window shows, worked out by the game from the furnace each frame.</summary>
/// <param name="Progress">How far the item smelting is, from 0 to 1.</param>
/// <param name="ProgressText">What's written on the progress bar.</param>
/// <param name="FuelLeft">How much of the item burning is left, from 0 to 1.</param>
/// <param name="Efficiency">The furnace's fuel efficiency, from 0 to 1.</param>
/// <param name="Temperature">Its temperature now, in degrees C.</param>
/// <param name="Required">The temperature what's in it needs, or null if nothing is in it.</param>
/// <param name="Slots">Its input, input2, fuel and output slots, by name.</param>
public sealed record FurnaceView(string Title, string Status, float Progress, string ProgressText, float FuelLeft, float Efficiency,
    float Temperature, float? Required, IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Slots, int SlotCapacity);

/// <summary>
/// The window that opens on a furnace (E), laid out like the developer's sketch: the title, the progress bar,
/// the two input slots, an arrow and the output; the fuel slot under the inputs with how much of the burning
/// item is left; efficiency and current and required temperature beside it; and the backpack as a grid below.
/// Click an item in the backpack to pick it up and a slot to put it in (shift-click sends it straight to the
/// slot it belongs in); click a slot to take what's in it. The server does the moving and the smelting.
/// </summary>
public partial class FurnacePanel : PanelContainer
{
    private const int SlotSize = 84;
    private const int CellSize = 60;
    private const int BackpackColumns = 10;

    private static readonly Color Hot = new(0.45f, 1, 0.45f);
    private static readonly Color Cold = new(1, 0.55f, 0.35f);

    private Label _title = null!;
    private ProgressBar _progress = null!;
    private Label _progressText = null!;
    private ItemSlot _input = null!;
    private ItemSlot _input2 = null!;
    private ItemSlot _output = null!;
    private ItemSlot _fuel = null!;
    private ProgressBar _fuelLeft = null!;
    private Label _efficiency = null!;
    private Label _current = null!;
    private Label _required = null!;
    private Label _status = null!;
    private Label _backpackHeading = null!;
    private GridContainer _backpack = null!;
    private ItemSlot _held = null!;
    private string _shownBackpack = "";
    private FurnaceView? _view;
    private IReadOnlyDictionary<string, int> _backpackItems = ItemCounts.None;
    private ItemSettings? _items;
    private string _furnaceKind = "";

    /// <summary>The item picked up from the backpack, waiting to be put in a slot.</summary>
    private (string Item, int Count)? _picked;

    /// <summary>The player moved an item: slot, item id, how many, and whether into the furnace.</summary>
    public event Action<string, string, int, bool>? TransferClicked;

    /// <summary>The furnace being shown, or null while closed.</summary>
    public int? FurnaceId { get; private set; }

    public override void _Ready()
    {
        Name = "FurnacePanel";
        Visible = false;
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.06f, 0.08f, 0.92f),
            ContentMarginLeft = 28, ContentMarginRight = 28, ContentMarginTop = 16, ContentMarginBottom = 16,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
        });

        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 12);
        AddChild(layout);

        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _title.AddThemeFontSizeOverride("font_size", 30);
        layout.AddChild(_title);

        _progress = Bar(new Color(0.95f, 0.6f, 0.2f), new Vector2(0, 26));
        _progressText = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _progressText.AddThemeColorOverride("font_outline_color", Colors.Black);
        _progressText.AddThemeConstantOverride("outline_size", 4);
        _progressText.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _progress.AddChild(_progressText);
        layout.AddChild(_progress);

        // Inputs on the left, the arrow, the output on the right.
        var machine = new HBoxContainer();
        machine.AddThemeConstantOverride("separation", 16);
        _input = Slot(ItemCounts.InputSlot, "Slot 1");
        _input2 = Slot(ItemCounts.Input2Slot, "Slot 2");
        _output = Slot(ItemCounts.OutputSlot, "Output");
        var arrow = new Label { Text = "➜", VerticalAlignment = VerticalAlignment.Center, CustomMinimumSize = new Vector2(90, 0), HorizontalAlignment = HorizontalAlignment.Center };
        arrow.AddThemeFontSizeOverride("font_size", 44);
        machine.AddChild(_input);
        machine.AddChild(_input2);
        machine.AddChild(Spacer());
        machine.AddChild(arrow);
        machine.AddChild(Spacer());
        machine.AddChild(_output);
        layout.AddChild(machine);

        // Fuel under the two inputs, the furnace's numbers bottom right.
        var lower = new HBoxContainer();
        var fuelColumn = new VBoxContainer { CustomMinimumSize = new Vector2(SlotSize * 2 + 16, 0) };
        fuelColumn.Alignment = BoxContainer.AlignmentMode.Begin;
        _fuel = Slot(ItemCounts.FuelSlot, "Fuel");
        _fuelLeft = Bar(new Color(1, 0.35f, 0.15f), new Vector2(SlotSize, 8));
        _fuel.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _fuelLeft.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        fuelColumn.AddChild(_fuel);
        fuelColumn.AddChild(_fuelLeft);
        lower.AddChild(fuelColumn);
        lower.AddChild(Spacer());
        var numbers = new VBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _efficiency = new Label();
        _current = new Label();
        _required = new Label();
        foreach (Label line in new[] { _efficiency, _current, _required })
        {
            line.AddThemeFontSizeOverride("font_size", 20);
            numbers.AddChild(line);
        }

        lower.AddChild(numbers);
        layout.AddChild(lower);

        _status = new Label { HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(1, 0.8f, 0.5f) };
        layout.AddChild(_status);

        _backpackHeading = new Label();
        layout.AddChild(_backpackHeading);
        _backpack = new GridContainer { Columns = BackpackColumns };
        _backpack.AddThemeConstantOverride("h_separation", 4);
        _backpack.AddThemeConstantOverride("v_separation", 4);
        layout.AddChild(_backpack);
        layout.AddChild(new Label
        {
            Text = "Click an item to pick it up, then a slot to put it in (shift-click: straight in). Click a slot to take it out. E or Esc closes.",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(1, 1, 1, 0.6f),
        });

        // What's been picked up follows the mouse.
        _held = new ItemSlot("", CellSize) { TopLevel = true, MouseFilter = MouseFilterEnum.Ignore, Visible = false, ZIndex = 10 };
        AddChild(_held);

        SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        GrowHorizontal = GrowDirection.Both;
        GrowVertical = GrowDirection.Both;
    }

    public override void _Process(double delta)
    {
        _held.Visible = Visible && _picked is not null;
        if (_held.Visible)
        {
            _held.GlobalPosition = GetGlobalMousePosition() + new Vector2(8, 8);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        // A right click, or a click on the window outside any slot, puts the picked item back down.
        if (@event is InputEventMouseButton { Pressed: true } && _picked is not null)
        {
            _picked = null;
        }
    }

    public void Open(int furnaceId)
    {
        FurnaceId = furnaceId;
        _picked = null;
        _shownBackpack = "";
        Visible = true;
    }

    public void Close()
    {
        FurnaceId = null;
        _picked = null;
        Visible = false;
    }

    public void Show(FurnaceView view, IReadOnlyDictionary<string, int> backpack, ItemSettings items, string furnaceKind)
    {
        _view = view;
        _items = items;
        _furnaceKind = furnaceKind;
        _title.Text = view.Title.ToUpperInvariant();
        _progress.Value = view.Progress;
        _progressText.Text = view.ProgressText;
        _fuelLeft.Value = view.FuelLeft;
        _efficiency.Text = $"Efficiency: {view.Efficiency * 100:0}%";
        _current.Text = $"Current temp: {view.Temperature:0} °C";
        _required.Text = view.Required is { } required ? $"Required temp: {required:0} °C" : "Required temp: -";
        _current.Modulate = view.Required is null ? Colors.White : view.Temperature >= view.Required ? Hot : Cold;
        _status.Text = view.Status;

        IReadOnlyDictionary<string, int> In(string slot) => view.Slots.TryGetValue(slot, out IReadOnlyDictionary<string, int>? held) ? held : ItemCounts.None;
        _input.Show(In(ItemCounts.InputSlot), items);
        _input2.Show(In(ItemCounts.Input2Slot), items);
        _fuel.Show(In(ItemCounts.FuelSlot), items);
        _output.Show(In(ItemCounts.OutputSlot), items);

        // Whatever was picked up may have gone (moved, or the backpack changed under it).
        if (_picked is { } picked && backpack.Of(picked.Item) == 0)
        {
            _picked = null;
        }
        else if (_picked is { } still)
        {
            _picked = (still.Item, Math.Min(still.Count, backpack.Of(still.Item)));
            _held.Show(ItemCounts.None.Plus(still.Item, _picked.Value.Count), items);
        }

        _backpackItems = backpack;
        string content = string.Join(",", backpack);
        if (content == _shownBackpack)
        {
            return;
        }

        _shownBackpack = content;
        _backpackHeading.Text = $"Backpack  {backpack.Total()} / {items.BackpackCapacity}";
        foreach (Node old in _backpack.GetChildren())
        {
            _backpack.RemoveChild(old);
            old.QueueFree();
        }

        foreach ((string item, int count) in backpack)
        {
            var cell = new ItemSlot("", CellSize);
            cell.Show(ItemCounts.None.Plus(item, count), items);
            cell.Pressed += () => PickUp(item);
            _backpack.AddChild(cell);
        }
    }

    private void PickUp(string item)
    {
        int count = _backpackItems.Of(item);
        if (count == 0)
        {
            return;
        }

        if (Input.IsKeyPressed(Key.Shift))
        {
            _picked = null;
            if (SlotFor(item) is { } slot)
            {
                TransferClicked?.Invoke(slot, item, count, true);
            }

            return;
        }

        _picked = _picked?.Item == item ? null : (item, count);
    }

    /// <summary>A click on one of the furnace's slots: put the picked item in, or take what's there out.</summary>
    private void SlotClicked(string slot)
    {
        if (_picked is { } picked)
        {
            _picked = null;
            if (slot != ItemCounts.OutputSlot)
            {
                TransferClicked?.Invoke(slot, picked.Item, picked.Count, true);
            }

            return;
        }

        if (_view?.Slots.GetValueOrDefault(slot) is { Count: > 0 } held)
        {
            (string item, int count) = held.First();
            TransferClicked?.Invoke(slot, item, count, false);
        }
    }

    /// <summary>
    /// Where a shift-click sends an item: fuel to the fuel slot (logs burn rather than turn to charcoal), what
    /// smelts to the input already holding it or else an empty one.
    /// </summary>
    private string? SlotFor(string item)
    {
        if (_items is null || _view is null)
        {
            return null;
        }

        if (_items.IsFuel(item))
        {
            return ItemCounts.FuelSlot;
        }

        if (_items.SmeltingFor(item) is not { } recipe || !recipe.SmeltsIn(_furnaceKind))
        {
            return null;
        }

        string[] inputs = { ItemCounts.InputSlot, ItemCounts.Input2Slot };
        IReadOnlyDictionary<string, int> In(string slot) => _view.Slots.GetValueOrDefault(slot) ?? ItemCounts.None;
        return inputs.FirstOrDefault(s => In(s).Of(item) > 0) ?? inputs.FirstOrDefault(s => In(s).Count == 0);
    }

    private ItemSlot Slot(string slot, string label)
    {
        var view = new ItemSlot(label, SlotSize);
        view.Pressed += () => SlotClicked(slot);
        return view;
    }

    private static Control Spacer() => new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };

    private static ProgressBar Bar(Color fill, Vector2 size)
    {
        var bar = new ProgressBar { MinValue = 0, MaxValue = 1, Step = 0, ShowPercentage = false, CustomMinimumSize = size };
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0.18f, 0.18f, 0.2f), BorderColor = new Color(0.6f, 0.6f, 0.6f), BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1 });
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = fill });
        return bar;
    }
}

/// <summary>
/// A square slot showing one kind of item: its colour, its name and how many, with a label underneath when
/// empty. Pressing it is the caller's business.
/// </summary>
public partial class ItemSlot : Button
{
    private readonly string _emptyLabel;
    private readonly int _size;
    private IReadOnlyDictionary<string, int> _contents = ItemCounts.None;
    private ItemSettings? _items;
    private ColorRect _swatch = null!;
    private Label _name = null!;
    private Label _count = null!;

    /// <summary>Whether to show how many; off where the count means nothing (the give window).</summary>
    public bool ShowCount { get; init; } = true;

    public ItemSlot(string emptyLabel, int size)
    {
        _emptyLabel = emptyLabel;
        _size = size;
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(_size, _size);
        FocusMode = FocusModeEnum.None;
        // A box with a light edge, like the sketch, so an empty slot still reads as a slot.
        StyleBoxFlat Box(Color fill) => new()
        {
            BgColor = fill,
            BorderColor = new Color(0.75f, 0.75f, 0.78f),
            BorderWidthBottom = 2, BorderWidthTop = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
        };
        AddThemeStyleboxOverride("normal", Box(new Color(0.16f, 0.16f, 0.18f)));
        AddThemeStyleboxOverride("hover", Box(new Color(0.24f, 0.24f, 0.27f)));
        AddThemeStyleboxOverride("pressed", Box(new Color(0.3f, 0.3f, 0.34f)));
        AddThemeStyleboxOverride("disabled", Box(new Color(0.16f, 0.16f, 0.18f)));
        _swatch = new ColorRect { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _swatch.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.KeepSize, 6);
        AddChild(_swatch);
        _name = Outlined(new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, ClipText = true });
        _name.AddThemeFontSizeOverride("font_size", _size >= 70 ? 13 : 11);
        _name.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.KeepSize, 4);
        _count = Outlined(new Label { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom });
        _count.AddThemeFontSizeOverride("font_size", 16);
        _count.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.KeepSize, 4);
        Refresh();
    }

    /// <summary>Shows the first item of <paramref name="contents"/> (a slot holds one kind), or the empty label.</summary>
    public void Show(IReadOnlyDictionary<string, int> contents, ItemSettings? items)
    {
        _contents = contents;
        _items = items;
        if (_swatch is not null)
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        IReadOnlyDictionary<string, int> contents = _contents;
        ItemSettings? items = _items;

        if (contents.Count == 0)
        {
            _swatch.Visible = false;
            _name.Text = _emptyLabel;
            _name.Modulate = new Color(1, 1, 1, 0.45f);
            _count.Text = "";
            TooltipText = "";
            return;
        }

        (string item, int count) = contents.First();
        ItemKind? kind = items?.Item(item);
        _swatch.Visible = true;
        _swatch.Color = kind is null ? Colors.Magenta : new Color(kind.Color).Darkened(0.15f);
        _name.Text = kind?.Name ?? item;
        _name.Modulate = Colors.White;
        _count.Text = ShowCount ? count.ToString() : "";
        TooltipText = $"{kind?.Name ?? item} x{count}";
    }

    private Label Outlined(Label label)
    {
        label.MouseFilter = MouseFilterEnum.Ignore;
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 4);
        AddChild(label);
        return label;
    }
}
