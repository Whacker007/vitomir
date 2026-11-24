// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Key remapping: the defaults are 1.1's own keys and must
/// reproduce 1.1's reading of them exactly — the letter then the shift for R, the four modifier
/// flags of the click from Shift, Ctrl, Alt and Space — and what Vitomir did with its own driving
/// keys before they could move; the <c>[keys]</c> section reads back what it writes; and the key
/// page binds, asks before taking a key from another command, and writes on OK only.
/// </summary>
public class KeyBindingTests
{
    static bool Pressed(KeyBindings keys, int input, KeyMods held, bool driving, KeyCommand expected) =>
        keys.Pressed(input, held, driving).Any(p => p.Command == expected);

    [Fact]
    public void The_defaults_are_the_keys_of_1_1_and_escape_is_nobodys()
    {
        var keys = new KeyBindings();
        Assert.True(keys.AreDefaults);
        Assert.Equal([KeyChord.Mouse(1)], keys[KeyCommand.Act]);
        Assert.Equal([KeyChord.Mouse(3)], keys[KeyCommand.Cast]);
        Assert.Equal([KeyChord.Mouse(2)], keys[KeyCommand.SecondaryClick]);   // 1.1's third binding, the middle button
        Assert.Equal([new KeyChord(KeyNames.Letter('R'), KeyMods.Shift)], keys[KeyCommand.Ungroup]);
        Assert.Equal([new KeyChord(KeyNames.Letter('X')), new KeyChord(KeyNames.Keypad(5)), KeyChord.Mouse(5)], keys[KeyCommand.Swivel]);
        Assert.Equal([new KeyChord(KeyNames.Letter('Z')), KeyChord.Mouse(4)], keys[KeyCommand.FirstPerson]);
        Assert.Equal(new KeyChord(KeyNames.F(1)), keys[KeyCommand.Quick1][0]);
        Assert.Equal(new KeyChord(KeyNames.F(9)), keys[KeyCommand.Character4][0]);
        Assert.Equal(new KeyChord(KeyNames.Digit(6)), keys[KeyCommand.Discipline6][0]);
        Assert.Empty(keys.BoundTo(new KeyChord(KeyNames.Escape)));
        // Every command is on the key page, once, and the list is the groups' headings and commands in order.
        var listed = KeysPage.Groups.SelectMany(g => g.Commands).ToList();
        Assert.Equal(Enum.GetValues<KeyCommand>().OrderBy(c => c), listed.OrderBy(c => c));
        Assert.Equal(listed, KeysPage.Rows.Where(r => r.Command is not null).Select(r => r.Command!.Value));
        Assert.Equal(KeysPage.Groups.Length, KeysPage.Rows.Count(r => r.Command is null));
    }

    [Fact]
    public void R_is_regroup_shift_R_ungroup_and_ctrl_R_still_regroup_as_1_1_tests_the_letter_then_the_shift()
    {
        var keys = new KeyBindings();
        int r = KeyNames.Letter('R');
        Assert.Equal([KeyCommand.Regroup], keys.Pressed(r, KeyMods.None, false).Select(p => p.Command));
        Assert.Equal([KeyCommand.Ungroup], keys.Pressed(r, KeyMods.Shift, false).Select(p => p.Command));
        Assert.Equal([KeyCommand.Regroup], keys.Pressed(r, KeyMods.Ctrl, false).Select(p => p.Command));
        // F1 with Ctrl held is still F1 — the harness's Ctrl+F1 reads the Ctrl it is handed on with.
        Assert.Equal([KeyCommand.Quick1], keys.Pressed(KeyNames.F(1), KeyMods.Ctrl, false).Select(p => p.Command));
    }

    [Fact]
    public void The_mask_from_the_default_keys_is_shift_ctrl_alt_and_space()
    {
        var keys = new KeyBindings();
        InputModifier Mask(params int[] held) => keys.Mask(i => held.Contains(i), KeyMods.None);
        Assert.Equal(InputModifier.None, Mask());
        Assert.Equal(InputModifier.Shift, Mask(KeyNames.RShift));
        Assert.Equal(InputModifier.Ctrl, Mask(KeyNames.LCtrl));
        Assert.Equal(InputModifier.Alt, Mask(KeyNames.RAlt));
        Assert.Equal(InputModifier.Space, Mask(KeyNames.Space));
        Assert.Equal(InputModifier.Shift | InputModifier.Ctrl | InputModifier.Space, Mask(KeyNames.LShift, KeyNames.RCtrl, KeyNames.Space));
        // Moved: the hold flag on V, and Space is nothing of the kind any more.
        keys.Set(KeyCommand.Hold, [new KeyChord(KeyNames.Letter('V'))]);
        Assert.Equal(InputModifier.None, Mask(KeyNames.Space));
        Assert.Equal(InputModifier.Space, Mask(KeyNames.Letter('V')));
    }

