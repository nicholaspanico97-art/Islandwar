using Godot;
using IslandWar.Client.Ui;
using IslandWar.Sim.Match;

namespace IslandWar.Client.Game;

/// <summary>
/// Placeholder plane built from boxes: fuselage, wings, tail, cockpit, wheels and a propeller that spins
/// with the throttle. The node's origin is the plane's position (under the wheels), facing -Z.
/// </summary>
public partial class PlaneModel : Node3D
{
    private static readonly Color Body = new(0.36f, 0.42f, 0.26f);
    private static readonly Color Dark = new(0.15f, 0.16f, 0.14f);
    private static readonly Color Glass = new(0.25f, 0.35f, 0.45f);

    private readonly StandardMaterial3D _teamPaint = new();
    private Node3D _propeller = null!;
    private Team? _team;

    public int PlaneId { get; init; }

    public override void _Ready()
    {
        Name = $"Plane{PlaneId}";
        AddBox(new Vector3(0.9f, 0.9f, 5.4f), new Vector3(0, 1.05f, 0), Body);          // fuselage
        AddBox(new Vector3(0.95f, 0.95f, 0.5f), new Vector3(0, 1.05f, -2.85f), Dark);    // engine cowling
        AddBox(new Vector3(8.4f, 0.12f, 1.5f), new Vector3(0, 0.85f, -0.7f), Body);      // wings
        AddBox(new Vector3(3.0f, 0.1f, 0.9f), new Vector3(0, 1.2f, 2.35f), Body);        // tailplane
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.12f, 1.0f, 0.9f) }, Position = new Vector3(0, 1.7f, 2.35f), MaterialOverride = _teamPaint }); // fin
        foreach (float side in new[] { -1f, 1f })
        {
            // Wingtips in the team colour, so you can tell friend from foe in the air.
            AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.2f, 0.14f, 1.52f) }, Position = new Vector3(side * 3.65f, 0.85f, -0.7f), MaterialOverride = _teamPaint });
        }
        AddBox(new Vector3(0.7f, 0.45f, 1.1f), new Vector3(0, 1.65f, -0.4f), Glass);     // cockpit
        foreach (float side in new[] { -1f, 1f })
        {
            AddBox(new Vector3(0.08f, 0.55f, 0.08f), new Vector3(side * 0.9f, 0.55f, -1.1f), Dark);  // strut
            AddBox(new Vector3(0.2f, 0.5f, 0.5f), new Vector3(side * 0.9f, 0.25f, -1.1f), Dark);     // wheel
        }

        _propeller = new Node3D { Position = new Vector3(0, 1.05f, -3.15f) };
        AddChild(_propeller);
        _propeller.AddChild(Box(new Vector3(2.4f, 0.18f, 0.06f), Vector3.Zero, Dark));
        _propeller.AddChild(Box(new Vector3(0.18f, 2.4f, 0.06f), Vector3.Zero, Dark));
    }

    public void SetTeam(Team team)
    {
        if (_team != team)
        {
            _team = team;
            _teamPaint.AlbedoColor = TeamColors.Of(team);
        }
    }

    public void ShowState(Vector3 position, Quaternion rotation, float throttle, double delta)
    {
        Position = position;
        Quaternion = rotation.Normalized();
        // A slow idle turn so a parked plane's engine looks alive; full throttle is a blur.
        _propeller.RotateZ((float)delta * (2 + 40 * throttle));
    }

    private void AddBox(Vector3 size, Vector3 position, Color colour) => AddChild(Box(size, position, colour));

    private static MeshInstance3D Box(Vector3 size, Vector3 position, Color colour) => new()
    {
        Mesh = new BoxMesh { Size = size },
        Position = position,
        MaterialOverride = new StandardMaterial3D { AlbedoColor = colour },
    };
}
