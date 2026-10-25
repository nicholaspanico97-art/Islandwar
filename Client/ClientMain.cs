using Godot;
using IslandWar.Client.Sandbox;

namespace IslandWar.Client;

/// <summary>
/// Client entry. Without <c>--connect</c> it starts the single-player sandbox (M1a). With it, it
/// connects to a server and shows a status label; M1b replaces the label with the networked world.
/// </summary>
public partial class ClientMain : Node
{
    private Label _status = null!;

    public LaunchArgs Args { get; init; } = new();

    public override void _Ready()
    {
        Name = "Client";

        if (Args.ConnectHost is null)
        {
            AddChild(new LocalSandbox { Args = Args });
            return;
        }

        var ui = new CanvasLayer();
        _status = new Label { Position = new Vector2(16, 16) };
        ui.AddChild(_status);
        AddChild(ui);

        var peer = new ENetMultiplayerPeer();
        Error error = peer.CreateClient(Args.ConnectHost, Args.Port);
        if (error != Error.Ok)
        {
            SetStatus($"Could not start connecting to {Args.ConnectHost}:{Args.Port}: {error}");
            return;
        }

        Multiplayer.MultiplayerPeer = peer;
        Multiplayer.ConnectedToServer += () => SetStatus($"Connected to {Args.ConnectHost}:{Args.Port} as peer {Multiplayer.GetUniqueId()}.");
        Multiplayer.ConnectionFailed += () => SetStatus($"Connection to {Args.ConnectHost}:{Args.Port} failed.");
        Multiplayer.ServerDisconnected += () => SetStatus("Server disconnected.");
        SetStatus($"Connecting to {Args.ConnectHost}:{Args.Port}...");
    }

    private void SetStatus(string text)
    {
        _status.Text = text;
        GD.Print($"[client] {text}");
    }
}
