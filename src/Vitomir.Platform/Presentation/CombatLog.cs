// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the combat log on screen ("It's a bit hard to figure out what's happening to the
// guard, so maybe a a small running log window that's in one of the corners to show status").

namespace Vitomir.Platform.Presentation;

/// <summary>The corner of the picture the combat log's window stands in.</summary>
public enum LogCorner { TopRight, TopLeft, BottomRight, BottomLeft }

/// <summary>The combat log window's numbers (ENGINE_SANDBOX, ours: chosen by looking at a fight at 1080p and 1440p).</summary>
public sealed record CombatLogSettings
{
    /// <summary>The corner (the setting <c>combatLogCorner</c>; top right by default: the banner and HUD keep the top middle and the bottom, the status line the bottom left).</summary>
    public LogCorner Corner { get; init; } = LogCorner.TopRight;

    /// <summary>How many rows of text the window holds (the setting <c>combatLogLines</c>, 3 to 30; 10 by default): a long line wraps onto more than one.</summary>
    public int Rows { get; init; } = 10;

    /// <summary>The least and most rows the setting may give.</summary>
    public const int LeastRows = 3, MostRows = 30;

    /// <summary>The seconds of the simulation a line stays at full strength before it fades.</summary>
    public float HoldSeconds { get; init; } = 6f;

    /// <summary>The seconds it then fades over, to <see cref="Floor"/>.</summary>
    public float FadeSeconds { get; init; } = 6f;

    /// <summary>
    /// The strength an old line keeps (0.4): the window keeps the last lines readable after a fight, fainter, until newer push
    /// them out; 0 lets them go entirely (the window then hides when every line has).
    /// </summary>
    public float Floor { get; init; } = 0.4f;

    /// <summary>
    /// The lines the window takes: those whose subject (the word after the line's time, <c>12.34 s: guard: …</c>) is one of
    /// these — contacts, fights (blows begun, strikes, blocks, refusals, stuns, who fights whom), the guard's verdicts, harm,
    /// severing, Health's bands, the harness's stops, cancels and feints, dodges, powers, getting up, and staggers
    /// (when they are said) — and a Celerity swing's extra blows. Not the moves, landings, ladders, the pointer, perception,
    /// the player's stops at an edge or a wall (<c>stop: at …</c>), nor the developer's lines.
    /// </summary>
    public IReadOnlyList<string> Topics { get; init; } =
        ["contact", "fight", "guard", "harm", "sever", "health", "stop", "cancel", "dodge", "power", "up", "stagger", "stun", "parry", "clash"];
}

/// <summary>
/// The combat log (ours): the last lines the session said about the fight, kept for the window in a corner of the
/// picture. A ring of <see cref="Capacity"/> lines, each the very string the session made when it said it (no copy), with the
/// simulation's second it was said at and its number in order; what is not wanted (<see cref="Wanted"/>) is not kept.
/// <b>Presentation only</b>: never digested, never read by the simulation (the session offers it each line as it says it);
/// nothing is allocated when a line is taken or read.
/// </summary>
public sealed class CombatLog
{
    /// <summary>The most lines kept (the window shows the newest that fit).</summary>
    public const int Capacity = 64;

    readonly string[] texts = new string[Capacity];
    readonly double[] said = new double[Capacity];
    readonly long[] orders = new long[Capacity];
    int next, count;

    public CombatLogSettings Settings { get; set; } = new();

    /// <summary>How many lines are kept (at most <see cref="Capacity"/>).</summary>
    public int Count => count;

    /// <summary>How many lines have been taken since the start (the newest's number is this less one).</summary>
    public long Total { get; private set; }

    /// <summary>Forgets every line.</summary>
    public void Clear()
    {
        Array.Clear(texts);
        (next, count) = (0, 0);
    }

    /// <summary>A line the session said at <paramref name="seconds"/> of the simulation: kept when it is about the fight. Answers whether it was.</summary>
    public bool Offer(string line, double seconds)
    {
        if (!Wanted(line, Settings.Topics)) return false;
        Add(line, seconds);
        return true;
    }

    /// <summary>Keeps <paramref name="line"/> (whatever it says), the oldest let go when the ring is full.</summary>
    public void Add(string line, double seconds)
    {
        texts[next] = line;
        said[next] = seconds;
        orders[next] = Total++;
        next = (next + 1) % Capacity;
        if (count < Capacity) count++;
    }

    int Slot(int age) => (next - 1 - age + 2 * Capacity) % Capacity;

    /// <summary>The line <paramref name="age"/> back (0 the newest, up to <see cref="Count"/> less one).</summary>
    public string Line(int age) => (uint)age < (uint)count ? texts[Slot(age)] : "";

    /// <summary>The simulation's second the line <paramref name="age"/> back was said at.</summary>
    public double SaidAt(int age) => (uint)age < (uint)count ? said[Slot(age)] : double.NegativeInfinity;

    /// <summary>The line <paramref name="age"/> back's number in order (for a drawing's cache: a number is never reused).</summary>
    public long Order(int age) => (uint)age < (uint)count ? orders[Slot(age)] : -1;

    /// <summary>
    /// How strong the line <paramref name="age"/> back is drawn at <paramref name="now"/> (0 to 1): full for
    /// <see cref="CombatLogSettings.HoldSeconds"/>, then fading over <see cref="CombatLogSettings.FadeSeconds"/> to
    /// <see cref="CombatLogSettings.Floor"/>. A paused game keeps its lines (the simulation's time stands).
    /// </summary>
    public float Strength(int age, double now)
    {
        if ((uint)age >= (uint)count) return 0f;
        var s = Settings;
        double old = now - said[Slot(age)] - s.HoldSeconds;
        if (old <= 0) return 1f;
        float floor = Math.Clamp(s.Floor, 0f, 1f);
        if (s.FadeSeconds <= 0f || old >= s.FadeSeconds) return floor;
        return 1f - (1f - floor) * (float)(old / s.FadeSeconds);
    }

    /// <summary>Whether any line is still drawn at <paramref name="now"/> (the window hides when none is).</summary>
    public bool Shown(double now) => count > 0 && Strength(0, now) > 0f;

    /// <summary>
    /// Whether a line the session said is about the fight: its subject — the words after its time (<c>12.34 s: </c>) up to a
    /// colon, a space or a bracket — is one of <paramref name="topics"/> (<c>guard: …</c>, <c>contact 12: …</c>,
    /// <c>dodge refused: …</c>, <c>harm (quick): …</c>), or it tells a Celerity swing's extra blows. A player's stop at an edge or a
    /// wall (<c>stop: at …</c>) is the body's, not the fight's.
    /// </summary>
    public static bool Wanted(string line, IReadOnlyList<string> topics)
    {
        if (string.IsNullOrEmpty(line)) return false;
        var rest = line.AsSpan();
        int at = rest.IndexOf(" s: ", StringComparison.Ordinal);
        if (at >= 0 && at <= 12) rest = rest[(at + 4)..];
        if (rest.StartsWith("stop: at ", StringComparison.Ordinal)) return false;
        if (rest.IndexOf(" extra blow", StringComparison.Ordinal) >= 0) return true;
        for (int i = 0; i < topics.Count; i++)
        {
            var t = topics[i];
            if (string.IsNullOrEmpty(t) || !rest.StartsWith(t, StringComparison.Ordinal)) continue;
            if (rest.Length == t.Length || rest[t.Length] is ':' or ' ' or '(') return true;
        }
        return false;
    }
}
