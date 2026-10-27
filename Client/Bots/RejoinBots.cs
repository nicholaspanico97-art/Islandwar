using System.Linq;
using IslandWar.Sim.Match;

namespace IslandWar.Client.Bots;

/// <summary>
/// Rejoin scenario, first half (role <c>leaver</c>): joins Red as <c>red-leaver</c> and drops out once the
/// match is under way. The returner comes back with the same identity token.
/// </summary>
public partial class LeaverBot : TestBot
{
    protected override void Start()
    {
        Log($"joined {Net.MyTeam} as '{Net.Player(Multiplayer.GetUniqueId())?.Name}'; waiting for the match to start");
        WaitUntil(() => Net.Match.Playing, () =>
        {
            // Disconnect properly, so the server frees the seat now rather than after a timeout, then exit.
            Log("the match is on; dropping out");
            Net.LeavingOnPurpose = true;
            Multiplayer.MultiplayerPeer.DisconnectPeer(1);
            GetTree().CreateTimer(0.5).Timeout += () => GetTree().Quit(0);
        });
    }
}

/// <summary>
/// Rejoin scenario, second half (role <c>returner</c>): connects with the leaver's token while asking for
/// Blue under another name. The server must seat it back on Red as <c>red-leaver</c> (developer mode would
/// otherwise grant Blue). Then Blue concedes, and it must see Red win.
/// </summary>
public partial class ReturnerBot : TestBot
{
    public const string ExpectedName = "red-leaver";

    protected override void Start()
    {
        WaitUntil(() => Net.Player(Multiplayer.GetUniqueId()) is not null, () =>
        {
            string name = Net.Player(Multiplayer.GetUniqueId())!.Value.Name;
            Log($"came back as '{name}' on {Net.MyTeam}");
            if (Net.MyTeam != Team.Red || name != ExpectedName)
            {
                Finish(false, $"expected the old seat back: '{ExpectedName}' on Red");
                return;
            }

            WaitUntil(() => Net.Match.Stage == MatchStage.Ended, () =>
            {
                Log($"the match ended: {Net.Match.Winner} wins ({Net.Match.Reason})");
                if (Net.Match.Winner != Team.Red || Net.Match.Reason != EndReason.Concede)
                {
                    Finish(false, "expected Red to win by Blue conceding");
                    return;
                }

                CompareWithServer();
            });
        });
    }
}

/// <summary>
/// Rejoin scenario, the other side (role <c>conceder</c>, Blue): waits until Red's player has left and come
/// back, then votes to concede. As Blue's only player its vote is enough, so Red must win by concession.
/// </summary>
public partial class ConcederBot : TestBot
{
    private int? _firstRedPeer;

    protected override void Start()
    {
        Log($"on {Net.MyTeam}; waiting for Red to leave and come back");
        WaitUntil(RedCameBack, () =>
        {
            Log("Red is back; voting to concede");
            Net.SetConcedeVote(true);
            WaitUntil(() => Net.Match.Stage == MatchStage.Ended, () =>
            {
                Log($"the match ended: {Net.Match.Winner} wins ({Net.Match.Reason})");
                if (Net.Match.Winner != Team.Red || Net.Match.Reason != EndReason.Concede)
                {
                    Finish(false, "expected Red to win by our concession");
                    return;
                }

                CompareWithServer();
            });
        });
    }

    /// <summary>
    /// Red's player is back on a new connection during the match. Going by the peer id, not by seeing Red
    /// empty, because the leave and the return can both arrive between two frames.
    /// </summary>
    private bool RedCameBack()
    {
        int? redPeer = Net.Roster.Values.Where(p => p.Team == Team.Red).Select(p => (int?)p.PeerId).FirstOrDefault();
        _firstRedPeer ??= redPeer;
        return redPeer is { } now && now != _firstRedPeer && Net.Match.Playing;
    }
}
