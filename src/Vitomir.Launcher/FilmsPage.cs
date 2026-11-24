// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The launcher's Films page: improved versions of 1.1's films made on the player's own machine, chosen film by film.
using System.Globalization;
using Vitomir.Platform.Films;
using Vitomir.Platform.Setup;

namespace Vitomir.Launcher;

/// <summary>
/// **The Films page's working half** (ours): every one of 1.1's films with the version that plays (Original, Best or Fast), what each
/// version's state is (made, not made, being made, failed, too heavy for this machine), the tools fetched and removed, this machine
/// measured for an estimate of each film's time, the films made and stopped (<see cref="FilmMaker"/>), and the made versions removed
/// one by one or all together. The work runs on threads of its own; <see cref="Poll"/> takes its ends on the launcher's thread.
/// Nothing here knows SDL or the screen.
/// </summary>
public sealed class FilmsPage
{
    readonly Func<FFmpegFound?> ffmpeg;
    readonly Func<string?> baseFolder;
    readonly Action<string> say;
    readonly object gate = new();
    readonly Dictionary<string, (int Frames, int Height, double Seconds)> lengths = new(StringComparer.Ordinal);
    readonly Dictionary<(string Stem, FilmVersion V), FilmResult> lastResult = [];
    Task? fetching, describing;
    CancellationTokenSource? cancelFetch;
    (long Done, long Total) fetched;
    int seenEnds = -1, fetchEnds, describeEnds, seenFetch, seenDescribe;

    /// <param name="userFolder">The user folder: the films are kept in its <c>Films</c>, the tools in its <c>Tools</c>.</param>
    /// <param name="ffmpeg">The player's FFmpeg as the Setup page found it, or null.</param>
    /// <param name="baseFolder">The imported game's files, where 1.1's films are read from, or null.</param>
    /// <param name="say">The launcher's log.</param>
    /// <param name="tools">The tools (this machine's, unless a test's).</param>
    /// <param name="run">How the maker runs a program (a process, unless a test's).</param>
    public FilmsPage(string userFolder, Func<FFmpegFound?> ffmpeg, Func<string?> baseFolder, Action<string> say, FilmTools? tools = null,
                     Func<string, IReadOnlyList<string>, CancellationToken, Task<FilmRun>>? run = null)
    {
        // FFmpeg's ffprobe, beside it as the zip builds have it, else on the PATH (a system's packages may keep them apart).
        this.ffmpeg = () => ffmpeg() is not { } f ? null
            : f.FFprobe is not null ? f
            : (probeOnPath ??= OnPath("ffprobe") ?? "") is { Length: > 0 } p ? f with { FFprobe = p } : f;
        this.baseFolder = baseFolder;
        this.say = say;
        Versions = new FilmVersions(userFolder);
        Tools = tools ?? new FilmTools(userFolder);
        Maker = new FilmMaker(Versions, Tools, () => this.ffmpeg() is { } f ? (f.FFmpeg, f.FFprobe) : null, Source, say, run);
    }

    string? probeOnPath;

    static string? OnPath(string program)
    {
        string name = OperatingSystem.IsWindows() ? program + ".exe" : program;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            if (File.Exists(Path.Combine(dir.Trim('"'), name))) return Path.Combine(dir.Trim('"'), name);
        return null;
    }

    /// <summary>The made versions and the choices.</summary>
    public FilmVersions Versions { get; }

    /// <summary>The two programs.</summary>
    public FilmTools Tools { get; }

    /// <summary>The maker.</summary>
    public FilmMaker Maker { get; }

    /// <summary>Whether the player's FFmpeg, with its ffprobe, is there (the films are taken apart and written with it).</summary>
    public bool HasFFmpeg => ffmpeg() is { FFprobe: not null };

    /// <summary>Whether films are being made or the machine measured now.</summary>
    public bool Working => Maker.Working;

