using Godot;
using IslandWar.Sim.Voxels;

namespace IslandWar.Server;

/// <summary>
/// Dedicated server entry. Generates the island and listens for clients. For M0 it only logs
/// connections; it does not replicate anything yet.
/// </summary>
public partial class ServerMain : Node
{
    private const int MaxClients = 32;

    public LaunchArgs Args { get; init; } = new() { IsServer = true };

    public VoxelWorld? World { get; private set; }

    public override void _Ready()
    {
        Name = "Server";

        IslandSettings terrain = IslandSettings.FromJson(FileAccess.GetFileAsString("res://data/terrain.json"));
        int seed = Args.Seed ?? terrain.DefaultSeed;
        World = IslandGenerator.Generate(terrain, seed);
        GD.Print($"[server] generated island: seed {seed}, {World.SizeX}x{World.SizeY}x{World.SizeZ} blocks, hash {World.ComputeHash():x16}");

        var peer = new ENetMultiplayerPeer();
        Error error = peer.CreateServer(Args.Port, MaxClients);
        if (error != Error.Ok)
        {
            GD.PushError($"[server] could not listen on port {Args.Port}: {error}");
            GetTree().Quit(1);
            return;
        }

        Multiplayer.MultiplayerPeer = peer;
        Multiplayer.PeerConnected += id => GD.Print($"[server] peer {id} connected");
        Multiplayer.PeerDisconnected += id => GD.Print($"[server] peer {id} disconnected");
        GD.Print($"[server] listening on port {Args.Port}");
    }
}
