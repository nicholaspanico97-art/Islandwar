namespace IslandWar.Sim.Players;

/// <summary>Player tunables. Loaded from <c>data/player.json</c>; never hardcode these.</summary>
public sealed record PlayerSettings
{
    /// <summary>How far away a player can break or place blocks, in blocks. The server enforces it too.</summary>
    public float Reach { get; init; } = 6;

    public float WalkSpeed { get; init; } = 5;

    /// <summary>Speed multiplier while sprinting (walking) or boosting (flying).</summary>
    public float SprintMultiplier { get; init; } = 1.6f;

    /// <summary>Upward speed at the start of a jump.</summary>
    public float JumpVelocity { get; init; } = 7.5f;

    /// <summary>Downward acceleration in blocks per second squared.</summary>
    public float Gravity { get; init; } = 24;

    /// <summary>Width and depth of the player's collision box.</summary>
    public float Width { get; init; } = 0.6f;

    /// <summary>Height of the player's collision box.</summary>
    public float Height { get; init; } = 1.8f;

    /// <summary>Camera height above the player's feet.</summary>
    public float EyeHeight { get; init; } = 1.6f;

    /// <summary>Flying speed (F toggles flying, which also turns off collisions).</summary>
    public float FlySpeed { get; init; } = 15;

    /// <summary>Degrees of camera turn per pixel of mouse movement.</summary>
    public float LookSensitivity { get; init; } = 0.15f;

    public static PlayerSettings FromJson(string json)
    {
        PlayerSettings settings = DataJson.Parse<PlayerSettings>(json, "player.json");
        settings.Validate();
        return settings;
    }

    public void Validate()
    {
        if (Reach <= 0 || Width <= 0 || Height <= 0 || WalkSpeed <= 0 || SprintMultiplier <= 0 || JumpVelocity <= 0 || Gravity <= 0 || EyeHeight <= 0 || FlySpeed <= 0 || LookSensitivity <= 0)
        {
            throw new InvalidDataException("Every value in player.json must be positive.");
        }

        if (EyeHeight >= Height)
        {
            throw new InvalidDataException("eye_height must be less than height.");
        }
    }
}