    [Fact]
    public void Driving_keeps_A_N_E_and_D_off_until_the_driving_keys_move()
    {
        var keys = new KeyBindings();
        int a = KeyNames.Letter('A'), n = KeyNames.Letter('N'), e = KeyNames.Letter('E'), d = KeyNames.Letter('D');
        // Driving off: 1.1's keys, all four.
        Assert.True(Pressed(keys, a, KeyMods.None, false, KeyCommand.StanceAttack));
        Assert.True(Pressed(keys, n, KeyMods.None, false, KeyCommand.StanceNeutral));
        Assert.True(Pressed(keys, e, KeyMods.None, false, KeyCommand.StanceDefense));
        Assert.True(Pressed(keys, d, KeyMods.None, false, KeyCommand.Disciplines));
        // Driving on: A and D are side-steps, and the three stances go together.
        Assert.Empty(keys.Pressed(a, KeyMods.None, true));
        Assert.Empty(keys.Pressed(n, KeyMods.None, true));
        Assert.Empty(keys.Pressed(e, KeyMods.None, true));
        Assert.Empty(keys.Pressed(d, KeyMods.None, true));
        // The side-steps moved to J and L: all four are 1.1's again with driving on.
        keys.Set(KeyCommand.StepLeft, [new KeyChord(KeyNames.Letter('J'))]);
        keys.Set(KeyCommand.StepRight, [new KeyChord(KeyNames.Letter('L'))]);
        Assert.True(Pressed(keys, a, KeyMods.None, true, KeyCommand.StanceAttack));
        Assert.True(Pressed(keys, n, KeyMods.None, true, KeyCommand.StanceNeutral));
        Assert.True(Pressed(keys, d, KeyMods.None, true, KeyCommand.Disciplines));
        Assert.Empty(keys.Pressed(KeyNames.Letter('J'), KeyMods.None, true));
    }

    [Fact]
    public void A_key_bound_to_act_is_a_click_and_the_stance_moved_to_K_leaves_A_to_nobody()
    {
        var keys = new KeyBindings();
        keys.Set(KeyCommand.Act, [KeyChord.Mouse(1), new KeyChord(KeyNames.Letter('T'))]);
        keys.Set(KeyCommand.StanceAttack, [new KeyChord(KeyNames.Letter('K'))]);
        Assert.Equal([KeyCommand.Act], keys.Pressed(KeyNames.Letter('T'), KeyMods.Shift, false).Select(p => p.Command));
        Assert.Equal([KeyCommand.StanceAttack], keys.Pressed(KeyNames.Letter('K'), KeyMods.None, false).Select(p => p.Command));
        Assert.Empty(keys.Pressed(KeyNames.Letter('A'), KeyMods.None, false));
        Assert.True(KeyBindings.AnyDefault(KeyNames.Letter('A')));    // so the host swallows it rather than passing it on
        Assert.False(KeyBindings.AnyDefault(KeyNames.Letter('T')));   // and T, unbound, would have been the harness's
    }

    [Fact]
    public void Every_named_key_reads_back_from_its_name_long_or_short()
    {
        var inputs = Enumerable.Range(1, 300).Concat(Enumerable.Range(KeyChord.MouseBase + 1, 5));
        foreach (int input in inputs)
            foreach (var mods in new[] { KeyMods.None, KeyMods.Shift, KeyMods.Ctrl | KeyMods.Alt })
            {
                var chord = new KeyChord(input, mods);
                Assert.Equal(chord, KeyChord.Parse(chord.ToString()));
                Assert.Equal(chord, KeyChord.Parse(chord.Short));
            }
        Assert.Equal(new KeyChord(KeyNames.KeypadPlus, KeyMods.Shift), KeyChord.Parse("shift+keypad +"));
        Assert.Null(KeyChord.Parse("Hyper+Q"));
    }

    [Fact]
    public void The_keys_section_reads_back_what_it_writes_and_a_bad_line_keeps_the_default()
    {
        var keys = new KeyBindings();
        keys.Set(KeyCommand.Map, [new KeyChord(KeyNames.Tab, KeyMods.Ctrl), KeyChord.Mouse(4)]);
        keys.Set(KeyCommand.Swivel, []);
        string text = SettingsFile.Apply("; mine\n", KeyBindings.Section, keys.Values());
        Assert.Contains("map = Ctrl+Tab, Mouse 4", text);
        Assert.Contains("swivel = none", text);
        var read = new KeyBindings();
        read.Apply(IniFile.Parse(text));
        foreach (var info in KeyBindings.All) Assert.Equal(keys[info.Command], read[info.Command]);

        var complaints = new List<string>();
        var bad = new KeyBindings();
        bad.Apply(IniFile.Parse("[keys]\nregroup = Hyper+R\nnosuch = Q\nquests = J\n"), complaints.Add);
        Assert.Equal(KeyBindings.Of(KeyCommand.Regroup).Defaults, bad[KeyCommand.Regroup]);
        Assert.Equal([new KeyChord(KeyNames.Letter('J'))], bad[KeyCommand.Quests]);
        Assert.Equal(2, complaints.Count);
    }

