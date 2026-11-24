// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using Vitomir.Platform.Presentation;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The view's new settings — the crosshair at 20 % in melee, the combat log shown top right with ten rows and its
/// toggle unbound, the arc in the body's frame — saved and loaded; the Controls page's new rows through its values and back
/// (melee framing, the crosshair, the log and its corner, the camera's two); the attack direction read and written by its name
/// whether or not this build has the direction work's setting, and a field this build does not know kept through a save. Needs no
/// game files.
/// </summary>
public sealed class ViewSettingsTests
{
    static string TempFile(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vitomir-ce-view-{Environment.ProcessId}");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }

    [Fact]
    public void The_defaults_are_a_faint_crosshair_a_log_top_right_and_the_arc_on_the_body()
    {
        var s = new PlatformSettings();
        Assert.Equal(20f, s.MeleeReticleOpacity);
        Assert.True(s.ShowCombatLog);
        Assert.Equal(LogCorner.TopRight, s.CombatLogCorner);
        Assert.Equal(10, s.CombatLogLines);
        Assert.False(s.SwingArcCarryOn);
        Assert.False(s.ThirdPersonMeleeFraming);       // off, an option to test
        Assert.Empty(PlatformSettings.DefaultBindings()["combatLog"]);
    }

    [Fact]
    public void They_are_saved_and_loaded()
    {
        var path = TempFile("view.json");
        var s = new PlatformSettings
        {
            MeleeReticleOpacity = 35f, ShowCombatLog = false, CombatLogCorner = LogCorner.BottomLeft, CombatLogLines = 14,
            SwingArcCarryOn = true, ThirdPersonMeleeFraming = true, FirstPersonActionSway = 12f, ThirdPersonFollow = 0.15f,
        };
        s.Save(path);
        Assert.Contains("\"combatLogCorner\": \"bottomLeft\"", File.ReadAllText(path));
        var back = PlatformSettings.Load(path);
        Assert.Equal(35f, back.MeleeReticleOpacity);
        Assert.False(back.ShowCombatLog);
        Assert.Equal(LogCorner.BottomLeft, back.CombatLogCorner);
        Assert.Equal(14, back.CombatLogLines);
        Assert.True(back.SwingArcCarryOn);
        Assert.True(back.ThirdPersonMeleeFraming);
        Assert.Equal(12f, back.FirstPersonActionSway);
        Assert.Equal(0.15f, back.ThirdPersonFollow);
    }

    [Fact]
    public void The_pages_new_rows_go_through_its_values_and_back()
    {
        var s = new PlatformSettings { ThirdPersonMeleeFraming = true, MeleeReticleOpacity = 40f, ShowCombatLog = true, CombatLogCorner = LogCorner.TopLeft, FirstPersonActionSway = 7f, ThirdPersonFollow = 0.2f };
        var v = Menus.Values(s);
        Assert.True(v.MeleeFraming);
        Assert.Equal(40f, v.MeleeReticleOpacity);
        Assert.True(v.ShowCombatLog);
        Assert.Equal(LogCorner.TopLeft, v.CombatLogCorner);
        Assert.Equal(7f, v.ActionSway);
        Assert.Equal(0.2f, v.ThirdPersonFollow);
        Assert.Equal("keys", v.AttackDirection);   // the movement keys by default
        var other = new PlatformSettings();
        Menus.Store(v with { MeleeFraming = false, MeleeReticleOpacity = 0f, ShowCombatLog = false, ActionSway = 0f, ThirdPersonFollow = 0f, AttackDirection = "automatic" }, other);
        Assert.False(other.ThirdPersonMeleeFraming);
        Assert.Equal(0f, other.MeleeReticleOpacity);
        Assert.False(other.ShowCombatLog);
        Assert.Equal(LogCorner.TopLeft, other.CombatLogCorner);
        Assert.Equal(0f, other.FirstPersonActionSway);
        Assert.Equal(0f, other.ThirdPersonFollow);
        Assert.Equal("automatic", other.Named(Menus.AttackDirectionSetting));
        // Out of their ends in a file: the page shows them within.
        Assert.Equal(100f, Menus.Values(new PlatformSettings { MeleeReticleOpacity = 300f }).MeleeReticleOpacity);
        Assert.Equal(0.3f, Menus.Values(new PlatformSettings { ThirdPersonFollow = 2f }).ThirdPersonFollow);
    }

    [Fact]
    public void The_attack_direction_is_read_and_written_by_its_name_and_unknown_fields_are_kept()
    {
        // The direction work owns the setting attackDirection; this page reads it by name — the property when the build has it, else
        // the file's own value, kept through a save.
        var path = TempFile("named.json");
        File.WriteAllText(path, """{ "settingsVersion": 4, "attackDirection": "mouse", "someLaterSetting": { "a": 1 } }""");
        var s = PlatformSettings.Load(path);
        Assert.Equal("mouse", s.Named("attackDirection"));
        Assert.Equal("mouse", Menus.Values(s).AttackDirection);
        Assert.True(s.SetNamed("attackDirection", "keys"));
        s.Save(path);
        var text = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(text);
        Assert.Equal("keys", doc.RootElement.GetProperty("attackDirection").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("someLaterSetting").GetProperty("a").GetInt32());
        // A property this build has is read and set by its name too (an enumeration in camel case).
        var t = new PlatformSettings { CombatLogCorner = LogCorner.BottomRight };
        Assert.Equal("bottomRight", t.Named("combatLogCorner"));
        Assert.True(t.SetNamed("combatLogCorner", "topLeft"));
        Assert.Equal(LogCorner.TopLeft, t.CombatLogCorner);
        Assert.False(t.SetNamed("combatLogCorner", "middle"));
        Assert.Null(new PlatformSettings().Named("noSuchSetting"));
    }
}
