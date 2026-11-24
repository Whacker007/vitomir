// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Our lettering on 1.1's screens (ours): every
// word of 1.1's menus drawn by the engine in Vitomir Uncial, 1.1's own included, on 1.1's own panels.
using System.Globalization;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>Whose lettering 1.1's menus wear: ours (the default), or 1.1's own pictures of its words.</summary>
public enum MenuLetteringMode { Ours, Original }

/// <summary>
/// **Our lettering on 1.1's screens** (ours). 1.1 draws its menus' words as pictures — in a pane's tiles, or as
/// a button's picture over them — and ships no font for them, so a word of ours beside them could never match. The aim:
/// faithful renditions of our own as actual fonts, used in place of the original game's
/// menu items, so that how the menus look can be fixed while they still look almost exactly like the 1.1 menus:
/// our own version of the original styles, at a higher quality. So, for a pane that has a recipe here:
/// <list type="number">
/// <item>**1.1's words come off its art** (<see cref="LiftedArt"/>), from the player's own copy, when the pane is built;</item>
/// <item>**a button keeps its place and its clicks, and loses its pictures**: its word is drawn by the engine, beige,
/// red under the pointer, white while pressed, as 1.1's pictures were;</item>
/// <item>**every word is a `words` control of ours** (<see cref="Words"/>), which the menu pass draws in Vitomir Uncial
/// (<c>Lettering.Uncial</c>; a capital from Vitomir Capitals) from the engine's distance-field text;</item>
/// <item>**our own changes** are made in the same place (<c>MenuLetteringPanes.cs</c>): a keybinds row and the
/// fixes row on the Options screen, four bars on the Gameplay screen, our two pages' titles and buttons.</item>
/// </list>
/// <see cref="MenuLetteringMode.Original"/> keeps 1.1's pictures and our captions in its bitmap fonts, as first drew
/// them. **A recording carries the lettering it was made in** (<c>interface lettering=ours</c>; a tape without the line
/// was made in 1.1's), because the Options screen's rows are not where 1.1 has them.
/// </summary>
public static partial class MenuLettering
{
    /// <summary>The settings file's section and key: <c>[interface] lettering = ours | 1.1</c>.</summary>
    public const string Section = "interface", Key = "lettering";

    /// <summary>The lettering in force: ours unless something says otherwise.</summary>
    public static MenuLetteringMode Mode { get; set; } = MenuLetteringMode.Ours;

    /// <summary>
    /// Reads the lettering the player asked for and puts it in force: <c>--menu-lettering</c> on the command line, else
    /// <c>[interface] lettering</c> of the player's settings file, else ours. **Only the program calls this.**
    /// </summary>
    public static MenuLetteringMode Configure(string? flag, Action<string>? complain = null, string? settingsPath = null)
    {
        var mode = MenuLetteringMode.Ours;
        string path = settingsPath ?? Diagnostics.Diag.UserSettingsPath;
        if (File.Exists(path) && Diagnostics.IniFile.Read(path)[Section + "." + Key] is { Length: > 0 } kept)
        {
            if (Parse(kept) is { } m) mode = m;
            else complain?.Invoke($"[{Section}] {Key} = {kept} is neither ours nor 1.1; ours");
        }
        if (flag is not null)
        {
            if (Parse(flag) is { } m) mode = m;
            else complain?.Invoke($"--menu-lettering {flag} is neither ours nor 1.1; left as {Word(mode)}");
        }
        return Mode = mode;
    }

    /// <summary>
    /// What a host gives the front end (<see cref="FrontEnd.Dress"/>): a pane dressed while ours is in force when the pane is first
    /// built; while 1.1's is, only the art our controls need (the create screen's most players, <see cref="MostPlayersOption.Plain"/>).
    /// </summary>
    public static Func<NuiPane, string, NuiPane> Dresser(UiAssets assets) =>
        (nui, file) => Mode == MenuLetteringMode.Ours ? Dress(assets, nui, file)
            : Is(Path.GetFileNameWithoutExtension(file), MpMenus.CreateGameFile) ? MostPlayersOption.Plain(assets, nui) : nui;

    /// <summary>The word a setting, a flag or a tape gives for each.</summary>
    public static string Word(MenuLetteringMode mode) => mode == MenuLetteringMode.Ours ? "ours" : "1.1";

    /// <summary>A setting's word as a mode: <c>ours</c>, or <c>1.1</c> (also <c>original</c>); null for anything else.</summary>
    public static MenuLetteringMode? Parse(string? word) => word?.Trim().ToLowerInvariant() switch
    {
        "ours" or "vitomir" => MenuLetteringMode.Ours,
        "1.1" or "original" or "11" => MenuLetteringMode.Original,
        _ => null,
    };

