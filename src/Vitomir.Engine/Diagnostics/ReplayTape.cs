// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The replay harness's tape: ours entirely — 1.1 has no such thing.
using System.Globalization;
using System.Text;

namespace Vitomir.Engine.Diagnostics;

/// <summary>
/// One line of a replay tape: a word saying what it is, then its fields, each a word with no spaces
/// (<see cref="ReplayTape.Escape"/> makes any text one). <c>in key-down code=4 …</c> is the kind <c>in</c>
/// and the fields <c>key-down</c>, <c>code=4</c>.
/// </summary>
public readonly record struct TapeLine(string Kind, string[] Fields)
{
    /// <summary>The field <c>name=value</c>'s value, or null when the line has none of that name.</summary>
    public string? this[string name]
    {
        get
        {
            foreach (var f in Fields)
                if (f.Length > name.Length && f[name.Length] == '=' && f.StartsWith(name, StringComparison.Ordinal))
                    return f[(name.Length + 1)..];
            return null;
        }
    }

    /// <summary>The field's value read as a float (round-trip), or <paramref name="otherwise"/>.</summary>
    public float Float(string name, float otherwise = 0f) =>
        this[name] is { } v && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : otherwise;

    /// <summary>The field's value read as a long (decimal, or hex after <c>0x</c>), or <paramref name="otherwise"/>.</summary>
    public long Long(string name, long otherwise = 0) => this[name] is { } v ? ReplayTape.Number(v, otherwise) : otherwise;

    public override string ToString() => Fields.Length == 0 ? Kind : Kind + " " + string.Join(' ', Fields);
}

/// <summary>
/// **The replay tape** (ours; <c>--replay</c> and <c>--replay-check</c></i>): what a
/// run's outcome depends on, one line a record, in the order the run read it. The head says how the run
/// was launched — its arguments (<c>arg</c>), the random seed (<c>seed</c>), the Fixes and quality-of-life
/// switches, the rules file's numbers and the key bindings in force (<c>fixes</c>, <c>qol</c>, <c>rules</c>, <c>keys</c>), the save it loaded
/// (<c>save</c>, with its SHA-256); then each frame of the window's loop is a <c>frame</c> line, the
/// input it read (<c>in</c> an event in the platform's neutral terms, <see cref="TapeInput"/>, or in a tape of format 1
/// <c>ev</c> SDL's; <c>none</c> where the poll found no more, <c>state</c> the mouse,
/// the modifiers and the keys held when they changed), and the step it took (<c>step</c>, in seconds,
/// written to round-trip exactly). <c>end</c> closes it. Lines starting <c>#</c> are comments.
/// A tape holds only inputs and names: never any of the game's content.
/// </summary>
public static class ReplayTape
{
    /// <summary>
    /// The first line of every tape this build writes; the number is the format's version. 2: the events are the
    /// platform's neutral ones (<c>in</c> lines); 1: SDL's (<c>ev</c> lines), still read (<see cref="TapeInput"/>).
    /// </summary>
    public const string Magic = "vitomir-tape 2";

    /// <summary>The first line of a tape of format 1 (SDL's events), which a reader still takes.</summary>
    public const string FirstMagic = "vitomir-tape 1";

    /// <summary>
    /// The tape a recording writes for what <c>--record</c> was given (ours). A file's name is the tape. A
    /// **folder** — a path that ends in a separator, or one that is a folder already — gets a tape of its own
    /// name in it, <c>play-&lt;date&gt;-&lt;time&gt;.tape.gz</c>, so the same launch line records every session
    /// to a new tape and none is written over (the play recordings kept as replay baselines).
    /// </summary>
    public static string NameIn(string given, DateTime now) =>
        given.Length > 0 && (given[^1] is '/' or '\\' || Directory.Exists(given))
            ? Path.Combine(given, $"play-{now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)}.tape.gz")
            : given;

