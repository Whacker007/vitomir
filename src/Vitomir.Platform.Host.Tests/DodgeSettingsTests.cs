// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using SDL;
using Vitomir.Platform.Control;
using Vitomir.Platform.Interface;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// Dodge in the program's settings and input: the defaults put it on Mouse 4
/// (Mouse 4 or 5 is dodge; it was Q, aim's now) with no clash; an older file gets Mouse 4 where nothing
/// of the file has it or will have it by default, else Mouse 5, else no key (the log says so); a file that names it keeps it; the
/// Controls page lists it after Block; and its key held is the Dodge button in the tick. Needs no game files (SDL's key names only).
/// </summary>
public sealed class DodgeSettingsTests
{
    static string TempFile(string name, string text)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vitomir-dodge-settings-{Environment.ProcessId}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void The_defaults_put_Dodge_on_Mouse_4_with_no_clash()
    {
        var d = PlatformSettings.DefaultBindings();
        Assert.Equal(["Mouse 4"], d["dodge"]);
        Assert.Equal(["Q"], d["aim"]);                       // Q is aim's
        var table = SdlKeys.Table(d);
        Assert.Empty(table.Problems);
        Assert.True(table.TryGet(InputSource.Mouse(BindingNames.SdlX1), out var action));
        Assert.Equal("dodge", action);
        Assert.Equal(["dodge"], table.ActionsOf(InputSource.Mouse(BindingNames.SdlX1)));
    }

    [Fact]
    public void A_file_from_before_gets_Mouse_4_where_it_is_free_else_Mouse_5_else_none_and_keeps_its_own()
    {
        // A scheme with crouch (and cancel) on X.
        var said = new List<string>();
        var older = PlatformSettings.Load(TempFile("player.json", """
            { "settingsVersion": 3, "bindings": { "crouch": ["X"], "cancel": ["X"], "jump": ["Mouse Right"], "use": ["Space"], "walk": ["Left Alt"], "sprint": ["Left Ctrl"] } }
            """), said.Add);
        Assert.Equal(["Mouse 4"], older.Bindings["dodge"]);
        Assert.Contains(said, l => l.Contains("Dodge is on Mouse 4", StringComparison.Ordinal));
        Assert.DoesNotContain(SdlKeys.Table(older.Bindings).Problems, p => p.Contains("dodge", StringComparison.Ordinal));

        // A file that leaves everything out: the default's Mouse 4.
        var plain = PlatformSettings.Load(TempFile("plain.json", """{ "settingsVersion": 3, "showHud": false }"""));
        Assert.Equal(["Mouse 4"], plain.Bindings["dodge"]);
        Assert.Empty(SdlKeys.Table(plain.Bindings).Problems);

        // Mouse 4 taken (Windows' numbering names it too): Mouse 5.
        var four = PlatformSettings.Load(TempFile("four.json", """{ "settingsVersion": 3, "bindings": { "freePointer": ["Mouse4"] } }"""));
        Assert.Equal(["Mouse 5"], four.Bindings["dodge"]);

        // Mouse 4 and Mouse 5 both taken: no key, and the log says so.
        said.Clear();
        var full = PlatformSettings.Load(TempFile("full.json", """{ "settingsVersion": 3, "bindings": { "freePointer": ["Mouse 4"], "freeLook": ["Mouse 5"] } }"""), said.Add);
        Assert.Empty(full.Bindings["dodge"]);
        Assert.Contains(said, l => l.Contains("Dodge has no key yet", StringComparison.Ordinal));

        // A file that names Dodge keeps it.
        var own = PlatformSettings.Load(TempFile("own.json", """{ "settingsVersion": 3, "bindings": { "dodge": ["Mouse 5"] } }"""));
        Assert.Equal(["Mouse 5"], own.Bindings["dodge"]);
    }

    [Fact]
    public void The_Controls_page_lists_Dodge_after_Block()
    {
        int block = -1, dodge = -1;
        for (int i = 0; i < ControlsPage.Order.Count; i++)
        {
            if (ControlsPage.Order[i].Action == "block") block = i;
            if (ControlsPage.Order[i].Action == "dodge") dodge = i;
        }
        Assert.Equal(block + 1, dodge);
        Assert.Equal("Dodge", ControlsPage.Order[dodge].Label);
    }
}
