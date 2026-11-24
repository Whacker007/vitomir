// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A window with no title bar at a chosen size, in the middle of its display (ours; nothing
// of 1.1's, whose window is its video mode). A wide desktop can be one 7680x1440 desktop — three screens that
// Windows sees as a single display — with the task bar at the far right — and the game run windowed and centred at
// 2560x1440 with no title bar, whatever the task bar's position, and on top of the task bar (on top is
// best). The part that asks SDL is `Vitomir.Platform.Render/Windowing/BorderlessHost.cs`; everything here is rules and
// arithmetic, so it is tested without a display.

namespace Vitomir.Platform.Windowing;

/// <summary>A display as SDL reports it for a borderless window: its name, its **whole** bounds (<c>SDL_GetDisplayBounds</c>, the task bar's part included) and whether it is the primary one.</summary>
public sealed record DisplayBounds(string Name, DisplayRect Bounds, bool Primary);

/// <summary>
/// **The borderless window (ours).** <c>[window] borderless = 2560x1440</c> in the player's
/// <c>vitomir.ini</c> — a setting of the machine — or <c>--borderless 2560x1440</c> for one run: the window is
/// made with no decorations, its inside exactly that size, at the centre of its display's whole bounds, so
/// where the task bar is does not move it. Absent or <c>off</c>, the window is as it always was
/// (<see cref="FullScreen.Start"/>). What decides, first to last (<see cref="Start"/>):
/// <list type="number">
/// <item>a replay is a window at its tape's size, whatever else is said;</item>
/// <item><c>--borderless WxH</c> is borderless at that size — over <c>--fullscreen</c>, <c>--window</c> and
/// <c>--size</c> on the same command line; <c>--borderless off</c> is the window as it always was, the
/// ini's key unheard for the run;</item>
/// <item><c>--size</c>, <c>--window</c> or <c>--fullscreen</c> on the command line: as they always were, the
/// ini's key unheard for the run;</item>
/// <item><c>[window] borderless = WxH</c>: borderless, for an ordinary run and a harness run alike, and over
/// <c>[window] fullscreen</c> (which is on when absent);</item>
/// <item>otherwise as it always was.</item>
/// </list>
/// A borderless window's place is worked out at each start and never written; nothing else in
/// <c>[window]</c> is written by such a run either, so the kept window and full-screen state are there
/// when the key is taken out again.
/// </summary>
public static class Borderless
{
    /// <summary>The key in <see cref="WindowPlacement.Section"/> (ours): <c>WxH</c>, or <see cref="OffWord"/>.</summary>
    public const string Key = "borderless";

    /// <summary>The word for no borderless window (ours); <c>0</c>, <c>no</c> and <c>false</c> read the same.</summary>
    public const string OffWord = "off";

    /// <summary>
    /// A borderless size as written: <c>WxH</c> (as <see cref="FullScreen.ParseMode"/> reads a mode, at
    /// least <see cref="FullScreen.MinWidth"/> x <see cref="FullScreen.MinHeight"/>) for that size, or
    /// <see cref="OffWord"/> for none (<paramref name="size"/> null). False for anything else.
    /// </summary>
    public static bool Parse(string? text, out ScreenMode? size)
    {
        size = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.Trim().ToLowerInvariant() is OffWord or "0" or "no" or "false") return true;
        if (text.Trim().Equals(FullScreen.DesktopWord, StringComparison.OrdinalIgnoreCase)) return false;   // a size, not the desktop's
        if (!FullScreen.ParseMode(text, out var mode) || mode is null) return false;
        size = mode;
        return true;
    }

    /// <summary>What <paramref name="keys"/>' <c>[window] borderless</c> keeps: the size, or null when absent, off or unreadable.</summary>
    public static ScreenMode? Read(WindowKeys keys) => Parse(keys[Key], out var size) ? size : null;

    /// <summary>The name of the display the window was last kept on (<c>[window] display</c>), or null.</summary>
    public static string? KeptDisplay(WindowKeys keys) =>
        keys["display"] is { } name && !string.IsNullOrWhiteSpace(name) ? name.Trim() : null;

    /// <summary>
    /// How the window is made: <paramref name="asToday"/> (what <see cref="FullScreen.Start"/> decided), or
    /// borderless at a size (<see cref="WindowStart.Borderless"/>) — not restored, not kept, not full screen.
    /// <paramref name="flag"/> is <c>--borderless</c>'s value as given (null: not given; one that does not
    /// read counts as not given), <paramref name="kept"/> the ini's (<see cref="Read"/>). The order is the
    /// class's.
    /// </summary>
    public static WindowStart Start(WindowStart asToday, ScreenMode? kept, string? flag, bool replaying,
                                    bool sizeFlag, bool windowFlag, bool fullscreenFlag)
    {
        if (replaying) return asToday;
        if (flag is not null && Parse(flag, out var asked))
            return asked is { } a ? Make(a, "--borderless") : asToday;
        if (sizeFlag || windowFlag || fullscreenFlag) return asToday;
        return kept is { } k ? Make(k, "[window] borderless") : asToday;
    }

    static WindowStart Make(ScreenMode size, string why) => new(false, false, false, null, why) { Borderless = size };

    /// <summary>
    /// A window <paramref name="size"/> big at the centre of <paramref name="bounds"/>, the display's whole
    /// bounds; a size larger than the display, either way, is cut to the display's. On 7680x1440 a
    /// 2560x1440 window is at 2560,0.
    /// </summary>
    public static DisplayRect Centre(DisplayRect bounds, ScreenMode size)
    {
        int w = Math.Min(size.Width, bounds.Width), h = Math.Min(size.Height, bounds.Height);
        return new DisplayRect(bounds.X + (bounds.Width - w) / 2, bounds.Y + (bounds.Height - h) / 2, w, h);
    }

    /// <summary>
    /// The display a borderless window opens on: the one the window was last kept on
    /// (<paramref name="keptDisplay"/>, by name) when it is connected, else the primary, else the first;
    /// null when SDL reports none.
    /// </summary>
    public static DisplayBounds? Display(IReadOnlyList<DisplayBounds> displays, string? keptDisplay) =>
        (keptDisplay is null ? null : displays.FirstOrDefault(d => d.Name == keptDisplay))
        ?? displays.FirstOrDefault(d => d.Primary) ?? displays.FirstOrDefault();

    /// <summary>Where a borderless window of <paramref name="size"/> goes (<see cref="Display"/>, <see cref="Centre"/>), or null with no display: the system places it.</summary>
    public static DisplayRect? Place(ScreenMode size, IReadOnlyList<DisplayBounds> displays, string? keptDisplay) =>
        Display(displays, keptDisplay) is { } d ? Centre(d.Bounds, size) : null;

    /// <summary>The console's line for a borderless window made.</summary>
    public static string Describe(ScreenMode asked, DisplayRect? placed, string display, string why)
    {
        if (placed is not { } r) return $"window: borderless {asked}, placed by the system (no display reported) ({why})";
        string on = display.Length > 0 ? $" on {display}" : "";
        return (r.Width, r.Height) == (asked.Width, asked.Height)
            ? $"window: borderless {asked} at {r.X},{r.Y}{on} ({why})"
            : $"window: borderless {r.Width}x{r.Height} at {r.X},{r.Y}{on}, the display being smaller than the {asked} asked ({why})";
    }

    /// <summary>
    /// Whether a borderless window is kept above every other window — the task bar included — given whether
    /// it has the input focus: on top while it has it, not when it has not, so another program brought
    /// forward with Alt+Tab is not covered by the game.
    /// </summary>
    public static bool OnTop(bool focused) => focused;
}
