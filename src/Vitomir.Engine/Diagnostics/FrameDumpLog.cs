// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// `--frame-dump-log <file.jsonl>` (ours): what was pressed and where the camera was, at each frame a dump writes.
using System.Text;
using System.Text.Json;

namespace Vitomir.Engine.Diagnostics;

/// <summary>
/// **One dumped frame's inputs and camera** (ours, a harness; 1.1 has nothing like it). A frame dump
/// (<c>--frame-dump</c>) writes the pictures of a run, a replay of a tape above all; this is the line written beside each,
/// from which the web site's clips draw their overlay (<c>tools/site/media.py</c>): the keys lit while held, the mouse's
/// buttons and wheel, the camera's gauge, the ring at the pointer's tip and the swivel lock's padlock, each on the very
/// frame it happened.
/// </summary>
/// <param name="N">The picture's number, <c>frame{N:00000}.png</c>.</param>
/// <param name="Frame">The run's frame it was taken at (the loop's count).</param>
/// <param name="SimMs">The game's clock then.</param>
/// <param name="Width">The picture's width, in pixels.</param>
/// <param name="Height">The picture's height.</param>
/// <param name="Keys">The keyboard's keys down, by scan code (SDL's numbering, as the tape holds them).</param>
/// <param name="Held">The commands whose keys or buttons are down (<c>TurnLeft</c>, <c>Forward</c>, <c>DragSwivel</c>, <c>Act</c>, …), whatever the player has bound them to.</param>
/// <param name="Pressed">The commands pressed since the last picture (<c>SwivelLock</c>, <c>Act</c>, …), however briefly: with
/// <paramref name="Held"/>, what lets a clip show <b>the default keys</b> of what was done (the bindings' file beside the log,
/// <see cref="FrameDumpLog.Defaults"/>) while the player keeps his own.</param>
/// <param name="Buttons">The mouse buttons down, a bit each (1 left, 2 middle, 4 right, 8 and 16 the side buttons).</param>
/// <param name="Clicks">The mouse buttons pressed since the last picture, a bit each as <paramref name="Buttons"/>: a click quicker than a picture still shows.</param>
/// <param name="Wheel">The wheel's notches since the last picture: up positive.</param>
/// <param name="PointerX">The pointer in the window, in pixels: where the ankh's tip is drawn (the centre's point while the swivel lock holds it).</param>
/// <param name="PointerY">The same, down.</param>
/// <param name="WindowWidth">The window's width the pointer is measured in.</param>
/// <param name="WindowHeight">The window's height.</param>
/// <param name="Cursor">Whether the pointer is drawn.</param>
/// <param name="Mode">The camera's mode: <c>Game</c>, <c>Look</c>, <c>Cutscene</c>, <c>Free</c> or <c>Path</c>.</param>
/// <param name="Yaw">The camera's heading, degrees.</param>
/// <param name="Pitch">The camera's pitch, degrees, signed (below level negative).</param>
/// <param name="PitchLow">The pitch's lower limit, signed degrees (−80 in the base game).</param>
/// <param name="PitchTop">Its upper limit, signed (−20 in the base game, higher with the camera's choice).</param>
/// <param name="Zoom">The zoom's scale (0.1 nearest, 1 the base game's farthest).</param>
/// <param name="ZoomOut">How far out the zoom may go (1 in the base game, up to 3 with the camera's choice).</param>
/// <param name="Lock">Whether the swivel lock is on.</param>
/// <param name="Swivel">Whether the swivel has the camera (held, dragged or locked).</param>
/// <param name="Driving">Whether the keys steer the character (<c>[qol] driving</c>, the game camera up, no menu).</param>
/// <param name="Menu">Whether a menu has the screen.</param>
public readonly record struct FrameDumpEntry(
    int N, int Frame, long SimMs, int Width, int Height,
    int[] Keys, string[] Held, string[] Pressed, uint Buttons, uint Clicks, int Wheel,
    float PointerX, float PointerY, int WindowWidth, int WindowHeight, bool Cursor,
    string Mode, float Yaw, float Pitch, float PitchLow, float PitchTop, float Zoom, float ZoomOut,
    bool Lock, bool Swivel, bool Driving, bool Menu)
{
    /// <summary>The entry as one line of JSON, its fields in camel case, numbers to three places.</summary>
    public string Line()
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteNumber("n", N);
            w.WriteNumber("frame", Frame);
            w.WriteNumber("simMs", SimMs);
            w.WriteNumber("width", Width);
            w.WriteNumber("height", Height);
            w.WriteStartArray("keys");
            foreach (var k in Keys) w.WriteNumberValue(k);
            w.WriteEndArray();
            w.WriteStartArray("held");
            foreach (var h in Held) w.WriteStringValue(h);
            w.WriteEndArray();
            w.WriteStartArray("pressed");
            foreach (var p in Pressed) w.WriteStringValue(p);
            w.WriteEndArray();
            w.WriteNumber("buttons", Buttons);
            w.WriteNumber("clicks", Clicks);
            w.WriteNumber("wheel", Wheel);
            w.WriteNumber("pointerX", Round(PointerX));
            w.WriteNumber("pointerY", Round(PointerY));
            w.WriteNumber("windowWidth", WindowWidth);
            w.WriteNumber("windowHeight", WindowHeight);
            w.WriteBoolean("cursor", Cursor);
            w.WriteString("mode", Mode);
            w.WriteNumber("yaw", Round(Yaw));
            w.WriteNumber("pitch", Round(Pitch));
            w.WriteNumber("pitchLow", Round(PitchLow));
            w.WriteNumber("pitchTop", Round(PitchTop));
            w.WriteNumber("zoom", Round(Zoom));
            w.WriteNumber("zoomOut", Round(ZoomOut));
            w.WriteBoolean("lock", Lock);
            w.WriteBoolean("swivel", Swivel);
            w.WriteBoolean("driving", Driving);
            w.WriteBoolean("menu", Menu);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    static double Round(float v) => Math.Round((double)v, 3);

    /// <summary>
    /// An angle as the camera stores it (0..360, the pitch's limits wrapped: 280 is −80) made signed, −180 to 180.
    /// </summary>
    public static float Signed(float degrees)
    {
        float d = degrees % 360f;
        if (d > 180f) d -= 360f;
        else if (d <= -180f) d += 360f;
        return d;
    }
}

