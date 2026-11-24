// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A dialog takes every click (the newest dialog on the on-top list is the active pane,
// wherever the pointer is), so the read dialog's press is settled before anything else is asked —
// the status bar up or hidden. Earlier, Vitomir reached the dialog's scroll bar and OK button only
// from inside the status bar's branch, so with the bar hidden (a cutscene, a camera path, the
// Storyteller's seat, `--no-hud`) the dialog could not be closed with the mouse.
namespace Vitomir.Engine.Ui;

/// <summary>
/// **The newest dialog takes every click**. The ui library's frame walks the on-top list
/// first and stops at the first pane whose flags carry the dialog bit, <c>0x02</c>
/// (<c>dialog=1</c>), without testing the pointer at all — only when there is none does it look for
/// a pane under the pointer. That pane is the active one: it gets the keys and
/// the clicks, and the frame's input runs the world's click only when no pane is active. So while the read
/// dialog is up nothing under it hears a button — not the status bar, not a pane beside it, not the
/// world — whether the status bar is showing or not, and whatever the camera is doing (the free
/// camera included). The shipped game's one <c>dialog=1</c> pane is <c>ui/ReadTextBox.nui</c>.
/// </summary>
public static class DialogClicks
{
    /// <summary>What a press on the read dialog reached.</summary>
    public enum Took
    {
        /// <summary>None of its controls: swallowed, nothing happens (the pane has no hook for it).</summary>
        Nothing,
        /// <summary>Its scroll bar or slider, which make the control's press sound.</summary>
        ScrollBar,
        /// <summary>Its OK button, held; the release over it closes the dialog.</summary>
        Ok,
    }

    /// <summary>A left press on the dialog: what it reached, the control for its sound, and what the bar said.</summary>
    public readonly record struct Press(Took What, UiControl? Control, string? Said);

    /// <summary>
    /// The left button's press at interface point (<paramref name="x"/>, <paramref name="y"/>) with
    /// the read dialog up: the slider or the track first — the slider is the bar's child and is hit
    /// before it — then the OK button, else nothing. The caller takes the press whatever this answers;
    /// the right and middle buttons reach none of the dialog's controls (its OK is bound to the left
    /// button's release alone, binding 1), so with it up they go nowhere.
    /// </summary>
    public static Press Left(ReadPane pane, int x, int y)
    {
        if (pane.TrackAt(x, y) is { } track) return new(Took.ScrollBar, track, pane.Press(x, y));
        if (ReadPaneOpen.OkAt(pane.Pane, x, y)) return new(Took.Ok, null, null);
        return new(Took.Nothing, null, null);
    }
}
