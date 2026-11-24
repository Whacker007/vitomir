// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Platform.Interface.Tests;

/// <summary>
/// The pointer and the keyboard on a screen: the pointer tested against the same
/// rectangles the screen was drawn from, through its scrolling areas; a press and a release on one piece is a click;
/// the wheel scrolls; the focus moves with Up, Down and Tab, Enter works it and Escape backs out.
/// </summary>
public sealed class InputTests
{
    [Fact]
    public void The_pointer_finds_the_button_under_it_and_a_click_fires_its_action()
    {
        var s = LayoutTests.Laid("pause", 1920, 1080);
        var quit = s.Find("quit")!;
        var (x, y) = Fixtures.Middle(s, quit);
        Assert.Same(quit, s.HitTest(x, y));
        Assert.Null(s.HitTest(10, 10));
        // The button's edges, to the pixel: its last pixel is it, the next one is not.
        var px = s.Metrics.ToPixels(quit.Rect);
        Assert.Same(quit, s.HitTest(px.Right - 0.5f, y));
        Assert.Null(s.HitTest(px.Right + 0.5f, y));

        Fixtures.Click(s, quit);
        Assert.Equal(["quit"], s.TakeActions().Select(a => a.Name));
        Assert.Same(quit, s.Focus);              // the pointer moves the focus with it
    }

    [Fact]
    public void A_press_on_one_button_and_a_release_on_another_is_no_click()
    {
        var s = LayoutTests.Laid("pause", 2560, 1440);
        var (x0, y0) = Fixtures.Middle(s, s.Find("resume")!);
        var (x1, y1) = Fixtures.Middle(s, s.Find("quit")!);
        s.PointerMove(x0, y0);
        s.PointerDown();
        s.PointerMove(x1, y1);
        s.PointerUp();
        Assert.Empty(s.TakeActions());
    }

    [Fact]
    public void Up_and_Down_move_the_focus_Enter_works_it_and_Escape_backs_out()
    {
        var s = LayoutTests.Laid("pause", 2560, 1440);
        Assert.Equal("resume", s.Focus?.Id);
        s.Key(UiKey.Down);
        Assert.Equal("controls", s.Focus?.Id);
        s.Key(UiKey.Down);
        Assert.Equal("quit", s.Focus?.Id);
        s.Key(UiKey.Down);                        // nothing below: round to the first
        Assert.Equal("resume", s.Focus?.Id);
        s.Key(UiKey.Up);
        Assert.Equal("quit", s.Focus?.Id);
        s.Key(UiKey.BackTab);
        Assert.Equal("controls", s.Focus?.Id);
        s.Key(UiKey.Enter);
        s.Key(UiKey.Escape);
        Assert.Equal(["controls", "resume"], s.TakeActions().Select(a => a.Name));
    }

    [Fact]
    public void On_the_controls_page_Down_goes_down_a_column_of_cells_and_Right_across_a_row()
    {
        var page = Fixtures.LaidPage(2560, 1440);
        var s = page.Screen;
        s.SetFocus(page.Cell("forward", 0));
        s.Key(UiKey.Down);
        Assert.Same(page.Cell("back", 0), s.Focus);
        s.Key(UiKey.Right);
        Assert.Same(page.Cell("back", 1), s.Focus);
        s.Key(UiKey.Right);                       // from the secondary cells, across to the view's settings
        Assert.IsNotType<KeyCell>(s.Focus);
    }

    [Fact]
    public void The_wheel_scrolls_the_bindings_and_a_cell_scrolled_out_of_sight_is_not_hit()
    {
        var page = Fixtures.LaidPage(2560, 1440, 1.5f);
        var s = page.Screen;
        var area = (Scroll)s.Find("bindingsArea")!;
        var first = page.Cell("forward", 0)!;
        var (x, y) = Fixtures.Middle(s, first);
        s.PointerMove(x, y);
        Assert.Same(first, s.Hover);

        s.Wheel(-1f);                              // a turn down
        Assert.Equal(area.Step, area.Offset);
        // The first row has moved up out of the area; where its cell was is now the second row's.
        Assert.Same(page.Cell("back", 0), s.HitTest(x, y));
        var top = first.Rect;
        Assert.True(top.Bottom <= area.Rect.Y + 0.01f);
        Assert.Null(s.HitTest(x, (top.Y + top.Height / 2f) * s.Metrics.Pixel));

        for (int i = 0; i < 50; i++) s.Wheel(-1f);  // no further than the end
        Assert.Equal(area.MaxOffset, area.Offset);
    }

