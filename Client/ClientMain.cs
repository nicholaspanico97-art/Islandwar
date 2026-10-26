using Godot;
using IslandWar.Client.Bots;
using IslandWar.Client.Game;
using IslandWar.Client.Online;
using IslandWar.Client.Sandbox;
using IslandWar.Net;
using IslandWar.Sim.Players;

namespace IslandWar.Client;

/// <summary>
/// Client entry. Without <c>--connect</c> it starts the single-player sandbox. With it, it connects to a
/// server, waits for the world, and then plays the same <see cref="ClientGame"/> with a network link.
/// </summary>
public partial class ClientMain : Node
{
    private Label _status = null!;
    private NetClient _net = null!;

    public LaunchArgs Args { get; init; } = new();

    public override void _Ready()
    {
        // Same name as the server's root node, so RPC paths match (see Replicator).
        Name = "Game";
        InputActions.Register();

        if (Args.ConnectHost is null)
        {
            AddChild(new LocalSandbox { Args = Args });
            return;
        }

        var ui = new CanvasLayer { Layer = 10 };
        _status = new Label { Position = new Vector2(16, 40) };
        _status.AddThemeColorOverride("font_outline_color", Colors.Black);
        _status.AddThemeConstantOverride("outline_size", 4);
        ui.AddChild(_status);
        AddChild(ui);

        var replicator = new Replicator();
        AddChild(replicator);
        _net = new NetClient { Replicator = replicator };
        AddChild(_net);
        _net.Joined += StartGame;

        var peer = new ENetMultiplayerPeer();
        Error error = peer.CreateClient(Args.ConnectHost, Args.Port);
        if (error != Error.Ok)
        {
            SetStatus($"Could not start connecting to {Args.ConnectHost}:{Args.Port}: {error}");
            return;
        }

        Multiplayer.MultiplayerPeer = peer;
        Multiplayer.ConnectedToServer += () => SetStatus($"Connected to {Args.ConnectHost}:{Args.Port} as peer {Multiplayer.GetUniqueId()}. Loading the world...");
        Multiplayer.ConnectionFailed += () => SetStatus($"Connection to {Args.ConnectHost}:{Args.Port} failed.");
        Multiplayer.ServerDisconnected += () => SetStatus("Server disconnected.");
        SetStatus($"Connecting to {Args.ConnectHost}:{Args.Port}...");
    }

    private void StartGame(PlayerState spawn)
    {
        if (Args.Bot is not null)
        {
            AddChild(new SyncBot { Net = _net, Role = Args.Bot });
            return;
        }

        GameData data = _net.Data!;
        var game = new ClientGame
        {
            World = _net.World!,
            Terrain = data.Terrain,
            Settings = data.Player,
            Net = data.Net,
            Spawn = spawn,
            Link = _net,
        };
        AddChild(game);
        _net.WorldChanged += game.ShowWorldChange;
        _net.OwnStateReceived += game.Reconcile;
        SetStatus($"Playing on {Args.ConnectHost}:{Args.Port} as peer {Multiplayer.GetUniqueId()}.");
    }

    private void SetStatus(string text)
    {
        _status.Text = text;
        GD.Print($"[client] {text}");
    }
}
