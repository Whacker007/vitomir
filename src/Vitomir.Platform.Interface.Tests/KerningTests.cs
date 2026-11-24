// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text;

namespace Vitomir.Platform.Interface.Tests;

/// <summary>
/// Pair kerning in the interface's lettering: a face whose atlas carries pairs (<c>kern</c>, from the font's GPOS <c>kern</c>
/// feature) moves the second glyph of a pair by the pair's value at the size the line is laid out at, in its measure and
/// where its glyphs are placed alike; a face without pairs (our own code-drawn faces) places every glyph exactly where
/// it was placed before, a glyph's advance after another.
/// </summary>
public sealed class KerningTests
{
    /// <summary>A small face: A (advance 0.7 em), V (0.68 em), o (0.55 em) and a space; A then V closer by 0.048 em.</summary>
    static Face Small(bool withPairs)
    {
        const string glyph = "\"box\":[0,-0.1,0.7,0.8],\"ink\":[0.02,0,0.68,0.7],\"atlas\":[0,0,40,50]";
        string json = "{\"format\":1,\"family\":\"Test\",\"em\":64,\"spread\":16,\"atlasWidth\":256,\"atlasHeight\":256,"
                      + "\"ascender\":0.9,\"descender\":-0.25,\"glyphs\":{\"32\":{\"advance\":0.25},"
                      + "\"65\":{\"advance\":0.7," + glyph + "},\"86\":{\"advance\":0.68," + glyph + "},\"111\":{\"advance\":0.55," + glyph + "}}"
                      + (withPairs ? ",\"kern\":[[65,86,-0.048],[86,111,-0.052]]" : "") + "}";
        using var s = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return Face.Read(FaceId.Book, s, "test");
    }

    [Fact]
    public void A_pair_moves_the_second_glyph_by_the_fonts_value_at_the_laid_out_size()
    {
        var face = Small(withPairs: true);
        Assert.True(face.HasPairs);
        Assert.Equal(-0.048f, face.Kern('A', 'V'));
        Assert.Equal(0f, face.Kern('V', 'A'));                       // a pair is one way round
        const float size = 30f * 0.78f;                              // the launcher's body em at 1280x800, in pixels
        var placed = new List<PlacedGlyph>();
        face.Place("AVo", 100f, size, placed);
        Assert.Equal(3, placed.Count);
        Assert.Equal(100f, placed[0].X);
        Assert.Equal(100f + (0.7f - 0.048f) * size, placed[1].X, 4);
        Assert.Equal(100f + (0.7f - 0.048f + 0.68f - 0.052f) * size, placed[2].X, 4);
        // The measure agrees with where the glyphs went, and a space breaks a pair.
        Assert.Equal(0.7f - 0.048f + 0.68f - 0.052f + 0.55f, face.Measure("AVo"), 5);
        Assert.Equal(0.7f + 0.25f + 0.68f, face.Measure("A V"), 5);
    }

    [Fact]
    public void A_face_without_pairs_places_every_glyph_as_before()
    {
        var plain = Small(withPairs: false);
        Assert.False(plain.HasPairs);
        Assert.Equal(0f, plain.Kern('A', 'V'));
        Assert.Equal(0.7f + 0.68f + 0.55f, plain.Measure("AVo"));
        // Our own faces: every glyph at the pen's x moved by the advances alone, the same floats as the earlier loop.
        const string sample = "Vitomir plays from your own copy of the game: AVAWAY To Ta. Lifts Été, 1.1's «42»!";
        foreach (var id in new[] { FaceId.Book, FaceId.Uncial, FaceId.Capitals })
        {
            var face = Face.Get(id);
            Assert.False(face.HasPairs);
            foreach (float size in new[] { 19.5f, 23.4f, 35.1f })
            {
                var placed = new List<PlacedGlyph>();
                face.Place(sample, 12.25f, size, placed);
                float x = 12.25f;
                int i = 0;
                foreach (var r in sample.EnumerateRunes())
                {
                    if (!face.TryGlyph(r.Value, out var g)) continue;
                    Assert.Equal(g, placed[i].Glyph);
                    Assert.Equal(x, placed[i].X);                    // exactly: no kerning arithmetic at all
                    x += g.Advance * size;
                    i++;
                }
                Assert.Equal(i, placed.Count);
            }
        }
    }

    [Fact]
    public void A_line_with_pairs_is_measured_and_wrapped_with_them()
    {
        var face = Small(withPairs: true);
        float measured = face.Measure("AVAV");
        Assert.Equal(0.7f * 2 + 0.68f * 2 - 0.048f * 2, measured, 5);
        Assert.True(measured < Small(withPairs: false).Measure("AVAV"));
    }

    [Theory]
    [InlineData(FaceId.Text)]
    [InlineData(FaceId.TextSemiBold)]
    [InlineData(FaceId.WedgeSemiBold)]
    [InlineData(FaceId.Knife)]
    public void The_delivered_faces_atlases_carry_their_fonts_pairs(FaceId id)
    {
        var face = Face.Get(id);
        Assert.True(face.HasPairs);
        // The fonts' own values (their GPOS kern feature, as HarfBuzz sets them): A V, T o, V o, L T, in thousandths of an em.
        Assert.Equal(-0.048f, face.Kern('A', 'V'), 5);
        Assert.Equal(-0.062f, face.Kern('T', 'o'), 5);
        Assert.Equal(-0.052f, face.Kern('V', 'o'), 5);
        Assert.Equal(-0.062f, face.Kern('L', 'T'), 5);
        Assert.Equal(0f, face.Kern('a', 'b'));
        // Laid out at the launcher's body size at 1280x800 (an em of 30 units, 0.78 pixels to the unit): V of "AV" sits the
        // pair's value nearer than A's advance.
        const float size = 30f * 0.78f;
        var placed = new List<PlacedGlyph>();
        face.Place("AV", 0f, size, placed);
        Assert.True(face.TryGlyph('A', out var a));
        Assert.Equal((a.Advance - 0.048f) * size, placed[1].X, 3);
        Assert.InRange(face.CapMiddle, 0.25f, 0.45f);
        // A line set in it is one run of that face.
        var runs = new List<FaceRun>();
        Letters.Runs(id switch { FaceId.Text => Lettering.Text, FaceId.TextSemiBold => Lettering.TextSemiBold, FaceId.WedgeSemiBold => Lettering.Wedge, _ => Lettering.Knife },
                     "Vitomir Platform 1.0", runs);
        Assert.Equal(new FaceRun("Vitomir Platform 1.0", id, 1f), Assert.Single(runs));
    }
}