    [Fact]
    public void The_scroll_bar_s_thumb_is_dragged_and_a_press_beside_it_takes_it_there()
    {
        var page = Fixtures.LaidPage(2560, 1440, 1.5f);
        var s = page.Screen;
        var area = (Scroll)s.Find("bindingsArea")!;
        float u = s.Metrics.Pixel;
        Assert.True(area.MaxOffset > 0f);
        var thumb = area.Thumb;
        float barX = area.Rect.Right - Scroll.BarRoom / 2f;

        // The thumb held near its top and pulled down by half the bar's free run: half way.
        s.PointerMove(barX * u, (thumb.Y + 5f) * u);
        s.PointerDown();
        float run = area.Rect.Height - thumb.Height;
        s.PointerMove(barX * u, (thumb.Y + 5f + run / 2f) * u);
        Assert.Equal(area.MaxOffset / 2f, area.Offset, 1);
        s.PointerMove(barX * u, (area.Rect.Bottom + 500f) * u);                  // past the end: the end
        Assert.Equal(area.MaxOffset, area.Offset, 1);
        s.PointerUp();
        Assert.Empty(s.TakeActions());                                            // the bar works no piece

        // A press on the bar above the thumb: the thumb's middle comes to the pointer.
        s.PointerMove(barX * u, (area.Rect.Y + 2f) * u);
        s.PointerDown();
        s.PointerUp();
        Assert.Equal(0f, area.Offset, 1);
        s.PointerMove(barX * u, (area.Rect.Bottom - 2f) * u);                     // and below it: the end
        s.PointerDown();
        s.PointerUp();
        Assert.Equal(area.MaxOffset, area.Offset, 1);
    }

    [Fact]
    public void Moving_the_focus_to_a_cell_out_of_sight_scrolls_it_into_view()
    {
        var page = Fixtures.LaidPage(2560, 1440, 1.5f);
        var s = page.Screen;
        var area = (Scroll)s.Find("bindingsArea")!;
        s.SetFocus(page.Cell("forward", 0));
        // Down past Menu, over the Quick Slots' heading, to the last slot's row.
        for (int i = 0; i < ControlsPage.Order.Count + ControlsPage.QuickSlotRows.Count - 1; i++) s.Key(UiKey.Down);
        var last = page.Cell("slot8", 0)!;
        Assert.Same(last, s.Focus);
        Assert.Equal(area.MaxOffset, area.Offset, 0.01f);
        Assert.True(last.Rect.Bottom <= area.Rect.Bottom + 0.01f && last.Rect.Y >= area.Rect.Y - 0.01f);
    }

    [Fact]
    public void A_slider_follows_the_pointer_and_Left_and_Right_step_it()
    {
        var page = Fixtures.LaidPage(1920, 1080);
        var s = page.Screen;
        var slider = (Slider)s.Find("sensitivity")!;
        var r = s.Metrics.ToPixels(slider.Rect);
        s.PointerMove(r.X + 1, r.Y + r.Height / 2f);
        s.PointerDown();
        Assert.Equal(slider.Min, s.Data.Number("sensitivity"), 0.00001f);
        s.PointerMove(r.Right - 1, r.Y + r.Height / 2f);
        Assert.Equal(slider.Max, s.Data.Number("sensitivity"), 0.00001f);
        s.PointerUp();
        s.Key(UiKey.Left);
        Assert.Equal(slider.Max - slider.Step, s.Data.Number("sensitivity"), 0.00001f);
        Assert.All(s.TakeActions(), a => Assert.Equal("changed", a.Name));
    }

    [Fact]
    public void A_toggle_goes_round_its_words()
    {
        var page = Fixtures.LaidPage(2560, 1440);
        var s = page.Screen;
        var facing = s.Find("facing")!;
        Assert.Equal("Move", s.Data.Text("facing"));
        Fixtures.Click(s, facing);
        Assert.Equal("View", s.Data.Text("facing"));
        s.Key(UiKey.Right);
        Assert.Equal("Move", s.Data.Text("facing"));
    }
}
