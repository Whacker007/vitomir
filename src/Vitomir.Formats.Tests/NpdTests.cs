// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Formats.Tests;

/// <summary>
/// `misc/game.npd` — every flame, candle, smoke plume, blood spurt and spell glow in the game —
/// against the shipped file. The reader keeps every line of every block, because what a key means
/// is up to the kind's own parser in the engine (a gas block's `maxparticles` is read and ignored);
/// so what is pinned here is that nothing is lost on the way in: all 218 blocks, all 2,551 lines,
/// every key one the loader's table knows, and a few values read the way the C library reads them.
/// </summary>
public class NpdTests
{
    static NpdFile Read()
    {
        using var nob = GameData.Open("Resource.nob");
        Assert.SkipUnless(nob.TryRead("misc/game.npd", out var data), "misc/game.npd not in Resource.nob");
        return NpdFile.Read(data, "misc/game.npd");
    }

    [Fact]
    public void Every_block_of_the_shipped_file_reads_and_none_is_skipped()
    {
        var npd = Read();
        Assert.Empty(npd.Skipped);
        Assert.Equal(218, npd.Emitters.Count);
        Assert.Equal(2551, npd.Emitters.Sum(e => e.Lines.Count));
        Assert.All(npd.Emitters, e => Assert.All(e.Lines, l => Assert.NotNull(l.Value)));
        var byKind = npd.Emitters.GroupBy(e => e.Kind).ToDictionary(g => g.Key, g => g.Count());
        var expected = new Dictionary<EmitterKind, int>
        {
            [EmitterKind.Pad] = 58, [EmitterKind.Gas] = 38, [EmitterKind.Fire] = 26, [EmitterKind.Beam] = 21,
            [EmitterKind.Ring] = 17, [EmitterKind.Spurt] = 12, [EmitterKind.Magic] = 11, [EmitterKind.Shock] = 8,
            [EmitterKind.Flame] = 6, [EmitterKind.Trail] = 6, [EmitterKind.Rain] = 5, [EmitterKind.Sparks] = 5,
            [EmitterKind.Liquid] = 3, [EmitterKind.Object] = 2,
        };
        Assert.Equal(expected.OrderBy(p => p.Key), byKind.OrderBy(p => p.Key));
    }

    /// <summary>
    /// The torch every Prague street is lit by (`torchFlame` names it) and the candle: two fire
    /// blocks, read line for line. The header's "fire " with a trailing blank is still a fire.
    /// </summary>
    [Fact]
    public void The_torch_and_the_candle_read_as_written()
    {
        var npd = Read();
        var torch = npd["FLAMETEST"];              // looked up without regard to case, as the game does
        Assert.NotNull(torch);
        Assert.Equal(EmitterKind.Fire, torch.Kind);
        Assert.Equal("fireSprite", torch[NpdKey.Material]);
        Assert.Equal(35, NpdFile.Int(torch[NpdKey.Lifespan]));
        Assert.Equal(15, NpdFile.Int(torch[NpdKey.BirthRate]));
        Assert.Equal(500, NpdFile.Int(torch[NpdKey.MaxParticles]));
        Assert.Equal(-200f, NpdFile.Float(torch[NpdKey.Gravity]));
        Assert.True(NpdFile.Hex(torch[NpdKey.StartColor], out var start));
        Assert.Equal(0xc0ff961eu, start);

        var candle = npd["candleFlame"];
        Assert.NotNull(candle);
        Assert.Equal(["material", "lifespan", "birthrate", "startsize", "mindist", "maxdist", "startcolor",
                      "endcolor", "minspeed", "maxspeed", "turbulence"],
                     candle.Lines.Select(l => NpdFile.KeyNames[(int)l.Key]));
        Assert.Equal(0.5f, NpdFile.Float(candle[NpdKey.MaxDist]));
    }

    /// <summary>A flame's velocity is a parenthesised triple, and the arrow's trail is a trail.</summary>
    [Fact]
    public void Vectors_and_the_arrow_trail_read()
    {
        var npd = Read();
        var flame = npd["flame"]!;
        Assert.Equal(EmitterKind.Flame, flame.Kind);
        float x = 9, y = 9, z = 9;
        Assert.Equal(3, NpdFile.Vector(flame[NpdKey.Velocity], ref x, ref y, ref z));
        Assert.Equal((0f, 400f, -10f), (x, y, z));
        Assert.Equal(EmitterKind.Trail, npd["arrowTrail"]!.Kind);
        Assert.Equal(EmitterKind.Rain, npd["rain"]!.Kind);
    }

    /// <summary>The C library's leniency, which the shipped file leans on in places.</summary>
    [Fact]
    public void The_conversions_are_the_c_librarys()
    {
        Assert.Equal(0, NpdFile.Int("0x20"));
        Assert.Equal(2, NpdFile.Int("2.5"));
        Assert.Equal(-200, NpdFile.Int("-200"));
        Assert.Equal(12.5f, NpdFile.Float("12.5abc"));
        Assert.True(NpdFile.Hex("ff", out var h));
        Assert.Equal(0xffu, h);
        Assert.False(NpdFile.Hex("zz", out _));
        float x = 1, y = 2, z = 3;
        Assert.Equal(1, NpdFile.Vector("(5,oops,7)", ref x, ref y, ref z));
        Assert.Equal((5f, 2f, 3f), (x, y, z));
    }

    /// <summary>
    /// The loader's own rules on a made-up file: a comment is a ';' in column 0, a header needs a
    /// name, a repeated name is passed over with its block, and a kind is matched by prefix.
    /// </summary>
    [Fact]
    public void A_repeated_name_is_skipped_and_a_kind_matches_by_prefix()
    {
        var text = "; c\ngasses first\nlifespan 10\nend\ngas first\nlifespan 20\nend\nfire\nnone x\nend\n";
        var npd = NpdFile.Read(System.Text.Encoding.Latin1.GetBytes(text));
        Assert.Single(npd.Emitters);
        Assert.Equal(EmitterKind.Gas, npd.Emitters[0].Kind);
        Assert.Equal("10", npd["first"]![NpdKey.Lifespan]);
        Assert.Single(npd.Skipped);
    }
}
