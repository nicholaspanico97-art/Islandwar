namespace IslandWar.Sim.Weapons;

/// <summary>Bomb tunables. Loaded from <c>data/weapons.json</c>; never hardcode these.</summary>
public sealed record BombSettings
{
    /// <summary>Downward acceleration in blocks per second squared. Matching the plane's keeps drops intuitive.</summary>
    public float Gravity { get; init; } = 15;

    /// <summary>Air drag per second, proportional to speed.</summary>
    public float Drag { get; init; } = 0.05f;

    /// <summary>
    /// Crater radius outside a match (warmup, practice, the sandbox). During a match the preset's
    /// crater_radius_by_phase decides instead.
    /// </summary>
    public int CraterRadius { get; init; } = 4;

    /// <summary>
    /// Bombs only destroy core blocks within this fraction of their crater radius: the core is hardened, so
    /// raids chip it rather than wipe it. Nukes ignore this.
    /// </summary>
    public float CoreBlastFactor { get; init; } = 0.5f;

    /// <summary>The nuke's crater radius. It ignores the core's hardening.</summary>
    public int NukeCraterRadius { get; init; } = 9;

    /// <summary>A nuke landing within this many blocks of the enemy core's footprint counts as a hit.</summary>
    public float NukeHitMargin { get; init; } = 3;

    /// <summary>Seconds between drops from the same plane.</summary>
    public float CooldownSeconds { get; init; } = 1;

    /// <summary>Bombs are released this far above the plane's wheels, under the fuselage.</summary>
    public float DropHeight { get; init; } = 0.5f;

    /// <summary>A bomb still falling after this long is removed (it can't really happen: the sea stops everything).</summary>
    public float MaxLifetimeSeconds { get; init; } = 30;

    public static BombSettings FromJson(string json)
    {
        BombSettings settings = DataJson.Parse<BombSettings>(json, "weapons.json");
        if (settings.Gravity <= 0 || settings.Drag < 0 || settings.CraterRadius <= 0 || settings.CooldownSeconds < 0
            || settings.DropHeight < 0 || settings.MaxLifetimeSeconds <= 0
            || settings.CoreBlastFactor < 0 || settings.CoreBlastFactor > 1 || settings.NukeCraterRadius <= 0 || settings.NukeHitMargin < 0)
        {
            throw new InvalidDataException("weapons.json has a value out of range.");
        }

        return settings;
    }
}
