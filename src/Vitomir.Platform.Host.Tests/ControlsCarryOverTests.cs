// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using SDL;
using Vitomir.Platform.Control;
using Vitomir.Platform.Interface;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The keys: Q is aim, Mouse 4 or 5 is dodge (it depends on the game), and
/// swap shoulder is gone: the "change camera" key alone cycles first person, the right
/// shoulder, then the left shoulder, then round again. A new file: aim on Q, Dodge on Mouse 4, Switch View on V, no
/// swapShoulder. An older file carried over once: swapShoulder dropped (any version), Dodge on Q moved to Mouse 4 (else Mouse 5,
/// else kept), aim given Q where nothing else will have it; the player's own keys otherwise kept; each change said once. The
/// program never sends the retired button. Needs no game files (SDL's key names only).
/// </summary>
public sealed class ControlsCarryOverTests
{
    static string TempFile(string name, string text)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vitomir-ck-controls-{Environment.ProcessId}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    static readonly InputSource Q = InputSource.Key((int)SDL_Scancode.SDL_SCANCODE_Q);
    static readonly InputSource Mouse4 = InputSource.Mouse(BindingNames.SdlX1);
    static readonly InputSource V = InputSource.Key((int)SDL_Scancode.SDL_SCANCODE_V);

    [Fact]
    public void A_new_file_has_aim_on_Q_dodge_on_Mouse_4_switch_view_on_V_and_no_swap_shoulder()
    {
        var none = PlatformSettings.Load(Path.Combine(Path.GetTempPath(), $"vitomir-ck-no-such-{Environment.ProcessId}.json"));
        Assert.Equal(PlatformSettings.CurrentVersion, none.SettingsVersion);
        Assert.Equal(["Q"], none.Bindings["aim"]);
        Assert.Equal(["Mouse 4"], none.Bindings["dodge"]);
        Assert.Equal(["V"], none.Bindings["switchView"]);
        Assert.False(none.Bindings.ContainsKey("swapShoulder"));
        var table = SdlKeys.Table(none.Bindings);
        Assert.Empty(table.Problems);
        Assert.Equal(["aim"], table.ActionsOf(Q));          // Q alone: nothing else of the defaults has it
        Assert.Equal(["dodge"], table.ActionsOf(Mouse4));
        Assert.Equal(["switchView"], table.ActionsOf(V));
        // The Controls page: no Swap Shoulder row; Aim and Dodge listed.
        Assert.DoesNotContain(ControlsPage.Order, o => o.Action == "swapShoulder");
        Assert.Contains(ControlsPage.Order, o => o.Action == "aim" && o.Label == "Aim");
    }

