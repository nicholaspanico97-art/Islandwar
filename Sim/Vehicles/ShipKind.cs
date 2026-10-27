namespace IslandWar.Sim.Vehicles;

/// <summary>
/// One kind of ship, from the <c>ships</c> list in <c>data/structures.json</c>. Ships are built at a dock:
/// players carry the ship's cost to the dock and it's launched into the water in front of it. Speeds in
/// blocks per second, angles in radians.
/// </summary>
public sealed record ShipKind
{
    /// <summary>Stable name used on the wire; never rename one.</summary>
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    /// <summary>What building one takes, by item id.</summary>
    public IReadOnlyDictionary<string, int> Cost { get; init; } = new Dictionary<string, int>();

    /// <summary>Top speed rowing forward (W).</summary>
    public float MaxSpeed { get; init; } = 7;

    /// <summary>Top speed backing water (S).</summary>
    public float ReverseSpeed { get; init; } = 2;

    /// <summary>How fast the speed changes towards what the oars ask for.</summary>
    public float Acceleration { get; init; } = 2.5f;

    /// <summary>Turn rate (A/D) at speed; standing still the ship turns at a third of it.</summary>
    public float TurnRate { get; init; } = 0.6f;

    /// <summary>Beam: the collision box is this wide and deep (a square, so the bow and stern can overlap the shore a little).</summary>
    public float Width { get; init; } = 3;

    /// <summary>Length from bow to stern, for drawing and for the dock's slip.</summary>
    public float Length { get; init; } = 9;

    /// <summary>Collision box height above the waterline.</summary>
    public float Height { get; init; } = 2;

    /// <summary>A player can get aboard from this far away (feet to the middle of the hull).</summary>
    public float EnterDistance { get; init; } = 5;

    public string Color { get; init; } = "#7a5230";

    public void Validate(float maxStepSpeed)
    {
        float[] positive = { MaxSpeed, ReverseSpeed, Acceleration, TurnRate, Width, Length, Height, EnterDistance };
        if (Id.Length == 0 || Name.Length == 0 || positive.Any(v => v <= 0) || Cost.Any(c => c.Value <= 0))
        {
            throw new InvalidDataException($"structures.json: ship '{Id}' needs an id, a name, positive sizes and speeds, and positive costs.");
        }

        if (MaxSpeed >= maxStepSpeed || ReverseSpeed >= maxStepSpeed)
        {
            throw new InvalidDataException($"structures.json: ship '{Id}' is faster than one block per tick.");
        }
    }
}

/// <summary>A structure that builds ships: the ships it can build (the first one, until there's a choice).</summary>
public sealed record DockSpec
{
    public IReadOnlyList<string> Ships { get; init; } = Array.Empty<string>();
}
