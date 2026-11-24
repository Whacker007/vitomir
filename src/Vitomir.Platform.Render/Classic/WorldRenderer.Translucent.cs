// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The 1.1 look, in the platform's renderer.
// The translucent stage: the things' first, then the level's.
using System.Numerics;
using SDL;
using static SDL.SDL3;

using Vitomir.Platform.Presentation.Classic;

namespace Vitomir.Platform.Render.Classic;

public sealed unsafe partial class WorldRenderer
{
    // A level batch's corners for rlDirect's key, taken once: the level does not move.
    readonly Dictionary<DrawBatch, Vector3[]> levelPoints = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Every translucent batch in 1.1's order (<see cref="TranslucentOrder"/>): the things' farthest
    /// first (by the model's depth), then the level's as rlDirect's flush draws its held-back
    /// blended groups, nearest point first.
    /// </summary>
    public void DrawTranslucent(SDL_GPUCommandBuffer* cmd, SDL_GPURenderPass* pass, Matrix4x4 viewProj, Vector3 eye)
    {
        if (!Bind(cmd, pass, viewProj)) return;
        var translucent = new List<DrawBatch>();
        pieceOf.Clear();
        for (int i = 0; i < batches.Length; i++)
            if (Shown(i) && !ModelLeftOut(batches[i]) && IsTranslucent(batches[i]))
            {
                if (FadedRange(i, out var lo, out var hi)) KeptPieces(i, lo, hi, translucent);   // less what is seen through (ours)
                else translucent.Add(batches[i]);
            }
        var keyed = viewProj;
        var ordered = TranslucentOrder.Order(translucent, eye, b => TranslucentOrder.Key(PointsOf(pieceOf.TryGetValue(b, out var whole) ? whole : b), keyed));
        foreach (var b in ordered)
        {
            var m = Transform(b, viewProj);
            if (m != viewProj) SDL_PushGPUVertexUniformData(cmd, 0, (nint)(&m), (uint)sizeof(Matrix4x4));
            DrawBatch(cmd, pass, b);
            if (m != viewProj) SDL_PushGPUVertexUniformData(cmd, 0, (nint)(&viewProj), (uint)sizeof(Matrix4x4));
        }
        // Last, what is seen through, and its outline (ours, WorldRenderer.SeeThrough.cs).
        DrawFaded(cmd, pass, viewProj, eye);
    }

    // The level batch's own points, read once from the mesh resource (every translucent level batch is taken in the same pass).
    Vector3[] PointsOf(DrawBatch b)
    {
        if (levelPoints.TryGetValue(b, out var have)) return have;
        var verts = scene.Mesh.Vertices;
        var idx = scene.Mesh.Indices;
        foreach (var t in batches)
        {
            if (t.Layer != BatchLayer.Level || levelPoints.ContainsKey(t) || !IsTranslucent(t)) continue;
            var points = new HashSet<Vector3>();
            for (uint i = t.FirstIndex; i < t.FirstIndex + t.IndexCount && i < idx.Length; i++)
                if (idx[i] < verts.Length) points.Add(verts[idx[i]].Position);
            levelPoints[t] = [.. points];
        }
        return levelPoints.TryGetValue(b, out have) ? have : levelPoints[b] = [b.Center];
    }
}
