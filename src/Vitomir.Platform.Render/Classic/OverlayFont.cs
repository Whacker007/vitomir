// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A 5x7 bitmap font for the diagnostic overlay, drawn for this project rather than taken from
// anywhere. One entry per printable ASCII character from space (32) to tilde (126); bit
// `y * 5 + x` is lit. The art it was generated from is in tools/genfont.py, which is where to
// edit a glyph - not here.
//
// It is deliberately plain and deliberately not the game's own font. The overlay is a developer
// tool and should never be mistaken on screen for the HUD being rebuilt beside it.
// The overlay's font (the module never draws): the module lays its text out by it and the host's
// renderer makes its atlas from it (DeveloperOverlay).
namespace Vitomir.Platform.Render.Classic;

/// <summary>The overlay's font: 5x7 glyphs packed one to a <see cref="ulong"/> (35 bits, so a uint will not do).</summary>
public static class OverlayFont
{
    public const int Width = 5, Height = 7, First = 32, Last = 126;

    /// <summary>Glyph bits for <paramref name="c"/>, or the box drawn for anything unprintable.</summary>
    public static ulong Glyph(char c) =>
        c >= First && c <= Last ? Glyphs[c - First] : Glyphs['?' - First];

    /// <summary>Whether the glyph for <paramref name="c"/> has a pixel at (x, y).</summary>
    public static bool Lit(char c, int x, int y) =>
        (uint)x < Width && (uint)y < Height && (Glyph(c) & (1UL << (y * Width + x))) != 0;

    public static readonly ulong[] Glyphs =
    [
        0x000000000UL, 0x008021084UL, 0x00000014AUL, 0x000AFABEAUL, 0x11F4717C4UL, 0x672222273UL, 0x02C9A9926UL, 0x000000084UL,
        0x010410888UL, 0x004442082UL, 0x02AEFBAA0UL, 0x0084F9080UL, 0x088C00000UL, 0x0000F8000UL, 0x018C00000UL, 0x042222210UL,
        0x01D19D72EUL, 0x01C4210C4UL, 0x03E22222EUL, 0x01D18311FUL, 0x011F4A988UL, 0x01D183C3FUL, 0x01D18BC4CUL, 0x00842221FUL,
        0x01D18BA2EUL, 0x00C8F462EUL, 0x000C00180UL, 0x088C00180UL, 0x000820888UL, 0x001F07C00UL, 0x000222082UL, 0x00802222EUL,
        0x03C16F62EUL, 0x0231FC62EUL, 0x01F18BE2FUL, 0x01D10862EUL, 0x00E98C527UL, 0x03E10BC3FUL, 0x00210BC3FUL, 0x03D1C862EUL,
        0x02318FE31UL, 0x01C42108EUL, 0x01D184210UL, 0x022928D31UL, 0x03E108421UL, 0x02318D771UL, 0x0231CD671UL, 0x01D18C62EUL,
        0x00217C62FUL, 0x02C9AC62EUL, 0x02297C62FUL, 0x01D18383EUL, 0x00842109FUL, 0x01D18C631UL, 0x008A8C631UL, 0x023BAC631UL,
        0x022A22A31UL, 0x008422A31UL, 0x03E22221FUL, 0x01C21084EUL, 0x420820821UL, 0x01C84210EUL, 0x000004544UL, 0x03E000000UL,
        0x000000082UL, 0x033E83800UL, 0x01F18BC21UL, 0x03C10F800UL, 0x03D18FA10UL, 0x01C1FB800UL, 0x004211C4CUL, 0x3A1E8F800UL,
        0x02318BC21UL, 0x01C421804UL, 0x1D0843008UL, 0x01251A421UL, 0x018421086UL, 0x0235AAC00UL, 0x02318BC00UL, 0x01D18B800UL,
        0x042F8BC00UL, 0x421E8F800UL, 0x00211B400UL, 0x01F077800UL, 0x019211C42UL, 0x02D98C400UL, 0x008A8C400UL, 0x0155AC400UL,
        0x02A454400UL, 0x3A1E8C400UL, 0x03E247C00UL, 0x030420898UL, 0x008421084UL, 0x006422083UL, 0x00006C800UL,
    ];
}
