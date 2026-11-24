// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The read pane's own open, past its two refusals: the modal panes closed
// and the game camera put back, from any camera mode, by the close of every modal; a single-player game paused
// while it is up; Enter, the keypad's Enter and Escape to close it, and of the mouse's
// clicks only its OK button's. Found and a comparison with 1.1, built.
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **A document that opens puts the game camera back, whatever the camera was doing** (the pane's
/// open → the close of every modal → the camera's return).
///
/// <para>The pane's open refuses first — one already up, or no
/// document of that name — and only then closes every modal, the same "everything
/// modal closed" the shop's and the save screen's opens call: the game camera put back (whenever
/// the one camera exists; the return itself does nothing in the game
/// camera), then the advancement screen, the vault, the subtitle box, the map, the escape menu, the
/// shop, the save pane
/// and the console shut.</para>
///
/// <para>So unlike the caption part before it (<see cref="DocumentOpen.Before"/>, which
/// leaves only the first-person look), <b>a document opened under a script's camera path or a
/// cutscene ends them</b>: the camera is back behind the steered character, and a path that was
/// playing never reaches its end — its <c>pathended</c> is never told (the return swaps the camera's
/// think for the game camera's). That is 1.1's behaviour and Vitomir's, faithful. A
/// refused open touches nothing.</para>
/// </summary>
public static class ReadPaneOpen
{
    /// <summary>
    /// The close of every modal as the read pane's open reaches it (past its refusals): the game camera
    /// back from any mode, then the game's modal panes shut (<paramref name="closePanes"/>
    /// — the shop and the vault are Vitomir's; the map, the escape menu and the save screen pause the
    /// game, so no script opens a document under them). Answers whether the camera changed mode.
    /// </summary>
    public static bool Accepted(WorldCamera? camera, WorldThing? steered, Action? closePanes, FrontEnd? menus = null)
    {
        bool back = camera is not null && camera.BackToGame(steered);     // A camera: back to the game camera
        closePanes?.Invoke();                                             // The panes
        if (menus is { SinglePlayer: true }) menus.Paused = true;         // the game pause set
        return back;
    }

    /// <summary>
    /// **Reading pauses a single-player game** (the pane open's last act): with no network game
    ///  the game pause (<see cref="FrontEnd.Paused"/>)
    /// is set as the pane goes up, so nothing in the world moves while a book is open; the pane's two
    /// closes — its OK button and its key hook — clear it again, in single
    /// player, before they take the pane down. It is the same flag the map, the escape menu and the
    /// Pause key use, so closing a document also carries on a game the Pause key had paused (the
    /// console's <c>read</c> is the only way to open one then). Earlier, Vitomir's world ran on under
    /// an open document.
    /// </summary>
    public static void Closed(FrontEnd? menus)
    {
        if (menus is { SinglePlayer: true }) menus.Paused = false;        // the game pause cleared
    }

    /// <summary>
    /// The keys that close the pane (its key hook), as DirectInput codes: the frame one of
    /// them went down (its down-stamp against the frame counter, in the key table) —
    /// Enter (<c>DIK_RETURN</c>,
    /// 0x1C), the keypad's Enter (<c>DIK_NUMPADENTER</c>, 0x9C) and
    /// Escape (<c>DIK_ESCAPE</c>, 0x01). Once only Escape closed it.
    /// </summary>
    public static readonly int[] CloseKeys = [0x1C, 0x9C, 0x01];

    /// <summary>The key table's down-stamp for a DirectInput code.</summary>
    public static uint DownStamp(int dik) => 0x6f0730u + 0x18u * (uint)dik + 4u;

    /// <summary>
    /// The pane's OK button, <c>readOKbutton</c> (26 × 15 at 456,163 in <c>ui/ReadTextBox.nui</c>):
    /// <b>the only control that closes it</b>. The pane's open binds the close to its release
    /// (binding 1); the pane is <c>dialog=1</c>, so a click anywhere else is swallowed and does nothing.
    /// Earlier, any click closed Vitomir's. (The pane's scroll bar, <c>readScrollBar</c>, came later,
    /// <see cref="ReadPane.Press"/>; a press reaches either wherever the pointer is and whether or
    /// not the status bar is up, <see cref="DialogClicks"/>)
    /// </summary>
    public const string OkButton = "readOKbutton";

    /// <summary>Whether a point in the interface's 640 × 480 (the pane's own space) is on <see cref="OkButton"/>.</summary>
    public static bool OkAt(NuiPane pane, int x, int y)
    {
        int px = pane.Pos.Length > 1 ? pane.Pos[0] : 0, py = pane.Pos.Length > 1 ? pane.Pos[1] : 0;
        foreach (var e in pane.Elements)
        {
            if (e.Type != "button" || e.Pos.Length < 2 || e.Size.Length < 2
                || !e.Name.Equals(OkButton, StringComparison.OrdinalIgnoreCase)) continue;
            int bx = px + e.Pos[0], by = py + e.Pos[1];
            if (x >= bx && y >= by && x < bx + e.Size[0] && y < by + e.Size[1]) return true;
        }
        return false;
    }
}
