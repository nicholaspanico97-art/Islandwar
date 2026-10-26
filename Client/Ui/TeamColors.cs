using Godot;
using IslandWar.Sim.Match;

namespace IslandWar.Client.Ui;

public static class TeamColors
{
    public static Color Of(Team team) => team switch
    {
        Team.Red => new Color(0.86f, 0.26f, 0.2f),
        Team.Blue => new Color(0.24f, 0.46f, 0.92f),
        _ => new Color(0.6f, 0.6f, 0.6f),
    };
}
