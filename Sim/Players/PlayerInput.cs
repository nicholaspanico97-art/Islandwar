namespace IslandWar.Sim.Players;

[Flags]
public enum InputButtons : byte
{
    None = 0,
    Jump = 1,
    Sprint = 2,
    Descend = 4,

    /// <summary>The player wants to be flying. Sent as a state, not a toggle, so a lost packet can't flip it.</summary>
    Fly = 8,

    /// <summary>
    /// Get in or out of a plane. Set on the one tick the key went down; the server acts on it once, because
    /// it runs each input sequence number once however many packets repeat it.
    /// </summary>
    Use = 16,
}

/// <summary>
/// One physics tick of player input. Clients send only these; the server runs the movement itself.
/// </summary>
/// <param name="Seq">Increases by one per tick, so the server can acknowledge inputs and drop repeats.</param>
/// <param name="MoveX">Strafe, -1 (left) to 1 (right).</param>
/// <param name="MoveZ">Walk, -1 (forward) to 1 (back), the same sign as Godot's -Z forward.</param>
/// <param name="Yaw">Facing around the vertical axis in radians; 0 faces -Z.</param>
/// <param name="Pitch">Look up or down in radians. Doesn't affect movement; other players see it.</param>
public readonly record struct PlayerInput(uint Seq, float MoveX, float MoveZ, float Yaw, float Pitch, InputButtons Buttons)
{
    public bool Has(InputButtons button) => (Buttons & button) != 0;
}
