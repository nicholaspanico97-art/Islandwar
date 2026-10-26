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
        "WASD move, Space jump, Shift sprint, F fly (Space/Ctrl up/down). Left click break, right click place. 1-5 or wheel: block. E: get in a plane. Esc frees the mouse.";

    private const string FlyingHelp =
        "W/S pitch, A/D roll (steer on the ground), Shift/Ctrl throttle, Space brake. Left click: drop a bomb. Mouse looks around. E: get out.";

    private Crosshair _crosshair = null!;
    private Label _help = null!;
    private Label _prompt = null!;
    private Label _flight = null!;

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
