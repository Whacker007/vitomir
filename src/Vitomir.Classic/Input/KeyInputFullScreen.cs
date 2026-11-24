// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Alt+Enter, anywhere (ours, `Engine.Ui.FullScreen`): the one command read before the menus, the text
// fields and the binding layer hear the event (`KeyBindings.HostFirst`).
using Vitomir.Engine.Ui;
using Vitomir.Platform.Modules;

namespace Vitomir;

static partial class KeyInput
{
    /// <summary>The input that went down as the toggle, whose release is eaten too; -1 when none is held.</summary>
    static int fullScreenDown = -1;

    /// <summary>
    /// Whether <paramref name="e"/> is <see cref="KeyCommand.FullScreen"/>'s — Alt+Enter unless moved —
    /// and so eaten: its press toggles the window and full screen (<see cref="FullScreenHost.Toggle"/>; a
    /// repeat only eaten; the host's toggle, <see cref="IInputHost.ToggleFullScreen"/>), and its release goes nowhere either, so plain Enter's readers (the chat line,
    /// the key handler; the console; a text box; a conversation) never hear half of it. Not while the key page
    /// waits for a key (<paramref name="capturing"/>): Alt+Enter can be given to another command there.
    /// The modifiers are the event's own, which a replay has from its tape.
    /// </summary>
    public static bool FullScreenKey(in LoopInput e, bool capturing)
    {
        switch (e.Kind)
        {
            case LoopInputKind.KeyDown or LoopInputKind.ButtonDown when !capturing:
            {
                bool key = e.Kind == LoopInputKind.KeyDown;
                int input = key ? Scan(e) : KeyChord.MouseBase + e.Code;
                if (!Bindings.Is(KeyCommand.FullScreen, input, key ? HeldMods(e.Mods) : HeldMods())) return false;
                if (key && e.Repeat) return true;
                fullScreenDown = input;
                InputTape.Live.ToggleFullScreen();
                return true;
            }
            case LoopInputKind.KeyUp when Scan(e) == fullScreenDown:
            case LoopInputKind.ButtonUp when KeyChord.MouseBase + e.Code == fullScreenDown:
                fullScreenDown = -1;
                return true;
        }
        return false;
    }
}
