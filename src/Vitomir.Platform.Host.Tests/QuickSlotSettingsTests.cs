// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using SDL;
using Vitomir.Platform.Control;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The quick slots in the program's settings and input: by default on the number keys 1 to 8 and the HUD shown with
/// its toggle unbound; a settings file from before (version 2) gets them, but a number key its player already gave
/// another action stays there (the slot is left with no key, and the log says so); <c>--bind slot3=F</c> works; a number
/// key held is its slot's button in the tick, a tap shorter than a tick still reaches one, and nothing is held once the
/// menu takes the input. Needs no game files (SDL's key names only).
/// </summary>
public sealed class QuickSlotSettingsTests
{
    static string TempFile(string name, string text)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vitomir-slots-settings-{Environment.ProcessId}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void The_defaults_put_the_quick_slots_on_1_to_8_and_the_HUD_shown_with_no_key()
    {
        var s = new PlatformSettings();
        for (int n = 1; n <= 8; n++) Assert.Equal([$"{n}"], s.Bindings[$"slot{n}"]);
        Assert.Empty(s.Bindings["hud"]);
        Assert.True(s.ShowHud);
        Assert.Equal(5, PlatformSettings.CurrentVersion);     // 4 (third person's facing brought to the view); 5 (aim on Q)
        // SDL names the number keys 1 to 8 so (not the keypad's), and nothing else of the defaults is on them.
        var table = SdlKeys.Table(PlatformSettings.DefaultBindings());
        Assert.Empty(table.Problems);
        for (int n = 1; n <= 8; n++)
        {
            Assert.Equal((int)SDL_Scancode.SDL_SCANCODE_1 + n - 1, SdlKeys.Code($"{n}"));
            Assert.True(table.TryGet(InputSource.Key((int)SDL_Scancode.SDL_SCANCODE_1 + n - 1), out var action));
            Assert.Equal($"slot{n}", action);
        }
        // A file that does not exist gives the defaults, at this version.
        var none = PlatformSettings.Load(Path.Combine(Path.GetTempPath(), $"vitomir-no-such-{Environment.ProcessId}.json"));
        Assert.Equal(PlatformSettings.CurrentVersion, none.SettingsVersion);
        Assert.Equal(["5"], none.Bindings["slot5"]);
    }

    [Fact]
    public void A_version_2_file_gets_the_number_keys_but_keeps_its_players_own_on_another_action()
    {
        var path = TempFile("v2.json", """
            { "settingsVersion": 2, "showReach": true,
              "bindings": { "jump": ["Mouse Right", "1"], "use": ["Space"], "masquerade": ["4"], "crouch": ["X"] } }
            """);
        var said = new List<string>();
        var s = PlatformSettings.Load(path, said.Add);
        Assert.Equal(PlatformSettings.CurrentVersion, s.SettingsVersion);
        Assert.Equal(["Mouse Right", "1"], s.Bindings["jump"]);    // the player's own stays
        Assert.Equal(["4"], s.Bindings["masquerade"]);
        Assert.Empty(s.Bindings["slot1"]);
        Assert.Empty(s.Bindings["slot4"]);
        foreach (var n in new[] { 2, 3, 5, 6, 7, 8 }) Assert.Equal([$"{n}"], s.Bindings[$"slot{n}"]);
        Assert.Equal(2, said.Count(l => l.Contains("quick slot", StringComparison.Ordinal)));
        Assert.Contains(said, l => l.Contains("the key 1 works jump", StringComparison.Ordinal) && l.Contains("slot1", StringComparison.Ordinal));
        Assert.True(s.ShowHud);
        Assert.DoesNotContain(SdlKeys.Table(s.Bindings).Problems, p => p.Contains("slot", StringComparison.Ordinal));   // no clash left behind
        // Saved and read again: version 3, the slots as they were left (no key put back on slot 1).
        s.Save(path);
        said.Clear();
        var again = PlatformSettings.Load(path, said.Add);
        Assert.Empty(again.Bindings["slot1"]);
        Assert.Empty(said);
    }

    [Fact]
    public void A_version_3_file_keeps_its_slots_and_one_without_them_gets_the_defaults()
    {
        var path = TempFile("v3.json", """
            { "settingsVersion": 3, "showHud": false, "bindings": { "slot1": ["Q"], "slot2": [] } }
            """);
        var s = PlatformSettings.Load(path);
        Assert.Equal(["Q"], s.Bindings["slot1"]);
        Assert.Empty(s.Bindings["slot2"]);
        Assert.Equal(["3"], s.Bindings["slot3"]);
        Assert.False(s.ShowHud);
    }

    [Fact]
    public void Bind_slot3_to_F_takes_F_from_use_if_it_had_it_and_saves()
    {
        var path = TempFile("bind.json", """{ "settingsVersion": 3 }""");
        var s = PlatformSettings.Load(path);
        s.Bindings["use"] = ["F"];
        var said = new List<string>();
        Assert.Equal(0, SdlKeys.BindCommand(["--bind", "slot3=F"], s, path, said.Add));
        Assert.Equal(["F"], s.Bindings["slot3"]);
        Assert.Empty(s.Bindings["use"]);
        Assert.Contains(said, l => l.Contains("F taken from use", StringComparison.Ordinal));
        Assert.Equal(["F"], PlatformSettings.Load(path).Bindings["slot3"]);
        Assert.Equal(2, SdlKeys.BindCommand(["--bind", "slot9=G"], s, path, said.Add));      // no such action
    }

    static SDL_Event Key(SDL_Scancode code, bool down)
    {
        var e = new SDL_Event();
        e.type = (uint)(down ? SDL_EventType.SDL_EVENT_KEY_DOWN : SDL_EventType.SDL_EVENT_KEY_UP);
        e.key.scancode = code;
        return e;
    }

    [Fact]
    public void A_number_key_is_its_slots_button_a_tap_reaches_a_tick_and_the_menu_lets_go_of_it()
    {
        var input = new InputCollector(new PlatformSettings());
        input.Take(Key(SDL_Scancode.SDL_SCANCODE_3, true), true, 2560, 1440);
        Assert.Equal(Buttons.Slot3, input.NextTick().Held & QuickSlotButtons.All);
        Assert.Equal(Buttons.Slot3, input.NextTick().Held & QuickSlotButtons.All);    // held: the simulation takes the press, not the holding
        input.Take(Key(SDL_Scancode.SDL_SCANCODE_3, false), true, 2560, 1440);
        Assert.Equal(Buttons.None, input.NextTick().Held & QuickSlotButtons.All);

        // A tap shorter than a tick still reaches one.
        input.Take(Key(SDL_Scancode.SDL_SCANCODE_8, true), true, 2560, 1440);
        input.Take(Key(SDL_Scancode.SDL_SCANCODE_8, false), true, 2560, 1440);
        Assert.Equal(Buttons.Slot8, input.NextTick().Held & QuickSlotButtons.All);
        Assert.Equal(Buttons.None, input.NextTick().Held & QuickSlotButtons.All);

        // The menu opening lets go of everything (and the program sends its keys to the menu, not here, while it is open).
        input.Take(Key(SDL_Scancode.SDL_SCANCODE_1, true), true, 2560, 1440);
        input.ReleaseAll();
        Assert.Equal(Buttons.None, input.NextTick().Held);
    }
}
