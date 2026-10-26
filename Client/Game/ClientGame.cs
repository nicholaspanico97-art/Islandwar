using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using IslandWar.Client.Ui;
using IslandWar.Client.Voxels;
using IslandWar.Sim;
using IslandWar.Sim.Match;
using IslandWar.Sim.Players;
using IslandWar.Sim.Protocol;
using IslandWar.Sim.Vehicles;
using IslandWar.Sim.Voxels;
using IslandWar.Sim.Weapons;
using Explosion = IslandWar.Net.Explosion;

namespace IslandWar.Client.Game;

/// <summary>
/// What a player sees and controls: the rendered world, the camera, the HUD, other players and planes.
/// The local player (or the plane they fly) moves by Sim prediction every tick. With a <see cref="Link"/>
/// the inputs and edits also go to the server, which can correct them; without one (the sandbox) the
/// prediction is simply the truth, and there are no planes.
/// </summary>
public partial class ClientGame : Node3D
{
    /// <summary>Corrections bigger than this snap instead of being smoothed (a respawn, say).</summary>
    private const float SnapDistance = 2;

    /// <summary>How fast a smoothed correction fades out, per second.</summary>
    private const float SmoothingRate = 15;

    /// <summary>How many ticks ahead the bombsight looks for an impact (10 seconds at 60 ticks).</summary>
    private const int BombsightTicks = 600;

    private readonly Dictionary<int, RemoteAvatar> _avatars = new();
    private readonly Dictionary<int, PlaneModel> _planes = new();
    private readonly Dictionary<int, BombModel> _bombs = new();
    private GodotVoxelView _view = null!;
    private BlockHighlight _highlight = null!;
    private Bombsight _bombsight = null!;
    private BombImpact? _aim;
    private double _lastDropSeconds = double.NegativeInfinity;
    private GameHud _hud = null!;
    private CameraRig _camera = null!;
    private Node3D _others = null!;
    private PredictedPlayer _player = null!;
    private PawnState _previous;
    private Vector3 _renderOffset;
    private float _yaw;
    private float _pitch;
    private bool _wantsFlying;
    private bool _usePressed;
    private bool _firePressed;
    private bool _nukePressed;
    private MatchStatus _lastMatch;

    public VoxelWorld World { get; init; } = null!;

    public IslandSettings Terrain { get; init; } = null!;

    public GameRules Rules { get; init; } = null!;

    public NetSettings Net { get; init; } = null!;

    /// <summary>Where the local player starts. The server's spawn when online.</summary>
    public PawnState Spawn { get; init; }

    public IGameLink? Link { get; init; }

    private PlayerSettings Settings => Rules.Player;

    private PawnState State => _player.State;

