using System.Text;

namespace IslandWar.Sim.Match;

/// <summary>One connected player as everyone sees them.</summary>
/// <param name="ConcedeVote">They've voted for their team to concede.</param>
/// <param name="PingMs">Round trip to the server in milliseconds, as the server last measured it.</param>
/// <param name="PlayerId">The server's lasting id for this player (it survives a reconnect); blocks record it as their owner.</param>
public readonly record struct RosterEntry(int PeerId, string Name, Team Team, bool ConcedeVote = false, int PingMs = 0, int PlayerId = 0);

/// <summary>Player names: what a client asks for is cleaned up before anyone sees it.</summary>
public static class PlayerNames
{
    public const int MaxLength = 16;
    public const string Default = "Player";

    /// <summary>
    /// Keeps letters, digits, spaces and a little punctuation, trims and shortens the result, and adds a
    /// number if someone already has the name, so names are readable and unique.
    /// </summary>
    public static string Clean(string? requested, IEnumerable<string> taken)
    {
        var kept = new StringBuilder();
        foreach (char c in requested ?? "")
        {
            if (char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.')
            {
                kept.Append(c);
            }
        }

        string name = kept.ToString().Trim();
        if (name.Length > MaxLength)
        {
            name = name[..MaxLength].TrimEnd();
        }

        if (name.Length == 0)
        {
            name = Default;
        }

        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(name))
        {
            return name;
        }

        for (int n = 2; ; n++)
        {
            string suffix = $" {n}";
            string candidate = (name.Length + suffix.Length > MaxLength ? name[..(MaxLength - suffix.Length)].TrimEnd() : name) + suffix;
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
