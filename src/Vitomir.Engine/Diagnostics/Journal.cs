// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A machine-readable record of what happened in a play session.
//
// This is not the log. `Diag` writes prose for a person reading it live; the journal writes one
// tab-separated line per event, for somebody reading it **afterwards** — usually to answer "what
// actually happened in that fight?" from a run they did not watch.
//
// In play the combat overlay shows things happening too fast to give feedback on: a log of them can be
// checked after the game has run.
// That is why the file lands in the working directory rather than
// under the user profile: the working directory is the repo, and the repo is what can be read back.
//
// The format is deliberately dull: `ms<TAB>kind<TAB>key=value key=value ...`, one line per event,
// with a header block of `#` comments. It greps, it sorts, it diffs, and a column never moves.
using System.Globalization;
using System.Text;

namespace Vitomir.Engine.Diagnostics;

/// <summary>What kind of event a journal line records. The set is small on purpose.</summary>
public static class JournalKind
{
    /// <summary>The run's own facts, written once at the top.</summary>
    public const string Run = "run";
    /// <summary>A click, and the verb it came to.</summary>
    public const string Click = "click";
    /// <summary>An order given to a character — attack, walk, stop.</summary>
    public const string Order = "order";
    /// <summary>A blow rolled: who, at whom, with what, and what it came to.</summary>
    public const string Blow = "blow";
    /// <summary>A shot loosed: who, at whom, with what, from where and which way.</summary>
    public const string Shot = "shot";
    /// <summary>Harm applied outside a blow.</summary>
    public const string Harm = "harm";
    /// <summary>Somebody died.</summary>
    public const string Death = "death";
    /// <summary>A mind changed AI mode.</summary>
    public const string Mode = "mode";
    /// <summary>A mind took or dropped a target.</summary>
    public const string Target = "target";
    /// <summary>A periodic census, so a quiet stretch can be told from a stalled one.</summary>
    public const string Tick = "tick";
    /// <summary>Something happened that is not written yet, named once.</summary>
    public const string Gap = "gap";
    /// <summary>
    /// The story moving on: a conversation begun, a line said, a reply chosen, a conversation
    /// over, the camera taken for a cutscene or given back, experience handed out.
    /// </summary>
    public const string Story = "story";
    /// <summary>Something came out of a death, a chest or a barrel — the item, and where from.</summary>
    public const string Loot = "loot";
    /// <summary>A shop opened, a purchase, a sale, a refusal, the shop closed.</summary>
    public const string Shop = "shop";
    /// <summary>
    /// Health put back by an effect — a healing discipline, a potion, a tick of healing over time —
    /// with what was asked for, what fitted, and where it left the character.
    /// </summary>
    public const string Heal = "heal";
}

/// <summary>
/// The journal. Off unless a run asks for it, so nothing pays for it in the ordinary case:
/// <see cref="Current"/> is null and every caller makes a null check.
/// </summary>
public sealed class Journal : IDisposable
{
    /// <summary>The journal this run is writing to, or null when none was asked for.</summary>
    public static Journal? Current { get; set; }

    /// <summary>Where the file went, so the run can say so on its way out.</summary>
    public string Path { get; }

    /// <summary>How many lines have been written, for the closing summary.</summary>
    public int Lines { get; private set; }

    readonly StreamWriter writer;
    readonly Dictionary<string, int> counts = new(StringComparer.Ordinal);
    readonly HashSet<string> gaps = new(StringComparer.Ordinal);
    readonly object gate = new();

    Journal(string path, StreamWriter writer)
    {
        Path = path;
        this.writer = writer;
    }

    /// <summary>
    /// Opens a journal. <paramref name="where"/> is a folder or a file; a folder (or nothing) gets
    /// a file named for the moment the run started, which is what makes two runs comparable.
    /// </summary>
    public static Journal Open(string? where, string level, string? scene, IEnumerable<string> arguments)
    {
        string path = Resolve(where, level);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        // No byte-order mark: the file is meant to be grepped, and a BOM breaks the first match.
        var writer = new StreamWriter(path, append: false, new UTF8Encoding(false)) { AutoFlush = true };

        writer.WriteLine("# vitomir journal");
        writer.WriteLine($"# started\t{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        writer.WriteLine($"# level\t{level}");
        if (!string.IsNullOrEmpty(scene)) writer.WriteLine($"# scene\t{scene}");
        writer.WriteLine($"# arguments\t{string.Join(' ', arguments)}");
        writer.WriteLine("# columns\tms\tkind\tfields");

        return new Journal(path, writer);
    }

    static string Resolve(string? where, string level)
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string name = $"{stamp}-{Safe(level)}.tsv";
        // No path at all, or a folder: the run names its own file, under `journal/` beside wherever
        // the game was started from — which for a developer run is the repo.
        if (string.IsNullOrWhiteSpace(where)) return System.IO.Path.Combine("journal", name);
        if (Directory.Exists(where) || where.EndsWith('/') || where.EndsWith('\\')
            || string.IsNullOrEmpty(System.IO.Path.GetExtension(where)))
            return System.IO.Path.Combine(where, name);
        return where;

        static string Safe(string s) =>
            string.Concat(s.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_'));
    }

    /// <summary>One event. Fields are written in the order given and never reordered.</summary>
    public void Write(int ms, string kind, params (string Key, object? Value)[] fields)
    {
        var line = new StringBuilder();
        line.Append(ms.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(kind).Append('\t');
        bool first = true;
        foreach (var (key, value) in fields)
        {
            if (value is null) continue;
            if (!first) line.Append(' ');
            first = false;
            line.Append(key).Append('=').Append(Format(value));
        }
        lock (gate)
        {
            writer.WriteLine(line.ToString());
            Lines++;
            counts[kind] = counts.GetValueOrDefault(kind) + 1;
        }
    }

    /// <summary>
    /// Names something the engine does and Vitomir does not, once per run. The point is the count
    /// in the summary: "this fight hit the unwritten discipline path forty times" is a finding.
    /// </summary>
    public void Gap(int ms, string what, params (string Key, object? Value)[] fields)
    {
        lock (gate) { counts["gap:" + what] = counts.GetValueOrDefault("gap:" + what) + 1; }
        lock (gate) { if (!gaps.Add(what)) return; }
        Write(ms, JournalKind.Gap, [("what", what), .. fields]);
    }

    /// <summary>
    /// Values are written so that a number is a number and a string never breaks the format: tabs
    /// and newlines become spaces, and a value with a space in it is quoted.
    /// </summary>
    static string Format(object value)
    {
        string s = value switch
        {
            float f => f.ToString("0.##", CultureInfo.InvariantCulture),
            double d => d.ToString("0.##", CultureInfo.InvariantCulture),
            bool b => b ? "1" : "0",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };
        // A tab or a newline would split the line; a double quote would confuse the quoting. All
        // three are neutralised whether or not the value ends up quoted, so a parser can trust that
        // a field is `key=token` or `key="one token with spaces"` and nothing else.
        s = s.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ').Replace('"', '\'');
        return s.Contains(' ') ? '"' + s + '"' : s;
    }

    /// <summary>Writes the tally and closes the file. Safe to call twice.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (!writer.BaseStream.CanWrite) return;
            writer.WriteLine("# --- summary ---");
            foreach (var (kind, n) in counts.OrderByDescending(e => e.Value))
                writer.WriteLine($"# {kind}\t{n}");
            writer.WriteLine($"# ended\t{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            writer.Flush();
            writer.Dispose();
        }
    }
}
