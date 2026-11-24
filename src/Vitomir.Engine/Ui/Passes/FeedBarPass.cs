// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The feed bar, drawn: `ui/feedBar.nui`'s two quads and its progress bar, wherever `FeedBar` put
// the pane this frame.
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>Draws the feed bar while <see cref="FeedBar.Shown"/>.</summary>
public sealed class FeedBarPass(UiCanvas ui, FeedBar bar) : IUiPass
{
    /// <summary>The canvas it emits into.</summary>
    public UiCanvas Ui => ui;

    readonly NuiPane pane = ui.Assets.Pane(FeedBar.PaneName);

    public FeedBar Bar { get; } = bar;

    public void Emit(int width, int height, FreeCamera camera, Simulation sim)
    {
        if (!Bar.Shown || width <= 0 || height <= 0) return;
        ui.Begin(width, height);
        float x = Bar.Left, y = Bar.Bottom;
        foreach (var q in pane.Quads)
            ui.Draw(q.Material, x + q.Pos[0], y + q.Pos[1], q.Size[0], q.Size[1]);
        // `feedProgress`, filled left to right by its own `fillbitmap` (flags 0x02), 0..the pool.
        if (pane.Elements.FirstOrDefault(e => e.Name.Equals(FeedBar.Progress, StringComparison.OrdinalIgnoreCase)) is { } progress
            && progress.Get("fillbitmap") is { } fill && Bar.Fraction > 0f)
        {
            float f = Bar.Fraction;
            ui.Sub(fill, x + progress.Pos[0], y + progress.Pos[1], progress.Size[0] * f, progress.Size[1], 0f, 0f, f, 1f);
        }
    }
}
