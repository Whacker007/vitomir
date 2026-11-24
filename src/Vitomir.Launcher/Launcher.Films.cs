// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The launcher's Movies page (its key "films"): its actions, its notices (every removal and every start asked first, in plain words), its data.
using Vitomir.Platform.Films;
using Vitomir.Platform.Interface;

namespace Vitomir.Launcher;

public sealed partial class Launcher
{
    /// <summary>The Films page: improved versions of 1.1's films, made on this machine and chosen film by film (ours).</summary>
    public FilmsPage Films { get; private set; }

    FilmsPage NewFilms(string folder) => options.NewFilms?.Invoke(folder) ?? new FilmsPage(folder, () => Setup.FFmpeg, BaseFolder, say);

    /// <summary>The warnings the page opens with, and the start asks again: long work, the machine shared, what stays until removed.</summary>
    static readonly PageLine[] FilmWarnings =
    [
        new("Upscaling takes a long time. On a fast graphics card, all eleven movies take about an hour in Best and 15 minutes in Fast; on a laptop or an older card it can take many hours, Best especially. Movies are made one at a time, so you can cancel at any point and continue later.", Caution: true),
        new("Using the PC for anything else while movies are being made, gaming above all, slows the work down a lot. A game that takes the graphics card's memory can make a movie fail; if that happens, just start it again.", Caution: true),
        new("Upscaled movies and the tools stay on this PC until you remove them here. Switching a movie to another version keeps the old one."),
    ];

    /// <summary>A Films page action; answers whether it was one.</summary>
    bool HandleFilms(UiAction a)
    {
        var f = Films;
        switch (a.Name)
        {
            case "filmchoice" when Split(a.Argument) is { } choice:
                f.Choose(choice.Stem, choice.Version);
                return true;
            case "filmall" when FilmVersions.Parse(a.Argument) is { } all:
                f.ChooseAll(all);
                return true;
            case "filmmeasure":
                f.Measure();
                return true;
            case "filmcancelmeasure":
                f.Stop();
                return true;
            case "filmfetch":
                AskFetch();
                return true;
            case "filmcancelfetch":
                f.CancelFetch();
                return true;
            case "filmstart":
                AskStart();
                return true;
            case "filmstop":
                AskStop();
                return true;
            case "filmremove" when Split(a.Argument) is { } one && FilmCatalog.Of(one.Stem) is { } film:
                AskRemove(film, one.Version);
                return true;
            case "filmremoveall":
                AskRemoveAll();
                return true;
            case "filmremovetools":
                AskRemoveTools();
                return true;
        }
        return false;
    }

    static (string Stem, FilmVersion Version)? Split(string arg) =>
        arg.Split(':') is [var stem, var word] && FilmVersions.Parse(word) is { } v ? (stem, v) : null;

    void AskFetch()
    {
        var (missing, bytes) = Films.ToolsToFetch;
        if (missing.Count == 0) return;
        var lines = new List<PageLine> { new("Vitomir doesn't include these programs. It downloads them from their makers' official releases on GitHub, checks each one against a known fingerprint (SHA-256) so nothing else is used, and keeps only the files it needs in your user folder's Tools folder.") };
        foreach (var p in missing)
            lines.Add(new($"{p.Name}: {FilmsPage.Megabytes(p.Bytes)} ({p.Licence} licence; {p.Home})."));
        lines.Add(new($"{FilmsPage.Megabytes(bytes)} in all. They are portable: nothing is installed in Windows. They stay until you remove them on this page, which you can do safely at any time, for example once your movies are done."));
        Notice = new("filmfetch", "Download the upscaling tools?", lines,
            [("Download", () => { Notice = null; Films.Fetch(); }), ("Cancel", () => Notice = null)], Default: 0, Cancel: 1);
    }

    void AskStart()
    {
        var jobs = Films.ToMake();
        if (jobs.Count == 0) return;
        var lines = new List<PageLine>
        {
            new($"{string.Join(", ", jobs.Select(j => $"{j.Film.Title} ({FilmsPage.Word(j.Version)})"))}."),
            Films.EstimateAll(jobs) is { } all
                ? new($"About {FilmsPage.Time(all)} on this PC.")
                : new("No estimate yet: run Test this PC on this page for one."),
        };
        lines.AddRange(FilmWarnings.Take(2));
        lines.Add(new("Each movie plays as soon as it's done. You can cancel at any time: finished movies are kept, and the one in progress is discarded."));
        Notice = new("filmstart", jobs.Count == 1 ? "Upscale this movie?" : $"Upscale these {jobs.Count} movies?", lines,
            [("Start", () => { Notice = null; Films.Start(); }), ("Cancel", () => Notice = null)], Default: 0, Cancel: 1);
    }

