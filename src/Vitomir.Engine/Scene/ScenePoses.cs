// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A mesh's animating things posed and lit for the frame's picture (out of the renderer).
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Scene;

/// <summary>
/// **A mesh's animating things posed, measured and lit for the frame's picture** (ours, earlier, the world renderer
/// did it inside its upload, <c>WorldRenderer.UploadAnimated</c>). The engine poses and lights a thing as it draws it, and
/// draws the things of the sectors its walk reached; ours does all of that before the draw: what the
/// view cannot show (<see cref="Reached"/>) is neither posed, lit, sent up nor drawn. <see cref="Pose"/> writes the posed
/// and lit vertices into the mesh (<see cref="AnimatedThing.Vertices"/>, the standing things' lit runs); the renderer only
/// sends up what it wrote (<see cref="Posed"/>, <see cref="ThingLighting.Changed"/>) and leaves out what it passed over
/// (<see cref="Passed"/>). The simulation never reads the posed vertices (its bone questions go through
/// <c>ThingAnimation.PoseBones</c>, a comparison with 1.1's section 1.2).
/// </summary>
public sealed class ScenePoses(SceneMesh mesh)
{
    readonly List<AnimatedThing> posing = [];
    readonly HashSet<int> passed = [];

    public SceneMesh Mesh => mesh;

    /// <summary>The things the mesh was built from (<c>Location.Drawn</c>, never <c>Location.Things</c>; see the renderer's).</summary>
    public IReadOnlyList<WorldThing> Things { get; set; } = [];

    /// <summary>
    /// The place whose lights the things are lit by every frame, or null for a mesh with no place — the status bar's heads
    /// and the portraits, whose models keep the fixed light they were built with (<see cref="SceneMesh.LightSource"/>).
    /// </summary>
    public Location? Place { get; set; }

    /// <summary>The hover glow, whose raise of a model's base light is part of its light; null for none.</summary>
    public Ui.HoverGlow? Glow { get; set; }

    /// <summary>
    /// Whether this frame's view could show a thing in the given sector, a ball of the given radius about the given point
    /// (<see cref="WorldFrame.Reaches"/>), or null when every animating thing is to be posed: a mirror in view (its picture
    /// reaches sectors the walk does not), or a mesh with no place. Set every frame by whoever owns the walk.
    /// </summary>
    public Func<long, Vector3, float, bool>? Reached { get; set; }

    /// <summary>The per-frame lighting (<see cref="ThingLighting"/>), whose light count the detail options set.</summary>
    public ThingLighting Relighting { get; } = new();

    /// <summary>The animating things the last <see cref="Pose"/> posed, in the mesh's order (what goes up).</summary>
    public IReadOnlyList<AnimatedThing> Posed => posing;

    /// <summary>The things (by their index in <see cref="Things"/>) the last <see cref="Pose"/> passed over: not drawn.</summary>
    public IReadOnlySet<int> Passed => passed;

    /// <summary>How many animating things the last frame posed, and how many it passed over, for the diagnostics.</summary>
    public (int Posed, int Passed) LastAnimated { get; private set; }

    /// <summary>
    /// This frame's poses and light (WorldRenderer.UploadAnimated's first half, as it was): each animating thing the view
    /// reaches posed and measured, then every thing's light.
    /// </summary>
    public void Pose()
    {
        posing.Clear();
        passed.Clear();
        foreach (var a in mesh.Animated)
            if (Posing(a)) posing.Add(a); else passed.Add(a.Thing);
        LastAnimated = (posing.Count, mesh.Animated.Count - posing.Count);
        foreach (var a in posing) { a.Pose(); Measure(a); }
        Relight();
    }

    /// <summary>Whether the frame poses it: always, unless the view is known not to reach it.</summary>
    bool Posing(AnimatedThing a)
    {
        if (Reached is not { } reached) return true;
        if (a.Thing < 0 || a.Thing >= Things.Count || Things[a.Thing] is not { } thing) return true;
        // Half as far again as its last pose reached, and a little: the next pose is not the last.
        return reached(thing.Sector, thing.Position, a.Reach * 1.5f + 32f);
    }

    /// <summary>
    /// The box of a thing's body as the last <see cref="Pose"/> drew it (ours): its posed vertices' least and most corners,
    /// or null when it was not posed. What an animation carries away from the thing's own point — a sleeper on a bed beside
    /// which the thing stands — is where the body is drawn (<c>SeeThrough</c>'s subjects).
    /// </summary>
    public (Vector3 Min, Vector3 Max)? BodyOf(WorldThing thing)
    {
        foreach (var a in posing)
        {
            if (a.Thing < 0 || a.Thing >= Things.Count || !ReferenceEquals(Things[a.Thing], thing) || a.Vertices.Length == 0) continue;
            var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
            foreach (ref readonly var v in a.Vertices.AsSpan()) { min = Vector3.Min(min, v.Position); max = Vector3.Max(max, v.Position); }
            // The vertices are posed in the placement the mesh was built with (AnimatedThing.World); the thing is drawn
            // where it is now, so the box's corners are carried from the one to the other.
            if (!Matrix4x4.Invert(a.World, out var unbake)) return (min, max);
            var move = unbake * thing.World;
            var lo = new Vector3(float.MaxValue); var hi = new Vector3(float.MinValue);
            for (int c = 0; c < 8; c++)
            {
                var corner = Vector3.Transform(new Vector3((c & 1) == 0 ? min.X : max.X, (c & 2) == 0 ? min.Y : max.Y, (c & 4) == 0 ? min.Z : max.Z), move);
                lo = Vector3.Min(lo, corner); hi = Vector3.Max(hi, corner);
            }
            return (lo, hi);
        }
        return null;
    }

    /// <summary>How far from the thing's own point its posed vertices go, kept for <see cref="Posing"/>.</summary>
    void Measure(AnimatedThing a)
    {
        if (Reached is null || a.Thing < 0 || a.Thing >= Things.Count || Things[a.Thing] is not { } thing) return;
        float far = 0f;
        var at = thing.Position;
        foreach (ref readonly var v in a.Vertices.AsSpan()) far = MathF.Max(far, Vector3.DistanceSquared(v.Position, at));
        foreach (var part in a.Carried)
            if (part.Shown)
                foreach (ref readonly var v in part.Vertices.AsSpan()) far = MathF.Max(far, Vector3.DistanceSquared(v.Position, at));
        a.Reach = MathF.Sqrt(far);
    }

    /// <summary>
    /// Every thing's light for this frame: the animating ones into the vertices about to go up, the ones standing still
    /// into their own runs when anything they are lit by has changed (<see cref="ThingLighting.Changed"/>). The hover
    /// glow's raise of the base is part of it, which is why the glow's light needs no pass of its own.
    /// </summary>
    void Relight()
    {
        if (Place is null) return;
        // `maxThingLights`: the maxed-out preset's eight, whatever the options screens say
        // (the graphics settings are presentation).
        Relighting.MaxLights = Ui.DetailPresets.Maxed.MaxThingLights;
        Relighting.Posed = Reached is null ? null : posing;
        Relighting.Relight(mesh, Place, Things, Glow is { } glow ? glow.Intensity : null);
    }
}
