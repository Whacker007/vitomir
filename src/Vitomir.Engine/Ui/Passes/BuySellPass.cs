// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The shop pane, drawn. What it says is `Vitomir.Engine.Ui.BuySellPane`'s; this puts it on screen: the
// seven quads, the shelf, the slider, the item in the display box, the buttons' states and the two
// text boxes.
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>Draws the game's own shop pane over the finished frame while <see cref="Enabled"/>.</summary>
public sealed class BuySellPass(UiCanvas ui, BuySellPane? pane = null) : IUiPass
{
    /// <summary>The canvas it emits into.</summary>
    public UiCanvas Ui => ui;

    public BuySellPane? Pane { get; set; } = pane;

    /// <summary>On while a shop is open; `ExecuteBuySell` opens it and Done or Escape shuts it.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The button held down, by element name, or null. A button draws its `depressed=` art only
    /// while held (state 2) and its `greyed=` art while disabled (state 3); the rest of the time it
    /// is the pane's own artwork under it, because none of the three names a normal or rollover
    /// bitmap.
    /// </summary>
    public string? Pressed { get; set; }

    /// <summary>`defaultFont`, which the list and both text boxes use — none of them names one.</summary>
    public NfdFont Font { get; } = ui.Assets.Font(BuySellPane.FontName);

    public static BuySellPane Load(UiAssets assets) => new(assets.Pane(BuySellPane.PaneName));

    public void Emit(int width, int height, FreeCamera camera, Simulation sim)
    {
        if (!Enabled || Pane is not { } pane || width <= 0 || height <= 0) return;
        ui.BeginPanes(width, height);   // a pane above the bar: the panes' own map (UiLayout.Panes)
        float px = pane.Pane.Pos.Length > 1 ? pane.Pane.Pos[0] : 0, py = pane.Pane.Pos.Length > 1 ? pane.Pane.Pos[1] : 0;
        foreach (var q in pane.Pane.Quads)
            ui.Draw(q.Material, px + q.Pos[0], py + q.Pos[1], q.Size[0], q.Size[1]);

        // The shelf: each visible row at its pen, left-aligned, cut at the box's edges. The current
        // row is tested first, so a row both current and hovered is red.
        if (pane.Element(BuySellPane.List) is { Pos.Length: > 1, Size.Length: > 1 } list)
        {
            float lx = px + list.Pos[0], ly = py + list.Pos[1];
            for (int i = 0; i < pane.Rows.Count; i++)
            {
                if (!pane.Visible(i)) continue;
                uint colour = i == pane.Current ? BuySellPane.CurrentColour
                            : i == pane.Hovered ? BuySellPane.HoverColour : pane.Rows[i].Colour;
                ui.TextClipped(Font, lx, ly + pane.RowY(i) + pane.Scroll, pane.Rows[i].Text, Tint.From(colour | 0xFF000000u),
                               lx, ly, lx + list.Size[0], ly + list.Size[1]);
            }
        }

        // The slider: a fixed 8x8, never hidden.
        if (pane.Element(BuySellPane.Slider) is { Size.Length: > 1 } slider && slider.Get("bitmap") is { } art
            && pane.Element(BuySellPane.ScrollBar) is { Pos.Length: > 1 } bar)
            ui.Draw(art, px + bar.Pos[0], py + pane.SliderY, slider.Size[0], slider.Size[1]);

        // The display box: the icon at its own size, centred, 1:1.
        if (pane.Shown is { Icon.Length: > 0 } shown && pane.IconPlacement() is var (ix, iy, iw, ih)
            && pane.Element(BuySellPane.DisplayBox) is { Pos.Length: > 1 } box)
            ui.Icon(shown.Icon, px + box.Pos[0] + ix, py + box.Pos[1] + iy, iw, ih);

        Button(pane, BuySellPane.BuyButton, pane.BuyEnabled, px, py);
        Button(pane, BuySellPane.SellButton, pane.SellEnabled, px, py);
        Button(pane, BuySellPane.DoneButton, true, px, py);

        TextBox(pane, BuySellPane.ValueBox, pane.Value, pane.ValueColour, px, py);
        TextBox(pane, BuySellPane.NameBox, pane.Title, 0, px, py);
    }

    void Button(BuySellPane pane, string name, bool enabled, float px, float py)
    {
        if (pane.Element(name) is not { Pos.Length: > 1, Size.Length: > 1 } b) return;
        string? art = !enabled ? b.Get("greyed") : Pressed == name ? b.Get("depressed") : null;
        if (art is not null) ui.Draw(art, px + b.Pos[0], py + b.Pos[1], b.Size[0], b.Size[1]);
    }

    void TextBox(BuySellPane pane, string name, string text, uint colour, float px, float py)
    {
        if (text.Length == 0 || pane.Element(name) is not { Pos.Length: > 1, Size.Length: > 1 } box) return;
        var (tx, ty) = NuiPane.TextBoxOrigin(box, box.Size[0], box.Size[1], UiCanvas.Measure(Font, text), Font.LineHeight);
        ui.Text(Font, px + box.Pos[0] + tx, py + box.Pos[1] + ty, text,
                colour == 0 ? CharacterPass.TextColour(box) : Tint.From(colour));
    }
}
