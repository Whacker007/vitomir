// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The network game's escape menu, `escapeMenuMP` (built by the same routine as the single
// player's, with an argument set in a network game): its buttons' handlers, which the game
// run (`NetPlay`) hooks into the front end's other-click.
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **The network escape menu** (`ui/escapeMenuMP.nui`). The routine that builds either menu registers
/// Resume, Options and Quit on both, and on the network one Save
/// (`emmpSaveButton`) and Revive (`emmpReviveButton`) in place of Save and Load. Every
/// button first puts the menu away.
/// <list type="bullet">
/// <item><b>Options</b> is the single player's own handler: the menu put away and the Options
/// screen opened — Gameplay, Controls, Display, Sound and Done, the same screen. In a network
/// game nothing is paused, neither by the menu nor by the screen, and Done
/// unpauses nothing (<see cref="OptionsMenu.Open"/> pauses only a single-player game). Vitomir's Fixes page,
/// reached from it (ours), shows the switches the host governs as the host's, greyed.
/// </item>
/// <item><b>Save</b> is the host's alone — greyed on a player's machine; <b>Revive</b> is for the dead
/// (message 0x49); <b>Quit</b> leaves the game and goes to the main menu.</item>
/// </list>
/// </summary>
public static class NetEscape
{
    /// <summary>The menu's pane.</summary>
    public const string File = "escapeMenuMP";

    /// <summary>A click on the menu: false when <paramref name="pane"/> is not it.</summary>
    /// <param name="isHost">Whether this machine hosts the game (Save is its alone).</param>
    /// <param name="revive">Revive (message 0x49 on a player's machine, the host's own body at home).</param>
    /// <param name="quit">Leave the game and go to the main menu.</param>
    public static bool Click(FrontEnd menus, MenuPane pane, NuiElement button, bool isHost, Action revive, Action quit)
    {
        if (!pane.File.Equals(File, StringComparison.OrdinalIgnoreCase)) return false;
        menus.PutEscapeAway();
        switch (button.Name.ToLowerInvariant())
        {
            case "emmpsavebutton" when isHost: menus.Ask(MenuRequest.SaveMenu); break;
            case "emmprevivebutton": revive(); break;
            case "emoptionsbutton": Options(menus); break;
            case "emquitbutton": quit(); break;
        }
        return true;
    }

    /// <summary>Options: the Options screen, the game going on under it. False when the front end has none.</summary>
    public static bool Options(FrontEnd menus)
    {
        if (menus.Options is not { } options) return false;
        options.Open();
        return true;
    }
}
