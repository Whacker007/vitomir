// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Formats.Tests;

/// <summary>The engine's text rules for UI panes, pinned with small inputs (no game data needed).</summary>
public class NuiParserTests
{
    [Fact]
    public void Sscanf_style_integers_stop_at_the_first_bad_field()
    {
        Assert.Equal([53, 15], NuiPane.Ints("53,15,"));
        Assert.Equal([228, 323], NuiPane.Ints("228,323"));
        Assert.Equal([-4, 7], NuiPane.Ints("-4,7,x,9"));
        Assert.Empty(NuiPane.Ints("abc"));
    }

    [Theory]
    [InlineData("0x10", 16)]
    [InlineData("010", 8)]
    [InlineData("10", 10)]
    [InlineData("-0x1f", -31)]
    [InlineData("junk", 0)]
    [InlineData("0", 0)]
    public void Percent_i_integers(string text, int expected) => Assert.Equal(expected, NuiPane.CInt(text));

    [Fact]
    public void Key_value_splits_like_the_engine()
    {
        Assert.Equal(("pos", "1,2"), NuiPane.SplitKv("pos=1,2"));
        Assert.Equal(("a", "b"), NuiPane.SplitKv("a=b=c"));
        Assert.Equal(("dialog", ""), NuiPane.SplitKv("dialog"));
    }

    [Fact]
    public void Lines_are_lower_cased_comments_only_in_column_zero_and_pane_keys_use_the_first_token()
    {
        var pane = NuiPane.Parse(
            "; a comment\n" +
            "name=Network Load screen\n" +
            "POS=10,20\n" +
            "  ; not a comment: indented, so the first token is ';'\n" +
            "quad pos=0,0 size=64,32 mat=Foo.TGA\n" +
            "element button name=OK pos=1,2 size=3,4 flags=0x10\n" +
            "element bogus name=x\n");
        Assert.Equal("network", pane.Name);
        Assert.Equal([10, 20], pane.Pos);
        Assert.Equal("foo.tga", Assert.Single(pane.Quads).Material);
        var e = Assert.Single(pane.Elements);
        Assert.Equal(("button", "ok", 16), (e.Type, e.Name, e.Flags));
        Assert.Equal(2, pane.Warnings.Count);    // the indented ';' line and the unknown control type
    }
}
