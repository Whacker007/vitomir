// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The 1.1 look, in the platform's renderer.
// The things a cell level draws through the modern path instead (ours; CellThingPass).
using System.Numerics;
using SDL;
using static SDL.SDL3;

using Vitomir.Platform.Presentation.Classic;

namespace Vitomir.Platform.Render.Classic;

public sealed unsafe partial class WorldRenderer
{
    /// <summary>
    /// **Ours.** The thing batches another renderer has drawn this frame — a cell level's characters,
    /// drawn and lit by the modern path (<c>CellThingPass</c> (Vitomir.Render)). <see cref="DrawOpaque"/> draws
    /// each of them into the depth buffer alone, with the same matrix as ever, so that the shells, the
    /// hover glow, the translucent batches and the effects drawn after it still stand in front of and
    /// behind the character exactly as they did; the picture is left as it is. Null for every 1.1 level.
    /// </summary>
    public Func<int, bool>? DrawnElsewhere { get; set; }

    /// <summary>
    /// Whether a thing's batch can be drawn by the modern path this frame (ours): an opaque or cut-out
    /// model batch that <see cref="DrawOpaque"/> would draw, with the plain single-texture draw
    /// (<see cref="DrawPlan.ForThing"/>'s <c>Rgb</c>, <c>Rgb2Sided</c> or <c>RgbMasked</c>) and its own light
    /// — not fully lit, not <c>pass</c> lines, not faded by the stealth fade or a model alpha. The rest
    /// keep 1.1's draw. Public (with <see cref="Placement"/> and <see cref="TwoSidedBatch"/>): the cell levels'
    /// <c>CellThingPass</c> asks it from <c>Vitomir.Render</c>, another assembly now.
    /// </summary>
    public bool ModernDrawable(int index)
    {
        var b = batches[index];
        if (b.Layer != BatchLayer.Thing || b.Thing < 0 || b.Thing >= ThingCount) return false;
        if (!Shown(index) || ModelLeftOut(b)) return false;
        var mat = scene.Materials[b.Material];
        if (IsTranslucent(b) || scene.HasPasses(b.Material) || AlphaOf(b) < 1f) return false;
        if (b.Light == Lighting.Full || mat.FullyLit || mat.Traits.FullyLit) return false;
        return DrawPlan.ForThing(mat.Traits) is RenderKind.Rgb or RenderKind.Rgb2Sided or RenderKind.RgbMasked;
    }

    /// <summary>A thing batch's placement: from where the scene built its geometry to where the thing is now (identity when it has not moved).</summary>
    public Matrix4x4 Placement(DrawBatch b) => Transform(b, Matrix4x4.Identity);

    /// <summary>Whether a batch is drawn with both faces.</summary>
    public bool TwoSidedBatch(DrawBatch b) => b.TwoSided || scene.Materials[b.Material].TwoSided || scene.Materials[b.Material].Traits.TwoSided;

    /// <summary>
    /// A batch into the depth buffer alone: its ordinary mode's depth test and culling, and a blend
    /// that keeps the picture (zero times the source plus one times what is there). A cut-out keeps
    /// its alpha test, so its holes stay holes.
    /// </summary>
    void DrawDepthOnly(SDL_GPUCommandBuffer* cmd, SDL_GPURenderPass* pass, DrawBatch b)
    {
        var mat = scene.Materials[b.Material];
        var kind = DrawPlan.ForThing(mat.Traits);
        if (RenderKinds.State(kind) is not { } state) return;
        var key = new PipelineKey(Blend.Zero, Blend.One, true, true, state.DepthFunc,
                                  TwoSidedBatch(b) ? Cull.None : state.Cull, state.DepthBias);
        SDL_BindGPUGraphicsPipeline(pass, Pipeline(key));
        var bindings = stackalloc SDL_GPUTextureSamplerBinding[2];
        bindings[0] = new SDL_GPUTextureSamplerBinding
        {
            texture = materialTextures[b.Material][scene.CurrentImage(b.Material)], sampler = Sampler(state.Address == Address.Clamp, state.Mipmaps),
        };
        bindings[1] = new SDL_GPUTextureSamplerBinding { texture = white, sampler = clampSampler };
        SDL_BindGPUFragmentSamplers(pass, 0, bindings, 2);
        var prm = Params(state, 1f, fullyLit: true);
        SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&prm), (uint)sizeof(MaterialParams));
        SDL_DrawGPUIndexedPrimitives(pass, b.IndexCount, 1, b.FirstIndex, 0, 0);
    }
}
