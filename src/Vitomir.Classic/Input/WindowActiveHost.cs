// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Vitomir.Platform.Modules;

namespace Vitomir;

/// <summary>
/// The window's events (neutral) told to <see cref="WindowActive"/>, the way 1.1's window procedure hears
/// WM_ACTIVATE: the focus gained or lost, the window minimised or restored.
/// </summary>
static class WindowActiveHost
{
    /// <summary>Tells <paramref name="active"/> what this event says about the window, if anything.</summary>
    public static void Hear(this WindowActive active, in LoopInput e)
    {
        switch (e.Kind)
        {
            case LoopInputKind.FocusGained: active.Focus(true); LetGo("gained"); break;
            case LoopInputKind.FocusLost: active.Focus(false); LetGo("lost"); break;
            case LoopInputKind.Minimized: active.Minimise(true); break;
            // Out of the minimised state to a maximised window SDL says "maximised", not "restored".
            case LoopInputKind.Restored or LoopInputKind.Maximized: active.Minimise(false); break;
        }
    }

    /// <summary>
    /// **Nothing held survives a focus change** (ours): the binding layer's held keys and buttons, the
    /// harness's, SDL's own keyboard and modifiers (<c>KeyInput.LetGo</c>), and the free camera's keys and
    /// drag look. Said on the console when anything was held.
    /// </summary>
    static void LetGo(string how)
    {
        var held = KeyInput.LetGo();
        FreeCameraInput.LetGo();
        if (held.Count > 0) Console.WriteLine($"  keys: the window {how} the focus; let go of {string.Join(", ", held)}");
    }
}