    /// <summary>The control type of a word of ours (no file of 1.1's has it).</summary>
    public const string WordsType = "words";

    /// <summary>How a word is dressed: a menu word (beige, a soft dark glow), a panel's word (gold, a dark rim) or a title (gold, larger).</summary>
    public const string MenuStyle = "menu", PanelStyle = "panel", TitleStyle = "title";

    /// <summary>The ems of 1.1's sizes (the uncial's x-height is half an em): a menu column's words, a sub-screen's buttons, a panel's labels, a plate's title.</summary>
    public const float MenuEm = 23f, ButtonEm = 19f, PanelEm = 11.5f, TitleEm = 13.5f;

    /// <summary>
    /// A word of ours on a pane: <paramref name="text"/> with its baseline at sheet (<paramref name="x"/>,
    /// <paramref name="baseline"/>) — <paramref name="x"/> its middle, or its left end with <paramref name="align"/>
    /// <c>left</c> — at an em of <paramref name="em"/> units, in <paramref name="style"/>; following
    /// <paramref name="button"/>'s state when it names one (and hidden with it).
    /// </summary>
    public static NuiElement Words(string name, string text, float x, float baseline, string style, float em,
                                   string? button = null, string align = "center")
    {
        var attrs = new Dictionary<string, string>
        {
            ["name"] = name, ["pos"] = $"{F(x)},{F(baseline)}", ["size"] = "0,0", ["text"] = text,
            ["style"] = style, ["em"] = F(em), ["align"] = align,
        };
        if (button is not null) attrs["button"] = button;
        return new NuiElement(WordsType, attrs, 0);
    }

    static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>A word control's numbers, read back: where, how big, and how it is set.</summary>
    public readonly record struct Placed(string Text, float X, float Baseline, float Em, string Style, string Align, string? Button);

