// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The Fixes page scrolls** (ours). At twenty-one rows it was full — an earlier build had shrunk the rows to
/// twelve pixels to keep the last above the hint box — and `soakedvoid` made twenty-two (`netprops` twenty-three). So the list
/// scrolls a row at a time behind the load screen's own scroll bar (the page's frame is the load
/// screen's, `spLoadScrollBar` at 441,105), driven the way the mouse drives it through the front end's
/// own press, drag, release and wheel: the rows in the box shown and raised, the rest off the pane,
/// the slider where the scroll says, and the check boxes and the hint box working on whatever row is
/// under the pointer. The last row, scrolled to, sits above the hint box. A page (the wheel, a press on the
/// track) is a box's worth. **Now** the repairs are off the page, a row is 16 high with its box level with
/// it, and each heading but the first has room over it (<see cref="FixesPage.HeadingGap"/>), so how many rows are
/// on show depends on how many headings are among them (<see cref="FixesPage.ShownAt"/>).
/// </summary>
public class FixesScrollTests
{
    sealed record Rig(FrontEnd Menus, OptionsMenu Options, FixesPage Page, MenuPane Pane);

    static Rig Open(GameFiles files)
    {
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find) { State = FrontEnd.StateFrontEnd };
        var ini = Path.Combine(Path.GetTempPath(), $"vitomir-fixscroll-{Guid.NewGuid():N}.ini");
        var screen = new OptionsMenu(menus, text.Find, new GameOptions()) { SavePath = ini };
        screen.Fixes.SavePath = ini;
        menus.Options = screen;
        screen.Open();
        screen.Fixes.Open();
        return new Rig(menus, screen, screen.Fixes, screen.Fixes.Pane);
    }

    static (float X, float Y) Middle(MenuPane pane, string control)
    {
        var e = pane.Element(control)!;
        var (x, y) = pane.At(e);
        return (x + e.Size[0] / 2f, y + e.Size[1] / 2f);
    }

    static void PressAt(FrontEnd menus, float x, float y)
    {
        menus.PointerAt(x, y);
        menus.Frame(menus.NowMs);
        menus.LeftHeld = true;
        menus.Press();
        menus.LeftHeld = false;
        menus.Release();
    }

    static FixesPage.Row[] Rows => [.. FixesPage.Rows()];

    /// <summary>The slider's foot as drawn: its `.nui` place raised by what the page lifted it.</summary>
    static float SliderY(MenuPane pane) => pane.At(pane.Element(FixesPage.SliderName)!).Y;

    [Fact]
    public void The_page_opens_at_the_top_with_the_rows_the_box_holds_and_the_slider_at_the_top_of_its_track()
    {
        using var files = Install.Open();
        var rig = Open(files);
        int shown = rig.Page.ShownAt(0);
        Assert.True(Rows.Length > shown, $"{Rows.Length} rows, {shown} shown: nothing to scroll");
        Assert.Contains(Rows, r => r.Name == "soakedvoid");
        Assert.DoesNotContain(Rows, r => r.Kind == FixesPage.RowKind.Repair);                 // off the page
        Assert.Equal(0, rig.Page.Scroll);
        for (int i = 0; i < Rows.Length; i++)
            Assert.Equal(i < shown, !rig.Pane.Hidden.Contains(Rows[i].Id));
        Assert.Equal((float)FixesPage.TopY, rig.Pane.At(rig.Pane.Element(Rows[0].Id)!).Y);
        Assert.Equal((float)(FixesPage.BarY + FixesPage.BarH - 8), SliderY(rig.Pane));          // the top of the list, the top of the track
    }

    /// <summary>How far one page goes from the top: a box's worth, or to the end when that is nearer.</summary>
    static int OnePage(FixesPage page) => Math.Min(page.ShownAt(0), page.ScrollEnd);

    /// <summary>
    /// A press on the track below the slider pages a box's worth towards the end — and, the list
    /// being longer than two boxes since the Fixes became two groups, a second press reaches it: the last row
    /// comes into the box's lowest place — **above the hint box**, check box and all — the first goes, and the
    /// slider is at the foot of its track; above the slider, back to the top.
    /// </summary>
    [Fact]
    public void A_press_on_the_track_pages_and_the_last_row_sits_above_the_hint_box()
    {
        using var files = Install.Open();
        var rig = Open(files);
        int end = rig.Page.ScrollEnd;
        Assert.True(end >= 1);
        // The very foot of the track: below the slider wherever a page has left it.
        PressAt(rig.Menus, FixesPage.BarX + 4, FixesPage.BarY + 2);
        Assert.Equal(OnePage(rig.Page), rig.Page.Scroll);
        for (int presses = 0; presses < 5 && rig.Page.Scroll < end; presses++) PressAt(rig.Menus, FixesPage.BarX + 4, FixesPage.BarY + 2);
        Assert.Equal(end, rig.Page.Scroll);
        var last = Rows[^1];
        Assert.DoesNotContain(last.Id, rig.Pane.Hidden);
        Assert.Contains(Rows[0].Id, rig.Pane.Hidden);
        var box = rig.Pane.Element(last.Id)!;
        var (_, lastY) = rig.Pane.At(rig.Pane.Element(last.Id + "label")!);
        Assert.Equal((float)rig.Page.LastRowY, lastY);
        Assert.True(rig.Pane.At(box).Y >= FixesPage.HintY + FixesPage.HintH,
                    $"the last row's box at {rig.Pane.At(box).Y}, the hint box's top at {FixesPage.HintY + FixesPage.HintH}");
        var (x, y) = Middle(rig.Pane, last.Id);
        Assert.Same(box, rig.Pane.ControlAt(x, y));                                     // under the pointer where it is drawn
        Assert.Equal((float)FixesPage.BarY, SliderY(rig.Pane));
        // Exactly the box's count of rows at that scroll is on show.
        Assert.Equal(rig.Page.ShownAt(end), Rows.Count(r => !rig.Pane.Hidden.Contains(r.Id)));

        // The very top of the track, above the slider: a page back, and so on to the top.
        PressAt(rig.Menus, FixesPage.BarX + 4, FixesPage.BarY + FixesPage.BarH - 2);
        Assert.Equal(Math.Max(0, end - rig.Page.ShownAt(end)), rig.Page.Scroll);
        for (int presses = 0; presses < 5 && rig.Page.Scroll > 0; presses++) PressAt(rig.Menus, FixesPage.BarX + 4, FixesPage.BarY + FixesPage.BarH - 2);
        Assert.Equal(0, rig.Page.Scroll);
        Assert.DoesNotContain(Rows[0].Id, rig.Pane.Hidden);
    }

    [Fact]
    public void The_slider_dragged_down_its_track_scrolls_the_list_and_stays_where_it_is_let_go()
    {
        using var files = Install.Open();
        var rig = Open(files);
        var menus = rig.Menus;
        var (x, y) = Middle(rig.Pane, FixesPage.SliderName);
        menus.PointerAt(x, y);
        menus.Frame(menus.NowMs);
        menus.LeftHeld = true;
        menus.Press();
        Assert.True(rig.Page.Bar!.Held);
        float travel = FixesPage.BarH - 8;
        menus.PointerAt(x, y - travel / 2f);                                             // half way down
        menus.Frame(menus.NowMs);
        int half = rig.Page.Scroll;
        Assert.InRange(half, 1, rig.Page.ScrollEnd - 1);
        Assert.Equal(y - travel / 2f - 4, SliderY(rig.Pane), 1f);                        // under the pointer, not snapped to a row
        menus.PointerAt(x, 0);                                                           // past the foot: held to the track
        menus.Frame(menus.NowMs);
        Assert.Equal(rig.Page.ScrollEnd, rig.Page.Scroll);
        menus.LeftHeld = false;
        menus.Release();
        Assert.False(rig.Page.Bar.Held);
        menus.PointerAt(x, 400);
        menus.Frame(menus.NowMs);
        Assert.Equal(rig.Page.ScrollEnd, rig.Page.Scroll);                               // let go: it no longer follows
    }

    [Fact]
    public void The_wheel_scrolls_the_page_down_and_back()
    {
        using var files = Install.Open();
        var rig = Open(files);
        int end = rig.Page.ScrollEnd;
        rig.Menus.PointerAt(300, 300);
        rig.Menus.Wheel(-1);                                                             // a box's worth a turn,
        Assert.Equal(OnePage(rig.Page), rig.Page.Scroll);
        for (int turns = 0; turns < 6; turns++) rig.Menus.Wheel(-1);                     // held at the end,
        Assert.Equal(end, rig.Page.Scroll);
        rig.Menus.Wheel(+1);                                                             // and back the same way.
        Assert.Equal(Math.Max(0, end - rig.Page.ShownAt(end)), rig.Page.Scroll);
        for (int turns = 0; turns < 6; turns++) rig.Menus.Wheel(+1);
        Assert.Equal(0, rig.Page.Scroll);
    }

    /// <summary>
    /// Scrolled, a row's check box is where it is drawn: clicked there, its switch follows it, and the
    /// hint box says what the row under the pointer does — and a row scrolled out of the box is not
    /// under the pointer at its old place. OK writes what was ticked.
    /// </summary>
    [Fact]
    public void A_scrolled_row_s_check_box_and_hint_work_where_the_row_is_drawn()
    {
        using var files = Install.Open();
        var rig = Open(files);
        const string fix = "fxfix_netprops";
        var (x0, y0) = Middle(rig.Pane, "fxfix_movespeed");
        // As far down as the list goes with the row still in the box.
        int at = Enumerable.Range(1, rig.Page.ScrollEnd).Last(n => { rig.Page.ScrollTo(n); return !rig.Pane.Hidden.Contains(fix); });
        rig.Page.ScrollTo(at);
        Assert.True(rig.Pane.Lift[fix] > 0);
        Assert.NotEqual("fxfix_movespeed", rig.Pane.ControlAt(x0, y0)?.Name);           // scrolled out
        var (x, y) = Middle(rig.Pane, fix);
        PressAt(rig.Menus, x, y);
        Assert.True(rig.Pane.Checks[fix]);
        Assert.True(rig.Page.Editing.NetProps);
        rig.Menus.PointerAt(x, y);
        rig.Menus.Frame(rig.Menus.NowMs);
        // The row's one line, then what the registry says of it (`FixesPage.Reach`).
        Assert.StartsWith(FixesPage.Says(Vitomir.Engine.Features.GameFeatures.Registry.Find("fixes", "netprops")!) + ". ", rig.Pane.Texts["fxhint"]);
        Fixes? got = null;
        rig.Page.Changed += (f, _) => got = f;
        rig.Page.Ok();
        Assert.True(got!.NetProps);
        Assert.Equal("on", Diagnostics.IniFile.Read(rig.Page.SavePath!)["fixes.netprops"]);
        File.Delete(rig.Page.SavePath!);
    }

    /// <summary>
    /// **The ticks are seen.** Multiplayer's pass drew a check box for every pane up in the front end,
    /// with its own record of what is ticked — which knows nothing of the Options screens' or this
    /// page's — so a ticked box was drawn over, unticked, and at its place before any scroll (found in
    /// Earlier renders of this page). It draws its own panes' now; these are the front end's pass's.
    /// </summary>
    [Fact]
    public void Multiplayer_s_pass_draws_no_box_over_the_page_s_own()
    {
        using var files = Install.Open();
        var rig = Open(files);
        var crypt = Path.Combine(Path.GetTempPath(), $"vitomir-fixscroll-crypt-{Guid.NewGuid():N}");
        var mp = new MpMenus(rig.Menus, files, new Strings(files).Find, crypt);
        Assert.Empty(mp.Checkboxes());
        rig.Page.Cancel();                                                           // the Options screen under it
        Assert.Empty(mp.Checkboxes());
    }

    [Fact]
    public void Reopening_the_page_starts_it_at_the_top_again()
    {
        using var files = Install.Open();
        var rig = Open(files);
        rig.Page.ScrollTo(rig.Page.ScrollEnd);
        rig.Page.Cancel();
        rig.Page.Open();
        Assert.Equal(0, rig.Page.Scroll);
        Assert.DoesNotContain(Rows[0].Id, rig.Pane.Hidden);
        Assert.Contains(Rows[^1].Id, rig.Pane.Hidden);
    }
}
