using System;
using Godot;
using IslandWar.Sim.Match;

namespace IslandWar.Client.Ui;

/// <summary>
/// The first screen: a name, a server address and a team choice, then Join, Sandbox or Quit. The name and
/// address are remembered in <c>user://settings.cfg</c> for next time.
/// </summary>
public partial class MainMenu : Control
{
    private const string SettingsPath = "user://settings.cfg";

    private LineEdit _name = null!;
    private LineEdit _address = null!;
    private OptionButton _team = null!;
    private Label _status = null!;

    /// <summary>Join pressed with a valid address: host, port, name and requested team.</summary>
    public event Action<string, int, string, Team>? JoinRequested;

    public event Action? SandboxRequested;

    public override void _Ready()
    {
        Name = "MainMenu";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(new ColorRect { Color = new Color(0.1f, 0.14f, 0.2f), AnchorRight = 1, AnchorBottom = 1 });

        var panel = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0) };
        panel.AddThemeConstantOverride("separation", 10);
        var centre = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
        centre.AddChild(panel);
        AddChild(centre);

        var title = new Label { Text = "Island War", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 42);
        panel.AddChild(title);

        var saved = new ConfigFile();
        saved.Load(SettingsPath);
        _name = Field(panel, "Name", saved.GetValue("player", "name", "").AsString(), "Your name");
        _address = Field(panel, "Server", saved.GetValue("player", "address", "127.0.0.1:7777").AsString(), "host:port");

        panel.AddChild(new Label { Text = "Team" });
        _team = new OptionButton();
        _team.AddItem("Whichever needs players", 0);
        _team.AddItem("Red (west island)", (int)Team.Red);
        _team.AddItem("Blue (east island)", (int)Team.Blue);
        panel.AddChild(_team);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 10);
        Button join = AddButton(buttons, "Join", OnJoin);
        AddButton(buttons, "Sandbox", () => SandboxRequested?.Invoke());
        AddButton(buttons, "Quit", () => GetTree().Quit());
        panel.AddChild(buttons);

        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        panel.AddChild(_status);

        _name.TextSubmitted += _ => OnJoin();
        _address.TextSubmitted += _ => OnJoin();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        (string.IsNullOrWhiteSpace(_name.Text) ? (Control)_name : join).GrabFocus();
    }

    public void ShowStatus(string text) => _status.Text = text;

    private void OnJoin()
    {
        LaunchArgs address;
        try
        {
            address = LaunchArgs.Parse(new[] { $"--connect={_address.Text.Trim()}" });
        }
        catch (ArgumentException e)
        {
            ShowStatus(e.Message);
            return;
        }

        var saved = new ConfigFile();
        saved.Load(SettingsPath);
        saved.SetValue("player", "name", _name.Text.Trim());
        saved.SetValue("player", "address", _address.Text.Trim());
        saved.Save(SettingsPath);
        JoinRequested?.Invoke(address.ConnectHost!, address.Port, _name.Text.Trim(), (Team)_team.GetSelectedId());
    }

    private static LineEdit Field(Container parent, string label, string value, string placeholder)
    {
        parent.AddChild(new Label { Text = label });
        var edit = new LineEdit { Text = value, PlaceholderText = placeholder, MaxLength = 64 };
        parent.AddChild(edit);
        return edit;
    }

    private static Button AddButton(Container parent, string text, Action pressed)
    {
        var button = new Button { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }
}
