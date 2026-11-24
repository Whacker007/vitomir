// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The recipes (ours): for each pane put in our lettering, what comes off 1.1's art, where our words go and what
// else changes. Every number here is a place on 1.1's own art, measured off the player's copy in the stills of;
// rows of the pane's picture run down from its top.
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

public static partial class MenuLettering
{
    // ---- the Options screen ---------------------------------------------------------------------------------------

    /// <summary>
    /// The Options screen's rows (ours): 1.1's five and two of ours — **keybinds** under controls (<i>"Add a
    /// 'keybinds' option below controls on the options menu"</i>) and **fixes** above done — in that order, their
    /// buttons' names beside them.
    /// </summary>
    public static readonly (string Word, string Button)[] OptionsRows =
    [
        ("game", "opgameplaybutton"), ("controls", "opcontrolsbutton"), ("keybinds", OptionsMenu.KeybindsButton),
        ("display", "opdisplaybutton"), ("sound", "opsoundbutton"), ("fixes", OptionsMenu.FixesButton), ("done", "opdonebutton"),
    ];

    /// <summary>
    /// The rows' baselines: the first where 1.1 has *game* (its button at 341, the word 11.5 over the button's foot),
    /// the rest **35 apart** (evenly; 1.1's five are about 40.5 apart). At 35 the window's lower crossbar (sheet
    /// 167) falls between fixes and done, not through a word.
    /// </summary>
    public const float OptionsFirst = 352.5f, OptionsPitch = 35f, OptionsMiddle = 320.5f;

    /// <summary>A row's button: as wide as its word and a margin (at least 1.1's done, 86), 33 high from 9.5 under the baseline.</summary>
    public static (int X, int Y, int W, int H) OptionsButton(int row)
    {
        float baseline = OptionsFirst - OptionsPitch * row;
        int w = Math.Max(86, (int)MathF.Ceiling(Measure(OptionsRows[row].Word, MenuEm)) + 24);
        return ((int)MathF.Round(OptionsMiddle - w / 2f), (int)MathF.Floor(baseline - 9.5f), w, 33);
    }

    /// <summary>
    /// The Options screen. 1.1's window behind the words is the single-player menu's own (<c>sp.nui</c>: the same
    /// tiles but the words' ones, its words in other rows), so where 1.1's words and their dark halo are, the
    /// single-player menu's pixels are taken; where both have words, the gap is filled leaning on the pixels above
    /// and below (the window's bars and the door's planks are upright), and the crossbar above the first row is
    /// carried across from both sides.
    /// </summary>
    static void Options(UiAssets assets, NuiPane nui)
    {
        var art = LiftedArt.Of(assets, nui);
        var letters = art.Mask();
        WithButtons(assets, art, nui, letters);
        var sp = assets.Pane("sp");
        var twin = LiftedArt.Of(assets, sp);
        var theirs = twin.Mask();
        WithButtons(assets, twin, sp, theirs);
        var mine = art.Grown(letters, 12);
        var other = twin.Grown(theirs, 12);
        var take = new bool[mine.Length];
        var both = new bool[mine.Length];
        for (int i = 0; i < mine.Length; i++)
        {
            take[i] = mine[i] && !other[i];
            both[i] = mine[i] && other[i];
        }
        art.Take(twin, take);
        art.Fill(both, upright: 8f, steps: 800);
        var crossbar = new bool[both.Length];
        for (int y = 109; y <= 116; y++)
            for (int x = 0; x < art.Width; x++) crossbar[y * art.Width + x] = both[y * art.Width + x];
        art.Fill(crossbar, upright: 0.05f, steps: 800);
        art.Commit("lettered/options");

        // The buttons: 1.1's five moved to their rows and stripped of their pictures, ours added, a word on each.
        for (int row = 0; row < OptionsRows.Length; row++)
        {
            var (word, name) = OptionsRows[row];
            var (x, y, w, h) = OptionsButton(row);
            var place = new Dictionary<string, string> { ["pos"] = $"{x},{y}", ["size"] = $"{w},{h}" };
            if (Find(nui, name) is { } b) Replace(nui, b, Changed(b, Pictures, place));
            else nui.Elements.Add(new NuiElement("button", new Dictionary<string, string>(place) { ["name"] = name }, 0));
            nui.Elements.Add(Words(name + "words", word, OptionsMiddle, OptionsFirst - OptionsPitch * row, MenuStyle, MenuEm, name));
        }
    }

