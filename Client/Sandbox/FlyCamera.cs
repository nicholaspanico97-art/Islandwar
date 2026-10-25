using Godot;
using IslandWar.Sim.Players;

namespace IslandWar.Client.Sandbox;

/// <summary>
/// Free-fly camera: mouse to look, WASD to move, Space and Ctrl for up and down, Shift for speed.
/// Click to capture the mouse, Esc to release it.
/// </summary>
public partial class FlyCamera : Camera3D
{
    private float _yaw;
    private float _pitch;

    public PlayerSettings Settings { get; init; } = new();

    public override void _Ready()
    {
        _yaw = Rotation.Y;
        _pitch = Rotation.X;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yaw -= Mathf.DegToRad(motion.Relative.X * Settings.LookSensitivity);
            _pitch = Mathf.Clamp(_pitch - Mathf.DegToRad(motion.Relative.Y * Settings.LookSensitivity), -Mathf.Pi / 2 + 0.01f, Mathf.Pi / 2 - 0.01f);
            Rotation = new Vector3(_pitch, _yaw, 0);
        }
    }

    public override void _Process(double delta)
    {
        var input = new Vector3(
            Axis(Key.D, Key.A),
            Axis(Key.Space, Key.Ctrl),
            Axis(Key.S, Key.W));
        if (input == Vector3.Zero)
        {
            return;
        }

        float speed = Settings.FlySpeed * (Input.IsKeyPressed(Key.Shift) ? 3 : 1);
        // Move relative to where the camera faces horizontally, so looking down doesn't slow you.
        Vector3 move = Basis.FromEuler(new Vector3(0, _yaw, 0)) * input;
        Position += move.Normalized() * speed * (float)delta;
    }

    private static float Axis(Key positive, Key negative) =>
        (Input.IsKeyPressed(positive) ? 1 : 0) - (Input.IsKeyPressed(negative) ? 1 : 0);
}
