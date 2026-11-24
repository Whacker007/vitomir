// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The order the translucent batches are drawn in.
using System.Numerics;

namespace Vitomir.Platform.Presentation.Classic;

/// <summary>
/// 1.1's translucent order, read from both halves of the renderer.
///
/// <para>The world draw walks the visible sectors and hands each one's surfaces to
/// rlDirect group by group, a blended
/// group (flag 0x20: <c>Trans33</c>/<c>Trans66</c>, or its material's 0x40 or 0x2000000) among them.
/// rlDirect <b>does not draw a blended group there</b>: it transforms it into a buffer of its own
/// and puts it on a list (twenty bytes an entry) with a key, the
/// smallest screen depth <c>z/w</c> of its vertices — its nearest point. Then the
/// things of each sector, then the translucent things, sorted by their model's depth and
/// drawn farthest first. Only then does the world draw call rlDirect's flush,
/// which sorts the list by the key (<c>qsort</c>, the
/// comparator answering <c>a − b</c>) and draws it in that order and empties it.</para>
///
/// <para>So a translucent <b>thing</b> is blended before every translucent <b>surface</b> — a ghost
/// behind a stained window is seen through the window, and a flame in front of one has the window
/// blended over it — and the level's translucent surfaces are drawn <b>nearest first</b>: the key is
/// ascending and the depth test is <c>LESSEQUAL</c> (rlDirect sets render state 0x17 to 4), so a small
/// <c>z/w</c> is near. That is 1.1's order, odd as it is for blending; Vitomir does the same. Until then
/// Vitomir sorted the two together, farthest first.</para>
///
/// <para>Ours: a thing's batches are sorted by their own centres, not by the model's depth, so two
/// translucent things' parts can interleave (1.1 keeps a thing's parts together). A vertex behind
/// the eye counts as on the near plane, where rlDirect's clip (not read) would
/// put it. rlDirect's list is flushed once a frame, after the mirrors' pass too, which Vitomir does not
/// copy (the mirrors' translucent surfaces are drawn in their own pass).</para>
/// </summary>
public static class TranslucentOrder
{
    /// <summary>
    /// rlDirect's key for a blended group: the least screen depth <c>z/w</c> of its
    /// points, through <paramref name="viewProj"/>. A point on or behind the eye counts as 0.
    /// </summary>
    public static float Key(ReadOnlySpan<Vector3> points, Matrix4x4 viewProj)
    {
        float least = float.MaxValue;
        foreach (var p in points)
        {
            var c = Vector4.Transform(new Vector4(p, 1f), viewProj);
            float z = c.W > 1e-4f ? Math.Max(0f, c.Z / c.W) : 0f;
            if (z < least) least = z;
        }
        return least == float.MaxValue ? 0f : least;
    }

    /// <summary>
    /// The translucent batches in 1.1's order: the things' farthest first, then the
    /// level's by rlDirect's key, least first. <paramref name="key"/> gives a level batch's key.
    /// </summary>
    public static List<DrawBatch> Order(IEnumerable<DrawBatch> translucent, Vector3 eye, Func<DrawBatch, float> key)
    {
        var things = new List<DrawBatch>();
        var level = new List<(DrawBatch Batch, float Key)>();
        foreach (var b in translucent)
            if (b.Layer == BatchLayer.Thing) things.Add(b);
            else level.Add((b, key(b)));
        things.Sort((x, y) => Vector3.DistanceSquared(y.Center, eye).CompareTo(Vector3.DistanceSquared(x.Center, eye)));
        level.Sort((x, y) => x.Key.CompareTo(y.Key));
        foreach (var (b, _) in level) things.Add(b);
        return things;
    }
}
