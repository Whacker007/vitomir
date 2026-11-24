// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The discipline pane, drawn. What it shows is `Vitomir.Engine.Ui.DisciplinePane`'s; this puts it
// on screen: the artwork, the tab strip with its icons and arrows, and the five rows.
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>Draws the game's own discipline pane while <see cref="Enabled"/>.</summary>
public sealed class DisciplinePass(UiCanvas ui, DisciplinePane? pane = null) : IUiPass
{
    /// <summary>The canvas it emits into.</summary>
    public UiCanvas Ui => ui;

    public DisciplinePane? Pane { get; set; } = pane;

    /// <summary>Off by default; **D** and the status bar's discipline button open it.</summary>
    public bool Enabled { get; set; }

    /// <summary>The rows' font — the boxes name none.</summary>
    public NfdFont Font { get; } = ui.Assets.Font(CharacterPass.DefaultFont);

    /// <summary>Builds the pane from the player's own files.</summary>
    public static DisciplinePane Load(UiAssets assets) => new(assets.Pane(DisciplinePane.PaneName));

    /// <summary>The level sockets' frames, `discLevels0` … `discLevels5`.</summary>
    public static string Dots(int frame) => $"discLevels{Math.Clamp(frame, 0, 5)}";

    public void Emit(int width, int height, FreeCamera camera, Simulation sim)
    {
        if (!Enabled || Pane is not { } pane || width <= 0 || height <= 0) return;
        ui.BeginPanes(width, height);   // a pane above the bar: the panes' own map (UiLayout.Panes)
        float px = pane.Pane.Pos.Length > 1 ? pane.Pane.Pos[0] : 0, py = pane.Pane.Pos.Length > 1 ? pane.Pane.Pos[1] : 0;
        foreach (var q in pane.Pane.Quads)
            ui.Draw(q.Material, px + q.Pos[0], py + q.Pos[1], q.Size[0], q.Size[1]);

        // The strip: each visible tab in its checked or plain bitmap, its group's icon over it.
        foreach (var (v, group, on) in pane.Tabs())
        {
            if (pane.Element($"discTab{v}") is { Size.Length: > 1, Pos.Length: > 1 } tab
                && (on ? tab.Get("checkedbitmap") : tab.Get("bitmap")) is { } art)
                ui.Draw(art, px + tab.Pos[0], py + tab.Pos[1], tab.Size[0], tab.Size[1]);
            if (group.Icon.Length > 0 && pane.Element($"discTabIcon{v}") is { Size.Length: > 1, Pos.Length: > 1 } icon)
                ui.Icon(group.Icon, px + icon.Pos[0], py + icon.Pos[1], icon.Size[0], icon.Size[1]);
        }
        Text(pane, "discGroupName", pane.GroupName, px, py);

        // The four arrows: their own art is in the pane's backdrop, and a greyed one is drawn over it.
        Arrow(pane, "discTabScroll1", pane.CanTabLeft, px, py);
        Arrow(pane, "discTabScroll2", pane.CanTabRight, px, py);
        Arrow(pane, "discScroll1", pane.CanScrollUp, px, py);
        Arrow(pane, "discScroll2", pane.CanScrollDown, px, py);

        var rows = pane.Rows();
        var current = pane.Subject?.Actor?.Disciplines.Current;
        for (int i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            if (r.Discipline is not { } d) continue;
            if (pane.Element($"slot{i}") is { Size.Length: > 1, Pos.Length: > 1 } slot)
            {
                // The selected discipline wears its red ring (`_R`), as on the bar.
                string icon = d.Definition.Icon;
                float sx = px + slot.Pos[0], sy = py + slot.Pos[1];
                if (!(ReferenceEquals(d, current) && ui.Icon(icon + StatusBar.SelectedSuffix, sx, sy, slot.Size[0], slot.Size[1])))
                    ui.Icon(icon, sx, sy, slot.Size[0], slot.Size[1]);
            }
            Text(pane, $"slot{i}text", r.Text, px, py);
            if (pane.Element($"slot{i}dots") is { Size.Length: > 1, Pos.Length: > 1 } dots)
                ui.Draw(Dots(r.Dots), px + dots.Pos[0], py + dots.Pos[1], dots.Size[0], dots.Size[1]);
            if (r.Learn && pane.Element($"advDisc{i}") is { Size.Length: > 1, Pos.Length: > 1 } learn
                && learn.Get("bitmap") is { } button)
                ui.Draw(button, px + learn.Pos[0], py + learn.Pos[1], learn.Size[0], learn.Size[1]);
        }
    }

    void Arrow(DisciplinePane pane, string name, bool enabled, float px, float py)
    {
        if (enabled || pane.Element(name) is not { Size.Length: > 1, Pos.Length: > 1 } e || e.Get("greyed") is not { } grey) return;
        ui.Draw(grey, px + e.Pos[0], py + e.Pos[1], e.Size[0], e.Size[1]);
    }

    void Text(DisciplinePane pane, string name, string text, float px, float py)
    {
        if (text.Length == 0 || pane.Element(name) is not { Size.Length: > 1, Pos.Length: > 1 } box) return;
        var lines = ConversationPane.Wrap(Font, text, box.Size[0]);
        for (int i = 0; i < lines.Count; i++)
        {
            var (tx, ty) = NuiPane.TextBoxLine(box, box.Size[0], box.Size[1],
                                               UiCanvas.Measure(Font, lines[i]), Font.LineHeight, i, lines.Count);
            ui.Text(Font, px + box.Pos[0] + tx, py + box.Pos[1] + ty, lines[i], CharacterPass.TextColour(box));
        }
    }
}
