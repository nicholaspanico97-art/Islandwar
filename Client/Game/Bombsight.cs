using Godot;

namespace IslandWar.Client.Game;

/// <summary>
/// A ring on the ground where a bomb dropped now would land, sized to the crater. Only the pilot sees it.
/// Red over land, white over the sea (where a bomb does nothing).
/// </summary>
public partial class Bombsight : MeshInstance3D
{
    private StandardMaterial3D _material = null!;
    private TorusMesh _ring = null!;

    public float Radius { get; private set; } = 4;

    public override void _Ready()
    {
        Name = "Bombsight";
        Visible = false;
        CastShadow = ShadowCastingSetting.Off;
        _material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            NoDepthTest = true,
        };
        _ring = new TorusMesh { Rings = 32, RingSegments = 6 };
        Mesh = _ring;
        MaterialOverride = _material;
        SetRadius(Radius);
    }

    /// <summary>The crater size changes with the match phase.</summary>
    public void SetRadius(float radius)
    {
        Radius = radius;
        _ring.InnerRadius = Mathf.Max(0.1f, radius - 0.25f);
        _ring.OuterRadius = Mathf.Max(0.35f, radius);
    }

    /// <summary>Shows the ring at the predicted impact, or hides it (null) when there isn't one.</summary>
    public void ShowAt(Vector3? impact, bool inWater)
    {
        Visible = impact.HasValue;
        if (impact is { } point)
        {
            Position = point + Vector3.Up * 0.1f;
            _material.AlbedoColor = inWater ? new Color(1, 1, 1, 0.5f) : new Color(1, 0.15f, 0.1f, 0.85f);
        }
    }
}
