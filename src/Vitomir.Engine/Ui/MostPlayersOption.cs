// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Most players (ours):, *"Yes, by default and changeable somehow"* — a hosted game allows
// eight, as 1.1 — and a control on 1.1's create-game screen to choose it.
using System.Globalization;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Net;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **The most players** (ours) on 1.1's create-game screen (<c>mpCreateGame</c>): how many may play the game this
/// machine hosts, itself among them, 2 to 8, **8 by default** — 1.1's own, which has no control for it (its session's count,
/// is set to 8 at start-up and never changed; <c>GetMaxMPPlayers</c> reads it). It is drawn and behaves
/// as the screen's own fields: **a third row of the password panel**, under Confirm — the panel made a row taller in the art (the
/// Confirm row copied under itself, the panel's foot moved down a row, <see cref="Row"/>) — with an **edit field like the
/// passwords'** (<see cref="Edit"/>, one digit: 1.1's digits-only field) labelled <i>most players</i> on the panel's plate in the
/// lettering of the screen's other labels (<see cref="Words"/>): with ours in force, as they are relabelled; with 1.1's, whose
/// words are pictures and have none for this, the same words of ours in the same gold where the copied <i>confirm</i> was taken
/// off (<see cref="Plain"/>). Create Game refuses anything but 2 to 8 in a
/// <c>UI_NET_CREATEGAMEERROR</c> box (<see cref="Refusal"/>), as it refuses a missing name; a game made takes it as its limit
/// (<see cref="GameSettings.MaxPlayers"/>), and the host refuses a join past it with 1.1's <c>UI_NET_JOINFULL</c>, as before.
/// <para>**Remembered** in the player's <c>vitomir.ini</c>, <c>[multiplayer] mostplayers</c> (<see cref="Read"/>,
/// <see cref="Write"/>): 1.1 keeps none of this screen's fields (each is filled afresh), so the last choice is kept as Vitomir
/// keeps the last character (<see cref="LastPlayer"/>), written when a game is made. It changes only what this screen shows and the
/// session's limit, and never marks a game modded (it is no Fix and no quality of life).</para>
/// </summary>
public static class MostPlayersOption
{
    /// <summary>The field's control name, and the label's words' in 1.1's lettering (ours).</summary>
    public const string Edit = "mpcgMostPlayers", Label = "mpcgmostplayerswords";

    /// <summary>The key it is kept under, in <see cref="LastPlayer.Section"/> of the player's <c>vitomir.ini</c> (ours).</summary>
    public const string Key = "mostplayers";

    /// <summary>The range: two (a host and one) to 1.1's eight slots (<see cref="GameSettings.Slots"/>), eight by default.</summary>
    public const int Fewest = 2, Most = GameSettings.Slots, Default = GameSettings.Slots;

    /// <summary>
    /// Where the field sits: the Confirm field's place and size (<c>mpcgPWConfirm</c>, 174,242 127 x 15) a row of the panel lower
    /// (16, the panel's rows' pitch: Password at 258, Confirm at 242).
    /// </summary>
    public const int EditX = 174, EditY = 226, EditW = 127, EditH = 15;

    /// <summary>The panel's dark plate left of the field (47 to 172), whose copied word 1.1's lettering takes off.</summary>
    public const int PlateLeft = 47, PlateRight = 172;

    /// <summary>What the label says.</summary>
    public const string Caption = "most players";

    /// <summary>
    /// The label in our lettering, on the art as the screen's own (<c>MenuLettering.CreateGameLabels</c>): where the copied
    /// <i>confirm</i> is (83 to 136, a row lower: the picture's rows 240 to 252), whose gold it takes off and whose middle it is centred on.
    /// </summary>
    public static readonly MenuLettering.Label Words = new(Caption, 83, 136, 240, 252);

    /// <summary>
    /// The art's rows (the 640 x 480 picture's, from the top): the Confirm row with the black line over it (222 to 237), and the
    /// panel's foot under it — the black line, the stone and its ornaments, the rim and its shadow (238 to 261) — across the panel's
    /// columns (40 to 307, the frame's edges at 42 and 305).
    /// </summary>
    public const int RowTop = 222, RowRows = 16, FootTop = 238, FootRows = 24, Left = 40, Right = 308;

