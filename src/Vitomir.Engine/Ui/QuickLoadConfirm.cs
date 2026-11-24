// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Confirm quick load (ours): *"Leave it non confirm by default,
// but next run add a feature to turn on a confirm before quickloading, and have it default off."*
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **Confirm quick load** (ours): <see cref="GameOptions.ConfirmQuickLoad"/>, a box of ours on 1.1's
/// Gameplay screen (<c>ui\optionsGameplay.nui</c>). Off (the default) the quick load loads its slot with
/// no question (<see cref="QuickSave"/>). On, it asks first in **the game's own message box**
/// (<c>ui\messageBox.nui</c>, <see cref="FrontEnd.Message"/>) — the one the single-player menu's
/// New asks in when an autosave would be overwritten — titled as that one is ("Warning!",
/// <c>UI_WARNING</c>), in the words of the save screen's own question
/// (<c>UI_SPLOADSAVE_OVERWRITESAVE</c>'s "OK to continue?"). **OK loads** as the unasked key would;
/// **Cancel** (or Escape) leaves the game as it was. The game is paused while it asks, as the load screen
/// pauses it, and the pause is put back as it was either way. Only the quick load asks: the
/// load screen, which is 1.1's, asks nothing as 1.1's does, and a quick load that opens the load
/// screen (no slot yet) asks nothing either, since the screen is the question. It is read when the key is
/// pressed, so it is in force the moment the Gameplay screen's OK is.
/// </summary>
public static class QuickLoadConfirm
{
    /// <summary>The box's control name, and the label's beside it (ours).</summary>
    public const string Box = "opgpConfirmQuickLoad", Label = "opgpConfirmQuickLoadText";

    /// <summary>
    /// Where the box sits (ours): **above** 1.1's two, 25 over the blood box (381) as each of theirs is 25
    /// from the next, in the empty band between the panel's top border and the blood bar — below
    /// <see cref="AlwaysRunOption"/> the panel's lower border is drawn, and there is no room.
    /// </summary>
    public const int BoxX = 435, BoxY = 406, BoxW = 13, BoxH = 15;

    /// <summary>The label (ours), as <see cref="AlwaysRunOption"/>'s: <c>morpheus15</c>, centred across 1.1's labels' span.</summary>
    public const int LabelX = 193, LabelY = 403, LabelW = 238, LabelH = 21;
    public const string Caption = "confirm quick load";

    /// <summary>The question's text (ours), after the save screen's own (<c>UI_SPLOADSAVE_OVERWRITESAVE</c>).</summary>
    public const string Question = "{0} will be loaded, and the game since it was saved lost.  OK to continue?";

    /// <summary>The box and its label, put on the Gameplay screen once, as it is loaded (<see cref="OptionsMenu.AddOurButtons"/>).</summary>
    public static void Add(MenuPane pane)
    {
        if (!pane.File.Equals(OptionsMenu.GameplayFile, StringComparison.OrdinalIgnoreCase) || pane.Element(Box) is not null) return;
        pane.Nui.Elements.Add(new NuiElement("checkbox", new Dictionary<string, string>
        {
            ["name"] = Box, ["pos"] = $"{BoxX},{BoxY}", ["size"] = $"{BoxW},{BoxH}",
            ["checkedbitmap"] = "xButtonDep", ["bitmap"] = "xButton",
        }, 0));
        pane.Nui.Elements.Add(new NuiElement("textbox", new Dictionary<string, string>
        {
            ["name"] = Label, ["pos"] = $"{LabelX},{LabelY}", ["size"] = $"{LabelW},{LabelH}",
            ["font"] = "morpheus15", ["flags"] = "0x02", ["textboxflags"] = "0x03",
        }, 0));
        pane.Checks[Box] = false;
        pane.Texts[Label] = Caption;
    }

    /// <summary>The Gameplay screen opening: the box from the setting.</summary>
    public static void Show(MenuPane pane, GameOptions options) => pane.Check(Box, options.ConfirmQuickLoad);

    /// <summary>The Gameplay screen's OK: the setting from the box.</summary>
    public static void Take(MenuPane pane, GameOptions options) => options.ConfirmQuickLoad = pane.Checks.GetValueOrDefault(Box);

    /// <summary>
    /// The quick load of <paramref name="slot"/> (<see cref="QuickAct.Load"/>): at once with the option off,
    /// otherwise the question first and the load on its OK. False only when the question could not be put —
    /// a message box already up (one at a time) — and then nothing is done.
    /// </summary>
    public static bool Load(FrontEnd menus, int slot, GameOptions options, Func<string, string?> find)
    {
        if (!options.ConfirmQuickLoad)
        {
            menus.Ask(MenuRequest.Restore, slot);
            return true;
        }
        bool paused = menus.Paused;
        string name = Strings.Format(find, "UI_SPLOADSAVE_SLOT", $"%d{slot}");
        bool asked = menus.Message(find("UI_WARNING") ?? "Warning!", string.Format(Question, name), ok: true, cancel: true, result =>
        {
            if (result == 1) menus.Ask(MenuRequest.Restore, slot);
            menus.Paused = paused;
        });
        if (asked && menus.SinglePlayer) menus.Paused = true;
        return asked;
    }
}
