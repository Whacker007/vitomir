// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Making improved versions of 1.1's films on the player's own machine, one film at a time, stoppable at any point.
using System.Diagnostics;
using System.Globalization;

namespace Vitomir.Platform.Films;

/// <summary>One film to make in one version.</summary>
public sealed record FilmJob(FilmEntry Film, FilmVersion Version);

/// <summary>How a film's making ended: made (and whether it is too heavy to play smoothly here), failed (why), or stopped.</summary>
public sealed record FilmResult(FilmJob Job, bool Made, bool Heavy, string? Why, bool Stopped);

/// <summary>What a program run answered: its exit code and what it wrote.</summary>
public sealed record FilmRun(int Code, string Output);

/// <summary>
/// How long each film would take on this machine, from a short measurement: seconds a source frame for each version (making it larger
/// and the frames between, and writing it), measured on a few frames of the first film there is.
/// </summary>
public sealed record FilmSpeed(double BestSecondsAFrame, double FastSecondsAFrame, double BestStartSeconds, double FastStartSeconds, int Height)
{
    /// <summary>
    /// The estimate for a film of <paramref name="frames"/> frames <paramref name="height"/> pixels high in a version: its frames at the
    /// measured pace (in proportion to their height), and the programs' starts.
    /// </summary>
    public TimeSpan For(FilmVersion v, int frames, int height)
    {
        double scale = Height > 0 && height > 0 ? height / (double)Height : 1;
        return v switch
        {
            FilmVersion.Best => TimeSpan.FromSeconds(frames * BestSecondsAFrame * scale + BestStartSeconds),
            FilmVersion.Fast => TimeSpan.FromSeconds(frames * FastSecondsAFrame * scale + FastStartSeconds),
            _ => TimeSpan.Zero,
        };
    }
}

/// <summary>
/// **The film maker** (ours). Each film in its turn: its frames and sound taken out of 1.1's film by the player's FFmpeg, each frame
/// made larger by Real-ESRGAN (Best: <c>realesrgan-x4plus-anime</c> at 4x; Fast: <c>realesr-animevideov3</c> at 2x), a frame made
/// between each two by RIFE 4.6 (twice the frames, the same length), and the whole written as an MP4 with 1.1's sound. **Checked
/// before it counts**: it must run as long as 1.1's film, to a tenth of a second, since a script may wait for a film's end; then it is
/// timed playing (too heavy to play smoothly here is marked, not refused) and moved in. Everything is made in the films' work folder,
/// emptied before and after each film.
///
/// **Stop** ends the film being made at once (its programs ended by their process ids) and throws its work away; the films made
/// before it stay made and play. The programs run below normal priority, so the machine stays usable, though anything else using
/// the graphics card slows them, and a game that takes the card's memory can make one fail (a failed film is only not made: start
/// it again). Runs on a thread of its own; the launcher takes its state from <see cref="Now"/> and <see cref="Results"/>.
/// </summary>
public sealed class FilmMaker
{
    readonly FilmVersions versions;
    readonly FilmTools tools;
    readonly Func<(string FFmpeg, string? FFprobe)?> ffmpeg;
    readonly Func<FilmEntry, string?> source;
    readonly Action<string> say;
    readonly Func<string, IReadOnlyList<string>, CancellationToken, Task<FilmRun>> run;
    readonly Func<double> seconds;
    readonly object gate = new();
    CancellationTokenSource? stop;
    Task? working;
    (FilmJob Job, string Step, double Share, TimeSpan? Left)? now;
    readonly List<FilmResult> results = [];

    /// <param name="versions">Where the films are kept.</param>
    /// <param name="tools">The two programs.</param>
    /// <param name="ffmpeg">The player's FFmpeg (and ffprobe beside it, if any), or null when there is none.</param>
    /// <param name="source">1.1's own file of a film, or null when the install has not got it.</param>
    /// <param name="say">The launcher's log.</param>
    /// <param name="run">How a program is run (a process, unless a test's).</param>
    /// <param name="seconds">The clock a measurement is timed by, in seconds (the machine's, unless a test's).</param>
    public FilmMaker(FilmVersions versions, FilmTools tools, Func<(string FFmpeg, string? FFprobe)?> ffmpeg, Func<FilmEntry, string?> source,
                     Action<string> say, Func<string, IReadOnlyList<string>, CancellationToken, Task<FilmRun>>? run = null, Func<double>? seconds = null)
    {
        var wall = Stopwatch.StartNew();
        this.seconds = seconds ?? (() => wall.Elapsed.TotalSeconds);
        this.versions = versions;
        this.tools = tools;
        this.ffmpeg = ffmpeg;
        this.source = source;
        this.say = say;
        this.run = run ?? RunProcess;
    }

