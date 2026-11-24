// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The window's settings as the platform keeps them, one file per game (every game and module on the
// platform gets the same window modes, so the window's settings are the platform's, kept per game).
using System.Text;
using System.Text.Json;

namespace Vitomir.Platform.Windowing;

/// <summary>
/// **The window's keys for one game, the platform's**: <c>&lt;the platform's user data&gt;\Games\&lt;game id&gt;\window.json</c>,
/// a JSON object of the same keys <c>vitomir.ini</c>'s <c>[window]</c> has (<c>fullscreen</c>, <c>fullscreenmode</c>,
/// <c>worldsize</c>, <c>borderless</c>, <c>x</c>, <c>y</c>, <c>width</c>, <c>height</c>, <c>display</c>, <c>maximised</c>), each
/// value a string as the window's rules write it. <see cref="Read"/> reads the file afresh each time (a hand edit between starts
/// is seen; missing or unreadable, no keys: a first start); <see cref="Write"/> sets the keys given, matched without regard to
/// case, every other key kept in its place, and writes the file whole only when something changed (through a file beside it, so
/// a run that stops half way never leaves half a file). Under <c>vitomir-platform</c> the Classic game's window keeps its
/// place here; <c>vitomir-tools</c>' stays its <c>vitomir.ini</c>'s <c>[window]</c>.
/// </summary>
public sealed class GameWindowSettings : IWindowSettings
{
    /// <summary>The file's name in its game's folder.</summary>
    public const string FileName = "window.json";

    /// <summary>The folder of the games' own things in the platform's user data.</summary>
    public const string GamesFolder = "Games";

    readonly Action<string> say;
    bool saidUnreadable;

    /// <param name="path">The file (<see cref="PathFor"/>).</param>
    /// <param name="say">Where a file that cannot be read is said, once.</param>
    public GameWindowSettings(string path, Action<string>? say = null)
    {
        Path = System.IO.Path.GetFullPath(path);
        this.say = say ?? (_ => { });
    }

    /// <summary>The game's window file in the platform's user data folder <paramref name="userData"/>.</summary>
    public static string PathFor(string userData, string gameId) => System.IO.Path.Combine(userData, GamesFolder, gameId, FileName);

    /// <summary>The file.</summary>
    public string Path { get; }

    /// <summary>Whether the file is there: false at the game's first start under the platform.</summary>
    public bool Kept => File.Exists(Path);

    public WindowKeys Read() => new(Pairs());

    public void Write(IEnumerable<KeyValuePair<string, string>> values)
    {
        var kept = Pairs();
        bool changed = !Kept;
        foreach (var kv in values)
        {
            int at = kept.FindIndex(p => p.Key.Equals(kv.Key, StringComparison.OrdinalIgnoreCase));
            if (at < 0) { kept.Add(kv); changed = true; }
            else if (kept[at].Value != kv.Value) { kept[at] = new(kept[at].Key, kv.Value); changed = true; }
        }
        if (!changed) return;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        string tmp = Path + ".tmp";
        File.WriteAllText(tmp, Text(kept));
        File.Move(tmp, Path, overwrite: true);
    }

    /// <summary>The keys as the file has them, in its order (a later one of a name over an earlier one); none when it is not there or does not read.</summary>
    List<KeyValuePair<string, string>> Pairs()
    {
        var pairs = new List<KeyValuePair<string, string>>();
        string text;
        try
        {
            if (!File.Exists(Path)) return pairs;
            text = File.ReadAllText(Path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Unreadable(e.Message); return pairs; }
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) { Unreadable("not a JSON object"); return pairs; }
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                string value = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()!.Trim() : p.Value.GetRawText();
                int at = pairs.FindIndex(k => k.Key.Equals(p.Name, StringComparison.OrdinalIgnoreCase));
                if (at >= 0) pairs[at] = new(pairs[at].Key, value);
                else pairs.Add(new(p.Name, value));
            }
        }
        catch (JsonException e) { Unreadable(e.Message); pairs.Clear(); }
        return pairs;
    }

    void Unreadable(string why)
    {
        if (saidUnreadable) return;
        saidUnreadable = true;
        say($"window: {Path} could not be read ({why}); the window starts as on a first start");
    }

    /// <summary>The keys as the file's text: one indented JSON object of strings.</summary>
    static string Text(IEnumerable<KeyValuePair<string, string>> pairs)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            foreach (var (key, value) in pairs) json.WriteString(key, value);
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray()) + Environment.NewLine;
    }
}

/// <summary>
/// **A window's settings read but never written**: what a replay's window is given, so that whichever settings are behind
/// it — a <c>vitomir.ini</c>'s or the platform's — a replay never writes the machine's own (as <c>vitomir.ini</c>'s writer has
/// always refused to while a tape is replayed).
/// </summary>
public sealed class ReadOnlyWindowSettings(IWindowSettings read) : IWindowSettings
{
    public WindowKeys Read() => read.Read();

    public void Write(IEnumerable<KeyValuePair<string, string>> values) { }
}