    /// <summary>
    /// A pane's buttons laid over its art as 1.1 draws them at rest, and their letters marked: where the button's
    /// picture and its rollover differ, and where the picture differs from the tiles under it (the Options screen's
    /// tiles say *gameplay* under the button that says *game*).
    /// </summary>
    static void WithButtons(UiAssets assets, LiftedArt art, NuiPane nui, bool[] letters)
    {
        foreach (var e in nui.Elements)
        {
            if (e.Type != "button" || e.Get("bitmap") is not { } normal || e.Get("rollover") is not { } over) continue;
            if (assets.Texture(normal) is not { } n || assets.Texture(over) is not { } o) continue;
            int x = e.Pos[0], y = e.Pos[1], w = e.Size[0], h = e.Size[1];
            art.DifferFromArt(letters, n, x, y, w, h);
            art.Paste(n, x, y, w, h);
            art.Differ(letters, n, o, x, y, w, h);
        }
    }

    // ---- the Gameplay screen --------------------------------------------------------------------------------------

    /// <summary>
    /// The Gameplay screen's bars (ours): 1.1's two and ours on bars like theirs, 25 apart, each with its box at the
    /// bar's right end (1.1's: 435,381 and 435,356) — and the panel lengthened by two of its own bars to hold them.
    /// </summary>
    public static readonly (string Word, string Box, int BoxY)[] GameplayRows =
    [
        ("no blood", "opgpBloodOn", 381), ("subtitling", "opgpSubtitling", 356),
        ("confirm quick load", QuickLoadConfirm.Box, 331), ("always run", AlwaysRunOption.Box, 306),
    ];

    /// <summary>
    /// A bar's label: its middle across (the bar's, left of the box), and the middle of the bar's dark inside over the
    /// box's foot — 1.1's Gameplay bars run from the box's foot 15 up, the Controls screen's from a unit over it.
    /// </summary>
    public const float BarMiddle = 312f, GameplayBarCentre = 7.5f, ControlBarCentre = 8f;

    /// <summary>The middle of a title plate's dark inside (rows 13 to 27 of the picture on every Options sub-screen and on our pages).</summary>
    public const float PlateCentre = 459.5f;

    static void Gameplay(UiAssets assets, NuiPane nui)
    {
        var art = LiftedArt.Of(assets, nui);
        art.LabelOff(12, 28, 196, 444);                          // game options
        art.LabelOff(85, 99, 192, 430);                          // no blood
        art.LabelOff(110, 124, 192, 430);                        // subtitling
        BakedButtons(assets, art, CancelOk);
        // Two more bars: the panel below the second (rows 126 to 180) moved 50 down, and the gap-and-bar above the
        // second (rows 101 to 125) laid twice in the room it leaves.
        art.CopyRows(126, 55, 176, 184, 457);
        art.CopyRows(101, 25, 126, 184, 457);
        art.CopyRows(101, 25, 151, 184, 457);
        art.Commit("lettered/optionsgameplay");

        nui.Elements.Add(Boxed("opgptitlewords", "game options", 320f, PlateCentre, TitleStyle, TitleEm));
        foreach (var (word, box, boxY) in GameplayRows)
        {
            if (Find(nui, box) is null)
                nui.Elements.Add(new NuiElement("checkbox", new Dictionary<string, string>
                {
                    ["name"] = box, ["pos"] = $"435,{boxY}", ["size"] = "13,15", ["checkedbitmap"] = "xButtonDep", ["bitmap"] = "xButton",
                }, 0));
            nui.Elements.Add(Boxed(box + "words", word, BarMiddle, boxY + GameplayBarCentre, PanelStyle, PanelEm));
        }
        CancelOkWords(nui, "opgpcancelbutton", "opgpokbutton");
    }

    // ---- the Controls screen --------------------------------------------------------------------------------------

    /// <summary>The Controls screen's ten rows (1.1's words), by their boxes.</summary>
    public static readonly (string Word, string Box)[] ControlRows =
    [
        ("enable mouse pitch", "opConMousePitch"), ("auto camera slewing", "opConCameraSlew"),
        ("auto repeat attack", "opConRepeatAttack"), ("auto-load quick slot from backpack", "opConAutoQuickSlot"),
        ("use quick disciplines only", "opConQuickDisc"), ("use quick items only", "opConQuickItems"),
        ("auto cast healing", "opConHealing"), ("auto cast buff", "opConBuff"),
        ("auto cast damage", "opConDamage"), ("auto cast special", "opConSpecial"),
    ];

