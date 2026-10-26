using IslandWar.Sim.Players;
using IslandWar.Sim.Vehicles;

namespace IslandWar.Sim;

/// <summary>The tunables a simulation step needs, bundled so the server and clients pass the same ones.</summary>
/// <param name="SeaLevel">Planes treat the water surface at this height as ground.</param>
public sealed record GameRules(PlayerSettings Player, PlaneSettings Plane, int SeaLevel);
