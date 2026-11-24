// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Black above a bottom-seated sheet while the escape menu is up** (*"a."*). The escape menu's
/// ankh is cut flat at the sheet's top in the art itself (its upper quads end at 480); 1.1's screen ended there. On a
/// picture narrower than 16:9 the sheet sits at the bottom with the world above it, so while a dialog whose art runs
/// up to the sheet's top is drawn the rows above the sheet are black (<see cref="FrontEnd.DialogAgainstTop"/>,
/// <see cref="MenuPass.TopBand"/>). These say which panes those are, from the installed art.
/// </summary>
public class EscapeBandTests
{
    [Fact]
    public void The_escape_menus_and_the_console_are_dialogs_against_the_top_and_nothing_else_is()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), new Strings(files).Find);
        foreach (string against in new[] { "escapeMenu", "escapeMenuMP", "console" })
        {
            var pane = menus.Pane(against);
            Assert.True(pane.Dialog && pane.AgainstTop, against);
        }
        // The full-screen menus are the whole sheet (their own bands); the boxes and plaques stop short of the top; the
        // panes above the bar reach it but are no dialogs (and have their own map, centred above the bar).
        foreach (string not in new[] { "Options", "spSave", "spLoad", "mapPragueOT", "messageBox", "inputBox", "LoadScreen",
                                       "ReadTextBox", "conversationPane", "subtitle", "statusBarSP", "inventoryPane",
                                       "characterPane", "questPane", "disciplinePane", "buySellPane", "VaultPane" })
        {
            var pane = menus.Pane(not);
            Assert.False(pane.Dialog && pane.AgainstTop, not);
        }
        Assert.False(menus.DialogAgainstTop);
        menus.OpenEscape();
        Assert.True(menus.DialogAgainstTop);
    }
}
