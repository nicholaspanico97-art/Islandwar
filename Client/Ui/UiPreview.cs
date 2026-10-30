using System.Collections.Generic;
using Godot;
using IslandWar.Sim.Items;

namespace IslandWar.Client.Ui;

/// <summary>
/// A window on its own with made-up contents, for working on its layout without playing to it
/// (<c>--preview=furnace</c>). Nothing is connected: clicks do nothing.
/// </summary>
public partial class UiPreview : CanvasLayer
{
    public string Window { get; init; } = "furnace";

    public override void _Ready()
    {
        var backdrop = new ColorRect { Color = new Color(0.32f, 0.42f, 0.3f) };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(backdrop);

        ItemSettings items = ItemSettings.FromJson(FileAccess.GetFileAsString("res://data/items.json"));
        var furnace = new FurnacePanel();
        AddChild(furnace);
        furnace.Open(1);
        var slots = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [ItemCounts.InputSlot] = ItemCounts.None.Plus("copper_ingot", 9),
            [ItemCounts.Input2Slot] = ItemCounts.None.Plus("tin_ingot", 3),
            [ItemCounts.FuelSlot] = ItemCounts.None.Plus("charcoal", 6),
            [ItemCounts.OutputSlot] = ItemCounts.None.Plus("bronze_ingot", 4),
        };
        IReadOnlyDictionary<string, int> backpack = ItemCounts.None.Plus("copper_ore", 7).Plus("log", 12).Plus("charcoal", 4)
            .Plus("bronze_pickaxe", 1).Plus("bow", 1).Plus("arrow", 12).Plus("stone", 3);
        var view = new FurnaceView("Furnace", "Heating up", 0.35f, "Bronze ingot   3.5 / 10 s", 0.6f, 0.3f, 842, 1000, slots, 32);
        furnace.Show(view, backpack, items, "furnace");
    }
}
