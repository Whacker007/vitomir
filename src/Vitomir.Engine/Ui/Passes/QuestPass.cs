// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The quest pane, drawn. What it says is `Vitomir.Engine.Ui.QuestPane`'s; this puts it on screen:
// the six quads of artwork, then the eight rows of text and icon.
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>Draws the game's own quest pane over the finished frame while <see cref="Enabled"/>.</summary>
public sealed class QuestPass(UiCanvas ui, QuestPane? pane = null) : IUiPass
{
    /// <summary>The canvas it emits into.</summary>
    public UiCanvas Ui => ui;

    /// <summary>What to draw. Null until somebody opens the pane.</summary>
    public QuestPane? Pane { get; set; } = pane;

    /// <summary>Off by default; the status bar's quest button, **Q**, or `--quests` opens it.</summary>
    public bool Enabled { get; set; }

    /// <summary>The font the rows use — they declare none.</summary>
    public NfdFont Font { get; } = ui.Assets.Font(CharacterPass.DefaultFont);

    /// <summary>Builds the pane from the player's own files.</summary>
    public static QuestPane Load(UiAssets assets) => new(assets.Pane(QuestPane.PaneName));

    public void Emit(int width, int height, FreeCamera camera, Simulation sim)
    {
        if (!Enabled || Pane is not { } pane || width <= 0 || height <= 0) return;
        ui.BeginPanes(width, height);   // a pane above the bar: the panes' own map (UiLayout.Panes)
        float px = pane.Pane.Pos.Length > 1 ? pane.Pane.Pos[0] : 0, py = pane.Pane.Pos.Length > 1 ? pane.Pane.Pos[1] : 0;
        foreach (var q in pane.Pane.Quads)
            ui.Draw(q.Material, px + q.Pos[0], py + q.Pos[1], q.Size[0], q.Size[1]);

        var rows = pane.Rows();
        for (int row = 0; row < rows.Length; row++)
        {
            var r = rows[row];
            if (r.Icon is { } icon && pane.Element($"questicon{row}") is { Size.Length: > 1, Pos.Length: > 1 } slot)
                ui.Icon(icon, px + slot.Pos[0], py + slot.Pos[1], slot.Size[0], slot.Size[1]);
            if (r.Text.Length == 0 || pane.Element($"questtext{row}") is not { Size.Length: > 1, Pos.Length: > 1 } box)
                continue;
            // `textboxflags=0x07`: centred both ways, and a line wider than the box is broken
            // between words — into a box one item tall, so a long name spills past it.
            var lines = ConversationPane.Wrap(Font, r.Text, box.Size[0]);
            for (int i = 0; i < lines.Count; i++)
            {
                var (tx, ty) = NuiPane.TextBoxLine(box, box.Size[0], box.Size[1],
                                                   UiCanvas.Measure(Font, lines[i]), Font.LineHeight, i, lines.Count);
                ui.Text(Font, px + box.Pos[0] + tx, py + box.Pos[1] + ty, lines[i], CharacterPass.TextColour(box));
            }
        }
    }
}
