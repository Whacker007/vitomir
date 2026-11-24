// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Every word on the menus in our faces** (<see cref="MenuText"/>; <i>"let's go ahead and
/// replace all of the menu items with our new font so it looks consistent"</i>): names in Vitomir Uncial,
/// sentences in Vitomir Book, our faces holding what 1.1's menus write (figures, marks, accented letters); the
/// Display, Advanced Display and Sound screens in our lettering; and the menus' trail a tape's check compares.
/// </summary>
public class MenuTextTests
{
    // ---- the faces ------------------------------------------------------------------------------------------------

    /// <summary>What a menu writes: every printable ASCII character, and Latin-1's letters (a save's or a player's name in French, German, Spanish or Italian).</summary>
    static readonly string Printable = string.Concat(Enumerable.Range(0x21, 0x7f - 0x21).Select(c => (char)c));
    static readonly string Latin1Letters = "àáâãäåçèéêëìíîïñòóôõöùúûüýÿÀÁÂÃÄÅÇÈÉÊËÌÍÎÏÑÒÓÔÕÖÙÚÛÜÝØø";

    [Fact]
    public void A_name_s_faces_and_the_book_face_have_every_character_a_menu_writes()
    {
        var uncial = Lettering.Load(Lettering.Uncial);
        var capitals = Lettering.Load(Lettering.Capitals);
        var book = Lettering.Load(Lettering.Book);
        foreach (char c in Printable + Latin1Letters)
        {
            var nameFace = char.IsUpper(c) ? capitals : uncial;
            Assert.True(nameFace[c] is not null, $"a name's face ({nameFace.Name}) has no '{c}'");
            Assert.True(book[c] is not null, $"the book face has no '{c}'");
        }
        // Figures are lining, as 1.1's menus write theirs: every one the same width and standing on the baseline.
        var widths = "0123456789".Select(c => uncial[c]!.Value.Advance).Distinct().ToList();
        Assert.Single(widths);
        Assert.All("0123456789", c => Assert.InRange(uncial[c]!.Value.InkBottom, -0.03f, 0.03f));
    }

    [Fact]
    public void A_letter_no_face_has_is_written_as_its_plain_letters()
    {
        var uncial = Lettering.Load(Lettering.Uncial);
        Assert.Equal("æ ‘quoted’ – well", MenuText.Spelled(uncial, "æ ‘quoted’ – well"));   // it has these: kept
        Assert.Equal("a", MenuText.Spelled(uncial, "ā"));                   // ā: no face has it, its letter has
        Assert.Equal("?", MenuText.Spelled(uncial, "中"));                    // nothing like it: a question mark
        // A name's capital it lacks is its letter in the capitals' plain form (Æ as AE).
        var runs = MenuText.Runs(MenuText.Kind.Name, "Æsir", 10f).ToList();
        Assert.Equal("AE", runs[0].Text);
        Assert.Equal(Lettering.Capitals, runs[0].Face.Name);
    }

    [Fact]
    public void A_box_that_wraps_is_a_sentence_in_the_book_face_and_everything_else_a_name()
    {
        NuiElement Box(string attrs) => NuiPane.Parse($"name=t\nelement textbox name=b pos=0,0 size=100,20 {attrs}\n", "t.nui").Elements[0];
        Assert.Equal(MenuText.Kind.Sentence, MenuText.KindOf(Box("textboxflags=0x04")));
        Assert.Equal(MenuText.Kind.Name, MenuText.KindOf(Box("textboxflags=0x02 maxitems=1")));
        Assert.Equal(MenuText.Kind.Sentence, MenuText.KindOf(Box("textboxflags=0x02 face=sentence")));
        Assert.Equal(MenuText.Kind.Name, MenuText.KindOf(Box("textboxflags=0x04 face=name")));
        Assert.Equal(Lettering.Book, Assert.Single(MenuText.Runs(MenuText.Kind.Sentence, "A Hint.", 10f)).Face.Name);
        Assert.Equal([Lettering.Capitals, Lettering.Uncial], MenuText.Runs(MenuText.Kind.Name, "Body collision", 10f).Select(r => r.Face.Name));
        // A sentence's lines are broken as 1.1 breaks them, measured in the book face.
        var lines = MenuText.Lines(MenuText.Kind.Sentence, "A game with these switches is faithful to 1.1's rules.", true, 120f, MenuText.SentenceEm);
        Assert.True(lines.Count >= 2);
        Assert.All(lines, l => Assert.True(MenuText.Measure(MenuText.Kind.Sentence, l.TrimEnd(), MenuText.SentenceEm) <= 120.5f, l));
    }

