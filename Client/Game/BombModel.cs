using Godot;

namespace IslandWar.Client.Game;

/// <summary>A falling bomb: a dark body with tail fins, nose pointed along its flight.</summary>
public partial class BombModel : Node3D
{
    public override void _Ready()
    {
        Name = "Bomb";
        var metal = new StandardMaterial3D { AlbedoColor = new Color(0.18f, 0.2f, 0.18f) };
        // Built along -Z so LookAt points the nose where it's going.
        AddChild(new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.18f, Height = 1.1f },
            RotationDegrees = new Vector3(90, 0, 0),
            MaterialOverride = metal,
        });
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.6f, 0.04f, 0.25f) }, Position = new Vector3(0, 0, 0.5f), MaterialOverride = metal });
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.04f, 0.6f, 0.25f) }, Position = new Vector3(0, 0, 0.5f), MaterialOverride = metal });
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
