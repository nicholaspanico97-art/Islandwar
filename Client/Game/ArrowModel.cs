using Godot;

namespace IslandWar.Client.Game;

/// <summary>An arrow in flight: a thin shaft with a dark head, pointed along its flight.</summary>
public partial class ArrowModel : Node3D
{
    public override void _Ready()
    {
        Name = "Arrow";

        // Built along -Z so LookAt points the head where it's going.
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.025f, Height = 0.8f },
            RotationDegrees = new Vector3(90, 0, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.78f, 0.72f, 0.6f) },
        });
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.08f, 0.08f, 0.12f) },
            Position = new Vector3(0, 0, -0.42f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.25f, 0.27f) },
        });
    }

    public void ShowState(Vector3 position, Vector3 velocity)
    {
        Position = position;
        if (velocity.LengthSquared() > 0.01f && !velocity.Normalized().Cross(Vector3.Up).IsZeroApprox())
        {
            LookAt(position + velocity, Vector3.Up);
        }
    }
}
