// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The frame renderer's drawing half (the module never draws). Its game half (the place, the simulation, the frame's game side,
// the poses and the snapshot's producer) is the module's, Vitomir.Classic's PlaceFrame, which hands this the frame to draw.
using System.Numerics;
using SDL;
using static SDL.SDL3;

using Vitomir.Platform.Presentation.Classic;

namespace Vitomir.Platform.Render.Classic;

/// <summary>
/// **Draws a place's Classic frame** (<see cref="ClassicFrame"/>): owns the GPU resources for it and keeps the original's order —
/// the level's opaque geometry, then the sky inside its window, then the things, then everything translucent back to front, and
/// finally the game's brightness curve on the way to the screen, and what goes over it (<see cref="ClassicOver"/>: the
/// interface, the screen's fade and flash, the developer overlay).
/// <para>**It reads the frame and nothing else** (first read only, then the snapshot, then the platform's, then the glue's drawing
/// half): the place's game side and the snapshot's producer are the module's (<c>PlaceFrame</c>), which runs the frame's game side
/// and fills the frame before it hands it here. What it draws with (<see cref="WorldRenderer"/>, the sky, the shadows, the mirrors,
/// the particles, the blobs, the decals, the halos, the ring) holds no engine object. A cell level's world (<see cref="Below"/>)
/// is a cell level's, given by whoever made the place (<c>vitomir-tools</c>).</para>
/// </summary>
public sealed unsafe class ClassicFrameRenderer : IClassicWorld
{
    readonly Gpu gpu;
    SDL_GPUTexture* depth;
    int depthWidth, depthHeight;

    public WorldRenderer World { get; }
    /// <summary>The place's particles, drawn last in the world pass (<see cref="ParticleRenderer"/>).</summary>
    public ParticleRenderer Particles { get; }
    /// <summary>The dynamic lights on the walls, drawn over the level's surfaces (<see cref="LightBlobRenderer"/>).</summary>
    public LightBlobRenderer LightBlobs { get; }
    /// <summary>The halos — lanterns, lamps, the flashlight (stepped by the snapshot's producer).</summary>
    public HaloRenderer Halos { get; }
    /// <summary>The place's decals, drawn over the level's surfaces (<see cref="DecalRenderer"/>).</summary>
    public DecalRenderer Decals { get; }
    /// <summary>The characters' shadows, drawn after the opaque things (<see cref="ShadowRenderer"/>).</summary>
    public ShadowRenderer Shadows { get; }
    /// <summary>The mirrors and shiny floors, drawn before the world (<see cref="ReflectionPass"/>).</summary>
    public ReflectionPass Reflections { get; }
    public SkyRenderer? SkyDrawn { get; private set; }
    public Presenter Presenter { get; }
    /// <summary>A viewer aid: draw the sky over the whole frame instead of inside its window.</summary>
    public bool SkyEverywhere { get; set; }

    /// <summary>The brightness curve's gamma (the presenter's).</summary>
    public float Gamma { get => Presenter.Gamma; set => Presenter.Gamma = value; }

    /// <summary>The world under the things, for a cell level; null for every 1.1 level (ours).</summary>
    public IClassicWorldBelow? Below { get; set; }

    /// <summary>The sky window the last frame used, in pixels (y down); zero when no sky was drawn.</summary>
    public (int X, int Y, int W, int H) LastSkyWindow { get; private set; }

    /// <param name="lightmaps">The level's lightmaps (the snapshot's, sent once).</param>
    /// <param name="sky">The place's sky as it is when the renderer is made (a viewer's <c>--sky-only</c> and <c>--sky-hide</c> are set on it next).</param>
    public ClassicFrameRenderer(Gpu gpu, IReadOnlyList<ClassicPicture> lightmaps, ClassicSky? sky)
    {
        this.gpu = gpu;
        World = new WorldRenderer(gpu, lightmaps);
        Presenter = new Presenter(gpu);
        Particles = new ParticleRenderer(gpu, World);
        LightBlobs = new LightBlobRenderer(gpu, World);
        Halos = new HaloRenderer(gpu, World);
        Decals = new DecalRenderer(gpu, World);
        Shadows = new ShadowRenderer(gpu, World);
        Reflections = new ReflectionPass(gpu, World);
        SkyDrawn = sky is not null ? new SkyRenderer(gpu, World, sky) : null;
    }

    /// <summary>
    /// Draws the click-to-move ring, or null for a viewer that has none. Its state lives on the game (the snapshot's
    /// <see cref="ClassicFrame.Ring"/>); its clock runs in the frame's game side while there is a ring to draw.
    /// </summary>
    public WalkTargetRenderer? Marker { get; set; }

    IClassicRing? IClassicWorld.Ring { get => Marker; set => Marker = (WalkTargetRenderer?)value; }

    /// <summary>A ring for this world (its material loaded by the game first: the snapshot's <c>RingMaterial</c>).</summary>
    public WalkTargetRenderer NewMarker(int material) => new(gpu, World, material);

