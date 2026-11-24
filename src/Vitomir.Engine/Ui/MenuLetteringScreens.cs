// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The rest of the Options screens in our lettering (ours): Display, Advanced Display and Sound. 1.1's words are
// read off its art where they lie (measured off the composed tiles; the art is the same in every copy of 1.1), taken
// off it, and written again in ours where 1.1 set them, each in the middle of the box it sits in.
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

public static partial class MenuLettering
{
    /// <summary>
    /// One of 1.1's words on a screen's tiles: its text as 1.1 wrote it, where its ink lies (columns
    /// <paramref name="Left"/> to <paramref name="Right"/> − 1 and rows <paramref name="Top"/> to <paramref name="Bottom"/> − 1
    /// of the 640 x 480 picture, top row first), its em in ours, and its style. Ours goes where 1.1's ink was: its middle
    /// across, and its ink's middle down — which on a bar or a plate is the bar's middle, as 1.1 centred its words.
    /// </summary>
    public readonly record struct Label(string Text, int Left, int Right, int Top, int Bottom, float Em = PanelEm, string Style = PanelStyle)
    {
        public float X => (Left + Right) / 2f;
        public float Middle => UiAssets.Height - (Top + Bottom) / 2f;
    }

    /// <summary>The ems of a slider's scale: its words (low, high, off) and its figures.</summary>
    public const float ScaleEm = 10f, FigureEm = 13f;

    /// <summary>
    /// 1.1's words taken off a screen's tiles (each label's gold and its rim, filled from round it) and ours put where
    /// they were.
    /// </summary>
    static void Relabel(LiftedArt art, NuiPane nui, string prefix, IReadOnlyList<Label> labels)
    {
        // A label's rows and columns are the pane's own picture's (a plaque's is drawn at its corner on the sheet).
        int ox = art.OriginX, oy = art.OriginY;
        var mask = art.Mask();
        foreach (var l in labels) art.MarkGold(mask, l.Top - oy - 1, l.Bottom - oy, l.Left + ox - 1, l.Right + ox + 1);
        art.Fill(art.Grown(mask, 2), 1f, 400);
        for (int i = 0; i < labels.Count; i++)
            nui.Elements.Add(Boxed($"{prefix}words{i}", labels[i].Text, labels[i].X, labels[i].Middle, labels[i].Style, labels[i].Em));
    }

    // ---- the Display screen ---------------------------------------------------------------------------------------

    /// <summary>The Display screen's words (`optionsDisplay.nui`'s tiles).</summary>
    public static readonly Label[] DisplayLabels =
    [
        new("display options", 259, 380, 13, 28, TitleEm, TitleStyle),
        new("device", 297, 343, 46, 55),
        new("video mode", 282, 359, 112, 122),
        new("detail level", 284, 357, 314, 325),
        new("brightness", 286, 355, 370, 382),
    ];

    /// <summary>The Display screen's advanced options button, its rollover picture, and its two lines' baselines.</summary>
    static readonly (string Over, int X, int Y, int W, int H) AdvancedButton = ("opsdisplayadvopsbuttonover", 129, 12, 91, 41);
    public const float AdvancedX = 174.5f, AdvancedBaseline1 = 36f, AdvancedBaseline2 = 22f;

    static void Display(UiAssets assets, NuiPane nui)
    {
        var art = LiftedArt.Of(assets, nui);
        BakedButtons(assets, art, [.. CancelOk, AdvancedButton]);
        Relabel(art, nui, "opdis", DisplayLabels);
        DetailRowOff(art);
        art.Commit("lettered/optionsdisplay");
        CancelOkWords(nui, "opdiscancelbutton", "opdisokbutton");
        const string advanced = "opdisadvancedoptionsbutton";
        if (Find(nui, advanced) is { } b) Replace(nui, b, Changed(b, Pictures));
        nui.Elements.Add(Words(advanced + "words", "advanced", AdvancedX, AdvancedBaseline1, MenuStyle, ButtonEm, advanced));
        nui.Elements.Add(Words(advanced + "words2", "options", AdvancedX, AdvancedBaseline2, MenuStyle, ButtonEm, advanced));
    }

    /// <summary>The detail level's row on the Display screen's art (its arrows and its name's box), in the picture's rows and columns.</summary>
    public const int DetailTop = 284, DetailBottom = 332, DetailLeft = 233, DetailRight = 408;

    /// <summary>
    /// The detail level's arrows and box taken off the Display screen's art (ours: the control is inert by
    /// decision and hidden, <see cref="OptionsMenu.InertDisplayControls"/>): each half of the row filled from the
    /// parchment beside it in the same rows, mirrored back and forth, and the seam in the middle softened.
    /// </summary>
    static void DetailRowOff(LiftedArt art)
    {
        const int span = 32;                                     // the parchment each side, inside the panel's rim
        int ox = art.OriginX, oy = art.OriginY, w = art.Width, mid = (DetailLeft + DetailRight) / 2;
        var rgb = art.Rgb;
        static int Mirror(int d) { int m = d % (2 * span); return m < span ? m : 2 * span - 1 - m; }
        for (int y = DetailTop - oy; y <= DetailBottom - oy; y++)
            for (int x = DetailLeft; x < DetailRight; x++)
            {
                int sx = x < mid ? DetailLeft - 1 - Mirror(x - DetailLeft) : DetailRight + Mirror(DetailRight - 1 - x);
                int d = (y * w + x + ox) * 3, from = (y * w + sx + ox) * 3;
                rgb[d] = rgb[from]; rgb[d + 1] = rgb[from + 1]; rgb[d + 2] = rgb[from + 2];
            }
        var seam = art.Mask();
        for (int y = DetailTop - oy; y <= DetailBottom - oy; y++)
            for (int x = mid - 1; x <= mid + 1; x++) seam[y * w + x + ox] = true;
        art.Fill(seam, 1f, 200);
    }

