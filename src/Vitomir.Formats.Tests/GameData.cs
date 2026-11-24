// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Formats.Tests;

/// <summary>
/// Locates the player's install for the corpus tests: VTMR_GAME_DIR, else C:\Games\VTMR.
/// Nothing from the game is stored in the repo; without an install these tests skip.
/// </summary>
public static class GameData
{
    public static readonly string? Dir = Find();

    static string? Find()
    {
        foreach (var d in new[] { Environment.GetEnvironmentVariable("VTMR_GAME_DIR"), @"C:\Games\VTMR" })
            if (!string.IsNullOrEmpty(d) && File.Exists(Path.Combine(d, "Resource.nob"))) return d;
        return null;
    }

    public static void RequireInstall() =>
        Assert.SkipWhen(Dir is null, "game install not found (set VTMR_GAME_DIR to the folder holding Resource.nob)");

    public static NobArchive Open(string nob)
    {
        RequireInstall();
        var path = Path.Combine(Dir!, nob);
        Assert.SkipUnless(File.Exists(path), $"{nob} not found in {Dir}");
        return new NobArchive(path);
    }

    /// <summary>Parses every entry with <paramref name="extension"/> and returns the failures (name → message).</summary>
    public static (int Parsed, Dictionary<string, string> Failures) ParseAll<T>(NobArchive nob, string extension,
        Func<byte[], string, T> read, Action<string, T>? visit = null)
    {
        int parsed = 0;
        var failures = new Dictionary<string, string>();
        foreach (var name in nob.NamesWithExtension(extension))
        {
            try
            {
                var obj = read(nob.Read(name), name);
                parsed++;
                visit?.Invoke(name, obj);
            }
            catch (NodFormatException e) { failures[name] = e.Message; }
        }
        return (parsed, failures);
    }
}
