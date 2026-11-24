// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Vfs;
using Vitomir.Engine.Video;
using Vitomir.Platform.Films;

namespace Vitomir.Engine.Tests;

/// <summary>The films' choice is the game's own state for the run (<see cref="FilmVersions.Active"/>): these tests run alone.</summary>
[CollectionDefinition(nameof(ImprovedFilmTests), DisableParallelization = true)]
public sealed class ImprovedFilmCollection;

/// <summary>
/// **An improved film plays in place of 1.1's** (ours): only once chosen and made, never over a player module's film, and never
/// changing the data a network game compares (it lives in the user folder, not the game's files).
/// </summary>
[Collection(nameof(ImprovedFilmTests))]
public sealed class ImprovedFilmTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("vitomir-improved-films-").FullName;

    public void Dispose()
    {
        FilmVersions.Active = null;
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void The_chosen_version_plays_once_made_and_the_original_otherwise()
    {
        string game = Path.Combine(root, "game", "Video");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "intro.bik"), "BIKi the original");
        using (var nob = System.IO.Compression.ZipFile.Open(Path.Combine(root, "game", "Resource.nob"), System.IO.Compression.ZipArchiveMode.Create))
            using (var w = new StreamWriter(nob.CreateEntry("misc/x.txt").Open())) w.Write("an archive, as an install has");
        var versions = new FilmVersions(Path.Combine(root, "user"));
        FilmVersions.Active = versions;
        using var files = new GameFiles(Path.Combine(root, "game"));
        string original = Path.Combine(game, "intro.bik");

        Assert.Equal(original, Films.Find(files, Films.NewGame));
        versions.Choose("intro.bik", FilmVersion.Best);
        Assert.Equal(original, Films.Find(files, Films.NewGame));                 // chosen, not made yet

        string best = versions.PathOf(FilmVersion.Best, "intro.bik");
        Directory.CreateDirectory(Path.GetDirectoryName(best)!);
        File.WriteAllText(best, "an MP4");
        Assert.Equal(best, Films.Find(files, "INTRO"));
        Assert.True(File.Exists(original));                                       // 1.1's film is only ever read

        FilmVersions.Active = null;                                               // a run without the user folder's films: tools, tests
        Assert.Equal(original, Films.Find(files, Films.NewGame));
    }
}