    public override void _Ready()
    {
        Name = "ClientGame";

        AddChild(new Scenery { World = World, Terrain = Terrain });
        _view = new GodotVoxelView { World = World };
        AddChild(_view);
        _highlight = new BlockHighlight();
        AddChild(_highlight);
        _bombsight = new Bombsight();
        AddChild(_bombsight);
        _others = new Node3D { Name = "Others" };
        AddChild(_others);

        _player = new PredictedPlayer(Spawn, Link?.LastSentSeq ?? 0);
        // A fresh game scene is made when a match starts (the world resets), so it never sees warmup turn into
        // play. If the match has only just begun, count it as unseen so the start is still announced.
        MatchStatus match = Link?.Match ?? MatchStatus.Warmup;
        _lastMatch = match.Playing && match.StageSeconds < 3 ? MatchStatus.Warmup : match;
        _previous = Spawn;
        _camera = new CameraRig();
        AddChild(_camera);
        _hud = new GameHud();
        AddChild(_hud);
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <summary>Draws a block change the client didn't predict: another player's, or a server correction.</summary>
    public void ShowWorldChange(VoxelEdit edit) => _view.ShowEdit(edit);

    /// <summary>Draws an explosion the server reported. Its crater arrives separately as world changes.</summary>
    public void ShowExplosion(Explosion explosion)
    {
        _bombs.Remove(explosion.BombId, out BombModel? bomb);
        bomb?.QueueFree();
        Vector3 at = ToGodot(explosion.Position);
        AddChild(new ExplosionEffect { Position = at, Radius = explosion.CraterRadius, InWater = explosion.InWater });
        _camera.Shake(at.DistanceTo(ToGodot(State.Player.Position)), explosion.CraterRadius);
    }

    /// <summary>
    /// The server's state for the local player after it processed input <paramref name="ack"/>. Small
    /// corrections are blended out over a few frames; big ones, and getting in or out of a plane, snap.
    /// </summary>
    public void Reconcile(PawnState server, uint ack)
    {
        PawnState before = State;
        _player.Reconcile(World, Rules, server, ack, Net.TickSeconds);
        Vector3 shift = ToGodot(State.Player.Position) - ToGodot(before.Player.Position);

        if (before.InPlane != State.InPlane)
        {
            if (!State.InPlane)
            {
                // Out of the plane: face the way it was pointing.
                _yaw = before.Plane.Heading;
                _pitch = 0;
            }

            _previous = State;
            _renderOffset = Vector3.Zero;
            return;
        }

        _previous = _previous with
        {
            Player = _previous.Player with { Position = _previous.Player.Position + ToSim(shift) },
            Plane = _previous.Plane with { Position = _previous.Plane.Position + ToSim(shift) },
        };
        _renderOffset = shift.Length() > SnapDistance ? Vector3.Zero : _renderOffset - shift;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        if (@event is InputEventMouseMotion motion && captured)
        {
            float yaw = -Mathf.DegToRad(motion.Relative.X * Settings.LookSensitivity);
            float pitch = -Mathf.DegToRad(motion.Relative.Y * Settings.LookSensitivity);
            if (State.InPlane)
            {
                _camera.Orbit(yaw, pitch);
            }
            else
            {
                _yaw += yaw;
                _pitch = Mathf.Clamp(_pitch + pitch, -Mathf.Pi / 2 + 0.01f, Mathf.Pi / 2 - 0.01f);
            }
        }
        else if (@event.IsActionPressed(InputActions.ReleaseMouse))
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else if (!captured)
        {
            // While the mouse is free, a click only recaptures it; it doesn't edit.
            if (@event is InputEventMouseButton { Pressed: true })
            {
                Input.MouseMode = Input.MouseModeEnum.Captured;
                GetViewport().SetInputAsHandled();
            }
        }
        else if (@event.IsActionPressed(InputActions.Use))
        {
            _usePressed = true;
        }
        else if (@event.IsActionPressed(InputActions.Concede) && Link is not null)
        {
            bool voted = Link.Player(Multiplayer.GetUniqueId())?.ConcedeVote ?? false;
            Link.SetConcedeVote(!voted);
        }
        else if (State.InPlane)
        {
            // In a plane the left button drops bombs; building and noclip are for players on foot.
            if (@event.IsActionPressed(InputActions.Break))
            {
                _firePressed = true;
            }
            else if (@event.IsActionPressed(InputActions.Place))
            {
                _nukePressed = true;
            }
        }
        else if (@event.IsActionPressed(InputActions.ToggleFly) && (Link?.DevMode ?? true))
        {
            // Noclip is a developer convenience: the sandbox always allows it, a server only in developer mode.
            _wantsFlying = !_wantsFlying;
        }
        else if (@event.IsActionPressed(InputActions.Break))
        {
            Break();
        }
        else if (@event.IsActionPressed(InputActions.Place))
        {
            Place();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        _previous = State;
        PlayerInput sent = _player.Predict(World, Rules, ReadInput(), Net.TickSeconds);
        Link?.SendInputs(_player.Latest(Net.InputsPerPacket));

        // Online, the server decides respawns and the correction arrives with the next snapshot.
        if (Link is null && State.Player.Position.Y < Settings.RespawnBelowY)
        {
            _player.Reconcile(World, Rules, Spawn, sent.Seq, Net.TickSeconds);
            _previous = Spawn;
        }

        _highlight.ShowAt(State.InPlane ? null : Aim()?.Block);
        _aim = State.InPlane && !State.Plane.OnGround && CraterRadius > 0
            ? BombFlight.PredictImpact(World, Rules.Bomb, Rules.SeaLevel, BombFlight.Drop(Rules.Bomb, State.Plane), Net.TickSeconds, BombsightTicks)
            : null;
    }

    public override void _Process(double delta)
    {
        _renderOffset *= Mathf.Exp(-SmoothingRate * (float)delta);
        float fraction = (float)Engine.GetPhysicsInterpolationFraction();
        IReadOnlyDictionary<int, RemotePlane> planes = Link?.SampleRemotePlanes() ?? new Dictionary<int, RemotePlane>();

        if (State.InPlane)
        {
            PlaneState from = _previous.InPlane ? _previous.Plane : State.Plane;
            Vector3 position = ToGodot(from.Position).Lerp(ToGodot(State.Plane.Position), fraction) + _renderOffset;
            Quaternion rotation = ToGodot(from.Rotation).Slerp(ToGodot(State.Plane.Rotation), fraction);
            Plane(State.PlaneId).ShowState(position, rotation, State.Plane.Throttle, delta);
            _camera.Chase(position, rotation, delta);
            _hud.ShowFlying(State.Plane, Rules.Plane, State.Plane.Position.Y - Rules.SeaLevel, BombStatus() + (Link is null ? "" : "\n" + MatchText.Nuke(Link.NukeReadyIn)));
        }
        else
        {
            Vector3 feet = ToGodot(_previous.Player.Position).Lerp(ToGodot(State.Player.Position), fraction) + _renderOffset;
            _camera.FirstPerson(feet + Vector3.Up * Settings.EyeHeight, _yaw, _pitch);
            _hud.ShowOnFoot(CanGetIn(planes) ? "Press E to fly the plane" : null);
        }

        ShowPlanes(planes, delta);
        ShowIdentity();
        ShowMatch();
        _bombsight.SetRadius(Math.Max(1, CraterRadius));
        _bombsight.ShowAt(_aim is { } aim ? ToGodot(aim.Position) : null, _aim?.InWater ?? false);
        if (Link is not null)
        {
            ShowOtherPlayers(Link.SampleRemotePlayers());
            ShowBombs(Link.SampleBombs());
        }
    }

    private void ShowIdentity()
    {
        if (Link is null)
        {
            _hud.ShowIdentity("Sandbox", Colors.White);
        }
        else if (Link.Player(Multiplayer.GetUniqueId()) is { } me)
        {
            _hud.ShowIdentity($"{me.Name} - {me.Team} team", TeamColors.Of(me.Team).Lightened(0.3f));
        }
    }

    /// <summary>The crater a bomb dropped now would make (0 = bombs locked), from the match phase.</summary>
    private int CraterRadius => Link?.Preset is { } preset
        ? MatchFlow.CraterRadius(Link.Match, preset, Rules.Bomb.CraterRadius)
        : Rules.Bomb.CraterRadius;

    private void ShowMatch()
    {
        if (Link?.Preset is not { } preset)
        {
            _hud.ShowMatch("");
            return;
        }

        MatchStatus match = Link.Match;
        int red = Link.Roster.Values.Count(p => p.Team == Team.Red);
        int blue = Link.Roster.Values.Count(p => p.Team == Team.Blue);
        _hud.ShowMatch(MatchText.Status(match, preset, red, blue));
        IReadOnlyList<CoreSite> cores = Link.Cores;
        int redCore = cores.Count < 2 ? 0 : cores[Team.Red.IslandIndex()].StandingBlocks(World);
        int blueCore = cores.Count < 2 ? 0 : cores[Team.Blue.IslandIndex()].StandingBlocks(World);
        _hud.ShowCores(cores.Count < 2 ? "" : MatchText.Cores(redCore, blueCore, cores[0].TotalBlocks));

        Team mine = Link.MyTeam;
        RosterEntry[] team = Link.Roster.Values.Where(p => p.Team == mine).ToArray();
        _hud.ShowConcede(match.Playing ? MatchText.Concede(team.Count(p => p.ConcedeVote), team.Length, preset.ConcedeFraction) : "");

        _hud.Scoreboard.Visible = Input.IsActionPressed(InputActions.Scoreboard);
        if (_hud.Scoreboard.Visible)
        {
            Dictionary<Team, int>? percent = cores.Count < 2 ? null : new Dictionary<Team, int>
            {
                [Team.Red] = 100 * redCore / cores[0].TotalBlocks,
                [Team.Blue] = 100 * blueCore / cores[0].TotalBlocks,
            };
            _hud.Scoreboard.ShowRoster(Link.Roster.Values.ToList(), percent, Multiplayer.GetUniqueId(), preset.ConcedeFraction);
        }
        if (MatchText.Announcement(_lastMatch, match, preset) is { } news)
        {
            Color colour = match.Stage == MatchStage.Ended && match.Winner != Team.None ? TeamColors.Of(match.Winner).Lightened(0.3f) : Colors.Gold;
            _hud.Announce(news.Title, news.Detail, colour, match.Stage == MatchStage.Ended ? preset.ResultsSeconds : 5);
        }

        _lastMatch = match;
    }

    private bool BombReady => NowSeconds - _lastDropSeconds >= Rules.Bomb.CooldownSeconds;

    private static double NowSeconds => Time.GetTicksMsec() / 1000.0;

    private string BombStatus() =>
        Link is null ? "No bombs in the sandbox"
        : CraterRadius == 0 ? (Link.Match.Playing ? "Bombs unlock in phase 2" : "No bombs now")
        : State.Plane.OnGround ? "Bombs: take off first"
        : BombReady ? "Bomb ready: left click"
        : "Bomb bay reloading...";

    private PlayerInput ReadInput()
    {
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        Vector2 move = captured
            ? Input.GetVector(InputActions.MoveLeft, InputActions.MoveRight, InputActions.MoveForward, InputActions.MoveBack)
            : Vector2.Zero;
        InputButtons buttons = InputButtons.None;
        if (captured && Input.IsActionPressed(InputActions.Jump))
        {
            buttons |= InputButtons.Jump;
        }

        if (captured && Input.IsActionPressed(InputActions.Sprint))
        {
            buttons |= InputButtons.Sprint;
        }

        if (captured && Input.IsActionPressed(InputActions.Descend))
        {
            buttons |= InputButtons.Descend;
        }

        if (_wantsFlying)
        {
            buttons |= InputButtons.Fly;
        }

        if (_usePressed)
        {
            buttons |= InputButtons.Use;
            _usePressed = false;
        }

        if (_firePressed)
        {
            // The server has the final say on the reload; this only keeps the HUD honest.
            if (State.InPlane && !State.Plane.OnGround && BombReady && CraterRadius > 0)
            {
                _lastDropSeconds = NowSeconds;
            }

            buttons |= InputButtons.Fire;
            _firePressed = false;
        }

        if (_nukePressed)
        {
            buttons |= InputButtons.FireNuke;
            _nukePressed = false;
        }

        return new PlayerInput(0, move.X, move.Y, _yaw, _pitch, buttons);
    }

    /// <summary>Whether a free plane is close enough to get into. The server makes the real decision.</summary>
    private bool CanGetIn(IReadOnlyDictionary<int, RemotePlane> planes) =>
        planes.Values.Any(p => p.PilotPeerId == 0
            && VehicleRules.InEnterRange(Rules.Plane, State.Player.Position, new PlaneState(p.Position, p.Rotation, 0, 0, 0, true)));

    private RaycastHit? Aim()
    {
        Vector3 eye = ToGodot(State.Player.Position) + Vector3.Up * Settings.EyeHeight;
        Vector3 look = Basis.FromEuler(new Vector3(_pitch, _yaw, 0)) * Vector3.Forward;
        return VoxelRaycast.Cast(World, ToSim(eye), ToSim(look), Settings.Reach);
    }

    private void Break()
    {
        if (Aim() is { } hit && BuildRules.CanBreak(World, hit.Block))
        {
            Predict(new VoxelEdit(hit.Block, Block.Air));
        }
    }

    private void Place()
    {
        // Flying turns collisions off, so the player's own box doesn't block placement then. Other players
        // and planes are left to the server, which knows where they really are.
        System.Numerics.Vector3[] blockers = State.Player.Flying ? Array.Empty<System.Numerics.Vector3>() : new[] { State.Player.Position };
        if (Aim() is { } hit && BuildRules.CanPlace(World, Settings, hit.Before, _hud.Hotbar.Selected, blockers))
        {
            Predict(new VoxelEdit(hit.Before, _hud.Hotbar.Selected));
        }
    }

    /// <summary>Applies an edit at once. Online, the server confirms it or sends back the real block.</summary>
    private void Predict(VoxelEdit edit)
    {
        if (World.ApplyEdit(edit))
        {
            _view.ShowEdit(edit);
            Link?.SubmitEdit(edit);
        }
    }

    private PlaneModel Plane(int id)
    {
        if (!_planes.TryGetValue(id, out PlaneModel? model))
        {
            model = new PlaneModel { PlaneId = id };
            _others.AddChild(model);
            _planes[id] = model;
        }

        return model;
    }

    /// <summary>Draws every plane except the one being flown here, which <see cref="_Process"/> draws from prediction.</summary>
    private void ShowPlanes(IReadOnlyDictionary<int, RemotePlane> planes, double delta)
    {
        foreach ((int id, RemotePlane plane) in planes)
        {
            Plane(id).SetTeam(plane.Team);
            if (id != State.PlaneId)
            {
                Plane(id).ShowState(ToGodot(plane.Position), ToGodot(plane.Rotation), plane.Throttle, delta);
            }
        }

        foreach (int gone in _planes.Keys.Where(id => !planes.ContainsKey(id) && id != State.PlaneId).ToList())
        {
            _planes[gone].QueueFree();
            _planes.Remove(gone);
        }
    }

    private void ShowBombs(IReadOnlyDictionary<int, (BombState State, bool Nuke)> bombs)
    {
        foreach ((int id, (BombState bomb, bool nuke)) in bombs)
        {
            if (!_bombs.TryGetValue(id, out BombModel? model))
            {
                model = new BombModel { Nuke = nuke };
                _others.AddChild(model);
                _bombs[id] = model;
            }

            model.ShowState(ToGodot(bomb.Position), ToGodot(bomb.Velocity));
        }

        foreach (int gone in _bombs.Keys.Where(id => !bombs.ContainsKey(id)).ToList())
        {
            _bombs[gone].QueueFree();
            _bombs.Remove(gone);
        }
    }

    private void ShowOtherPlayers(IReadOnlyDictionary<int, RemoteState> others)
    {
        foreach ((int peer, RemoteState state) in others)
        {
            if (!_avatars.TryGetValue(peer, out RemoteAvatar? avatar))
            {
                avatar = new RemoteAvatar { PeerId = peer, Settings = Settings };
                _others.AddChild(avatar);
                _avatars[peer] = avatar;
            }

            // A pilot sits inside the plane, which is drawn instead.
            avatar.Visible = !state.InPlane;
            if (Link?.Player(peer) is { } who)
            {
                avatar.SetIdentity(who.Name, who.Team);
            }

            avatar.ShowState(state);
        }

        foreach (int gone in _avatars.Keys.Where(peer => !others.ContainsKey(peer)).ToList())
        {
            _avatars[gone].QueueFree();
            _avatars.Remove(gone);
        }
    }

    private static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    private static Quaternion ToGodot(System.Numerics.Quaternion q) => new(q.X, q.Y, q.Z, q.W);

    private static System.Numerics.Vector3 ToSim(Vector3 v) => new(v.X, v.Y, v.Z);
}
