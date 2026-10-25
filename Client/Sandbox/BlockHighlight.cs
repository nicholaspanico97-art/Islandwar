using Godot;
using IslandWar.Sim.Voxels;

namespace IslandWar.Client.Sandbox;

/// <summary>Dark wireframe box around the block under the crosshair.</summary>
public partial class BlockHighlight : MeshInstance3D
{
    // Slightly bigger than a block so the lines don't flicker against its faces.
    private const float Grow = 0.005f;

    public override void _Ready()
    {
        Name = "BlockHighlight";
        Visible = false;
        CastShadow = ShadowCastingSetting.Off;

        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        Vector3 min = -Vector3.One * Grow;
        Vector3 max = Vector3.One * (1 + Grow);
        Vector3[] c =
        {
            new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z), new(max.X, min.Y, max.Z), new(min.X, min.Y, max.Z),
            new(min.X, max.Y, min.Z), new(max.X, max.Y, min.Z), new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z),
        };
        int[] edges = { 0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7 };
        foreach (int i in edges)
        {
            mesh.SurfaceAddVertex(c[i]);
        }

        mesh.SurfaceEnd();
        Mesh = mesh;
        MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0, 0, 0, 0.7f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
    }

    /// <summary>Shows the box around a block, or hides it when <paramref name="block"/> is null.</summary>
    public void ShowAt(BlockPos? block)
    {
        Visible = block.HasValue;
        if (block is { } b)
        {
            Position = new Vector3(b.X, b.Y, b.Z);
        }
    }
}
