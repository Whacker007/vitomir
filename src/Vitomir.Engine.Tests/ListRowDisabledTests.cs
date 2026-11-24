// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A list row out of the choosing** (read in a comparison with 1.1's helpers): the ui library's list
/// keeps a flag on each row (bit 0 of its flags) that the disable sets and the enable clears; a row with it
/// is never hovered, so a press never takes it, and choosing it by its index
/// does nothing. The Storyteller's scene pane greys and disables the steps already reached,
/// and the control pane the host's own row of the cast list — earlier
/// Vitomir greyed the steps but let them be chosen.
/// </summary>
public class ListRowDisabledTests
{
    const string Nui = """
        name=stScenePane
        pos=0,0
        element textlistbox name=stspListBox pos=42,182 size=193,243 flags=0x02 font=defaultFont10
        """;

    static MenuList List(int rows)
    {
        var glyphs = new NfdGlyph[256];
        for (int i = 0; i < 256; i++) glyphs[i] = new NfdGlyph(0, 0, 6, 10, 6, 0);
        var font = new NfdFont { LineHeight = 10, StripWidth = 1, StripHeight = 1, Glyphs = glyphs };
        var list = new MenuList(NuiPane.Parse(Nui, "stScenePane.nui").Elements[0], font);
        for (int i = 0; i < rows; i++) list.Add($"step {i}", i);
        return list;
    }

    [Fact]
    public void A_disabled_row_is_never_hovered_pressed_or_chosen()
    {
        var list = List(3);
        list.Disable(0);                                   // the first step, reached
        Assert.True(list.Disabled(0));

        list.HoverAt(238);                                 // on the first row (its band 233..243)
        Assert.Equal(-1, list.Hovered);
        Assert.False(list.Press());
        list.Select(0);
        Assert.Equal(-1, list.Current);

        list.HoverAt(228);                                 // the second row is still anybody's
        Assert.Equal(1, list.Hovered);
        Assert.True(list.Press());
        Assert.Equal(1, list.CurrentData);
    }

    [Fact]
    public void Enabled_again_it_can_be_chosen_and_clearing_the_list_forgets_the_flags()
    {
        var list = List(2);
        list.Disable(1);
        list.Enable(1);
        list.Select(1);
        Assert.Equal(1, list.Current);

        list.Disable(0);
        list.Disable(7);                                   // no such row: nothing (the row lookup answers none)
        list.Clear();
        list.Add("again", 0);
        Assert.False(list.Disabled(0));
    }

    /// <summary>
    /// A list only shown — the lobby's room talk and the two status boxes — is
    /// never hovered, so a press takes nothing, and making it so forgets what was chosen; the list
    /// makes it choosable again.
    /// </summary>
    [Fact]
    public void A_list_only_shown_is_never_hovered_and_forgets_its_choice()
    {
        var list = List(3);
        list.Select(1);
        list.Inert = true;
        Assert.Equal(-1, list.Current);
        list.HoverAt(228);
        Assert.Equal(-1, list.Hovered);
        Assert.False(list.Press());

        list.Inert = false;
        list.HoverAt(228);
        Assert.Equal(1, list.Hovered);
    }
}
