// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The 1.1 look, in the platform's renderer.
using System.Numerics;
using SDL;
using static SDL.SDL3;

using Vitomir.Platform.Presentation.Classic;

namespace Vitomir.Platform.Render.Classic;

/// <summary>
/// **The reflection pass** — 1.1's mirrors and shiny floors, the first half of the world draw.
/// Read.
///
/// A level group flagged **Reflect** (0x8000; a sector with one gets flag 2 at load) is a mirror:
/// seventeen ship — the polished floors of Orsi's palace (`V1_ORSI`), St. Thomas (`P1_STHM`) and a
/// column top in the weapon shop (`V1_WEAP`), all translucent (Trans 66), a Trans 33 surface in the
/// monastery (`P1_MON1`) that faces down, and the invisible (Nodraw) mirror walls of `N1_DEVN`,
/// `P1_ANZK`, `V1_STE2` and `V1_STE3`. With reflections on (the Advanced screen's box,
/// `detailFlags` bit 4 clear — normal and high; in Vitomir always), before anything else of the world:
/// <list type="number">
/// <item><b>Which mirrors are seen</b>: in every sector the portal walk reached with flag 2, every
/// surface of a Reflect group whose plane the eye is not behind (<c>dist ≤ n · eye</c>) and which
/// comes out on the screen within the sector's rectangle (taken to the screen, clipped at
/// <c>w &gt; 0.1</c>) — their rectangles united into one, the sectors listed. The plane
/// kept is the <b>last</b> one found: every mirror in view is drawn with that one reflection;</item>
/// <item><b>the eye reflected</b> in that plane — its position by <c>eye + n × ((n · eye − d) × −2)</c>
/// and each axis by <c>a + n × ((n · a) × −2)</c> — and the viewport cut to the
/// mirrors' rectangle;</item>
/// <item><b>the world again</b> from there: a portal walk from the mirrors' sectors in that
/// rectangle, the triangles' winding reversed (renderer flag 8), the
/// walk's sectors' surfaces, their things with the reflection's rule (REFLECTION ONLY
/// drawn, NO REFLECTION not), <b>the particles a second time</b> (every
/// live emitter not switched off, the same rule as the ordinary draw, in the mirrored view) and the
/// sorted things; then the depth cleared (depth only, not colour) and the view put back;</item>
/// <item><b>the mirrors' own surfaces</b> (each Reflect group through the renderer's group
/// draw with 0xc0) — the floor's texture laid translucently over its reflection, writing depth —
/// and then the world as usual, in which the renderer leaves every Reflect group out.</item>
/// </list>
///
/// **Ours in method:** the walk's sectors are not singled out for the mirror — every surface and
/// thing is drawn, and what lies behind the mirror's plane is cut away by moving the projection's
/// near plane onto it (an oblique projection), which is what the walk from the mirror's own sector
/// achieves in 1.1 for the shipped mirrors. The mirror is drawn in a render pass of its own before
/// the world's, which starts with the depth clear. The main view is not cut to the walk's sectors.
/// The lights' blobs and the decals, part of a group's draw in 1.1, are drawn in the mirror too; the
/// sky is not, as in 1.1. With reflections off the Reflect groups are drawn as ordinary surfaces;
/// the engine's `black` stand-in for a Nodraw mirror then is not built (no batch is made for it).
/// <para>**What it reads**: the snapshot's — the mirror the walk found (<see cref="ClassicMirror"/>), the mirror's own
/// shadows, the particles, the walk's sectors and the level's mirrors and glass (<see cref="ClassicLevel"/>); the search
/// itself is the frame's game side (<c>WorldFrame.Frame</c>).</para>
/// </summary>
public sealed unsafe class ReflectionPass : IDisposable
{
    readonly Gpu gpu;
    readonly WorldRenderer world;
    /// <summary>The frame the pass draws (the last <see cref="Upload"/>'s).</summary>
    ClassicFrame frame = new();
    /// <summary>The Nodraw mirrors' glass: their polygons, for the depth-only draw in <see cref="DrawSurfaces"/>.</summary>
    SDL_GPUBuffer* glass;
    int glassCount;
    ClassicLevel? level;
    bool anyReflective;

    /// <summary>The mirror's own particle sprites, built for the mirrored eye.</summary>
    public ParticleRenderer Particles { get; }

