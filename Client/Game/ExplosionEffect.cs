using Godot;

namespace IslandWar.Client.Game;

/// <summary>
/// One explosion: a flash of light, a fireball that swells and fades, flying debris and rising smoke.
/// In the sea it's a white splash instead. Frees itself when the smoke has cleared.
/// </summary>
public partial class ExplosionEffect : Node3D
{
    private const float FireballSeconds = 0.7f;
    private const float LifetimeSeconds = 4;

    private MeshInstance3D _fireball = null!;
    private StandardMaterial3D _fireMaterial = null!;
    private OmniLight3D _flash = null!;
    private float _age;

    /// <summary>The crater radius in blocks; the effect scales with it.</summary>
    public float Radius { get; init; } = 4;

    public bool InWater { get; init; }

    public override void _Ready()
    {
        Name = "Explosion";
        Color fire = InWater ? new Color(0.9f, 0.95f, 1f) : new Color(1f, 0.55f, 0.15f);
        _fireMaterial = new StandardMaterial3D
        {
            AlbedoColor = fire,
            EmissionEnabled = true,
            Emission = fire,
            EmissionEnergyMultiplier = InWater ? 0.5f : 3,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _fireball = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 16, Rings = 8 },
            MaterialOverride = _fireMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Scale = Vector3.One * 0.1f,
        };
        AddChild(_fireball);

        _flash = new OmniLight3D { LightColor = fire, LightEnergy = InWater ? 2 : 12, OmniRange = Radius * 8 };
        AddChild(_flash);

        AddChild(Particles(
            amount: InWater ? 60 : 40,
            colour: InWater ? new Color(0.85f, 0.92f, 1f) : new Color(0.35f, 0.28f, 0.2f),
            size: InWater ? 0.35f : 0.4f,
            speed: Radius * 4,
            spread: InWater ? 25 : 70,
            gravity: -20,
            lifetime: 1.6f));
        if (!InWater)
        {
            AddChild(Particles(amount: 24, colour: new Color(0.55f, 0.55f, 0.55f, 0.45f), size: Radius * 0.6f, speed: 3, spread: 40, gravity: 2.5f, lifetime: LifetimeSeconds - 0.5f, round: true));
        }
    }

    public override void _Process(double delta)
    {
        _age += (float)delta;
        float t = Mathf.Clamp(_age / FireballSeconds, 0, 1);
        _fireball.Scale = Vector3.One * Mathf.Max(0.1f, Radius * 1.4f * Mathf.Sqrt(t));
        _fireMaterial.AlbedoColor = _fireMaterial.AlbedoColor with { A = 1 - t };
        _flash.LightEnergy *= Mathf.Exp(-8 * (float)delta);
        if (_age > LifetimeSeconds)
        {
            QueueFree();
        }
    }

    /// <param name="round">Soft spheres (smoke) instead of cubes (debris, spray).</param>
    private static CpuParticles3D Particles(int amount, Color colour, float size, float speed, float spread, float gravity, float lifetime, bool round = false) => new()
    {
        Amount = amount,
        OneShot = true,
        Emitting = true,
        Explosiveness = 0.95f,
        Lifetime = lifetime,
        Mesh = round ? new SphereMesh { Radius = size / 2, Height = size, RadialSegments = 8, Rings = 4 } : new BoxMesh { Size = Vector3.One * size },
        MaterialOverride = new StandardMaterial3D { AlbedoColor = colour, Transparency = colour.A < 1 ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled },
        Direction = Vector3.Up,
        Spread = spread,
        InitialVelocityMin = speed * 0.5f,
        InitialVelocityMax = speed,
        Gravity = new Vector3(0, gravity, 0),
        ScaleAmountMin = 0.5f,
        ScaleAmountMax = 1.2f,
        AngularVelocityMin = -180,
        AngularVelocityMax = 180,
    };
}
