// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Part of vitomir-tools: a folder of replays, and the program a tape is played by (Player).
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Ui;

namespace Vitomir;

/// <summary>
/// **`--replay-folder &lt;folder&gt; [--rebase] [--replay-jobs N]`: every recording of a folder, checked** (ours — the replay
/// harness's; checked</i>). The
/// rule a restructuring of the engine lands under is that every recording replays the same, and the
/// recordings worth most are a player's own play-tests: this is the one command that answers for all of them.
///
/// Each tape of the folder (<c>*.tape</c>, <c>*.tape.gz</c>, in name order) is given to <c>--replay-check</c>
/// **in a process of its own** — a run's state is the process's (the random streams, the quick save's slot,
/// the settings in force), so two tapes never share one — with this run's <c>--game</c>, <c>--data</c>,
/// <c>--gpu</c> and <c>--mute</c>. **Several run at once** (ours): each check uses
/// one processor thread, so <c>--replay-jobs N</c> of them run side by side (by default a quarter of the
/// machine's threads, at most six, never more than the tapes), each in a small window of its own laid out by its
/// <c>--replay-slot</c>. While a tape runs, the check's own lines are passed through with the tape's name before
/// them (its start, its progress every fifteen seconds, its answer); as each ends its row is printed: its name, its frames, the
/// seconds it took, and <i>the same</i>, <i>DIVERGED at frame N: …</i> or <i>could not run: …</i> (no digest,
/// no game, a crash, a window closed by hand before the end). The table
/// ends with a count and is written to <c>&lt;folder&gt;/replay-folder.txt</c> (written afresh each time)
/// under the build's version line and the date. The exit code is 0 when every tape agreed and 1 otherwise.
///
/// **<c>--rebase</c>** is for a recording a person made, which cannot be made again from its launch as a
/// scripted one can (<c>tools/replay/rerecord.py</c>), after a change that alters the game on purpose. A tape
/// that **diverged** is replayed whole (<c>--replay … --replay-fast</c>), its digest is kept once as
/// <c>&lt;tape&gt;.digest.&lt;build&gt;-before</c> (an earlier one of that name is left alone), and the replay's
/// digest becomes the tape's. A tape that agreed is not touched. A rebased tape is the same inputs played on
/// another game: from the frame it diverged at, the clicks may no longer do what the player meant. It still
/// pins what this build does with them. Not rebased: a tape whose replay leaves the tape (the game no longer
/// asks for its input in the recorded order — there is nothing whole to keep), and a digest recorded on
/// another platform (it diverges on the maths library's rounding, not on the game).
/// </summary>
static partial class ReplayFolder
{
    enum Outcome { Same, Diverged, Rebased, Failed }

    sealed record Row(string Name, string Frames, double Seconds, Outcome Outcome, string Text);

    /// <summary>What one run of the program said: its exit code, and its lines (the harness's own, and the last few of any kind).</summary>
    sealed record Ran(int Code, List<string> Harness, List<string> Last, double Seconds)
    {
        public string? Line(string start) => Harness.FirstOrDefault(l => l.StartsWith(start, StringComparison.Ordinal));
    }

    [GeneratedRegex(@", (\d+) frames: ")]
    private static partial Regex FramesOf();

    [GeneratedRegex(@"at frame (\d+)")]
    private static partial Regex FrameOf();

    /// <summary>
    /// The program each tape is played by, when one is named (<c>--replay-program &lt;path&gt;</c>); null: the platform's
    /// (<see cref="FindPlatform"/>), else this program when it is a game (<c>vitomir-watchlog</c>, which plays them as the old program did;
    /// <c>vitomir-tools</c> plays none).
    /// </summary>
    public static string? Player { get; set; }

    /// <summary>The platform's program's file name here.</summary>
    static string PlatformName => OperatingSystem.IsWindows() ? "vitomir-platform.exe" : "vitomir-platform";

