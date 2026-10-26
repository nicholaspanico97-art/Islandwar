using IslandWar.Sim.Players;
using IslandWar.Sim.Protocol;

namespace IslandWar.Sim.Server;

/// <summary>
/// Runs every player (or the plane they fly) from their inputs, one input per tick at most, the same way
/// the client predicts it. Seats and triggers in the input go to <see cref="Vehicles"/> and <see cref="Weapons"/>.
/// </summary>
public sealed class Movement
{
    private readonly ServerState _state;
    private readonly Vehicles _vehicles;
    private readonly Weapons _weapons;

    public Movement(ServerState state, Vehicles vehicles, Weapons weapons)
    {
        _state = state;
        _vehicles = vehicles;
        _weapons = weapons;
    }

    public void InputsReceived(int peer, byte[] data)
    {
        if (!_state.Players.TryGetValue(peer, out ServerPlayer? player))
        {
            return;
        }

        try
        {
            player.Receive(WireFormat.DecodeInputs(data), _state.Data.Net);
        }
        catch (InvalidDataException e)
        {
            _state.Log($"[server] bad input packet from peer {peer}: {e.Message}");
        }
    }

    public void Step()
    {
        NetSettings net = _state.Data.Net;
        foreach (ServerPlayer player in _state.Players.Values)
        {
            player.Refill(net);
            while (player.TryTakeInput(out PlayerInput input))
            {
                // Noclip is a developer convenience; in a real match the server just ignores it.
                if (!_state.Options.Dev)
                {
                    input = input with { Buttons = input.Buttons & ~InputButtons.Fly };
                }

                // Getting in or out happens before the input's movement, and the client replays it the same way.
                if (input.Has(InputButtons.Use))
                {
                    _vehicles.ToggleSeat(player);
                }

                player.Pawn = Pawn.Step(_state.World, _state.Rules, player.Pawn, input, net.TickSeconds);
                if (player.Pawn.InPlane)
                {
                    ServerPlane plane = _state.Planes[player.Pawn.PlaneId];
                    plane.State = player.Pawn.Plane;
                    if (input.Has(InputButtons.Fire))
                    {
                        _weapons.TryDropBomb(player, plane);
                    }

                    if (input.Has(InputButtons.FireNuke))
                    {
                        _weapons.TryDropNuke(player, plane);
                    }
                }

                player.Processed(input);
            }

            if (!player.Pawn.InPlane && player.Pawn.Player.Position.Y < _state.Data.Player.RespawnBelowY)
            {
                player.Pawn = PawnState.OnFoot(player.Spawn);
            }
        }
    }
}
