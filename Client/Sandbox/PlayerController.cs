using Godot;
using IslandWar.Sim.Players;

namespace IslandWar.Client.Sandbox;

/// <summary>
/// First-person player: walk, sprint, jump, and F to toggle flying (which turns collisions off).
/// The node's origin is at the player's feet. The camera is top-level and follows the interpolated
/// body each frame, so mouse look responds every rendered frame even though movement runs at the
/// physics tick rate.
/// </summary>
public partial class PlayerController : CharacterBody3D
{
    private CollisionShape3D _shape = null!;
    private float _yaw;
    private float _pitch;

    public PlayerSettings Settings { get; init; } = new();

    public Camera3D Camera { get; private set; } = null!;

    public bool Flying { get; private set; }

    /// <summary>While frozen the player doesn't move or fall. Used until the ground under the spawn has collision.</summary>
    public bool Frozen { get; set; } = true;

    public Vector3 EyePosition => GlobalPosition + Vector3.Up * Settings.EyeHeight;

    public Vector3 LookDirection => -Camera.GlobalBasis.Z;

    public override void _Ready()
    {
        Name = "Player";
        _shape = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(Settings.Width, Settings.Height, Settings.Width) },
            Position = new Vector3(0, Settings.Height / 2, 0),
        };
        AddChild(_shape);

        Camera = new Camera3D { TopLevel = true, Fov = 80 };
        AddChild(Camera);
        Camera.MakeCurrent();
        UpdateCamera();
    }

    /// <summary>Moves the player to a spot instantly, facing the given yaw (radians).</summary>
    public void Teleport(Vector3 feet, float yaw)
    {
        GlobalPosition = feet;
        Velocity = Vector3.Zero;
        _yaw = yaw;
        _pitch = 0;
        ResetPhysicsInterpolation();
        UpdateCamera();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yaw -= Mathf.DegToRad(motion.Relative.X * Settings.LookSensitivity);
            _pitch = Mathf.Clamp(_pitch - Mathf.DegToRad(motion.Relative.Y * Settings.LookSensitivity), -Mathf.Pi / 2 + 0.01f, Mathf.Pi / 2 - 0.01f);
        }
        else if (@event.IsActionPressed(InputActions.ToggleFly))
        {
            Flying = !Flying;
            _shape.Disabled = Flying;
            Velocity = Vector3.Zero;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Frozen)
        {
            return;
        }

        float dt = (float)delta;
        Vector2 input = Input.GetVector(InputActions.MoveLeft, InputActions.MoveRight, InputActions.MoveForward, InputActions.MoveBack);
        Vector3 wish = Basis.FromEuler(new Vector3(0, _yaw, 0)) * new Vector3(input.X, 0, input.Y);
        float boost = Input.IsActionPressed(InputActions.Sprint) ? Settings.SprintMultiplier : 1;

        if (Flying)
        {
            float vertical = (Input.IsActionPressed(InputActions.Jump) ? 1 : 0) - (Input.IsActionPressed(InputActions.Descend) ? 1 : 0);
            Velocity = (wish + Vector3.Up * vertical).LimitLength(1) * Settings.FlySpeed * boost;
            GlobalPosition += Velocity * dt;
            return;
        }

        Vector3 velocity = Velocity;
        velocity.X = wish.X * Settings.WalkSpeed * boost;
        velocity.Z = wish.Z * Settings.WalkSpeed * boost;
        if (IsOnFloor())
        {
            // Holding jump keeps hopping, which makes climbing a staircase of blocks less fiddly.
            velocity.Y = Input.IsActionPressed(InputActions.Jump) ? Settings.JumpVelocity : 0;
        }
        else
        {
            velocity.Y -= Settings.Gravity * dt;
        }

        Velocity = velocity;
        MoveAndSlide();
    }

    public override void _Process(double delta)
    {
        UpdateCamera();
    }

    private void UpdateCamera()
    {
        Camera.GlobalPosition = GetGlobalTransformInterpolated().Origin + Vector3.Up * Settings.EyeHeight;
        Camera.Rotation = new Vector3(_pitch, _yaw, 0);
    }
}