    // ---- the Display, Advanced Display and Sound screens ----------------------------------------------------------

    sealed record Rig(FrontEnd Menus, OptionsMenu Options, UiAssets Assets);

    static Rig Open(GameFiles files)
    {
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find)
        {
            State = FrontEnd.StateFrontEnd, Dress = (nui, file) => MenuLettering.Dress(assets, nui, file),
        };
        var ini = Path.Combine(Path.GetTempPath(), $"vitomir-menutext-{Guid.NewGuid():N}.ini");
        var screen = new OptionsMenu(menus, text.Find, new GameOptions()) { SavePath = ini };
        screen.Fixes.SavePath = ini;
        screen.Keys.SavePath = ini;
        menus.Options = screen;
        return new Rig(menus, screen, assets);
    }

    static void Click(FrontEnd menus, MenuPane pane, string control)
    {
        var e = pane.Element(control)!;
        var (x, y) = pane.At(e);
        menus.PointerAt(x + e.Size[0] / 2f, y + e.Size[1] / 2f);
        menus.Frame(menus.NowMs);
        menus.LeftHeld = true;
        menus.Press();
        menus.LeftHeld = false;
        menus.Release();
        menus.Frame(menus.NowMs);
        menus.Drawn();
    }

    static List<string> Texts(MenuPane pane) =>
        [.. pane.Nui.Elements.Where(e => e.Type == MenuLettering.WordsType).Select(e => MenuLettering.Read(e).Text)];

    /// <summary>1.1's gold left in a label's box once the screen is dressed: its words are taken off (a few of the tiles' own specks may stay).</summary>
    static int GoldLeft(UiAssets assets, NuiPane dressed, MenuLettering.Label l)
    {
        var art = LiftedArt.Of(assets, dressed);
        var mask = art.Mask();
        art.MarkGold(mask, l.Top, l.Bottom - 1, l.Left, l.Right);
        return mask.Count(m => m);
    }

    [Theory]
    [InlineData(OptionsMenu.DisplayFile)]
    [InlineData(OptionsMenu.AdvancedFile)]
    [InlineData(OptionsMenu.SoundFile)]
    public void Each_of_the_three_screens_has_every_word_of_1_1_s_in_ours_and_none_left_on_its_art(string file)
    {
        using var files = Install.Open();
        var rig = Open(files);
        rig.Options.Open();
        if (file == OptionsMenu.DisplayFile) rig.Options.OpenDisplay();
        else if (file == OptionsMenu.AdvancedFile) { rig.Options.OpenDisplay(); rig.Options.OpenAdvanced(); }
        else rig.Options.OpenSound();
        var pane = rig.Menus.Pane(file);
        Assert.True(pane.Lettered);
        var labels = file == OptionsMenu.DisplayFile ? MenuLettering.DisplayLabels
                   : file == OptionsMenu.AdvancedFile ? MenuLettering.AdvancedLabels : MenuLettering.SoundLabels;
        var texts = Texts(pane);
        Assert.All(labels, l => Assert.Contains(l.Text, texts));
        var raw = rig.Assets.Pane(file);
        foreach (var l in labels)
        {
            int before = GoldLeft(rig.Assets, raw, l), after = GoldLeft(rig.Assets, pane.Nui, l);
            Assert.True(before > 8, $"1.1's \"{l.Text}\" is gold in its box ({before})");
            Assert.True(after <= before / 10, $"\"{l.Text}\": {after} of {before} gold pixels left");
        }
        // No button keeps a picture with 1.1's word on it, and each has a word of ours that follows it.
        foreach (var b in pane.Nui.Elements.Where(e => e.Type == "button" && (e.Name.EndsWith("okbutton", StringComparison.OrdinalIgnoreCase)
                                                                               || e.Name.EndsWith("cancelbutton", StringComparison.OrdinalIgnoreCase)
                                                                               || e.Name.EndsWith("exitbutton", StringComparison.OrdinalIgnoreCase)
                                                                               || e.Name.EndsWith("advancedoptionsbutton", StringComparison.OrdinalIgnoreCase))))
        {
            Assert.Null(b.Get("rollover"));
            Assert.Contains(b.Name, pane.Worded);
        }
    }

    [Fact]
    public void The_display_screen_hides_the_inert_detail_level_and_advanced_options_and_sound_s_exit_puts_it_away()
    {
        using var files = Install.Open();
        var rig = Open(files);
        rig.Options.Open();
        rig.Options.OpenDisplay();
        var display = rig.Menus.Pane(OptionsMenu.DisplayFile);
        // Still in the .nui (data), but off the screen: not drawn, not under the pointer (inert by decision).
        foreach (var name in OptionsMenu.InertDisplayControls)
        {
            var e = display.Element(name)!;
            Assert.Contains(name, display.Hidden);
            var (x, y) = display.At(e);
            Assert.Null(display.ControlAt(x + e.Size[0] / 2f, y + e.Size[1] / 2f));
        }
        var label = Assert.Single(display.Nui.Elements, e => e.Type == MenuLettering.WordsType && MenuLettering.Read(e).Text == "detail level");
        Assert.Contains(label.Name, display.Hidden);
        Assert.DoesNotContain(display.Nui.Elements, e => e.Type == MenuLettering.WordsType && e.Get("button") is null
                                                          && e.Name != label.Name && display.Hidden.Contains(e.Name));
        Click(rig.Menus, display, "opDisAdvancedOptionsButton");
        Assert.False(rig.Menus.Pane(OptionsMenu.AdvancedFile).Shown);
        Assert.True(rig.Menus.IsUp(display));
        Click(rig.Menus, display, "opDisCancelButton");
        rig.Options.OpenSound();
        var sound = rig.Menus.Pane(OptionsMenu.SoundFile);
        Assert.True(sound.Shown);
        Click(rig.Menus, sound, "opsoExitButton");
        Assert.False(sound.Shown);
    }

    // ---- the menus' trail -----------------------------------------------------------------------------------------

    [Fact]
    public void The_front_end_says_each_step_through_the_menus_and_a_trail_finds_the_first_that_differs()
    {
        using var files = Install.Open();
        var rig = Open(files);
        var steps = new List<string>();
        rig.Menus.Trail = steps.Add;
        rig.Options.Open();
        var options = rig.Menus.Pane(OptionsMenu.OptionsFile);
        Click(rig.Menus, options, "opgameplaybutton");
        var gameplay = rig.Menus.Pane(OptionsMenu.GameplayFile);
        Click(rig.Menus, gameplay, AlwaysRunOption.Box);
        Click(rig.Menus, gameplay, "opgpcancelbutton");
        Assert.Equal(["show Options", "click Options opgameplaybutton", "show OptionsGameplay",
                      $"check OptionsGameplay {AlwaysRunOption.Box} 1", "click OptionsGameplay opgpcancelbutton", "hide OptionsGameplay"], steps);

        // Written in a digest, read back, and compared step by step.
        var path = Path.Combine(Path.GetTempPath(), $"vitomir-trail-{Guid.NewGuid():N}.digest");
        try
        {
            using (var w = new DigestWriter(new StreamWriter(path)))
                for (int i = 0; i < steps.Count; i++) w.WriteMenu(10 + i, steps[i]);
            var same = MenuTrail.Read(path)!;
            for (int i = 0; i < steps.Count; i++) Assert.Null(same.Check(10 + i, steps[i]));
            Assert.False(same.RecordedLeft);
            Assert.Equal(steps.Count, same.Agreed);

            var other = MenuTrail.Read(path)!;
            Assert.Null(other.Check(10, steps[0]));
            var first = other.Check(11, "click Options opcontrolsbutton");
            Assert.NotNull(first);
            Assert.Equal("the menus", first!.Value.What);
            Assert.Equal(1, other.Agreed);

            // A digest from before the trail (version 2) has none, and nothing of the menus is compared.
            File.WriteAllText(path, "vitomir-digest 2\nf 1 clock=5\n");
            Assert.Null(MenuTrail.Read(path));
        }
        finally { File.Delete(path); }
    }
}
