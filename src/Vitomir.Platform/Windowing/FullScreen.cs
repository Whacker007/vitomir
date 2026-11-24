// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Full screen, as 1.1 starts ("I can't force the game into fullscreen mode. The old
// game always assumed fullscreen."). 1.1 runs full screen at the Display screen's video mode unless its
// command line says `-window` or `/window` (bit 0x2000 of the raster
// options word, which `-full` or `/full` clears again; the word is handed whole
// to rlDirect.dll's start). Everything else here is ours: the toggle, the state
// kept in `vitomir.ini`, the desktop mode, the first start's default and the fallbacks. The part that asks
// SDL is `Vitomir.Platform.Render/Windowing/FullScreenHost.cs` (both programs' window, moved from the old program with these
// rules from `Vitomir.Engine/Ui`); everything here is rules, so they can be tested without a display.
using System.Globalization;

namespace Vitomir.Platform.Windowing;

/// <summary>A full-screen video mode's size, in the display's own pixels.</summary>
public readonly record struct ScreenMode(int Width, int Height)
{
    public override string ToString() => $"{Width}x{Height}";
}

/// <summary>
/// What <c>vitomir.ini</c>'s <c>[window]</c> keeps of full screen (ours): whether the game is full screen,
/// and the mode it is full screen at — <see cref="Mode"/> null for the desktop's own size (borderless,
/// no change of mode).
/// </summary>
public sealed record FullScreenSetting(bool On, ScreenMode? Mode);

/// <summary>
/// How the window is made at start: put where it was left (<see cref="Restore"/>, the old
/// <see cref="WindowPlacement"/>), its place and state written as they change (<see cref="Keep"/>), and
/// made full screen (<see cref="Full"/>) at <see cref="Mode"/> (null: the desktop's size).
/// <see cref="Why"/> names what decided it, for the console.
/// </summary>
public readonly record struct WindowStart(bool Restore, bool Keep, bool Full, ScreenMode? Mode, string Why)
{
    /// <summary>Ours: made with no title bar at this size, centred on its display (<see cref="Ui.Borderless"/>); null for the window as it always was.</summary>
    public ScreenMode? Borderless { get; init; }

    /// <summary>
    /// A window with its title bar whatever <c>[window] borderless</c> keeps (ours): <c>--window</c>, the Display
    /// screen's "Window". The place kept is used only where a window with a title bar fits
    /// (<see cref="WindowPlacement.Resolve"/>). Earlier, such a run read the key and was made borderless.
    /// </summary>
    public bool Titled { get; init; }
}

/// <summary>How far a request for full screen got: the mode asked for, the desktop instead, or not at all.</summary>
public enum FullScreenLanded
{
    /// <summary>An exclusive change of mode, as 1.1's (<c>SDL_SetWindowFullscreenMode</c> with a mode).</summary>
    Exclusive,
    /// <summary>Borderless at the desktop's own size (a null mode): asked for, or the display had no mode close enough.</summary>
    Desktop,
    /// <summary>SDL refused both: the window stays a window.</summary>
    Window,
}

/// <summary>
/// **Full screen (1.1's, with ours around it).** 1.1 is full screen at its video mode unless started with
/// <c>-window</c> (<see cref="WindowBit"/>); Vitomir is too, unless the run says otherwise:
/// <list type="bullet">
/// <item><c>[window] fullscreen = on|off</c> (ours) keeps whether it is, on when absent, and
/// <c>fullscreenmode = WxH</c> (ours) the mode — taken as the closest the display offers — or
/// <c>desktop</c> for borderless full screen at the desktop's size. **A first start** — a settings file
/// with neither key — is borderless full screen at the desktop's own size, no change of video mode
/// (<see cref="Default"/>). A file from before that keeps <c>fullscreen</c> and
/// names no mode means what it meant then, 1600x900 (<see cref="DefaultMode"/>);</item>
/// <item>Alt+Enter (the <c>fullscreen</c> key command, ours) toggles it anywhere and writes the new state at
/// once; the Display screen's video mode, while full screen, lists the display's modes and keeps the one
/// chosen as <c>fullscreenmode</c>;</item>
/// <item>a run given <c>--window</c> (ours, after 1.1's <c>-window</c>) is a window with its title bar (the Display
/// screen's "Window", whatever <c>[window] borderless</c> keeps), put where it was left when a window with a
/// title bar fits there, and writes nothing; <c>--fullscreen</c> (ours) forces full screen for the run; a run given
/// <c>--size</c>, a replay and the harness's windowed runs are windows as they were, writing nothing
/// (see <see cref="Start"/>).</item>
/// </list>
/// </summary>
public static class FullScreen
{
    /// <summary>1.1's windowed bit in the raster options word: set by <c>-window</c>/<c>/window</c>, cleared by <c>-full</c>/<c>/full</c>.</summary>
    public const uint WindowBit = 0x2000;

