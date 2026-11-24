// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The improved films a player has made of 1.1's films on their own machine, and which of them plays.
using Vitomir.Formats;

namespace Vitomir.Platform.Films;

/// <summary>Which version of a film plays: 1.1's own, or one the player made (ours).</summary>
public enum FilmVersion { Original, Best, Fast }

/// <summary>One of 1.1's films as the launcher lists it: its file in <c>Video\</c> and a plain title.</summary>
public sealed record FilmEntry(string File, string Title)
{
    /// <summary>The name a version is kept under: the file's own name without its extension, in lower case (1.1's folder mixes <c>NYC.BIK</c> with <c>Sunset.bik</c>).</summary>
    public string Stem => FilmVersions.StemOf(File);
}

/// <summary>1.1's eleven films, in the order the launcher lists them (the order a player meets them).</summary>
public static class FilmCatalog
{
    public static readonly IReadOnlyList<FilmEntry> All =
    [
        new("logo.bik", "Opening logo"),
        new("trailer.bik", "Trailer"),
        new("intro.bik", "New game opening"),
        new("embrace.bik", "The Embrace"),
        new("awakening.bik", "Awakening"),
        new("Sunrise.bik", "Sunrise"),
        new("Sunset.bik", "Sunset"),
        new("NYC.BIK", "New York"),
        new("finale1.bik", "Ending one"),
        new("finale2.bik", "Ending two"),
        new("finale3.bik", "Ending three"),
    ];

    /// <summary>The entry of a film's name (any case, with or without its extension), or null.</summary>
    public static FilmEntry? Of(string name) => All.FirstOrDefault(f => f.Stem == FilmVersions.StemOf(name));
}

/// <summary>
/// **The improved films of one user folder** (ours): kept in <c>Movies\best\</c> and <c>Movies\fast\</c> as MP4s under the film's own name,
/// the player's choice of version for each film in <c>Movies\films.ini</c> (<c>[choice]</c>), and the films made but too heavy for this
/// machine to play smoothly marked there too (<c>[heavy]</c>). A version is ready when its file is there: a film is made in
/// <c>Movies\work\</c> and only moved in once it has been checked, so a half-made one never counts. **Nothing here is 1.1's data**:
/// these files are each machine's own, outside the data a network game compares, and never count as modding. 1.1's films are only
/// ever read.
/// </summary>
public sealed class FilmVersions
{
    /// <param name="userFolder">The user folder; the films are kept in its <c>Movies</c> folder (the launcher calls them movies).</param>
    public FilmVersions(string userFolder) => Folder = Path.Combine(userFolder, "Movies");

    /// <summary>
    /// The user folder's improved films for the game being played, set by the game's start; null (as in tests and tools) plays 1.1's
    /// films only.
    /// </summary>
    public static FilmVersions? Active { get; set; }

    /// <summary>Where they are kept.</summary>
    public string Folder { get; }

    /// <summary>Where a film is made, emptied before and after every film.</summary>
    public string WorkFolder => Path.Combine(Folder, "work");

    string ChoicesPath => Path.Combine(Folder, "films.ini");

    /// <summary>The word a version is written as (and its folder's name).</summary>
    public static string Word(FilmVersion v) => v switch { FilmVersion.Best => "best", FilmVersion.Fast => "fast", _ => "original" };

    /// <summary>The version a word names, or null.</summary>
    public static FilmVersion? Parse(string? word) => word?.Trim().ToLowerInvariant() switch
    {
        "best" => FilmVersion.Best,
        "fast" => FilmVersion.Fast,
        "original" => FilmVersion.Original,
        _ => null,
    };

    /// <summary>A film's name as its versions are kept: the last part of the name, without its extension, in lower case.</summary>
    public static string StemOf(string name) => Path.GetFileNameWithoutExtension(Path.GetFileName(name.Replace('\\', '/'))).ToLowerInvariant();

    /// <summary>Where a made version of a film is (or would be) kept.</summary>
    public string PathOf(FilmVersion v, string film) => Path.Combine(Folder, Word(v), StemOf(film) + ".mp4");

    /// <summary>Whether a version can play now (1.1's own always can, here: whether it is in the install is the game's to find).</summary>
    public bool Ready(FilmVersion v, string film) => v == FilmVersion.Original || File.Exists(PathOf(v, film));

    /// <summary>A made version's size in bytes, or 0.</summary>
    public long Size(FilmVersion v, string film) => v != FilmVersion.Original && File.Exists(PathOf(v, film)) ? new FileInfo(PathOf(v, film)).Length : 0;

