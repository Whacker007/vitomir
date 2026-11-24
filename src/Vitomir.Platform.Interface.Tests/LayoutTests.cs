// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Platform.Interface.Tests;

/// <summary>
/// Where the screens' pieces land, to the pixel, at the design's 2560 × 1440, at 1920 × 1080, at 3840 × 2160 and on a
/// 3440 × 1440 ultrawide: units of a 1440-high screen, widths following the screen's
/// shape, and the player's own scale.
/// </summary>
public sealed class LayoutTests
{
    internal static Screen Laid(string name, int w, int h, float scale = 1f)
    {
        var s = new ScreenLoader().LoadEmbedded(name);
        s.Layout(new UiMetrics(w, h, scale));
        return s;
    }

    static PixelRect Px(Screen s, string id) => s.Metrics.ToPixels(s.Find(id)!.Rect);

    [Fact]
    public void A_unit_is_a_pixel_at_2560x1440_and_follows_the_height_elsewhere()
    {
        Assert.Equal(1f, new UiMetrics(2560, 1440).Pixel);
        Assert.Equal(0.75f, new UiMetrics(1920, 1080).Pixel);
        Assert.Equal(1.5f, new UiMetrics(3840, 2160).Pixel);
        Assert.Equal(2560f, new UiMetrics(1920, 1080).Width);
        Assert.Equal(3440f, new UiMetrics(3440, 1440).Width);           // a wider screen gets more room, not wider pieces
        Assert.Equal(1.5f, new UiMetrics(2560, 1440, 1.5f).Pixel);
        Assert.Equal(0.75f, new UiMetrics(2560, 1440, 0.5f).Pixel);     // the player's scale is kept within 75 % to 150 %
    }

    [Theory]
    [InlineData(2560, 1440, 960, 438, 640, 564)]
    [InlineData(1920, 1080, 720, 329, 480, 422)]
    [InlineData(3840, 2160, 1440, 657, 960, 846)]
    [InlineData(3440, 1440, 1400, 438, 640, 564)]
    public void The_pause_menus_plate_sits_in_the_middle_of_the_screen(int w, int h, int x, int y, int pw, int ph)
    {
        var s = Laid("pause", w, h);
        Assert.Equal(new PixelRect(x, y, pw, ph), Px(s, "plate"));
    }

    [Theory]
    [InlineData(2560, 1440, 1032, 673, 496, 74)]
    [InlineData(1920, 1080, 774, 505, 372, 55)]
    [InlineData(3840, 2160, 1548, 1010, 744, 110)]
    [InlineData(3440, 1440, 1472, 673, 496, 74)]
    public void The_pause_menus_first_button_fills_the_plate_inside_its_padding(int w, int h, int x, int y, int bw, int bh)
    {
        var s = Laid("pause", w, h);
        Assert.Equal(new PixelRect(x, y, bw, bh), Px(s, "resume"));
    }

    [Theory]
    [InlineData(2560, 1440, 360, 48, 1840, 1344)]
    [InlineData(1920, 1080, 270, 36, 1380, 1008)]
    [InlineData(3840, 2160, 540, 72, 2760, 2016)]
    [InlineData(3440, 1440, 800, 48, 1840, 1344)]
    public void The_controls_page_keeps_its_margin_and_is_no_wider_than_its_widest(int w, int h, int x, int y, int pw, int ph)
    {
        var s = Laid("controls", w, h);
        Assert.Equal(new PixelRect(x, y, pw, ph), Px(s, "page"));
    }

    [Theory]
    [InlineData(2560, 1440, 1856, 1284, 280, 74)]
    [InlineData(1920, 1080, 1392, 963, 210, 55)]
    [InlineData(3840, 2160, 2784, 1927, 420, 110)]
    [InlineData(3440, 1440, 2296, 1284, 280, 74)]
    public void OK_sits_at_the_pages_bottom_right_inside_its_padding(int w, int h, int x, int y, int bw, int bh)
    {
        var s = Laid("controls", w, h);
        Assert.Equal(new PixelRect(x, y, bw, bh), Px(s, "ok"));
        var page = Px(s, "page");
        Assert.Equal(page.Right - (int)MathF.Round(64f * s.Metrics.Pixel), Px(s, "ok").Right);
    }

