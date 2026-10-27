using Godot;

namespace IslandWar.Client;

/// <summary>
/// Input action names and their default bindings. Registered from code at startup so the bindings are
/// readable here instead of serialized into project.godot. Movement uses physical keys, so WASD stays
/// in the same place on any keyboard layout.
/// </summary>
public static class InputActions
{
    public const string MoveForward = "move_forward";
    public const string MoveBack = "move_back";
    public const string MoveLeft = "move_left";
    public const string MoveRight = "move_right";
    public const string Jump = "jump";
    public const string Descend = "descend";
    public const string Sprint = "sprint";
    public const string Break = "break_block";
    public const string Place = "place_block";
    public const string ToggleFly = "toggle_fly";
    public const string Use = "use";
    public const string Concede = "concede";
    public const string Scoreboard = "scoreboard";
    public const string Craft = "craft";
    public const string Research = "research";
    public const string HotbarNext = "hotbar_next";
    public const string HotbarPrevious = "hotbar_previous";
    public const string ReleaseMouse = "release_mouse";

    /// <summary>Number of hotbar slots that have a number key (1 to 9, then 0).</summary>
    public const int HotbarKeys = 10;

    public static string HotbarSlot(int index) => $"hotbar_{index + 1}";

    public static void Register()
    {
        AddKey(MoveForward, Key.W);
        AddKey(MoveBack, Key.S);
        AddKey(MoveLeft, Key.A);
        AddKey(MoveRight, Key.D);
        AddKey(Jump, Key.Space);
        AddKey(Descend, Key.Ctrl);
        AddKey(Sprint, Key.Shift);
        AddKey(ToggleFly, Key.F);
        AddKey(Use, Key.E);
        AddKey(Concede, Key.F10);
        AddKey(Scoreboard, Key.Tab);
        AddKey(ReleaseMouse, Key.Escape);
        AddKey(Craft, Key.C);
        AddKey(Research, Key.R);
        AddMouse(Break, MouseButton.Left);
        AddMouse(Place, MouseButton.Right);
        AddMouse(HotbarNext, MouseButton.WheelDown);
        AddMouse(HotbarPrevious, MouseButton.WheelUp);
        for (int i = 0; i < HotbarKeys; i++)
        {
            AddKey(HotbarSlot(i), i == 9 ? Key.Key0 : Key.Key1 + i);
        }
    }

    private static void AddKey(string action, Key key) => Add(action, new InputEventKey { PhysicalKeycode = key });

    private static void AddMouse(string action, MouseButton button) => Add(action, new InputEventMouseButton { ButtonIndex = button });

    private static void Add(string action, InputEvent binding)
    {
        if (!InputMap.HasAction(action))
        {
            InputMap.AddAction(action);
        }

        InputMap.ActionAddEvent(action, binding);
    }
}