    /// <summary>Anything but a whole number from <see cref="Fewest"/> to <see cref="Most"/>, refused by Create Game (ours).</summary>
    public const string Refusal = "The most players is a number from 2 to 8.";

    /// <summary>
    /// **The panel a row taller** (both letterings): its foot moved down a row, then the Confirm row copied into the row it left —
    /// which carries the word <i>confirm</i> with it, for the lettering in force to take off.
    /// </summary>
    public static void Row(LiftedArt art)
    {
        art.CopyRows(FootTop, FootRows, FootTop + RowRows, Left, Right);
        art.CopyRows(RowTop, RowRows, RowTop + RowRows, Left, Right);
    }

    /// <summary>
    /// **The screen in 1.1's lettering** (<c>MenuLettering.Dresser</c> while 1.1's is in force): the panel's new row, with the copied
    /// word taken off its plate as the Options screens take 1.1's words off (<see cref="LiftedArt.LabelOff"/>), and the label's
    /// words put where it was. 1.1's words are pictures and it has none for this one, so they are ours (the plate's gold, as our
    /// lettering's labels): the one label on the screen not 1.1's own picture. (The Gameplay screen's always run, put a
    /// <c>morpheus15</c> text box on 1.1's art instead; that font draws capitals in white, unlike any label here.)
    /// </summary>
    public static NuiPane Plain(UiAssets assets, NuiPane nui)
    {
        var art = LiftedArt.Of(assets, nui);
        Row(art);
        art.LabelOff(Words.Top, Words.Bottom, PlateLeft, PlateRight);
        art.Commit("ours/mpcreategame");
        nui.Elements.Add(MenuLettering.Boxed(Label, Words.Text, Words.X, Words.Middle, Words.Style, Words.Em));
        return nui;
    }

    /// <summary>
    /// The field put on the create screen once, as it is first built (<see cref="FrontEnd.Pane(string)"/>), whatever the lettering:
    /// on a copy, so 1.1's pane as the assets hold it is never changed. Any other pane comes back as it was.
    /// </summary>
    public static NuiPane Add(NuiPane nui, string file)
    {
        if (!Path.GetFileNameWithoutExtension(file).Equals(MpMenus.CreateGameFile, StringComparison.OrdinalIgnoreCase)
            || nui.Elements.Any(e => e.Name.Equals(Edit, StringComparison.OrdinalIgnoreCase))) return nui;
        var ours = nui.Copy();
        // After the two password fields, so it is the last of the focus ring (Tab from Confirm comes to it).
        ours.Elements.Add(new NuiElement("edit", new Dictionary<string, string>
        {
            ["name"] = Edit.ToLowerInvariant(), ["pos"] = $"{EditX},{EditY}", ["size"] = $"{EditW},{EditH}", ["flags"] = "0x02",
        }, 0));
        return ours;
    }

    /// <summary>The field as the screen opens: one digit, digits only, showing <paramref name="most"/>.</summary>
    public static void Show(MenuPane pane, int most)
    {
        var field = pane.Edits[Edit];
        field.Accepts = EditAccepts.Digits;                       // 1.1's digits-only field, as the pack's drop-cash box asks
        // One digit: a text set outright keeps one fewer than the limit (EditMask.SetCut), so it is set under a limit
        // of two and the field then held to one, which typing fills (the edit takes none past it).
        field.Limit(2);
        field.Set(most.ToString(CultureInfo.InvariantCulture));
        field.Limit(1);
    }

    /// <summary>The field's words as the most players: a whole number from 2 to 8, or false.</summary>
    public static bool Parse(string text, out int most) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out most) && most >= Fewest && most <= Most;

    /// <summary>The last choice kept in <paramref name="settingsPath"/>, or <see cref="Default"/> (none, or not 2 to 8); null, the default.</summary>
    public static int Read(string? settingsPath) =>
        settingsPath is not null && IniFile.Read(settingsPath)[LastPlayer.Section + "." + Key] is { } kept && Parse(kept, out int most) ? most : Default;

    /// <summary>Keeps <paramref name="most"/> as the last choice, the file's other lines left as they are.</summary>
    public static void Write(string settingsPath, int most) =>
        SettingsFile.Set(settingsPath, LastPlayer.Section, Key, most.ToString(CultureInfo.InvariantCulture));
}