    void AskStop()
    {
        if (!Films.Working) return;
        int made = Films.Maker.Results.Count(r => r.Made);
        var lines = new List<PageLine>();
        if (made > 0) lines.Add(new(made == 1 ? "The movie finished so far is kept and will play." : $"The {made} movies finished so far are kept and will play."));
        if (Films.Maker.Now is { } now && now.Step != FilmMaker.MeasuringStep)
            lines.Add(new($"{now.Job.Film.Title} ({FilmsPage.Word(now.Job.Version)}) is {(int)(now.Share * 100)}% done. That work will be discarded, and it will show as not finished.", true));
        lines.Add(new("You can start again any time. It continues with the movies not yet made, starting that one over."));
        Notice = new("filmstop", "Cancel upscaling?", lines,
            [("Cancel upscaling", () => { Notice = null; Films.Stop(); }), ("Keep going", () => Notice = null)], Default: 1, Cancel: 1);
    }

    /// <summary>Play while movies are being made: asked first (the work takes the graphics card); answers whether it asked.</summary>
    bool AskStopFilmsForPlay()
    {
        var lines = new List<PageLine> { new("Movies are being upscaled, which keeps the graphics card busy: the game would stutter, and the upscaling could fail.") };
        if (Films.Maker.Now is { } now && now.Step != FilmMaker.MeasuringStep)
            lines.Add(new($"Finished movies are kept. {now.Job.Film.Title} ({FilmsPage.Word(now.Job.Version)}) is {(int)(now.Share * 100)}% done: that work will be discarded. You can start again later on the Movies page.", true));
        Notice = new("filmplay", "Cancel upscaling and play?", lines,
            [("Cancel upscaling and play", () => { Notice = null; Films.Stop(); Films.Maker.Wait(TimeSpan.FromSeconds(15)); Play(); }), ("Keep upscaling", () => Notice = null)], Default: 1, Cancel: 1);
        return true;
    }

    void AskRemove(FilmEntry film, FilmVersion v)
    {
        if (!Films.IsMade(v, film)) return;
        var lines = new List<PageLine> { new($"This deletes the {FilmsPage.Word(v)} version of {film.Title} ({FilmsPage.Megabytes(Films.MadeBytes(v, film))}) from this PC. The original isn't affected.") };
        if (Films.Versions.Choice(film.File) == v) lines.Add(new($"{film.Title} is set to play this version, so it will play the original again.", Caution: true));
        lines.Add(new("To get it back, you would have to make it again."));
        Notice = new("filmremove", $"Remove the {FilmsPage.Word(v)} version of {film.Title}?", lines,
            [("Remove", () => { Notice = null; Films.Remove(film, v); }), ("Cancel", () => Notice = null)], Default: 1, Cancel: 1);
    }

    void AskRemoveAll()
    {
        var made = Films.MadeList();
        if (made.Count == 0) return;
        Notice = new("filmremoveall", "Remove all upscaled movies?",
            [
                new($"This deletes {(made.Count == 1 ? "the upscaled version" : $"all {made.Count} upscaled versions")} ({FilmsPage.Megabytes(made.Sum(m => m.Bytes))}) from this PC, and every movie goes back to playing the original. The originals aren't affected."),
                new("The tools are kept; you can remove them separately."),
                new("To get them back, you would have to make them again.", Caution: true),
            ],
            [("Remove all", () => { Notice = null; Films.RemoveAll(); }), ("Cancel", () => Notice = null)], Default: 1, Cancel: 1);
    }