    /// <summary>The key that keeps whether the game is full screen (ours), in <see cref="WindowPlacement.Section"/>.</summary>
    public const string OnKey = "fullscreen";

    /// <summary>The key that keeps the full-screen mode (ours): <c>WxH</c>, or <see cref="DesktopWord"/>.</summary>
    public const string ModeKey = "fullscreenmode";

    /// <summary>The mode word for borderless full screen at the desktop's own size (ours).</summary>
    public const string DesktopWord = "desktop";

    /// <summary>
    /// The four window modes, in the order the Display screen lists them (ours; and
    /// *"we need 4 modes actually. Windowed (with title bar). Borderless windowed.
    /// Fullscreen borderless windowed. Fullscreen."*): a window with its title bar; a window with none,
    /// at a size of its own in the middle of the display; borderless at the desktop's own size; and
    /// full screen at a video mode (an exclusive change of mode, as 1.1's).
    /// </summary>
    public static readonly string[] WindowModes = ["Window", "Borderless Window", "Borderless Full Screen", "Full Screen"];

    /// <summary>The rows of <see cref="WindowModes"/>.</summary>
    public const int ModeWindow = 0, ModeBorderlessWindow = 1, ModeBorderless = 2, ModeFull = 3;

    /// <summary>The key that keeps whether a window has no title bar (ours), in <see cref="WindowPlacement.Section"/>.</summary>
    public const string BorderlessKey = "borderless";

    /// <summary>Whether <c>[window] borderless</c> says the window has no title bar; off when absent.</summary>
    public static bool ReadBorderless(WindowKeys keys) =>
        keys[BorderlessKey] is { } v && !IsOff(v);

    public static void WriteBorderless(IWindowSettings settings, bool on) =>
        settings.Write([new(BorderlessKey, on ? "on" : "off")]);

    /// <summary>
    /// The key that keeps the size picked for borderless full screen (ours:
    /// *"it's locked to cover the entire desktop and it can render whatever resolution it needs"*):
    /// <c>WxH</c>, or <see cref="DesktopWord"/> for the desktop's own. The size is the one the world is
    /// drawn at, and its shape is the picture's (<see cref="Picture.RectOn"/>).
    /// </summary>
    public const string WorldSizeKey = "worldsize";

    /// <summary>What <c>[window] worldsize</c> says: a size, or null for the desktop's own (absent, <c>desktop</c>, or unreadable).</summary>
    public static ScreenMode? ReadWorldSize(WindowKeys keys) =>
        ParseMode(keys[WorldSizeKey], out var m) ? m : null;

    public static void WriteWorldSize(IWindowSettings settings, ScreenMode? size) =>
        settings.Write([new(WorldSizeKey, FormatMode(size))]);

    /// <summary>
    /// The size picked for borderless full screen on a desktop of <paramref name="desk"/>, as it is
    /// kept: <paramref name="picked"/> when it is smaller than the desktop either way and no larger the
    /// other; null — the desktop's own, written <c>desktop</c> — for the desktop's size, a larger one, or
    /// none. Its shape is the picture's and its size the world's (<see cref="Picture"/>).
    /// </summary>
    public static ScreenMode? WorldSizeOn(ScreenMode desk, ScreenMode? picked) =>
        picked is { } p && p.Width <= desk.Width && p.Height <= desk.Height && p != desk ? p : null;

    /// <summary>Which row a state is: a window, with its title bar or without; full screen with no mode of its own (the desktop's); or full screen at a mode.</summary>
    public static int WindowModeOf(bool full, bool hasMode, bool borderless = false) =>
        full ? (hasMode ? ModeFull : ModeBorderless) : borderless ? ModeBorderlessWindow : ModeWindow;

