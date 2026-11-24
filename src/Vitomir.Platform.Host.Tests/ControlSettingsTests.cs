// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using SDL;
using Vitomir.Platform.Control;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The earlier build's settings and input: third person faces the view by default, and an older file that said move
/// is brought to view once (the log saying so) while one saved since keeps move; the legs' turn on, double tap off with a
/// quarter-second window kept within its ends, free look unbound; a shared key (crouch and jump, or the default C's crouch and
/// cancel) works all its actions in the tick and an action two keys work stays down while either is held; <c>--bind
/// action=+key</c> shares and <c>--bind action=key</c> moves, as before. Needs no game files (SDL's key names only).
/// </summary>
public sealed class ControlSettingsTests
{
    static string TempFile(string name, string text)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vitomir-cb-control-{Environment.ProcessId}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>
    /// **A file with settings this build does not know still loads**: an unknown key (one a renamed or retired setting left behind)
    /// is passed over and kept as it was, an unknown word for <c>townsfolkBodies</c> is the default crowd, and every setting the
    /// file does not name keeps its default.
    /// </summary>
    [Fact]
    public void A_file_with_keys_this_build_does_not_know_loads_and_keeps_them()
    {
        var said = new List<string>();
        var path = TempFile("unknown.json", """{ "settingsVersion": 5, "aSettingNoBuildHas": true, "anotherOne": "x", "townsfolkBodies": "no-such-word", "wallClimbRate": 1.3, "wallMoveRate": 1.5, "turnFrom": 40 }""");
        var s = PlatformSettings.Load(path, said.Add);
        Assert.DoesNotContain(said, l => l.Contains("could not be read", StringComparison.Ordinal));
        Assert.Equal(40f, s.TurnFrom);
        Assert.False(s.WallsNoFlow);
        Assert.False(s.TownsfolkBodies.Equals(TownsfolkBodies.ModelsBuilt, StringComparison.Ordinal) || TownsfolkBodies.ChooseBuilt(false, s.TownsfolkBodies));
        Assert.True(s.Unknown is not null && s.Unknown.ContainsKey("aSettingNoBuildHas") && s.Unknown.ContainsKey("anotherOne"));
        s.Save(path);
        Assert.Contains("\"aSettingNoBuildHas\": true", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void Third_person_faces_the_view_by_default_and_a_file_from_before_saying_move_is_brought_to_view_once()
    {
        Assert.Equal(ThirdPersonFacing.View, new PlatformSettings().ThirdPersonFacing);
        Assert.Equal(ThirdPersonFacing.View, new SessionOptions { Level = "" }.Facing);
        Assert.Equal(5, PlatformSettings.CurrentVersion);   // aim on Q, Dodge on Mouse 4 (ControlsCarryOverTests)
        var said = new List<string>();
        var path = TempFile("old.json", """{ "settingsVersion": 3, "thirdPersonFacing": "move" }""");
        var old = PlatformSettings.Load(path, said.Add);
        Assert.Equal(ThirdPersonFacing.View, old.ThirdPersonFacing);
        Assert.Contains(said, l => l.Contains("third person now faces the view (the body turns", StringComparison.Ordinal));
        Assert.Equal(PlatformSettings.CurrentVersion, old.SettingsVersion);
        // Saved since and set back to move on the Controls page: kept, said nothing.
        old.ThirdPersonFacing = ThirdPersonFacing.Move;
        old.Save(path);
        said.Clear();
        Assert.Equal(ThirdPersonFacing.Move, PlatformSettings.Load(path, said.Add).ThirdPersonFacing);
        Assert.DoesNotContain(said, l => l.Contains("faces the view", StringComparison.Ordinal));
        // A older file that left the facing out, or a new file: view.
        Assert.Equal(ThirdPersonFacing.View, PlatformSettings.Load(TempFile("none.json", """{ "settingsVersion": 2 }""")).ThirdPersonFacing);
        Assert.Equal(ThirdPersonFacing.View, PlatformSettings.Load(Path.Combine(Path.GetTempPath(), $"vitomir-no-such-settings-{Environment.ProcessId}.json")).ThirdPersonFacing);
    }

    [Fact]
    public void The_legs_turn_double_tap_is_off_with_a_quarter_second_window_and_free_look_is_unbound()
    {
        var s = new PlatformSettings();
        Assert.True(s.LegTurn);
        Assert.False(s.DoubleTapDodge);
        Assert.Equal(0.25f, s.DoubleTapSeconds);
        Assert.Equal(0.25f, PlatformSettings.TapWindow(5f));        // outside its ends: the default
        Assert.Equal(0.4f, PlatformSettings.TapWindow(0.4f));
        var d = PlatformSettings.DefaultBindings();
        Assert.Empty(d["freeLook"]);
        Assert.Empty(d["freeLookToggle"]);
        // Kept across a save.
        var path = TempFile("tap.json", "{}");
        s.DoubleTapDodge = true;
        s.DoubleTapSeconds = 0.3f;
        s.LegTurn = false;
        s.Save(path);
        var back = PlatformSettings.Load(path);
        Assert.True(back.DoubleTapDodge);
        Assert.Equal(0.3f, back.DoubleTapSeconds);
        Assert.False(back.LegTurn);
    }

    static SDL_Event Key(SDL_Scancode code, bool down)
    {
        var e = new SDL_Event();
        e.type = (uint)(down ? SDL_EventType.SDL_EVENT_KEY_DOWN : SDL_EventType.SDL_EVENT_KEY_UP);
        e.key.scancode = code;
        return e;
    }

    [Fact]
    public void A_shared_key_works_all_its_actions_and_an_action_on_two_keys_stays_down_while_either_is_held()
    {
        var s = new PlatformSettings();
        s.Bindings["jump"] = ["Space", "X"];
        s.Bindings["crouch"] = ["C", "X"];      // X shared by crouch and jump; C crouch's and cancel's by default
        var said = new List<string>();
        var input = new InputCollector(s, said.Add);
        Assert.Contains(said, l => l.Contains("keys shared:", StringComparison.Ordinal) && l.Contains("X works jump and crouch", StringComparison.Ordinal)
                                   && l.Contains("C works crouch and cancel", StringComparison.Ordinal));
        input.Take(Key(SDL_Scancode.SDL_SCANCODE_X, true), true, 2560, 1440);
        Assert.Equal(Buttons.Jump | Buttons.Crouch, input.NextTick().Held & (Buttons.Jump | Buttons.Crouch | Buttons.Cancel));
        // Space held too, then X let go: jump stays down (Space holds it), crouch lets go.
        input.Take(Key(SDL_Scancode.SDL_SCANCODE_SPACE, true), true, 2560, 1440);
        input.Take(Key(SDL_Scancode.SDL_SCANCODE_X, false), true, 2560, 1440);
        Assert.Equal(Buttons.Jump, input.NextTick().Held & (Buttons.Jump | Buttons.Crouch));
        input.Take(Key(SDL_Scancode.SDL_SCANCODE_SPACE, false), true, 2560, 1440);
        Assert.Equal(Buttons.None, input.NextTick().Held & (Buttons.Jump | Buttons.Crouch));
        // C: crouch and cancel together (the pair; the simulation decides which a press does).
        input.Take(Key(SDL_Scancode.SDL_SCANCODE_C, true), true, 2560, 1440);
        Assert.Equal(Buttons.Crouch | Buttons.Cancel, input.NextTick().Held & (Buttons.Crouch | Buttons.Cancel));
        input.Take(Key(SDL_Scancode.SDL_SCANCODE_C, false), true, 2560, 1440);
        Assert.Equal(Buttons.None, input.NextTick().Held & (Buttons.Crouch | Buttons.Cancel));
        // Free look given a key works it (held), and its toggle likewise.
        s.Bindings["freeLook"] = ["Mouse 5"];
        s.Bindings["freeLookToggle"] = ["G"];
        input.Apply(s);
        input.Take(Key(SDL_Scancode.SDL_SCANCODE_G, true), true, 2560, 1440);
        Assert.Equal(Buttons.FreeLookToggle, input.NextTick().Held & (Buttons.FreeLook | Buttons.FreeLookToggle));
    }

    [Fact]
    public void Bind_plus_shares_a_key_and_bind_plain_moves_it()
    {
        var path = TempFile("bind.json", "{}");
        var s = PlatformSettings.Load(path);
        var said = new List<string>();
        Assert.Equal(0, SdlKeys.BindCommand(["--bind", "jump=+C"], s, path, said.Add));
        var shared = PlatformSettings.Load(path);
        Assert.Equal(["Space", "C"], shared.Bindings["jump"]);
        Assert.Equal(["C"], shared.Bindings["crouch"]);                    // kept: shared
        Assert.Contains(said, l => l.Contains("C shared: it works jump, crouch and cancel", StringComparison.Ordinal));
        said.Clear();
        Assert.Equal(0, SdlKeys.BindCommand(["--bind", "use=C"], shared, path, said.Add));
        var moved = PlatformSettings.Load(path);
        Assert.Equal(["C"], moved.Bindings["use"]);
        Assert.Empty(moved.Bindings["crouch"]);                            // moved: taken off crouch and jump, not cancel
        Assert.Equal(["Space"], moved.Bindings["jump"]);
        Assert.Equal(["C"], moved.Bindings["cancel"]);
        Assert.Contains(said, l => l.Contains("C taken from crouch", StringComparison.Ordinal));
    }
}
