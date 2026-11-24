// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using Vitomir.Platform.Films;

namespace Vitomir.Platform.Tests;

/// <summary>
/// **The improved films** (ours): the versions kept and chosen (a chosen version not made never takes a film away; a removed one sends
/// its film back to the original), the tools fetched against their SHA-256 and removed, and the maker with stand-in programs: each
/// version made and checked, a stop that keeps the films made and throws the one in progress away, a film of the wrong length
/// refused, and the measurement's pace with the programs' starts taken out.
/// </summary>
public sealed class FilmMakerTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("vitomir-films-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    // ---- the versions -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_chosen_version_plays_only_once_made_and_a_removed_one_sends_its_film_back_to_the_original()
    {
        var v = new FilmVersions(root);
        Assert.Null(v.Playing("intro.bik"));
        Assert.Equal(FilmVersion.Original, v.Choice("intro.bik"));

        v.Choose("intro.bik", FilmVersion.Best);
        Assert.Equal(FilmVersion.Best, v.Choice("INTRO"));                         // any case, with or without the extension
        Assert.Null(v.Playing("intro.bik"));                                      // chosen, not made: the original plays

        Made(v, FilmVersion.Best, "intro.bik");
        Made(v, FilmVersion.Fast, "intro.bik");
        Assert.Equal(v.PathOf(FilmVersion.Best, "intro.bik"), v.Playing("intro.bik"));
        v.Choose("intro.bik", FilmVersion.Fast);                                  // changing the choice keeps the other version
        Assert.Equal(v.PathOf(FilmVersion.Fast, "intro.bik"), v.Playing("intro.bik"));
        Assert.True(v.Ready(FilmVersion.Best, "intro.bik"));

        Assert.Null(v.Remove(FilmVersion.Fast, "intro.bik"));
        Assert.Equal(FilmVersion.Original, v.Choice("intro.bik"));                // the version it played is gone: the original again
        Assert.True(v.Ready(FilmVersion.Best, "intro.bik"));
        Assert.Equal([("intro", FilmVersion.Best)], v.Made().Select(m => (m.Film.Stem, m.Version)));

        Assert.Null(v.RemoveAll());
        Assert.Empty(v.Made());
        Assert.False(Directory.Exists(v.Folder));
        Assert.Equal("nyc", FilmCatalog.Of("NYC.BIK")!.Stem);
        Assert.Equal(11, FilmCatalog.All.Count);
    }

    // ---- the tools ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_tools_are_fetched_against_their_SHA_256_keeping_only_the_files_needed_and_removed_whole()
    {
        byte[] zip = Zip(("pack/tool.exe", "program"), ("pack/models/m.bin", "model"), ("pack/sample.jpg", "not kept"));
        string sha = Convert.ToHexStringLower(SHA256.HashData(zip));
        var good = new FilmToolPack("realesrgan", "Real-ESRGAN", "https://example.invalid/a.zip", sha, zip.Length, "tool.exe", ["tool.exe", "models/m.bin"], "BSD 3-Clause", "");
        var tools = new FilmTools(root, [good], (_, _) => Task.FromResult<Stream>(new MemoryStream(zip)));
        Assert.False(tools.Ready);
        Assert.Equal(zip.Length, tools.ToFetch().Bytes);

        long seen = 0;
        await tools.Fetch((done, _) => seen = done, TestContext.Current.CancellationToken);
        Assert.True(tools.Ready);
        Assert.Equal(zip.Length, seen);
        Assert.Equal(["models/m.bin", "tool.exe"], Directory.GetFiles(tools.Folder, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(tools.FolderOf("realesrgan"), f).Replace('\\', '/')).Order(StringComparer.Ordinal));

        Assert.Null(tools.Remove());
        Assert.False(Directory.Exists(tools.Folder));

        // A download that is not the release: refused, and nothing of it left.
        var bad = new FilmTools(root, [good with { Sha256 = new string('0', 64) }], (_, _) => Task.FromResult<Stream>(new MemoryStream(zip)));
        var refused = await Assert.ThrowsAsync<InvalidDataException>(() => bad.Fetch(null, TestContext.Current.CancellationToken));
        Assert.Contains("SHA-256 differs", refused.Message);
        Assert.False(bad.Ready);
        Assert.Equal(0, bad.Size());
    }

    // ---- the maker ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Each_version_is_made_checked_and_moved_in_and_the_work_folder_is_emptied()
    {
        var (maker, versions, _) = Maker();
        Assert.Null(maker.Start([Job("intro.bik", FilmVersion.Best), Job("trailer.bik", FilmVersion.Fast)]));
        maker.Wait(TimeSpan.FromSeconds(30));

        Assert.All(maker.Results, r => Assert.True(r.Made, r.Why));
        Assert.True(versions.Ready(FilmVersion.Best, "intro.bik"));
        Assert.True(versions.Ready(FilmVersion.Fast, "trailer.bik"));
        Assert.False(versions.Ready(FilmVersion.Fast, "intro.bik"));
        Assert.Equal(FilmVersion.Original, versions.Choice("intro.bik"));          // making a version does not choose it
        Assert.NotNull(versions.Pace(FilmVersion.Best));                          // the real run's pace kept, for the estimates
        Assert.NotNull(versions.Pace(FilmVersion.Fast));
        Assert.False(Directory.Exists(versions.WorkFolder) && Directory.EnumerateFileSystemEntries(versions.WorkFolder).Any());
        Assert.Null(maker.Now);
    }

    [Fact]
    public void A_stop_keeps_the_films_made_and_throws_the_one_in_progress_away()
    {
        var hold = new ManualResetEventSlim();
        var (maker, versions, programs) = Maker();
        programs.HoldLarger = "trailer";                                          // the second film waits in Real-ESRGAN until stopped
        programs.Holding = hold;
        maker.Start([Job("intro.bik", FilmVersion.Fast), Job("trailer.bik", FilmVersion.Fast), Job("embrace.bik", FilmVersion.Fast)]);
        Assert.True(hold.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        Assert.Equal("trailer", maker.Now!.Value.Job.Film.Stem);
        maker.Stop();
        maker.Wait(TimeSpan.FromSeconds(30));

        Assert.Equal([(true, false), (false, true)], maker.Results.Select(r => (r.Made, r.Stopped)));   // the third never started
        Assert.True(versions.Ready(FilmVersion.Fast, "intro.bik"));
        Assert.False(versions.Ready(FilmVersion.Fast, "trailer.bik"));
        Assert.False(Directory.Exists(versions.WorkFolder) && Directory.EnumerateFileSystemEntries(versions.WorkFolder).Any());
        Assert.False(maker.Working);
    }

    [Fact]
    public void A_film_that_does_not_run_as_long_as_the_original_is_refused()
    {
        var (maker, versions, programs) = Maker();
        programs.ShortBy = 0.5;
        maker.Start([Job("intro.bik", FilmVersion.Best)]);
        maker.Wait(TimeSpan.FromSeconds(30));
        var r = Assert.Single(maker.Results);
        Assert.False(r.Made);
        Assert.Contains("not used", r.Why);
        Assert.False(versions.Ready(FilmVersion.Best, "intro.bik"));
    }

    [Fact]
    public void Nothing_starts_without_the_tools_or_FFmpeg()
    {
        var versions = new FilmVersions(root);
        var tools = new FilmTools(root, [Pack("realesrgan")]);
        var noTools = new FilmMaker(versions, tools, () => ("ffmpeg", "ffprobe"), _ => "x.bik", _ => { }, new Programs().Run);
        Assert.Equal("the upscaling tools are not downloaded", noTools.Start([Job("intro.bik", FilmVersion.Best)]));
        var noFFmpeg = new FilmMaker(versions, tools, () => null, _ => "x.bik", _ => { }, new Programs().Run);
        Assert.Contains("FFmpeg", noFFmpeg.Start([Job("intro.bik", FilmVersion.Best)]));
    }

    [Fact]
    public void The_measurement_takes_the_programs_starts_out_of_the_pace_a_frame()
    {
        var (maker, _, programs) = Maker();
        programs.StartCost = 2.0;                                                 // seconds a program run costs before its first frame
        programs.FrameCost = 0.5;                                                 // and each frame after it
        maker.Measure();
        maker.Wait(TimeSpan.FromSeconds(30));
        var speed = Assert.IsType<FilmSpeed>(maker.Speed);
        // Three programs a version, each 2 s to start: larger (0.5 s a frame), between (0.5 s a source frame) and written (0.5 s for
        // each of twice the frames): 2 s a source frame and 6 s of starts, as the stand-ins' clock counts.
        Assert.Equal(2.0, speed.BestSecondsAFrame, 6);
        Assert.Equal(6.0, speed.BestStartSeconds, 6);
        Assert.Equal(2.0, speed.FastSecondsAFrame, 6);
        Assert.Equal(346, speed.Height);
        Assert.Equal(100 * 2.0 * 480 / 346.0 + 6, speed.For(FilmVersion.Best, 100, 480).TotalSeconds, 3);
    }

    // ---- the stand-ins ------------------------------------------------------------------------------------------------------------

    (FilmMaker, FilmVersions, Programs) Maker()
    {
        var versions = new FilmVersions(root);
        var tools = new FilmTools(root, [Pack("realesrgan"), Pack("rife")]);
        foreach (var p in tools.Packs)
            foreach (var k in p.Keep) { var f = Path.Combine(tools.FolderOf(p.Id), k); Directory.CreateDirectory(Path.GetDirectoryName(f)!); File.WriteAllText(f, "x"); }
        var programs = new Programs();
        var maker = new FilmMaker(versions, tools, () => ("ffmpeg", "ffprobe"), f => Path.Combine(root, "Video", f.File), _ => { }, programs.Run, () => programs.Seconds);
        return (maker, versions, programs);
    }

    static FilmToolPack Pack(string id) => new(id, id, "", "", 0, id, [id], "", "");

    static FilmJob Job(string file, FilmVersion v) => new(FilmCatalog.Of(file)!, v);

    static void Made(FilmVersions v, FilmVersion version, string film)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(v.PathOf(version, film))!);
        File.WriteAllText(v.PathOf(version, film), "film");
    }

    static byte[] Zip(params (string Name, string Text)[] files)
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, text) in files)
                using (var w = new StreamWriter(z.CreateEntry(name).Open())) w.Write(text);
        return ms.ToArray();
    }

    /// <summary>
    /// FFmpeg, ffprobe, Real-ESRGAN and RIFE as the maker calls them, writing what each would: a source film of 30 frames at 15 a
    /// second, frames made larger one for one, twice the frames between, and a written film as long as its frames at its rate.
    /// </summary>
    sealed class Programs
    {
        readonly Dictionary<string, double> lengths = new(StringComparer.Ordinal);
        public string? HoldLarger;
        public ManualResetEventSlim? Holding;
        public double ShortBy, StartCost, FrameCost;
        public double Seconds;

        public async Task<FilmRun> Run(string program, IReadOnlyList<string> args, CancellationToken token)
        {
            string last = args[^1];
            string? Arg(string name) => args.ToList().IndexOf(name) is var i and >= 0 && i + 1 < args.Count ? args[i + 1] : null;
            if (program == "ffprobe")
            {
                double length = last.EndsWith(".mp4", StringComparison.Ordinal) ? lengths.GetValueOrDefault(last) : 2.0;
                return new(0, $"r_frame_rate=15/1\nheight=346\nduration={length.ToString(CultureInfo.InvariantCulture)}\n");
            }
            if (program == "ffmpeg")
            {
                if (args.Contains("-framerate"))
                {
                    int frames = Directory.GetFiles(Path.GetDirectoryName(Arg("-i")!)!, "*.png").Length;
                    string[] rate = Arg("-framerate")!.Split('/');
                    double r = double.Parse(rate[0], CultureInfo.InvariantCulture) / double.Parse(rate[1], CultureInfo.InvariantCulture);
                    Spend(frames);
                    File.WriteAllText(last, "mp4");
                    lengths[last] = frames / r - ShortBy;
                }
                else if (last.EndsWith("%08d.png", StringComparison.Ordinal))
                    Write(Path.GetDirectoryName(last)!, Arg("-frames:v") is { } k ? int.Parse(k, CultureInfo.InvariantCulture) : 30);
                else if (last.EndsWith(".m4a", StringComparison.Ordinal)) File.WriteAllText(last, "sound");
                return new(0, "");
            }
            if (program.EndsWith("realesrgan", StringComparison.Ordinal))
            {
                if (HoldLarger is { } stem && args.Any(a => a.Contains(stem + "-", StringComparison.Ordinal)))
                {
                    Holding?.Set();
                    await Task.Delay(Timeout.Infinite, token);
                }
                int n = Directory.GetFiles(Arg("-i")!, "*.png").Length;
                Spend(n);
                Write(Arg("-o")!, n);
                return new(0, "");
            }
            if (program.EndsWith("rife", StringComparison.Ordinal))
            {
                int n = int.Parse(Arg("-n")!, CultureInfo.InvariantCulture);
                Spend(n / 2);
                Write(Arg("-o")!, n);
                return new(0, "");
            }
            return new(1, "unknown program");
        }

        // The stand-ins' own clock, which the measurement is timed by.
        void Spend(int frames) => Seconds += StartCost + FrameCost * frames;

        static void Write(string folder, int n)
        {
            Directory.CreateDirectory(folder);
            for (int i = 1; i <= n; i++) File.WriteAllText(Path.Combine(folder, i.ToString("00000000", CultureInfo.InvariantCulture) + ".png"), "png");
        }
    }
}
