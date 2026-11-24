// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the combat log's window ("a small running log window that's in one of the corners
// to show status").
using System.Numerics;
using Vitomir.Platform.Interface;
using Vitomir.Platform.Presentation;

namespace Vitomir.Platform.Render.Overlay;

/// <summary>
/// The combat log's window, laid out without a GPU: each line wrapped at spaces to the window's width in ems, the rows
/// it takes kept by the line's number (a line is wrapped once, when it is first drawn, into fixed arrays; nothing is made per
/// picture), and where the window stands in its corner.
/// </summary>
internal static class LogLayout
{
    /// <summary>The lettering's size, interface units (22: 22 pixels at 1440 lines, 16.5 at 1080; the HUD's state words are 20).</summary>
    public const float Em = 22f;
    /// <summary>The window's text width, ems (44: about 90 letters of the book face, a third of a 16:9 picture).</summary>
    public const float WidthEms = 44f;
    /// <summary>A row's height, ems.</summary>
    public const float RowEms = 1.28f;
    /// <summary>The padding inside the window, ems, and the indent of a wrapped line's later rows.</summary>
    public const float PadEms = 0.55f, IndentEms = 1.5f;
    /// <summary>The most rows one line may take (a longer one is cut there).</summary>
    public const int MostRowsALine = 8;

    /// <summary>
    /// Wraps <paramref name="text"/> to <paramref name="widthEms"/> (its later rows <paramref name="indentEms"/> narrower), each
    /// rune's advance in ems from <paramref name="advance"/>: at the last space that fits, or within a word too long for a row.
    /// The rows go into <paramref name="starts"/> and <paramref name="lengths"/> (as many as they hold); answers how many.
    /// </summary>
    public static int Wrap(ReadOnlySpan<char> text, float widthEms, float indentEms, Func<int, float> advance, Span<int> starts, Span<int> lengths)
    {
        int rows = 0, start = 0;
        while (start < text.Length && rows < starts.Length)
        {
            float room = rows == 0 ? widthEms : widthEms - indentEms;
            float x = 0f;
            int i = start, lastSpace = -1, end = text.Length;
            while (i < text.Length)
            {
                int len = i + 1 < text.Length && char.IsSurrogatePair(text[i], text[i + 1]) ? 2 : 1;
                int rune = len == 2 ? char.ConvertToUtf32(text[i], text[i + 1]) : text[i];
                float a = advance(rune);
                if (text[i] == ' ') lastSpace = i;
                if (x + a > room && i > start)
                {
                    end = lastSpace > start ? lastSpace : i;
                    break;
                }
                x += a;
                i += len;
            }
            if (i >= text.Length) end = text.Length;
            starts[rows] = start;
            lengths[rows] = end - start;
            rows++;
            start = end;
            while (start < text.Length && text[start] == ' ') start++;
        }
        return rows;
    }

    /// <summary>
    /// The window's rectangle (pixels) for <paramref name="rows"/> rows in <paramref name="corner"/> of a <paramref name="w"/> ×
    /// <paramref name="h"/> picture at <paramref name="em"/> pixels to the em: kept off the edges by a margin, below the banner
    /// when there is one (<paramref name="bandBottom"/>), above the status line in the bottom left.
    /// </summary>
    public static (Vector2 Min, Vector2 Max) Box(LogCorner corner, int rows, float em, int w, int h, float bandBottom)
    {
        float unit = MathF.Max(1f, h / 360f);
        float margin = 8f * unit;
        float width = (WidthEms + 2f * PadEms) * em;
        float height = (rows * RowEms + 2f * PadEms) * em;
        bool right = corner is LogCorner.TopRight or LogCorner.BottomRight;
        bool top = corner is LogCorner.TopRight or LogCorner.TopLeft;
        float x = right ? w - margin - width : margin;
        float y = top ? MathF.Max(margin, bandBottom + margin) : h - (corner == LogCorner.BottomLeft ? 20f * unit : margin) - height;
        return (new Vector2(x, y), new Vector2(x + width, y + height));
    }

    /// <summary>A line's colour by its subject: the guard's steel blue, harm's pale red, a stagger's or stun's amber, a dodge's or cancel's green, a power's violet, the rest a warm white.</summary>
    public static Vector4 Colour(ReadOnlySpan<char> line)
    {
        int at = line.IndexOf(" s: ", StringComparison.Ordinal);
        var rest = at >= 0 && at <= 12 ? line[(at + 4)..] : line;
        if (rest.StartsWith("guard", StringComparison.Ordinal) || rest.StartsWith("parry", StringComparison.Ordinal) || rest.StartsWith("clash", StringComparison.Ordinal)) return new(0.72f, 0.86f, 1f, 1f);
        if (rest.StartsWith("harm", StringComparison.Ordinal) || rest.StartsWith("sever", StringComparison.Ordinal) || rest.StartsWith("health", StringComparison.Ordinal) || rest.StartsWith("stop", StringComparison.Ordinal)) return new(1f, 0.74f, 0.66f, 1f);
        if (rest.StartsWith("stagger", StringComparison.Ordinal) || rest.StartsWith("stun", StringComparison.Ordinal) || rest.IndexOf(" stunned ", StringComparison.Ordinal) >= 0) return new(1f, 0.86f, 0.52f, 1f);
        if (rest.StartsWith("dodge", StringComparison.Ordinal) || rest.StartsWith("cancel", StringComparison.Ordinal)) return new(0.76f, 0.96f, 0.76f, 1f);
        if (rest.StartsWith("power", StringComparison.Ordinal)) return new(0.86f, 0.77f, 1f, 1f);
        return new(0.94f, 0.93f, 0.89f, 1f);
    }

