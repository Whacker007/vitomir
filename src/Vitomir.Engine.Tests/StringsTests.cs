// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The stacked string table, and the pane that was showing raw keys until it existed.
/// </summary>
public class StringsTests
{
    [Fact]
    public void The_four_the_engine_loads_at_start_up_are_loaded_in_its_order()
    {
        using var files = Install.Open();
        var strings = new Strings(files);
        Assert.Equal(Vitomir.Formats.NlsFile.Startup, strings.Loaded);
        Assert.Empty(strings.Missing);
        Assert.True(strings.Count > 1500, $"only {strings.Count} strings loaded");
    }

    [Fact]
    public void A_key_the_tables_state_comes_back_as_english_and_an_unknown_one_comes_back_as_itself()
    {
        using var files = Install.Open();
        var strings = new Strings(files);
        Assert.Equal("Human", strings["UI_GENERAL_HUMAN"]);
        Assert.Equal("Kindred", strings["UI_GENERAL_VAMPIRE"]);
        Assert.Equal("Acolyte", strings["UI_STATUS_HUMAN0"]);

        // A missing string should look missing rather than look like a blank field.
        Assert.Equal("UI_NO_SUCH_STRING", strings["UI_NO_SUCH_STRING"]);
        Assert.Null(strings.Find("UI_NO_SUCH_STRING"));
    }

    /// <summary>
    /// **Adding a table cannot overwrite what is already loaded.** The map refuses a key it
    /// already holds, so a chronicle's own strings extend the four rather than replace them — the
    /// opposite of the load-order override a mod loader wants, and the reason that is written down.
    /// </summary>
    [Fact]
    public void A_chronicles_own_table_extends_the_four_and_never_replaces_them()
    {
        using var files = Install.Open();
        var strings = new Strings(files);
        string before = strings["UI_GENERAL_HUMAN"];
        int was = strings.Count;

        Assert.True(strings.Add("prague"), "strings/prague.nls is in Local_Eng.nob");
        Assert.True(strings.Count > was, "prague.nls adds strings of its own");
        Assert.Equal(before, strings["UI_GENERAL_HUMAN"]);
        Assert.Contains("prague", strings.Loaded);

        // Asking twice is not an error and does not load it twice.
        int after = strings.Count;
        Assert.True(strings.Add("prague"));
        Assert.Equal(after, strings.Count);

        // A table that is not there is reported rather than thrown.
        Assert.False(strings.Add("no_such_chronicle"));
        Assert.Contains("strings/no_such_chronicle.nls", strings.Missing);
    }

    /// <summary>
    /// The character sheet, end to end: a human Christof's generation box reads **Human** and his
    /// status reads **Acolyte**, where before this they read the key names — which is exactly what
    /// screenshots showed.
    /// </summary>
    [Fact]
    public void The_character_pane_says_human_and_acolyte_rather_than_the_key_names()
    {
        using var files = Install.Open();
        var strings = new Strings(files);

        Assert.SkipUnless(files.TryRead("ui/characterPane.nui", out var nui), "no ui/characterPane.nui");
        var layout = Vitomir.Formats.NuiPane.Parse(System.Text.Encoding.Latin1.GetString(nui),
                                                   "characterPane.nui");

        var human = new Actor { Type = ActorType.Human, Name = "Christof" };
        var pane = new CharacterPane(layout) { Subject = human, Strings = key => strings[key] };
        Assert.Equal("Human", pane.Generation);
        Assert.Equal("charFaithBar", pane.Secondary.Bar);  // faith where a vampire has frenzy

        var kindred = new Actor { Type = ActorType.Vampire, Name = "Christof" };
        kindred.Stat[(int)ActorStat.Generation] = 9f;
        var vampire = new CharacterPane(layout) { Subject = kindred, Strings = key => strings[key] };
        Assert.Equal("9", vampire.Generation);            // a vampire shows the number, not a word
    }
}
