// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the level surfaces drawn see-through for being in the camera's way.
using System.Numerics;
using SDL;
using static SDL.SDL3;

using Vitomir.Platform.Presentation.Classic;

namespace Vitomir.Platform.Render.Classic;

public sealed unsafe partial class WorldRenderer
{
    // The frame's faded surfaces by batch: each batch's first and last entry in the scene's list (which is in batch order).
    readonly Dictionary<int, (int Lo, int Hi)> fadedAt = [];
    // A translucent batch's pieces drawn in its place, and the batch each is a piece of (its translucent key is the batch's).
    readonly Dictionary<DrawBatch, DrawBatch> pieceOf = new(ReferenceEqualityComparer.Instance);
    // The outlines: two indices an edge, every faded surface's edges in the scene's order, and each one's run of them.
    SDL_GPUBuffer* edgeBuffer;
    readonly List<uint> edges = [];
    readonly List<(uint First, uint Count)> edgeRuns = [];
    readonly List<(int Batch, int Run)> edgesOf = [];
    readonly List<int> farFirst = [];

    /// <summary>How opaque a faded surface's outline is drawn at a level of 1 (ours).</summary>
    public const float OutlineAlpha = 0.35f;

    /// <summary>The outline's colour (ours): a pale grey, readable on the dark of the levels.</summary>
    public static readonly Vector3 OutlineColour = new(0.8f, 0.8f, 0.75f);

    // A faded surface drawn: its texture over its light, blended, writing no depth, both faces.
    static readonly RenderKindState fadedLightmapped = RenderKinds.State(RenderKind.AlphaLightmapped)! with { DepthWrite = false, Cull = Cull.None };
    static readonly RenderKindState fadedLit = RenderKinds.State(RenderKind.RgbaTexAlpha)! with { DepthWrite = false, Cull = Cull.None };
    // Its outline: the constant colour alone, alpha from the scale, no fog.
    static readonly RenderKindState outline = new()
    {
        DepthWrite = false, Color = new(TexOp.SelectArg2, TexArg.Texture, TexArg.TextureFactor), Alpha = new(TexOp.Disable, TexArg.Texture, TexArg.Diffuse),
        SrcBlend = Blend.SrcAlpha, DestBlend = Blend.InvSrcAlpha, BlendEnable = true, Cull = Cull.None, Address = Address.Clamp, Mipmaps = false,
    };

    /// <summary>
    /// The frame's soft windows into a draw's parameters (the shader's <c>Clearness</c>), for a surface at fade
    /// <paramref name="level"/>: the eye and the level, then each window's line, cosines and reach; a window not there has
    /// no reach, and is passed over.
    /// </summary>
    void Windows(ref MaterialParams prm, float level)
    {
        var holes = scene.Holes;
        prm.HoleEye = new Vector4(scene.HoleEye, level);
        Vector4 Axis(int k) => k < holes.Count ? new Vector4(holes[k].Axis, holes[k].CosInner) : Vector4.Zero;
        float Outer(int k) => k < holes.Count ? holes[k].CosOuter : 0f;
        float Far(int k) => k < holes.Count ? holes[k].Far : 0f;
        prm.HoleAxis0 = Axis(0); prm.HoleAxis1 = Axis(1); prm.HoleAxis2 = Axis(2); prm.HoleAxis3 = Axis(3);
        prm.HoleOuter = new Vector4(Outer(0), Outer(1), Outer(2), Outer(3));
        prm.HoleFar = new Vector4(Far(0), Far(1), Far(2), Far(3));
    }

