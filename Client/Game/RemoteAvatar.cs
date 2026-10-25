using Godot;
using IslandWar.Sim.Players;

namespace IslandWar.Client.Game;

/// <summary>Placeholder body for another player: a coloured box the size of the collision box, a face and a name.</summary>
public partial class RemoteAvatar : Node3D
{
    private Node3D _head = null!;

    public int PeerId { get; init; }

    public PlayerSettings Settings { get; init; } = new();

    public override void _Ready()
    {
        Name = $"Player{PeerId}";

        // Same peer id, same colour on every client.
        var colour = Color.FromHsv((uint)PeerId * 0.618034f % 1f, 0.6f, 0.9f);
        float bodyHeight = Settings.Height - 0.5f;
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(Settings.Width, bodyHeight, Settings.Width * 0.6f) },
            Position = new Vector3(0, bodyHeight / 2, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = colour },
        });

        _head = new Node3D { Position = new Vector3(0, Settings.EyeHeight, 0) };
        AddChild(_head);
        _head.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.45f, 0.45f, 0.45f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = colour.Lightened(0.3f) },
        });
        // A dark visor on the -Z side shows which way they're looking.
        _head.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.36f, 0.12f, 0.05f) },
            Position = new Vector3(0, 0.05f, -0.23f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.1f, 0.1f, 0.12f) },
        });

        AddChild(new Label3D
        {
            Text = $"Player {PeerId}",
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Position = new Vector3(0, Settings.Height + 0.35f, 0),
            FontSize = 48,
            OutlineSize = 12,
            PixelSize = 0.006f,
        });
    }

    public void ShowState(RemoteState state)
    {
        Position = new Vector3(state.Position.X, state.Position.Y, state.Position.Z);
        Rotation = new Vector3(0, state.Yaw, 0);
        _head.Rotation = new Vector3(state.Pitch, 0, 0);
    }
}
