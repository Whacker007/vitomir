// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Audio;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The front end and the in-game menus as 1.1 has them: the main
/// menu, the single-player menu, the credits, the escape menu, the save and load screens and their
/// two dialogs, driven the way the mouse and the keys drive them.
/// </summary>
public class FrontEndTests
{
    sealed class Slots(params (int Slot, string Description)[] saves) : ISaveSlots
    {
        public Dictionary<int, string?> Saved { get; } = saves.ToDictionary(s => s.Slot, s => (string?)s.Description);
        public bool Exists(int slot) => Saved.ContainsKey(slot);
        public string? Description(int slot) => Saved.GetValueOrDefault(slot);
    }

    sealed record Rig(FrontEnd Menus, UiAssets Assets, Strings Text, List<int> Sounds, List<string> Music, List<string> Posted);

    static Rig Open(GameFiles files, ISaveSlots? slots = null, int state = FrontEnd.StateFrontEnd)
    {
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var sounds = new List<int>();
        var music = new List<string>();
        var posted = new List<string>();
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find)
        {
            State = state,
            SaveSlots = slots ?? new Slots(),
            Sound = sounds.Add,
            Music = music.Add,
            Describe = () => "The Convent (00:24:25)",
            Credits = () => files.TryRead(NlsFile.PathFor("credits"), out var d)
                ? FrontEnd.ParseCredits(System.Text.Encoding.Latin1.GetString(d)) : [],
        };
        menus.Posted += posted.Add;
        return new Rig(menus, assets, text, sounds, music, posted);
    }

    /// <summary>A click on a control: the pointer to its middle, pressed, released, a frame drawn.</summary>
    static void Click(FrontEnd menus, string file, string control)
    {
        var pane = menus.Pane(file);
        var e = pane.Element(control)!;
        menus.PointerAt(pane.X + e.Pos[0] + e.Size[0] / 2f, pane.Y + e.Pos[1] + e.Size[1] / 2f);
        menus.Frame(menus.NowMs);
        menus.LeftHeld = true;
        menus.Press();
        menus.LeftHeld = false;
        menus.Release();
        menus.Frame(menus.NowMs);
        menus.Drawn();
    }

    static void Key(FrontEnd menus, MenuKey key)
    {
        menus.Drawn();
        menus.Key(key);
    }

    [Fact]
    public void The_main_menu_plays_its_theme_and_floats_the_version_near_the_bottom_for_ten_seconds()
    {
        using var files = Install.Open();
        var rig = Open(files);
        rig.Menus.OpenMainMenu();
        Assert.Equal(FrontEnd.StateFrontEnd, rig.Menus.State);
        Assert.Equal(["redemptiontheme"], rig.Music);
        var line = Assert.Single(rig.Menus.Floating.Up);
        // Not 1.1's "Version 1.1": this engine and its build, so the two cannot
        // be mistaken for each other at a glance.
        Assert.Equal(FrontEnd.Version, line.Text);
        Assert.Matches(@"^Vitomir \d+\.\d+\.\d+(-[0-9A-Za-z.]+)?( \(\w+\))?$", line.Text);   // the release version, and a batch in a development build
        Assert.Equal(FrontEnd.VersionFont, line.Font);
        Assert.Equal((640 - rig.Assets.Font(FrontEnd.VersionFont).Measure(FrontEnd.Version)) / 2, (int)line.X);
        Assert.Equal(5f, line.Y);
        Assert.Equal((10000, 2000, 2000), (line.TotalMs, line.FadeInMs, line.FadeOutMs));
        // No key hook on the main menu: Escape and Enter do nothing.
        Key(rig.Menus, MenuKey.Escape);
        Assert.True(rig.Menus.Pane(FrontEnd.MainMenuFile).Shown);
    }

    [Fact]
    public void The_version_line_is_drawn_only_while_the_main_menu_is_up_not_over_a_screen_opened_on_it()
    {
        using var files = Install.Open();
        var rig = Open(files);
        rig.Menus.OpenMainMenu();
        var line = Assert.Single(rig.Menus.Floating.Up);
        Assert.True(rig.Menus.CaptionDrawn(line));
        rig.Menus.PushLayer();                                   // as the Options screen opens over it
        Assert.False(rig.Menus.CaptionDrawn(line));
        Assert.Contains(line, rig.Menus.Floating.Up);           // its time still running
        rig.Menus.PopLayer();
        Assert.True(rig.Menus.CaptionDrawn(line));
        Assert.True(rig.Menus.CaptionDrawn(new Caption("chapter", FrontEnd.VersionFont, 0, 0, 0, 1000, 0, 0)));   // others as they were
    }

    [Fact]
    public void A_packages_notice_is_put_in_the_version_font_s_own_glyphs()
    {
        using var files = Install.Open();
        var rig = Open(files);
        var font = rig.Assets.Font(FrontEnd.VersionFont);
        string line = FrontEnd.InFont(font, "Vitomir Platform 0.9.0-dev (dev) \u00b7 Private beta \u2013 please do not distribute");
        Assert.All(line, c => Assert.True(c == ' ' || c <= 0xff && font[c].Width > 0, $"U+{(int)c:X4} has no glyph"));
        Assert.StartsWith("Vitomir Platform 0.9.0-dev (dev) ", line);
        Assert.EndsWith(" please do not distribute", line);
    }

    [Fact]
    public void A_button_acts_on_the_release_over_itself_and_not_after_a_drag_off_and_back()
    {
        using var files = Install.Open();
        var rig = Open(files);
        var menus = rig.Menus;
        menus.OpenMainMenu();
        var pane = menus.Pane(FrontEnd.MainMenuFile);
        var sp = pane.Element("mmSinglePlayerButton")!;
        (float x, float y) on = (sp.Pos[0] + 10, sp.Pos[1] + 10);

        // Pressed, dragged off, back on, released: lit again but not held, so nothing happens.
        menus.PointerAt(on.x, on.y);
        menus.Frame(0);
        menus.LeftHeld = true;
        menus.Press();
        Assert.Equal(ButtonState.Pressed, pane.Buttons["mmSinglePlayerButton"]);
        menus.PointerAt(20, 20);
        menus.Frame(10);
        Assert.Equal(ButtonState.Normal, pane.Buttons["mmSinglePlayerButton"]);
        menus.PointerAt(on.x, on.y);
        menus.Frame(20);
        Assert.Equal(ButtonState.Rollover, pane.Buttons["mmSinglePlayerButton"]);
        menus.LeftHeld = false;
        menus.Release();
        Assert.False(menus.Pane(FrontEnd.SpFile).Shown);
        // The press made the press sound and the release the release sound: the hook plays them
        // whatever the button then does.
        Assert.Equal([FoleyEvent.ButtonDepress, FoleyEvent.ButtonRelease], rig.Sounds);

        // A clean click opens the single-player menu in the main menu's place.
        Click(menus, FrontEnd.MainMenuFile, "mmSinglePlayerButton");
        Assert.True(menus.Pane(FrontEnd.SpFile).Shown);
        Assert.False(pane.Shown);
    }

    [Fact]
    public void New_with_an_autosave_there_warns_and_its_cancel_goes_back_to_the_main_menu()
    {
        using var files = Install.Open();
        var fresh = Open(files);
        fresh.Menus.OpenMainMenu();
        Click(fresh.Menus, FrontEnd.MainMenuFile, "mmSinglePlayerButton");
        Click(fresh.Menus, FrontEnd.SpFile, "spNewButton");
        Assert.Equal((MenuRequest.NewGame, 0), fresh.Menus.TakeRequest());

        var rig = Open(files, new Slots((0, "The Convent (00:00:00)")));
        var menus = rig.Menus;
        menus.OpenMainMenu();
        Click(menus, FrontEnd.MainMenuFile, "mmSinglePlayerButton");
        Click(menus, FrontEnd.SpFile, "spNewButton");
        var box = menus.Pane(FrontEnd.MessageBoxFile);
        Assert.True(box.Shown);
        Assert.Equal("Warning!", box.Texts["mbTitle"]);
        Assert.Equal("This operation will overwrite your previous autosave.  OK to continue?", box.Texts["mbTextBox"]);
        Assert.Same(box, menus.Active());
        Click(menus, FrontEnd.MessageBoxFile, "mbCancelButton");
        Assert.False(box.Shown);
        Assert.False(menus.Pane(FrontEnd.SpFile).Shown);
        Assert.True(menus.Pane(FrontEnd.MainMenuFile).Shown);
        Assert.Equal(MenuRequest.None, menus.TakeRequest().Request);

        Click(menus, FrontEnd.MainMenuFile, "mmSinglePlayerButton");
        Click(menus, FrontEnd.SpFile, "spNewButton");
        Key(menus, MenuKey.Enter);                      // OK, from the keyboard
        Assert.Equal(MenuRequest.NewGame, menus.TakeRequest().Request);
        Assert.False(menus.Pane(FrontEnd.SpFile).Shown);
    }

    [Fact]
    public void Escape_on_the_single_player_menu_is_its_main_menu_button()
    {
        using var files = Install.Open();
        var rig = Open(files);
        rig.Menus.OpenMainMenu();
        Click(rig.Menus, FrontEnd.MainMenuFile, "mmSinglePlayerButton");
        Key(rig.Menus, MenuKey.Escape);
        Assert.True(rig.Menus.Pane(FrontEnd.MainMenuFile).Shown);
        Assert.False(rig.Menus.Pane(FrontEnd.SpFile).Shown);
    }

    [Fact]
    public void The_credits_come_a_line_every_1200_ms_rise_at_20_a_second_and_give_the_theme_back()
    {
        using var files = Install.Open();
        var rig = Open(files);
        var menus = rig.Menus;
        menus.Frame(0);
        menus.OpenMainMenu();
        Click(menus, FrontEnd.MainMenuFile, "mmCreditsButton");
        Assert.Equal(["redemptiontheme", "da_boss_2"], rig.Music);
        Assert.Empty(menus.Floating.Up);                // the version line went with the clear

        // The first line once 1.2 s have passed, not before.
        menus.Frame(1200);
        Assert.Empty(menus.Floating.Up);
        menus.Frame(1216);
        var first = Assert.Single(menus.Floating.Up);
        Assert.Equal("Vampire: The Masquerade", first.Text);
        Assert.Equal(FrontEnd.CreditsTitleFont, first.Font);          // CREDITS_TITLE_0000
        Assert.Equal(20f, first.Rise);
        Assert.Equal(40, (int)first.Y);                               // on time: (16 − 0) × 0.02 + 40
        Assert.Equal(20000 - 1216, first.TotalMs);

        // The next 1.2 s after that; "Redemption" is a TITLE too, the <> after it only a pause.
        menus.Frame(2400);
        Assert.Single(menus.Floating.Up);
        menus.Frame(2417);
        Assert.Equal(2, menus.Floating.Up.Count);
        menus.Frame(3618);                                            // the <>: nothing drawn, 300 ms more
        Assert.Equal(2, menus.Floating.Up.Count);
        menus.Frame(4818);
        Assert.Equal(2, menus.Floating.Up.Count);
        menus.Frame(5119);
        Assert.Equal(3, menus.Floating.Up.Count);
        Assert.Equal("CREDITS", menus.Floating.Up[^1].Text);

        // Escape closes them: the lines go and the theme comes back from the layer below.
        Key(menus, MenuKey.Escape);
        Assert.Empty(menus.Floating.Up);
        Assert.False(menus.Pane(FrontEnd.CreditsPaneFile).Shown);
        Assert.Equal("redemptiontheme", rig.Music[^1]);
        Assert.True(menus.IsUp(menus.Pane(FrontEnd.MainMenuFile)));
    }

    [Fact]
    public void The_credits_file_is_read_in_its_own_order_and_stops_at_an_entry_with_no_text()
    {
        var lines = FrontEnd.ParseCredits("// head\r\n\r\nCREDITS_TITLE_0010\tCREDITS\r\n  # note\r\nCREDITS_SUBTL_0000\t<>\r\n"
                                         + "CREDITS_SUBTL_0000\tSomeone\r\nEMPTY\r\nAFTER\tnever\r\n");
        Assert.Equal([new CreditLine("CREDITS_TITLE_0010", "CREDITS"), new CreditLine("CREDITS_SUBTL_0000", "<>"),
                      new CreditLine("CREDITS_SUBTL_0000", "Someone")], lines);
    }

    [Fact]
    public void The_escape_menu_pauses_and_its_escape_key_is_resume_but_not_on_the_frame_it_opened()
    {
        using var files = Install.Open();
        var rig = Open(files, state: FrontEnd.StateGame);
        var menus = rig.Menus;
        bool effectsCleared = false;
        menus.EscapeShown = () => effectsCleared = true;
        menus.OpenEscape();
        Assert.True(menus.Paused);
        Assert.True(effectsCleared);
        var escape = menus.Pane(FrontEnd.EscapeFile);
        Assert.Same(escape, menus.Active());            // a dialog: it has the keys wherever the pointer is
        menus.Key(MenuKey.Escape);                      // the frame it opened: its key hook does not run
        Assert.True(escape.Shown);
        Key(menus, MenuKey.Escape);
        Assert.False(escape.Shown);
        Assert.False(menus.Paused);
        Assert.Empty(rig.Sounds);                       // the key that closes it makes no sound
    }

    [Fact]
    public void Save_from_the_escape_menu_opens_every_slot_but_the_autosave_with_slot_1_chosen()
    {
        using var files = Install.Open();
        var rig = Open(files, new Slots((0, "auto"), (3, "Prague's Old Town (01:02:03)")), FrontEnd.StateGame);
        var menus = rig.Menus;
        menus.OpenEscape();
        Click(menus, FrontEnd.EscapeFile, "emSaveButton");
        Assert.False(menus.Paused);                      // the button put the menu away first
        Assert.Equal(MenuRequest.None, menus.TakeRequest().Request);   // the screen opens at the frame's end
        var save = menus.Pane(FrontEnd.SaveFile);
        Assert.True(save.Shown);
        Assert.True(menus.Paused);
        var list = save.Lists["spSaveList"];
        Assert.Equal(29, list.Rows.Count);
        Assert.Equal("Slot 1\nUnused", list.Rows[0].Text);
        Assert.Equal("Slot 3\nPrague's Old Town (01:02:03)", list.Rows[2].Text);
        Assert.Equal(0, list.Current);
        Assert.Equal(1, list.CurrentData);
        Assert.False(menus.Pane(FrontEnd.EscapeFile).Shown);
    }

    [Fact]
    public void Saving_over_a_slot_warns_then_asks_a_name_and_an_empty_one_is_refused()
    {
        using var files = Install.Open();
        var rig = Open(files, new Slots((1, "old")), FrontEnd.StateGame);
        var menus = rig.Menus;
        menus.OpenSave();
        Click(menus, FrontEnd.SaveFile, "spSaveSaveButton");
        var box = menus.Pane(FrontEnd.MessageBoxFile);
        Assert.Equal("The previous save will be overwritten.  OK to continue?", box.Texts["mbTextBox"]);
        Click(menus, FrontEnd.MessageBoxFile, "mbOkButton");
        Assert.False(box.Shown);

        var input = menus.Pane(FrontEnd.InputBoxFile);
        Assert.True(input.Shown);
        Assert.Equal("Please enter a name for this save", input.Texts["ibTitle"]);
        var edit = input.Edits["ibEdit"];
        Assert.Equal("The Convent (00:24:25)", edit.Text);
        Assert.Equal(FrontEnd.NameLimit, edit.Max);
        Assert.Equal("ibedit", input.Focus, ignoreCase: true);

        // Emptied and OK'd: an error, and the save screen still up.
        foreach (var _ in edit.Text) menus.Type('\b');
        Click(menus, FrontEnd.InputBoxFile, "ibOkButton");
        Assert.True(box.Shown);
        Assert.Equal("Error", box.Texts["mbTitle"]);
        Assert.Equal("Please enter a valid game name.", box.Texts["mbTextBox"]);
        Assert.Contains("mbCancelButton", box.Hidden);
        Key(menus, MenuKey.Escape);                      // no Cancel on it, so Escape does nothing
        Assert.True(box.Shown);
        Key(menus, MenuKey.Enter);
        Assert.False(box.Shown);
        Assert.True(menus.Pane(FrontEnd.SaveFile).Shown);
        Assert.Equal(MenuRequest.None, menus.TakeRequest().Request);

        // Named and OK'd: the request, the name, "Game saved." first, and back to the game.
        Click(menus, FrontEnd.SaveFile, "spSaveSaveButton");
        Key(menus, MenuKey.Enter);                       // OK on the warning
        foreach (char c in "Before the bridge") menus.Type(c);
        Key(menus, MenuKey.Enter);
        Assert.Equal(["Game saved."], rig.Posted);
        Assert.Equal((MenuRequest.Save, 1), menus.TakeRequest());
        Assert.Equal("The Convent (00:24:25)Before the bridge", menus.SaveName);
        Assert.False(menus.Pane(FrontEnd.SaveFile).Shown);
        Assert.False(menus.Paused);
        Assert.False(menus.Up);
    }

    [Fact]
    public void The_load_screen_lists_only_the_saves_there_are_and_loads_with_no_question()
    {
        using var files = Install.Open();
        var none = Open(files, state: FrontEnd.StateGame);
        none.Menus.OpenLoad();
        Assert.Empty(none.Menus.Pane(FrontEnd.LoadFile).Lists["spLoadList"].Rows);
        Click(none.Menus, FrontEnd.LoadFile, "spLoadLoadButton");        // nothing chosen: nothing done
        Assert.True(none.Menus.Pane(FrontEnd.LoadFile).Shown);
        Assert.Equal(MenuRequest.None, none.Menus.TakeRequest().Request);

        var slots = new Slots((0, "The Convent (00:00:00)"), (7, "Smithy (00:10:00)"));
        slots.Saved[9] = null;                           // one that will not open
        var rig = Open(files, slots, FrontEnd.StateGame);
        var menus = rig.Menus;
        menus.OpenLoad();
        var list = menus.Pane(FrontEnd.LoadFile).Lists["spLoadList"];
        Assert.Equal(["Auto Save\nThe Convent (00:00:00)", "Slot 7\nSmithy (00:10:00)",
                      "Slot 9\n" + FrontEnd.Unreadable], list.Rows.Select(r => r.Text));
        Assert.Equal(0, list.Current);
        Assert.True(menus.Paused);

        // A press on a row chooses it; Enter loads it.
        var row = list.Rows[1];
        var e = list.Element;
        menus.PointerAt(e.Pos[0] + 20, e.Pos[1] + row.Y + row.Height / 2f);
        menus.Frame(0);
        menus.LeftHeld = true;
        menus.Press();
        menus.LeftHeld = false;
        menus.Release();
        Assert.Equal(1, list.Current);
        Key(menus, MenuKey.Enter);
        Assert.Equal((MenuRequest.Restore, 7), menus.TakeRequest());
        Assert.False(menus.Paused);
        Assert.False(menus.Up);
    }

    /// <summary>
    /// 1.1's own saves (ours): listed after Vitomir's, plainly marked as the original
    /// game's; Load on one asks first, Cancel leaves the list up and asks for nothing, and OK asks for
    /// the import of that slot.
    /// </summary>
    [Fact]
    public void A_1_1_save_is_listed_after_ours_and_imported_only_on_the_players_say_so()
    {
        using var files = Install.Open();
        var rig = Open(files, new Slots((3, "Smithy (00:10:00)")), FrontEnd.StateGame);
        var menus = rig.Menus;
        menus.OriginalSaves = () => [(4, "Monastery 1 (01:28:05)")];
        menus.OpenLoad();
        var list = menus.Pane(FrontEnd.LoadFile).Lists["spLoadList"];
        Assert.Equal(["Slot 3\nSmithy (00:10:00)", $"{FrontEnd.OriginalLabel} 4\nMonastery 1 (01:28:05)"], list.Rows.Select(r => r.Text));
        list.Select(1);

        Click(menus, FrontEnd.LoadFile, "spLoadLoadButton");
        var box = menus.Pane(FrontEnd.MessageBoxFile);
        Assert.True(box.Shown);
        Assert.Equal(MenuRequest.None, menus.TakeRequest().Request);
        Click(menus, FrontEnd.MessageBoxFile, "mbCancelButton");
        Assert.True(menus.Pane(FrontEnd.LoadFile).Shown);
        Assert.Equal(MenuRequest.None, menus.TakeRequest().Request);

        Click(menus, FrontEnd.LoadFile, "spLoadLoadButton");
        Click(menus, FrontEnd.MessageBoxFile, "mbOkButton");
        Assert.Equal((MenuRequest.Import, 4), menus.TakeRequest());
        Assert.False(menus.Paused);
        Assert.False(menus.Up);
    }

    /// <summary>The install's own saves, as the load list offers them: only listed, never written.</summary>
    [Fact]
    public void The_install_s_1_1_saves_are_listed_with_their_descriptions()
    {
        using var files = Install.Open();
        var saves = new Vitomir.Engine.Save.OriginalSaves(files.Root);
        Assert.SkipUnless(Directory.Exists(saves.Folder), "no SaveGames folder in the install");
        var listed = saves.List();
        Assert.SkipWhen(listed.Count == 0, "no 1.1 saves in the install");
        foreach (var (slot, description) in listed)
        {
            Assert.InRange(slot, 0, Vitomir.Engine.Save.OriginalSaves.Slots - 1);
            Assert.Equal(NsgSave.ReadFile(Path.Combine(saves.PathOf(slot), "!main.nsg")).Description, description);
        }
    }

    [Fact]
    public void Quit_on_the_escape_menu_asks_for_the_main_menu_without_a_question()
    {
        using var files = Install.Open();
        var rig = Open(files, state: FrontEnd.StateGame);
        rig.Menus.OpenEscape();
        Click(rig.Menus, FrontEnd.EscapeFile, "emQuitButton");
        Assert.False(rig.Menus.Pane(FrontEnd.MessageBoxFile).Shown);
        Assert.Equal(MenuRequest.MainMenu, rig.Menus.TakeRequest().Request);
        Assert.False(rig.Menus.CreditsOnMainMenu);
        Assert.False(rig.Menus.Paused);
    }

    [Fact]
    public void The_save_list_lays_two_line_rows_from_the_top_and_its_slider_starts_at_the_top_of_its_track()
    {
        using var files = Install.Open();
        var rig = Open(files, state: FrontEnd.StateGame);
        rig.Menus.OpenSave();
        var pane = rig.Menus.Pane(FrontEnd.SaveFile);
        var list = pane.Lists["spSaveList"];
        int lh = list.Font.LineHeight;                  // DefaultFont, 10
        Assert.Equal(10, lh);
        Assert.Equal(312 - 20, list.Rows[0].Y);          // against the top of a 312-high box
        Assert.Equal(list.Rows[0].Y - 6 - 20, list.Rows[1].Y);
        Assert.Equal(["Slot 1", "Unused"], list.Rows[0].Lines);
        // The first line of a row is the upper one.
        var lines = list.LinesOf(0).ToList();
        Assert.True(lines[0].Y > lines[1].Y);
        Assert.Equal(0, list.Scroll);
        int last = list.Rows[^1].Y;
        Assert.Equal(lh / 4 - last, list.MaxScroll);
        var bar = pane.Element("spSaveScrollBar")!;
        var slider = pane.Element("spSaveScrollSlider")!;
        Assert.Equal(105 + 314 - 8, list.SliderY(bar, slider));   // the top of the track
        list.Page(+1);
        Assert.Equal(312, list.Scroll);
        list.ScrollTo(int.MaxValue);
        Assert.Equal(105, list.SliderY(bar, slider));             // the bottom
        // Choosing a row below the box asks for a negative scroll and lands on the top instead.
        list.Select(20);
        Assert.Equal(0, list.Scroll);

        // A list that fits has its slider at the bottom.
        var fits = new Slots((0, "x"));
        var small = Open(files, fits, FrontEnd.StateGame);
        small.Menus.OpenLoad();
        var loadPane = small.Menus.Pane(FrontEnd.LoadFile);
        Assert.Equal(105, loadPane.Lists["spLoadList"].SliderY(loadPane.Element("spLoadScrollBar")!, loadPane.Element("spLoadScrollSlider")!));
    }

    [Fact]
    public void A_text_box_breaks_after_the_last_word_that_fits_and_drops_the_spaces_at_the_break()
    {
        Func<char, int> six = _ => 6;
        Assert.Equal(["abc", "def gh"], TextBoxText.Lines("abc def gh", true, 36, six));
        Assert.Equal(["abcdef", "gh"], TextBoxText.Lines("abcdefgh", true, 36, six));    // no space: cut before the overflow
        Assert.Equal(["one", "two"], TextBoxText.Lines("one\n\ntwo", false, 1000, six));   // strtok: no empty lines
        Assert.Equal(["abc", "def"], TextBoxText.Lines("abc    def", true, 30, six));
    }

    [Fact]
    public void The_name_field_takes_typing_up_to_its_limit_and_its_caret_blinks_every_half_second()
    {
        using var files = Install.Open();
        var rig = Open(files, state: FrontEnd.StateGame);
        rig.Menus.Input("t", "", 5);
        var edit = rig.Menus.Pane(FrontEnd.InputBoxFile).Edits["ibEdit"];
        foreach (char c in "abcdefg") rig.Menus.Type(c);
        Assert.Equal("abcde", edit.Text);
        rig.Menus.Type('\u0007');                          // control characters are refused
        rig.Menus.Type('\b');
        Assert.Equal("abcd", edit.Text);
        rig.Menus.Frame(1);
        bool on = edit.CaretOn;
        rig.Menus.Frame(300);
        Assert.Equal(on, edit.CaretOn);
        rig.Menus.Frame(502);
        Assert.NotEqual(on, edit.CaretOn);
        Assert.Equal(edit.CaretOn ? "abcd|" : "abcd", edit.Shown);
    }
}
