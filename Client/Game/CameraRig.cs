using Godot;

namespace IslandWar.Client.Game;

/// <summary>
/// The view: first person on foot, a chase camera behind the plane when flying. In the plane the mouse
/// looks around, and the view drifts back behind the tail when the mouse is still.
/// </summary>
public partial class CameraRig : Node3D
{
    private const float ChaseDistance = 13;
    private const float ChaseHeight = 3.5f;

    /// <summary>How quickly the chase camera catches up with the plane, per second.</summary>
    private const float ChaseStiffness = 8;

    /// <summary>How quickly a mouse look-around in the plane returns behind the tail, per second.</summary>
    private const float OrbitReturn = 1.2f;

    /// <summary>How fast a shake dies away, per second.</summary>
    private const float ShakeDecay = 4;

    private readonly RandomNumberGenerator _random = new();
    private Camera3D _camera = null!;
    private float _shake;
    private Vector3 _chasePosition;
    private bool _chasing;
    private float _orbitYaw;
    private float _orbitPitch;

    public override void _Ready()
    {
        Name = "CameraRig";
        _camera = new Camera3D { Fov = 80, Far = 2000 };
        AddChild(_camera);
        _camera.MakeCurrent();
    }

    public void FirstPerson(Vector3 eye, float yaw, float pitch)
    {
        _chasing = false;
        _camera.GlobalPosition = eye + ShakeOffset();
        _camera.Rotation = new Vector3(pitch, yaw, 0);
    }

    /// <summary>Shakes the view for an explosion at <paramref name="distance"/> blocks: hard up close, nothing far away.</summary>
    public void Shake(float distance, float radius)
    {
        float strength = Mathf.Clamp(1 - distance / (radius * 12), 0, 1);
        _shake = Mathf.Max(_shake, strength * 0.6f);
    }

    public override void _Process(double delta)
    {
        _shake *= Mathf.Exp(-ShakeDecay * (float)delta);
    }

    private Vector3 ShakeOffset() =>
        _shake < 0.001f ? Vector3.Zero : new Vector3(_random.RandfRange(-1, 1), _random.RandfRange(-1, 1), _random.RandfRange(-1, 1)) * _shake;

    /// <summary>Turns the chase view, in radians. Called with mouse movement while flying.</summary>
    public void Orbit(float yaw, float pitch)
    {
        _orbitYaw += yaw;
        _orbitPitch = Mathf.Clamp(_orbitPitch + pitch, -1.2f, 1.2f);
    }

    public void Chase(Vector3 planePosition, Quaternion planeRotation, double delta)
    {
        float dt = (float)delta;
        _orbitYaw *= Mathf.Exp(-OrbitReturn * dt);
        _orbitPitch *= Mathf.Exp(-OrbitReturn * dt);

        // Follow the plane's heading and half its pitch, but not its roll: a rolling horizon is hard to fly by.
        Vector3 forward = planeRotation * Vector3.Forward;
        float heading = Mathf.Atan2(-forward.X, -forward.Z) + _orbitYaw;
        float pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(forward.Y, -1, 1)) * 0.5f - 0.15f + _orbitPitch, -1.3f, 1.3f);
        Vector3 target = planePosition + Vector3.Up * 1.2f;
        Vector3 offset = new Basis(Vector3.Up, heading) * new Basis(Vector3.Right, pitch) * new Vector3(0, 0, ChaseDistance);
        Vector3 desired = target + offset + Vector3.Up * ChaseHeight;

        _chasePosition = _chasing ? _chasePosition.Lerp(desired, 1 - Mathf.Exp(-ChaseStiffness * dt)) : desired;
        _chasing = true;
        _camera.GlobalPosition = _chasePosition + ShakeOffset();
        if (!_chasePosition.IsEqualApprox(target))
        {
            _camera.LookAt(target, Vector3.Up);
        }
    }
}