    /// <summary>Whether the tools are being fetched now.</summary>
    public bool Fetching { get { lock (gate) return fetching is { IsCompleted: false }; } }

    /// <summary>The fetch's bytes so far, of all, while it runs.</summary>
    public (long Done, long Total) Fetched { get { lock (gate) return fetched; } }

    /// <summary>Why the last fetch failed, or null.</summary>
    public string? FetchProblem { get; private set; }

    /// <summary>What the last start, stop or removal said, in words.</summary>
    public IReadOnlyList<PageLine> Result { get; private set; } = [];

    /// <summary>How many changes have been taken (the launcher fills the rows again when it moves).</summary>
    public int Changes { get; private set; }

    /// <summary>
    /// What the page shows from the disk, read once and kept: where each of 1.1's films is, which versions are made (their sizes, and
    /// whether too heavy here), and the tools. The launcher fills its screen every frame, so nothing there may touch the disk; this
    /// is read again only when something changes it (a film made, a removal, a fetch, the page opened).
    /// </summary>
    sealed record Snapshot(Dictionary<string, string?> Sources, Dictionary<(string Stem, FilmVersion V), (long Bytes, bool Heavy)> Made,
                           bool ToolsReady, long ToolsBytes, IReadOnlyList<FilmToolPack> Missing, long MissingBytes);

    Snapshot? snapshot;

    Snapshot Now
    {
        get
        {
            lock (gate) if (snapshot is { } kept) return kept;
            var sources = new Dictionary<string, string?>(StringComparer.Ordinal);
            string? root = baseFolder();
            var files = new List<string>();
            if (root is not null)
                foreach (var folder in new[] { Path.Combine(root, "video"), Path.Combine(root, "Video"), root })
                    if (Directory.Exists(folder)) files.AddRange(Directory.EnumerateFiles(folder));
            foreach (var film in FilmCatalog.All)
                sources[film.Stem] = files.FirstOrDefault(f => string.Equals(Path.GetFileName(f), film.File, StringComparison.OrdinalIgnoreCase));
            var made = new Dictionary<(string, FilmVersion), (long, bool)>();
            foreach (var (film, v, bytes) in Versions.Made()) made[(film.Stem, v)] = (bytes, Versions.Heavy(v, film.File));
            var (missing, missingBytes) = Tools.ToFetch();
            var taken = new Snapshot(sources, made, missing.Count == 0, Tools.Size(), missing, missingBytes);
            lock (gate) snapshot = taken;
            return taken;
        }
    }

    /// <summary>What the page shows read from the disk again at the next look (after anything that changes it).</summary>
    public void Refresh()
    {
        lock (gate) snapshot = null;
    }

    /// <summary>The page opened: the disk read again (the game may have been imported, a film removed by hand), the lengths looked up.</summary>
    public void Opened()
    {
        Versions.Reload();
        Refresh();
        Look();
    }

    /// <summary>1.1's own file of a film in the imported game (its <c>video\</c>, any case), or null.</summary>
    public string? Source(FilmEntry film) => Now.Sources.GetValueOrDefault(film.Stem);

    /// <summary>Whether a version of a film is made (as last read).</summary>
    public bool IsMade(FilmVersion v, FilmEntry film) => v == FilmVersion.Original || Now.Made.ContainsKey((film.Stem, v));

    /// <summary>A made version's size in bytes (as last read), or 0.</summary>
    public long MadeBytes(FilmVersion v, FilmEntry film) => Now.Made.TryGetValue((film.Stem, v), out var m) ? m.Bytes : 0;

    /// <summary>The made versions (as last read), in the list's order.</summary>
    public IReadOnlyList<(FilmEntry Film, FilmVersion Version, long Bytes)> MadeList() =>
        [.. from f in FilmCatalog.All from v in new[] { FilmVersion.Best, FilmVersion.Fast } where Now.Made.ContainsKey((f.Stem, v)) select (f, v, Now.Made[(f.Stem, v)].Bytes)];

