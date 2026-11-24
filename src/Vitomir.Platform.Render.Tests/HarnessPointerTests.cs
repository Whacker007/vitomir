// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using SDL;
using Vitomir.Platform.Render.Windowing;

namespace Vitomir.Platform.Render.Tests;

/// <summary>
/// **Only the harness has the pointer** (<see cref="PictureHost.HarnessPointer"/>, ours). A test's run sharing the desktop
/// with others heard the real pointer move off and leave its window between a harness press and its release, and the click
/// was lost — a save screen's Save, once at 1280x800 and once before as the escape menu's. In a run a harness drives, the
/// system's pointer — its motion, buttons, wheel and leaving the window — is not heard; the harness's own events carry
/// <see cref="PictureHost.HarnessMouse"/> and are.
/// </summary>
public class HarnessPointerTests
{
    static SDL_Event Of(SDL_EventType type, uint which = 0)
    {
        SDL_Event e = default;
        e.type = (uint)type;
        if (type == SDL_EventType.SDL_EVENT_MOUSE_MOTION) e.motion.which = (SDL_MouseID)which;
        else if (type is SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN or SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP) e.button.which = (SDL_MouseID)which;
        else if (type == SDL_EventType.SDL_EVENT_MOUSE_WHEEL) e.wheel.which = (SDL_MouseID)which;
        return e;
    }

    [Fact]
    public void The_system_s_pointer_is_told_from_the_harness_s_and_nothing_else_is_taken_for_it()
    {
        foreach (var type in new[] { SDL_EventType.SDL_EVENT_MOUSE_MOTION, SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN,
                                     SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP, SDL_EventType.SDL_EVENT_MOUSE_WHEEL })
        {
            Assert.True(PictureHost.SystemPointer(Of(type)));                       // the real mouse (id 0)
            Assert.True(PictureHost.SystemPointer(Of(type, 1)));                    // or any other mouse
            Assert.False(PictureHost.SystemPointer(Of(type, PictureHost.HarnessMouse)));
        }
        Assert.True(PictureHost.SystemPointer(Of(SDL_EventType.SDL_EVENT_WINDOW_MOUSE_LEAVE)));
        // The keyboard, typing, the window's focus and its entering are left to be heard.
        foreach (var type in new[] { SDL_EventType.SDL_EVENT_KEY_DOWN, SDL_EventType.SDL_EVENT_TEXT_INPUT,
                                     SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST, SDL_EventType.SDL_EVENT_WINDOW_MOUSE_ENTER })
            Assert.False(PictureHost.SystemPointer(Of(type)));
    }
}
