using IslandWar.Sim.Players;
using IslandWar.Sim.Vehicles;
using IslandWar.Sim.Weapons;

namespace IslandWar.Sim;

/// <summary>The tunables a simulation step needs, bundled so the server and clients pass the same ones.</summary>
/// <param name="SeaLevel">Planes treat the water surface at this height as ground, ships float on it, and it stops bombs.</param>
/// <param name="Ships">Every kind of ship, by id.</param>
public sealed record GameRules(PlayerSettings Player, PlaneSettings Plane, BombSettings Bomb, int SeaLevel, IReadOnlyDictionary<string, ShipKind>? Ships = null)
{
    /// <summary>A ship's kind; throws for an unknown one (the data is checked when it loads).</summary>
    public ShipKind Ship(string id) => Ships?.GetValueOrDefault(id) ?? throw new InvalidDataException($"No ship kind '{id}'.");
}