    /// <summary>The Controls screen's words to take off: rows of its picture and the plate or bar they are on.</summary>
    static readonly (int Top, int Bottom, int Left, int Right)[] ControlBands =
    [
        (12, 28, 196, 444),                                                         // control options
        (66, 79, 192, 430), (82, 95, 192, 430), (98, 111, 192, 430), (114, 127, 192, 430),
        (188, 202, 196, 444),                                                       // coterie AI options
        (243, 256, 192, 430), (259, 272, 192, 430),
        (287, 301, 232, 403),                                                       // auto discipline casting
        (308, 321, 192, 430), (324, 337, 192, 430), (340, 353, 192, 430), (356, 369, 192, 430),
    ];

    static void Controls(UiAssets assets, NuiPane nui)
    {
        var art = LiftedArt.Of(assets, nui);
        foreach (var (top, bottom, left, right) in ControlBands) art.LabelOff(top, bottom, left, right);
        BakedButtons(assets, art, CancelOk);
        art.Commit("lettered/optionscontrols");

        nui.Elements.Add(Boxed("opcontitlewords", "control options", 319.5f, PlateCentre, TitleStyle, TitleEm));
        nui.Elements.Add(Boxed("opconcoteriewords", "coterie AI options", 319.5f, 284.5f, TitleStyle, TitleEm));     // rows 188 to 202
        nui.Elements.Add(Boxed("opconcastingwords", "auto discipline casting", 317.5f, 185.5f, TitleStyle, PanelEm)); // rows 288 to 300
        foreach (var (word, box) in ControlRows)
            if (Find(nui, box) is { } b)
                nui.Elements.Add(Boxed(box + "words", word, 311f, b.Pos[1] + ControlBarCentre, PanelStyle, PanelEm));
        CancelOkWords(nui, "opconcancelbutton", "opconokbutton");
    }

    // ---- our two pages ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The Fixes page and the key page (ours, on the load screen's frame): the load screen's title plate's words
    /// (*load game*) taken off it in place of the console line laid over it, 1.1's Cancel and OK taken off the tiles,
    /// the title, Cancel and OK — and the key page's defaults — in our words. The key page's three are centred under
    /// its list, evenly apart; the Fixes page's two stay where every sub-screen has them.
    /// </summary>
    static void OurPage(UiAssets assets, NuiPane nui, string title, string titleBox, string? defaults)
    {
        nui.Quads.RemoveAll(q => q.Material.StartsWith("consoleline", StringComparison.OrdinalIgnoreCase));
        var art = LiftedArt.Of(assets, nui);
        art.LabelOff(10, 30, 196, 444);
        BakedButtons(assets, art, CancelOk);
        art.Commit("lettered/" + Path.GetFileNameWithoutExtension(nui.Name.Length > 0 ? nui.Name : title).ToLowerInvariant());
        if (Find(nui, titleBox) is { } t) nui.Elements.Remove(t);
        nui.Elements.Add(Boxed(titleBox + "words", title, 320f, PlateCentre, TitleStyle, TitleEm));
        var buttons = nui.Elements.Where(e => e.Type == "button" && (e.Name.EndsWith("okbutton") || e.Name.EndsWith("cancelbutton"))).ToList();
        string cancel = buttons.First(b => b.Name.EndsWith("cancelbutton")).Name, ok = buttons.First(b => b.Name.EndsWith("okbutton")).Name;
        if (defaults is null)
        {
            CancelOkWords(nui, cancel, ok);
            return;
        }
        foreach (var (name, word, middle) in KeysRow(defaults, cancel, ok))
        {
            int w = (int)MathF.Ceiling(Measure(word, ButtonEm)) + 16;
            var place = new Dictionary<string, string> { ["pos"] = $"{(int)MathF.Round(middle - w / 2f)},18", ["size"] = $"{w},34" };
            if (Find(nui, name) is { } b) Replace(nui, b, Changed(b, Pictures.Append("font").ToArray(), place));
            nui.Elements.Add(Words(name + "words", word, middle, ButtonBaseline, MenuStyle, ButtonEm, name));
        }
    }

    /// <summary>The space between the key page's three words.</summary>
    public const float KeysGap = 36f;

    /// <summary>The key page's three words, defaults, cancel and ok, centred under the list (its middle, 320), <see cref="KeysGap"/> apart.</summary>
    public static IEnumerable<(string Button, string Word, float Middle)> KeysRow(string defaults, string cancel, string ok)
    {
        (string, string)[] row = [(defaults, "defaults"), (cancel, "cancel"), (ok, "ok")];
        float total = row.Sum(r => Measure(r.Item2, ButtonEm)) + KeysGap * (row.Length - 1);
        float x = 320f - total / 2f;
        foreach (var (button, word) in row)
        {
            float w = Measure(word, ButtonEm);
            yield return (button, word, x + w / 2f);
            x += w + KeysGap;
        }
    }
}
