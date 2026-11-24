// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// `--show-objects` (ours): the show-objects key held for a headless still.
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Classic;

namespace Vitomir;

/// <summary>
/// The show-objects key (<see cref="ShowObjects"/>) held for a still: the frame the still is drawn
/// with (its view, the columns a pane leaves, and a portal walk as the renderer makes one — a still has
/// no last frame to take them from), the things it lights handed to the glow, and the glow stepped a
/// full second so its fade has finished. Each lit thing is printed with its colour as shown, and each
/// character on screen the key leaves out.
/// </summary>
static class ShowObjectsStill
{
    public static void Hold(HoverGlow glow, Location place, WorldThing player, FreeCamera cam, int width, int height, int rows)
    {
        var (_, cols) = Pick.View.Pixels(width);
        var projection = PlaceFrame.WorldViewport(cam, cols, height, rows, cam.Far, Pick.View.Magnify);
        var walk = ShowObjects.Walk(place, cam.Position, cam.View, projection);
        var viewProj = cam.View * projection;
        var found = ShowObjects.Find(place.Things, player, viewProj, walk);
        glow.Show(found);
        glow.Step(1f);
        Console.WriteLine($"show-objects: {found.Count} lit" + (walk is null ? " (no walk: the eye is in no sector)" : ""));
        foreach (var (thing, colour) in found.OrderBy(p => p.Key.Guid))
        {
            var shown = HoverGlow.Shown(colour, Fixes.Current.GlowColours) * 255f;
            Console.WriteLine($"  lit {thing.Template} (guid {thing.Guid}, {thing.Type}) 0x{colour:x8} shown as ({shown.X:0}, {shown.Y:0}, {shown.Z:0})");
        }
        foreach (var thing in place.Things.Where(t => t.InWorld && !ReferenceEquals(t, player) && ShowObjects.IsCharacter(t)
                                                        && ShowObjects.OnScreen(t, viewProj, walk)).OrderBy(t => t.Guid))
            Console.WriteLine($"  not lit (alive or acting) {thing.Template} (guid {thing.Guid}, {thing.Type})");
    }
}
