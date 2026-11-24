// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The character sheet, drawn. Every decision about *what* it says — which stats, which of the two
// pool bars, what the generation box reads, which frame each soak wedge is on — belongs to
// `Vitomir.Engine.Ui.CharacterPane`, which is testable without a GPU. This file is the part that
// needs one: it walks the pane's quads and then its controls, and puts the state on screen.
//
// The pane is 320 x 352 of artwork anchored at the bottom left of the 640x480 space, which is where
// `pos=0,0` puts it — `.nui` space counts up, so a left-hand pane occupies the lower left and the
// stat rows read top to bottom as their declared `y` counts *down* from 307 to 159.
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>Draws the game's own character pane over the finished frame while <see cref="Enabled"/>.</summary>
public sealed class CharacterPass : IUiPass
{
    readonly UiCanvas ui;

    /// <summary>The canvas it emits into.</summary>
    public UiCanvas Ui => ui;

    /// <summary>What to draw. Null until somebody opens the sheet.</summary>
    public CharacterPane? Pane { get; set; }

    /// <summary>Off by default; `--character` starts it on and F5 toggles it in play.</summary>
    public bool Enabled { get; set; }

    /// <summary>The font every box on this pane uses — none of them declares one.</summary>
    public NfdFont Font { get; }

    /// <summary>The font a `.nui` gets when its control names none.</summary>
    public const string DefaultFont = "DefaultFont";

    /// <summary>Names the pane asked for that the install has not got, reported once.</summary>
    public IReadOnlyList<string> Missing => ui.Assets.Missing;

    public CharacterPass(UiCanvas ui, CharacterPane? pane = null)
    {
        this.ui = ui;
        Pane = pane;
        Font = ui.Assets.Font(DefaultFont);
    }

    /// <summary>Builds the pane from the player's own files.</summary>
    public static CharacterPane Load(UiAssets assets) => new(assets.Pane(CharacterPane.PaneName));

    public void Emit(int width, int height, FreeCamera camera, Simulation sim)
    {
        if (!Enabled || Pane is not { } pane || width <= 0 || height <= 0) return;
        ui.BeginPanes(width, height);   // a pane above the bar: the panes' own map (UiLayout.Panes)

        // The backing artwork: seven quads that tile the pane's own rectangle.
        float px = pane.Pane.Pos[0], py = pane.Pane.Pos[1];
        foreach (var q in pane.Pane.Quads)
            ui.Draw(q.Material, px + q.Pos[0], py + q.Pos[1], q.Size[0], q.Size[1]);

        var texts = pane.Texts();
        var bars = pane.Bars();
        var images = pane.Images();

        // Then every control, in the order the pane declares them, which is the order the engine
        // built them in and therefore the order they stack.
        foreach (var e in pane.Pane.Elements)
        {
            if (e.Name.Length == 0 || !pane.Shown(e.Name)) continue;
            int w = e.Size.Length > 1 ? e.Size[0] : 0, h = e.Size.Length > 1 ? e.Size[1] : 0;
            float x = px + (e.Pos.Length > 1 ? e.Pos[0] : 0), y = py + (e.Pos.Length > 1 ? e.Pos[1] : 0);
            if (w <= 0 || h <= 0) continue;

            switch (e.Type)
            {
                case "progressbar":
                    if (bars.TryGetValue(e.Name, out float f)) Fill(e, x, y, w, h, f);
                    break;

                case "textbox":
                    if (texts.TryGetValue(e.Name, out var line) && line.Length > 0)
                    {
                        var (tx, ty) = NuiPane.TextBoxOrigin(e, w, h,
                                                             UiCanvas.Measure(Font, line), Font.LineHeight);
                        ui.Text(Font, x + tx, y + ty, line, TextColour(e));
                    }
                    break;

                case "static":
                    if (e.Get("bitmap") is { } art) ui.Draw(art, x, y, w, h);
                    break;

                case "imagearray":
                case "soakslice":
                    if (images.TryGetValue(e.Name, out var picture)) ui.Draw(picture, x, y, w, h);
                    break;

                // The raise buttons, which are up only while advancing (`CharacterPane.Shown`).
                case "button":
                    if (e.Get("bitmap") is { } face) ui.Draw(face, x, y, w, h);
                    break;
            }
        }
    }

    /// <summary>One bar, filled left to right out of its own `fillbitmap` — the status bar's rule.</summary>
    void Fill(NuiElement bar, float x, float y, int w, int h, float fraction)
    {
        if (bar.Get("fillbitmap") is not { } fill || fraction <= 0f) return;
        ui.Sub(fill, x, y, w * fraction, h, 0f, 0f, fraction, 1f);
    }

    /// <summary>
    /// A box's own `tbtextcolor`, white when it declares none. On this pane it marks the
    /// **effective** half of every stat pair and the unspent experience — nine rows plus one, all
    /// `255,222,0`.
    ///
    /// **It is written blue first.** The parser reads `%d %d %d` and stores the three
    /// numbers in written order into three bytes of the control, putting `0xff` in
    /// the fourth — so the first number lands in the low byte of a little-endian ARGB word, which is
    /// **blue**. `255,222,0` is therefore `rgb(0, 222, 255)`, a bright cyan, and Vitomir drew it as
    /// yellow until a base-game screenshot showed the real sheet with a blue
    /// effective column.
    ///
    /// **`color=` on the same pane is a different format entirely** — four numbers, written
    /// `a,r,g,b`, packed into a **16-bit ARGB1555** word on the control. Two colour
    /// attributes in one file format, with different orders and different depths; neither can be
    /// inferred from the other.
    /// </summary>
    public static Tint TextColour(NuiElement box)
    {
        if (box.Get("tbtextcolor") is not { } value) return Tint.White;
        var parts = NuiPane.Ints(value);
        return parts.Length < 3 ? Tint.White : new Tint(parts[2] / 255f, parts[1] / 255f, parts[0] / 255f);
    }
}