    /// <summary>What <c>[window]</c> keeps for a row: whether full screen, and the mode (null: the desktop's). <paramref name="last"/> is the last exclusive mode, kept for a window so that Alt+Enter goes back to it (the host keeps the mode Alt+Enter goes to instead when it knows one).</summary>
    public static FullScreenSetting SettingFor(int row, ScreenMode? last) => row switch
    {
        ModeWindow or ModeBorderlessWindow => new(false, last ?? DefaultMode),
        ModeBorderless => new(true, null),
        _ => new(true, last ?? DefaultMode),
    };

    /// <summary>The window sizes offered beside the one in use (ours: 1.1 lists the card's modes, as full screen's list does).</summary>
    public static readonly ScreenMode[] WindowSizes =
    [
        new(640, 480), new(800, 600), new(1024, 768), new(1280, 720), new(1280, 1024), new(1366, 768), new(1600, 900),
        new(1920, 1080), new(2560, 1440), new(3440, 1440), new(3840, 2160),
    ];

    /// <summary>
    /// The sizes a window may take on a display: <see cref="WindowSizes"/> that fit in
    /// <paramref name="room"/> — the desktop less the task bar and the window's own frame for a window
    /// with a title bar, the whole desktop for one without — and <paramref name="current"/> whatever
    /// it is, smallest first, once each. A size the display cannot show is not offered (a laptop
    /// was once offered 1920x1080, which it does not support).
    /// </summary>
    public static List<ScreenMode> SizesThatFit(ScreenMode room, ScreenMode? current)
    {
        var list = WindowSizes.Where(m => m.Width <= room.Width && m.Height <= room.Height).ToList();
        if (current is { } now && !list.Contains(now)) list.Add(now);
        return [.. list.OrderBy(m => m.Width).ThenBy(m => m.Height)];
    }

    /// <summary>
    /// 1600x900 (ours), the size of the 1.1 screenshots the work was checked on: the mode a
    /// settings file from before then means when it keeps <c>fullscreen</c> and names none, or names
    /// one that does not read — it was the default until then — and the size stood in for a desktop
    /// that cannot be asked. It is no longer what a first start runs at (<see cref="Default"/>).
    /// </summary>
    public static readonly ScreenMode DefaultMode = new(1600, 900);

    /// <summary>The smallest mode read (ours; 1.1's own list starts at 640x480): anything smaller reads as the default.</summary>
    public const int MinWidth = 640, MinHeight = 480;

    /// <summary>
    /// A first start (ours; full screen at the desktop's own resolution): what a
    /// settings file with neither <see cref="OnKey"/> nor <see cref="ModeKey"/> reads as — borderless full
    /// screen at the desktop's size, with no change of video mode. With <see cref="WorldSizeKey"/>
    /// absent too the world is drawn at the desktop's size, so there are no bars.
    /// </summary>
    public static readonly FullScreenSetting Default = new(true, null);

    /// <summary>
    /// What <paramref name="keys"/> (<c>[window]</c>) keeps: <see cref="Default"/> when it keeps neither
    /// key (a first start); otherwise <see cref="OnKey"/> read as on unless it says off (<c>off</c>,
    /// <c>0</c>, <c>no</c>, <c>false</c>), and <see cref="ModeKey"/> read by <see cref="ParseMode"/> —
    /// <see cref="DefaultMode"/> when it is absent or does not read, as such a file always meant.
    /// </summary>
    public static FullScreenSetting Read(WindowKeys keys)
    {
        string? on = keys[OnKey], text = keys[ModeKey];
        if (on is null && text is null) return Default;
        bool full = on is null || !IsOff(on);
        var mode = ParseMode(text, out var parsed) ? parsed : DefaultMode;
        return new FullScreenSetting(full, mode);
    }

    static bool IsOff(string value) => value.Trim().ToLowerInvariant() is "off" or "0" or "no" or "false";