    void AskRemoveTools()
    {
        if (Films.ToolsBytes == 0) return;
        Notice = new("filmremovetools", "Remove the upscaling tools?",
            [
                new($"This deletes Real-ESRGAN and RIFE ({FilmsPage.Megabytes(Films.ToolsBytes)}) from this PC, with everything they left behind. They were never installed in Windows and don't write anywhere else, so there is nothing to uninstall."),
                new("Your upscaled movies are kept and still play. Making more will need the tools downloaded again."),
            ],
            [("Remove", () => { Notice = null; Films.RemoveTools(); }), ("Cancel", () => Notice = null)], Default: 1, Cancel: 1);
    }
    void FillFilms()
    {
        var d = Data;
        var f = Films;
        if (Page != "films") return;                                               // the page's data is only filled while it shows
        f.Look();                                                                 // the films' lengths, once FFmpeg is found
        bool working = f.Working, fetching = f.Fetching, measuring = f.Maker.Now is { Step: FilmMaker.MeasuringStep };
        bool hasFFmpeg = f.HasFFmpeg;
        bool hasGame = FilmCatalog.All.Any(x => f.Source(x) is not null);
        string busyWhy = working ? (measuring ? "This PC is being tested." : "Movies are being upscaled: cancel that first.") : fetching ? "The tools are being downloaded." : "";

        d.Set("filmsIntro", "Vitomir can make sharper, smoother versions of the game's movies on this PC. Best makes each frame four times as large and Fast twice as large, and both add a frame between each pair for smoother motion. Choose below which version each movie plays. The original movies are never changed. The new versions belong to this PC alone: multiplayer never compares them, and they don't count as mods.");
        Lines("filmsWarn", FilmWarnings);

        // The tools.
        var (missing, bytes) = f.ToolsToFetch;
        long toolsSize = f.ToolsBytes;
        var (done, total) = f.Fetched;
        d.Set("filmToolsLine", fetching ? $"Downloading the tools… {FilmsPage.Megabytes(done)} of {FilmsPage.Megabytes(total)}"
            : missing.Count == 0 ? $"Real-ESRGAN and RIFE are downloaded ({FilmsPage.Megabytes(toolsSize)} in your user folder's Tools folder). They are portable programs that run from that folder: nothing is installed in Windows, so you can remove them safely at any time. They are needed only to upscale movies; finished movies play without them, so once yours are done you can remove them."
            : $"Upscaling uses two free programs, Real-ESRGAN and RIFE, which are not downloaded yet ({FilmsPage.Megabytes(bytes)}). They are portable programs that run from your user folder: nothing is installed in Windows, and you can remove them safely at any time. They are needed only while movies are being upscaled.");
        d.Set("filmFetching", fetching);
        d.Set("filmFetchProgress", total > 0 ? (float)(done / (double)total) : 0f);
        d.Set("filmCanFetch", missing.Count > 0 && !fetching);
        d.Set("filmFetchFree", !working);
        d.Set("filmFetchWhy", f.FetchProblem is { } fp ? $"The tools could not be downloaded: {fp}." : "");
        d.Set("filmHasTools", toolsSize > 0 && !fetching);
        d.Set("filmIdle", !working && !fetching);
        d.Set("filmBusyWhy", busyWhy);

        // The measurement.
        d.Set("filmMeasureLine", measuring ? "Timing a few frames in each version… (seconds on a fast graphics card, a minute or two on a slow one)"
            : f.Estimates(FilmVersion.Best) || f.Estimates(FilmVersion.Fast) ? $"All eleven movies would take about {(f.Estimates(FilmVersion.Best) ? FilmsPage.Time(All(f, FilmVersion.Best)) : "an unknown time")} in Best and {(f.Estimates(FilmVersion.Fast) ? FilmsPage.Time(All(f, FilmVersion.Fast)) : "an unknown time")} in Fast{(f.Versions.Pace(FilmVersion.Best) is not null || f.Versions.Pace(FilmVersion.Fast) is not null ? ", going by the movies already made here" : "")}. Each movie's estimate is shown beside it below. Anything else running on the PC makes it take longer."
            : f.Maker.Problem is { } mp ? $"The test failed: {mp}."
            : "Run a short test to estimate how long each movie takes on this PC.");
        d.Set("filmMeasuring", measuring);
        d.Set("filmMeasureLabel", f.Maker.Speed is null ? "Test this PC" : "Test again");
        d.Set("filmCanMeasure", f.ToolsReady && hasFFmpeg && hasGame && !working && !fetching);
        d.Set("filmMeasureWhy", !f.ToolsReady ? "Download the tools first." : !hasFFmpeg ? "FFmpeg (with ffprobe) is needed: see Setup." : !hasGame ? "Import the game first: see Setup." : busyWhy);
        d.Set("filmMeasureWhyShown", d.Flag("filmCanMeasure") ? "" : d.Text("filmMeasureWhy"));

        // The rows.
        string sig = string.Join("|", FilmCatalog.All.Select(x => x.Stem + f.Versions.Choice(x.File) + Cells(f, x) + f.Length(x)?.Seconds)) + working + fetching;
        Rows("films", sig, add =>
        {
            foreach (var film in FilmCatalog.All)
            {
                var item = add();
                item.Set("stem", film.Stem).Set("title", film.Title)
                    .Set("length", f.Length(film) is { Seconds: > 0 } l ? $"{(int)l.Seconds / 60}:{(int)l.Seconds % 60:00}" : "")
                    .Set("choice", FilmVersions.Word(f.Versions.Choice(film.File)))
                    .Set("originalText", f.Cell(film, FilmVersion.Original).Words)
                    .Set("removeFree", !working && !fetching).Set("removeWhy", busyWhy);
                foreach (var (v, key) in new[] { (FilmVersion.Best, "best"), (FilmVersion.Fast, "fast") })
                {
                    var (state, words) = f.Cell(film, v);
                    item.Set(key + "Text", words)
                        .Set(key + "Plain", state is FilmsPage.CellState.Made or FilmsPage.CellState.Working && !words.Contains("too heavy", StringComparison.Ordinal))
                        .Set(key + "Caution", state is FilmsPage.CellState.NotFinished || words.Contains("too heavy", StringComparison.Ordinal))
                        .Set(key + "Warn", state is FilmsPage.CellState.Failed)
                        .Set(key + "Note", state is FilmsPage.CellState.NotMade)
                        .Set(key + "Made", f.IsMade(v, film));
                }
            }
        });
        d.Set("filmNoGame", hasGame ? "" : "The game isn't imported yet, so there are no movies to upscale: see Setup.");

        // The work.
        var jobs = f.ToMake();
        d.Set("filmToMake", working && !measuring ? "" : jobs.Count == 0
            ? "Every movie's version is ready. Set a movie to Best or Fast above to upscale it."
            : $"{jobs.Count} {(jobs.Count == 1 ? "movie is" : "movies are")} waiting to be upscaled" + (f.EstimateAll(jobs) is { } all ? $" (about {FilmsPage.Time(all)} on this PC)." : "."));
        d.Set("filmWorking", working && !measuring);
        var now = f.Maker.Now;
        d.Set("filmProgress", (float)(now?.Share ?? 0));
        d.Set("filmProgressText", now is { } n && !measuring
            ? $"{n.Job.Film.Title} ({FilmsPage.Word(n.Job.Version)}): {n.Step.ToLowerInvariant()}, {(int)(n.Share * 100)}%" + (n.Left is { } left ? $", about {FilmsPage.Time(left)} left on it." : ".")
            : "");
        d.Set("filmCanStart", jobs.Count > 0 && f.ToolsReady && hasFFmpeg && !fetching);
        d.Set("filmStartWhy", jobs.Count == 0 ? "Set a movie to Best or Fast first." : !f.ToolsReady ? "Download the tools first." : !hasFFmpeg ? "FFmpeg (with ffprobe) is needed: see Setup." : busyWhy);
        d.Set("filmStartWhyShown", d.Flag("filmCanStart") ? "" : d.Text("filmStartWhy"));
        Lines("filmResult", f.Result);

        // The room.
        var made = f.MadeList();
        d.Set("filmHasMade", made.Count > 0);
        d.Set("filmSpaceLine", $"Upscaled movies: {(made.Count == 0 ? "none" : $"{made.Count}, {FilmsPage.Megabytes(made.Sum(m => m.Bytes))}")}. Tools: {(toolsSize == 0 ? "none" : FilmsPage.Megabytes(toolsSize))}. Both are in your user folder ({UserData}).");
    }

    static string Cells(FilmsPage f, FilmEntry film) => f.Cell(film, FilmVersion.Best).Words + f.Cell(film, FilmVersion.Fast).Words;

    static TimeSpan All(FilmsPage f, FilmVersion v) =>
        FilmCatalog.All.Aggregate(TimeSpan.Zero, (sum, film) => sum + (f.Estimate(film, v) ?? TimeSpan.Zero));
}
