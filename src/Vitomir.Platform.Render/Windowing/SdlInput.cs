// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The input path's SDL half (the platform's, both programs'): SDL's events made the platform's neutral ones.
using Vitomir.Platform.Render.Classic;
using SDL;
using Vitomir.Platform.Modules;
using static SDL.SDL3;

namespace Vitomir.Platform.Render.Windowing;

/// <summary>
/// **SDL's events in the platform's terms**: the one place
/// a host (both programs') turns an <c>SDL_Event</c> into a <see cref="LoopInput"/>. The frame controller, the binding layer
/// (<see cref="KeyInput"/>) and the tape (<see cref="InputTape"/>) read only what this makes. The numbers carry over as
/// they are (a key's scan code and key code, the modifiers' bits, a button's number, the buttons held), since the
/// platform's are SDL 3's own; an event of a kind nothing past the host reads (a window shown or exposed, its pixels'
/// size, a device added) makes none. As the tape's live side (<see cref="IInputHost"/>) it polls SDL through the picture
/// (<see cref="PictureHost.Poll"/>, the pointer in the picture's pixels before a tape writes it) and reads SDL's mouse,
/// modifiers and keyboard, for the host's window.
/// </summary>
sealed unsafe class SdlInput(Func<nint> window) : IInputHost
{
    SDL_Window* Window => (SDL_Window*)window();

    public bool Poll(out LoopInput e)
    {
        SDL_Event raw;
        while (PictureHost.Poll(&raw))
            if (From(raw) is { } made) { e = made; return true; }
        e = default;
        return false;
    }

    public bool Closed()
    {
        bool closed = false;
        SDL_Event junk;
        while (SDL_PollEvent(&junk))
            if (junk.Type == SDL_EventType.SDL_EVENT_QUIT) closed = true;
        return closed;
    }

    public uint Buttons(out float x, out float y)
    {
        float px, py;
        uint b = (uint)PictureHost.MouseState(&px, &py);
        (x, y) = (px, py);
        return b;
    }

    public LoopMods Mods
    {
        get => (LoopMods)(ushort)SDL_GetModState();
        set => SDL_SetModState((SDL_Keymod)(ushort)value);
    }

    public ReadOnlySpan<bool> Keyboard()
    {
        int n;
        SDLBool* keys = SDL_GetKeyboardState(&n);
        return keys == null ? [] : new ReadOnlySpan<bool>(keys, n);
    }

    public void ResetKeyboard() => SDL_ResetKeyboard();
    public bool TextInputActive => Window != null && SDL_TextInputActive(Window);
    public void ToggleFullScreen() => FullScreenHost.Toggle();

    public string? Clipboard() => SDL_HasClipboardText() ? SDL_GetClipboardText() : null;

    public void SetClipboard(string text) => SDL_SetClipboardText(text);
    public bool HasWindow => Window != null;

    public (int Width, int Height) PictureSize()
    {
        int w = 0, h = 0;
        if (Window != null) PictureHost.Size(Window, &w, &h);      // the picture's, in borderless full screen with bars
        return (w, h);
    }

    public void Resize(int w, int h)
    {
        var made = Window;
        if (made == null) return;
        // A shrunk replay (ours): the picture takes the recorded size and the window stays small.
        if (PictureHost.Shrunk is not null)
        {
            PictureHost.Shrunk = (w, h);
            var (ww, wh) = PictureHost.ShrunkWindow(w, h);
            SDL_SetWindowSize(made, ww, wh);
            SDL_SyncWindow(made);
            return;
        }
        SDL_SetWindowSize(made, w, h);
        SDL_SyncWindow(made);
        int gw, gh;
        SDL_GetWindowSize(made, &gw, &gh);
        if ((gw, gh) != (w, h)) Console.WriteLine($"replay: the window is {gw}x{gh}, not the recording's {w}x{h}; what the pointer picks will differ");
    }

    /// <summary>
    /// **The one place an <c>SDL_Event</c> becomes a <see cref="LoopInput"/>**: the platform renderer's
    /// <see cref="SdlEvents.From"/>, which both hosts call (this program's and <c>vitomir-platform</c>'s Classic host), so
    /// the two make the same events of the same SDL events.
    /// </summary>
    public static LoopInput? From(in SDL_Event e) => SdlEvents.From(e);
}