    [Fact]
    public void Every_binding_is_in_the_scrolled_list_and_the_quick_slots_follow_under_their_heading()
    {
        // The quick slots' group (eight rows under its heading) follows the other actions, so the list scrolls at the
        // design size; a row is still 34 units (36 with the earlier build's three toggled rows, 41 before them and the awareness row, 46
        // before the cancel and reach rows) and the heading 64.8 (14 above, the group style's line, 4 below).
        var design = Fixtures.LaidPage(2560, 1440).Screen;
        var area = (Scroll)design.Find("bindingsArea")!;
        float heading = 14f + 36f * 1.3f + 4f;
        Assert.Equal((ControlsPage.Order.Count + ControlsPage.QuickSlotRows.Count) * 34f + heading, area.ContentHeight, 0.01f);
        Assert.Equal(area.ContentHeight - area.Rect.Height, area.MaxOffset, 0.01f);
        // 40 units narrower than before (998, not 1038): the view's column is 600 wide, its rows in a scrolled area.
        Assert.Equal(new PixelRect(424, 324, 998, 830), Px(design, "bindingsArea"));
        // The heading sits under the last action's row and over the first slot's.
        var group = design.Find("quickSlotsHeading")!.Rect;
        var page = Fixtures.Page();
        page.Screen.Layout(new UiMetrics(2560, 1440));
        Assert.True(page.Cell("menu", 0)!.Rect.Bottom <= group.Y && group.Bottom <= page.Cell("slot1", 0)!.Rect.Y);

        var big = Fixtures.LaidPage(2560, 1440, 1.5f).Screen;
        var bigArea = (Scroll)big.Find("bindingsArea")!;
        Assert.True(bigArea.MaxOffset > area.MaxOffset, "at 150 % the bindings scroll further");
        // Everything else still fits on the screen: the page, and OK inside it.
        Assert.Equal(1440 - 72, Px(big, "page").Bottom);
        Assert.True(Px(big, "ok").Bottom < Px(big, "page").Bottom);
    }

    [Fact]
    public void A_name_is_set_in_the_uncial_its_capitals_in_the_capitals_face_one_after_the_other()
    {
        var s = Laid("pause", 2560, 1440);
        var list = new DrawList();
        s.Draw(list);
        var title = list.Commands.Where(c => c.Kind == DrawKind.Text && (c.Text == "P" || c.Text == "aused")).ToList();
        Assert.Equal(2, title.Count);
        Assert.Equal(FaceId.Capitals, title[0].Face);
        Assert.Equal(FaceId.Uncial, title[1].Face);
        Assert.Equal(84f * Letters.CapitalScale, title[0].Size, 0.001f);
        Assert.Equal(84f, title[1].Size, 0.001f);
        // The second run starts where the first one's advance ends, on the same baseline.
        Assert.Equal(title[0].Pen.X + Face.Get(FaceId.Capitals).Measure("P") * title[0].Size, title[1].Pen.X, 0.01f);
        Assert.Equal(title[0].Pen.Y, title[1].Pen.Y);
        // And the whole word is centred on the plate.
        float width = Letters.Measure(Lettering.Name, "Paused", 84f);
        Assert.Equal(1280f - width / 2f, title[0].Pen.X, 0.01f);
    }