    [Fact]
    public void An_older_file_loses_swap_shoulder_and_aim_takes_Q_once_and_the_rest_stays()
    {
        // A player's bindings of that time: swapShoulder Q, dodge Mouse 4, switchView V, aim unbound; the rest of the scheme besides.
        var path = TempFile("player.json", """
            { "settingsVersion": 4,
              "bindings": { "crouch": ["X"], "cancel": ["X"], "jump": ["Mouse Right"], "use": ["Space"], "walk": ["Left Alt"], "sprint": ["Left Ctrl"],
                            "swapShoulder": ["Q"], "dodge": ["Mouse 4"], "switchView": ["V"], "aim": [], "swapWeapon": ["F"] } }
            """);
        var said = new List<string>();
        var s = PlatformSettings.Load(path, said.Add);
        Assert.Equal(PlatformSettings.CurrentVersion, s.SettingsVersion);
        Assert.False(s.Bindings.ContainsKey("swapShoulder"));
        Assert.Equal(["Q"], s.Bindings["aim"]);
        Assert.Equal(["Mouse 4"], s.Bindings["dodge"]);
        Assert.Equal(["V"], s.Bindings["switchView"]);
        Assert.Equal(["X"], s.Bindings["crouch"]);
        Assert.Equal(["Mouse Right"], s.Bindings["jump"]);
        Assert.Equal(["F"], s.Bindings["swapWeapon"]);
        Assert.Single(said, l => l.Contains("Swap Shoulder is gone, so Q no longer works it", StringComparison.Ordinal));
        Assert.Single(said, l => l.Contains("Aim (the bow raised, held) is on Q", StringComparison.Ordinal));
        Assert.DoesNotContain(said, l => l.Contains("Dodge", StringComparison.Ordinal));     // his Dodge was already on Mouse 4
        var table = SdlKeys.Table(s.Bindings);
        Assert.Equal(["aim"], table.ActionsOf(Q));
        Assert.Equal(["dodge"], table.ActionsOf(Mouse4));

        // Saved and read again: as it was left, nothing said (once).
        s.Save(path);
        said.Clear();
        var again = PlatformSettings.Load(path, said.Add);
        Assert.Empty(said);
        Assert.Equal(["Q"], again.Bindings["aim"]);
        Assert.False(again.Bindings.ContainsKey("swapShoulder"));
        Assert.DoesNotContain("swapShoulder", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_with_the_older_defaults_moves_dodge_from_Q_to_Mouse_4_and_gives_aim_Q()
    {
        var said = new List<string>();
        var s = PlatformSettings.Load(TempFile("cj.json", """
            { "settingsVersion": 4, "bindings": { "swapShoulder": [], "dodge": ["Q"], "aim": [], "crouch": ["C"] } }
            """), said.Add);
        Assert.Equal(["Mouse 4"], s.Bindings["dodge"]);
        Assert.Equal(["Q"], s.Bindings["aim"]);
        Assert.False(s.Bindings.ContainsKey("swapShoulder"));
        Assert.Single(said, l => l.Contains("Dodge moved from Q to Mouse 4", StringComparison.Ordinal));
        Assert.DoesNotContain(said, l => l.Contains("Swap Shoulder", StringComparison.Ordinal));     // it had no key: nothing to say
        Assert.Empty(SdlKeys.Table(s.Bindings).Problems);
    }

    [Fact]
    public void Dodge_takes_Mouse_5_when_Mouse_4_is_taken_and_keeps_Q_when_both_are_then_aim_has_no_key()
    {
        var five = PlatformSettings.Load(TempFile("five.json", """
            { "settingsVersion": 4, "bindings": { "dodge": ["Q"], "freePointer": ["Mouse4"] } }
            """));
        Assert.Equal(["Mouse 5"], five.Bindings["dodge"]);
        Assert.Equal(["Mouse4"], five.Bindings["freePointer"]);
        Assert.Equal(["Q"], five.Bindings["aim"]);

        var said = new List<string>();
        var both = PlatformSettings.Load(TempFile("both.json", """
            { "settingsVersion": 4, "bindings": { "dodge": ["Q"], "freePointer": ["Mouse 4"], "freeLook": ["Mouse 5"] } }
            """), said.Add);
        Assert.Equal(["Q"], both.Bindings["dodge"]);
        Assert.Empty(both.Bindings["aim"]);
        Assert.Single(said, l => l.Contains("Q works dodge in your bindings", StringComparison.Ordinal) && l.Contains("Aim", StringComparison.Ordinal));

        // A file that leaves aim out altogether is written so too: the defaults do not put Q beside Dodge.
        var older = PlatformSettings.Load(TempFile("older.json", """
            { "settingsVersion": 4, "bindings": { "dodge": ["Q"], "freePointer": ["Mouse 4"], "freeLook": ["Mouse 5"], "use": ["E"] } }
            """));
        Assert.Empty(older.Bindings["aim"]);
        Assert.Equal(["dodge"], SdlKeys.Table(older.Bindings).ActionsOf(Q));
    }

    [Fact]
    public void The_players_own_keys_stay()
    {
        // Aim given a key of his own: kept; Dodge on Q still moves (Q is then left free).
        var own = PlatformSettings.Load(TempFile("own.json", """
            { "settingsVersion": 4, "bindings": { "dodge": ["Q"], "aim": ["G"] } }
            """));
        Assert.Equal(["G"], own.Bindings["aim"]);
        Assert.Equal(["Mouse 4"], own.Bindings["dodge"]);
        Assert.Empty(SdlKeys.Table(own.Bindings).ActionsOf(Q));

        // Q on another action of his: it stays there; aim has no key, Dodge (on Mouse 5) untouched.
        var other = PlatformSettings.Load(TempFile("other.json", """
            { "settingsVersion": 4, "bindings": { "slot1": ["Q"], "dodge": ["Mouse 5"], "aim": [] } }
            """));
        Assert.Equal(["Q"], other.Bindings["slot1"]);
        Assert.Empty(other.Bindings["aim"]);
        Assert.Equal(["Mouse 5"], other.Bindings["dodge"]);

        // Dodge with a side button besides Q: Q goes, the side button stays.
        var side = PlatformSettings.Load(TempFile("side.json", """
            { "settingsVersion": 4, "bindings": { "dodge": ["Q", "Mouse 4"], "aim": [] } }
            """));
        Assert.Equal(["Mouse 4"], side.Bindings["dodge"]);
        Assert.Equal(["Q"], side.Bindings["aim"]);

        // A file saved that put Dodge back on Q and aim elsewhere: kept as it says (the carry-over is once).
        var since = PlatformSettings.Load(TempFile("since.json", """
            { "settingsVersion": 5, "bindings": { "dodge": ["Q"], "aim": ["G"] } }
            """));
        Assert.Equal(["Q"], since.Bindings["dodge"]);
        Assert.Equal(["G"], since.Bindings["aim"]);
    }

    [Fact]
    public void Swap_shoulder_is_dropped_from_any_file_refused_by_bind_and_never_sent()
    {
        var said = new List<string>();
        var path = TempFile("v5.json", """{ "settingsVersion": 5, "bindings": { "swapShoulder": ["B"] } }""");
        var s = PlatformSettings.Load(path, said.Add);
        Assert.False(s.Bindings.ContainsKey("swapShoulder"));
        Assert.Single(said, l => l.Contains("Swap Shoulder is gone, so B no longer works it", StringComparison.Ordinal));

        said.Clear();
        Assert.Equal(2, SdlKeys.BindCommand(["--bind", "swapShoulder=B"], s, path, said.Add));
        Assert.Contains(said, l => l.Contains("swapShoulder is gone", StringComparison.Ordinal));
        Assert.Equal("""{ "settingsVersion": 5, "bindings": { "swapShoulder": ["B"] } }""", File.ReadAllText(path));   // nothing saved

        // Bindings that still name it (built by hand, not loaded): its key works nothing the simulation sees.
        var hand = new PlatformSettings();
        hand.Bindings["swapShoulder"] = ["B"];
        var input = new InputCollector(hand);
        var e = new SDL_Event();
        e.type = (uint)SDL_EventType.SDL_EVENT_KEY_DOWN;
        e.key.scancode = SDL_Scancode.SDL_SCANCODE_B;
        input.Take(e, true, 2560, 1440);
        Assert.Equal(Buttons.None, input.NextTick().Held & Buttons.SwapShoulder);
    }

    [Fact]
    public void Q_held_is_the_aim_button_Mouse_4_the_dodge_button_and_V_switch_view_in_the_tick()
    {
        var input = new InputCollector(new PlatformSettings());
        var q = new SDL_Event();
        q.type = (uint)SDL_EventType.SDL_EVENT_KEY_DOWN;
        q.key.scancode = SDL_Scancode.SDL_SCANCODE_Q;
        input.Take(q, true, 2560, 1440);
        Assert.Equal(Buttons.Aim, input.NextTick().Held & (Buttons.Aim | Buttons.Dodge | Buttons.SwapShoulder));
        q.type = (uint)SDL_EventType.SDL_EVENT_KEY_UP;
        input.Take(q, true, 2560, 1440);
        var m = new SDL_Event();
        m.type = (uint)SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN;
        m.button.button = (byte)BindingNames.SdlX1;
        input.Take(m, true, 2560, 1440);
        Assert.Equal(Buttons.Dodge, input.NextTick().Held & (Buttons.Aim | Buttons.Dodge | Buttons.SwapShoulder));
        m.type = (uint)SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP;
        input.Take(m, true, 2560, 1440);
        // V: switch view, the one key for the three views (PlayerControlSystem cycles them; FirstPersonEyeTests).
        var v = new SDL_Event();
        v.type = (uint)SDL_EventType.SDL_EVENT_KEY_DOWN;
        v.key.scancode = SDL_Scancode.SDL_SCANCODE_V;
        input.Take(v, true, 2560, 1440);
        Assert.Equal(Buttons.SwitchView, input.NextTick().Held & (Buttons.Aim | Buttons.Dodge | Buttons.SwitchView | Buttons.SwapShoulder));
    }

    [Fact]
    public void Same_input_reads_the_mouses_names_either_way()
    {
        Assert.True(PlatformSettings.SameInput("Mouse4", "Mouse 4"));
        Assert.True(PlatformSettings.SameInput(" mouse 5 ", "Mouse 5"));
        Assert.True(PlatformSettings.SameInput("q", "Q"));
        Assert.False(PlatformSettings.SameInput("Mouse 4", "Mouse 5"));
        Assert.False(PlatformSettings.SameInput("Mouse 4", "Q"));
        Assert.False(PlatformSettings.SameInput(null, "Q"));
    }
}