    /// <summary>The length of a line's time (<c>12.34 s:</c> with its colon), drawn fainter; 0 when it has none.</summary>
    public static int TimeLength(ReadOnlySpan<char> line)
    {
        int at = line.IndexOf(" s: ", StringComparison.Ordinal);
        return at >= 0 && at <= 12 ? at + 3 : 0;
    }
}

internal sealed unsafe partial class OverlayPass
{
    // The rows each line wraps to, by its number (Order) modulo the log's capacity: made once, when a line is first drawn.
    readonly long[] logWrapped = Enumerable.Repeat(-1L, CombatLog.Capacity).ToArray();
    readonly int[] logRows = new int[CombatLog.Capacity];
    readonly int[] logStarts = new int[CombatLog.Capacity * LogLayout.MostRowsALine];
    readonly int[] logLengths = new int[CombatLog.Capacity * LogLayout.MostRowsALine];
    Func<int, float>? advance;

    /// <summary>The rows of the log's line <paramref name="age"/> back (wrapped the first time it is asked for).</summary>
    int LogRows(CombatLog log, int age, out int slot)
    {
        long order = log.Order(age);
        slot = (int)(order % CombatLog.Capacity);
        if (logWrapped[slot] != order)
        {
            advance ??= rune => face.Glyphs.TryGetValue(rune, out var g) || face.Glyphs.TryGetValue('?', out g) ? g.Advance : 0.5f;
            int at = slot * LogLayout.MostRowsALine;
            logRows[slot] = LogLayout.Wrap(log.Line(age), LogLayout.WidthEms, LogLayout.IndentEms, advance,
                                           logStarts.AsSpan(at, LogLayout.MostRowsALine), logLengths.AsSpan(at, LogLayout.MostRowsALine));
            logWrapped[slot] = order;
        }
        return logRows[slot];
    }

    /// <summary>
    /// The combat log's window: the newest whole lines that fit its rows, the newest at the bottom, each faded by its
    /// strength at <paramref name="now"/>, on a translucent plate in its corner; the time of each line fainter, a line's colour by
    /// its subject (<see cref="LogLayout.Colour"/>). Nothing when no line is drawn.
    /// </summary>
    void LayLog(CombatLog log, double now, bool banner, int w, int h, float scale)
    {
        var s = log.Settings;
        int most = Math.Clamp(s.Rows, CombatLogSettings.LeastRows, CombatLogSettings.MostRows);
        // The newest lines whose rows fit (the newest always, cut to the window if it alone is taller).
        int used = 0, lines = 0;
        float strongest = 0f;
        for (int age = 0; age < log.Count; age++)
        {
            float strength = log.Strength(age, now);
            if (strength <= 0f) break;
            int rows = LogRows(log, age, out _);
            if (used + rows > most && lines > 0) break;
            used = Math.Min(most, used + rows);
            lines++;
            strongest = MathF.Max(strongest, strength);
        }
        if (lines == 0 || used == 0) return;
        float em = LogLayout.Em * new UiMetrics(w, h, scale).Pixel;
        float unit = MathF.Max(1f, h / 360f);
        var (min, max) = LogLayout.Box(s.Corner, used, em, w, h, banner ? 14f * unit * 1.6f : 0f);
        var plate = Theme.Colours["hudPlate"];
        Solid((min, max), new Vector4(plate.R, plate.G, plate.B, plate.A * 0.75f * strongest));
        Solid((min, new Vector2(max.X, min.Y + MathF.Max(1f, unit * 0.4f))), Faded(new Vector4(0.85f, 0.72f, 0.48f, 0.55f), strongest));   // a gold rule along its top
        float pad = LogLayout.PadEms * em, row = LogLayout.RowEms * em;
        // From the bottom row up: the newest line's rows last.
        int next = used;
        for (int age = 0; age < lines && next > 0; age++)
        {
            int rows = LogRows(log, age, out int slot);
            var text = log.Line(age).AsSpan();
            var colour = Faded(LogLayout.Colour(text), log.Strength(age, now));
            int time = LogLayout.TimeLength(text);
            int first = Math.Max(0, next - rows);
            for (int r = 0; r < rows && first + r < next; r++)
            {
                int at = slot * LogLayout.MostRowsALine + r;
                var part = text.Slice(logStarts[at], logLengths[at]);
                float y = min.Y + pad + (first + r) * row + em * 0.95f;
                float x = min.X + pad + (r == 0 ? 0f : LogLayout.IndentEms * em);
                if (r == 0 && time > 0 && time <= part.Length)
                {
                    Text(part[..time], new Vector2(x, y), em, colour with { X = colour.X * 0.7f, Y = colour.Y * 0.7f, Z = colour.Z * 0.7f });
                    x += Measure(part[..time]) * em;
                    part = part[time..];
                }
                Text(part, new Vector2(x, y), em, colour);
            }
            next = first;
        }
    }

    /// <summary>The width of <paramref name="text"/> in ems of the book face.</summary>
    float Measure(ReadOnlySpan<char> text)
    {
        float x = 0f;
        foreach (var rune in text.EnumerateRunes())
            if (face.Glyphs.TryGetValue(rune.Value, out var g) || face.Glyphs.TryGetValue('?', out g)) x += g.Advance;
        return x;
    }
}