    public static Placed Read(NuiElement words)
    {
        var pos = (words.Get("pos") ?? "0,0").Split(',');
        float At(int i) => pos.Length > i && float.TryParse(pos[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
        float em = float.TryParse(words.Get("em"), NumberStyles.Float, CultureInfo.InvariantCulture, out var e) ? e : MenuEm;
        return new Placed(words.Get("text") ?? "", At(0), At(1), em, words.Get("style") ?? MenuStyle, words.Get("align") ?? "center", words.Get("button"));
    }

    /// <summary>
    /// How wide a line of our lettering is at an em of <paramref name="em"/>: its lower case in Vitomir Uncial, a
    /// capital in Vitomir Capitals at <see cref="CapitalScale"/> of the em (so its height sits a little over the
    /// uncial's x-height, as 1.1's own capitals in *coterie AI options* do).
    /// </summary>
    public static float Measure(string text, float em)
    {
        float w = 0f;
        foreach (var (run, capitals) in Runs(text))
            w += capitals ? Lettering.Load(Lettering.Capitals).Measure(run, em * CapitalScale) : Lettering.Load(Lettering.Uncial).Measure(run, em);
        return w;
    }

    /// <summary>How far a line's ink reaches below and above its baseline at an em of <paramref name="em"/>, capitals and all.</summary>
    public static (float Bottom, float Top) Ink(string text, float em)
    {
        float bottom = float.MaxValue, top = float.MinValue;
        foreach (var (run, capitals) in Runs(text))
        {
            var (b, t) = capitals ? Lettering.Load(Lettering.Capitals).Ink(run, em * CapitalScale) : Lettering.Load(Lettering.Uncial).Ink(run, em);
            if (b == 0f && t == 0f) continue;
            bottom = Math.Min(bottom, b);
            top = Math.Max(top, t);
        }
        return bottom == float.MaxValue ? (0f, 0f) : (bottom, top);
    }

    /// <summary>
    /// The baseline that puts a line's ink in the middle of a box whose middle is at <paramref name="centre"/>: as much
    /// room over its tallest letter as under its lowest (<i>"Any situation like this, in any menu,
    /// let's have the text just a hair lower so there's slightly more even space above and below it inside the menu box
    /// containing the text"</i>).
    /// </summary>
    public static float Centred(string text, float em, float centre)
    {
        var (bottom, top) = Ink(text, em);
        return centre - (bottom + top) / 2f;
    }

    /// <summary>A word of ours set in a plate or on a bar: its ink centred on <paramref name="centre"/> (<see cref="Centred"/>).</summary>
    public static NuiElement Boxed(string name, string text, float x, float centre, string style, float em, string? button = null) =>
        Words(name, text, x, Centred(text, em, centre), style, em, button);

    /// <summary>A capital's em, as a share of the line's.</summary>
    public const float CapitalScale = 0.85f;

    /// <summary>A line cut into runs of lower case (the uncial's) and of capitals (the capitals face's).</summary>
    public static IEnumerable<(string Run, bool Capitals)> Runs(string text)
    {
        int start = 0;
        for (int i = 1; i <= text.Length; i++)
        {
            if (i < text.Length && char.IsUpper(text[i]) == char.IsUpper(text[start])) continue;
            yield return (text[start..i], char.IsUpper(text[start]));
            start = i;
        }
    }

    /// <summary>
    /// **A pane put in our lettering**, when there is a recipe for it (the Options screen and two of its sub-screens,
    /// our two pages): 1.1's words taken off its art, its buttons' pictures dropped, our words added, and the changes
    /// asked for made. Any other pane comes back as it was. Called once, when the front end first builds the pane.
    /// </summary>
    public static NuiPane Dress(UiAssets assets, NuiPane nui, string file)
    {
        string stem = Path.GetFileNameWithoutExtension(file);
        if (stem.Equals(OptionsMenu.OptionsFile, StringComparison.OrdinalIgnoreCase)) Options(assets, nui);
        else if (stem.Equals(OptionsMenu.GameplayFile, StringComparison.OrdinalIgnoreCase)) Gameplay(assets, nui);
        else if (stem.Equals(OptionsMenu.ControlsFile, StringComparison.OrdinalIgnoreCase)) Controls(assets, nui);
        else if (stem.Equals(OptionsMenu.DisplayFile, StringComparison.OrdinalIgnoreCase)) Display(assets, nui);
        else if (stem.Equals(OptionsMenu.AdvancedFile, StringComparison.OrdinalIgnoreCase)) Advanced(assets, nui);
        else if (stem.Equals(OptionsMenu.SoundFile, StringComparison.OrdinalIgnoreCase)) Sound(assets, nui);
        // 1.1's front-end menus
        else if (Is(stem, FrontEnd.MainMenuFile)) WindowMenu(assets, nui, MainMenuWords, "mainmenu", upright: 1f);
        else if (Is(stem, FrontEnd.SpFile)) WindowMenu(assets, nui, SpWords, "sp", upright: 8f);
        else if (Is(stem, MpMenus.SelectFile)) WindowMenu(assets, nui, MpSelectWords, "mpselect", upright: 8f);
        else if (Is(stem, FrontEnd.EscapeFile)) WindowMenu(assets, nui, EscapeWords, "escapemenu", upright: 1f);
        else if (Is(stem, NetEscape.File)) WindowMenu(assets, nui, EscapeMpWords, "escapemenump", upright: 1f);
        else if (Is(stem, FrontEnd.LoadFile)) LoadOrSave(assets, nui, "load game", "spload", [new("sploadcancelbutton", "cancel"), new("sploadloadbutton", "load")]);
        else if (Is(stem, FrontEnd.SaveFile)) LoadOrSave(assets, nui, "save game", "spsave", [new("spsavecancelbutton", "cancel"), new("spsavesavebutton", "save")]);
        else if (Is(stem, FrontEnd.MessageBoxFile)) Dialog(assets, nui, "mbokbutton", "mbcancelbutton", "messagebox");
        else if (Is(stem, FrontEnd.InputBoxFile)) Dialog(assets, nui, "ibokbutton", "ibcancelbutton", "inputbox");
        else if (Is(stem, FrontEnd.CreditsPaneFile)) Mark(nui);
        else if (Is(stem, MpMenus.CreateGameFile)) MpCreateGame(assets, nui);
        else if (Is(stem, MpMenus.PlayerCreateFile)) MpPlayerCreate(assets, nui);
        else if (Is(stem, MpMenus.PlayerSelectFile)) MpPlayerSelect(assets, nui);
        else if (Is(stem, MpMenus.ChatFile)) MpChat(assets, nui);
        else if (Is(stem, FrontEnd.LoadScreenFile)) Plaque(assets, nui, LoadingLabel, "loadscreen");
        else if (Is(stem, MpMenus.LoadFile)) Plaque(assets, nui, CommunicatingLabel, "networkloadscreen");
        else if (stem.Equals(FixesPage.File, StringComparison.OrdinalIgnoreCase)) OurPage(assets, nui, FixesPage.Title, "fxtitle", null);
        else if (stem.Equals(KeysPage.File, StringComparison.OrdinalIgnoreCase)) OurPage(assets, nui, KeysPage.Title, "kbtitle", KeysPage.DefaultsButton);
        return nui;
    }

    /// <summary>Every pane with a recipe here: the Options tree and our two pages, 1.1's front-end menus.</summary>
    public static readonly string[] Recipes =
    [
        OptionsMenu.OptionsFile, OptionsMenu.GameplayFile, OptionsMenu.ControlsFile, OptionsMenu.DisplayFile, OptionsMenu.AdvancedFile,
        OptionsMenu.SoundFile, FixesPage.File, KeysPage.File,
        FrontEnd.MainMenuFile, FrontEnd.SpFile, MpMenus.SelectFile, FrontEnd.EscapeFile, NetEscape.File, FrontEnd.LoadFile, FrontEnd.SaveFile,
        FrontEnd.MessageBoxFile, FrontEnd.InputBoxFile, FrontEnd.CreditsPaneFile, MpMenus.CreateGameFile, MpMenus.PlayerCreateFile,
        MpMenus.PlayerSelectFile, MpMenus.ChatFile, FrontEnd.LoadScreenFile, MpMenus.LoadFile,
    ];

    static bool Is(string stem, string file) => stem.Equals(file, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a pane has a recipe here (so a host knows the Options screens are ours).</summary>
    public static bool Dresses(string file)
    {
        string stem = Path.GetFileNameWithoutExtension(file);
        return Recipes.Any(f => f.Equals(stem, StringComparison.OrdinalIgnoreCase));
    }

    // ---- the tools the recipes share ----------------------------------------------------------------------------

    /// <summary>An element with these attributes taken away and these changed (an element is immutable).</summary>
    static NuiElement Changed(NuiElement e, string[]? without = null, IReadOnlyDictionary<string, string>? with = null)
    {
        var attrs = new Dictionary<string, string>(e.Attrs);
        foreach (var k in without ?? []) attrs.Remove(k);
        foreach (var (k, v) in with ?? new Dictionary<string, string>()) attrs[k] = v;
        return e with { Attrs = attrs };
    }

    static void Replace(NuiPane nui, NuiElement old, NuiElement now)
    {
        int i = nui.Elements.IndexOf(old);
        if (i >= 0) nui.Elements[i] = now;
    }

    static NuiElement? Find(NuiPane nui, string name) =>
        nui.Elements.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The attributes that give a button its pictures.</summary>
    static readonly string[] Pictures = ["bitmap", "rollover", "depressed", "greyed"];

    /// <summary>
    /// **1.1's Cancel and OK, whose words are in the tiles** (the button has only its rollover and pressed pictures):
    /// the words found where the rollover picture differs from the tiles, a little more for their rims, and filled
    /// from round them; the button left without pictures, our word drawn in their place.
    /// </summary>
    static void BakedButtons(UiAssets assets, LiftedArt art, IEnumerable<(string Over, int X, int Y, int W, int H)> buttons)
    {
        var mask = art.Mask();
        foreach (var (over, x, y, w, h) in buttons)
            if (assets.Texture(over) is { } o) art.DifferFromArt(mask, o, x, y, w, h);
        art.Fill(art.Grown(mask, 2), 1f, 400);
    }

    /// <summary>1.1's Cancel and OK at the foot of every Options sub-screen, and their rollover pictures (`optionsGameplay.nui`).</summary>
    static readonly (string Over, int X, int Y, int W, int H)[] CancelOk =
    [
        ("opsdisadvcancelbuttonover", 220, 12, 91, 41), ("opsdisadvokbuttonover", 332, 12, 91, 41),
    ];

    /// <summary>Where 1.1's Cancel and OK words sit: their middles, and the baseline they share (measured off the art).</summary>
    public const float CancelX = 265f, OkX = 377f, ButtonBaseline = 32.5f;

    /// <summary>A sub-screen's Cancel and OK buttons stripped of their pictures, with our words.</summary>
    static void CancelOkWords(NuiPane nui, string cancel, string ok)
    {
        foreach (var name in new[] { cancel, ok })
            if (Find(nui, name) is { } b) Replace(nui, b, Changed(b, Pictures));
        nui.Elements.Add(Words(cancel + "words", "cancel", CancelX, ButtonBaseline, MenuStyle, ButtonEm, cancel));
        nui.Elements.Add(Words(ok + "words", "ok", OkX, ButtonBaseline, MenuStyle, ButtonEm, ok));
    }
}
