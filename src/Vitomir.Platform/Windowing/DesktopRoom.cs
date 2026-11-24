// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The room a window has on the desktop, asked while the display is in another video mode, and when a
// full-screen window is kept above the task bar (ours). Both are from a run on a
// three-screen desktop (a run's log).
namespace Vitomir.Platform.Windowing;

/// <summary>
/// **Two rules about the desktop behind a full screen (ours).**
///
/// <para><see cref="UsableOnDesktop"/>: the Display screen's Window row lists the sizes a window can have
/// on the desktop, less the task bar. Asked while the game is in a video mode of its own, the display's
/// bounds and usable bounds are *that mode's* — 1600x900 and 1387x900 on a desktop of 7680x1440 — and the
/// row offered nothing over 1366x768 (*"it wouldn't let me pick anything that was a few resolution levels
/// under 1600x900"*). Leaving the mode gives the desktop back, so the room is the desktop's own size less
/// the task bar's margins, which are the same thickness in either mode.</para>
///
/// <para><see cref="StaysOnTop"/>: in a video mode of its own on that desktop the window covered its
/// display exactly and the task bar was drawn over it all the same (the system's own rectangles in the
/// log: the window 1600x900 at 0,0 on a monitor of 1600x900, the task bar 213x900 at 1387,0). The task
/// bar is a topmost window that the change of mode moves after ours has come to the front. So a
/// full-screen window with a mode of its own is kept topmost while it has the keyboard, and let go the
/// moment it has not.</para>
/// </summary>
public static class DesktopRoom
{
    /// <summary>A margin thicker than this part of the desktop's side is no task bar (ours).</summary>
    public const int ThickestBar = 4;

    /// <summary>
    /// The usable part of the display once it is in the desktop's own mode <paramref name="desk"/>,
    /// from the <paramref name="bounds"/> and <paramref name="usable"/> bounds it reports now. In the
    /// desktop's mode already, <paramref name="usable"/> as it is. In another, the desktop's size less
    /// the four margins the task bar takes now. **A margin over a quarter of the desktop's side is taken
    /// for stale and left out**: just after his display left 1600x900 it reported 7680x540 usable of
    /// 7680x1440, the old mode's height still in it.
    /// </summary>
    public static DisplayRect UsableOnDesktop(ScreenMode desk, DisplayRect bounds, DisplayRect usable)
    {
        int Margin(int thick, int side) => thick <= 0 || thick > side / ThickestBar ? 0 : thick;
        int left = Margin(usable.X - bounds.X, desk.Width), right = Margin(bounds.Right - usable.Right, desk.Width);
        int top = Margin(usable.Y - bounds.Y, desk.Height), bottom = Margin(bounds.Bottom - usable.Bottom, desk.Height);
        return new DisplayRect(bounds.X + left, bounds.Y + top, desk.Width - left - right, desk.Height - top - bottom);
    }

    /// <summary>
    /// Whether the window is kept above every other, the task bar with them: full screen, at a video
    /// mode of its own, and holding the keyboard. Borderless full screen changes no mode, so nothing
    /// moves the task bar over it, and it is left as any window.
    /// </summary>
    public static bool StaysOnTop(bool full, bool ownMode, bool keyboard) => full && ownMode && keyboard;
}
