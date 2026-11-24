// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// **The round trip in the logs** (ours; his friends are across the US and in Australia, 150 to 250 ms away):
// each joined player's measured round trip, on the host's side and on the player's, in the session's log and so in the log bundle.
using System.Globalization;

namespace Vitomir.Engine.Net;

/// <summary>
/// **Each joined player's round trip, said in the log**: the
/// smoothed figure the transport keeps (<see cref="Connection.RoundTripMs"/>) with the least and the most measured since the player
/// joined (<see cref="Connection.RoundTripLeastMs"/>, <see cref="Connection.RoundTripMostMs"/>) and how many were measured, as one
/// line: on the host for each player (<see cref="GameServer"/>) and on a player's machine for the host (<see cref="GameClient"/>),
/// once a minute while the game goes on, when the player leaves or is lost, and when the session ends. The lines go where the
/// session's other <c>net:</c> lines go (the console and the program's log) and into the game's own log (category
/// <c>Vitomir.Net</c>), so <c>--log-bundle</c> and <c>/bundle</c> gather them; <c>/bundle</c>'s <c>identity.txt</c> has the last of each
/// too (<see cref="Last"/>). Nothing of the game reads them: they are timed by the transport's clock as its last poll read it.
/// </summary>
public static class RoundTrips
{
    /// <summary>How often a joined player's line is said while the game goes on (the transport's milliseconds).</summary>
    public const long EveryMs = 60_000;

    /// <summary>The words every line begins with (what a reader of the logs looks for).</summary>
    public const string Mark = "net: round trip ";

    static readonly Dictionary<string, string> last = new(StringComparer.Ordinal);
    static readonly object gate = new();

    /// <summary>
    /// The line for one connection: <c>net: round trip to Bob (1.2.3.4:4994): 212 ms smoothed, least 180 ms, most 262 ms, 1,204
    /// measured over 5 min 12 s</c>, and why it is said (<paramref name="when"/>: "", "; Bob disconnected", "; the session ended").
    /// </summary>
    public static string Line(string to, Connection c, long nowMs, string when = "")
    {
        var over = TimeSpan.FromMilliseconds(Math.Max(0, nowMs - c.StartedMs));
        string span = over.TotalMinutes >= 1 ? $"{(int)over.TotalMinutes} min {over.Seconds} s" : $"{over.Seconds} s";
        string measured = c.RoundTripsMeasured == 0
            ? "none measured yet"
            : string.Create(CultureInfo.InvariantCulture, $"{c.RoundTripMs:0} ms smoothed, least {c.RoundTripLeastMs:0} ms, most {c.RoundTripMostMs:0} ms, {c.RoundTripsMeasured:#,0} measured");
        return $"{Mark}{to} ({c.Remote}): {measured} over {span}{when}";
    }

    /// <summary>
    /// A line said: kept as the last of its connection (<paramref name="key"/>) for the bundle, written to the game's own log, and
    /// handed back for the session's own lines (its <c>Log</c> event).
    /// </summary>
    public static string Note(string key, string line)
    {
        lock (gate) last[key] = line;
        Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(Diagnostics.Diag.For("Vitomir.Net"), "{Line}", line);
        return line;
    }

    /// <summary>The last line said of each connection this process has had, in the order of their keys (for the bundle's <c>identity.txt</c>).</summary>
    public static IReadOnlyList<string> Last()
    {
        lock (gate) return [.. last.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Value)];
    }

    /// <summary>Whether a connection's line is due: a minute since the last (<paramref name="saidMs"/>, the transport's clock), or since it began.</summary>
    public static bool Due(Connection c, long nowMs, ref long saidMs)
    {
        long from = saidMs != 0 ? saidMs : c.StartedMs;
        if (nowMs - from < EveryMs) return false;
        saidMs = nowMs;
        return true;
    }
}
