// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The full shadow's picture: the caster's model drawn flat into a 128×128 bitmap as the light sees
// it (with the shadow's triangle filler). Read.
using System.Numerics;
using Vitomir.Engine.Scene;

namespace Vitomir.Engine.World;

/// <summary>Something that can draw a caster's silhouette into a full-shadow picture.</summary>
public interface IShadowModel
{
    /// <summary>
    /// Clears <paramref name="into"/> (<see cref="Shadows.BitmapSize"/> square) and draws the model
    /// into it as seen along the beam: <paramref name="light"/> is the light's frame (X across the
    /// beam, Y along it, Z the beam's "up", placed at the light); the picture spans
    /// <paramref name="width"/> across X centred on the beam and <paramref name="tall"/> up Z from
    /// <paramref name="radius"/> below it.
    /// </summary>
    void Draw(byte[] into, Matrix4x4 light, float width, float tall, float radius);
}

/// <summary>
/// **The full shadow's picture (mode 3).** The picture is cleared, and the caster's model
/// posed into the light's frame (the frame's inverse times the thing's transform), moved
/// half the width across and a radius up and scaled to the picture — 128 over the width
/// across, 128 over one and a half heights up (the scale on every bone) — then every triangle of it
/// facing the light drawn in white (0xffff), a two-sided one whichever way it faces
/// (the group's flag <c>0x80</c>): first the body, unless its model has the engine's bit
/// 0x8 (not read), then each of the four sub-models (a held weapon…) the same way. A group whose
/// named shape is hidden (bit 0 of its group word,
/// <see cref="NamedShapes"/>) is left out. The picture's rows run down: the filler writes row
/// <c>127 − z</c>, so row 0 is the top of the shadow, texture v 0.
/// <para>**Ours:** 1.1 draws the silhouette from the model's shadow LOD (a copy of the
/// model reduced to <see cref="ShadowSettings.Detail"/>); Vitomir has no model LOD and
/// draws it from the whole model, as posed for this frame. The filler's exact pixel rule (16.16 fixed
/// point) is not read line by line; this one fills a row from the rounded left edge to the
/// rounded right, rows from the rounded top to the rounded bottom, as the filler steps them.</para>
/// </summary>
public sealed class MeshSilhouette
{
    /// <summary>One run of a thing's triangles: which model it is part of, whether its material is two-sided, and its corners.</summary>
    sealed record Run(int Slot, int Mesh, bool TwoSided, int Source, int[] Corners);

    /// <summary>
    /// Where a run's corners stand: the mesh's own list from <see cref="First"/> (a thing standing
    /// still), or an animating thing's posed body (<see cref="Part"/> −1) or one of its carried parts.
    /// </summary>
    readonly record struct Source(uint First, int Count, int Part);

    readonly Dictionary<int, Model> models = [];
    SceneMesh? mesh;
    int revision = -1;

    /// <summary>
    /// The silhouette of one thing of a place's mesh (its index in <see cref="Location.Drawn"/>), or
    /// null for a thing the mesh holds no model for, which the shadow draw leaves without a full shadow
    /// (no shadow model). Kept between frames; safe to draw from several threads at once,
    /// each into its own picture.
    /// </summary>
    public IShadowModel? For(Location place, SceneMesh mesh, int index, WorldThing thing, AnimatedThing? animated)
    {
        if (!ReferenceEquals(this.mesh, mesh) || revision != mesh.Revision)
        {
            this.mesh = mesh;
            revision = mesh.Revision;
            models.Clear();
        }
        if (models.TryGetValue(index, out var have)) return have.Runs.Count == 0 ? null : have.With(animated);
        var model = new Model(mesh, thing);
        var sources = new List<Source>();
        foreach (var b in mesh.Batches)
        {
            if (b.Layer != BatchLayer.Thing || b.Thing != index || b.IndexCount < 3) continue;
            bool two = b.TwoSided || b.Material >= 0 && b.Material < place.Materials.Items.Count
                                     && place.Materials.Items[b.Material].Traits.TwoSided;
            // A batch is one model instance's: its corners all lie in one run of the vertex list.
            uint v0 = mesh.IndexAt((int)b.FirstIndex);
            var source = Locate(v0, mesh, animated);
            int at = sources.IndexOf(source);
            if (at < 0) { at = sources.Count; sources.Add(source); }
            var corners = new int[b.IndexCount / 3 * 3];
            for (int k = 0; k < corners.Length; k++) corners[k] = (int)(mesh.IndexAt((int)(b.FirstIndex + k)) - source.First);
            model.Runs.Add(new Run(b.Slot, b.Mesh, two, at, corners));
        }
        model.Sources = [.. sources];
        models[index] = model;
        return model.Runs.Count == 0 ? null : model.With(animated);
    }

