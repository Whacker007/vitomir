// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Always run (ours): *"I'd like to be able to have a kind of "always run"
// toggle in the gameplay options so I don't have to hold down shift for driving mode. If that's ok, then
// shift should change to "walk" instead of run while it's held down, so basically inverting it."*
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **Always run** (ours): <see cref="GameOptions.AlwaysRun"/>, a box of ours on 1.1's Gameplay screen
/// (<c>ui\optionsGameplay.nui</c>), under its two. With it on, **driving** (<c>[qol] driving</c>, ours)
/// runs with nothing held and **the run key held walks** — the run key is <see cref="KeyCommand.Run"/>,
/// either Shift by default, so the rule is simply inverted. What it does not touch:
/// <list type="bullet">
/// <item>**a click's walk** keeps 1.1's rule — a walk within 300 units, a run beyond, and a run with the
/// secondary flag: the option is the keyboard's;</item>
/// <item>**the free camera's fast** (1.1's either Shift), which the run key also is.</item>
/// </list>
/// It is read every frame, so it is in force the moment the Gameplay screen's OK is pressed.
/// </summary>
public static class AlwaysRunOption
{
    /// <summary>The box's control name, and the label's beside it (ours).</summary>
    public const string Box = "opgpAlwaysRun", Label = "opgpAlwaysRunText";

    /// <summary>
    /// Where the box sits (ours): under 1.1's two (subtitles at 435,356 and blood at 435,381, 25 apart),
    /// the same size and art (<c>xButton</c>, <c>xButtonDep</c>).
    /// </summary>
    public const int BoxX = 435, BoxY = 331, BoxW = 13, BoxH = 15;

    /// <summary>
    /// The label (ours). The screen's own words are its art — each centred on a dark bar left of its box —
    /// so this one is a text box in the game's <c>morpheus15</c>, centred across the same span as theirs.
    /// </summary>
    public const int LabelX = 193, LabelY = 328, LabelW = 238, LabelH = 21;
    public const string Caption = "always run";

    /// <summary>Whether driving runs this frame, from the run key held (<see cref="KeyCommand.Run"/>) and the option.</summary>
    public static bool Runs(bool runHeld, GameOptions options) => options.AlwaysRun ? !runHeld : runHeld;

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
    public static void Show(MenuPane pane, GameOptions options) => pane.Check(Box, options.AlwaysRun);

    /// <summary>The Gameplay screen's OK: the setting from the box.</summary>
    public static void Take(MenuPane pane, GameOptions options) => options.AlwaysRun = pane.Checks.GetValueOrDefault(Box);
}
