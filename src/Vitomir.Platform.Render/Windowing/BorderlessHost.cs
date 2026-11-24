// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The borderless window (ours; `Platform.Windowing.Borderless` has the rules and the arithmetic).
// This file is the part that asks SDL: the displays' whole bounds, the window made without decorations and
// put at the centre, and whether it has the focus.
using SDL;
using Vitomir.Platform.Windowing;
using static SDL.SDL3;

namespace Vitomir.Platform.Render.Windowing;

/// <summary>
/// Makes the game's window with no title bar at the size <c>[window] borderless</c> or <c>--borderless</c>
/// names, at the centre of its display's whole bounds (<see cref="Open"/>, from
/// <see cref="WindowPlaceHost.Open"/>), and keeps it above every other window, the task bar included,
/// while it has the input focus (<see cref="Tick"/>, once a frame). Nothing here is written to
/// <c>vitomir.ini</c>.
/// </summary>
static unsafe class BorderlessHost
{
    static SDL_Window* window;
    static bool? onTop;

    /// <summary>
    /// The run's start with borderless heard (<see cref="Borderless.Start"/>): <paramref name="asToday"/> is
    /// what <see cref="FullScreen.Start"/> decided, <paramref name="flag"/> is <c>--borderless</c>'s value.
    /// A value that does not read is said and passed over.
    /// </summary>
    public static WindowStart Plan(WindowStart asToday, WindowKeys ini, string? flag, bool sizeFlag, bool windowFlag, bool fullscreenFlag)
    {
        if (flag is not null && !Borderless.Parse(flag, out _))
            Console.WriteLine($"window: --borderless {flag} is not a size (WxH, at least {FullScreen.MinWidth}x{FullScreen.MinHeight}) or off; passed over");
        return Borderless.Start(asToday, Borderless.Read(ini), flag, WindowRun.Replaying, sizeFlag, windowFlag, fullscreenFlag);
    }

    /// <summary>
    /// The window: no decorations, not resizable, its inside <paramref name="size"/> (cut to the display's
    /// when the display is smaller), at the centre of the whole bounds of the display the window was last
    /// kept on, else the primary one — placed before it is first shown. Says where it went, and its size in
    /// pixels when that is not its size.
    /// </summary>
    public static SDL_Window* Open(string title, ScreenMode size, string why)
    {
        var displays = Displays();
        string? keptOn = Borderless.KeptDisplay(WindowRun.Settings.Read());
        var on = Borderless.Display(displays, keptOn);
        var place = Borderless.Place(size, displays, keptOn);
        int w = place?.Width ?? size.Width, h = place?.Height ?? size.Height;
        window = SDL_CreateWindow(title, w, h, SDL_WindowFlags.SDL_WINDOW_BORDERLESS | SDL_WindowFlags.SDL_WINDOW_HIDDEN);
        if (window == null) return null;
        WindowIcon.Set(window);
        if (place is { } r) SDL_SetWindowPosition(window, r.X, r.Y);
        SDL_ShowWindow(window);
        SDL_SyncWindow(window);
        Console.WriteLine(Borderless.Describe(size, place, on?.Name ?? "", why));
        int gw, gh, pw, ph;
        if (SDL_GetWindowSize(window, &gw, &gh) && SDL_GetWindowSizeInPixels(window, &pw, &ph) && ((gw, gh) != (w, h) || (pw, ph) != (w, h)))
            Console.WriteLine($"window: it came out {gw}x{gh}, drawn at {pw}x{ph} pixels");
        onTop = null;
        Tick();
        return window;
    }

    /// <summary>
    /// Once a frame: a borderless window is above every other window while it has the input focus and not
    /// when it has not (<see cref="Borderless.OnTop"/>), asked of SDL only when that changes. Read from the
    /// window's own flags, not from the focus events, so it holds in whichever loop is running. Nothing for
    /// a window that is not borderless, or one gone full screen with Alt+Enter.
    /// </summary>
    public static void Tick()
    {
        if (window == null) return;
        var flags = SDL_GetWindowFlags(window);
        bool want = (flags & SDL_WindowFlags.SDL_WINDOW_FULLSCREEN) == 0
                    && Borderless.OnTop((flags & SDL_WindowFlags.SDL_WINDOW_INPUT_FOCUS) != 0);
        if (onTop == want) return;
        if (!SDL_SetWindowAlwaysOnTop(window, want) && want)
            Console.WriteLine($"window: could not be put on top ({SDL_GetError()})");
        onTop = want;
    }

    /// <summary>Every connected display with its whole bounds (<c>SDL_GetDisplays</c>, <c>SDL_GetDisplayBounds</c>).</summary>
    static List<DisplayBounds> Displays()
    {
        var list = new List<DisplayBounds>();
        int count = 0;
        var ids = SDL_GetDisplays(&count);
        if (ids == null) return list;
        var primary = SDL_GetPrimaryDisplay();
        for (int i = 0; i < count; i++)
        {
            SDL_Rect b;
            if (!SDL_GetDisplayBounds(ids[i], &b)) continue;
            list.Add(new DisplayBounds(SDL_GetDisplayName(ids[i]) ?? "", new DisplayRect(b.x, b.y, b.w, b.h), ids[i] == primary));
        }
        SDL_free(ids);
        return list;
    }
}
