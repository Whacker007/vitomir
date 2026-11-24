// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text;

namespace Vitomir.Formats.Tests;

/// <summary>
/// The motion-set reader, against a file written the way the shipped ones are: a leading `;`
/// comment block, tab-aligned columns, flags run together after the animation.
/// </summary>
public class NmsTests
{
    static NmsMotionSet Parse(string text) =>
        NmsMotionSet.Read(Encoding.Latin1.GetBytes(text.ReplaceLineEndings("\r\n")), "Test.nms");

    const string Sample = """
        ; Test.nms
        ;
        ; A motion set written the way the shipped ones are.

        stand			test_stand.nad		lowpriority
        walk			test_run.nad		movespeed
        deathslow		test_death.nad		noloop endpause
        listen			test_stand
        punch			test_attack.nad		noloop		; the bite, actually

        attack			test_attack.nad		noloop

        ; tags section
        tag		LFoot		6
        tag		Weapon		24	1.5 -2 0.25
        tag		Nose		9

        mouth a 22 (0.0,0.0,0.0)
        mouth c 22 (-7.0,0.0,0.0)
        mouth z 22 (0.0,0.0,0.0)
        """;

    [Fact]
    public void It_reads_the_modes_in_file_order()
    {
        var set = Parse(Sample);
        Assert.Equal(["stand", "walk", "deathslow", "listen", "punch"], set.Modes.Select(m => m.Name));
        Assert.Equal("test_stand.nad", set.Mode("stand")!.Animation);
        Assert.Equal("Test", set.Name);
    }

    [Fact]
    public void A_mode_name_the_engine_does_not_know_drops_the_line()
    {
        // "attack" is not one of the engine's 257 mode names, so the line is not a mode at all.
        var set = Parse(Sample);
        Assert.Null(set.Mode("attack"));
        Assert.Contains(set.Dropped, d => d.StartsWith("attack"));
    }

    [Fact]
    public void Modes_carry_the_engine_s_own_numbering()
    {
        var set = Parse(Sample);
        Assert.Equal(33, set.Mode("stand")!.Index);          // the 33rd name in the engine's table
        Assert.Equal(1, set.Mode("walk")!.Index);
        Assert.All(set.Modes, m => Assert.Equal(NmsModeKind.Body, m.Kind));
        Assert.False(set.IsHead);
    }

    [Fact]
    public void Head_modes_are_numbered_in_their_own_table()
    {
        var set = Parse("head_idle\twolfHEAD_idle.nad\nhead_bite\twolfHEAD_bite.nad\n");
        Assert.Equal([1, 8], set.Modes.Select(m => m.Index));
        Assert.All(set.Modes, m => Assert.Equal(NmsModeKind.Head, m.Kind));
        Assert.True(set.IsHead);
    }

    [Fact]
    public void Flags_are_the_engine_s_bits_and_unknown_words_are_ignored()
    {
        var set = Parse(Sample);
        Assert.Equal(NmsFlags.LowPriority, set.Mode("stand")!.Flags);
        Assert.Equal(NmsFlags.MoveSpeed, set.Mode("walk")!.Flags);
        Assert.Equal(NmsFlags.NoLoop | NmsFlags.EndPause, set.Mode("deathslow")!.Flags);
        Assert.Equal(NmsFlags.None, set.Mode("listen")!.Flags);
        // The trailing "; the bite, actually" is words the engine simply does not recognise.
        Assert.Equal(NmsFlags.NoLoop, set.Mode("punch")!.Flags);
        Assert.Equal((uint)0x8, (uint)NmsFlags.LowPriority);
        Assert.Equal((uint)0x2000, (uint)NmsFlags.Fidget);
    }

    [Fact]
    public void A_mode_line_may_leave_the_extension_off()
    {
        var set = Parse(Sample);
        Assert.Equal("test_stand", set.Mode("listen")!.Animation);
        Assert.Equal("test_stand.nad", set.Mode("listen")!.File);
        Assert.Equal("test_stand.nad", set.Mode("stand")!.File);
    }

    [Fact]
    public void Tags_take_a_name_from_the_engine_s_table_and_an_optional_offset()
    {
        var set = Parse(Sample);
        Assert.Equal(["LFoot", "Weapon"], set.Tags.Select(t => t.Name));
        Assert.Equal(6, set.Tags[0].Bone);
        Assert.Equal(Vector3.Zero, set.Tags[0].Offset);
        Assert.Equal(new Vector3(1.5f, -2f, 0.25f), set.Tags[1].Offset);
        Assert.Equal(26, set.Tags[1].Index);
        // "Nose" is not one of the 43 attachment points.
        Assert.Contains(set.Dropped, d => d.Contains("Nose"));
    }

    [Fact]
    public void Mouths_are_the_nine_letters_a_to_i()
    {
        var set = Parse(Sample);
        Assert.Equal(['a', 'c'], set.Mouths.Select(m => m.Letter));
        Assert.Equal(22, set.Mouths[0].Bone);
        Assert.Equal(new Vector3(-7f, 0f, 0f), set.Mouths[1].Angles);
        Assert.Contains(set.Dropped, d => d.StartsWith("mouth z"));
    }

    [Fact]
    public void A_mouth_line_that_is_short_of_a_number_is_dropped()
    {
        // cosmas.nms really does have "(1.50.0,0.0)", which the engine's scan reads as two values.
        var set = Parse("mouth h 20 (1.50.0,0.0)\n");
        Assert.Empty(set.Mouths);
        Assert.Single(set.Dropped);
    }

    [Fact]
    public void A_number_stops_where_it_stops_being_a_number()
    {
        // Azhra.nms has "(-2.2.0,0.0,0.0)"; the engine's scan keeps -2.2 and moves on.
        var set = Parse("mouth b 22 (-2.2.0,0.0,0.0)\n");
        Assert.Equal(new Vector3(-2.2f, 0f, 0f), Assert.Single(set.Mouths).Angles);
    }

    [Fact]
    public void Only_a_semicolon_in_the_first_column_comments_a_line_out()
    {
        var set = Parse("; stand\ttest.nad\n  ; walk\ttest.nad\nlisten\ttest.nad\n");
        Assert.Equal(["listen"], set.Modes.Select(m => m.Name));
        // The indented one is not a comment to the engine — it is a line starting with ";", which
        // is not a mode name, so it is dropped just the same.
        Assert.Single(set.Dropped);
    }
}