    /// <summary>A path longer than its line breaks after its separators, not at the edge; a word of no path still stands alone.</summary>
    [Fact]
    public void A_long_path_wraps_after_its_separators_and_a_short_text_is_unchanged()
    {
        // A Windows path, built from its parts (the export refuses a drive's path written out).
        string path = string.Join('\\', "D:", "Games", "a-long-folder-name", "Vitomir", "UserData", "Modules", "classic", "Saves");
        float em = 20f, width = Letters.Measure(Lettering.Text, string.Join('\\', "D:", "Games", "a-long-folder-name", ""), em) * 1.2f;
        var lines = Letters.Wrap(Lettering.Text, "In " + path + " now", em, width);
        Assert.True(lines.Count > 2, string.Join(" | ", lines));
        Assert.Equal("In " + path + " now", string.Join("", lines.Select((l, i) => i == 0 || lines[i - 1].EndsWith('\\') ? l : " " + l)));
        Assert.All(lines.Where(l => l.Contains('\\')), l => Assert.True(Letters.Measure(Lettering.Text, l, em) <= width || !l.TrimEnd('\\').Contains('\\'), l));
        Assert.Equal(["a b c"], Letters.Wrap(Lettering.Text, "a b c", em, Letters.Measure(Lettering.Text, "a b c", em) + 0.5f));
        var lone = Letters.Wrap(Lettering.Text, "unbreakableword", em, 10f);
        Assert.Equal(["unbreakableword"], lone);
    }

    [Theory]
    [InlineData(2560, 1440, false)]
    [InlineData(2560, 1440, true)]
    [InlineData(1280, 720, false)]
    [InlineData(1280, 720, true)]
    [InlineData(1024, 600, true)]
    public void With_the_toggled_rows_and_the_field_of_views_axis_the_page_still_fits(int w, int h, bool across)
    {
        // Three toggled rows and the axis's row in place of the pointer key's. the quick slots' group below the
        // other actions makes the bindings scroll (the same in units at every size); the rest of the page still fits.
        var current = Fixtures.Defaults() with { FieldOfViewAxis = across ? Presentation.FieldOfViewAxis.Horizontal : Presentation.FieldOfViewAxis.Vertical };
        var s = Fixtures.LaidPage(w, h, current: current).Screen;
        var area = (Scroll)s.Find("bindingsArea")!;
        Assert.Equal(area.ContentHeight - area.Rect.Height, area.MaxOffset, 0.01f);
        var page = Px(s, "page");
        Assert.True(page.Bottom <= h && page.Right <= w);
        // The view's column ends above the rule under the body. its rows are in a scrolled area (the arc work found it full; the
        // camera's, melee framing, the crosshair, the combat log and the attack direction added): the area is inside the body,
        // and every row, scrolled to, is seen whole inside it.
        var body = Px(s, "body");
        var view = (Scroll)s.Find("viewArea")!;
        var viewPx = s.Metrics.ToPixels(view.Rect);
        Assert.True(viewPx.Y >= body.Y && viewPx.Bottom <= body.Bottom, $"the view's area at {viewPx}, the body {body}");
        foreach (var id in new[] { "sensitivity", "invert", "headMotion", across ? "fieldOfViewHorizontal" : "fieldOfView", "fieldOfViewAxis",
                                   "actionSway", "follow", "facing", "meleeFraming", "alwaysRun", "doubleTap", "doubleTapSeconds",
                                   "attackDirection", "swingArcOpacity", "meleeReticle", "combatLog", "viewNote" })
        {
            var piece = s.Find(id)!;
            view.Reveal(piece.Rect);
            s.Layout(s.Metrics);
            var r = Px(s, id);
            var seen = s.Metrics.ToPixels(view.Rect);
            Assert.True(r.Y >= seen.Y - 1 && r.Bottom <= seen.Bottom + 1, $"{id} at {r}, the view's area {seen} (offset {view.Offset:0})");
            Assert.True(r.Right <= seen.Right + 1, $"{id} at {r} is cut at the right of {seen}");
        }
        Assert.True(Px(s, "ok").Bottom <= page.Bottom);
    }
}
