// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The advancement screen's own status bar, `ui/statusBarADV.nui`: six quads of artwork over the
// bottom of the screen, the "done" and "revert" buttons, and the SP dummy over the bar's centre.
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>Draws `statusBarADV` while <see cref="Enabled"/>.</summary>
public sealed class AdvancementBarPass(UiCanvas ui) : IUiPass
{
    /// <summary>The canvas it emits into.</summary>
    public UiCanvas Ui => ui;

    public const string PaneName = "statusBarADV";
    public const string Done = "ADVokButton", Revert = "ADVresetButton";

    public NuiPane Pane { get; } = ui.Assets.Pane(PaneName);

    public bool Enabled { get; set; }

    /// <summary>The button being held down, drawn in its `depressed` art; null for none.</summary>
    public string? Pressed { get; set; }

    /// <summary>The status bar whose message box this bar carries while it is up.</summary>
    public StatusBarPass? Messages { get; set; }

    /// <summary>The button under a point in the interface's space, or null.</summary>
    public string? ButtonAt(float x, float y)
    {
        foreach (var name in new[] { Done, Revert })
            if (Element(name) is { Pos.Length: > 1, Size.Length: > 1 } e
                && x >= e.Pos[0] && x <= e.Pos[0] + e.Size[0] && y >= e.Pos[1] && y <= e.Pos[1] + e.Size[1])
                return name;
        return null;
    }

    /// <summary>Whether a point is on the bar's artwork, which swallows the click.</summary>
    public bool Covers(float x, float y) =>
        Pane.Quads.Any(q => x >= q.Pos[0] && x <= q.Pos[0] + q.Size[0] && y >= q.Pos[1] && y <= q.Pos[1] + q.Size[1]);

    public NuiElement? Element(string name) =>
        Pane.Elements.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public void Emit(int width, int height, FreeCamera camera, Simulation sim)
    {
        if (!Enabled || width <= 0 || height <= 0) return;
        ui.Begin(width, height);
        foreach (var q in Pane.Quads) ui.Draw(q.Material, q.Pos[0], q.Pos[1], q.Size[0], q.Size[1]);
        // `advSpDummy` covers the SP bar's centre hole in single player (hidden only in MP).
        if (Element("ADVspDummy") is { Pos.Length: > 1, Size.Length: > 1 } dummy && dummy.Get("bitmap") is { } art)
            ui.Draw(art, dummy.Pos[0], dummy.Pos[1], dummy.Size[0], dummy.Size[1]);
        if (Pressed is { } held && Element(held) is { Pos.Length: > 1, Size.Length: > 1 } b && b.Get("depressed") is { } dep)
            ui.Draw(dep, b.Pos[0], b.Pos[1], b.Size[0], b.Size[1]);
        // The message box, moved onto this bar while it is up (the screen's open re-parents
        // `sbTextBox` and its scroll bar into `statusBarADV`), where it goes on showing the log.
        Messages?.DrawMessages();
    }
}