    /// <summary>
    /// **Where the platform's program is**: beside this program (a developer's package with both), else the source tree's build
    /// of it in this program's configuration (<c>src/Vitomir.Platform.Host/bin/&lt;config&gt;/&lt;tfm&gt;/</c>, its modules laid out beside
    /// it), else null.
    /// </summary>
    public static string? FindPlatform()
    {
        string beside = Path.Combine(AppContext.BaseDirectory, PlatformName);
        if (File.Exists(beside)) return beside;
        var dir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (dir.Parent?.Parent?.Parent?.Parent is { } src && File.Exists(Path.Combine(src.FullName, "Vitomir.slnx")))
        {
            string built = Path.Combine(src.FullName, "Vitomir.Platform.Host", "bin", dir.Parent.Name, dir.Name, PlatformName);
            if (File.Exists(built)) return built;
        }
        return null;
    }

    /// <summary>Whether a program is the platform's (its tapes are given <c>--game classic --developer --oneone &lt;install&gt;</c>).</summary>
    internal static bool IsPlatform(string program) =>
        Path.GetFileNameWithoutExtension(program).StartsWith("vitomir-platform", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        int at = Array.IndexOf(args, "--replay-folder");
        string? folder = at + 1 < args.Length && !args[at + 1].StartsWith("--", StringComparison.Ordinal) ? args[at + 1] : null;
        if (folder is null || !Directory.Exists(folder))
        {
            Console.Error.WriteLine(folder is null ? "--replay-folder wants a folder of tapes" : $"replay-folder: {folder} is not a folder");
            return 2;
        }
        folder = Path.GetFullPath(folder);
        bool rebase = Array.IndexOf(args, "--rebase") >= 0;
        var tapes = Directory.GetFiles(folder)
            .Where(f => LogFile.Stem(f).EndsWith(".tape", StringComparison.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ThenBy(f => f, StringComparer.Ordinal).ToList();
        if (tapes.Count == 0)
        {
            Console.Error.WriteLine($"replay-folder: no tape (*.tape, *.tape.gz) in {folder}");
            return 2;
        }

        // The program the tapes are played by: the one named, else the platform's, else this one when it is a game.
        string? program = Player ?? FindPlatform()
            ?? (System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name == "vitomir-tools" ? null : Environment.ProcessPath);
        if (program is null)
        {
            Console.Error.WriteLine($"replay-folder: the tapes are played by vitomir-platform, and it is neither beside this program ({AppContext.BaseDirectory}) "
                + "nor built in its source tree: name it with --replay-program <path>");
            return 2;
        }
        bool platform = IsPlatform(program);
        // What every tape's process is given of this one's line: where the game is, the driver, and quiet. The platform's is the Classic
        // game's with the developer's switches (a tape's are a harness's), the install named as the platform names it (--oneone).
        var passed = new List<string>();
        if (platform) passed.AddRange(["--game", "classic", "--developer"]);
        foreach (var name in new[] { "--game", "--data", "--gpu" })
            if (Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length) passed.AddRange([platform && name == "--game" ? "--oneone" : name, args[i + 1]]);
        if (Array.IndexOf(args, "--mute") >= 0) passed.Add("--mute");

        int jobs = Jobs(args, tapes.Count);
        Console.WriteLine($"replay-folder: {Count(tapes.Count)} in {folder}{(rebase ? "; a tape that diverges is rebased" : "")}" +
                          $"{(jobs > 1 ? $"; {jobs} at a time" : "")}");
        Console.WriteLine($"replay-folder: played by {program}{(platform ? " --game classic --developer" : "")}");
        Spawned = program;
        int wide = tapes.Max(t => Path.GetFileName(t).Length);
        var done = new Row?[tapes.Count];
        var total = Stopwatch.StartNew();
        // Slots, so each running check's window has a place of its own on the screen.
        var slots = new System.Collections.Concurrent.ConcurrentBag<int>(Enumerable.Range(0, jobs));
        Parallel.For(0, tapes.Count, new ParallelOptions { MaxDegreeOfParallelism = jobs }, i =>
        {
            slots.TryTake(out int slot);
            try
            {
                var row = One(tapes[i], [.. passed, "--replay-slot", slot.ToString(CultureInfo.InvariantCulture)], rebase);
                done[i] = row;
                lock (Out) Console.WriteLine(Format(row, wide));
            }
            finally { slots.Add(slot); }
        });
        var rows = done.Select(r => r!).ToList();
        if (jobs > 1)
        {
            // The table again in the tapes' order, since they ended in their own.
            Console.WriteLine();
            foreach (var row in rows) Console.WriteLine(Format(row, wide));
        }
        string counts = Counts(rows, total.Elapsed.TotalSeconds);
        Console.WriteLine("replay-folder: " + counts);

        string report = Path.Combine(folder, "replay-folder.txt");
        try
        {
            using var w = new StreamWriter(report, append: false);
            w.WriteLine($"{FrontEnd.Version}, {DateTime.Now:yyyy-MM-dd HH:mm:ss}, {RuntimeInformation.RuntimeIdentifier}");
            w.WriteLine($"vitomir-tools --replay-folder {folder}{(rebase ? " --rebase" : "")}, the tapes played by {Path.GetFileName(program)}");
            w.WriteLine();
            foreach (var row in rows) w.WriteLine(Format(row, wide));
            w.WriteLine();
            w.WriteLine(counts);
            Console.WriteLine($"replay-folder: the table is in {report}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"replay-folder: the table could not be written to {report}: {e.Message}");
        }
        return rows.All(r => r.Outcome == Outcome.Same) ? 0 : 1;
    }

    /// <summary>One lock for every line written while checks run side by side.</summary>
    static readonly object Out = new();

    /// <summary>
    /// How many checks run at once: <c>--replay-jobs N</c>, else a quarter of the machine's threads (each check
    /// takes one, and the machine's other work and the graphics driver want room), at least one and at most six;
    /// never more than there are tapes.
    /// </summary>
    internal static int Jobs(string[] args, int tapes)
    {
        int at = Array.IndexOf(args, "--replay-jobs");
        int n = at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int asked) && asked > 0
            ? asked
            : Math.Clamp(Environment.ProcessorCount / 4, 1, 6);
        return Math.Max(1, Math.Min(n, tapes));
    }

    /// <summary>One tape: checked, and rebased when asked and it diverged.</summary>
    static Row One(string tape, List<string> passed, bool rebase)
    {
        string name = Path.GetFileName(tape);
        lock (Out) Console.WriteLine($"{name}: running");
        var check = Spawn(["--replay-check", tape, .. passed], name);
        string frames = check.Harness.Select(l => FramesOf().Match(l)).FirstOrDefault(m => m.Success)?.Groups[1].Value ?? "?";

        if (check.Code == 0 && check.Line("replay-check: the same") is not null)
            return new Row(name, frames, check.Seconds, Outcome.Same,
                           check.Harness.Any(l => l.StartsWith("replay-check: the same", StringComparison.Ordinal) && l.Contains(InputTape.MenusOnlyMark, StringComparison.Ordinal))
                               ? "the same (the menus only)" : "the same");

        // A window closed by hand ends the check before its tape does: no answer about the tape, and nothing to rebase.
        if (check.Line("replay: the window was closed") is not null)
            return new Row(name, frames, check.Seconds, Outcome.Failed, "could not run: the window was closed before the tape's end");

        // The first thing that went wrong, in the order it was said: a frame's digest, or the run leaving its tape.
        string? diverged = check.Harness.FirstOrDefault(l => l.StartsWith("replay-check: DIVERGED at frame ", StringComparison.Ordinal)
                                                          || l.StartsWith("replay: the run left its tape at frame ", StringComparison.Ordinal))
                           ?? check.Line("replay-check: DIVERGED");
        if (diverged is null)
            return new Row(name, frames, check.Seconds, Outcome.Failed, "could not run: " + Why(check));

        bool left = diverged.StartsWith("replay: the run left its tape", StringComparison.Ordinal);
        string text = left ? "DIVERGED at frame" + diverged["replay: the run left its tape at frame".Length..]
                           : diverged["replay-check: ".Length..];
        string? digest = LogFile.Find(LogFile.Beside(tape, ".digest"));
        string here = RuntimeInformation.RuntimeIdentifier;
        string? recordedOn = digest is null ? null : RecordedOn(digest);
        bool foreign = recordedOn is not null && recordedOn != here;
        if (foreign) text += $" (its digest was recorded on {recordedOn}; this is {here})";
        if (!rebase) return new Row(name, frames, check.Seconds, Outcome.Diverged, text);

        // ---- the rebase: the tape replayed whole, and its digest made the tape's ----
        if (foreign || left || digest is null)
            return new Row(name, frames, check.Seconds, Outcome.Diverged, text + " — not rebased: " +
                (foreign ? "a tape is replayed on the platform that recorded it" : left ? "its replay leaves the tape" : "it has no digest"));
        lock (Out) Console.WriteLine($"{name}: rebasing (the tape replayed whole)");
        var replay = Spawn(["--replay", tape, "--replay-fast", "--no-log-file", .. passed], name);
        double seconds = check.Seconds + replay.Seconds;
        string replayed = LogFile.Beside(tape, ".replay.digest");
        string? spoilt = replay.Line("replay: the run left its tape") ?? replay.Line("replay: the window was closed");
        if (replay.Code != 0 || spoilt is not null || !replay.Harness.Any(l => l.Contains(" frames replayed; digest ", StringComparison.Ordinal)) || !File.Exists(replayed))
            return new Row(name, frames, seconds, Outcome.Diverged, text + " — not rebased: " + (spoilt ?? "its replay did not finish: " + Why(replay)));
        try
        {
            string had = FrameOf().Match(text) is { Success: true } m ? $"had diverged at frame {m.Groups[1].Value}" : "had diverged: " + text["DIVERGED".Length..].TrimStart(' ', '—');
            string before = Rebase(digest, replayed, had);
            return new Row(name, frames, seconds, Outcome.Rebased, $"rebased ({had}); the digest before is {Path.GetFileName(before)}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new Row(name, frames, seconds, Outcome.Diverged, text + " — not rebased: " + e.Message);
        }
    }

    /// <summary>
    /// The build's name in a file's: its one word (<see cref="Vitomir.Platform.ReleaseVersion.Word"/>: the development tag, else the
    /// release version, <c>1.0.0-rc.1</c>), with nothing a file name cannot hold.
    /// </summary>
    internal static string Build
    {
        get
        {
            string word = Vitomir.Platform.ReleaseVersion.Word;
            return new string([.. word.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_')]);
        }
    }

    /// <summary>
    /// The digest a tape had, kept beside it under this build's name: <c>&lt;tape&gt;.digest.&lt;build&gt;-before</c>,
    /// compressed when the digest is (<c>x.tape.digest.gz</c> → <c>x.tape.digest.1.0-before.gz</c>, so it
    /// still reads as what it is).
    /// </summary>
    internal static string BeforeOf(string digest) => LogFile.Beside(digest, $".{Build}-before");

    /// <summary>
    /// The tape's digest put aside as <see cref="BeforeOf"/> — unless one of that name is there already, which
    /// is the digest before this build's first rebase and stays — and the replay's digest written in its
    /// place, its head saying what it is. The replay's own file goes. Answers the kept digest's path.
    /// </summary>
    static string Rebase(string digest, string replayed, string had)
    {
        string before = BeforeOf(digest);
        string made = LogFile.Beside(digest, ".rebasing");
        // Written whole beside the digest first, in the digest's own form, so a failure part-way leaves the tape's digest as it was.
        using (var from = LogFile.OpenText(replayed))
        using (var to = LogFile.CreateText(made))
        {
            to.NewLine = "\n";
            bool headed = false;
            while (from.ReadLine() is { } line)
            {
                // The replay's own head line (`# x.tape, replayed on <platform>`): this is the tape's digest now.
                if (!headed && line.StartsWith("# ", StringComparison.Ordinal) && line.Contains(", replayed on ", StringComparison.Ordinal))
                {
                    headed = true;
                    to.WriteLine(line.Replace(", replayed on ", ", recorded on ", StringComparison.Ordinal));
                    to.WriteLine($"# rebased {DateTime.Now:yyyy-MM-dd HH:mm:ss} by {FrontEnd.Version}: the tape's inputs replayed on that build; " +
                                 $"the digest before it {had} and is kept as {Path.GetFileName(before)}");
                    continue;
                }
                to.WriteLine(line);
            }
        }
        if (!File.Exists(before)) File.Move(digest, before);
        File.Move(made, digest, overwrite: true);
        File.Delete(replayed);
        return before;
    }

    /// <summary>The platform a digest was recorded on, from its head (<c># x.tape, recorded on win-x64</c>); null when it does not say.</summary>
    static string? RecordedOn(string digest)
    {
        try
        {
            using var r = LogFile.OpenText(digest);
            for (int i = 0; i < 6 && r.ReadLine() is { } line; i++)
                if (line.StartsWith('#') && line.Split(", recorded on ") is [_, var where]) return where.Trim();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { }
        return null;
    }

    /// <summary>Why a run gave no answer: the last thing the check said of itself, else the last line of any kind, and the exit code.</summary>
    static string Why(Ran ran)
    {
        bool Progress(string l) => l.Contains(" frames: ", StringComparison.Ordinal) || l.StartsWith("replay-check: frame ", StringComparison.Ordinal);
        string? said = ran.Harness.LastOrDefault(l => l.StartsWith("replay-check: ", StringComparison.Ordinal) && !Progress(l) && !l.Contains("not waiting for the screen", StringComparison.Ordinal))
                       ?? ran.Last.LastOrDefault(l => !Progress(l));
        if (said?.StartsWith("replay-check: ", StringComparison.Ordinal) == true) said = said["replay-check: ".Length..];
        return (said is null ? "" : said.Trim() + " ") + $"(exit code {ran.Code.ToString(CultureInfo.InvariantCulture)})";
    }

    /// <summary>
    /// This program again, with <paramref name="args"/>, its output read line by line: the harness's own lines
    /// (<c>replay…</c>) are passed through as they come, after <paramref name="name"/> (the tape's), and kept; of
    /// the rest the last few are kept, for a run that ended without an answer.
    /// </summary>
    /// <summary>The program this folder's tapes are played by (<see cref="Run"/> decides it).</summary>
    static string Spawned = "vitomir";

    static Ran Spawn(IReadOnlyList<string> args, string name)
    {
        string program = Spawned;
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        // Run as `dotnet vitomir.dll …`, the process is the host and the program its first argument.
        if (Path.GetFileNameWithoutExtension(program).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            && System.Reflection.Assembly.GetEntryAssembly()?.Location is { Length: > 0 } dll)
            start.ArgumentList.Add(dll);
        foreach (var a in args) start.ArgumentList.Add(a);
        var harness = new List<string>();
        var last = new Queue<string>();
        var gate = new object();
        void Line(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (gate)
            {
                if (line.StartsWith("replay", StringComparison.Ordinal)) { harness.Add(line); lock (Out) Console.WriteLine($"  {name}: {line}"); }
                last.Enqueue(line);
                if (last.Count > 8) last.Dequeue();
            }
        }
        var clock = Stopwatch.StartNew();
        try
        {
            using var p = new Process { StartInfo = start };
            p.OutputDataReceived += (_, e) => Line(e.Data);
            p.ErrorDataReceived += (_, e) => Line(e.Data);
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();
            return new Ran(p.ExitCode, harness, [.. last], clock.Elapsed.TotalSeconds);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return new Ran(-1, harness, [$"{program} could not be started: {e.Message}"], clock.Elapsed.TotalSeconds);
        }
    }

    static string Format(Row row, int wide) =>
        $"{row.Name.PadRight(wide)}  {row.Frames,8} frames  {row.Seconds.ToString("0.0", CultureInfo.InvariantCulture),8} s  {row.Text}";

    static string Counts(List<Row> rows, double seconds)
    {
        int Of(Outcome o) => rows.Count(r => r.Outcome == o);
        var parts = new List<string> { $"{Of(Outcome.Same)} the same", $"{Of(Outcome.Diverged)} diverged" };
        if (Of(Outcome.Rebased) > 0) parts.Add($"{Of(Outcome.Rebased)} rebased");
        parts.Add($"{Of(Outcome.Failed)} could not run");
        return $"{Count(rows.Count)}: {string.Join(", ", parts)} — {seconds.ToString("0.0", CultureInfo.InvariantCulture)} s";
    }

    static string Count(int tapes) => tapes == 1 ? "1 tape" : tapes.ToString(CultureInfo.InvariantCulture) + " tapes";
}