    /// <summary>
    /// The mirror's own shadows: 1.1 draws the mirror's things through the thing draw like the
    /// world's, and each casts its shadows there after its model — worked out again for the mirror's
    /// rule and the reflected eye (the snapshot's <see cref="ClassicFrame.MirrorShadows"/>).
    /// </summary>
    public ShadowRenderer Shadows { get; }
    bool shadowsUp;

    /// <summary>
    /// Whether reflections are drawn: <c>detailFlags</c> bit 4 clear — in 1.1 normal and high, and the
    /// Advanced screen's box; in Vitomir always, the maxed-out preset's.
    /// The search runs with the walk, before the draw (<c>WorldFrame.Reflections</c>); this is the frame's word.
    /// </summary>
    public bool On => frame.Reflections;

    /// <summary>After the frame's walk: whether a mirror is in view.</summary>
    public bool Seen => frame.Mirror.Seen;

    public ReflectionPass(Gpu gpu, WorldRenderer world)
    {
        this.gpu = gpu;
        this.world = world;
        Particles = new ParticleRenderer(gpu, world);
        Shadows = new ShadowRenderer(gpu, world);
    }

    /// <summary>The level's mirrors and glass, sent once: the glass goes up the first time.</summary>
    void Take(ClassicLevel? sent)
    {
        if (ReferenceEquals(sent, level)) return;
        level = sent;
        anyReflective = sent is not null && sent.BatchReflective.Contains(true);
        if (glass != null) SDL_ReleaseGPUBuffer(gpu.Device, glass);
        glass = null;
        // A Nodraw mirror has no batch: its glass is kept here as triangles.
        glassCount = sent?.Glass.Length ?? 0;
        if (glassCount > 0) glass = gpu.CreateBuffer<SceneVertex>(SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX, sent!.Glass);
    }

    bool Reflective(int batch) => level?.Reflective(batch) == true;

    /// <summary>
    /// Whether the main draw should leave a batch out: every Reflect group while reflections are on
    /// (the renderer skips them in the ordinary group draw; <see cref="DrawSurfaces"/> draws them).
    /// </summary>
    public bool Hides(int batch) => On && Reflective(batch) || frame.Walked && Culled(batch);

    /// <summary>The mirror's own rule: only the Reflect groups (what lies behind the glass is cut away by the projection).</summary>
    public bool HidesInMirror(int batch) => On && Reflective(batch);

    /// <summary>
    /// A level batch of a sector the walk did not reach: the engine draws only the walk's sectors'
    /// surfaces (over the visible list), which is what keeps what lies behind a Nodraw
    /// mirror from being drawn over its reflection. Batches Vitomir did not build from a sector
    /// are never culled.
    /// </summary>
    bool Culled(int batch) => level?.SectorOf(batch) is int si and >= 0 && !frame.Sees(si);

    /// <summary>
    /// Takes the frame (the level's mirrors the first time), and builds the mirror's shadows and its particle sprites for the
    /// mirrored eye. Outside a render pass. Answers whether there are particles to draw (false when no mirror is in view).
    /// </summary>
    public bool Upload(SDL_GPUCommandBuffer* cmd, ClassicFrame frame)
    {
        this.frame = frame;
        Take(frame.Level);
        shadowsUp = false;
        if (!frame.Mirror.Seen) return false;
        // The shadows are laid on every surface (the mirror's world is not cut to a walk here).
        shadowsUp = Shadows.Upload(cmd, frame.MirrorShadows);
        return Particles.Upload(cmd, frame.Particles, frame.Mirror.View);
    }

    /// <summary>The frame, the level's mirrors the first time, with no mirror to draw (a frame that shows none).</summary>
    public void Take(ClassicFrame frame)
    {
        this.frame = frame;
        Take(frame.Level);
    }

