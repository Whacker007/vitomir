// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Features;
using Vitomir.Engine.Ui;
using Vitomir.Platform.Windowing;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Our captions in Title Case, and still in their boxes** (ours): the menus
/// with proper capitalisation across words for in-menu options, where the Fixes
/// pages once used lower case. Every caption of ours — a button, a label, a heading, a page's title, a switch's or a
/// command's name — has each word's first letter a capital, short joining words (and, or, of, the…) excepted after
/// the first; and the wider words still fit the boxes the pages give them, in the page's own font. 1.1's words are
/// its own and are not touched (its buttons' words are pictures).
/// </summary>
public class CaptionCaseTests
{
    static readonly HashSet<string> Small = ["a", "an", "the", "and", "or", "of", "on", "in", "to", "for", "at", "by"];

    /// <summary>
    /// Whether every word of a caption starts with a capital (a joining word after the first may not; a figure or sign
    /// may; a note in brackets is a note, as "(host:port)", and keeps its own case).
    /// </summary>
    static bool TitleCase(string caption)
    {
        int bracket = caption.IndexOf('(');
        if (bracket > 0) caption = caption[..bracket];
        var words = caption.Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            string w = words[i].TrimStart('"');
            if (w.Length == 0 || !char.IsLetter(w[0])) continue;
            if (i > 0 && Small.Contains(w.TrimEnd(',', ':'))) continue;
            if (!char.IsUpper(w[0])) return false;
        }
        return true;
    }

    [Fact]
    public void The_captions_of_ours_are_in_title_case()
    {
        string[] ours =
        [
            MpMenus.AddressTitle, FrontEnd.OriginalLabel, FrontEnd.OriginalAutosave,
            .. FullScreen.WindowModes,
            .. KeysPage.Groups.Select(g => g.Heading),
            .. KeyBindings.All.Select(i => i.Name),
            .. FixesPage.Headings.Select(h => h.Split(" - ")[0]),          // the heading; what follows the dash is a phrase
        ];
        Assert.All(ours, c => Assert.True(TitleCase(c), $"\"{c}\" is not in Title Case"));
    }

    [Fact]
    public void Every_switch_has_a_title_of_its_own_in_title_case_and_its_key_is_unchanged()
    {
        var all = GameFeatures.Registry.All.ToList();
        Assert.Equal(39, all.Count);
        Assert.All(all, f =>
        {
            Assert.NotEqual(f.Name, f.Title);                                  // a title is given, not the key again
            Assert.True(TitleCase(f.Title), $"{f.Name}'s title \"{f.Title}\" is not in Title Case");
            Assert.Equal(f.Name.ToLowerInvariant(), f.Name);                   // the keys stay as the ini and the saves have them
        });
        Assert.Equal("Matched Gait", GameFeatures.Registry.Find("fixes", "matchedgait")!.Title);
        Assert.Equal(all.Count, all.Select(f => f.Title).Distinct().Count());
    }

    [Fact]
    public void A_switchs_label_fits_its_row_and_a_commands_name_its_column()
    {
        using var files = Install.Open();
        var font = new UiAssets(files).Font("defaultfont10");
        int Width(string s) => s.Sum(c => font[c].Advance);

        // The Fixes page: a row's label box is the row less the box and the indent of a switch under another.
        foreach (var f in GameFeatures.Registry.All)
        {
            int indent = f.Refines is null ? 0 : FixesPage.Indent;
            int box = FixesPage.BoxW - 26 - indent;
            string longest = f.Kind == FeatureKind.Repair ? f.Title + " (always on)" : f.Title + FixesPage.HostsLabel;
            Assert.True(Width(longest) <= box, $"\"{longest}\" is {Width(longest)} wide in a label of {box}");
        }

        // The key page: a command's name in its column, and a group's heading in the heading's box.
        Assert.All(KeyBindings.All, i => Assert.True(Width(i.Name) <= KeysPage.NameW, $"\"{i.Name}\" is {Width(i.Name)} wide in a column of {KeysPage.NameW}"));
        Assert.All(KeysPage.Groups, g => Assert.True(Width(g.Heading) <= KeysPage.BoxW - 4, $"\"{g.Heading}\" is {Width(g.Heading)} wide in a heading of {KeysPage.BoxW - 4}"));
        // The hint box's line at its foot, in one line of its width.
        Assert.True(Width(KeysPage.Prompt) <= KeysPage.BoxW - 8, $"the prompt is {Width(KeysPage.Prompt)} wide in a line of {KeysPage.BoxW - 8}");
    }

    /// <summary>
    /// **On 1.1's own screens our words are lower case** (<i>"we can change it back to lowercase for the
    /// original v1.1. menu style, but only for that"</i>): the captions of ours drawn there in 1.1's lettering, the words
    /// in ours (but a capital that 1.1's own words have, the AI of *coterie AI options*), and our pages' titles.
    /// </summary>
    [Fact]
    public void Our_words_on_1_1_s_screens_are_in_lower_case()
    {
        string[] words =
        [
            OptionsMenu.FixesCaption, OptionsMenu.KeysCaption, AlwaysRunOption.Caption, QuickLoadConfirm.Caption,
            FixesPage.Title, KeysPage.Title, KeysPage.Prompt.Length > 0 ? "defaults" : "",
            .. MenuLettering.OptionsRows.Select(r => r.Word),
            .. MenuLettering.GameplayRows.Select(r => r.Word),
            .. MenuLettering.ControlRows.Select(r => r.Word),
        ];
        Assert.All(words, w => Assert.Equal(w.ToLowerInvariant(), w));
    }
}
