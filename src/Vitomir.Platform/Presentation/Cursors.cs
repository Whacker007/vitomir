// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Engine/Ui/Cursors.cs at 402bef2 (the eight cursors in 1.1's order, their
// files and anchors, their hotspots), reworked for the platform: the choice by verb is ours.
using System.Numerics;
using Vitomir.Platform.Cameras;

namespace Vitomir.Platform.Presentation;

/// <summary>1.1's eight cursors, in the order 1.1 registers them.</summary>
public enum CursorId : byte
{
    Default = 0,
    Attack = 1,
    Pickup = 2,
    Talk = 3,
    Negative = 4,
    Select = 5,
    Scroll = 6,
    Target = 7,
}

/// <summary>Which point of a cursor's art the pointer is (1.1's anchor modes 1 and 0; the rest no cursor uses).</summary>
public enum CursorAnchor : byte
{
    Centre = 0,
    TopLeft = 1,
}

/// <summary>
/// 1.1's cursors: each one's art in 1.1's archives (<c>ui/Materials/cursor*.tga</c>, every one 32 × 32, 32-bit;
/// read from the player's install at run time, never shipped), its anchor — the blade's tip at the top left for the
/// default, attack, pick-up and scroll cursors, the centre for the rest — and which the enhanced game's verbs show:
/// <c>cursorDefault</c> for none, <c>cursorTalk</c> for talk, <c>cursorTarget</c> for a target, <c>cursorNegative</c>
/// for a refusal.
/// </summary>
public static class Cursors
{
    /// <summary>Every cursor's art is this square (pixels, at 1.1's 640 × 480).</summary>
    public const int Size = 32;

    /// <summary>
    /// How large the platform draws a cursor (ours): 64 interface units square — the art's 32 pixels doubled on the
    /// interface's 1440 lines at the player's scale 100 %, so it stays sharp at 2560 × 1440 (1.1 itself drew it at 1/15
    /// of the screen's height, 96 of these units).
    /// </summary>
    public const int Units = 64;

    /// <summary>The folder in 1.1's archives the art is in.</summary>
    public const string Folder = "ui/Materials/";

    static readonly (string Name, CursorAnchor Anchor)[] Table =
    [
        ("cursorDefault", CursorAnchor.TopLeft),
        ("cursorAttack", CursorAnchor.TopLeft),
        ("cursorPickup", CursorAnchor.TopLeft),
        ("cursorTalk", CursorAnchor.Centre),
        ("cursorNegative", CursorAnchor.Centre),
        ("cursorSelect", CursorAnchor.Centre),
        ("cursorScroll", CursorAnchor.TopLeft),
        ("cursorTarget", CursorAnchor.Centre),
    ];

    public static int Count => Table.Length;

    /// <summary>The art's name (<c>cursorTalk</c>).</summary>
    public static string Name(CursorId id) => (uint)id < Table.Length ? Table[(int)id].Name : Table[0].Name;

    /// <summary>The art's path in 1.1's archives.</summary>
    public static string Path(CursorId id) => Folder + Name(id) + ".tga";

    public static CursorAnchor Anchor(CursorId id) => (uint)id < Table.Length ? Table[(int)id].Anchor : CursorAnchor.TopLeft;

    /// <summary>Where the pointer is in a cursor drawn <paramref name="size"/> pixels square, from its top left.</summary>
    public static Vector2 Hotspot(CursorId id, float size) => Anchor(id) == CursorAnchor.Centre ? new Vector2(size / 2f) : Vector2.Zero;

    /// <summary>The cursor a verb shows.</summary>
    public static CursorId For(PointerVerb verb) => verb switch
    {
        PointerVerb.Talk => CursorId.Talk,
        PointerVerb.Target => CursorId.Target,
        PointerVerb.Refused => CursorId.Negative,
        _ => CursorId.Default,
    };
}

/// <summary>
/// The freed pointer as the picture shows it: its place on the picture (0 to 1 across and down), which of
/// 1.1's cursors, the name of the thing under it (or null) and whether no verb applies there (the name is dimmed).
/// </summary>
public readonly record struct PointerCursor(Vector2 Place, CursorId Cursor, string? Name, bool Dimmed);
