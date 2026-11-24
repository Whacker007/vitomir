// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Whether 1.1's frame runs at all: its window procedure's WM_ACTIVATE and the message
// loop of WinMain. Read in a comparison with 1.1.
namespace Vitomir.Engine.Ui;

/// <summary>
/// **A single-player game stands still behind an inactive window.** 1.1's window procedure keeps
/// one flag: WM_ACTIVATE sets it when the window is activated and
/// not minimised (the low word of wParam not <c>WA_INACTIVE</c>, the high word 0 — and takes the
/// input devices back), and clears it on anything else. WinMain's loop runs a
/// frame only when no message is waiting, and then: with the flag set,
/// an ordinary frame; with it clear, **in a network session** a frame with
/// parameter 1 — the world ticks and the network pumps, but no key is read and nothing is drawn —
/// and **in single player nothing at all**. So Alt+Tab out of a fight, or a minimised window, and
/// the whole game waits: nobody walks, nothing burns, no clock moves; the music, which the sound
/// card plays by itself, goes on. When the window comes back the next frame's clock takes the time
/// that passed as one step, held to the frame's limit.
///
/// Vitomir ran on regardless until then. This decides; the host hears SDL's focus and minimise
/// events and asks <see cref="Runs"/> before each frame. Ours: the window counts as active until it
/// is told otherwise (1.1 waits for its first WM_ACTIVATE), so a run with no window events at all
/// — headless, a harness — is never held; and a network game's frame is drawn as usual.
/// </summary>
public sealed class WindowActive
{
    /// <summary>Whether the window has the focus (WM_ACTIVATE's low word not <c>WA_INACTIVE</c>).</summary>
    public bool Focused { get; private set; } = true;

    /// <summary>Whether the window is minimised (WM_ACTIVATE's high word).</summary>
    public bool Minimised { get; private set; }

    /// <summary>The flag itself: focused and not minimised.</summary>
    public bool Active => Focused && !Minimised;

    /// <summary>The window gained (true) or lost (false) the focus.</summary>
    public void Focus(bool gained) => Focused = gained;

    /// <summary>The window was minimised (true) or restored (false).</summary>
    public void Minimise(bool minimised) => Minimised = minimised;

    /// <summary>
    /// Whether this pass of the loop runs a frame: always while the window is active;
    /// behind an inactive one only in a network session.
    /// </summary>
    public bool Runs(bool network) => Active || network;
}
