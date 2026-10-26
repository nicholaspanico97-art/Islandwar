using Godot;
using IslandWar.Client.Bots;
using IslandWar.Client.Game;
using IslandWar.Client.Online;
using IslandWar.Client.Sandbox;
using IslandWar.Client.Ui;
using IslandWar.Net;
using IslandWar.Sim.Match;
using IslandWar.Sim.Players;

namespace IslandWar.Client;

/// <summary>
/// Client entry. With <c>--connect</c> it joins that server straight away (bots always do). Otherwise it
/// shows the main menu, which joins a server or starts the single-player sandbox. Losing the connection
/// before or during a game goes back to the menu with the reason.
/// </summary>
public partial class ClientMain : Node
{
    private CanvasLayer _ui = null!;
    private Label _status = null!;
    private Replicator _replicator = null!;
    private Node? _session;
    private NetClient? _net;
    private ClientGame? _game;
    private bool _botStarted;
    private string _address = "";
    private (string Name, Team Team, string Token) _hello;

    public LaunchArgs Args { get; init; } = new();

    public override void _Ready()
    {
        // Same name as the server's root node, so RPC paths match (see Replicator).
        Name = "Game";
        InputActions.Register();

        _ui = new CanvasLayer { Layer = 10 };
        _status = new Label { Position = new Vector2(16, 62) };
        _status.AddThemeColorOverride("font_outline_color", Colors.Black);
        _status.AddThemeConstantOverride("outline_size", 4);
        _ui.AddChild(_status);
        AddChild(_ui);

        // One replicator and one set of connection handlers for the client's whole life: a replicator made
        // per connection could get a different node path while the old one is still being freed.
        _replicator = new Replicator();
        AddChild(_replicator);
        Multiplayer.ConnectedToServer += () =>
        {
            SetStatus($"Connected to {_address}. Loading the world...");
            _net?.SayHello(_hello.Name, _hello.Team, _hello.Token);
        };
        Multiplayer.ConnectionFailed += () => Fail($"Couldn't reach a server at {_address}.");
        Multiplayer.ServerDisconnected += () => Fail("The server disconnected.");

        if (Args.ConnectHost is not null)
        {
            Connect(Args.ConnectHost, Args.Port, Args.Name ?? Args.Bot ?? PlayerNames.Default, Teams.Parse(Args.Team));
        }
        else
        {
            ShowMenu(null);
        }
    }

    private void ShowMenu(string? message)
    {
        EndSession();
        var menu = new MainMenu();
        _ui.AddChild(menu);
        _session = menu;
        menu.ShowStatus(message ?? "");
        menu.JoinRequested += (host, port, name, team) => Connect(host, port, name, team);
        menu.SandboxRequested += () =>
        {
            EndSession();
            _session = new LocalSandbox { Args = Args };
            AddChild(_session);
        };
    }

    private void Connect(string host, int port, string name, Team team)
    {
        EndSession();
        _address = $"{host}:{port}";
        _hello = (name, team, IdentityToken());
        var session = new Node { Name = "Session" };
        _session = session;
        AddChild(session);
        _net = new NetClient { Replicator = _replicator };
        session.AddChild(_net);
        _net.Joined += StartGame;
        _net.WorldReset += EndGame;
        _net.KickedOut += reason => Fail($"The server turned us away: {reason}");

        var peer = new ENetMultiplayerPeer();
        Error error = peer.CreateClient(host, port);
        if (error != Error.Ok)
        {
            Fail($"Could not start connecting to {_address}: {error}");
            return;
        }

        Multiplayer.MultiplayerPeer = peer;
        SetStatus($"Connecting to {_address}...");
    }

    private void StartGame(PawnState spawn)
    {
        NetClient net = _net!;
        if (Args.Bot is not null)
        {
            // Bots keep running through a world reset; they watch for it themselves.
            if (_botStarted)
            {
                return;
            }

            _botStarted = true;
            _session!.AddChild(Args.Bot switch
            {
                "pilot" or "nuker" => new PilotBot { Net = net, Role = Args.Bot, Spawn = spawn },
                "defender" => new DefenderBot { Net = net, Role = Args.Bot },
                "leaver" => new LeaverBot { Net = net, Role = Args.Bot },
                "returner" => new ReturnerBot { Net = net, Role = Args.Bot },
                "conceder" => new ConcederBot { Net = net, Role = Args.Bot },
                "watcher" => new MatchBot { Net = net, Role = Args.Bot },
                _ => (TestBot)new SyncBot { Net = net, Role = Args.Bot },
            });
            return;
        }

        GameData data = net.Data!;
        var game = new ClientGame
        {
            World = net.World!,
            Terrain = data.Terrain,
            Rules = data.Rules,
            Net = data.Net,
            Spawn = spawn,
            Link = net,
        };
        _game = game;
        _session!.AddChild(game);
        net.WorldChanged += game.ShowWorldChange;
        net.OwnStateReceived += game.Reconcile;
        net.Exploded += game.ShowExplosion;
        SetStatus($"Playing on {_address} ({net.PresetName} preset{(net.DevMode ? ", developer mode" : "")}).");
        GetTree().CreateTimer(6).Timeout += () => _status.Text = "";
    }

    /// <summary>A new match: drop the old game scene; <see cref="StartGame"/> runs again with the new spawn.</summary>
    private void EndGame()
    {
        if (_game is null || _net is null)
        {
            return;
        }

        _net.WorldChanged -= _game.ShowWorldChange;
        _net.OwnStateReceived -= _game.Reconcile;
        _net.Exploded -= _game.ShowExplosion;
        _game.QueueFree();
        _game = null;
    }

    /// <summary>
    /// Who we are to the server across reconnects: <c>--token</c> if given, a fresh one for bots, otherwise a
    /// random one made once and kept in <c>user://settings.cfg</c>.
    /// </summary>
    private string IdentityToken()
    {
        if (Args.Token is not null)
        {
            return Args.Token;
        }

        if (Args.Bot is not null)
        {
            return System.Guid.NewGuid().ToString("N");
        }

        const string path = "user://settings.cfg";
        var settings = new ConfigFile();
        settings.Load(path);
        string token = settings.GetValue("player", "token", "").AsString();
        if (token.Length == 0)
        {
            token = System.Guid.NewGuid().ToString("N");
            settings.SetValue("player", "token", token);
            settings.Save(path);
        }

        return token;
    }

    /// <summary>Bots exit with a failure; players go back to the menu with the reason.</summary>
    private void Fail(string reason)
    {
        if (_net is null || _net.LeavingOnPurpose)
        {
            return;
        }

        GD.Print($"[client] {reason}");
        if (Args.Bot is not null)
        {
            GetTree().Quit(1);
        }
        else
        {
            ShowMenu(reason);
        }
    }

    /// <summary>Leaves whatever is running (menu, sandbox or connection) so another can start.</summary>
    private void EndSession()
    {
        _status.Text = "";
        _session?.QueueFree();
        _session = null;
        _net = null;
        _game = null;
        _botStarted = false;
        if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer peer)
        {
            peer.Close();
            Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
        }
    }

    private void SetStatus(string text)
    {
        _status.Text = text;
        GD.Print($"[client] {text}");
    }
}
