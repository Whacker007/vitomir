// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Platform.Interface.Tests;

/// <summary>
/// What a screen with a look of its own needs of the framework, and that a screen without one is drawn as it always was: a look
/// (<see cref="ISkin"/>) is asked for every piece and may draw it its own way; the pieces' states it reads (a tab's selected, a
/// piece disabled, a toggle's options side by side, the focus shown only once a key moved it); a loader's own text styles;
/// pictures in the draw list.
/// </summary>
public sealed class LookTests
{
    const string File = """
        { "root": { "type": "column", "children": [
            { "type": "text", "id": "words", "text": "Words", "style": "mine" },
            { "type": "button", "id": "tab", "text": "Tab", "style": "tab", "selected": "chosen" },
            { "type": "button", "id": "off", "text": "Off", "enabled": "can" },
            { "type": "toggle", "id": "pick", "bind": "pick", "options": ["Leave", "Import"], "segments": true, "action": "pick", "width": 200 },
            { "type": "panel", "id": "plain", "fill": "band", "height": 10 }
        ] } }
        """;

    sealed class Dresser : ISkin
    {
        public readonly List<string> Asked = [];
        public bool Draw(Painter p, Piece piece)
        {
            Asked.Add(piece.Id ?? piece.Type);
            if (piece is not Button b) return false;
            p.List.Picture(new PictureQuad(1, 0f, 0f, 10f, 10f, 0f, 0f, 1f, 1f, new Rgba(1f, 1f, 1f)));
            return true;
        }
    }

    static (Screen Screen, ScreenLoader Loader) Make(ScreenData data)
    {
        var mine = new TextStyle(Lettering.Text, 33f, new Rgba(1f, 0f, 0f), TextLook.Plain, 1.2f);
        var loader = new ScreenLoader { Styles = new Dictionary<string, TextStyle> { ["mine"] = mine } };
        var s = loader.Parse(File, "look", data);
        s.Layout(new UiMetrics(1280, 800));
        return (s, loader);
    }

    [Fact]
    public void A_look_is_asked_for_every_piece_and_draws_the_ones_it_dresses()
    {
        var data = new ScreenData().Set("chosen", true).Set("can", true).Set("pick", "Leave");
        var (s, loader) = Make(data);
        Assert.Empty(loader.Problems);
        var plain = new DrawList();
        s.Draw(plain);
        Assert.DoesNotContain(plain.Commands, c => c.Kind == DrawKind.Picture);

        var dresser = new Dresser();
        s.Skin = dresser;
        var dressed = new DrawList();
        s.Draw(dressed);
        Assert.Equal(["column", "words", "tab", "off", "pick", "plain"], dresser.Asked);
        Assert.Equal(2, dressed.Pictures.Count);                         // the two buttons, its way
        Assert.Equal(["Words", "Leave"], dressed.Texts());                                    // the rest as before
        Assert.Equal(["Words"], plain.Texts().Take(1));
        Assert.Equal("Leave", plain.Texts().Last());
        // The loader's own style, before the theme's.
        var words = (TextPiece)s.Find("words")!;
        Assert.Equal(Lettering.Text, words.Style.Lettering);
        Assert.Equal("mine", words.StyleName);
        Assert.Equal("tab", ((Button)s.Find("tab")!).StyleName);
    }

    [Fact]
    public void A_tab_knows_it_is_chosen_and_a_disabled_piece_is_shown_but_never_worked()
    {
        var data = new ScreenData().Set("chosen", true).Set("can", false).Set("pick", "Leave");
        var (s, _) = Make(data);
        Assert.True(((Button)s.Find("tab")!).Selected);
        var off = (Button)s.Find("off")!;
        Assert.False(off.Enabled);
        Assert.DoesNotContain(off, s.Worked);
        var (x, y) = Fixtures.Middle(s, off);
        Assert.Null(s.HitTest(x, y));
        data.Set("can", true);
        s.Layout(s.Metrics);
        Assert.Contains(off, s.Worked);
    }

    [Fact]
    public void A_click_on_a_toggles_option_picks_that_option_and_Enter_goes_to_the_next()
    {
        var data = new ScreenData().Set("chosen", true).Set("can", true).Set("pick", "Leave");
        var (s, _) = Make(data);
        var pick = (Toggle)s.Find("pick")!;
        float u = s.Metrics.Pixel;
        // A click on the right half: Import; on the right half again: nothing changes (no action); on the left: Leave.
        void ClickAt(float units)
        {
            s.PointerMove(units * u, (pick.Rect.Y + pick.Rect.Height * 0.5f) * u);
            s.PointerDown();
            s.PointerUp();
        }
        ClickAt(pick.Rect.X + pick.Rect.Width * 0.75f);
        Assert.Equal("Import", data.Text("pick"));
        ClickAt(pick.Rect.X + pick.Rect.Width * 0.8f);
        Assert.Equal("Import", data.Text("pick"));
        ClickAt(pick.Rect.X + pick.Rect.Width * 0.1f);
        Assert.Equal("Leave", data.Text("pick"));
        Assert.Equal(["pick", "pick"], s.TakeActions().Select(a => a.Name));
        s.SetFocus(pick);
        s.Key(UiKey.Enter);
        Assert.Equal("Import", data.Text("pick"));
        Assert.Equal(0, pick.SegmentAt(pick.Rect.X + 1f));
        Assert.Equal(1, pick.SegmentAt(pick.Rect.Right - 1f));
    }

    [Fact]
    public void The_focus_is_shown_once_a_key_moves_it_and_no_longer_when_the_pointer_moves()
    {
        var data = new ScreenData().Set("chosen", true).Set("can", true).Set("pick", "Leave");
        var (s, _) = Make(data);
        Assert.False(s.FocusShown);
        s.Key(UiKey.Down);
        Assert.True(s.FocusShown);
        s.PointerMove(5f, 5f);
        Assert.False(s.FocusShown);
        s.Key(UiKey.Tab);
        Assert.True(s.FocusShown);
        s.Key(UiKey.Escape);
        Assert.True(s.FocusShown);
        s.PointerDown();
        Assert.False(s.FocusShown);
    }

    [Fact]
    public void A_picture_in_the_list_keeps_its_place_its_part_and_its_tint_and_the_list_forgets_it_when_begun_again()
    {
        var list = new DrawList();
        list.Begin(100, 100);
        list.Picture(new PictureQuad(3, 1.5f, 2.5f, 10f, 0f, 0f, 0f, 1f, 1f, new Rgba(1f, 1f, 1f)));   // no height: nothing
        list.Picture(new PictureQuad(3, 1.5f, 2.5f, 10f, 20f, 0.25f, 0f, 0.75f, 1f, new Rgba(1f, 0.5f, 1f, 0.5f)));
        var c = Assert.Single(list.Commands);
        Assert.Equal(DrawKind.Picture, c.Kind);
        Assert.Equal(new PictureQuad(3, 1.5f, 2.5f, 10f, 20f, 0.25f, 0f, 0.75f, 1f, new Rgba(1f, 0.5f, 1f, 0.5f)), list.Pictures[c.Item]);
        list.Begin(100, 100);
        Assert.Empty(list.Pictures);
    }
}
