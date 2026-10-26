using System.IO;
using IslandWar.Sim;

namespace IslandWar.Client.Voxels;

/// <summary>How far and how fast clients draw the terrain. Loaded from <c>data/view.json</c>.</summary>
public sealed record ViewSettings
{
    /// <summary>godot_voxel's limit on a viewer's distance.</summary>
    public const int MaxDistance = 512;

    public int ViewDistance { get; init; } = 224;

    public int FlyingViewDistance { get; init; } = 384;

    public int ChunksPerFrame { get; init; } = 64;

    public float FogStart { get; init; } = 0.55f;

    public static ViewSettings FromJson(string json)
    {
        ViewSettings settings = DataJson.Parse<ViewSettings>(json, "view.json");
        if (settings.ViewDistance is < 32 or > MaxDistance || settings.FlyingViewDistance is < 32 or > MaxDistance)
        {
            throw new InvalidDataException($"view_distance and flying_view_distance must be between 32 and {MaxDistance}.");
        }

        if (settings.ChunksPerFrame <= 0 || settings.FogStart is <= 0 or >= 1)
        {
            throw new InvalidDataException("chunks_per_frame must be positive and fog_start between 0 and 1.");
        }

        return settings;
    }
}
