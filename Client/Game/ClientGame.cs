using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using IslandWar.Client.Ui;
using IslandWar.Client.Voxels;
using IslandWar.Sim;
using IslandWar.Sim.Items;
using IslandWar.Sim.Match;
using IslandWar.Sim.Objects;
using IslandWar.Sim.Players;
using IslandWar.Sim.Protocol;
using IslandWar.Sim.Research;
using IslandWar.Sim.Server;
using IslandWar.Sim.Vehicles;
using IslandWar.Sim.Voxels;
using IslandWar.Sim.Weapons;
using Explosion = IslandWar.Sim.Protocol.Explosion;

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
    private Scenery _scenery = null!;
    private GodotVoxelView _view = null!;
    private ObjectViews _objects = null!;
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

    /// <summary>Our team's era as last shown, to announce reaching a new one; null until the team state arrives.</summary>
    private int? _lastEra;

    /// <summary>The block being mined (hold to mine), and for how long it has been.</summary>
    private BlockPos? _miningAt;
    private double _miningSeconds;
    private bool _miningRefused;

    public VoxelWorld World { get; init; } = null!;

    public IslandSettings Terrain { get; init; } = null!;

    public GameRules Rules { get; init; } = null!;

    public NetSettings Net { get; init; } = null!;

    /// <summary>Items, what each block drops and how long it takes to mine.</summary>
    public ItemSettings Items { get; init; } = null!;

    /// <summary>The eras and techs, and what research unlocks.</summary>
    public TechTree Techs { get; init; } = null!;

    /// <summary>Where the local player starts. The server's spawn when online.</summary>
    public PawnState Spawn { get; init; }

    public IGameLink? Link { get; init; }

    private PlayerSettings Settings => Rules.Player;

    /// <summary>Building is free (the offline sandbox, practice and tests): no items, instant mining.</summary>
    private bool Creative => Link?.Creative ?? true;

    private PawnState State => _player.State;

    public override void _Ready()
    {
        Name = "ClientGame";

        ViewSettings view = ViewSettings.FromJson(FileAccess.GetFileAsString("res://data/view.json"));
        _scenery = new Scenery { World = World, Terrain = Terrain, View = view };
        AddChild(_scenery);
        _view = new GodotVoxelView { World = World, Settings = view, Focus = ToGodot(Spawn.Player.Position) };
        AddChild(_view);
        _objects = new ObjectViews { Objects = Link?.Objects };
        AddChild(_objects);
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
        _hud = new GameHud
        {
            StructureKinds = Link?.Objects?.Kinds.All ?? Array.Empty<ObjectKind>(),

            // Out of creative, the hotbar offers the blocks some item places; how many you have shows on each.
            HotbarBlocks = Creative ? null : Items.Items.Select(Items.BlockPlacedBy).OfType<Block>().Order().ToArray(),
        };
        AddChild(_hud);
        _hud.Crafting.CraftClicked += recipe => Link?.RequestCraft(recipe, _hud.Crafting.StationId);
        _hud.Research.ResearchClicked += tech => Link?.RequestResearch(tech);
        _hud.Furnace.TransferClicked += (slot, item, count, into) =>
        {
            if (_hud.Furnace.FurnaceId is { } id)
            {
                Link?.RequestTransfer(id, slot, item, count, into);
            }
        };
        _hud.Storage.TransferClicked += (item, count, into) =>
        {
            if (_hud.Storage.StorageId is { } id)
            {
                Link?.RequestTransfer(id, ItemCounts.StorageSlot, item, count, into);
            }
        };
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <summary>Streams the terrain in around the player, further (with the fog pushed back) when flying.</summary>
    private void DrawAround(Vector3 focus, bool flying)
    {
        _view.Focus = focus;
        _view.ViewDistance = flying ? _scenery.View.FlyingViewDistance : _scenery.View.ViewDistance;
        _scenery.SetViewDistance(_view.ViewDistance);
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
        if (explosion.Harmless && Link is not null && Protection.IsHomeGround(Link.MyTeam, at.X, World.SizeX, Terrain.IslandCount))
        {
            _hud.Toast("No friendly fire: bombs don't crater your own island.");
        }
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
        else if (WindowOpen && (@event.IsActionPressed(InputActions.Use) || @event.IsActionPressed(InputActions.ReleaseMouse)
                                || @event.IsActionPressed(InputActions.Craft) || @event.IsActionPressed(InputActions.Research)))
        {
            CloseWindows();
            GetViewport().SetInputAsHandled();
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
                CloseWindows();
                GetViewport().SetInputAsHandled();
            }
        }
        else if (@event.IsActionPressed(InputActions.Use))
        {
            // E on our team's storage or workbench opens it; anywhere else it's for the plane.
            if (State.InPlane || !TryOpenStructure())
            {
                _usePressed = true;
            }
        }
        else if (@event.IsActionPressed(InputActions.Craft) && !State.InPlane && Link is not null)
        {
            _hud.Crafting.Open(null, 0);
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else if (@event.IsActionPressed(InputActions.Research) && !State.InPlane && Link is not null)
        {
            if (Creative)
            {
                _hud.Toast("Everything is unlocked on this server.");
            }
            else
            {
                _hud.Research.Open();
                Input.MouseMode = Input.MouseModeEnum.Visible;
            }
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
        else if (@event.IsActionPressed(InputActions.Break) && Creative)
        {
            // Out of creative, mining is holding the button (see Mine).
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
            DrawAround(position, flying: true);
            _hud.ShowFlying(State.Plane, Rules.Plane, State.Plane.Position.Y - Rules.SeaLevel, BombStatus() + (Link is null ? "" : "\n" + MatchText.Nuke(Link.NukeReadyIn)));
        }
        else
        {
            Vector3 feet = ToGodot(_previous.Player.Position).Lerp(ToGodot(State.Player.Position), fraction) + _renderOffset;
            _camera.FirstPerson(feet + Vector3.Up * Settings.EyeHeight, _yaw, _pitch);
            DrawAround(feet, flying: false);
            _hud.ShowOnFoot(!CanGetIn(planes) ? AimedStructure()
                : Unlocked(Unlock.Plane) ? "Press E to fly the plane"
                : $"The plane needs {Techs.UnlockedBy(Unlock.Plane)}");
        }

        ShowPlacement();
        Mine(delta);
        ShowInventory();

        ShowPlanes(planes, delta);
        ShowIdentity();
        ShowEra();
        ShowMatch();
        _bombsight.SetRadius(Math.Max(1, CraterRadius));
        // White where a bomb would do nothing: the sea, or our own half of the map (no friendly fire).
        bool harmless = _aim is { } target && (target.InWater
            || (Link is not null && Protection.IsHomeGround(Link.MyTeam, target.Position.X, World.SizeX, Terrain.IslandCount)));
        _bombsight.ShowAt(_aim is { } aim ? ToGodot(aim.Position) : null, harmless);
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

    /// <summary>Whether our team may use something research can lock; everything is open in creative.</summary>
    private bool Unlocked(string key) => Creative || (Link?.TeamState is { } team && Techs.IsUnlocked(team, key));

    /// <summary>"Needs Mining research" for something locked, or null if our team may use it.</summary>
    private string? Locked(string key) => Unlocked(key) ? null : $"Needs {Techs.UnlockedBy(key)}";

    /// <summary>Our team's era and points under our name, and a banner when the team reaches a new era.</summary>
    private void ShowEra()
    {
        if (Creative || Link?.TeamState is not { } team)
        {
            _hud.ShowEra("");
            return;
        }

        Era era = Techs.EraOf(team);
        _hud.ShowEra($"{era.Name}: {team.Points.Of(era.Id)} research points (R)");
        if (_lastEra is { } last && team.Era > last)
        {
            _hud.Announce($"The {era.Name}", "Your team has reached a new era. R to see what it can research.", Colors.Gold);
        }

        _lastEra = team.Era;
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
        Team mine = Link.MyTeam;
        RosterEntry[] team = Link.Roster.Values.Where(p => p.Team == mine).ToArray();
        _hud.ShowConcede(match.Playing ? MatchText.Concede(team.Count(p => p.ConcedeVote), team.Length, preset.ConcedeFraction) : "");

        _hud.Scoreboard.Visible = Input.IsActionPressed(InputActions.Scoreboard);
        if (_hud.Scoreboard.Visible)
        {
            _hud.Scoreboard.ShowRoster(Link.Roster.Values.ToList(), Multiplayer.GetUniqueId(), preset.ConcedeFraction, TeamSummary());
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
        if (Aim() is not { } hit)
        {
            return;
        }

        if (WhyCantMine(hit.Block) is { } reason)
        {
            if (reason.Length > 0)
            {
                _hud.Toast(reason);
            }

            return;
        }

        if (Link?.Objects?.At(hit.Block) is { } structure)
        {
            Link.RequestRemove(structure.Id);
            return;
        }

        Predict(new VoxelEdit(hit.Block, Block.Air));
    }

    /// <summary>
    /// Why a block (or the structure it's part of) can't be mined: null if it can, a message to show if the
    /// server would refuse (team build protection, a full store), or empty if there's simply nothing to mine.
    /// </summary>
    private string? WhyCantMine(BlockPos block)
    {
        bool online(int id) => Link!.Roster.Values.Any(p => p.PlayerId == id);
        string nameOf(int id) => Link!.Roster.Values.FirstOrDefault(p => p.PlayerId == id).Name ?? "A teammate";
        if (Link?.Objects?.At(block) is { } structure)
        {
            if (!Protection.MayBreak(structure.Owner, Link.MyPlayerId, Link.MyTeam, online))
            {
                return $"{nameOf(structure.OwnerPlayerId)} built that. Teammates' structures are protected.";
            }

            return ItemCounts.HasItems(structure)
                ? $"Empty the {Link.Objects.KindOf(structure).Name} before taking it down."
                : null;
        }

        if (!BuildRules.CanBreak(World, block))
        {
            return "";
        }

        if (!Creative && MiningSeconds(block) is null)
        {
            string tool = Items.ToolNeededFor(World.Get(block))?.Name.ToLowerInvariant() ?? "better tool";
            return $"You need a {tool} to mine {Items.Item(Items.RuleFor(World.Get(block))?.Drops ?? "")?.Name.ToLowerInvariant() ?? "that"}.";
        }

        // Team build protection: the server would refuse, so say why instead of flickering the block.
        BlockOwner owner = World.OwnerOf(block);
        return Link is not null && !Protection.MayBreak(owner, Link.MyPlayerId, Link.MyTeam, online)
            ? $"{nameOf(owner.PlayerId)} built that. Teammates' blocks are protected."
            : null;
    }

    /// <summary>
    /// Seconds to mine a block with what we carry (the structure time for part of a structure); null if it
    /// needs a better tool than we have.
    /// </summary>
    private float? MiningSeconds(BlockPos block) =>
        Link?.Objects?.At(block) is not null ? Items.StructureHardness : Items.MiningSeconds(World.Get(block), Link?.Backpack ?? ItemCounts.None);

    /// <summary>
    /// Hold to mine: while the button is held on the same block, time builds up to its hardness and then it
    /// breaks; looking away starts over. Creative mining is a click instead (see _UnhandledInput).
    /// </summary>
    private void Mine(double delta)
    {
        bool holding = !Creative && !State.InPlane && Input.MouseMode == Input.MouseModeEnum.Captured
                       && Input.IsActionPressed(InputActions.Break);
        if (!holding || Aim() is not { } hit)
        {
            _miningAt = null;
            _hud.ShowMining(null);
            return;
        }

        if (_miningAt != hit.Block)
        {
            _miningAt = hit.Block;
            _miningSeconds = 0;
            string? reason = WhyCantMine(hit.Block);
            _miningRefused = reason is not null;
            if (reason is { Length: > 0 })
            {
                _hud.Toast(reason);
            }
        }

        if (_miningRefused)
        {
            _hud.ShowMining(null);
            return;
        }

        _miningSeconds += delta;
        float hardness = MiningSeconds(hit.Block) ?? float.PositiveInfinity;
        _hud.ShowMining(hardness > 0 ? Mathf.Min(1, (float)(_miningSeconds / hardness)) : 1);
        if (_miningSeconds >= hardness)
        {
            Break();
            _miningAt = null;
        }
    }

    /// <summary>Hotbar counts, the backpack line, and the storage window if it's open.</summary>
    private void ShowInventory()
    {
        if (Creative || Link is null)
        {
            _hud.Hotbar.ShowCounts(_ => null);
            _hud.ShowBackpack("");
        }
        else
        {
            IReadOnlyDictionary<string, int> backpack = Link.Backpack;
            _hud.Hotbar.ShowCounts(slot => slot.IsStructure ? null : Items.ItemPlacing(slot.Block) is { } item ? backpack.Of(item.Id) : 0);
            string contents = string.Join(", ", backpack.Select(b => $"{Items.Item(b.Key)?.Name ?? b.Key} {b.Value}"));
            _hud.ShowBackpack($"Backpack {backpack.Total()} / {Items.BackpackCapacity}{(contents.Length > 0 ? ":  " + contents : "")}");
        }

        if (_hud.Crafting.IsOpen && Link is not null)
        {
            // Close if the workbench went away or we've walked off.
            if (_hud.Crafting.StationId != 0 && (Link.Objects?.Get(_hud.Crafting.StationId) is not { } bench || !InReach(bench)))
            {
                CloseWindows();
                return;
            }

            IReadOnlyDictionary<string, int> available = Link.Backpack.Sum(Link.TeamState?.Stock ?? ItemCounts.None);
            string where = _hud.Crafting.Station is null ? "Crafting by hand" : $"Crafting at the {Link.Objects!.Kinds.Get(_hud.Crafting.Station)?.Name ?? "station"}";
            _hud.Crafting.Show(where, Items, available, Creative, recipe => Locked(Unlock.Recipe(recipe.Id)));
        }

        if (_hud.Furnace.FurnaceId is { } furnaceId)
        {
            ShowFurnace(furnaceId);
        }

        if (_hud.Research.IsOpen && Link?.TeamState is { } team)
        {
            _hud.Research.Show(Techs, team, Link.Backpack.Sum(team.Stock), Items, Link.Objects!.Kinds);
        }

        if (_hud.Storage.StorageId is not { } id)
        {
            return;
        }

        // Close if it's gone (destroyed, or a new match) or we've walked away from it.
        if (Link?.Objects?.Get(id) is not { } storage || !InReach(storage))
        {
            CloseWindows();
            return;
        }

        ObjectKind kind = Link.Objects.KindOf(storage);
        _hud.Storage.Show($"{storage.Team} {kind.Name}", Link.Creative ? ItemCounts.None : Link.Backpack, Items.BackpackCapacity,
            ItemCounts.Contents(storage), kind.Capacity, Items);
    }

    private void ShowFurnace(int id)
    {
        if (Link?.Objects?.Get(id) is not { } furnace || !InReach(furnace) || Link.Objects.KindOf(furnace).Smelter is not { } smelter)
        {
            CloseWindows();
            return;
        }

        var slots = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [ItemCounts.InputSlot] = ItemCounts.Contents(furnace, ItemCounts.InputSlot),
            [ItemCounts.FuelSlot] = ItemCounts.Contents(furnace, ItemCounts.FuelSlot),
            [ItemCounts.OutputSlot] = ItemCounts.Contents(furnace, ItemCounts.OutputSlot),
        };
        _hud.Furnace.Show($"{furnace.Team} Furnace", FurnaceStatus(furnace, smelter), Link.Backpack, slots, smelter.SlotCapacity, Items);
    }

    /// <summary>One line on what a furnace is doing, from its slots and burn and progress values.</summary>
    private string FurnaceStatus(GameObject furnace, SmelterSpec smelter)
    {
        IReadOnlyDictionary<string, int> input = ItemCounts.Contents(furnace, ItemCounts.InputSlot);
        SmeltingRecipe? recipe = Furnaces.RecipeIn(furnace, Items, r => Unlocked(Unlock.Smelting(r.Makes)));
        int burn = furnace.Values.Of(Furnaces.BurnValue);
        bool fuel = ItemCounts.Contents(furnace, ItemCounts.FuelSlot).Count > 0;
        if (recipe is null)
        {
            // Something partway there: locked behind research, or short of an ingredient (bronze takes copper and tin).
            string name(string item) => Items.Item(item)?.Name.ToLowerInvariant() ?? item;
            if (input.Keys.Select(Items.SmeltingFor).OfType<SmeltingRecipe>().FirstOrDefault() is { } partial)
            {
                return Locked(Unlock.Smelting(partial.Makes)) is { } why
                    ? $"Stopped: {name(partial.Makes)} {why.ToLowerInvariant()}"
                    : $"Waiting: {name(partial.Makes)} takes {string.Join(" and ", partial.Takes.Select(t => $"{t.Value} {name(t.Key)}"))}";
            }

            return burn > 0 ? $"Idle, {burn / 1000f:0} s of fuel still burning" : "Idle: put in something to smelt";
        }

        if (ItemCounts.Contents(furnace, ItemCounts.OutputSlot).Total() + recipe.Count > smelter.SlotCapacity)
        {
            return "Stopped: take out what's done";
        }

        if (burn <= 0 && !fuel)
        {
            return "Stopped: needs fuel";
        }

        string makes = Items.Item(recipe.Makes)?.Name.ToLowerInvariant() ?? recipe.Makes;
        return $"Making {makes}: {furnace.Values.Of(Furnaces.ProgressValue) / 1000f:0.0} / {recipe.Seconds:0} s   (fuel burning {burn / 1000f:0} s)";
    }

    /// <summary>
    /// Opens the storage or crafting station under the crosshair if it's our team's; false if there isn't
    /// one (so E goes to the plane instead).
    /// </summary>
    private bool TryOpenStructure()
    {
        if (Link?.Objects is not { } objects || Aim() is not { } hit || objects.At(hit.Block) is not { } target)
        {
            return false;
        }

        ObjectKind kind = objects.KindOf(target);
        bool station = Items.Recipes.Any(r => r.Station == kind.Id);
        if (!kind.IsStorage && !station && kind.Smelter is null)
        {
            return false;
        }

        if (target.Team != Link.MyTeam)
        {
            _hud.Toast($"That's the other team's {kind.Name.ToLowerInvariant()}.");
            return true;
        }

        if (kind.IsStorage)
        {
            _hud.Storage.Open(target.Id);
        }
        else if (kind.Smelter is not null)
        {
            _hud.Furnace.Open(target.Id);
        }
        else
        {
            _hud.Crafting.Open(kind.Id, target.Id);
        }

        Input.MouseMode = Input.MouseModeEnum.Visible;
        return true;
    }

    private bool WindowOpen => _hud.Storage.StorageId is not null || _hud.Crafting.IsOpen || _hud.Furnace.FurnaceId is not null || _hud.Research.IsOpen;

    private void CloseWindows()
    {
        _hud.Storage.Close();
        _hud.Crafting.Close();
        _hud.Furnace.Close();
        _hud.Research.Close();
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private bool InReach(GameObject obj)
    {
        System.Numerics.Vector3 eye = State.Player.Position + System.Numerics.Vector3.UnitY * Settings.EyeHeight;
        return obj.Footprint(Link!.Objects!.KindOf(obj)).Any(b => BuildRules.InReach(Settings, eye, b));
    }

    private void Place()
    {
        if (_hud.Hotbar.Selected.Kind is { } kind)
        {
            PlaceStructure(kind);
            return;
        }

        Block block = _hud.Hotbar.Selected.Block;
        if (!Creative && Items.ItemPlacing(block) is { } item && Link!.Backpack.Of(item.Id) == 0)
        {
            _hud.Toast($"No {item.Name.ToLowerInvariant()} in your backpack.");
            return;
        }

        if (Items.ItemPlacing(block) is { } placing && Locked(Unlock.Place(placing.Id)) is { } locked)
        {
            _hud.Toast($"Placing {placing.Name.ToLowerInvariant()}: {locked.ToLowerInvariant()}.");
            return;
        }

        // Flying turns collisions off, so the player's own box doesn't block placement then. Other players
        // and planes are left to the server, which knows where they really are.
        System.Numerics.Vector3[] blockers = State.Player.Flying ? Array.Empty<System.Numerics.Vector3>() : new[] { State.Player.Position };
        if (Aim() is { } hit && BuildRules.CanPlace(World, Settings, hit.Before, _hud.Hotbar.Selected.Block, blockers))
        {
            // Predict the same owner the server will record, so its confirmation changes nothing.
            BlockOwner me = Link is null ? default : new BlockOwner(Link.MyPlayerId, Link.MyTeam);
            Predict(new VoxelEdit(hit.Before, _hud.Hotbar.Selected.Block, me));
        }
    }

    /// <summary>Where the selected structure would go: against the face aimed at, turned the way we look.</summary>
    private (ObjectKind Kind, BlockPos Origin, int Facing, string? WhyNot)? Placement()
    {
        if (State.InPlane || _hud.Hotbar.Selected.Kind is not { } kind || Aim() is not { } hit)
        {
            return null;
        }

        int facing = ObjectRules.FacingFor(_yaw);
        BlockPos origin = ObjectRules.OriginFor(kind, hit.Before, facing);
        System.Numerics.Vector3[] blockers = State.Player.Flying ? Array.Empty<System.Numerics.Vector3>() : new[] { State.Player.Position };
        return (kind, origin, facing, ObjectRules.WhyNot(World, Settings, kind, origin, facing, blockers));
    }

    private void ShowPlacement()
    {
        if (Placement() is { } placement)
        {
            _objects.ShowPreview(placement.Kind, placement.Origin, placement.Facing, placement.WhyNot is null);
        }
        else
        {
            _objects.ShowPreview(null, default, 0, false);
        }
    }

    private void PlaceStructure(ObjectKind kind)
    {
        if (Link is null || Placement() is not { } placement)
        {
            return;
        }

        if (Locked(Unlock.Structure(kind.Id)) is { } locked)
        {
            _hud.Toast($"The {kind.Name}: {locked.ToLowerInvariant()}.");
            return;
        }

        if (placement.WhyNot is { } reason)
        {
            _hud.Toast($"Can't place a {kind.Name} there: {reason}.");
            return;
        }

        if (!Creative && !Link.Backpack.Sum(Link.TeamState?.Stock ?? ItemCounts.None).Covers(kind.Cost))
        {
            _hud.Toast($"A {kind.Name} takes {CostText(kind)}.");
            return;
        }

        Link.RequestPlace(kind.Id, placement.Origin, placement.Facing);
    }

    private string CostText(ObjectKind kind) =>
        kind.Cost.Count == 0 ? "nothing" : string.Join(", ", kind.Cost.Select(c => $"{c.Value} {Items.Item(c.Key)?.Name.ToLowerInvariant() ?? c.Key}"));

    /// <summary>
    /// The prompt line: what the selected structure costs, or the name, team and health of the structure
    /// under the crosshair.
    /// </summary>
    private string? AimedStructure()
    {
        if (_hud.Hotbar.Selected.Kind is { } selected && !Creative && Link is not null)
        {
            if (Locked(Unlock.Structure(selected.Id)) is { } locked)
            {
                return $"{selected.Name}: {locked.ToLowerInvariant()}";
            }

            bool afford = Link.Backpack.Sum(Link.TeamState?.Stock ?? ItemCounts.None).Covers(selected.Cost);
            return $"{selected.Name}: {CostText(selected)}{(afford ? "" : "  (not enough)")}";
        }

        if (Link?.Objects is not { } objects || Aim() is not { } hit || objects.At(hit.Block) is not { } structure)
        {
            return null;
        }

        ObjectKind kind = objects.KindOf(structure);
        string open = structure.Team != Link.MyTeam ? ""
            : kind.IsStorage ? $"   {ItemCounts.Contents(structure).Total()} / {kind.Capacity} stored, E to open"
            : Items.Recipes.Any(r => r.Station == kind.Id) ? "   E to craft"
            : kind.Smelter is not null ? $"   {(structure.Values.Of(Furnaces.BurnValue) > 0 ? "burning, " : "")}E to open"
            : "";
        string take = Creative ? "left click" : "hold left click";
        return $"{structure.Team} {kind.Name}   {structure.Health} / {kind.MaxHealth}{open}   ({take} to take it down)";
    }

    /// <summary>Our team's structures and stock, for the scoreboard.</summary>
    private string TeamSummary()
    {
        if (Link?.Objects is not { } objects)
        {
            return "";
        }

        int standing = objects.All.Count(o => o.Team == Link.MyTeam);
        string stock = Link.TeamState is { Stock.Count: > 0 } state
            ? string.Join(", ", state.Stock.Select(s => $"{s.Key} {s.Value}"))
            : "nothing yet";
        string era = Creative || Link.TeamState is not { } team ? "" : $"{Techs.EraOf(team).Name}, {team.Researched.Count} techs. ";
        return $"Your team: {era}{standing} structure{(standing == 1 ? "" : "s")} standing. Stock: {stock}.";
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