    /// <summary>Whether both tools are there (as last read).</summary>
    public bool ToolsReady => Now.ToolsReady;

    /// <summary>The room the tools take (as last read).</summary>
    public long ToolsBytes => Now.ToolsBytes;

    /// <summary>The tools still to fetch and their bytes (as last read).</summary>
    public (IReadOnlyList<FilmToolPack> Missing, long Bytes) ToolsToFetch => (Now.Missing, Now.MissingBytes);

    /// <summary>A film's length and size as FFmpeg described it (once the page has looked), or null.</summary>
    public (int Frames, int Height, double Seconds)? Length(FilmEntry film)
    {
        lock (gate) return lengths.TryGetValue(film.Stem, out var l) ? l : null;
    }

    /// <summary>The page shown: the films' lengths looked up once (on a thread of its own), so their times can be estimated.</summary>
    public void Look()
    {
        lock (gate)
        {
            if (describing is not null || ffmpeg() is not { FFprobe: not null }) return;
            describing = Task.Run(async () =>
            {
                foreach (var film in FilmCatalog.All)
                    if (Source(film) is { } src)
                    {
                        var (frames, _, seconds, height) = await Maker.Describe(src, CancellationToken.None);
                        lock (gate) lengths[film.Stem] = (frames, height, seconds);
                    }
                lock (gate) describeEnds++;
            });
        }
    }

    // ---- what a cell says ----------------------------------------------------------------------------------------------------------

    /// <summary>A version of a film's state, as its cell shows it.</summary>
    public enum CellState { Original, Made, NotMade, NotFinished, Working, Failed }

    /// <summary>A version's cell: its state, and its words (made and its size, not finished, the share done, the estimate).</summary>
    public (CellState State, string Words) Cell(FilmEntry film, FilmVersion v)
    {
        if (v == FilmVersion.Original) return (CellState.Original, Source(film) is null ? "Not in the game" : "The original");
        if (Maker.Now is { } now && now.Job.Film.Stem == film.Stem && now.Job.Version == v && now.Step != FilmMaker.MeasuringStep)
            return (CellState.Working, $"{(int)(now.Share * 100)}%" + (now.Left is { } left ? $", {Time(left)} left" : ""));
        if (Now.Made.TryGetValue((film.Stem, v), out var m))
            return (CellState.Made, $"Made, {Megabytes(m.Bytes)}" + (m.Heavy ? "; too heavy to play smoothly on this PC" : ""));
        if (lastResult.TryGetValue((film.Stem, v), out var r) && r.Why is { } why) return (CellState.Failed, "Failed: " + why);
        string estimate = Estimate(film, v) is { } e ? $"about {Time(e)}" : "";
        if (Versions.Choice(film.File) == v)
            return (CellState.NotFinished, "Not finished" + (estimate.Length > 0 ? $" ({estimate})" : ""));
        return (CellState.NotMade, estimate.Length > 0 ? $"Not made yet ({estimate})" : "Not made yet");
    }

    /// <summary>The estimate of a version's time on this machine, once it is measured and the film's length is known, or null.</summary>
    public TimeSpan? Estimate(FilmEntry film, FilmVersion v)
    {
        if (Length(film) is not { Frames: > 0 } l || v == FilmVersion.Original) return null;
        // The pace of the last movie this PC made in the version, when there is one: truer than the short test.
        if (Versions.Pace(v) is { } pace) return TimeSpan.FromSeconds(pace * l.Frames * Math.Max(1, l.Height));
        return Maker.Speed?.For(v, l.Frames, l.Height);
    }

    /// <summary>Whether an estimate is known for a version (a test run, or a movie made in it).</summary>
    public bool Estimates(FilmVersion v) => Versions.Pace(v) is not null || Maker.Speed is not null;