    /// <summary>
    /// Step 3: the reflected world into <paramref name="target"/>, in a render pass of its own that
    /// clears the frame and the depth, cut to the mirrors' rectangle.
    /// </summary>
    public void Draw(SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* target, SDL_GPUTexture* depth, int x0, int cols, int rows,
                     float seconds, bool particles, LightBlobRenderer? blobs, DecalRenderer? decals, bool blobsUp, bool decalsUp)
    {
        if (!frame.Mirror.Seen) return;
        var mirror = frame.Mirror;
        var color = new SDL_GPUColorTargetInfo
        {
            texture = target, clear_color = new SDL_FColor { r = 0, g = 0, b = 0, a = 1 },
            load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
        };
        var ds = new SDL_GPUDepthStencilTargetInfo
        {
            texture = depth, clear_depth = 1, load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
            stencil_load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE, stencil_store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
        };
        var pass = SDL_BeginGPURenderPass(cmd, &color, 1, &ds);
        var port = new SDL_GPUViewport { x = x0, y = 0, w = cols, h = rows, min_depth = 0, max_depth = 1 };
        SDL_SetGPUViewport(pass, &port);
        var r = (X0: mirror.X0, Y0: mirror.Y0, X1: mirror.X1, Y1: mirror.Y1);    // already within the screen (the producer's)
        int px0 = x0 + (int)MathF.Floor((r.X0 + 1f) * 0.5f * cols), px1 = x0 + (int)MathF.Ceiling((r.X1 + 1f) * 0.5f * cols);
        int py0 = (int)MathF.Floor((1f - r.Y1) * 0.5f * rows), py1 = (int)MathF.Ceiling((1f - r.Y0) * 0.5f * rows);
        var box = new SDL_Rect { x = px0, y = py0, w = Math.Max(0, px1 - px0), h = Math.Max(0, py1 - py0) };
        SDL_SetGPUScissor(pass, &box);
        var vp = mirror.ViewProjection;
        world.Mirrored = true;
        var leave = world.Leave;
        world.Leave = HidesInMirror;
        try
        {
            world.DrawOpaque(cmd, pass, vp, BatchLayer.Level);
            if (blobsUp) blobs?.Draw(cmd, pass, vp, all: true);   // the mirror draws every surface (ours in method), and so every blob
            if (decalsUp) decals?.Draw(cmd, pass, vp, seconds);
            world.DrawOpaque(cmd, pass, vp, BatchLayer.Thing);
            if (shadowsUp) Shadows.Draw(cmd, pass, vp);                // each thing's after its model in 1.1
            world.DrawShells(cmd, pass, vp);
            world.DrawGlow(cmd, pass, vp);
            world.DrawTranslucent(cmd, pass, vp, mirror.Eye);
            if (particles) Particles.Draw(cmd, pass, vp, seconds);
        }
        finally
        {
            world.Mirrored = false;
            world.Leave = leave;
        }
        SDL_EndGPURenderPass(pass);
    }

    /// <summary>
    /// Step 4: the mirrors' own surfaces, first in the world's pass — translucent over
    /// their reflection, writing depth, so nothing behind them is drawn over it.
    /// </summary>
    public void DrawSurfaces(SDL_GPUCommandBuffer* cmd, SDL_GPURenderPass* pass, Matrix4x4 viewProj, Vector3 eye)
    {
        if (!Seen) return;
        // A Nodraw mirror's glass, into the depth only. **Ours in method**: the renderer's handling of
        // a Reflect group with no material (the 0x40 of 0xc0) is not known in
        // detail; what it must do — and what this does — is keep what lies behind the glass in the
        // world's own draw (the alcove's back wall) from being drawn over the reflection.
        if (glass != null && RenderKinds.State(RenderKind.ZOnly) is { } z)
        {
            SDL_PushGPUVertexUniformData(cmd, 0, (nint)(&viewProj), (uint)sizeof(Matrix4x4));
            var vb = new SDL_GPUBufferBinding { buffer = glass, offset = 0 };
            SDL_BindGPUVertexBuffers(pass, 0, &vb, 1);
            SDL_BindGPUGraphicsPipeline(pass, world.Pipeline(z, twoSided: true));
            var bindings = stackalloc SDL_GPUTextureSamplerBinding[2];
            bindings[0] = new SDL_GPUTextureSamplerBinding { texture = world.White, sampler = world.ClampSampler };
            bindings[1] = new SDL_GPUTextureSamplerBinding { texture = world.White, sampler = world.ClampSampler };
            SDL_BindGPUFragmentSamplers(pass, 0, bindings, 2);
            var prm = world.Params(z);
            SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&prm), (uint)sizeof(WorldRenderer.MaterialParams));
            SDL_DrawGPUPrimitives(pass, (uint)glassCount, 1, 0, 0);
        }
        if (!anyReflective) return;
        var leave = world.Leave;
        world.Leave = b => !Reflective(b);
        try
        {
            world.DrawOpaque(cmd, pass, viewProj, BatchLayer.Level);
            world.DrawTranslucent(cmd, pass, viewProj, eye);
        }
        finally { world.Leave = leave; }
    }

    public void Dispose()
    {
        Particles.Dispose();
        Shadows.Dispose();
        if (glass != null) SDL_ReleaseGPUBuffer(gpu.Device, glass);
        glass = null;
    }
}
