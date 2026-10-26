using Godot;
using IslandWar.Sim.Vehicles;

namespace IslandWar.Client.Ui;

/// <summary>
/// Everything drawn over the game: crosshair, hotbar and help on foot; a flight readout in the plane;
/// and a prompt when a plane is close enough to get in.
/// </summary>
public partial class GameHud : CanvasLayer
{
    private const string OnFootHelp =
        "WASD move, Space jump, Shift sprint | Left click break, right click place, 1-5 block | E plane | Tab scores | Esc mouse";

    private const string FlyingHelp =
        "W/S pitch, A/D roll, Shift/Ctrl throttle, Space brake | Left click bomb, right click nuke | Mouse looks | E get out";

    private Crosshair _crosshair = null!;
    private Label _help = null!;
    private Label _prompt = null!;
    private Label _flight = null!;
    private Label _identity = null!;
    private Label _match = null!;
    private Label _cores = null!;
    private Label _concede = null!;

    public Scoreboard Scoreboard { get; private set; } = null!;
    private Label _bannerTitle = null!;
    private Label _bannerDetail = null!;
    private double _bannerSecondsLeft;

    public Hotbar Hotbar { get; private set; } = null!;

    public override void _Ready()
    {
        Name = "Hud";
        _crosshair = new Crosshair();
        AddChild(_crosshair);
        Hotbar = new Hotbar();
        AddChild(Hotbar);
        _help = Outlined(new Label { Position = new Vector2(12, 8) });

        _prompt = Outlined(new Label { HorizontalAlignment = HorizontalAlignment.Center, Visible = false });
        _prompt.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
        _prompt.OffsetTop = -110;
        _prompt.GrowHorizontal = Control.GrowDirection.Both;
        _prompt.AddThemeFontSizeOverride("font_size", 22);

        _identity = Outlined(new Label { HorizontalAlignment = HorizontalAlignment.Right });
        _identity.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        _identity.GrowHorizontal = Control.GrowDirection.Begin;
        _identity.OffsetRight = -16;
        _identity.OffsetTop = 8;
        _identity.AddThemeFontSizeOverride("font_size", 20);

        _match = Outlined(new Label { HorizontalAlignment = HorizontalAlignment.Center });
        _match.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        _match.GrowHorizontal = Control.GrowDirection.Both;
        _match.OffsetTop = 34;
        _match.AddThemeFontSizeOverride("font_size", 18);

        _cores = Outlined(new Label { HorizontalAlignment = HorizontalAlignment.Center });
        _cores.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        _cores.GrowHorizontal = Control.GrowDirection.Both;
        _cores.OffsetTop = 58;
        _cores.AddThemeFontSizeOverride("font_size", 18);

        _concede = Outlined(new Label { HorizontalAlignment = HorizontalAlignment.Center });
        _concede.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        _concede.GrowHorizontal = Control.GrowDirection.Both;
        _concede.OffsetTop = 82;
        _concede.Modulate = Colors.Orange;
        _concede.AddThemeFontSizeOverride("font_size", 18);

        Scoreboard = new Scoreboard();
        AddChild(Scoreboard);

        _bannerTitle = Outlined(new Label { HorizontalAlignment = HorizontalAlignment.Center, Visible = false });
        _bannerTitle.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        _bannerTitle.GrowHorizontal = Control.GrowDirection.Both;
        _bannerTitle.OffsetTop = -150;
        _bannerTitle.AddThemeFontSizeOverride("font_size", 48);
        _bannerTitle.AddThemeConstantOverride("outline_size", 10);
        _bannerDetail = Outlined(new Label { HorizontalAlignment = HorizontalAlignment.Center, Visible = false });
        _bannerDetail.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        _bannerDetail.GrowHorizontal = Control.GrowDirection.Both;
        _bannerDetail.OffsetTop = -90;
        _bannerDetail.AddThemeFontSizeOverride("font_size", 24);

        _flight = Outlined(new Label { Visible = false });
        // Anchored to the bottom-left corner and growing upward, so extra lines never run off the screen.
        _flight.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
        _flight.GrowVertical = Control.GrowDirection.Begin;
        _flight.OffsetLeft = 16;
        _flight.OffsetBottom = -16;
        _flight.OffsetTop = -16;
        _flight.AddThemeFontSizeOverride("font_size", 22);
        ShowOnFoot(null);
    }

    public override void _Process(double delta)
    {
        if (_bannerSecondsLeft > 0)
        {
            _bannerSecondsLeft -= delta;
            float fade = Mathf.Clamp((float)_bannerSecondsLeft, 0, 1);
            _bannerTitle.Modulate = _bannerTitle.Modulate with { A = fade };
            _bannerDetail.Modulate = _bannerDetail.Modulate with { A = fade };
            _bannerTitle.Visible = _bannerDetail.Visible = _bannerSecondsLeft > 0;
        }
    }

    /// <summary>The match line under the help text: stage, phase and clock.</summary>
    public void ShowMatch(string text) => _match.Text = text;

    /// <summary>Both cores' health, under the match line. Empty hides it.</summary>
    public void ShowCores(string text) => _cores.Text = text;

    /// <summary>Our team's concede votes, under the cores. Empty hides it.</summary>
    public void ShowConcede(string text) => _concede.Text = text;

    /// <summary>A big announcement in the middle of the screen for a few seconds.</summary>
    public void Announce(string title, string detail, Color colour, double seconds = 5)
    {
        _bannerTitle.Text = title;
        _bannerDetail.Text = detail;
        _bannerTitle.Modulate = colour;
        _bannerDetail.Modulate = Colors.White;
        _bannerTitle.Visible = _bannerDetail.Visible = true;
        _bannerSecondsLeft = seconds;
    }

    /// <summary>Who and which side you are, top right.</summary>
    public void ShowIdentity(string text, Color colour)
    {
        _identity.Text = text;
        _identity.Modulate = colour;
    }

    /// <param name="prompt">Text to show above the hotbar, or null for none.</param>
    public void ShowOnFoot(string? prompt)
    {
        _help.Text = OnFootHelp;
        _crosshair.Visible = true;
        Hotbar.Visible = true;
        _flight.Visible = false;
        _prompt.Visible = prompt is not null;
        _prompt.Text = prompt ?? "";
    }

    /// <param name="altitude">Height of the wheels above sea level.</param>
    /// <param name="bombStatus">One line about the bomb bay.</param>
    public void ShowFlying(PlaneState plane, PlaneSettings settings, float altitude, string bombStatus)
    {
        _help.Text = FlyingHelp;
        _crosshair.Visible = false;
        Hotbar.Visible = false;
        _prompt.Visible = false;
        _flight.Visible = true;
        bool stalling = !plane.OnGround && plane.Airspeed < settings.StallSpeed;
        _flight.Text = $"Throttle {plane.Throttle * 100:0}%\nSpeed {plane.Airspeed:0}  (stall {settings.StallSpeed:0})\nAltitude {altitude:0}\n{bombStatus}"
            + (stalling ? "\nSTALL - nose down, more throttle" : plane.OnGround ? "\nOn the ground" : "");
        _flight.Modulate = stalling ? new Color(1, 0.4f, 0.3f) : Colors.White;
    }

    private Label Outlined(Label label)
    {
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 4);
        AddChild(label);
        return label;
    }
}
