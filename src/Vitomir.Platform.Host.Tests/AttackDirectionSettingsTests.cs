// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Control;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The setting <c>attackDirection</c> — the movement keys by default (Mount &amp; Blade's
/// movement-based attack), the earlier build's automatic before — saved and loaded, its three names read in any case and nothing else; a
/// recording's note (a tape without one chooses automatically, as it was recorded), and a session's options automatic unless given
/// (the program gives the setting's). Needs no game files.
/// </summary>
public sealed class AttackDirectionSettingsTests
{
    [Fact]
    public void The_attack_direction_is_the_keys_by_default_and_saved_and_loaded()
    {
        var s = new PlatformSettings();
        Assert.Equal("keys", s.AttackDirection);
        Assert.Equal(Vitomir.Platform.Interface.ControlValues.DefaultAttackDirection, s.AttackDirection);   // the Controls page's Defaults the same
        var dir = Path.Combine(Path.GetTempPath(), $"vitomir-ce-direction-{Environment.ProcessId}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        s.AttackDirection = "mouse";
        s.Save(path);
        Assert.Equal("mouse", PlatformSettings.Load(path).AttackDirection);
    }

    [Fact]
    public void Its_names_are_automatic_mouse_and_keys_and_a_tape_says_which()
    {
        Assert.Equal(["automatic", "mouse", "keys"], AttackDirectionInput.Names);
        Assert.True(AttackDirectionInput.TryParse("Keys", out var k));
        Assert.Equal(AttackDirectionMode.Keys, k);
        Assert.True(AttackDirectionInput.TryParse(" mouse ", out var m));
        Assert.Equal(AttackDirectionMode.Mouse, m);
        Assert.False(AttackDirectionInput.TryParse("spin", out _));
        Assert.Equal("keys", new AttackDirectionInput { Mode = AttackDirectionMode.Keys }.Note);
        Assert.Equal(AttackDirectionMode.Mouse, AttackDirectionInput.FromNote("mouse").Mode);
        Assert.Equal(AttackDirectionMode.Automatic, AttackDirectionInput.FromNote(null).Mode);     // an older tape
        Assert.Equal(AttackDirectionMode.Automatic, new SessionOptions { Level = "" }.AttackDirection.Mode);
    }
}
