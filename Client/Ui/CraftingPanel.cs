using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using IslandWar.Sim.Items;

namespace IslandWar.Client.Ui;

/// <summary>
/// The crafting window: C opens what can be made by hand, E on a workbench adds what's made there. Each
/// row shows what a recipe makes and takes, and a Craft button that's off when the backpack and the team's
/// storage together don't hold enough. The server does the crafting.
/// </summary>
public partial class CraftingPanel : PanelContainer
{
    private VBoxContainer _rows = null!;
    private Label _title = null!;
    private string _shown = "";

    /// <summary>The player clicked Craft on a recipe.</summary>
    public event Action<string>? CraftClicked;

    /// <summary>Whether the window is showing.</summary>
    public bool IsOpen => Visible;

    /// <summary>The station structure it was opened on, or 0 for crafting by hand.</summary>
    public int StationId { get; private set; }

    /// <summary>The kind of station it was opened on, or null for crafting by hand.</summary>
    public string? Station { get; private set; }

    public override void _Ready()
    {
        Name = "CraftingPanel";
        Visible = false;
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.06f, 0.08f, 0.88f),
            ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = 16, ContentMarginBottom = 16,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
        });

        var layout = new VBoxContainer { CustomMinimumSize = new Vector2(520, 0) };
        layout.AddThemeConstantOverride("separation", 10);
        AddChild(layout);
        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _title.AddThemeFontSizeOverride("font_size", 22);
        layout.AddChild(_title);
        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 6);
        layout.AddChild(_rows);
        layout.AddChild(new Label
        {
            Text = "Ingredients come from your backpack, then your team's storage.  C, E or Esc to close",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(1, 1, 1, 0.6f),
        });

        SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        GrowHorizontal = GrowDirection.Both;
        GrowVertical = GrowDirection.Both;
    }

    /// <param name="station">The station's kind (null by hand) and its object id.</param>
    public void Open(string? station, int stationId)
    {
        Station = station;
        StationId = stationId;
        _shown = "";
        Visible = true;
    }

    public void Close() => Visible = false;

    /// <summary>Redraws the rows if what can be afforded changed.</summary>
    /// <param name="available">Everything the player can spend: backpack and team storage together.</param>
    /// <param name="locked">Why a recipe is locked ("Needs Mining research"), or null if our team may make it.</param>
    public void Show(string title, ItemSettings items, IReadOnlyDictionary<string, int> available, bool free, Func<Recipe, string?> locked)
    {
        Recipe[] recipes = items.Recipes.Where(r => r.ByHand || r.Station == Station).ToArray();
        string content = $"{title}|{string.Join(",", recipes.Select(r => $"{free || available.Covers(r.Takes)}{locked(r)}"))}";
        if (content == _shown)
        {
            return;
        }

        _shown = content;
        _title.Text = title;
        foreach (Node old in _rows.GetChildren())
        {
            old.QueueFree();
        }

        foreach (Recipe recipe in recipes)
        {
            string Names(IReadOnlyDictionary<string, int> counts) =>
                string.Join(", ", counts.Select(c => $"{c.Value} {items.Item(c.Key)?.Name ?? c.Key}"));
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = $"{Names(recipe.Makes)}   from {Names(recipe.Takes)}", SizeFlagsHorizontal = SizeFlags.ExpandFill });
            if (locked(recipe) is { } why)
            {
                row.AddChild(new Label { Text = why, Modulate = new Color(1, 1, 1, 0.5f) });
                _rows.AddChild(row);
                continue;
            }

            var craft = new Button { Text = "Craft", Disabled = !free && !available.Covers(recipe.Takes) };
            craft.Pressed += () => CraftClicked?.Invoke(recipe.Id);
            row.AddChild(craft);
            _rows.AddChild(row);
        }
    }
}
