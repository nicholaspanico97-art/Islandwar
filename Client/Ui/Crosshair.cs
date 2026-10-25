using Godot;

namespace IslandWar.Client.Ui;

/// <summary>A small plus sign in the middle of the screen, outlined so it shows on sky and on sand.</summary>
public partial class Crosshair : Control
{
    private const float Arm = 9;

    public override void _Ready()
    {
        Name = "Crosshair";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
        {
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        Vector2 c = (Size / 2).Floor();
        foreach ((Color color, float width) in new[] { (new Color(0, 0, 0, 0.6f), 4f), (Colors.White, 2f) })
        {
            DrawLine(c - new Vector2(Arm, 0), c + new Vector2(Arm, 0), color, width);
            DrawLine(c - new Vector2(0, Arm), c + new Vector2(0, Arm), color, width);
        }
    }
}
