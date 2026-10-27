using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using IslandWar.Sim.Items;
using IslandWar.Sim.Match;
using IslandWar.Sim.Objects;
using IslandWar.Sim.Research;
using IslandWar.Sim.Voxels;

namespace IslandWar.Client.Ui;

/// <summary>
/// The research window (R): our team's era and points, what it takes to reach the next era, and every tech of
/// the eras we've reached with what it costs, what it unlocks and a Research button. The server decides.
/// </summary>
public partial class ResearchPanel : PanelContainer
{
    private VBoxContainer _rows = null!;
    private Label _title = null!;
    private Label _goal = null!;
    private string _shown = "";

    /// <summary>The player clicked Research on a tech, by id.</summary>
    public event Action<string>? ResearchClicked;

    public bool IsOpen => Visible;

    public override void _Ready()
    {
        Name = "ResearchPanel";
        Visible = false;
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.06f, 0.08f, 0.9f),
            ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = 16, ContentMarginBottom = 16,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
        });

        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 10);
        AddChild(layout);
        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _title.AddThemeFontSizeOverride("font_size", 22);
        layout.AddChild(_title);
        _goal = new Label { HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(1, 0.8f, 0.5f) };
        layout.AddChild(_goal);
        _rows = new VBoxContainer { CustomMinimumSize = new Vector2(720, 0) };
        _rows.AddThemeConstantOverride("separation", 6);
        layout.AddChild(_rows);
        layout.AddChild(new Label { Text = "R or Esc to close", HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(1, 1, 1, 0.6f) });

        SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        GrowHorizontal = GrowDirection.Both;
        GrowVertical = GrowDirection.Both;
    }

    public void Open()
    {
        _shown = "";
        Visible = true;
    }

    public void Close() => Visible = false;

    /// <summary>Redraws if the team's research or what it can afford changed.</summary>
    /// <param name="available">Everything the player can spend: backpack and team storage together.</param>
    public void Show(TechTree tree, TeamState team, IReadOnlyDictionary<string, int> available, ItemSettings items, ObjectKinds kinds)
    {
        Era era = tree.EraOf(team);
        Tech[] techs = tree.Techs.Where(t => tree.EraIndex(t.Era) <= team.Era).ToArray();
        string content = $"{team.Era}|{string.Join(",", team.Points)}|{string.Join(",", team.Researched)}|"
                         + string.Join(",", techs.Select(t => available.Covers(t.Costs)));
        if (content == _shown)
        {
            return;
        }

        _shown = content;
        _title.Text = $"{team.Team} team research: {era.Name}";
        _goal.Text = Goal(tree, team);
        foreach (Node old in _rows.GetChildren())
        {
            _rows.RemoveChild(old);
            old.QueueFree();
        }

        foreach (Era reached in tree.Eras.Take(team.Era + 1))
        {
            var heading = new Label { Text = $"{reached.Name}   {team.Points.Of(reached.Id)} points   ({Earning(reached, items)})" };
            heading.AddThemeFontSizeOverride("font_size", 18);
            _rows.AddChild(heading);
            foreach (Tech tech in techs.Where(t => t.Era == reached.Id))
            {
                _rows.AddChild(Row(tree, team, tech, available, items, kinds));
            }
        }
    }

    private HBoxContainer Row(TechTree tree, TeamState team, Tech tech, IReadOnlyDictionary<string, int> available, ItemSettings items, ObjectKinds kinds)
    {
        string costs = string.Join("", tech.Costs.Select(c => $", {c.Value} {items.Item(c.Key)?.Name.ToLowerInvariant() ?? c.Key}"));
        var row = new HBoxContainer();
        row.AddChild(new Label
        {
            Text = $"{tech.Name}  ({tech.Points} points{costs}):  {TechTree.Describe(tech.Unlocks, items, kinds)}",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        });
        string? why = tree.WhyNot(team, tech) ?? (available.Covers(tech.Costs) ? null : "not enough items");
        if (team.HasResearched(tech.Id))
        {
            row.AddChild(new Label { Text = "Researched", Modulate = new Color(0.5f, 1, 0.5f) });
        }
        else if (why is not null)
        {
            row.AddChild(new Label { Text = why, Modulate = new Color(1, 1, 1, 0.5f) });
        }
        else
        {
            var button = new Button { Text = "Research" };
            button.Pressed += () => ResearchClicked?.Invoke(tech.Id);
            row.AddChild(button);
        }

        return row;
    }

    /// <summary>What takes the team on to the next era, or that it's as far as the game goes for now.</summary>
    private static string Goal(TechTree tree, TeamState team)
    {
        Era era = tree.EraOf(team);
        if (era.Advance.Count == 0 || team.Era >= tree.Eras.Count - 1)
        {
            return $"The {era.Name} is as far as research goes for now.";
        }

        string[] left = era.Advance.Where(t => !team.HasResearched(t)).Select(t => tree.Tech(t)?.Name ?? t).ToArray();
        return $"To reach the {tree.Eras[team.Era + 1].Name}: research {string.Join(" and ", left)}";
    }

    /// <summary>What earns an era's points, in words.</summary>
    private static string Earning(Era era, ItemSettings items)
    {
        var parts = new List<string>();
        foreach ((string action, IReadOnlyDictionary<string, int> sources) in era.Points)
        {
            foreach ((string what, int points) in sources)
            {
                // Blocks by the item that places them: wood is a log.
                string? itemId = action == PointSource.Smelt ? what : items.ItemPlacing(BlockNames.Parse(what, "techs.json"))?.Id;
                string name = items.Item(itemId ?? "")?.Name.ToLowerInvariant() ?? what.Replace('_', ' ');
                parts.Add($"{action} {name}: {points}");
            }
        }

        return parts.Count == 0 ? "nothing earns these yet" : string.Join(", ", parts);
    }
}
