using System;
using Godot;
using IslandWar.Client;
using IslandWar.Server;

namespace IslandWar;

/// <summary>Entry scene. Starts the dedicated server when launched with <c>--server</c>, otherwise the client.</summary>
public partial class Main : Node
{
    public override void _Ready()
    {
        LaunchArgs args;
        try
        {
            args = LaunchArgs.Parse(OS.GetCmdlineUserArgs());
        }
        catch (ArgumentException e)
        {
            GD.PushError(e.Message);
            GetTree().Quit(2);
            return;
        }

        AddChild(args.IsServer ? new ServerMain { Args = args } : new ClientMain { Args = args });
    }
}
