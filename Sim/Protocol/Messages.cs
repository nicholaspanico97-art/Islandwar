using IslandWar.Sim.Weapons;

namespace IslandWar.Sim.Protocol;

/// <summary>Everything a client needs to rebuild the current world when it joins.</summary>
/// <param name="EditsData">Every edit since generation, packed with <see cref="WireFormat.EncodeEdits"/>.</param>
/// <param name="Hash">The server's world hash, so the client can check its rebuild.</param>
/// <param name="DevMode">The server allows developer conveniences (noclip flying).</param>
/// <param name="PresetName">The match preset, one of <see cref="GameData.Presets"/>.</param>
public sealed record WorldInit(int Seed, GameData Data, byte[] EditsData, ulong Hash, bool DevMode, string PresetName);

/// <summary>A bomb (or the nuke) left a plane at server tick <see cref="Tick"/>. Clients simulate its fall from here.</summary>
public sealed record BombDrop(int Id, int OwnerPeerId, uint Tick, BombState State, bool Nuke = false);

/// <summary>
/// A bomb hit something. <see cref="EditsData"/> is the crater, packed with <see cref="WireFormat.EncodeEdits"/>
/// (empty in the sea); <see cref="CraterRadius"/> sizes the effect. <see cref="Harmless"/> means it landed on
/// its own team's half, where bombs make no crater (team build protection).
/// </summary>
public sealed record Explosion(int BombId, System.Numerics.Vector3 Position, bool InWater, byte[] EditsData, int CraterRadius = 4, bool Nuke = false, bool Harmless = false);