    /// <summary>The films whose chosen version is not made yet (what Start makes), in the list's order.</summary>
    public IReadOnlyList<FilmJob> ToMake() =>
        [.. FilmCatalog.All.Where(f => Source(f) is not null).Select(f => new FilmJob(f, Versions.Choice(f.File)))
            .Where(j => j.Version != FilmVersion.Original && !IsMade(j.Version, j.Film))];

    /// <summary>The estimate of every film Start would make, or null when any is unknown.</summary>
    public TimeSpan? EstimateAll(IReadOnlyList<FilmJob> jobs)
    {
        var all = TimeSpan.Zero;
        foreach (var j in jobs)
            if (Estimate(j.Film, j.Version) is { } e) all += e;
            else return null;
        return all;
    }

    // ---- the choices -----------------------------------------------------------------------------------------------------------------

    /// <summary>A film's version chosen (kept at once: a made one plays from the next film the game shows).</summary>
    public void Choose(string stem, FilmVersion v)
    {
        if (FilmCatalog.Of(stem) is not { } film) return;
        Versions.Choose(film.File, v);
        say($"movies: {film.Title} plays {FilmVersions.Word(v)}");
        Changes++;
    }

    /// <summary>Every film's version chosen at once.</summary>
    public void ChooseAll(FilmVersion v)
    {
        foreach (var f in FilmCatalog.All) Versions.Choose(f.File, v);
        say($"movies: every movie plays {FilmVersions.Word(v)}");
        Changes++;
    }

    // ---- the work ---------------------------------------------------------------------------------------------------------------------

    /// <summary>The chosen films not made yet, started. Answers why not, or null.</summary>
    public string? Start()
    {
        var jobs = ToMake();
        if (jobs.Count == 0) return "every chosen movie is made already";
        lastResult.Clear();
        string? why = Maker.Start(jobs);
        Result = why is null ? [] : [new(char.ToUpperInvariant(why[0]) + why[1..] + ".", true)];
        Changes++;
        return why;
    }

    /// <summary>The film being made stopped; its work thrown away, the ones made kept.</summary>
    public void Stop()
    {
        Maker.Stop();
        Changes++;
    }

    /// <summary>This machine measured, for the estimates.</summary>
    public void Measure()
    {
        if (!ToolsReady || Working) return;
        Maker.Measure();
        Changes++;
    }

    /// <summary>The launcher closing (or moving to another user folder): the work stopped and waited for, so no program is left running.</summary>
    public void Shutdown()
    {
        Maker.Stop();
        Maker.Wait(TimeSpan.FromSeconds(15));
        lock (gate) cancelFetch?.Cancel();
        try { fetching?.Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
    }

    /// <summary>The tools fetched, on a thread of their own.</summary>
    public void Fetch()
    {
        lock (gate)
        {
            if (fetching is { IsCompleted: false } || ToolsReady) return;
            FetchProblem = null;
            cancelFetch = new CancellationTokenSource();
            var token = cancelFetch.Token;
            fetched = (0, ToolsToFetch.Bytes);
            fetching = Task.Run(async () =>
            {
                try
                {
                    await Tools.Fetch((done, total) => { lock (gate) fetched = (done, total); }, token);
                    say("movies: the tools are fetched");
                }
                catch (OperationCanceledException) { say("movies: the tools' fetch was cancelled"); }
                catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or TaskCanceledException)
                {
                    FetchProblem = e.Message.TrimEnd('.');
                    say($"movies: the tools could not be fetched: {e.Message}");
                }
                lock (gate) fetchEnds++;
            });
        }
        Changes++;
    }

    /// <summary>The fetch cancelled (whatever it had downloaded thrown away).</summary>
    public void CancelFetch()
    {
        lock (gate) cancelFetch?.Cancel();
    }

    /// <summary>One made version removed.</summary>
    public string? Remove(FilmEntry film, FilmVersion v)
    {
        string? why = Versions.Remove(v, film.File);
        Refresh();
        Result = why is null ? [new($"The {Word(v)} version of {film.Title} was removed.")] : [new($"It could not be removed: {why}.", true)];
        say($"movies: removed {FilmVersions.Word(v)} {film.Stem}{(why is null ? "" : $": {why}")}");
        Changes++;
        return why;
    }

