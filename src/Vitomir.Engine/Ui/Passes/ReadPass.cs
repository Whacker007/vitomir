// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The read-text dialog, drawn. What it says and how it wraps belongs to `Vitomir.Engine.Ui.ReadPane`;
// this puts it on screen.
//
// It is the only shipped pane that declares `dialog=1`, and it sits in the middle of the screen
// rather than in a half, so it goes **on top of everything** — which is also where the original
// puts it, since a dialog is what stops the game underneath it being clicked.
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>Draws `ui/ReadTextBox.nui` with a document in it while one is open.</summary>
public sealed class ReadPass : IUiPass
{
    readonly UiCanvas ui;

    /// <summary>The canvas it emits into.</summary>
    public UiCanvas Ui => ui;

    public ReadPane? Pane { get; set; }

    /// <summary>The dialog is up exactly while a document is open — there is no separate flag.</summary>
    public bool Enabled => Pane?.Document is not null;

    /// <summary>The body's own font: `defaultFont10`, which the box names.</summary>
    public NfdFont Body { get; }
    public NfdFont Title { get; }

    public ReadPass(UiCanvas ui, ReadPane? pane = null)
    {
        this.ui = ui;
        Pane = pane;
        // The pane lays out with the same body font (ReadPane.Letter, which whoever makes the pane calls).
        Body = ui.Assets.Font(ReadPane.FontOf(pane, ReadPane.BodyBox));
        Title = ui.Assets.Font(ReadPane.FontOf(pane, ReadPane.TitleBox));
    }

    public static ReadPane Load(UiAssets assets) => new(assets.Pane(ReadPane.PaneName));

    public void Emit(int width, int height, FreeCamera camera, Simulation sim)
    {
        if (!Enabled || Pane is not { } pane || width <= 0 || height <= 0) return;
        ui.Begin(width, height);

        float px = pane.Pane.Pos.Length > 1 ? pane.Pane.Pos[0] : 0;
        float py = pane.Pane.Pos.Length > 1 ? pane.Pane.Pos[1] : 0;
        foreach (var q in pane.Pane.Quads)
            ui.Draw(q.Material, px + q.Pos[0], py + q.Pos[1], q.Size[0], q.Size[1]);

        if (pane.Control(ReadPane.TitleBox) is { } titleBox && pane.Title.Length > 0
            && titleBox.Size.Length > 1)
        {
            var (tx, ty) = NuiPane.TextBoxOrigin(titleBox, titleBox.Size[0], titleBox.Size[1],
                                                 UiCanvas.Measure(Title, pane.Title), Title.LineHeight);
            ui.Text(Title, px + titleBox.Pos[0] + tx, py + titleBox.Pos[1] + ty, pane.Title, Tint.White);
        }

        if (pane.Control(ReadPane.BodyBox) is { } body && body.Size.Length > 1)
        {
            // Each line at its own height in the box, scrolled by the pixel, and cut at the box's
            // edges: the library writes the text into the box's own surface.
            float bx = px + body.Pos[0], by = py + body.Pos[1];
            foreach (var (text, y, colour) in pane.Drawn())
                ui.TextClipped(Body, bx, by + y, text, Colour(colour), bx, by, bx + body.Size[0], by + body.Size[1]);
        }

        // `readScrollSlider` where the bar puts it. The bar itself is `flags=0x02` with no
        // art: a copy of the quads under it, which are already drawn.
        if (pane.Bar is { } bar && bar.Slider.Element.Get("bitmap") is { Length: > 0 } art)
            ui.Draw(art, px + bar.Slider.X, py + bar.Slider.Y, bar.Slider.Width, bar.Slider.Height);
    }

    /// <summary>`0xAARRGGBB` as the file writes it — every shipped paragraph is white.</summary>
    public static Tint Colour(uint argb) =>
        new(((argb >> 16) & 0xFF) / 255f, ((argb >> 8) & 0xFF) / 255f, (argb & 0xFF) / 255f);
}
