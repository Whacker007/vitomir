// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Writing the player's own vitomir.ini, which until the Options screen nothing did: the file was the
// player's to edit by hand and the engine only read it. The Options screen, the Fixes page and the
// key bindings now write it too, and they write it the way a person would — one line changed in
// place, every comment and every other line left exactly as it was.
namespace Vitomir.Engine.Diagnostics;

/// <summary>
/// Changes keys in an INI file in place (ours). The file is the one <see cref="IniFile"/> reads, so
/// the same rules hold: sections and keys match case-insensitively, a comment starts with <c>;</c>
/// or <c>#</c>. A key already in its section keeps its line, its position and its comments and only
/// its value changes; a key that is not there is added at the end of its section; a section that is
/// not there is added at the end of the file. A key repeated in its section has every occurrence
/// changed, because the reader takes the last one and a stale earlier one would only mislead
/// whoever opens the file.
/// </summary>
public static class SettingsFile
{
    /// <summary>
    /// Set while a recording is replayed: the player's choices it replays (the Options screen, the Fixes
    /// page, the keys) are put in force for the replay but never written to this machine's own file.
    /// Until then a replay wrote the recording player's gamma, volumes and Fixes into the
    /// replaying machine's settings.
    /// </summary>
    public static bool Replaying { get; set; }

    /// <summary>Sets <paramref name="values"/> under <paramref name="section"/> in <paramref name="path"/>, creating the file and its folder when needed.</summary>
    public static void Set(string path, string section, IEnumerable<KeyValuePair<string, string>> values)
    {
        if (Replaying) return;
        string text = "";
        try { if (File.Exists(path)) text = File.ReadAllText(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        var updated = Apply(text, section, values);
        if (updated == text) return;
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        File.WriteAllText(path, updated);
    }

    /// <summary>One key.</summary>
    public static void Set(string path, string section, string key, string value) =>
        Set(path, section, [new KeyValuePair<string, string>(key, value)]);

    /// <summary>The same change made to a file's text, which is what the tests check (<see cref="Vitomir.Formats.IniText.Apply"/>, shared with the launcher).</summary>
    public static string Apply(string text, string section, IEnumerable<KeyValuePair<string, string>> values) =>
        Vitomir.Formats.IniText.Apply(text, section, values);
}
