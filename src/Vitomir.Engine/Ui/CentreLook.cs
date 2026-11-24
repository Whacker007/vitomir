// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The swivel lock's centred look (ours): *"For the ctrl-middlemouse
// view, I want to change it so that it will lock the cursor in the middle of the visible screen area, with
// the red tip of the anhk in the center. I also want it to be able to continue clicking so that if there's
// something underneath that's in range or accessible, christof will interact with whatever it is
// appropriately. Attacking, picking up an object, going through a door, etc. When he opens a panel like the
// inventory or char sheet, it should automatically go back to free cursor if it doesn't already."*
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ui;

/// <summary>What is open over the game camera, for <see cref="CentreLook.Opened"/>: a pane, a screen or a box.</summary>
[Flags]
public enum OpenPanes
{
    None = 0,
    Character = 1 << 0,
    Inventory = 1 << 1,
    Quests = 1 << 2,
    Disciplines = 1 << 3,
    /// <summary>The vault (a chest's or a body's pane).</summary>
    Vault = 1 << 4,
    Map = 1 << 5,
    Shop = 1 << 6,
    /// <summary>A document being read.</summary>
    Document = 1 << 7,
    /// <summary>A conversation's answer box, offering replies — which need the pointer to be chosen.</summary>
    Answers = 1 << 8,
    Advancement = 1 << 9,
    /// <summary>A menu of the front end's: the escape menu, the save and load screens, a message box.</summary>
    Menu = 1 << 10,
}

/// <summary>
/// **The swivel lock's centred look** (ours; <see cref="SwivelHold"/> is the lock itself, Ctrl and the
/// middle button, <c>[qol] camera</c>'s). 1.1 has no such thing: its swivel pins the pointer
/// where it was and turns the camera under it. While the lock is on:
/// <list type="bullet">
/// <item>**the mouse turns the camera**, as the swivel does;</item>
/// <item>**the game's pointer sits at the middle of the world's view** — the columns and rows the world is
/// drawn into (<see cref="Pick.View"/>; the status bar's share of the height,
/// <c>FrameRenderer.WorldRows</c>), not the whole window — with the cursor's point on it. The point is the
/// cursor's hotspot, which for the ankh (<c>cursorDefault</c>) is the top left of its art, the red tip
/// (<see cref="CursorAnchor.TopLeft"/>); the sword and the hand put theirs there too,
/// and the centred four (talk, the refusal, select, target) their middles;</item>
/// <item>**the pick runs there every frame** — the camera turns under a pointer that does not move — so
/// the highlight, the description box and the cursor's shape are what is under the middle;</item>
/// <item>**a click is a click at the middle** — the left button's act and the right button's cast, with
/// their modifiers — so it attacks, picks up, opens a door or talks exactly as a click there would.</item>
/// </list>
/// **Opening anything that wants the pointer ends it** (<see cref="Opened"/>): a pane (the character sheet,
/// the inventory, the quests, the disciplines, a vault), the map, a shop, a document, a conversation's
/// answer box, the advancement screen or a menu — and a cutscene or anything else that takes the game
/// camera ends it already (<see cref="SwivelHold.Frame"/>). A pane that was open when the lock was turned on
/// does not end it (the middle is then the middle of the half the world has); only one opening does.
/// **The pointer is left at the middle** where it was drawn, free again — not put back where it was before
/// the lock — so it does not jump; turning the lock off with its own keys does the same.
/// </summary>
public sealed class CentreLook
{
    OpenPanes open;

    /// <summary>
    /// Where the lock puts the pointer. <see cref="X"/>, <see cref="Y"/>: the mouse's place in the window's
    /// pixels, as SDL gives it and <see cref="Pick.Ray(FreeCamera, float, float, int, int, int)"/> takes it —
    /// the pixel whose ray is the view's own axis. <see cref="TipX"/>, <see cref="TipY"/>: the cursor's
    /// point in the <c>.nui</c> 640 x 480 space (y up) that the cursor is drawn in — exactly the middle,
    /// not rounded to the interface's pixels as a moved pointer is.
    /// </summary>
    public readonly record struct Point(float X, float Y, float TipX, float TipY);

    /// <summary>
    /// The middle of the world's view for a window <paramref name="width"/> x <paramref name="height"/>
    /// whose top <paramref name="rows"/> the world is drawn into, across <paramref name="view"/>'s columns.
    /// Null when the world is not drawn at all (a pane on each side, the view's state 4).
    /// </summary>
    public static Point? At(int width, int height, int rows, ViewColumns view)
    {
        if (!view.Drawn || width <= 0 || height <= 0) return null;
        if (rows <= 0 || rows > height) rows = height;
        var (x0, cols) = view.Pixels(width);
        float cx = x0 + cols / 2f, cy = rows / 2f;
        // A mouse at pixel p means the pixel's middle, p + 0.5 (Pick.Ray), so the middle of the view is
        // half a pixel up and left of it.
        // The cursor is drawn in the sheet, wherever the layout puts that on the picture (UiLayout). Where the sheet
        // is 1.1's stretch over the picture (stretched, or the 16:9 stretch on a 16:9 picture), 1.1's own arithmetic.
        var (tx, ty) = UiLayout.Stretched(width, height)
            ? (cx * UiAssets.Width / width, UiAssets.Height - cy * UiAssets.Height / height)
            : UiLayout.FromPicture(cx, cy, width, height);
        return new Point(cx - 0.5f, cy - 0.5f, tx, ty);
    }

    /// <summary>
    /// Where a click is while the lock holds the pointer at <paramref name="at"/>: there, wherever the
    /// system pointer says it is (it is hidden and travelling, the mouse turning the camera); with no lock,
    /// where it came.
    /// </summary>
    public static (float X, float Y) Click(Point? at, float x, float y) => at is { } p ? (p.X, p.Y) : (x, y);

    /// <summary>
    /// Once a frame, with what is open now: whether anything opened since the last frame — which ends the
    /// lock (<see cref="SwivelHold.End"/>). Something already open, or closing, does not.
    /// </summary>
    public bool Opened(OpenPanes now)
    {
        bool opened = (now & ~open) != 0;
        open = now;
        return opened;
    }
}
