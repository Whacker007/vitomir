// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Formats.Tests;

/// <summary>
/// The six shipped UI fonts. The format is undocumented, so the checks here are the ones that would
/// have caught the mistake the first pass made — a table origin eight bytes out, which shifts every
/// field by one and still parses.
/// </summary>
public class NfdTests
{
    static readonly string[] Names =
        ["DefaultFont.nfd", "defaultFont10.nfd", "defaultFont15.nfd", "defaultFont20.nfd",
         "morpheus15.nfd", "morpheus20.nfd"];

    static NfdFont Read(NobArchive nob, string name) =>
        NfdFont.Read(nob.Read("ui/" + name), name);

    [Fact]
    public void Every_shipped_font_parses_to_exact_eof()
    {
        using var nob = GameData.Open("Local_Eng.nob");
        foreach (var name in Names)
        {
            var font = Read(nob, name);
            Assert.InRange(font.LineHeight, 8, 24);
            Assert.InRange(font.StripHeight, font.LineHeight - 1, font.LineHeight + 4);
            Assert.Equal(font.StripWidth * font.StripHeight, font.Coverage.Length);
            Assert.Equal(NfdFont.GlyphCount, font.Glyphs.Length);
        }
    }

    /// <summary>The line height is in the file and in the file's own name, and they agree.</summary>
    [Fact]
    public void The_line_height_matches_the_name()
    {
        using var nob = GameData.Open("Local_Eng.nob");
        foreach (var (name, height) in new[]
                 { ("defaultFont10.nfd", 10), ("defaultFont15.nfd", 15), ("defaultFont20.nfd", 20),
                   ("morpheus15.nfd", 15), ("morpheus20.nfd", 20), ("DefaultFont.nfd", 10) })
            Assert.Equal(height, Read(nob, name).LineHeight);
    }

    /// <summary>
    /// The columns are packed edge to edge and every glyph's ink stays inside its own box. Both
    /// fail immediately if the record's fields are off by one.
    /// </summary>
    [Fact]
    public void Glyphs_are_packed_edge_to_edge_with_their_ink_inside_their_boxes()
    {
        using var nob = GameData.Open("Local_Eng.nob");
        foreach (var name in Names)
        {
            var font = Read(nob, name);
            int x = 0;
            foreach (var g in font.Glyphs)
            {
                if (!g.Present) continue;
                Assert.Equal(x, g.PixelX);
                x += g.Width;
                if (g.Width == 0) continue;
                for (int y = g.Height; y < font.StripHeight; y++)
                    for (int i = 0; i < g.Width; i++)
                        Assert.True(font.At(g.PixelX + i, y) == 0,
                            $"{name}: glyph at {g.PixelX} has ink at row {y}, above its height {g.Height}");
            }
            Assert.Equal(font.StripWidth, x);
        }
    }

    /// <summary>
    /// Typography, checked as arithmetic. In the three sans faces everything that sits on the
    /// baseline shares one y offset and everything that hangs below it has a lower one — the
    /// property a wrong field alignment destroys, because it reads the next character's offset.
    /// The two Morpheus faces are the game's gothic display font and their baseline is deliberately
    /// uneven (`n`, `m` and `k` sit lower), so they only have to keep descenders below the rest.
    /// </summary>
    [Fact]
    public void Baseline_sitters_share_a_y_offset_and_descenders_drop_below_it()
    {
        const string OnBaseline = "HOoxnsezium0123456789AZbdhklt", Descenders = "gjpqy,;";
        using var nob = GameData.Open("Local_Eng.nob");
        foreach (var name in Names)
        {
            var font = Read(nob, name);
            bool gothic = name.StartsWith("morpheus", StringComparison.OrdinalIgnoreCase);
            var offsets = OnBaseline.Select(c => font[c].YOffset).ToArray();

            if (!gothic)
                Assert.True(offsets.Distinct().Count() == 1,
                    $"{name}: baseline characters disagree — {string.Join(",", offsets.Distinct())}");

            int baseline = offsets.GroupBy(o => o).OrderByDescending(g => g.Count()).First().Key;
            Assert.Equal(0, baseline);

            if (gothic)
                // Blackletter: its `g` sits on the baseline, so only require that some tail drops.
                Assert.Contains(Descenders, c => font[c].YOffset < baseline);
            else
                foreach (char c in Descenders)
                    Assert.True(font[c].YOffset < baseline,
                        $"{name}: '{c}' has y offset {font[c].YOffset}, not below the baseline's {baseline}");
        }
    }

    /// <summary>Coverage is a five-level ramp, and the advance is never smaller than the ink.</summary>
    [Fact]
    public void Coverage_is_a_five_level_ramp()
    {
        using var nob = GameData.Open("Local_Eng.nob");
        foreach (var name in Names)
        {
            var font = Read(nob, name);
            Assert.Equal([0, 64, 128, 192, 255], font.Coverage.Distinct().Order().Select(b => (int)b));
            foreach (char c in " !ABCabc0123")
                Assert.True(font[c].Advance >= font[c].Width + font[c].XOffset - 1, $"{name}: '{c}' advance");
        }
    }

    /// <summary>A line of text measures to the sum of its advances, and no glyph is missing from it.</summary>
    [Fact]
    public void Printable_ascii_is_complete_in_every_font()
    {
        using var nob = GameData.Open("Local_Eng.nob");
        foreach (var name in Names)
        {
            var font = Read(nob, name);
            for (char c = ' '; c <= '~'; c++)
                Assert.True(font[c].Present, $"{name}: no glyph for '{c}'");
            for (char c = (char)0; c < ' '; c++)
                Assert.False(font[c].Present, $"{name}: unexpected glyph for control {(int)c}");
            Assert.Equal("Hi".Sum(c => font[c].Advance), font.Measure("Hi"));
        }
    }
}