    /// <summary>The frame's faded surfaces taken (in <see cref="Sync"/>), and their outlines made when the set has changed.</summary>
    void TakeFaded()
    {
        fadedAt.Clear();
        var faded = scene.Faded;
        for (int k = 0; k < faded.Count; k++)
        {
            int b = faded[k].Batch;
            fadedAt[b] = fadedAt.TryGetValue(b, out var have) ? (have.Lo, k) : (k, k);
        }
        bool same = edgesOf.Count == faded.Count;
        for (int k = 0; same && k < faded.Count; k++) same = edgesOf[k] == (faded[k].Batch, faded[k].Run);
        if (same) return;
        edgesOf.Clear();
        edges.Clear();
        edgeRuns.Clear();
        var idx = scene.Mesh.Indices;
        var all = scene.Mesh.Batches;
        foreach (var f in faded)
        {
            edgesOf.Add((f.Batch, f.Run));
            uint first = (uint)edges.Count;
            if ((uint)f.Batch < (uint)all.Length && (uint)f.Run < (uint)all[f.Batch].Runs.Length)
            {
                // A surface's triangles are a fan of its corners: (a, b, c), (a, c, d) ...; its edges are a-b, each b-c, and the last c-a.
                var run = all[f.Batch].Runs[f.Run];
                int triangles = (int)run.IndexCount / 3;
                if (triangles > 0 && run.FirstIndex + run.IndexCount <= idx.Length)
                {
                    uint a = idx[run.FirstIndex];
                    edges.Add(a); edges.Add(idx[run.FirstIndex + 1]);
                    for (int t = 0; t < triangles; t++) { edges.Add(idx[run.FirstIndex + 3 * t + 1]); edges.Add(idx[run.FirstIndex + 3 * t + 2]); }
                    edges.Add(idx[run.FirstIndex + 3 * (triangles - 1) + 2]); edges.Add(a);
                }
            }
            edgeRuns.Add((first, (uint)edges.Count - first));
        }
        if (edgeBuffer != null) SDL_ReleaseGPUBuffer(gpu.Device, edgeBuffer);   // released once the GPU is done with it
        edgeBuffer = edges.Count > 0 ? gpu.CreateBuffer<uint>(SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_INDEX, edges.ToArray()) : null;
    }

    /// <summary>Whether a level batch has surfaces seen through this frame, and where they are in the scene's list.</summary>
    bool FadedRange(int batch, out int lo, out int hi)
    {
        (lo, hi) = (0, -1);
        if (fadedAt.Count == 0 || batches[batch].Layer != BatchLayer.Level || !fadedAt.TryGetValue(batch, out var at)) return false;
        (lo, hi) = at;
        return true;
    }

    /// <summary>The runs of a batch that are not seen through, back to back as pieces of it, into <paramref name="into"/>.</summary>
    void KeptPieces(int batch, int lo, int hi, List<DrawBatch> into, bool keyed = true)
    {
        var b = batches[batch];
        var faded = scene.Faded;
        int k = lo;
        uint start = 0, count = 0;
        void Flush()
        {
            if (count == 0) return;
            var piece = b with { FirstIndex = start, IndexCount = count };
            into.Add(piece);
            if (keyed) pieceOf[piece] = b;
            count = 0;
        }
        for (int r = 0; r < b.Runs.Length; r++)
        {
            while (k <= hi && faded[k].Run < r) k++;
            var run = b.Runs[r];
            if (k <= hi && faded[k].Run == r) { Flush(); continue; }
            if (count == 0) start = run.FirstIndex;
            count += run.IndexCount;
        }
        Flush();
    }

    readonly List<DrawBatch> kept = [];

    /// <summary>An opaque level batch less its surfaces seen through: the rest drawn as ever, piece by piece.</summary>
    void DrawKept(SDL_GPUCommandBuffer* cmd, SDL_GPURenderPass* pass, DrawBatch b, int lo, int hi)
    {
        kept.Clear();
        KeptPieces(batchIndex[b], lo, hi, kept, keyed: false);
        foreach (var piece in kept) DrawBatch(cmd, pass, piece);
    }