    sealed record Rig(FrontEnd Menus, OptionsMenu Options, string Ini);

    static Rig Open(GameFiles files)
    {
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find);
        var ini = Path.Combine(Path.GetTempPath(), $"vitomir-keys-{Guid.NewGuid():N}.ini");
        var screen = new OptionsMenu(menus, text.Find, new GameOptions()) { SavePath = ini };
        screen.Keys.SavePath = ini;
        menus.Options = screen;
        return new Rig(menus, screen, ini);
    }

    static void Click(FrontEnd menus, MenuPane pane, string control)
    {
        var e = pane.Element(control)!;
        var (x, y) = pane.At(e);                                         // where it is drawn: a scrolled row is lifted
        menus.PointerAt(x + e.Size[0] / 2f, y + e.Size[1] / 2f);
        menus.Frame(menus.NowMs);
        menus.LeftHeld = true;
        menus.Press();
        menus.LeftHeld = false;
        menus.Release();
        menus.Frame(menus.NowMs);
        menus.Drawn();
    }

    [Fact]
    public void In_1_1_s_lettering_the_controls_screen_has_a_keys_button_beside_cancel_and_it_opens_the_key_page()
    {
        using var files = Install.Open();
        var rig = Open(files);
        rig.Options.Open();
        rig.Options.OpenControls();
        var controls = rig.Menus.Pane(OptionsMenu.ControlsFile);
        Assert.Equal("keys", controls.Texts[OptionsMenu.KeysButton]);                    // lower case, as 1.1's own words
        Click(rig.Menus, controls, OptionsMenu.KeysButton);
        var page = rig.Options.Keys.Pane;
        Assert.True(rig.Menus.IsUp(page));
        Assert.Equal("The Mouse", page.Texts[KeysPage.Label(0)]);                        // a heading, then the commands
        Assert.Equal("Act", page.Texts[KeysPage.Label(1)]);
        Assert.Equal("L Click", page.Texts[KeysPage.Cell(1, 0)]);
        Assert.Equal(KeysPage.Empty, page.Texts[KeysPage.Cell(1, 1)]);
        rig.Menus.Key(MenuKey.Escape);
        Assert.False(rig.Menus.IsUp(page));
        Assert.True(rig.Menus.IsUp(controls));
        File.Delete(rig.Ini);
    }

    [Fact]
    public void The_key_page_binds_the_next_key_asks_before_taking_one_and_writes_keys_on_ok()
    {
        using var files = Install.Open();
        var rig = Open(files);
        KeyBindings? got = null;
        rig.Options.Keys.Changed += k => got = k;
        rig.Options.Open();
        var keys = rig.Options.Keys;
        keys.Open();
        var page = keys.Pane;
        int act = KeysPage.RowOf(KeyCommand.Act), cast = KeysPage.RowOf(KeyCommand.Cast);

        // Act's second cell: T.
        Click(rig.Menus, page, KeysPage.Cell(act, 1));
        Assert.True(keys.Capturing);
        Assert.Equal(KeysPage.Waiting, page.Texts[KeysPage.Cell(act, 1)]);
        Assert.Equal(KeysPage.Prompt, page.Texts["kbprompt"]);                           // the line at the foot of the hint box
        keys.Capture(new KeyChord(KeyNames.Letter('T')));
        Assert.False(keys.Capturing);
        Assert.Equal("T", page.Texts[KeysPage.Cell(act, 1)]);
        Assert.Equal("", page.Texts["kbprompt"]);

        // The waiting cell clicked again: left as it was.
        Click(rig.Menus, page, KeysPage.Cell(cast, 0));
        Assert.True(keys.Capturing);
        Click(rig.Menus, page, KeysPage.Cell(cast, 0));
        Assert.False(keys.Capturing);
        Assert.Equal([KeyChord.Mouse(3)], keys.Editing[KeyCommand.Cast]);

        // Cast's first cell: Space, which is hold's and skip line's — asked, and OK moves it.
        Click(rig.Menus, page, KeysPage.Cell(cast, 0));
        keys.Capture(new KeyChord(KeyNames.Space));
        var box = rig.Menus.Pane(FrontEnd.MessageBoxFile);
        Assert.True(rig.Menus.IsUp(box));
        rig.Menus.Drawn();
        rig.Menus.Key(MenuKey.Enter);
        Assert.Equal([new KeyChord(KeyNames.Space)], keys.Editing[KeyCommand.Cast]);
        Assert.Empty(keys.Editing[KeyCommand.Hold]);
        Assert.Empty(keys.Editing[KeyCommand.SkipLine]);

        Click(rig.Menus, page, KeysPage.OkButton);
        Assert.False(rig.Menus.IsUp(page));
        Assert.Equal([KeyChord.Mouse(1), new KeyChord(KeyNames.Letter('T'))], got![KeyCommand.Act]);
        var ini = IniFile.Read(rig.Ini);
        Assert.Equal("Left Click, T", ini["keys.act"]);
        Assert.Equal("none", ini["keys.hold"]);
        Assert.Equal("Shift+R", ini["keys.ungroup"]);
        Assert.True(KeyBindings.Current.AreDefaults);   // the page never installs them; the host does
        File.Delete(rig.Ini);
    }

    /// <summary>
    /// **Escape while a cell waits unmaps it** (<i>"have a tooltip at the very bottom underneath
    /// the normal key tooltip say 'press esc to unmap'"</i>) — the clear button gone — and a command further down the
    /// list is reached by scrolling; defaults puts 1.1's keys back and Cancel keeps nothing.
    /// </summary>
    [Fact]
    public void Escape_unmaps_the_waiting_cell_defaults_puts_1_1_back_and_cancel_keeps_nothing()
    {
        using var files = Install.Open();
        var rig = Open(files);
        bool changed = false;
        rig.Options.Keys.Changed += _ => changed = true;
        rig.Options.Open();
        var keys = rig.Options.Keys;
        keys.Open();
        var page = keys.Pane;
        int row = KeysPage.RowOf(KeyCommand.Regroup);
        Assert.Contains(KeysPage.Cell(row, 0), page.Hidden);                               // below the box at the top of the list
        keys.Reveal(KeyCommand.Regroup);
        Assert.True(keys.Scroll > 0);
        Assert.DoesNotContain(KeysPage.Cell(row, 0), page.Hidden);
        Assert.Equal("Regroup", page.Texts[KeysPage.Label(row)]);
        Click(rig.Menus, page, KeysPage.Cell(row, 0));
        Assert.True(keys.Capturing);
        keys.Capture(new KeyChord(KeyNames.Escape));
        Assert.False(keys.Capturing);
        Assert.Empty(keys.Editing[KeyCommand.Regroup]);
        Assert.Equal(KeysPage.Empty, page.Texts[KeysPage.Cell(row, 0)]);
        Click(rig.Menus, page, KeysPage.DefaultsButton);
        Assert.True(keys.Editing.AreDefaults);
        Click(rig.Menus, page, KeysPage.CancelButton);
        Assert.False(changed);
        Assert.False(File.Exists(rig.Ini));
    }

    [Fact]
    public void The_key_list_scrolls_to_its_last_command_with_room_over_each_heading()
    {
        Assert.Equal(KeysPage.TopY, KeysPage.RowY(0));
        Assert.Equal(KeysPage.TopY - KeysPage.RowH, KeysPage.RowY(1));                     // no room over the first heading's row
        int second = Array.FindIndex(KeysPage.Rows, 1, r => r.Command is null);
        Assert.Equal(KeysPage.RowY(second - 1) - KeysPage.RowH - KeysPage.HeadingGap, KeysPage.RowY(second));
        int end = KeysPage.ScrollEnd;
        Assert.True(KeysPage.Fits(KeysPage.Rows.Length - 1, end));
        Assert.False(KeysPage.Fits(KeysPage.Rows.Length - 1, end - 1));
        Assert.All(Enumerable.Range(0, end + 1), s => Assert.True(KeysPage.ShownAt(s) >= 10));
    }

    [Fact]
    public void A_commands_description_is_shown_without_the_notes_for_the_codes_readers()
    {
        Assert.Equal("Turn the camera left.", KeysPage.Described("Turn the camera left (ours, KeyBindings.KeypadZoom)."));
        Assert.Equal("Act on what is under the pointer: walk, attack, talk, pick up, use. Held, it keeps swinging.",
                     KeysPage.Described(KeyBindings.Of(KeyCommand.Act).What));
        Assert.All(KeyBindings.All, i =>
        {
            string shown = KeysPage.Described(i.What);
            Assert.DoesNotContain("0x", shown);
            Assert.DoesNotContain("KeyBindings.", shown);
            Assert.False(string.IsNullOrWhiteSpace(shown));
        });
    }
}