    /// <summary>
    /// A mode as written: <c>WxH</c> (<c>1600x900</c>, spaces allowed) for that mode, <see cref="DesktopWord"/>
    /// for the desktop (<paramref name="mode"/> null). False, with the default, for anything else or a
    /// mode smaller than <see cref="MinWidth"/> x <see cref="MinHeight"/>.
    /// </summary>
    public static bool ParseMode(string? text, out ScreenMode? mode)
    {
        mode = DefaultMode;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim().ToLowerInvariant();
        if (t == DesktopWord) { mode = null; return true; }
        var parts = t.Split('x');
        if (parts.Length != 2
            || !int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int w)
            || !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)
            || w < MinWidth || h < MinHeight) return false;
        mode = new ScreenMode(w, h);
        return true;
    }

    /// <summary>A mode as it is written: <c>WxH</c>, or <see cref="DesktopWord"/> for null.</summary>
    public static string FormatMode(ScreenMode? mode) => mode is { } m ? m.ToString() : DesktopWord;

    /// <summary>The line written for whether the game is full screen.</summary>
    public static KeyValuePair<string, string> OnValue(bool on) => new(OnKey, on ? "on" : "off");

    /// <summary>The line written for the mode.</summary>
    public static KeyValuePair<string, string> ModeValue(ScreenMode? mode) => new(ModeKey, FormatMode(mode));

    /// <summary>Keeps whether the game is full screen in <paramref name="settings"/>, the file's other lines left as they are.</summary>
    public static void WriteOn(IWindowSettings settings, bool on) => settings.Write([OnValue(on)]);

    /// <summary>Keeps the full-screen mode in <paramref name="settings"/>.</summary>
    public static void WriteMode(IWindowSettings settings, ScreenMode? mode) => settings.Write([ModeValue(mode)]);

    /// <summary>
    /// How the window is made, from the run's flags and what the ini keeps:
    /// <list type="bullet">
    /// <item>a **replay** (and a replay check) is a window at its tape's size — never full screen, never
    /// kept — so it comes out the size its recording's window was;</item>
    /// <item><paramref name="size"/> (<c>--size</c>) is a window of that size, placed by the system and not
    /// kept; with <paramref name="fullscreenFlag"/> it is full screen at that size as a mode;</item>
    /// <item><paramref name="windowFlag"/> (<c>--window</c>, after 1.1's <c>-window</c>) is a window with its title
    /// bar (<see cref="WindowStart.Titled"/>) where it was left, not kept; it wins over
    /// <paramref name="fullscreenFlag"/>, as 1.1's bit does over the default;</item>
    /// <item>a **harness** run (<c>--ui-click</c>, <c>--frames</c>, <c>--frame-ms</c>, <c>--shot</c>) is a
    /// window where it was left, not kept, unless <paramref name="fullscreenFlag"/>;</item>
    /// <item>otherwise it is where it was left and kept, full screen when <paramref name="fullscreenFlag"/>
    /// or the ini says so (<see cref="Default"/>: on), at the ini's mode.</item>
    /// </list>
    /// </summary>
    public static WindowStart Start(FullScreenSetting kept, bool replaying, ScreenMode? size, bool harness,
                                    bool windowFlag, bool fullscreenFlag)
    {
        if (replaying) return new WindowStart(false, false, false, null, "a replay is a window at its tape's size");
        if (size is { } s)
            return fullscreenFlag && !windowFlag
                ? new WindowStart(false, false, true, s, "--size with --fullscreen")
                : new WindowStart(false, false, false, null, "--size");
        if (windowFlag) return new WindowStart(true, false, false, null, "--window") { Titled = true };
        if (harness)
            return new WindowStart(true, false, fullscreenFlag, kept.Mode, fullscreenFlag ? "a harness run with --fullscreen" : "a harness run");
        if (fullscreenFlag) return new WindowStart(true, true, true, kept.Mode, "--fullscreen");
        return new WindowStart(true, true, kept.On, kept.Mode, kept.On ? "[window] fullscreen" : "[window] fullscreen = off");
    }

    /// <summary>
    /// The Display screen's list while full screen: the display's modes (<paramref name="offered"/>, SDL's
    /// list, which names a size once for each refresh rate and density) once each by width and height,
    /// with <paramref name="current"/> always among them, smallest first.
    /// </summary>
    public static List<ScreenMode> ModeList(IEnumerable<ScreenMode> offered, ScreenMode current) =>
        [.. offered.Append(current).Distinct().OrderBy(m => m.Width).ThenBy(m => m.Height)];

    /// <summary>What a request for full screen came to, for the console and the log.</summary>
    public static string Describe(FullScreenLanded landed, ScreenMode? asked, ScreenMode? got, string display) => landed switch
    {
        FullScreenLanded.Exclusive when asked == got => $"window: full screen at {got} on {display}",
        FullScreenLanded.Exclusive => $"window: full screen at {got} on {display} (the closest it offers to {asked})",
        FullScreenLanded.Desktop when asked is null => $"window: full screen at the desktop's size, {got} on {display}",
        FullScreenLanded.Desktop => $"window: {display} offers no mode close to {asked}; full screen at the desktop's size, {got}",
        _ => $"window: full screen was refused; a window",
    };
}
