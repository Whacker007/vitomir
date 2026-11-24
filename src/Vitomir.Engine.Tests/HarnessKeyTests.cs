// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The harness's keys and the modifiers a handed-on key keeps (`HarnessKeys`, `KeyHandOn`): two of
/// the things the sweep of every bindable command found reading beside a binding. The harness's F1–F5
/// answered without Ctrl whenever the quick-slot handler let them by (F2 in a cutscene, a quick item
/// moved to J, a quick item used through a shop), F4 toggled the bar, F10 opened the vault and Ctrl+F1–F5
/// were the harness's for anybody — 1.1 uses a quick item on F1–F5 whatever is held;
/// the vault has no key at all (the vault opens through the haven's box; the console's `vault`);
/// and Shift with a moved regroup — or Shift+R after ungroup had moved — was read as ungroup, because
/// the handler tests 1.1's Shift on R.
/// </summary>
public class HarnessKeyTests
{
    static readonly KeyMods[] AllMods =
        [KeyMods.None, KeyMods.Shift, KeyMods.Ctrl, KeyMods.Alt, KeyMods.Shift | KeyMods.Ctrl, KeyMods.Shift | KeyMods.Alt,
         KeyMods.Ctrl | KeyMods.Alt, KeyMods.Shift | KeyMods.Ctrl | KeyMods.Alt];

    [Fact]
    public void No_harness_key_takes_a_key_of_1_1s()
    {
        foreach (var key in HarnessKeys.All)
        {
            Assert.True(key.DebugOnly, key.Name);           // a player's keyboard is 1.1's alone
            Assert.True((key.Chord.Mods & KeyMods.Ctrl) != 0 || !KeyBindings.AnyDefault(key.Chord.Input), key.Name);
        }
        Assert.DoesNotContain(HarnessKeys.All, k => k.Chord.Input == KeyNames.F(4));   // the bar's old alias is gone
    }

    [Fact]
    public void The_vault_is_on_no_key_and_F10_is_nobodys()
    {
        // By design: the vault opens only through the haven's box; the developer's way in is 1.1's own
        // console command `vault`. F10 is no key of 1.1's (DIK_F10's entry, read nowhere).
        Assert.DoesNotContain(HarnessKeys.All, k => k.Chord.Input == KeyNames.F(10));
        Assert.DoesNotContain(HarnessKeys.All, k => k.Name.Contains("vault"));
        Assert.False(Enum.IsDefined(typeof(HarnessKey), "Vault"));
        foreach (var mods in AllMods)
            foreach (var debug in new[] { false, true })
            {
                Assert.False(HarnessKeys.Any(KeyNames.F(10), mods, debug, repeat: false));
                Assert.False(HarnessKeys.PassesPane(KeyNames.F(10), mods, debug, repeat: false));   // the vault closes on Escape alone
            }
        Assert.False(KeyBindings.AnyDefault(KeyNames.F(10)));                                   // and no command of 1.1's is on it
        Assert.Contains("vault", DevConsole.OriginalCommands);
    }

    [Fact]
    public void A_harness_key_answers_only_to_its_chord_once_a_press()
    {
        int f2 = KeyNames.F(2);
        Assert.False(HarnessKeys.Is(HarnessKey.Overlay, f2, KeyMods.None, debug: true, repeat: false));   // 1.1's quick item 2
        Assert.True(HarnessKeys.Is(HarnessKey.Overlay, f2, KeyMods.Ctrl, debug: true, repeat: false));
        Assert.False(HarnessKeys.Is(HarnessKey.Overlay, f2, KeyMods.Ctrl, debug: false, repeat: false));  // 1.1's quick item 2, Ctrl or not
        Assert.False(HarnessKeys.Is(HarnessKey.Overlay, f2, KeyMods.Ctrl, debug: true, repeat: true));
        Assert.False(HarnessKeys.Any(KeyNames.F(4), KeyMods.Ctrl, debug: true, repeat: false));          // F4 is quick item 4 alone
    }

    [Fact]
    public void A_shop_or_the_vault_lets_by_only_the_harness()
    {
        int f1 = KeyNames.F(1);
        Assert.False(HarnessKeys.PassesPane(f1, KeyMods.None, debug: true, repeat: false));   // no quick item through a shop
        Assert.True(HarnessKeys.PassesPane(f1, KeyMods.Ctrl, debug: true, repeat: false));
        Assert.False(HarnessKeys.PassesPane(f1, KeyMods.Ctrl, debug: false, repeat: false));
        Assert.False(HarnessKeys.PassesPane(KeyNames.F(11), KeyMods.None, debug: true, repeat: false));
    }

    [Fact]
    public void Shift_on_a_moved_regroup_is_regroup_and_not_ungroup()
    {
        Assert.Equal(KeyMods.None, KeyHandOn.Mods(KeyCommand.Regroup, KeyMods.Shift));        // not handed on as Shift+R
        Assert.Equal(KeyMods.None, KeyHandOn.Mods(KeyCommand.Regroup, KeyMods.Shift | KeyMods.Ctrl));
        Assert.Equal(KeyMods.Ctrl, KeyHandOn.Mods(KeyCommand.Regroup, KeyMods.Ctrl));         // Ctrl+R is regroup in 1.1 too
        Assert.Equal(KeyMods.Shift, KeyHandOn.Mods(KeyCommand.Ungroup, KeyMods.None));        // ungroup, wherever it is, is 1.1's Shift+R
        Assert.Equal(KeyMods.Ctrl, KeyHandOn.Mods(KeyCommand.Quick1, KeyMods.Ctrl));          // Ctrl+F1 stays the harness's
        Assert.Equal(KeyMods.Shift, KeyHandOn.Mods(KeyCommand.DragSwivel, KeyMods.Shift));    // X with Shift is still the swivel
        // With ungroup moved to U, Shift+R is the regroup R alone.
        var keys = new KeyBindings();
        keys.Set(KeyCommand.Ungroup, [new KeyChord(KeyNames.Letter('U'))]);
        var (command, chord) = Assert.Single(keys.Pressed(KeyNames.Letter('R'), KeyMods.Shift, driving: false));
        Assert.Equal(KeyCommand.Regroup, command);
        Assert.Equal(KeyMods.None, KeyHandOn.Mods(command, KeyMods.Shift & ~chord.Mods));
    }

    [Fact]
    public void With_1_1s_keys_every_chord_is_handed_on_with_all_its_modifiers()
    {
        var keys = new KeyBindings();
        foreach (var info in KeyBindings.All.Where(i => i.Kind == CommandKind.Press))
            foreach (var d in info.Defaults.Where(d => !d.IsMouse))
                foreach (var held in AllMods.Select(m => m | d.Mods))
                    foreach (var (command, chord) in keys.Pressed(d.Input, held, driving: false))
                    {
                        var leftover = held & ~chord.Mods;
                        Assert.Equal(KeyBindings.HandedOn(command).Mods | leftover, KeyHandOn.Mods(command, leftover));
                    }
    }
}