    // ---- the Advanced Display screen ------------------------------------------------------------------------------

    /// <summary>The Advanced Display screen's words (`optionsDisplayAdv.nui`'s tiles): its bars' and its sliders' scales.</summary>
    public static readonly Label[] AdvancedLabels =
    [
        new("advanced display options", 222, 420, 13, 28, TitleEm, TitleStyle),
        new("driver info", 198, 264, 59, 71),
        new("detail level", 471, 544, 97, 108),
        new("draw sky", 103, 166, 146, 159), new("reflections", 102, 169, 168, 181), new("3d UI heads", 98, 172, 211, 221),
        new("texture bit depth", 269, 373, 147, 158), new("16 bit", 257, 289, 163, 173), new("32 bit", 344, 377, 163, 173),
        new("texture filtering", 274, 368, 194, 207), new("bilinear", 246, 297, 211, 222), new("trilinear", 337, 389, 211, 222),
        new("static lighting", 466, 547, 146, 159), new("vertex", 432, 477, 164, 173), new("lightmap", 521, 579, 162, 175),
        new("styled lights", 463, 541, 188, 201), new("dynamic lightblobs", 443, 564, 210, 223),
        // the sliders: each one's scale above it and its name below
        new("low", 52, 77, 244, 252, ScaleEm), new("high", 194, 222, 244, 254, ScaleEm), new("model LOD", 95, 175, 271, 282),
        new("off", 246, 266, 244, 254, ScaleEm), new("high", 376, 403, 244, 254, ScaleEm), new("decals", 299, 344, 271, 282),
        new("1", 437, 449, 244, 254, FigureEm), new("8", 562, 576, 243, 253, FigureEm), new("# char dynamic lights", 437, 576, 271, 284),
        new("low", 52, 77, 297, 305, ScaleEm), new("high", 194, 222, 297, 307, ScaleEm), new("UI head LOD", 90, 182, 325, 335),
        new("0", 250, 266, 297, 307, FigureEm), new("5", 377, 390, 297, 307, FigureEm), new("# automatic mip levels", 249, 391, 324, 337),
        new("0", 436, 452, 297, 307, FigureEm), new("4", 562, 576, 297, 307, FigureEm), new("# char shadows", 454, 563, 324, 337),
        new("low", 52, 77, 350, 358, ScaleEm), new("high", 194, 222, 350, 360, ScaleEm), new("particle LOD", 92, 177, 378, 390),
        new("32", 244, 266, 350, 360, FigureEm), new("128", 309, 335, 350, 360, FigureEm), new("512", 376, 402, 350, 360, FigureEm),
        new("mip selection", 279, 363, 378, 390),
        new("low", 429, 454, 350, 360, ScaleEm), new("high", 559, 587, 350, 360, ScaleEm), new("shadow detail", 462, 554, 378, 389),
    ];

    static void Advanced(UiAssets assets, NuiPane nui)
    {
        var art = LiftedArt.Of(assets, nui);
        BakedButtons(assets, art, CancelOk);
        Relabel(art, nui, "opda", AdvancedLabels);
        art.Commit("lettered/optionsdisplayadv");
        CancelOkWords(nui, "opdacancelbutton", "opdaokbutton");
    }

    // ---- the Sound screen -----------------------------------------------------------------------------------------

    /// <summary>The Sound screen's words (`optionsSound.nui`'s tiles): its speakers' four, each level with its box, and its two sliders'.</summary>
    public static readonly Label[] SoundLabels =
    [
        new("3d sound", 282, 356, 15, 28, TitleEm, TitleStyle),
        new("2 speakers", 284, 354, 217, 232), new("4 speakers", 284, 354, 233, 248),
        new("headphones", 279, 360, 249, 264), new("surround", 288, 349, 265, 280),
        new("music", 297, 339, 316, 326), new("sound", 296, 339, 364, 374),
    ];

    /// <summary>The Sound screen's one button, exit, where every sub-screen has cancel.</summary>
    static readonly (string Over, int X, int Y, int W, int H) ExitButton = ("opssoundexitbuttonover", 220, 12, 91, 41);

    static void Sound(UiAssets assets, NuiPane nui)
    {
        var art = LiftedArt.Of(assets, nui);
        BakedButtons(assets, art, [ExitButton]);
        Relabel(art, nui, "opso", SoundLabels);
        art.Commit("lettered/optionssound");
        const string exit = "opsoexitbutton";
        if (Find(nui, exit) is { } b) Replace(nui, b, Changed(b, Pictures));
        nui.Elements.Add(Words(exit + "words", "exit", CancelX, ButtonBaseline, MenuStyle, ButtonEm, exit));
    }
}
