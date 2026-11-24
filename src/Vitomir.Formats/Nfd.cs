// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// NFD — the UI's bitmap fonts (`ui/*.nfd`). Six of them ship: `DefaultFont`, `defaultFont10/15/20`
// and `morpheus15/20`, and the `.nui` layouts name them by file.
//
// The SDK does not document this format. The game's loader allocates 0x1834 bytes, `fread`s the whole header block in one call, then reads
// `stripHeight * stripWidth` bytes of coverage after it — which fixes every offset below without
// guessing. How the letters are drawn names the glyph's six fields, and it is worth knowing:
// an earlier pass here took the table to start eight bytes later than it does, which shifted
// every field by one and made two of them look like nonsense, because they were the *next*
// character's.
namespace Vitomir.Formats;

/// <summary>One character's box in the font's strip.</summary>
/// <param name="XOffset">Added to the pen's x before drawing (`glyph[0] + penX`).</param>
/// <param name="YOffset">Added to the pen's y. Negative on descenders — `p` is −3 in the 15px font.</param>
/// <param name="Advance">How far the pen moves afterwards, which is not the same as the width.</param>
/// <param name="PixelX">The glyph's column in the strip, or −1 when the font has no glyph for this character.</param>
public readonly record struct NfdGlyph(int XOffset, int YOffset, int Width, int Height, int Advance, int PixelX)
{
    /// <summary>Whether the font draws anything for this character. Space has a width of 0 and is still present.</summary>
    public bool Present => PixelX >= 0;
}

/// <summary>A UI font: 256 glyph boxes and one strip of 8-bit coverage they all point into.</summary>
public sealed class NfdFont
{
    /// <summary>The line height the font is named for — 10, 15 or 20.</summary>
    public int LineHeight { get; init; }
    /// <summary>The strip's width, which is the sum of every glyph's width.</summary>
    public int StripWidth { get; init; }
    /// <summary>The strip's height, which is the tallest glyph — not the line height.</summary>
    public int StripHeight { get; init; }
    /// <summary>One box per byte value, 0–255. Characters below 32 are absent in every shipped font.</summary>
    public NfdGlyph[] Glyphs { get; init; } = [];
    /// <summary>
    /// The strip, one byte per pixel, **bottom row first** — the same way round as every other
    /// texture in this engine. The shipped fonts use five levels:
    /// 0, 64, 128, 192, 255. The original treats the byte as an index into a 256-entry ramp it
    /// builds from the requested colour, and skips any pixel whose ramp entry is 0.
    /// </summary>
    public byte[] Coverage { get; init; } = [];

    public NfdGlyph this[char c] => Glyphs[(byte)c];

    /// <summary>Coverage at (x, y) in the strip, with y measured from the strip's bottom.</summary>
    public byte At(int x, int y) =>
        (uint)x < (uint)StripWidth && (uint)y < (uint)StripHeight ? Coverage[y * StripWidth + x] : (byte)0;

    /// <summary>How wide <paramref name="text"/> draws, in the font's own pixels.</summary>
    public int Measure(ReadOnlySpan<char> text)
    {
        int w = 0;
        foreach (char c in text) w += Glyphs[(byte)c].Advance;
        return w;
    }

    /// <summary>
    /// What one byte of a glyph's coverage becomes where the text is drawn. 1.1 writes text into a
    /// surface through a 256-entry ramp made once per colour (one for a 16-bit surface,
    /// one for a 32-bit one): entry <c>k</c> is each channel of the colour times <c>k</c> over
    /// 256, <b>with the alpha forced on</b>, and a pixel is written only where the coverage is not 0.
    /// So a glyph's soft edge (the fonts' coverage is 0, 64, 128, 192 or 255) is the colour darkened
    /// and opaque — not blended into what is behind it: dark text on the parchment has hard edges,
    /// light text on a dark pane a dark rim. As a texture that the text's colour multiplies: grey
    /// is the coverage, alpha is all or nothing.
    /// </summary>
    public static (byte Grey, byte Alpha) Texel(byte coverage) => (coverage, coverage == 0 ? (byte)0 : (byte)255);

    /// <summary>
    /// A character the blitters pass over without drawing it or moving the pen (byte 0xff)
    /// — though the width (<see cref="Measure"/>) still counts its advance.
    /// </summary>
    public static bool Skipped(char c) => (byte)c == 0xff;

    /// <summary>The fixed header block the original reads in one `fread`.</summary>
    public const int HeaderBytes = 0x1834, GlyphCount = 256, GlyphBytes = 24;

    public static NfdFont Read(byte[] data, string name = "<font>")
    {
        var r = new BinReader(data, name);
        int lineHeight = r.Count32(1024);
        int stripWidth = r.Count32(1 << 20);

        var glyphs = new NfdGlyph[GlyphCount];
        for (int i = 0; i < GlyphCount; i++)
            glyphs[i] = new NfdGlyph(r.I32(), r.I32(), r.I32(), r.I32(), r.I32(), r.I32());

        r.Skip(4);                    // the pixel pointer, which the original overwrites on load
        r.Skip(0x20);                 // the font's own file name, written by the caller after load
        r.Skip(4);
        int stripHeight = r.Count32(1024);
        if (r.Pos != HeaderBytes) throw r.Error($"header is {r.Pos} bytes, expected {HeaderBytes}");

        var coverage = r.Bytes(stripWidth * stripHeight);
        r.ExpectEof();

        // The strip is packed edge to edge, so the widths have to add up to it exactly.
        int sum = 0;
        foreach (var g in glyphs) if (g.Present) sum += g.Width;
        if (sum != stripWidth) throw new NodFormatException($"{name}: glyph widths total {sum}, strip is {stripWidth}");

        return new NfdFont
        {
            LineHeight = lineHeight, StripWidth = stripWidth, StripHeight = stripHeight,
            Glyphs = glyphs, Coverage = coverage,
        };
    }
}