/// <summary>
/// The file of <see cref="FrameDumpEntry"/> lines, one per picture, flushed as the run ends. It measures and changes
/// nothing: no rule reads it, and a replay checks the same with it or without.
/// </summary>
public sealed class FrameDumpLog : IDisposable
{
    readonly StreamWriter file;

    FrameDumpLog(string path)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
        file = new StreamWriter(path, append: false, new UTF8Encoding(false)) { NewLine = "\n" };
    }

    /// <summary>
    /// A log when the command line names one, with <b>the commands' default keys</b> written beside it once
    /// (<see cref="DefaultsPath"/>, <see cref="Defaults"/>).
    /// </summary>
    /// <remarks>
    /// A load ends the game's run and begins another in the same process, which opens the same log again: it gets the log
    /// already open, so the lines go on after those of the run before instead of the file starting over (the pictures'
    /// dump carries on the same way). Each run ends with <see cref="Flush"/>; the file is closed as the process ends.
    /// </remarks>
    public static FrameDumpLog? Open(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        string full = Path.GetFullPath(path);
        lock (open)
        {
            if (open.TryGetValue(full, out var log)) return log;
            log = new FrameDumpLog(path);
            File.WriteAllText(DefaultsPath(path), Defaults(), new UTF8Encoding(false));
            open[full] = log;
            if (open.Count == 1) AppDomain.CurrentDomain.ProcessExit += (_, _) => { lock (open) foreach (var l in open.Values) l.file.Dispose(); };
            return log;
        }
    }

    // The process's logs by path, so a run after a load carries on its predecessor's.
    static readonly Dictionary<string, FrameDumpLog> open = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where the defaults go: beside the log, <c>log.jsonl</c>'s as <c>log.keys.json</c>.</summary>
    public static string DefaultsPath(string log) => Path.ChangeExtension(log, ".keys.json");

    /// <summary>
    /// Every command's kind and **default keys** (1.1's, and ours for our commands), as the key page's Defaults puts them:
    /// for each chord the key's scan code or the mouse button (SDL's numbers: 1 left, 2 middle, 3 right, 4 and 5 the side
    /// buttons), the modifiers and the name a person reads. A clip lights these for what the log says was held or pressed,
    /// whatever the player who recorded it had bound.
    /// </summary>
    public static string Defaults()
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteStartArray("commands");
            foreach (var info in Vitomir.Engine.Ui.KeyBindings.All)
            {
                w.WriteStartObject();
                w.WriteString("command", info.Command.ToString());
                w.WriteString("kind", info.Kind.ToString());
                w.WriteStartArray("defaults");
                foreach (var chord in info.Defaults)
                {
                    w.WriteStartObject();
                    if (chord.IsMouse) w.WriteNumber("button", chord.Button);
                    else w.WriteNumber("key", chord.Input);
                    w.WriteBoolean("ctrl", (chord.Mods & Vitomir.Engine.Ui.KeyMods.Ctrl) != 0);
                    w.WriteBoolean("shift", (chord.Mods & Vitomir.Engine.Ui.KeyMods.Shift) != 0);
                    w.WriteBoolean("alt", (chord.Mods & Vitomir.Engine.Ui.KeyMods.Alt) != 0);
                    w.WriteString("name", chord.ToString());
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    /// <summary>The picture just written, as a line.</summary>
    public void Write(in FrameDumpEntry entry) => file.WriteLine(entry.Line());

    /// <summary>The lines so far written through to the file, as a run ends (the next run's go on after them).</summary>
    public void Flush() => file.Flush();

    /// <summary>The file closed, and the log forgotten, so the path opens afresh.</summary>
    public void Dispose()
    {
        lock (open)
        {
            foreach (var (key, log) in open)
                if (ReferenceEquals(log, this)) { open.Remove(key); break; }
        }
        file.Dispose();
    }
}
