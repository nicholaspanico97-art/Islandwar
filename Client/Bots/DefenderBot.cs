using IslandWar.Sim.Match;

namespace IslandWar.Client.Bots;

/// <summary>
/// The other side of the nuke test (role <c>defender</c>): it holds a team so the match can start, waits
/// for the result, and passes if the other team won by hitting its core and its world matches the server's.
/// </summary>
public partial class DefenderBot : TestBot
{
    protected override void Start()
    {
        Log($"on {Net.MyTeam}; waiting for the match to end");
        WaitUntil(() => Net.Match.Stage == MatchStage.Ended, () =>
        {
            Log($"the match ended: {Net.Match.Winner} wins ({Net.Match.Reason})");
            if (Net.Match.Winner != Net.MyTeam.Other() || Net.Match.Reason != EndReason.CoreDestroyed)
            {
                Finish(false, $"expected {Net.MyTeam.Other()} to win by hitting our core");
                return;
            }

            CompareWithServer();
        });
    }
}
