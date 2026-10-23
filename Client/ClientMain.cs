using Godot;

namespace IslandWar.Client;

/// <summary>
/// Client entry. For M0 it shows a status label and, with <c>--connect</c>, connects to a server.
/// The M1a sandbox (player controller and blocks) replaces the label.
/// </summary>
public partial class ClientMain : Node
{
    private Label _status = null!;

    public LaunchArgs Args { get; init; } = new();

    public override void _Ready()
    {
        Name = "Client";

        var ui = new CanvasLayer();
        _status = new Label { Position = new Vector2(16, 16) };
        ui.AddChild(_status);
        AddChild(ui);

        if (Args.ConnectHost is null)
        {
            SetStatus("Island War client (M0). Not connected. Start with --connect=HOST:PORT to join a server.");
            return;
        }

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
