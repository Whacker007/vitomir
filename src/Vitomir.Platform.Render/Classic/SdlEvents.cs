// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// SDL's events made the platform's neutral ones (once in the old program's SdlInput; now here, for both of the Classic game's hosts).
using SDL;
using Vitomir.Platform.Modules;
using static SDL.SDL3;

namespace Vitomir.Platform.Render.Classic;

/// <summary>
/// **The one conversion of SDL's events into the platform's neutral ones** (moved here from
/// the old program's <c>SdlInput</c>): <c>vitomir-tools</c>' host and <c>vitomir-platform</c>'s Classic host both
/// make their <see cref="LoopInput"/>s here, so a tape recorded through one is the same events through the other. The numbers
/// are SDL 3's own (<c>NeutralInputTests</c> pins them), so nothing is converted but the event's shape.
/// </summary>
public static unsafe class SdlEvents
{
    /// <summary>The neutral event an SDL event is, or null when it is one nothing past the host reads.</summary>
    public static LoopInput? From(in SDL_Event e)
    {
        long t = (long)e.common.timestamp;
        switch (e.Type)
        {
            case SDL_EventType.SDL_EVENT_KEY_DOWN or SDL_EventType.SDL_EVENT_KEY_UP:
                return new LoopInput(e.Type == SDL_EventType.SDL_EVENT_KEY_DOWN ? LoopInputKind.KeyDown : LoopInputKind.KeyUp)
                {
                    Code = (int)e.key.scancode, Key = (int)(uint)e.key.key, Mods = (LoopMods)(ushort)e.key.mod, Repeat = e.key.repeat, TimeNs = t,
                };
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN or SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
                return new LoopInput(e.Type == SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN ? LoopInputKind.ButtonDown : LoopInputKind.ButtonUp)
                {
                    Code = e.button.button, X = e.button.x, Y = e.button.y, TimeNs = t,
                };
            case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
                return new LoopInput(LoopInputKind.PointerMove)
                {
                    X = e.motion.x, Y = e.motion.y, DX = e.motion.xrel, DY = e.motion.yrel, Buttons = (uint)e.motion.state, TimeNs = t,
                };
            case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
                return new LoopInput(LoopInputKind.Wheel) { X = e.wheel.mouse_x, Y = e.wheel.mouse_y, DX = e.wheel.x, DY = e.wheel.y, TimeNs = t };
            case SDL_EventType.SDL_EVENT_TEXT_INPUT:
                return new LoopInput(LoopInputKind.Text) { Text = PtrToStringUTF8(e.text.text, false) ?? "", TimeNs = t };
            case SDL_EventType.SDL_EVENT_QUIT:
                return new LoopInput(LoopInputKind.Quit) { TimeNs = t };
        }
        if (WindowKind(e.Type) is not { } kind) return null;
        return new LoopInput(kind) { X = e.window.data1, Y = e.window.data2, TimeNs = t };
    }

    /// <summary>The window events the game reads, by the neutral kind each is.</summary>
    public static LoopInputKind? WindowKind(SDL_EventType type) => type switch
    {
        SDL_EventType.SDL_EVENT_WINDOW_FOCUS_GAINED => LoopInputKind.FocusGained,
        SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST => LoopInputKind.FocusLost,
        SDL_EventType.SDL_EVENT_WINDOW_RESIZED => LoopInputKind.Resized,
        SDL_EventType.SDL_EVENT_WINDOW_MOUSE_ENTER => LoopInputKind.PointerEnter,
        SDL_EventType.SDL_EVENT_WINDOW_MOUSE_LEAVE => LoopInputKind.PointerLeave,
        SDL_EventType.SDL_EVENT_WINDOW_MOVED => LoopInputKind.Moved,
        SDL_EventType.SDL_EVENT_WINDOW_MINIMIZED => LoopInputKind.Minimized,
        SDL_EventType.SDL_EVENT_WINDOW_MAXIMIZED => LoopInputKind.Maximized,
        SDL_EventType.SDL_EVENT_WINDOW_RESTORED => LoopInputKind.Restored,
        SDL_EventType.SDL_EVENT_WINDOW_DISPLAY_CHANGED => LoopInputKind.DisplayChanged,
        SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED => LoopInputKind.CloseRequested,
        _ => null,
    };
}