    /// <summary>Every made version removed (every film plays the original again).</summary>
    public string? RemoveAll()
    {
        string? why = Versions.RemoveAll();
        Refresh();
        Result = why is null ? [new("All upscaled movies were removed; every movie plays the original again.")] : [new($"Some could not be removed: {why}.", true)];
        say($"movies: removed every improved movie{(why is null ? "" : $": {why}")}");
        Changes++;
        return why;
    }

    /// <summary>The tools removed.</summary>
    public string? RemoveTools()
    {
        string? why = Tools.Remove();
        Refresh();
        Result = why is null ? [new("The upscaling tools were removed. Your upscaled movies are kept and still play.")] : [new($"The tools could not be removed: {why}.", true)];
        say($"movies: removed the tools{(why is null ? "" : $": {why}")}");
        Changes++;
        return why;
    }

    /// <summary>The work's ends taken (once a frame). Answers whether anything changed.</summary>
    public bool Poll()
    {
        bool changed = false;
        int ends = Maker.Ends;
        if (ends != seenEnds)
        {
            seenEnds = ends;
            Refresh();
            foreach (var r in Maker.Results) lastResult[(r.Job.Film.Stem, r.Job.Version)] = r;
            if (!Maker.Working) Result = Summary(Maker.Results);
            changed = true;
        }
        lock (gate)
        {
            if (fetchEnds != seenFetch) { seenFetch = fetchEnds; snapshot = null; changed = true; }
            if (describeEnds != seenDescribe) { seenDescribe = describeEnds; changed = true; }
        }
        if (Working || Fetching) changed = true;                                  // the meter moves
        if (changed) Changes++;
        return changed;
    }

    /// <summary>What a finished start said: films made, the one stopped, the ones failed.</summary>
    static IReadOnlyList<PageLine> Summary(IReadOnlyList<FilmResult> results)
    {
        if (results.Count == 0) return [];
        var lines = new List<PageLine>();
        int made = results.Count(r => r.Made);
        if (made > 0) lines.Add(new(made == 1 ? "1 movie is finished and ready to play." : $"{made} movies are finished and ready to play."));
        foreach (var r in results.Where(r => r.Stopped))
            lines.Add(new($"{r.Job.Film.Title} was cancelled and its work discarded. Start again to make it."));
        foreach (var r in results.Where(r => r.Why is not null))
            lines.Add(new($"{r.Job.Film.Title} ({Word(r.Job.Version)}) failed: {r.Why}. Start again to retry it.", true));
        foreach (var r in results.Where(r => r.Heavy))
            lines.Add(new($"{r.Job.Film.Title} ({Word(r.Job.Version)}) is too heavy to play smoothly on this PC: it will play, but drop frames. The Fast version suits this PC better.", Caution: true));
        return lines;
    }

    /// <summary>A version as the page says it.</summary>
    public static string Word(FilmVersion v) => v switch { FilmVersion.Best => "Best", FilmVersion.Fast => "Fast", _ => "Original" };

    /// <summary>A time as the page says it: seconds under a minute, minutes under two hours, else hours and minutes.</summary>
    public static string Time(TimeSpan t) =>
        t.TotalSeconds < 60 ? "under a minute"
        : t.TotalMinutes < 120 ? $"{Math.Ceiling(t.TotalMinutes).ToString(CultureInfo.InvariantCulture)} min"
        : $"{(int)t.TotalHours} h {t.Minutes} min";

    /// <summary>A size in megabytes, as the page says it.</summary>
    public static string Megabytes(long bytes) => $"{Math.Max(bytes > 0 ? 1 : 0, (long)Math.Round(bytes / 1_000_000.0)).ToString("N0", CultureInfo.InvariantCulture)} MB";
}
