// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The borderless window, for the platform: the primary display,
// the platform's log, and the arithmetic in Vitomir.Platform.Presentation.WindowPlacement.
using SDL;
using Vitomir.Platform.Presentation;
using static SDL.SDL3;

namespace Vitomir.Platform.Host;

/// <summary>
/// The part of the centred window that asks SDL (the rules and the arithmetic are
/// <see cref="WindowPlacement"/>): the primary display's whole bounds, the window made without decorations at
/// their centre (<see cref="OpenCentred"/>), and a window with no title bar kept above every other window, the
/// task bar included, while it has the input focus (<see cref="Tick"/>, once a frame).
/// </summary>
static unsafe class WindowKeeper
{
    static SDL_Window* window;
    static bool? onTop;

    /// <summary>
    /// The window: no decorations, not resizable, its inside <paramref name="width"/> by <paramref name="height"/>
    /// (cut to the display's when the display is smaller), at the centre of the primary display's whole bounds —
    /// placed before it is first shown — and kept (<see cref="Keep"/>). Says where it went, and its size in
    /// pixels when that is not its size. Null when SDL could not make it.
    /// </summary>
    public static SDL_Window* OpenCentred(string title, int width, int height, Action<string> say)
    {
        var display = SDL_GetPrimaryDisplay();
        SDL_Rect b;
        bool known = SDL_GetDisplayBounds(display, &b);
        var bounds = known ? new DisplayRect(b.x, b.y, b.w, b.h) : new DisplayRect(0, 0, width, height);
        var place = WindowPlacement.Centre(bounds, width, height);
        var w = SDL_CreateWindow(title, place.Width, place.Height, SDL_WindowFlags.SDL_WINDOW_BORDERLESS | SDL_WindowFlags.SDL_WINDOW_HIDDEN);
        if (w == null) return null;
        if (known) SDL_SetWindowPosition(w, place.X, place.Y);
        SDL_ShowWindow(w);
        SDL_SyncWindow(w);
        say(known ? WindowPlacement.Describe(width, height, place, bounds, SDL_GetDisplayName(display) ?? "")
                  : $"window: centred, no title bar, {width}x{height}, placed by the system (no display reported: {SDL_GetError()})");
        int gw, gh, pw, ph;
        if (SDL_GetWindowSize(w, &gw, &gh) && SDL_GetWindowSizeInPixels(w, &pw, &ph) && ((gw, gh) != (place.Width, place.Height) || (pw, ph) != (place.Width, place.Height)))
            say($"window: it came out {gw}x{gh}, drawn at {pw}x{ph} pixels");
        Keep(w, say);
        return w;
    }

    static Action<string> log = _ => { };

    /// <summary>Keeps <paramref name="w"/>, a window with no title bar, above the others while it has the focus (<see cref="Tick"/>).</summary>
    public static void Keep(SDL_Window* w, Action<string> say)
    {
        window = w;
        log = say;
        onTop = null;
        Tick();
    }

    /// <summary>
    /// Once a frame: a kept window is above every other window while it has the input focus and not when it
    /// has not (<see cref="WindowPlacement.OnTop"/>), asked of SDL only when that changes. Read from the window's
    /// own flags, not from the focus events, so it holds whichever loop is running. Nothing for a window with a
    /// title bar (never kept) or one gone full screen.
    /// </summary>
    public static void Tick()
    {
        if (window == null) return;
        var flags = SDL_GetWindowFlags(window);
        bool want = (flags & SDL_WindowFlags.SDL_WINDOW_FULLSCREEN) == 0
                    && WindowPlacement.OnTop((flags & SDL_WindowFlags.SDL_WINDOW_INPUT_FOCUS) != 0);
        if (onTop == want) return;
        if (!SDL_SetWindowAlwaysOnTop(window, want) && want)
            log($"window: could not be put on top ({SDL_GetError()})");
        onTop = want;
    }
}
