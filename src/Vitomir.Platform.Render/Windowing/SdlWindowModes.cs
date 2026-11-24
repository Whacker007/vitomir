// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The window's modes for the Options screen (once in the old program: what OptionsHost asked of SDL directly; the platform's, both programs').
using Vitomir.Platform.Render.Classic;
using SDL;
using Vitomir.Platform.Windowing;
using static SDL.SDL3;

namespace Vitomir.Platform.Render.Windowing;

/// <summary>
/// **The window's modes and sizes, host side** (<see cref="IWindowModes"/>): the Display page's rows and modes are
/// <see cref="FullScreenHost"/>'s, a window's size SDL's. The calls are the ones <c>OptionsHost</c> made itself until the
/// controller moved into the Classic module's library, in the same order, saying the same lines.
/// </summary>
sealed unsafe class SdlWindowModes(Func<nint> window) : IWindowModes
{
    SDL_Window* Window => (SDL_Window*)window();

    public int Mode() => FullScreenHost.WindowMode();

    public IReadOnlyList<(int Width, int Height)> ModesFor(int row) => [.. FullScreenHost.ModesFor(row).Select(m => (m.Width, m.Height))];

    public (int Width, int Height) CurrentFor(int row) { var m = FullScreenHost.CurrentFor(row); return (m.Width, m.Height); }

    public bool SetDisplay(int row, int width, int height) => FullScreenHost.SetDisplay(row, new ScreenMode(width, height));

    public bool IsFull => FullScreenHost.IsFull;

    public (int Width, int Height) Current() { var m = FullScreenHost.Current(); return (m.Width, m.Height); }

    public (int Width, int Height)? Desktop() => FullScreenHost.Desktop() is { } d ? (d.Width, d.Height) : null;

    public IReadOnlyList<(int Width, int Height)>? FullModes() => FullScreenHost.Modes() is { } full ? [.. full.Select(m => (m.Width, m.Height))] : null;

    public bool SetFullMode(int width, int height) => FullScreenHost.SetMode(width, height);

    public (int Width, int Height) WindowSize()
    {
        int w, h;
        SDL_GetWindowSize(Window, &w, &h);
        return (w, h);
    }

    public bool SetWindowSize(int width, int height)
    {
        bool sized = SDL_SetWindowSize(Window, width, height);
        SDL_SyncWindow(Window);
        WindowFrameHost.Hold();
        WindowPlaceHost.Note();     // the run's window now, what Alt+Enter comes back to
        FullScreenHost.Say(sized ? $"window: sized to {width}x{height} from the Display screen" : $"window: the size {width}x{height} was refused ({SDL_GetError()})");
        return sized;
    }
}
