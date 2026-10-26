using Godot;
using IslandWar.Client.Voxels;
using IslandWar.Sim.Voxels;

namespace IslandWar.Client.Game;

/// <summary>Sky, sun and sea around the island.</summary>
public partial class Scenery : Node3D
{
    public VoxelWorld World { get; init; } = null!;

    public IslandSettings Terrain { get; init; } = null!;

    public override void _Ready()
    {
        Name = "Scenery";
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() },
                AmbientLightSource = Godot.Environment.AmbientSource.Sky,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
            },
        });

        var sun = new DirectionalLight3D { ShadowEnabled = true, DirectionalShadowMaxDistance = 250 };
        AddChild(sun);
        sun.RotationDegrees = new Vector3(-55, -30, 0);
        AddSea();
    }

    /// <summary>
    /// Water is a flat plane at sea level, not blocks (see docs/decisions.md). A sand-coloured floor
    /// continues the seabed past the world's edge, so the map's square boundary doesn't show through.
    /// </summary>
    private void AddSea()
    {
        float size = Mathf.Max(World.SizeX, World.SizeZ) * 16;
        var centre = new Vector3(World.SizeX / 2f, 0, World.SizeZ / 2f);

        // Just under the top face of the seabed blocks, so inside the map the blocks cover it.
        float seabedTop = Terrain.SeaLevel - Terrain.SeabedDepth + 1;
        AddChild(new MeshInstance3D
        {
            Name = "SeabedBeyondMap",
            Mesh = new PlaneMesh { Size = new Vector2(size, size) },
            Position = centre + Vector3.Up * (seabedTop - 0.02f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = BlockColors.Of(Block.Sand) },
        });

        AddChild(new MeshInstance3D
        {
            Name = "Water",
            Mesh = new PlaneMesh { Size = new Vector2(size, size) },
            Position = centre + Vector3.Up * (Terrain.SeaLevel - 0.1f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.08f, 0.25f, 0.5f, 0.85f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                Roughness = 0.2f,
            },
        });
    }
}