    /// <summary>Which run of the vertex list a vertex lies in.</summary>
    static Source Locate(uint v, SceneMesh mesh, AnimatedThing? a)
    {
        if (a is not null)
        {
            if (v >= a.FirstVertex && v < a.FirstVertex + (uint)a.Vertices.Length) return new(a.FirstVertex, a.Vertices.Length, -1);
            for (int i = 0; i < a.Carried.Count; i++)
            {
                var part = a.Carried[i];
                if (v >= part.FirstVertex && v < part.FirstVertex + (uint)part.Vertices.Length) return new(part.FirstVertex, part.Vertices.Length, i);
            }
        }
        foreach (var (_, first, count) in mesh.Slices)
            if (v >= first && v < first + (uint)count) return new(first, count, -2);
        return new(v, 0, -2);
    }

    /// <summary>One thing's silhouette.</summary>
    sealed class Model(SceneMesh mesh, WorldThing thing) : IShadowModel
    {
        public readonly List<Run> Runs = [];
        public Source[] Sources = [];
        AnimatedThing? moving;

        public Model With(AnimatedThing? animated) { moving = animated; return this; }

        public void Draw(byte[] into, Matrix4x4 light, float width, float tall, float radius)
        {
            Array.Clear(into);
            if (!(width > 0f) || !(tall > 0f)) return;
            var toLight = Placed(thing) * Explosions.Decals.Rigid(light);
            float sx = Shadows.BitmapSize / width, sz = Shadows.BitmapSize / tall, half = width * 0.5f;
            // Every corner of each model taken to the picture once (posed, then scaled).
            // The corners' room is borrowed for the call and given back: one array for every
            // source, each at its own offset. (Not the model's own room: a thing lit by several lights
            // has its pictures drawn by several workers at once.)
            int sources = Sources.Length, total = 0;
            Span<int> at = sources <= 64 ? stackalloc int[64] : new int[sources];
            for (int si = 0; si < sources; si++) { at[si] = total; total += Sources[si].Count; }
            var room = System.Buffers.ArrayPool<Vector2>.Shared.Rent(Math.Max(total, 1));
            Span<bool> shown = sources <= 64 ? stackalloc bool[64] : new bool[sources];
            try
            {
                for (int si = 0; si < sources; si++)
                {
                    var src = Sources[si];
                    SceneVertex[]? posed = src.Part == -1 ? moving?.Vertices
                                         : src.Part >= 0 && moving is { } a && src.Part < a.Carried.Count && a.Carried[src.Part].Shown ? a.Carried[src.Part].Vertices
                                         : null;
                    shown[si] = !(src.Part >= 0 && posed is null);
                    if (!shown[si]) continue;                                               // a part put away
                    var px = room.AsSpan(at[si], src.Count);
                    for (int k = 0; k < src.Count; k++)
                    {
                        var p = posed is not null ? posed[k].Position : mesh.PositionAt(src.First + (uint)k);
                        var q = Vector3.Transform(p, toLight);
                        px[k] = new Vector2((q.X + half) * sx, (q.Z + radius) * sz);
                    }
                }
                foreach (var run in Runs)
                {
                    if (!shown[run.Source]) continue;
                    var px = room.AsSpan(at[run.Source], Sources[run.Source].Count);
                    if (px.Length == 0 || NamedShapes.IsHidden(thing, run.Slot, run.Mesh)) continue;
                    var c3 = run.Corners;
                    for (int k = 0; k + 2 < c3.Length; k += 3)
                    {
                        int i0 = c3[k], i1 = c3[k + 1], i2 = c3[k + 2];
                        if ((uint)i0 >= (uint)px.Length || (uint)i1 >= (uint)px.Length || (uint)i2 >= (uint)px.Length) continue;
                        Vector2 a = px[i0], b = px[i1], c = px[i2];
                        // Facing the light when (x2 − x0)(z1 − z0) − (z2 − z0)(x1 − x0) > 0.
                        if (!run.TwoSided && !((c.X - a.X) * (b.Y - a.Y) - (c.Y - a.Y) * (b.X - a.X) > 0f)) continue;
                        Fill(into, a, b, c);
                    }
                }
            }
            finally { System.Buffers.ArrayPool<Vector2>.Shared.Return(room); }
        }
    }

