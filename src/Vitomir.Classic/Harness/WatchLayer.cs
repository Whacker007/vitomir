// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The diagnostic overlay as a layer over the frame, so it is drawn the same way whether the frame is going
// to a window or to a PNG. The module never draws: it lays the overlay out
// and hands its quads to the host's renderer, which draws them.
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.World;

namespace Vitomir.Classic;

/// <summary>Says <see cref="Watch"/>'s report over the finished frame while <see cref="Enabled"/>; <paramref name="drawer"/> draws it.</summary>
public sealed class WatchLayer(Overlay overlay, IClassicOverlay drawer, Location location) : IFrameLayer
{
    public Overlay Overlay { get; } = overlay;

    /// <summary>
    /// The place whose things this labels. Settable because the place can change underneath a
    /// running game: taking an exit builds a new <see cref="Location"/> and everything pointed at
    /// the old one has to follow, or the labels name characters in a level that is no longer there.
    /// </summary>
    public Location Where { get; set; } = location;

    /// <summary>Off by default; `--watch` starts it on and F2 toggles it in play.</summary>
    public bool Enabled { get; set; }

    /// <summary>How far from the player a character still gets a label.</summary>
    public float Range { get; set; } = Watch.LabelRange;

    /// <summary>
    /// How many of the window's rows the world was drawn into, from the top — fewer than the
    /// window while the status bar is up (`Ui.StatusBar.WorldBottom`). Null means all of them.
    ///
    /// **A label has to be projected with the matrix the frame was drawn with.** When the world
    /// moved into the rows above the status bar and this did not follow, every label drifted off
    /// the character it names — correctly at dead centre and worse towards the edges, so as the
    /// camera moved the labels slid about on their own. In play it looked like the boxes flickering, which
    /// is exactly what a label that jumps a different distance every frame looks like.
    /// </summary>
    public Func<int, int>? WorldRows { get; set; }

    public void Emit(ClassicOver into, int width, int height, ClassicFrame? frame, FreeCamera camera, Simulation sim)
    {
        if (!Enabled || width <= 0 || height <= 0) return;
        var report = Watch.Look(Where, sim, Range);
        int rows = Math.Clamp(WorldRows?.Invoke(height) ?? height, 1, height);
        WatchOverlay.Draw(Overlay, report,
                          camera.View * PlaceFrame.WorldViewport(camera, width, height, rows, camera.Far),
                          camera.Position, width, rows);
        into.Overlay(drawer, Overlay.Quads);
    }
}
