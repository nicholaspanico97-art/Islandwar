using System.Collections.Generic;
using System.Linq;
using Godot;
using IslandWar.Sim.Match;

namespace IslandWar.Client.Ui;

/// <summary>
/// Shown while Tab is held: both teams side by side with each player's ping and concede vote, and how many
/// votes a concession needs.
/// </summary>
public partial class Scoreboard : PanelContainer
{
    private readonly Dictionary<Team, VBoxContainer> _columns = new();
    private Label _footer = null!;
    private string _shown = "";

    public override void _Ready()
    {
        Name = "Scoreboard";
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0.7f),
            ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = 16, ContentMarginBottom = 16,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
        });

        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 12);
        AddChild(layout);
        var teams = new HBoxContainer();
        teams.AddThemeConstantOverride("separation", 48);
        layout.AddChild(teams);
        foreach (Team team in Teams.Playing)
        {
            var column = new VBoxContainer { CustomMinimumSize = new Vector2(260, 0) };
            teams.AddChild(column);
            _columns[team] = column;
        }

        _footer = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        layout.AddChild(_footer);

        SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        GrowHorizontal = GrowDirection.Both;
        GrowVertical = GrowDirection.Both;
    }

    public void ShowRoster(IReadOnlyCollection<RosterEntry> roster, int myPeer, float concedeFraction)
    {
        // Rebuild the rows only when something on them changed.
        string content = string.Join(";", roster.OrderBy(p => p.PeerId));
        if (content == _shown)
        {
            return;
        }

        _shown = content;
        foreach ((Team team, VBoxContainer column) in _columns)
        {
            foreach (Node old in column.GetChildren())
            {
                old.QueueFree();
            }

            RosterEntry[] members = roster.Where(p => p.Team == team).OrderBy(p => p.Name).ToArray();
            column.AddChild(Line($"{team} ({members.Length})", TeamColors.Of(team).Lightened(0.3f), 22));
            foreach (RosterEntry player in members)
            {
                string you = player.PeerId == myPeer ? "  (you)" : "";
                string vote = player.ConcedeVote ? "   votes to concede" : "";
                column.AddChild(Line($"{player.Name}{you}   {player.PingMs} ms{vote}", Colors.White, 18));
            }

            int votes = members.Count(p => p.ConcedeVote);
            if (votes > 0)
            {
                column.AddChild(Line($"Concede: {votes} of {MatchScore.ConcedeVotesNeeded(members.Length, concedeFraction)} votes", Colors.Orange, 16));
            }
        }

        _footer.Text = "F10: vote to concede (or take your vote back)";
    }

    private static Label Line(string text, Color colour, int size)
    {
        var label = new Label { Text = text, Modulate = colour };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }
}
