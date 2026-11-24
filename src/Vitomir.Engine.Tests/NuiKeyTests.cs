// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// What a <c>.nui</c> file can do with the keyboard (`NuiKeys`). Pinned because — *"In v1.1 it
/// just changed the status bar"* of F10 — suggested a pane's data might bind the key where the code does not
/// (F10's entry, is read nowhere). It cannot: the control parser's one keyboard attribute, <c>key</c>
/// (<c>%i</c>), only puts a control in its pane's focus ring for typed characters, no
/// shipped pane uses it, and so every pane's ring — the status bar's first — is its edit fields alone.
/// </summary>
public class NuiKeyTests
{
    /// <summary>Every shipped pane, read from the install through the archives.</summary>
    static List<(string File, NuiPane Pane)> Shipped()
    {
        using var files = Install.Open();
        var list = files.List("ui/").Where(n => n.EndsWith(".nui", StringComparison.OrdinalIgnoreCase))
                        .Select(n => (Path.GetFileName(n), NuiPane.Read(files.Read(n), n))).ToList();
        Assert.NotEmpty(list);
        return list;
    }

    [Fact]
    public void No_shipped_pane_gives_a_control_a_key()
    {
        var panes = Shipped();
        Assert.True(panes.Count >= 63, $"{panes.Count} panes");
        foreach (var (file, pane) in panes)
        {
            Assert.DoesNotContain(pane.Elements, e => e.Attrs.ContainsKey(NuiKeys.Attribute));
            Assert.Empty(NuiKeys.KeyedControls(pane));
            // So the ring is the edit fields, in file order.
            Assert.Equal(pane.Elements.Where(e => e.Type == "edit").Select(e => e.Name), NuiKeys.Ring(pane).Select(e => e.Name));
        }
    }

    [Fact]
    public void The_status_bars_give_the_keyboard_to_nothing_but_the_chat_line()
    {
        var panes = Shipped().ToDictionary(p => p.File, p => p.Pane, StringComparer.OrdinalIgnoreCase);
        foreach (var bar in new[] { "statusBarSP.nui", "statusBarADV.nui" })
            Assert.Empty(NuiKeys.Ring(panes[bar]));
        Assert.Equal(["sbchatedit"], NuiKeys.Ring(panes["statusBarMP.nui"]).Select(e => e.Name));   // the network game's chat (focused on it)
    }

    [Fact]
    public void Every_attribute_a_shipped_pane_uses_is_one_the_parser_reads()
    {
        foreach (var (file, pane) in Shipped())
        {
            Assert.Empty(pane.Warnings);
            foreach (var e in pane.Elements)
                foreach (var key in e.Attrs.Keys)
                    Assert.True(NuiPane.ElementKeys.Contains(key), $"{file}: {e.Name}'s '{key}'");
        }
    }

    /// <summary>
    /// A mod's <c>key</c>: a nonzero <c>%i</c> joins the ring where its line is, zero does not, and an
    /// edit field joins by itself. The ring's first has the keyboard when the pane is made and shown.
    /// </summary>
    [Fact]
    public void A_key_attribute_puts_a_control_in_the_focus_ring_in_file_order()
    {
        var nui = NuiPane.Parse(string.Join('\n',
            "name=t",
            "element button name=first key=1 pos=0,0 size=10,10",
            "element edit name=e1 pos=0,20 size=50,10",
            "element checkbox name=off key=0 pos=0,40 size=10,10",
            "element static name=hex key=0x2 pos=0,60 size=10,10",
            "element edit name=e2 key=1 pos=0,80 size=50,10"), "t.nui");
        Assert.Equal(["first", "e1", "hex", "e2"], NuiKeys.Ring(nui).Select(e => e.Name));
        Assert.Equal(["first", "hex"], NuiKeys.KeyedControls(nui).Select(e => e.Name));
        Assert.False(NuiKeys.Keyed(nui.Elements.First(e => e.Name == "off")));
    }

    /// <summary>
    /// Tab (an edit field's character 9) walks the whole ring, so it can land on a <c>key</c> control — which
    /// then has the keyboard and, having only the base handler and no event-9 binding, types nothing and
    /// does not pass the keyboard on (a click on a field takes it back).
    /// </summary>
    [Fact]
    public void Tab_can_land_on_a_keyed_control_which_types_nothing()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var nui = NuiPane.Parse(string.Join('\n',
            "name=t",
            "element edit name=e1 pos=0,20 size=50,10",
            "element button name=b key=1 pos=0,40 size=10,10",
            "element edit name=e2 pos=0,80 size=50,10"), "t.nui");
        var menus = new FrontEnd(n => n == "t" ? nui : assets.Pane(n), n => assets.Font(n), text.Find) { State = FrontEnd.StateGame };
        var pane = menus.Pane("t");
        Assert.Equal("e1", pane.Focus);
        Assert.True(pane.Tab(shift: false));
        Assert.Equal("b", pane.Focus);
        Assert.False(pane.Edits.ContainsKey("b"));                                 // what is typed now goes nowhere (FrontEnd.Type)
        Assert.False(pane.Tab(shift: false));                                      // not even Tab: the keyboard stays on it
        Assert.Equal("b", pane.Focus);
    }
}