    /// <summary>
    /// Where the mesh's copy of a thing is now: the mesh keeps each model where the scene put it,
    /// and a thing that has moved since (or been scaled) is drawn with the difference — the
    /// placement taken back out, its scale, and where it is now (WorldRenderer's <c>Transform</c>).
    /// </summary>
    public static Matrix4x4 Placed(WorldThing thing)
    {
        bool scaled = thing.Fx is { Scale: var s } && s != Vector3.One;
        if ((!thing.Moved && !scaled) || !Matrix4x4.Invert(thing.Base, out var inverse)) return Matrix4x4.Identity;
        var world = scaled ? Matrix4x4.CreateScale(thing.Fx!.Scale) * thing.World : thing.World;
        return inverse * world;
    }

    /// <summary>
    /// Fills a triangle given in picture coordinates (x across, z up) with 255: rows from the rounded
    /// lowest z to the rounded highest (the last not included), each from the rounded left edge to
    /// the rounded right, written to row <c>127 − z</c>; anything outside the picture is left out
    /// (the filler tests <c>0 ≤ z ≤ height − 1</c>).
    /// </summary>
    public static void Fill(byte[] into, Vector2 a, Vector2 b, Vector2 c)
    {
        const int size = Shadows.BitmapSize;
        if (b.Y < a.Y) (a, b) = (b, a);
        if (c.Y < a.Y) (a, c) = (c, a);
        if (c.Y < b.Y) (b, c) = (c, b);
        int r0 = Math.Max(0, (int)MathF.Round(a.Y)), r1 = Math.Min(size, (int)MathF.Round(c.Y));
        if (r0 >= r1 || !(c.Y > a.Y)) return;
        float slopeAc = (c.X - a.X) / (c.Y - a.Y);
        float slopeAb = b.Y > a.Y ? (b.X - a.X) / (b.Y - a.Y) : 0f;
        float slopeBc = c.Y > b.Y ? (c.X - b.X) / (c.Y - b.Y) : 0f;
        for (int z = r0; z < r1; z++)
        {
            if (z < a.Y || z > c.Y) continue;                    // a row the rounding took past a corner
            float x1 = a.X + slopeAc * (z - a.Y);
            float x2 = z < b.Y ? (b.Y > a.Y ? a.X + slopeAb * (z - a.Y) : b.X)
                               : (c.Y > b.Y ? b.X + slopeBc * (z - b.Y) : b.X);
            if (x2 < x1) (x1, x2) = (x2, x1);
            int x0 = Math.Max(0, (int)MathF.Round(x1)), xe = Math.Min(size, (int)MathF.Round(x2));
            int row = (size - 1 - z) * size;
            for (int x = x0; x < xe; x++) into[row + x] = 255;
        }
    }
}
