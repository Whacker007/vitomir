// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What the mesh keeps of each model so that its light can be worked out again every frame
// (Scene.ThingLighting).
using System.Numerics;
using Vitomir.Formats;

namespace Vitomir.Engine.Scene;

public sealed partial class SceneMesh
{
    /// <summary>
    /// One model's run of the vertex list, as the per-frame lighting needs it: the thing it belongs
    /// to, where its vertices start, its normals in the frame it was built in, its baked colours when
    /// it is prelit (B, G, R, A, as the scene file stores them), its model flags, and — made the first
    /// time it is lit — a copy of its vertices whose colours the lighting rewrites and a renderer
    /// uploads.
    /// </summary>
    public sealed class LitSlice(int thing, uint first, Vector3[] normals, uint[]? baked, uint modelFlags, int slot)
    {
        public int Thing { get; } = thing;
        public uint First { get; } = first;
        public Vector3[] Normals { get; } = normals;
        /// <summary>The model's baked colours when it is `PRE LIT` and has them; null otherwise.</summary>
        public uint[]? Baked { get; } = baked;
        public uint ModelFlags { get; } = modelFlags;
        /// <summary>Which of the thing's models this is (<see cref="World.NamedShapes.Body"/> or a sub-model slot).</summary>
        public int Slot { get; } = slot;
        /// <summary>The run as last lit, for a thing standing still; null until it is first lit.</summary>
        public SceneVertex[]? Run { get; internal set; }
    }

    readonly List<LitSlice> lit = [];
    readonly Dictionary<uint, LitSlice> litAt = [];

    /// <summary>Every model's slice, in the order they went in.</summary>
    public IReadOnlyList<LitSlice> Lit => lit;

    /// <summary>The slice that starts at a vertex, or null.</summary>
    public LitSlice? LitAt(uint first) => litAt.GetValueOrDefault(first);

    void Remember(NodVertex[] verts, System.Numerics.Matrix4x4 world, int thing, uint first, uint[]? baked, uint modelFlags, int slot)
    {
        var rotation = world with { M41 = 0, M42 = 0, M43 = 0 };
        var normals = new Vector3[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            var n = Vector3.TransformNormal(verts[i].Normal, rotation);
            normals[i] = n == Vector3.Zero ? n : Vector3.Normalize(n);
        }
        var slice = new LitSlice(thing, first, normals, baked, modelFlags, slot);
        lit.Add(slice);
        litAt[first] = slice;
    }

    /// <summary>A slice's vertices as the mesh holds them, for its first lighting.</summary>
    internal SceneVertex[] Copy(uint first, int count)
    {
        var run = new SceneVertex[count];
        vertices.CopyTo((int)first, run, 0, count);
        return run;
    }

    /// <summary>
    /// Writes a run's colours back into the mesh, so that a renderer rebuilding its buffer from
    /// <see cref="Vertices"/> after the mesh has grown starts from this frame's light, not the load's.
    /// </summary>
    internal void WriteColors(uint first, ReadOnlySpan<SceneVertex> run)
    {
        for (int i = 0; i < run.Length; i++)
        {
            var v = vertices[(int)first + i];
            v.Color = run[i].Color;
            vertices[(int)first + i] = v;
        }
    }
}