    IClassicRing IClassicWorld.NewRing(int material) => NewMarker(material);

    public void Sky(string? only, string? hide)
    {
        if (SkyDrawn is { } sky) { sky.Only = only; sky.Hide = hide; }
    }

    public int MissingTextures => World.MissingTextures;
    public int TexturesMade => World.TexturesMade;
    public int Rebuilds => World.Rebuilds;
    public int PipelinesMade => World.PipelinesMade;
    public (int Posed, int Passed) LastAnimated => World.LastAnimated;

    /// <summary>The size the last frame's world was drawn at and its target's, for the log.</summary>
    public (int Width, int Height, int TargetWidth, int TargetHeight) LastWorldSize { get; private set; }

    /// <summary>
    /// Where the last frame's drawing went, in milliseconds of this thread's own time (for the
    /// slow-frame line): the animated things sent up; the shadows; the mirrors; the
    /// world's own pass put together; and the interface over it. What the graphics card then does
    /// with it is not in any of them. The shadows' and the halos' working out is the snapshot's,
    /// and the shadows' figure only their sending up. The interface's emitting is the module's (it adds it).
    /// </summary>
    public (double Animated, double Shadows, double Mirrors, double World, double Interface) LastDrawMs { get; private set; }

    /// <summary>The frame drawn into a host's picture (<see cref="GpuPicture"/>).</summary>
    public void Draw(in GpuPicture picture, ClassicFrame frame, ClassicOver? over) => Draw(picture.Commands, picture.Target, frame, over);

