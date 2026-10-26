namespace IslandWar.Sim.Vehicles;

/// <summary>Plane tunables. Loaded from <c>data/plane.json</c>; never hardcode these. Speeds in blocks per second.</summary>
public sealed record PlaneSettings
{
    /// <summary>Forward acceleration at full throttle.</summary>
    public float Thrust { get; init; } = 14;

    /// <summary>Air drag per second, proportional to speed. Top speed on the level is about thrust / drag.</summary>
    public float Drag { get; init; } = 0.3f;

    /// <summary>Hard speed cap. Must stay under the tick rate so one tick never moves more than a block.</summary>
    public float MaxSpeed { get; init; } = 50;

    /// <summary>Below this the wings can't hold the plane up and it starts to sink.</summary>
    public float StallSpeed { get; init; } = 18;

    /// <summary>Controls reach full strength at this speed and get weaker below it.</summary>
    public float ControlSpeed { get; init; } = 25;

    /// <summary>Pitch, roll and bank-turn rates in radians per second at full control strength.</summary>
    public float PitchRate { get; init; } = 1.3f;

    public float RollRate { get; init; } = 2.4f;

    /// <summary>How fast a banked plane turns: radians per second at a 90-degree bank.</summary>
    public float TurnRate { get; init; } = 1.2f;

    /// <summary>How hard gravity pulls when climbing, diving and sinking.</summary>
    public float Gravity { get; init; } = 15;

    /// <summary>Fastest sinking speed in a stall.</summary>
    public float MaxSink { get; init; } = 30;

    /// <summary>Throttle change per second while Shift or Ctrl is held (throttle runs 0 to 1).</summary>
    public float ThrottleRate { get; init; } = 0.6f;

    /// <summary>Turn rate on the ground in radians per second, reached at taxiing speed.</summary>
    public float GroundSteerRate { get; init; } = 1.0f;

    /// <summary>Deceleration from the brakes (Space) on the ground.</summary>
    public float Brake { get; init; } = 12;

    /// <summary>Deceleration from wheels on the ground with no brakes.</summary>
    public float RollingFriction { get; init; } = 1.5f;

    /// <summary>Highest nose-up angle on the ground, in radians, for the take-off rotation.</summary>
    public float MaxGroundPitch { get; init; } = 0.35f;

    /// <summary>Collision box width and depth. A box, so wings can overlap blocks a little; fine for M1.</summary>
    public float Width { get; init; } = 2;

    /// <summary>Collision box height above the wheels.</summary>
    public float Height { get; init; } = 1.4f;

    /// <summary>A player can get in from this far away (feet to the plane's base).</summary>
    public float EnterDistance { get; init; } = 4;

    public static PlaneSettings FromJson(string json)
    {
        PlaneSettings settings = DataJson.Parse<PlaneSettings>(json, "plane.json");
        settings.Validate();
        return settings;
    }

    public void Validate()
    {
        float[] positive = { Thrust, Drag, MaxSpeed, StallSpeed, ControlSpeed, PitchRate, RollRate, TurnRate, Gravity, MaxSink, ThrottleRate, GroundSteerRate, Brake, RollingFriction, MaxGroundPitch, Width, Height, EnterDistance };
        if (positive.Any(v => v <= 0))
        {
            throw new InvalidDataException("Every value in plane.json must be positive.");
        }

        if (StallSpeed >= MaxSpeed)
        {
            throw new InvalidDataException("stall_speed must be below max_speed.");
        }
    }
}
