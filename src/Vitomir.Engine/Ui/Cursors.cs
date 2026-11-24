// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The mouse cursor: which of the game's eight it is, and where its point sits inside its artwork.
//
// **This is read, not matched by eye.** The original builds the whole set in one
// constructor, naming eight textures in a fixed order and giving each an anchor mode; the modes are
// spelled out in the cursor's hotspot code, which turns a mode into the pixel offset the draw
// subtracts. `ui/Materials/` ships **nine** `cursor*.tga`, all 32 x 32 32-bit; `cursorUse` is the
// one the constructor never asks for, so the engine cannot ever show it.
//
// **Where the point is.** The draw builds the quad from corners (0,h), (w,h), (w,0), (0,0) with
// texture v running 0 at y=h, and a `.tga` in this engine stores its bottom row first — so quad
// y=0 is the picture's **top**. Mode 1's offset is (0,0), which therefore puts the pointer's point
// at the artwork's **top left**, and the artwork agrees: `cursorDefault`, `cursorAttack` and
// `cursorScroll` all draw a blade whose tip touches the canvas's top-left corner. Mode 0 is
// (w/2, h/2), the centre, and that is the one a carried item gets.
namespace Vitomir.Engine.Ui;

/// <summary>
/// The eight cursors the engine registers, in the order the original stores them —
/// which is also the index it keeps and sets directly.
/// </summary>
public enum CursorId
{
    /// <summary>`cursorDefault` — what nothing under the pointer gets, and what setting cursor 0 restores.</summary>
    Default = 0,
    /// <summary>`cursorAttack` — an actor on another side that is not talkable and not liftable.</summary>
    Attack = 1,
    /// <summary>`cursorPickup` — an item, a weapon, armour, or a body flagged <c>can be picked up</c>.</summary>
    Pickup = 2,
    /// <summary>`cursorTalk` — an actor flagged <c>can be talked to</c> (flag `0x80`).</summary>
    Talk = 3,
    /// <summary>`cursorNegative` — the refusal, shown for a quarter second by the refusal.</summary>
    Negative = 4,
    /// <summary>`cursorSelect`.</summary>
    Select = 5,
    /// <summary>`cursorScroll`.</summary>
    Scroll = 6,
    /// <summary>`cursorTarget`.</summary>
    Target = 7,
}

/// <summary>
/// Which corner of a cursor's artwork the pointer's point is, as the hotspot code's
/// <c>switch</c> sets it. The names are in screen terms: quad y = 0 is the picture's top, because
/// the draw maps texture v = 0 to quad y = h and a `.tga` here is stored bottom row first.
/// </summary>
public enum CursorAnchor
{
    /// <summary>Mode 0 — (w/2, h/2). Every cursor built with a 0, and every carried item icon.</summary>
    Centre = 0,
    /// <summary>Mode 1 — (0, 0).</summary>
    TopLeft = 1,
    /// <summary>Mode 2 — (w, 0). No shipped caller uses it.</summary>
    TopRight = 2,
    /// <summary>Mode 3 — (0, h). No shipped caller uses it.</summary>
    BottomLeft = 3,
    /// <summary>Mode 4 — (w, h). No shipped caller uses it.</summary>
    BottomRight = 4,
}

/// <summary>The cursor table and the arithmetic that places one.</summary>
public static class Cursors
{
    /// <summary>Every shipped `cursor*.tga` is exactly this square, 32-bit, uncompressed.</summary>
    public const int Size = 32;

    /// <summary>
    /// How long the refusal holds <see cref="CursorId.Negative"/> before letting the context take the
    /// cursor back: <c>now + 250</c>, a quarter of a second. It plays sound 5 at the same moment.
    /// </summary>
    public const int RefuseMs = 250;

    /// <summary>The interface sound played with the refusal.</summary>
    public const int RefuseSound = 5;

    /// <summary>
    /// The ninth file. `ui/Materials/cursorUse.tga` ships and is never registered, so nothing in
    /// the shipped game can show it — kept named so a mod that wants it knows it is there.
    /// </summary>
    public const string Unregistered = "cursorUse";

    static readonly (string Name, CursorAnchor Anchor)[] Table =
    [
        ("cursorDefault",  CursorAnchor.TopLeft),
        ("cursorAttack",   CursorAnchor.TopLeft),
        ("cursorPickup",   CursorAnchor.TopLeft),
        ("cursorTalk",     CursorAnchor.Centre),
        ("cursorNegative", CursorAnchor.Centre),
        ("cursorSelect",   CursorAnchor.Centre),
        ("cursorScroll",   CursorAnchor.TopLeft),
        ("cursorTarget",   CursorAnchor.Centre),
    ];

    /// <summary>How many the engine registers — eight, of the nine that ship.</summary>
    public static int Count => Table.Length;

    /// <summary>The texture name, which the engine resolves through the ordinary material path.</summary>
    public static string Name(CursorId id) =>
        (uint)id < Table.Length ? Table[(int)id].Name : Table[0].Name;

    public static CursorAnchor Anchor(CursorId id) =>
        (uint)id < Table.Length ? Table[(int)id].Anchor : Table[0].Anchor;

    /// <summary>
    /// The offset the draw subtracts from the pointer, measured from the artwork's top left, for a
    /// glyph <paramref name="w"/> x <paramref name="h"/>.
    /// </summary>
    public static (float X, float Y) Hotspot(CursorAnchor anchor, float w, float h) => anchor switch
    {
        CursorAnchor.Centre => (w / 2f, h / 2f),
        CursorAnchor.TopLeft => (0f, 0f),
        CursorAnchor.TopRight => (w, 0f),
        CursorAnchor.BottomLeft => (0f, h),
        CursorAnchor.BottomRight => (w, h),
        _ => (0f, 0f),
    };

    /// <summary>
    /// Where the glyph's quad goes in `.nui` space — **bottom left**, because that is the corner
    /// this project's UI draw takes and `.nui` y counts up. The pointer is at
    /// (<paramref name="x"/>, <paramref name="y"/>) in that same space.
    /// </summary>
    public static (float X, float Y) Corner(CursorAnchor anchor, float x, float y, float w, float h)
    {
        var (hx, hy) = Hotspot(anchor, w, h);
        return (x - hx, y - (h - hy));
    }
}