    /// <summary>
    /// **The 3D world drawn from the snapshot alone**: <paramref name="frame"/>'s layout, camera, scene, sky, shadows,
    /// mirror, particles, blobs, decals, halos and ring; then, after the brightness curve, <paramref name="over"/> (the
    /// interface's draw list, the screen's fade and flash, the developer overlay: what the old glue's passes drew).
    /// </summary>
    public void Draw(SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* target, ClassicFrame frame, ClassicOver? over)
    {
        // The world's picture laid out as the walk laid it (the target's size is the interface's; the world's may be
        // smaller, WorldSize), with the rows the frame asked for.
        var lay = frame.Layout;
        int targetWidth = lay.TargetWidth, targetHeight = lay.TargetHeight;
        int width = lay.Width, height = lay.Height;
        LastWorldSize = (width, height, targetWidth, targetHeight);
        EnsureDepth(width, height);
        SkyDrawn = SkySwap.Take(SkyDrawn, frame.Sky, gpu, World);   // a chapter swap's new sky, at once
        int rows = lay.Rows, drawn = lay.Drawn;
        int x0 = lay.X0, cols = lay.Cols;
        var viewProj = lay.ViewProjection;
        var eye = frame.Camera.Position;
        // The portal walk and the mirrors in view were found before the draw (WorldFrame: the first half of the world draw).
        bool mirror = frame.Mirror.Seen;
        // The sky only exists where the level's Sky surfaces are on screen (its own animation advanced with the walk).
        var window = frame.SkyWindow;
        bool skyOn = frame.SkyOn && SkyDrawn is not null;
        if (skyOn && SkyEverywhere) window = (0, 0, cols, drawn);
        window.X += x0;
        LastSkyWindow = skyOn ? window : default;
        if (skyOn) SkyDrawn!.Upload(cmd, frame.SkyPositions);
        // Anything that entered the world during that step has geometry now but no place in the
        // GPU buffers yet; this is where they catch up, once, however many arrived.
        World.Sync(frame.Scene);
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        World.UploadAnimated(cmd);                   // posed and lit before the draw (WorldFrame, Engine.Scene.ScenePoses)
        double animatedMs = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        // The click ring, posed at whatever size and colour its clock has reached. Outside the
        // render pass, because it uploads.
        bool marker = Marker is not null && frame.Ring.Shown && Marker.Upload(cmd, frame.Ring);
        bool particles = Particles.Upload(cmd, frame.Particles, frame.Camera.View);
        bool blobs = LightBlobs.Upload(cmd, frame);
        bool halos = Halos.Upload(cmd, frame.Halos, frame.Camera.View);
        bool decals = Decals.Upload(cmd, frame);
        t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        bool shadows = Shadows.Upload(cmd, frame.Shadows);   // each caster's shadow picture, sent up before the pass
        double shadowsMs = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        bool mirrorParticles = Reflections.Upload(cmd, frame);
        if (mirror) Reflections.Draw(cmd, Presenter.SceneTarget(width, height), depth, x0, cols, drawn, frame.Seconds,
                                     mirrorParticles, LightBlobs, Decals, blobs, decals);
        double mirrorsMs = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        // A cell level's world, drawn first by the modern path with its depth in ours (ours; the old program's CellWorldPass).
        bool below = lay.ColumnsDrawn && Below is { } cellWorld && cellWorld.Draw(cmd, Presenter.SceneTarget(width, height), depth, width, height,
                                                                         x0, cols, rows, lay.Magnify, viewProj);
        var color = new SDL_GPUColorTargetInfo
        {
            texture = Presenter.SceneTarget(width, height), clear_color = new SDL_FColor { r = 0, g = 0, b = 0, a = 1 },
            load_op = mirror || below ? SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD : SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
            store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
        };
        var ds = new SDL_GPUDepthStencilTargetInfo
        {
            texture = depth, clear_depth = 1, load_op = below ? SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD : SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
            store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
            stencil_load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE, stencil_store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
        };
        var pass = SDL_BeginGPURenderPass(cmd, &color, 1, &ds);
        // The world is drawn into the top `rows` of the target and nowhere else; what is left is the
        // frame's own black, which is what shows through the one hole in the status bar's artwork.
        if (drawn < height || cols < width)
        {
            var port = new SDL_GPUViewport { x = x0, y = 0, w = cols, h = drawn, min_depth = 0, max_depth = 1 };
            SDL_SetGPUViewport(pass, &port);
            var box = new SDL_Rect { x = x0, y = 0, w = cols, h = drawn };
            SDL_SetGPUScissor(pass, &box);
        }
        // A pane on each side: the world is not drawn at all.
        if (!lay.ColumnsDrawn) { SDL_EndGPURenderPass(pass); Presenter.Present(cmd, target); over?.Draw(cmd, target, targetWidth, targetHeight, frame); return; }
        // The mirrors' own surfaces over their reflection, then the world without them.
        if (mirror) Reflections.DrawSurfaces(cmd, pass, viewProj, eye);
        World.Leave = Reflections.Hides;
        World.DrawOpaque(cmd, pass, viewProj, BatchLayer.Level);
        // The dynamic lights' blobs, part of each lit surface's draw in 1.1.
        if (blobs) LightBlobs.Draw(cmd, pass, viewProj);
        // The decals, each group's after its surfaces in 1.1.
        if (decals) Decals.Draw(cmd, pass, viewProj, frame.Seconds);
        if (skyOn) SkyDrawn!.Draw(cmd, pass, frame.Camera.SkyViewProjection, window, x0 + cols, drawn);
        World.DrawOpaque(cmd, pass, viewProj, BatchLayer.Thing);
        if (shadows) Shadows.Draw(cmd, pass, viewProj);                    // each thing's after its model in 1.1
        World.DrawShells(cmd, pass, viewProj);
        World.DrawGlow(cmd, pass, viewProj);
        World.DrawTranslucent(cmd, pass, viewProj, eye);
        // The particles, after every surface and thing.
        if (particles) Particles.Draw(cmd, pass, viewProj, frame.Seconds);
        if (halos) Halos.Draw(cmd, pass, viewProj);                        // the world draw's last loop (Engine.World.Halos)
        // Last in the world pass, which is where 1.1 puts it — after every surface, thing and
        // translucent batch. `WalkTargetRenderer` says why it can afford to be.
        if (marker) Marker!.Draw(cmd, pass, viewProj);
        World.Leave = null;
        SDL_EndGPURenderPass(pass);
        double worldMs = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        Presenter.Present(cmd, target);
        over?.Draw(cmd, target, targetWidth, targetHeight, frame);
        LastDrawMs = (animatedMs, shadowsMs, mirrorsMs, worldMs, System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
    }

    /// <summary>
    /// Renders one frame with no window at all and hands back its pixels (RGBA, top row first): what the headless viewer and the
    /// rendering tests use, after the frame's game side and its snapshot, as for any other picture.
    /// </summary>
    public byte[] Still(int width, int height, ClassicFrame frame, ClassicOver? over)
    {
        var target = gpu.CreateTarget(width, height, gpu.ColorFormat, SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET);
        var cmd = SDL_AcquireGPUCommandBuffer(gpu.Device);
        Draw(cmd, target, frame, over);
        SDL_SubmitGPUCommandBuffer(cmd);
        var pixels = gpu.Download(target, width, height);
        SDL_ReleaseGPUTexture(gpu.Device, target);
        return pixels;
    }

    void EnsureDepth(int width, int height)
    {
        if (depth != null && depthWidth == width && depthHeight == height) return;
        if (depth != null) SDL_ReleaseGPUTexture(gpu.Device, depth);
        depth = gpu.CreateTarget(width, height, gpu.DepthFormat, SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET);
        depthWidth = width; depthHeight = height;
    }

    public void Dispose()
    {
        SkyDrawn?.Dispose();
        Below?.Dispose();
        Presenter.Dispose();
        Particles.Dispose();
        LightBlobs.Dispose();
        Halos.Dispose();
        Decals.Dispose();
        Shadows.Dispose();
        Reflections.Dispose();
        World.Dispose();
        if (depth != null) SDL_ReleaseGPUTexture(gpu.Device, depth);
        depth = null;
    }
}