    /// <summary>
    /// Any text as one field: <c>%</c>, spaces, tabs, line breaks, <c>=</c> and anything below a space are
    /// written <c>%XX</c> (their UTF-8 bytes), so a typed line or a path with spaces stays one word. The
    /// empty text is <c>%</c> alone.
    /// </summary>
    public static string Escape(string text)
    {
        if (text.Length == 0) return "%";
        var sb = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            int v = rune.Value;
            if (v <= 0x20 || v == '%' || v == '=' || v == 0x7f)
                sb.Append('%').Append(v.ToString("X2", CultureInfo.InvariantCulture));
            else sb.Append(rune.ToString());
        }
        return sb.ToString();
    }

    /// <summary>The text <see cref="Escape"/> wrote.</summary>
    public static string Unescape(string field)
    {
        if (field == "%") return "";
        if (!field.Contains('%')) return field;
        var bytes = new List<byte>(field.Length);
        for (int i = 0; i < field.Length; i++)
        {
            if (field[i] == '%' && i + 2 < field.Length
                && byte.TryParse(field.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            {
                bytes.Add(b);
                i += 2;
            }
            else if (char.IsHighSurrogate(field[i]) && i + 1 < field.Length) { bytes.AddRange(Encoding.UTF8.GetBytes(field.Substring(i, 2))); i++; }
            else bytes.AddRange(Encoding.UTF8.GetBytes(field[i].ToString()));
        }
        return Encoding.UTF8.GetString([.. bytes]);
    }

    /// <summary>A number as a tape writes it: decimal, or hex after <c>0x</c>.</summary>
    public static long Number(string text, long otherwise = 0) =>
        text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h) ? h : otherwise
            : long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : otherwise;

    /// <summary>A float written so it reads back the same bits (.NET's shortest round-trip form).</summary>
    public static string Float(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>One line read back, or null for a blank line or a comment.</summary>
    public static TapeLine? Parse(string line)
    {
        if (line.Length == 0 || line[0] == '#') return null;
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        return new TapeLine(parts[0], parts[1..]);
    }

    /// <summary>A line as a tape writes it.</summary>
    public static string Format(string kind, params string[] fields) => fields.Length == 0 ? kind : kind + " " + string.Join(' ', fields);
}

/// <summary>
/// A tape being written (<c>--record</c>). Every line goes out as it is made and the file is flushed at
/// each frame, so a run that crashes still leaves the tape up to its last whole frame.
/// </summary>
public sealed class TapeWriter : IDisposable
{
    readonly TextWriter output;
    /// <summary>The frames begun so far.</summary>
    public int Frames { get; private set; }

    public TapeWriter(TextWriter output, string? comment = null)
    {
        this.output = output;
        output.NewLine = "\n";
        output.WriteLine(ReplayTape.Magic);
        output.WriteLine("# Vitomir's replay tape: the launch, then each frame's input and step");
        if (comment is not null) output.WriteLine("# " + comment.Replace('\n', ' '));
    }

    /// <summary>Writes one line.</summary>
    public void Write(string kind, params string[] fields) => output.WriteLine(ReplayTape.Format(kind, fields));

    /// <summary>A comment line, which a reader passes over.</summary>
    public void Comment(string text) => output.WriteLine("# " + text.Replace('\n', ' '));

    /// <summary>Begins a frame of the window's loop.</summary>
    public void Frame()
    {
        Frames++;
        output.Flush();
        output.WriteLine(ReplayTape.Format("frame", Frames.ToString(CultureInfo.InvariantCulture)));
    }

    public void Dispose()
    {
        output.WriteLine("end");
        output.Flush();
        output.Dispose();
    }
}

/// <summary>
/// A tape being read back (<c>--replay</c>): its lines one at a time, from the first after the magic, in the
/// order the replayed run asks for them (<see cref="Head"/> is the launch's, before the first frame, to look at).
/// </summary>
public sealed class TapeReader
{
    readonly List<TapeLine> lines = [];
    int at;

    /// <summary>The launch's lines, everything before the first <c>frame</c>.</summary>
    public IReadOnlyList<TapeLine> Head { get; }

    /// <summary>The frames the tape holds.</summary>
    public int FrameCount { get; }

    /// <summary>The tape's format: 1 (SDL's events, <c>ev</c>) or 2 (the neutral ones, <c>in</c>).</summary>
    public int Format { get; }

    public TapeReader(TextReader input)
    {
        var first = input.ReadLine();
        Format = first == ReplayTape.Magic ? 2 : first == ReplayTape.FirstMagic ? 1
            : throw new InvalidDataException($"not a Vitomir replay tape this build reads (its first line is \"{first}\", not \"{ReplayTape.Magic}\" or \"{ReplayTape.FirstMagic}\")");
        var head = new List<TapeLine>();
        bool inHead = true;
        while (input.ReadLine() is { } text)
        {
            if (ReplayTape.Parse(text) is not { } line) continue;
            if (line.Kind == "frame") { inHead = false; FrameCount++; }
            if (inHead) head.Add(line);
            lines.Add(line);
        }
        Head = head;
    }

    public static TapeReader Open(string path)
    {
        using var reader = LogFile.OpenText(path);            // plain or gzip
        return new TapeReader(reader);
    }

    /// <summary>Every head line of that kind's fields, in order (<c>arg</c> gives the launch's arguments).</summary>
    public IEnumerable<string[]> HeadOf(string kind) => Head.Where(l => l.Kind == kind).Select(l => l.Fields);

    /// <summary>
    /// The size the recording's window had when its first loop began: the first <c>window</c> line (the front
    /// end's) or <c>interactive</c> line (the game's) that names one, wherever in the tape it is. A replay's
    /// window is made that size before anything is drawn. It has to be: the menus turn a click's pixel into a
    /// place on their 640 by 480 by the window's size, and a tape recorded from the title screen earlier
    /// names no size until the game's own loop begins, by which time the menus have been clicked through —
    /// A recording made in a picture of 2560 by 1440 was once replayed in the default
    /// 1600 by 900 and its click on Load never landed. Null when the tape names none.
    /// </summary>
    public (int Width, int Height)? FirstSize()
    {
        foreach (var line in lines)
            if (line.Kind is "window" or "interactive" && line["size"] is { } size && size.Split('x') is [var w, var h]
                && int.TryParse(w, NumberStyles.None, CultureInfo.InvariantCulture, out int width)
                && int.TryParse(h, NumberStyles.None, CultureInfo.InvariantCulture, out int height) && width > 0 && height > 0)
                return (width, height);
        return null;
    }

    /// <summary>The next line without taking it, or null at the end.</summary>
    public TapeLine? Peek => at < lines.Count ? lines[at] : null;

    /// <summary>Takes the next line, or null at the end.</summary>
    public TapeLine? Next() => at < lines.Count ? lines[at++] : null;

    /// <summary>Whether everything has been read (or only <c>end</c> is left).</summary>
    public bool Done => at >= lines.Count || lines[at].Kind == "end";

    /// <summary>How far through the lines the reading is, for a message.</summary>
    public int Position => at;
}