    /// <summary>
    /// The surfaces seen through, after every other translucent draw, the farthest batch first, texture over light, blended
    /// and writing no depth, both faces. The players' window (<see cref="ClassicScene.Windowed"/>): each drawn solid but
    /// clear inside the frame's soft windows (<see cref="ClassicScene.Holes"/>, the shader's <c>Clearness</c>), by its level,
    /// and no outlines. The developer's x-ray: each at its opacity (<see cref="ClassicFaded.Opacity"/>) whole, then their
    /// outlines as thin lines, <see cref="OutlineAlpha"/> at a level of 1.
    /// </summary>
    void DrawFaded(SDL_GPUCommandBuffer* cmd, SDL_GPURenderPass* pass, Matrix4x4 viewProj, Vector3 eye)
    {
        var faded = scene.Faded;
        if (faded.Count == 0) return;
        farFirst.Clear();
        for (int k = 0; k < faded.Count; k++) if (Shown(faded[k].Batch)) farFirst.Add(k);
        farFirst.Sort((x, y) => Vector3.DistanceSquared(batches[faded[y].Batch].Center, eye)
                                    .CompareTo(Vector3.DistanceSquared(batches[faded[x].Batch].Center, eye)));
        var bindings = stackalloc SDL_GPUTextureSamplerBinding[2];
        foreach (int k in farFirst)
        {
            var f = faded[k];
            var b = batches[f.Batch];
            var run = b.Runs[f.Run];
            var mat = scene.Materials[b.Material];
            bool lit = b.Light == Lighting.Lightmap;
            var state = (lit ? fadedLightmapped : fadedLit) with { AlphaTest = mat.Traits.Masked };
            SDL_BindGPUGraphicsPipeline(pass, Pipeline(state, true));
            bindings[0] = new SDL_GPUTextureSamplerBinding
            {
                texture = materialTextures[b.Material][scene.CurrentImage(b.Material)],
                sampler = Sampler(state.Address == Address.Clamp, state.Mipmaps),
            };
            bindings[1] = new SDL_GPUTextureSamplerBinding { texture = lit ? lightmapTextures[b.LightmapPage] : white, sampler = clampSampler };
            SDL_BindGPUFragmentSamplers(pass, 0, bindings, 2);
            var prm = Params(state, (scene.Windowed ? 1f : f.Opacity) * b.AlphaScale, b.Light == Lighting.Full || mat.FullyLit);
            if (scene.Windowed) Windows(ref prm, f.Level);
            SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&prm), (uint)sizeof(MaterialParams));
            SDL_DrawGPUIndexedPrimitives(pass, run.IndexCount, 1, run.FirstIndex, 0, 0);
        }
        if (scene.Windowed) return;
        if (edgeBuffer == null || edgeRuns.Count != faded.Count) return;
        var ib = new SDL_GPUBufferBinding { buffer = edgeBuffer, offset = 0 };
        SDL_BindGPUIndexBuffer(pass, &ib, SDL_GPUIndexElementSize.SDL_GPU_INDEXELEMENTSIZE_32BIT);
        SDL_BindGPUGraphicsPipeline(pass, Pipeline(new PipelineKey(outline.SrcBlend, outline.DestBlend, outline.BlendEnable, outline.DepthWrite,
                                                                   outline.DepthFunc, Cull.None, 0, Lines: true)));
        bindings[0] = new SDL_GPUTextureSamplerBinding { texture = white, sampler = clampSampler };
        bindings[1] = new SDL_GPUTextureSamplerBinding { texture = white, sampler = clampSampler };
        SDL_BindGPUFragmentSamplers(pass, 0, bindings, 2);
        foreach (int k in farFirst)
        {
            var (first, count) = edgeRuns[k];
            if (count == 0) continue;
            var prm = Params(outline, OutlineAlpha * faded[k].Level, fullyLit: true, factor: new Vector4(OutlineColour, 1f));
            SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&prm), (uint)sizeof(MaterialParams));
            SDL_DrawGPUIndexedPrimitives(pass, count, 1, first, 0, 0);
        }
        // The level's own index buffer back, for whatever draws next.
        var back = new SDL_GPUBufferBinding { buffer = indexBuffer, offset = 0 };
        SDL_BindGPUIndexBuffer(pass, &back, SDL_GPUIndexElementSize.SDL_GPU_INDEXELEMENTSIZE_32BIT);
    }
}
