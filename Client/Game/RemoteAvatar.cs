using Godot;
using IslandWar.Client.Ui;
using IslandWar.Sim.Match;
using IslandWar.Sim.Players;

namespace IslandWar.Client.Game;

/// <summary>
/// Placeholder body for another player: a box the size of the collision box in their team's colour, a head
/// with a visor showing where they look, and their name.
/// </summary>
public partial class RemoteAvatar : Node3D
{
    private Node3D _head = null!;
    private StandardMaterial3D _body = null!;
    private StandardMaterial3D _headMaterial = null!;
    private Label3D _label = null!;
    private (string Name, Team Team)? _identity;

    public int PeerId { get; init; }

    public PlayerSettings Settings { get; init; } = new();

    public override void _Ready()
    {
        Name = $"Player{PeerId}";
        _body = new StandardMaterial3D();
        _headMaterial = new StandardMaterial3D();
        float bodyHeight = Settings.Height - 0.5f;
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(Settings.Width, bodyHeight, Settings.Width * 0.6f) },
            Position = new Vector3(0, bodyHeight / 2, 0),
            MaterialOverride = _body,
        });

        _head = new Node3D { Position = new Vector3(0, Settings.EyeHeight, 0) };
        AddChild(_head);
        _head.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.45f, 0.45f, 0.45f) }, MaterialOverride = _headMaterial });
        // A dark visor on the -Z side shows which way they're looking.
        _head.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.36f, 0.12f, 0.05f) },
            Position = new Vector3(0, 0.05f, -0.23f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.1f, 0.1f, 0.12f) },
        });

        _label = new Label3D
        {
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Position = new Vector3(0, Settings.Height + 0.35f, 0),
            FontSize = 48,
            OutlineSize = 12,
            PixelSize = 0.006f,
        };
        AddChild(_label);
        SetIdentity($"Player {PeerId}", Team.None);
    }

    /// <summary>Shows the player's name and team colour. Cheap to call every frame; only changes are applied.</summary>
    public void SetIdentity(string name, Team team)
    {
        if (_identity == (name, team))
        {
            return;
        }

        _identity = (name, team);
        Color colour = TeamColors.Of(team);
        _body.AlbedoColor = colour;
        _headMaterial.AlbedoColor = colour.Lightened(0.35f);
        _label.Text = name;
        _label.Modulate = colour.Lightened(0.5f);
    }

    public void ShowState(RemoteState state)
    {
        Position = new Vector3(state.Position.X, state.Position.Y, state.Position.Z);
        Rotation = new Vector3(0, state.Yaw, 0);
        _head.Rotation = new Vector3(state.Pitch, 0, 0);
    }
}