    /// <summary>The version the player chose for a film (1.1's own when they chose none).</summary>
    public FilmVersion Choice(string film) => Parse(Read("choice", StemOf(film))) ?? FilmVersion.Original;

    /// <summary>The player's choice of version for a film, kept.</summary>
    public void Choose(string film, FilmVersion v) => Write("choice", StemOf(film), Word(v));

    /// <summary>
    /// **The file the game plays for a film**: the chosen version's when it is made, else null (the game plays 1.1's own). A chosen
    /// version not yet made, or removed, never takes a film away.
    /// </summary>
    public string? Playing(string film)
    {
        var v = Choice(film);
        return v != FilmVersion.Original && Ready(v, film) ? PathOf(v, film) : null;
    }

    /// <summary>Whether a made version was found too heavy for this machine to play smoothly when it was made.</summary>
    public bool Heavy(FilmVersion v, string film) => IniText.On(Read("heavy", Word(v) + "." + StemOf(film)));

    /// <summary>A made version marked as too heavy for this machine, or not.</summary>
    public void MarkHeavy(FilmVersion v, string film, bool heavy) => Write("heavy", Word(v) + "." + StemOf(film), heavy ? "yes" : "no");

    /// <summary>
    /// This machine's pace for a version as its last made film ran (seconds a source frame for each pixel row of its height: a film's
    /// time is its frames times its height times this), or null before one is made. Real runs beat a short measurement: on the development PC
    /// the measurement said 38 minutes for Best and the run took 57 (the writing of every frame to the disk, which a few frames do not
    /// show).
    /// </summary>
    public double? Pace(FilmVersion v) =>
        double.TryParse(Read("pace", Word(v)), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double p) && p > 0 ? p : null;

    /// <summary>A version's pace on this machine, kept (<see cref="Pace"/>).</summary>
    public void SetPace(FilmVersion v, double secondsAFrameRow) =>
        Write("pace", Word(v), secondsAFrameRow.ToString("R", System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>The made versions there are, film by film in the catalogue's order.</summary>
    public IReadOnlyList<(FilmEntry Film, FilmVersion Version, long Bytes)> Made() =>
        [.. from f in FilmCatalog.All from v in new[] { FilmVersion.Best, FilmVersion.Fast } where Ready(v, f.File) select (f, v, Size(v, f.File))];

    /// <summary>
    /// **One made version removed**: its file deleted. A film that was set to play it goes back to 1.1's own. Answers why not, or null.
    /// </summary>
    public string? Remove(FilmVersion v, string film)
    {
        if (v == FilmVersion.Original) return "1.1's own films are never removed";
        try
        {
            if (File.Exists(PathOf(v, film))) File.Delete(PathOf(v, film));
            Write("heavy", Word(v) + "." + StemOf(film), "no");
            if (Choice(film) == v) Choose(film, FilmVersion.Original);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return e.Message; }
    }

    /// <summary>
    /// **Every made version removed**, the work folder and the choices with them (every film plays 1.1's own again). Answers why
    /// not, or null.
    /// </summary>
    public string? RemoveAll()
    {
        try
        {
            if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return e.Message; }
        finally { Reload(); }
    }

    /// <summary>The work folder emptied (what a stopped or failed film left behind).</summary>
    public void CleanWork()
    {
        try { if (Directory.Exists(WorkFolder)) Directory.Delete(WorkFolder, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // The choices' file as last read or written: read once, not once a question (the launcher asks every frame).
    string? text;
    readonly object gate = new();

    /// <summary>The choices read from their file again at the next question (after another program may have changed it).</summary>
    public void Reload()
    {
        lock (gate) text = null;
    }

    string Text()
    {
        lock (gate)
        {
            if (text is not null) return text;
            try { text = File.Exists(ChoicesPath) ? File.ReadAllText(ChoicesPath) : ""; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { text = ""; }
            return text;
        }
    }

    string? Read(string section, string key) => IniText.Value(Text(), section, key);

    void Write(string section, string key, string value)
    {
        lock (gate)
        {
            string old = Text();
            string from = old.Length > 0 ? old : "; The improved films' choices (Vitomir's launcher writes this).\n";
            string updated = IniText.Apply(from, section, [new KeyValuePair<string, string>(key, value)]);
            if (updated == old) return;
            Directory.CreateDirectory(Folder);
            File.WriteAllText(ChoicesPath, updated);
            text = updated;
        }
    }
}
