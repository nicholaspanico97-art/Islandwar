using System.Collections.Generic;
using Godot;
using IslandWar.Client.Ui;
using IslandWar.Sim.Match;
using IslandWar.Sim.Vehicles;

namespace IslandWar.Client.Game;

/// <summary>
/// Placeholder galley built from boxes: a hull sized from the ship's kind, a raised stern, a row of oars on
/// each side that sweep with the stroke, and a mast with a sail in the team colour. The node's origin is
/// the middle of the keel at the waterline, facing -Z.
/// </summary>
public partial class ShipModel : Node3D, IVehicleModel
{
    private static readonly Color Oar = new(0.55f, 0.42f, 0.26f);
    private static readonly Color Mast = new(0.3f, 0.22f, 0.14f);

    private readonly StandardMaterial3D _teamPaint = new();
    private readonly List<Node3D> _oars = new();
    private Team? _team;
    private float _stroke;

    public int PlaneId { get; init; }

    public ShipKind Kind { get; init; } = null!;

    public override void _Ready()
    {
        Name = $"Ship{PlaneId}";
        float width = Kind.Width, length = Kind.Length;
        var hull = new Color(Kind.Color);
        // The hull sits a little below the waterline so the sea laps at it.
        AddBox(new Vector3(width, 1.0f, length), new Vector3(0, 0.3f, 0), hull);
        AddBox(new Vector3(width * 0.6f, 0.6f, length * 0.12f), new Vector3(0, 1.0f, -length * 0.47f), hull.Darkened(0.2f)); // bow
        AddBox(new Vector3(width * 0.9f, 1.0f, length * 0.18f), new Vector3(0, 1.2f, length * 0.42f), hull.Darkened(0.2f));  // stern
        AddBox(new Vector3(0.25f, 5.0f, 0.25f), new Vector3(0, 3.2f, -length * 0.08f), Mast);
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(width * 1.1f, 2.4f, 0.08f) },
            Position = new Vector3(0, 4.0f, -length * 0.08f + 0.2f),
            MaterialOverride = _teamPaint,
        });

        int perSide = Mathf.Max(2, (int)(length / 2));
        foreach (float side in new[] { -1f, 1f })
        {
            for (int i = 0; i < perSide; i++)
            {
                var pivot = new Node3D { Position = new Vector3(side * width / 2, 0.75f, -length * 0.3f + i * length * 0.6f / (perSide - 1)) };
                pivot.AddChild(Box(new Vector3(2.2f, 0.1f, 0.15f), new Vector3(side * 1.0f, -0.35f, 0), Oar));
                pivot.RotateZ(side * 0.3f);
                AddChild(pivot);
                _oars.Add(pivot);
            }
        }
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
        // The oars sweep while they pull and rest level when they don't.
        _stroke += (float)delta * 3 * throttle;
        float sweep = throttle > 0 ? 0.45f * Mathf.Sin(_stroke) : 0;
        foreach (Node3D oar in _oars)
        {
            oar.Rotation = new Vector3(0, sweep * Mathf.Sign(oar.Position.X), oar.Rotation.Z);
        }
    }

    private void AddBox(Vector3 size, Vector3 position, Color colour) => AddChild(Box(size, position, colour));

    private static MeshInstance3D Box(Vector3 size, Vector3 position, Color colour) => new()
    {
        Mesh = new BoxMesh { Size = size },
        Position = position,
        MaterialOverride = new StandardMaterial3D { AlbedoColor = colour },
    };
}

/// <summary>What the game draws a vehicle with: a plane or a ship.</summary>
public interface IVehicleModel
{
    void SetTeam(Team team);

    void ShowState(Vector3 position, Quaternion rotation, float throttle, double delta);
}