    /// <summary>The step a measurement shows as (the launcher tells a measurement from a film by it).</summary>
    public const string MeasuringStep = "Testing this PC";

    /// <summary>Whether films are being made now (or measured).</summary>
    public bool Working { get { lock (gate) return working is { IsCompleted: false }; } }

    /// <summary>The film being made: which, the step it is at in plain words, how far (0 to 1), and the time left on it, once known.</summary>
    public (FilmJob Job, string Step, double Share, TimeSpan? Left)? Now { get { lock (gate) return now; } }

    /// <summary>How each film of the last start ended, in the order they ended.</summary>
    public IReadOnlyList<FilmResult> Results { get { lock (gate) return [.. results]; } }

    /// <summary>The last measurement of this machine, or null before one.</summary>
    public FilmSpeed? Speed { get; private set; }

    /// <summary>Why the last measurement or start could not be made, or null.</summary>
    public string? Problem { get; private set; }

    /// <summary>How many times a film's making or a measurement has ended (the launcher looks again when it moves).</summary>
    public int Ends { get { lock (gate) return ends; } }
    int ends;

    /// <summary>A film as ffprobe describes it: its frames, frame rate, length and height, or zeros (no ffprobe, or a file it cannot read).</summary>
    public async Task<(int Frames, double Rate, double Seconds, int Height)> Describe(string file, CancellationToken stop)
    {
        if (ffmpeg() is not { FFprobe: { } probe }) return (0, 0, 0, 0);
        var answer = await run(probe, ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=r_frame_rate,height:format=duration", "-of", "default=nw=1", file], stop);
        double rate = 0, seconds = 0;
        int height = 0;
        foreach (var line in answer.Output.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("r_frame_rate=", StringComparison.Ordinal)) rate = Ratio(t["r_frame_rate=".Length..]);
            else if (t.StartsWith("height=", StringComparison.Ordinal)) int.TryParse(t["height=".Length..], out height);
            else if (t.StartsWith("duration=", StringComparison.Ordinal)) double.TryParse(t["duration=".Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
        }
        return ((int)Math.Round(rate * seconds), rate, seconds, height);
    }

    // ---- measuring -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The frames a measurement makes, twice over: a few, then more, so that the programs' starts (their models loaded onto the card)
    /// fall out of the pace a frame. A few seconds on a fast card, a minute or two on a slow one.
    /// </summary>
    public const int MeasureFew = 2, MeasureMore = 10;

    /// <summary>
    /// **This machine measured**: a few frames of the first film there is made in each version, timed, so each film's time can be
    /// estimated before it is started. On a thread of its own; <see cref="Speed"/> holds the answer, <see cref="Problem"/> why not.
    /// </summary>
    public void Measure()
    {
        lock (gate)
        {
            if (working is { IsCompleted: false }) return;
            stop = new CancellationTokenSource();
            var token = stop.Token;
            Problem = null;
            working = Task.Run(async () =>
            {
                try { Speed = await Measuring(token); }
                catch (OperationCanceledException) { }
                catch (Exception e) { Problem = Plain(e); say($"films: the measurement failed: {e.Message}"); }
                finally { versions.CleanWork(); lock (gate) { now = null; ends++; } }
            });
        }
    }

    async Task<FilmSpeed?> Measuring(CancellationToken token)
    {
        var ff = ffmpeg() ?? throw new InvalidOperationException("FFmpeg is not found");
        // The trailer first (the size of all but the logo), else the first film there is.
        var film = FilmCatalog.All.OrderBy(f => f.Stem == "trailer" ? 0 : 1).FirstOrDefault(f => source(f) is not null)
                   ?? throw new InvalidOperationException("none of the movies is in the imported game");
        string src = source(film)!;
        var (_, rate, _, height) = await Describe(src, token);
        if (rate <= 0) rate = 30;
        string work = Path.Combine(versions.WorkFolder, "measure");
        string few = Path.Combine(work, "few"), more = Path.Combine(work, "more");
        Directory.CreateDirectory(few);
        Directory.CreateDirectory(more);
        lock (gate) now = (new FilmJob(film, FilmVersion.Best), MeasuringStep, 0, null);
        // From the middle of the film: its first frames are often black, which a program makes larger faster than a picture.
        await Must(ff.FFmpeg, ["-y", "-v", "error", "-ss", "8", "-i", src, "-frames:v", MeasureMore.ToString(CultureInfo.InvariantCulture), Path.Combine(more, "%08d.png")], token, "extracting frames for the test");
        var taken = Directory.GetFiles(more, "*.png").Order(StringComparer.Ordinal).ToList();
        if (taken.Count < MeasureMore) throw new InvalidOperationException("FFmpeg gave too few frames to time");
        foreach (var f in taken.Take(MeasureFew)) File.Copy(f, Path.Combine(few, Path.GetFileName(f)));
        var paces = new List<(double Frame, double Start)>();
        foreach (var (v, at) in new[] { (FilmVersion.Best, 0.05), (FilmVersion.Fast, 0.6) })
        {
            lock (gate) now = (new FilmJob(film, v), MeasuringStep, at, null);
            double a = await Timed(v, few, Path.Combine(work, "a"), MeasureFew, rate, token);
            double b = await Timed(v, more, Path.Combine(work, "b"), MeasureMore, rate, token);
            double frame = Math.Max(0.001, (b - a) / (MeasureMore - MeasureFew));
            paces.Add((frame, Math.Max(0, a - frame * MeasureFew)));
        }
        say($"films: measured {paces[0].Frame:0.###} s a frame (+{paces[0].Start:0.#} s) for Best, {paces[1].Frame:0.###} s (+{paces[1].Start:0.#} s) for Fast");
        return new FilmSpeed(paces[0].Frame, paces[1].Frame, paces[0].Start, paces[1].Start, height);
    }

    async Task<double> Timed(FilmVersion v, string orig, string work, int n, double rate, CancellationToken token)
    {
        string up = Path.Combine(work, "up-" + FilmVersions.Word(v)), between = Path.Combine(work, "between-" + FilmVersions.Word(v));
        double started = seconds();
        await Larger(v, orig, up, token);
        await Between(up, between, n, token);
        await Encode(v, between, null, rate * 2, Path.Combine(work, FilmVersions.Word(v) + ".mp4"), token);
        return seconds() - started;
    }

    // ---- making --------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// **The films started**: each job in its turn, on a thread of its own. Nothing is started while films are already being made,
    /// without FFmpeg, or without both tools.
    /// </summary>
    public string? Start(IReadOnlyList<FilmJob> jobs)
    {
        if (ffmpeg() is not { FFprobe: not null }) return "FFmpeg (with its ffprobe) is not found";
        if (!tools.Ready) return "the upscaling tools are not downloaded";
        lock (gate)
        {
            if (working is { IsCompleted: false }) return "movies are already being upscaled";
            stop = new CancellationTokenSource();
            var token = stop.Token;
            results.Clear();
            Problem = null;
            working = Task.Run(async () =>
            {
                versions.CleanWork();
                foreach (var job in jobs)
                {
                    if (token.IsCancellationRequested) break;
                    var result = await Make(job, token);
                    lock (gate) { results.Add(result); ends++; }
                    if (result.Stopped) break;
                }
                lock (gate) { now = null; ends++; }
            });
        }
        return null;
    }

    /// <summary>**Stop**: the film being made ended now and its work thrown away; the films made before it stay made.</summary>
    public void Stop()
    {
        lock (gate) stop?.Cancel();
    }

    /// <summary>Waits for the work to end (tests; the launcher's close).</summary>
    public void Wait(TimeSpan most)
    {
        Task? w;
        lock (gate) w = working;
        try { w?.Wait(most); } catch (AggregateException) { }
    }

    async Task<FilmResult> Make(FilmJob job, CancellationToken token)
    {
        string name = $"{job.Film.Title} ({(job.Version == FilmVersion.Best ? "Best" : "Fast")})";
        string work = Path.Combine(versions.WorkFolder, job.Film.Stem + "-" + FilmVersions.Word(job.Version));
        var clock = Stopwatch.StartNew();
        try
        {
            if (job.Version == FilmVersion.Original) return new(job, true, false, null, false);
            var ff = ffmpeg() ?? throw new InvalidOperationException("FFmpeg is not found");
            string src = source(job.Film) ?? throw new InvalidOperationException($"{job.Film.File} is not in the imported game");
            say($"films: making {name}");
            var (_, rate, seconds, height) = await Describe(src, token);
            if (rate <= 0) throw new InvalidOperationException("FFmpeg could not read the movie's frame rate");
            string orig = Path.Combine(work, "orig"), up = Path.Combine(work, "up"), between = Path.Combine(work, "between"), sound = Path.Combine(work, "sound.m4a");
            Directory.CreateDirectory(orig);

            void At(string step, double share) => Report(job, step, share, clock.Elapsed);
            At("Extracting the frames", 0);
            await Must(ff.FFmpeg, ["-y", "-v", "error", "-i", src, "-fps_mode", "passthrough", Path.Combine(orig, "%08d.png")], token, "extracting the frames");
            await run(ff.FFmpeg, ["-y", "-v", "error", "-i", src, "-vn", "-c:a", "aac", "-b:a", "256k", sound], token);
            token.ThrowIfCancellationRequested();
            int n = Directory.GetFiles(orig, "*.png").Length;
            if (n == 0) throw new InvalidOperationException("FFmpeg gave no frames");

            // The shares of the work each step takes, as the trial run measured them.
            (double from, double to) larger = job.Version == FilmVersion.Best ? (0.03, 0.55) : (0.03, 0.35),
                                        betweenAt = job.Version == FilmVersion.Best ? (0.55, 0.88) : (0.35, 0.80);
            await Watching(() => Larger(job.Version, orig, up, token), up, n, larger, "Upscaling the frames", job, clock, token);
            await Watching(() => Between(up, between, n, token), between, 2 * n, betweenAt, "Adding in-between frames", job, clock, token);
            At("Encoding the movie", betweenAt.to);
            string made = Path.Combine(work, "made.mp4");
            await Encode(job.Version, between, File.Exists(sound) && new FileInfo(sound).Length > 0 ? sound : null, rate * 2, made, token);

            At("Checking the result", 0.97);
            var (_, _, length, _) = await Describe(made, token);
            double want = seconds > 0 ? seconds : n / rate;
            if (Math.Abs(length - want) > 0.1)
                throw new InvalidDataException($"the result runs {length:0.00} s instead of the original's {want:0.00} s, so it was not used");
            bool heavy = await TooHeavy(ff.FFmpeg, made, Math.Min(4, want), token);

            string to = versions.PathOf(job.Version, job.Film.File);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Move(made, to, overwrite: true);
            versions.MarkHeavy(job.Version, job.Film.File, heavy);
            versions.SetPace(job.Version, clock.Elapsed.TotalSeconds / (n * (double)Math.Max(1, height)));
            say($"films: made {name} in {clock.Elapsed.TotalMinutes:0.0} min{(heavy ? "; too heavy to play smoothly here" : "")}");
            return new(job, true, heavy, null, false);
        }
        catch (OperationCanceledException)
        {
            say($"films: stopped {name}; its work is thrown away");
            return new(job, false, false, null, true);
        }
        catch (Exception e)
        {
            say($"films: {name} failed: {e.Message}");
            return new(job, false, false, Plain(e), false);
        }
        finally
        {
            try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    void Report(FilmJob job, string step, double share, TimeSpan elapsed)
    {
        // The time left, once enough is done for it to mean anything.
        TimeSpan? left = share > 0.05 ? TimeSpan.FromSeconds(elapsed.TotalSeconds / share * (1 - share)) : null;
        lock (gate) now = (job, step, Math.Clamp(share, 0, 1), left);
    }

    /// <summary>A step run while the frames it has written so far are counted, for the share done.</summary>
    async Task Watching(Func<Task> step, string folder, int expect, (double From, double To) span, string words, FilmJob job, Stopwatch clock, CancellationToken token)
    {
        Report(job, words, span.From, clock.Elapsed);
        var task = step();
        while (!task.IsCompleted)
        {
            await Task.WhenAny(task, Task.Delay(500, CancellationToken.None));
            int done = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.png").Count() : 0;
            Report(job, words, span.From + (span.To - span.From) * Math.Min(1, done / (double)Math.Max(1, expect)), clock.Elapsed);
        }
        await task;
    }

    Task Larger(FilmVersion v, string from, string to, CancellationToken token)
    {
        string program = tools.ProgramOf("realesrgan") ?? throw new InvalidOperationException("Real-ESRGAN is not downloaded");
        Directory.CreateDirectory(to);
        var (model, scale) = v == FilmVersion.Best ? ("realesrgan-x4plus-anime", "4") : ("realesr-animevideov3", "2");
        return Must(program, ["-i", from, "-o", to, "-n", model, "-s", scale, "-m", Path.Combine(Path.GetDirectoryName(program)!, "models"), "-f", "png"],
                    token, "upscaling the frames (Real-ESRGAN)");
    }

    Task Between(string from, string to, int n, CancellationToken token)
    {
        string program = tools.ProgramOf("rife") ?? throw new InvalidOperationException("RIFE is not downloaded");
        Directory.CreateDirectory(to);
        return Must(program, ["-i", from, "-o", to, "-m", Path.Combine(Path.GetDirectoryName(program)!, "rife-v4.6"), "-n", (2 * n).ToString(CultureInfo.InvariantCulture)],
                    token, "adding in-between frames (RIFE)");
    }

    Task Encode(FilmVersion v, string frames, string? sound, double rate, string to, CancellationToken token)
    {
        var ff = ffmpeg() ?? throw new InvalidOperationException("FFmpeg is not found");
        var args = new List<string> { "-y", "-v", "error", "-framerate", Rate(rate), "-i", Path.Combine(frames, "%08d.png") };
        if (sound is not null) args.AddRange(["-i", sound, "-map", "0:v", "-map", "1:a", "-c:a", "copy"]);
        args.AddRange(["-c:v", "libx264", "-preset", v == FilmVersion.Best ? "slow" : "medium", "-crf", "16", "-pix_fmt", "yuv420p", "-movflags", "+faststart", to]);
        return Must(ff.FFmpeg, args, token, "encoding the movie (FFmpeg)");
    }

    /// <summary>
    /// Whether this machine decodes the made film too slowly to play it smoothly: a few seconds decoded flat out, as the game decodes
    /// (into whole pictures), taking longer than four fifths of their playing time.
    /// </summary>
    async Task<bool> TooHeavy(string program, string film, double seconds, CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        var r = await run(program, ["-v", "error", "-hwaccel", "auto", "-i", film, "-t", seconds.ToString("0.###", CultureInfo.InvariantCulture),
                                    "-an", "-pix_fmt", "rgba", "-f", "null", "-"], token);
        return r.Code == 0 && clock.Elapsed.TotalSeconds > seconds * 0.8;
    }

    async Task Must(string program, IReadOnlyList<string> args, CancellationToken token, string what)
    {
        var r = await run(program, args, token);
        token.ThrowIfCancellationRequested();
        if (r.Code != 0)
        {
            string last = r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "";
            throw new InvalidOperationException($"{what} failed{(last.Length > 0 ? $" ({last})" : "")}");
        }
    }

    static string Rate(double rate)
    {
        // FFmpeg takes a rate as a ratio; 1.1's films run at rates like 7024968/468229, so to the thousandth of a frame.
        long top = (long)Math.Round(rate * 1000);
        return top.ToString(CultureInfo.InvariantCulture) + "/1000";
    }

    static double Ratio(string text)
    {
        int at = text.IndexOf('/');
        if (at < 0) return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double flat) ? flat : 0;
        return double.TryParse(text[..at], NumberStyles.Float, CultureInfo.InvariantCulture, out double top)
            && double.TryParse(text[(at + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out double bottom) && bottom != 0 ? top / bottom : 0;
    }

    static string Plain(Exception e) => e.Message.TrimEnd('.');

    /// <summary>A program run as a process below normal priority; on a stop, it and its children are ended by its process id.</summary>
    static async Task<FilmRun> RunProcess(string program, IReadOnlyList<string> args, CancellationToken token)
    {
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) start.ArgumentList.Add(a);
        using var p = Process.Start(start) ?? throw new InvalidOperationException($"{Path.GetFileName(program)} would not start");
        try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException) { }
        var output = p.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errors = p.StandardError.ReadToEndAsync(CancellationToken.None);
        try { await p.WaitForExitAsync(token); }
        catch (OperationCanceledException)
        {
            try { Process.GetProcessById(p.Id).Kill(entireProcessTree: true); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            try { await p.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)); } catch (TimeoutException) { }
            throw;
        }
        return new FilmRun(p.ExitCode, await output + await errors);
    }
}
