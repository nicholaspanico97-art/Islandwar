using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using IslandWar.Sim.Items;

namespace IslandWar.Client.Ui;

/// <summary>
/// The window that opens on a team storage structure (E): your backpack on the left, the storage on the
/// right, and a button on each row to move that item across, as much as fits. The server makes the move;
/// the window shows whatever it last said.
/// </summary>
public partial class StoragePanel : PanelContainer
{
    private VBoxContainer _backpack = null!;
    private VBoxContainer _stored = null!;
    private Label _title = null!;
    private string _shown = "";

    /// <summary>The player clicked to move an item: item id, how many, and whether into the storage.</summary>
    public event Action<string, int, bool>? TransferClicked;

    /// <summary>The storage structure being shown, or null while closed.</summary>
    public int? StorageId { get; private set; }

    public override void _Ready()
    {
        Name = "StoragePanel";
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

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 40);
        layout.AddChild(columns);
        _backpack = new VBoxContainer { CustomMinimumSize = new Vector2(280, 0) };
        _stored = new VBoxContainer { CustomMinimumSize = new Vector2(280, 0) };
        columns.AddChild(_backpack);
        columns.AddChild(_stored);
        layout.AddChild(new Label { Text = "E or Esc to close", HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(1, 1, 1, 0.6f) });

        SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        GrowHorizontal = GrowDirection.Both;
        GrowVertical = GrowDirection.Both;
    }

    public void Open(int storageId)
    {
        StorageId = storageId;
        _shown = "";
        Visible = true;
    }

    public void Close()
    {
        StorageId = null;
        Visible = false;
    }

    /// <summary>Redraws the rows if anything on them changed.</summary>
    public void Show(string title, IReadOnlyDictionary<string, int> backpack, int backpackCapacity,
        IReadOnlyDictionary<string, int> stored, int capacity, ItemSettings items)
    {
        string content = $"{title}|{string.Join(",", backpack)}|{string.Join(",", stored)}";
        if (content == _shown)
        {
            return;
        }

        _shown = content;
        _title.Text = title;
        Fill(_backpack, $"Backpack  {backpack.Total()} / {backpackCapacity}", backpack, items, intoStorage: true);
        Fill(_stored, $"Stored  {stored.Total()} / {capacity}", stored, items, intoStorage: false);
    }

    private void Fill(VBoxContainer column, string heading, IReadOnlyDictionary<string, int> counts, ItemSettings items, bool intoStorage)
    {
        foreach (Node old in column.GetChildren())
        {
            old.QueueFree();
        }

        var title = new Label { Text = heading };
        title.AddThemeFontSizeOverride("font_size", 18);
        column.AddChild(title);
        if (intoStorage && counts.Count > 0)
        {
            var all = new Button { Text = "Put everything in" };
            all.Pressed += () =>
            {
                foreach ((string item, int count) in counts.ToList())
                {
                    TransferClicked?.Invoke(item, count, true);
                }
            };
            column.AddChild(all);
        }

        if (counts.Count == 0)
        {
            column.AddChild(new Label { Text = "(empty)", Modulate = new Color(1, 1, 1, 0.5f) });
        }

        foreach ((string item, int count) in counts)
        {
            ItemKind? kind = items.Item(item);
            var row = new HBoxContainer();
            row.AddChild(new ColorRect { Color = kind is null ? Colors.Magenta : new Color(kind.Color), CustomMinimumSize = new Vector2(18, 18) });
            row.AddChild(new Label { Text = $"{kind?.Name ?? item}  {count}", SizeFlagsHorizontal = SizeFlags.ExpandFill });
            var move = new Button { Text = intoStorage ? "Put in" : "Take" };
            move.Pressed += () => TransferClicked?.Invoke(item, count, intoStorage);
            row.AddChild(move);
            column.AddChild(row);
        }
    }
}
