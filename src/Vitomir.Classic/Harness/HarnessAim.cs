// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// `--ui-click`'s `@template` steps: where on the window a thing is, so a scripted click can land on it (ours).
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir;

/// <summary>
/// Aiming the scripted-input harness at a thing (ours). A <c>--ui-click</c> step written
/// <c>r:@cappadocian</c>, <c>move:@cappadocian</c> or <c>@cappadocian</c> is resolved on the frame it
/// goes in: the nearest living thing of that template to the steered character, a little above its
/// middle, projected through the camera the frame was drawn with — the inverse of <see cref="Pick.Ray"/>
/// — so a click written once lands on a character that has walked since the run began.
/// </summary>
static class HarnessAim
{
    /// <summary>How far up a character the click lands, as a share of its height (ours: the chest).</summary>
    const float Chest = 0.6f;

    /// <summary>The thing <paramref name="template"/> names: the nearest living one to <paramref name="from"/>.</summary>
    public static WorldThing? Find(Location place, WorldThing? from, string template) =>
        place.Things.Where(t => !t.Removed && t != from && t.Template.Equals(template, StringComparison.OrdinalIgnoreCase)
                                && t.Actor?.Has(Actor.Flag.Dead) != true)
                    .OrderBy(t => from is null ? 0f : Vector3.DistanceSquared(t.Position, from.Position))
                    .FirstOrDefault();

    /// <summary>
    /// The window pixel <paramref name="point"/> is drawn at, or null behind the camera: the pixel
    /// <see cref="Pick.Ray"/> would send back through it, with the same columns, rows and magnification.
    /// </summary>
    public static (float X, float Y)? Screen(FreeCamera camera, Vector3 point, int width, int height, int rows)
    {
        if (rows <= 0 || rows > height) rows = height;
        var view = Pick.View;
        var (x0, cols) = view.Pixels(width);
        float aspect = (float)cols / rows;
        float tan = MathF.Tan(camera.VerticalFov * 0.5f) * rows / height / view.Magnify;
        var forward = camera.Forward;
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
        var up = Vector3.Cross(right, forward);
        var v = point - camera.Position;
        float f = Vector3.Dot(v, forward);
        if (f <= 0.01f) return null;
        float ndcX = Vector3.Dot(v, right) / (f * tan * aspect);
        float ndcY = Vector3.Dot(v, up) / (f * tan);
        return ((ndcX + 1f) * cols / 2f + x0 - 0.5f, (1f - ndcY) * rows / 2f - 0.5f);
    }

    /// <summary>A little above the middle of <paramref name="thing"/>: its position raised by <see cref="Chest"/> of its height.</summary>
    public static Vector3 Mark(WorldThing thing)
    {
        float tall = thing.Height > 0f ? thing.Height : 70f;
        return thing.Position + new Vector3(0f, 0f, tall * Chest);
    }
}
