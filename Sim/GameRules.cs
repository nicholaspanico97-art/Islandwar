using IslandWar.Sim.Players;
using IslandWar.Sim.Vehicles;
using IslandWar.Sim.Weapons;

namespace IslandWar.Sim;

/// <summary>The tunables a simulation step needs, bundled so the server and clients pass the same ones.</summary>
/// <param name="SeaLevel">Planes treat the water surface at this height as ground, and it stops bombs.</param>
public sealed record GameRules(PlayerSettings Player, PlaneSettings Plane, BombSettings Bomb, int SeaLevel);
