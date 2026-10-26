using Godot;
using IslandWar.Sim;

namespace IslandWar;

/// <summary>The engine side of <see cref="GameData"/>: reading the files from the project, and the tick rate.</summary>
public static class GameFiles
{
    /// <summary>Reads every file in <c>res://data/</c>.</summary>
    public static GameData LoadData() => GameData.Load(name => FileAccess.GetFileAsString($"res://data/{name}"));

    /// <summary>Makes Godot's physics tick match the simulation tick rate.</summary>
    public static void ApplyTickRate(this GameData data) => Engine.PhysicsTicksPerSecond = data.Net.TickRate;
}
