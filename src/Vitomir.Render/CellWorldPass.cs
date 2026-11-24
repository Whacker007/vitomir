// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using SDL;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Engine.World;
using Vitomir.Render.Modern;
using static SDL.SDL3;

namespace Vitomir.Render;

/// <summary>
/// **Ours.** A cell level's world in the game's frame: the level's mesh
/// drawn by the modern renderer (<see cref="ModernRenderer"/>, called as it is) into the frame's
/// colour target, then its depth drawn again into the game's own depth buffer with the game's
/// projection, so that 1.1's characters, drawn next by the 1.1 look's ordinary thing pass (<see cref="ClassicFrameRenderer"/>'s)
/// stand in front of and behind the world correctly. The characters and things themselves are
/// drawn by the modern renderer with the world (<see cref="Things"/>, <see cref="CellThingPass"/>), lit by
/// its sun, shadows, ambient light and torches; the thing pass draws only their depth, and whatever of
/// 1.1's it keeps (translucent and fully lit batches, shells, effects).
/// </summary>
public sealed unsafe class CellWorldPass : IClassicWorldBelow
{
    readonly Gpu gpu;
    readonly ModernRenderer renderer;
    readonly ModernScene scene;
    readonly SDL_GPUGraphicsPipeline* depthPipeline, depthPipelineCulled;
    /// <summary>A streamed level's cells and far versions (ours, Phase 3); null for a level drawn from its glTF whole.</summary>
    readonly CellStreamScene? stream;
    public CellStreamScene? Stream => stream;

    public ModernSettings Settings => renderer.Settings;
    /// <summary>1.1's characters and things, drawn with the world by the modern path (<see cref="CellThingPass"/>); null leaves them to 1.1's thing pass.</summary>
    public CellThingPass? Things { get; init; }
    /// <summary>The game's clock, seconds, which the clouds drift by (ours; <see cref="ModernCloudSettings.Seconds"/>).</summary>
    public Func<float>? Clock { get; init; }
    /// <summary>The camera the game's frame is drawn from (the module's, asked as the host's renderer draws the world under the things).</summary>
    public Func<FreeCamera>? Camera { get; init; }
    public CellLevel Level { get; }

    public CellWorldPass(Gpu gpu, CellLevel level)
    {
        this.gpu = gpu;
        Level = level;
        CellLooks.ResolveQuality(gpu);   // the graphics quality preset: --quality, the ini, the GPU (ours)
        var settings = SettingsFor(level);
        if (level.Streamed)
        {
            // A streamed level (ours, Phase 3): its cells' buffers come and go; the glTF gives only the materials.
            stream = new CellStreamScene(gpu, level, settings.Anisotropy, CellLooks.StreamSettings());
            scene = stream.Scene;
        }
        else
        {
            var source = level.MeshFile is { } file && File.Exists(file) ? GltfReader.Load(file) : new GltfScene();
            scene = new ModernScene(gpu, source, settings.Anisotropy);
        }
        CellLooks.AddLights(scene, level);   // the cook's torches when the glTF brings no lights (ours)
        renderer = new ModernRenderer(gpu, settings, gpu.ColorFormat);
        var vs = ModernDevice.LoadShader(gpu, "ModernShadow", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, 0, 1);
        var fs = ModernDevice.LoadShader(gpu, "ModernShadow", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 2, 1);
        depthPipeline = DepthPipeline(vs, fs, cull: false);
        depthPipelineCulled = DepthPipeline(vs, fs, cull: true);
        SDL_ReleaseGPUShader(gpu.Device, vs);
        SDL_ReleaseGPUShader(gpu.Device, fs);
    }

    /// <summary>
    /// The modern path's settings for a level: its manifest's <c>extras.modern</c> (the sun's bearing
    /// and height, exposure, haze), its <c>date</c> and its compass (<c>north</c>; an older
    /// package's <c>extras.modern.northYaw</c>) over the renderer's defaults.
    /// </summary>
    public static ModernSettings SettingsFor(CellLevel level)
    {
        var s = new ModernSettings();
        if (level.Manifest.Extras["modern"] is JsonObject m)
        {
            float? F(string key) => m[key] is JsonValue v && v.TryGetValue(out double d) ? (float)d : null;
            s.SunAzimuth = F("sunAzimuth") ?? s.SunAzimuth;
            s.SunElevation = F("sunElevation") ?? s.SunElevation;
            s.NorthYaw = F("northYaw") ?? s.NorthYaw;
            s.LevelNorthYaw = s.NorthYaw;
            s.Exposure = F("exposure") ?? s.Exposure;
            s.HazeVisibilityMetres = F("hazeVisibility") ?? s.HazeVisibilityMetres;
        }
        if (level.Manifest.North is { } north) s.NorthYaw = s.LevelNorthYaw = north.Yaw;   // the level's compass (ours)
        var date = Vitomir.Formats.L2.L2Date.Of(level.Manifest);   // the level's date (ours): what the morning's sun follows
        (s.Day, s.Hour) = (date.DayOfYear, date.Hour);
        CellLooks.Apply(s);   // --look, --time-of-day, --day (ours)
        if (Environment.GetEnvironmentVariable("VITOMIR_CELL_SUN") is { Length: > 0 } sun)   // az,el for a look without a re-cook (ours)
        {
            var p = sun.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            (s.SunAzimuth, s.SunElevation) = (p[0], p[1]);
        }
        return s;
    }

    /// <summary>The factor the level's areas last put on the exposure (ours), so it can be taken off again.</summary>
    float areaExposure = 1f;
    /// <summary>The stops that factor stands for, and the game's clock when it was set (null before the first frame or without a clock).</summary>
    float areaStops;
    float? areaClock;
    /// <summary>How fast the eye opens and closes while the game's clock runs, stops a second (ours: the smithy's 2.5 stops in half a second).</summary>
    public const float AdaptStopsPerSecond = 5f;

    /// <summary>
    /// The level's areas' part of the exposure (ours; <see cref="Vitomir.Formats.L2.L2Area.ExposureStops"/>):
    /// with the eye in an interior the picture is exposed that many stops brighter, rising through the doorway.
    /// While the game's clock runs the exposure follows at <see cref="AdaptStopsPerSecond"/>, so the camera's
    /// passing the doorway is half a second of an eye opening, not a step; a still, a first frame or a clock
    /// that stands is exposed for where the eye is. Whatever else sets the exposure (a look, a key,
    /// <c>--exposure</c>) stays: only this factor is exchanged.
    /// </summary>
    void AreaExposure(Vector3 eye)
    {
        float stops = Vitomir.Formats.L2.L2Area.ExposureStops(Level.Manifest.Areas, eye / Vitomir.Formats.L2.L2Manifest.EngineUnitsPerMetre);
        float? now = Clock?.Invoke();
        if (now is { } t && areaClock is { } last && t > last && t - last < 0.25f)
            stops = areaStops + Math.Clamp(stops - areaStops, -AdaptStopsPerSecond * (t - last), AdaptStopsPerSecond * (t - last));
        (areaStops, areaClock) = (stops, now);
        float factor = MathF.Pow(2f, stops);
        if (factor == areaExposure) return;
        renderer.Settings.Exposure *= factor / areaExposure;
        areaExposure = factor;
    }

    /// <summary>
    /// How many frames of its one view a still of a cell level takes (ours): two when the screen's
    /// reflections are on, since they read the frame before, else one.
    /// <c>VITOMIR_STILL_FRAMES</c> asks for more (a harness aid: temporal anti-aliasing settles over several).
    /// </summary>
    public int StillFrames =>
        int.TryParse(Environment.GetEnvironmentVariable("VITOMIR_STILL_FRAMES"), out int n) && n > 0 ? Math.Min(n, 64)
        : renderer.Settings.SsrSteps > 0 ? 2 : 1;

    /// <summary>The world under the things as the host's renderer draws it (<see cref="IClassicWorldBelow"/>): from the frame's camera (<see cref="Camera"/>).</summary>
    public bool Draw(SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* colour, SDL_GPUTexture* depth, int width, int height,
                     int x0, int cols, int rows, float magnify, Matrix4x4 viewProj) =>
        Draw(cmd, colour, depth, width, height, x0, cols, rows, magnify, Camera!(), viewProj);

    /// <summary>
    /// The world into <paramref name="colour"/> through the modern path, then its depth into
    /// <paramref name="depth"/> (cleared first) through <paramref name="viewProj"/>, the game's own.
    /// When the world is not the whole frame — the status bar under it, a pane beside it — it is
    /// drawn at its own size with the game's narrowed view (<c>PlaceFrame.WorldViewport</c>)
    /// and copied into its place, the rest of the frame left black.
    /// </summary>
    public bool Draw(SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* colour, SDL_GPUTexture* depth, int width, int height,
                     int x0, int cols, int rows, float magnify, FreeCamera camera, Matrix4x4 viewProj)
    {
        bool whole = x0 == 0 && cols == width && rows == height && magnify == 1f;
        if (stream is not null)
        {
            // The cells round the camera (ours): all of them in before the first picture, then as they come.
            var cull = ModernRenderer.ViewProjection(camera, (float)cols / rows);
            if (!stream.Started) stream.LoadNow(camera.Position, cull);
            stream.Update(cmd, camera.Position, cull, rows / (2f * MathF.Tan(camera.VerticalFov / 2f)));
            CellStreamLog.Frame(stream, cols, rows, camera.Position);   // VITOMIR_STREAM_LOG (ours, a harness aid)
        }
        if (Things is { } things) { things.Prepare(cmd); renderer.Extra = things.Extra; }
        if (Clock is { } clock) renderer.Settings.Clouds.Seconds = clock();
        AreaExposure(camera.Position);
        if (whole) renderer.Render(cmd, colour, width, height, scene, camera);
        else
        {
            Part(cols, rows);
            renderer.Render(cmd, part, cols, rows, scene, Narrowed(camera, rows / (height * magnify)));
            var blit = new SDL_GPUBlitInfo
            {
                source = new SDL_GPUBlitRegion { texture = part, w = (uint)cols, h = (uint)rows },
                destination = new SDL_GPUBlitRegion { texture = colour, x = (uint)x0, y = 0, w = (uint)cols, h = (uint)rows },
                load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR, clear_color = new SDL_FColor { r = 0, g = 0, b = 0, a = 1 },
                filter = SDL_GPUFilter.SDL_GPU_FILTER_NEAREST,
            };
            SDL_BlitGPUTexture(cmd, &blit);
        }
        var ds = new SDL_GPUDepthStencilTargetInfo
        {
            texture = depth, clear_depth = 1, load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
            stencil_load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE, stencil_store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
        };
        var pass = SDL_BeginGPURenderPass(cmd, null, 0, &ds);
        if (!whole)
        {
            // The world pass's own viewport (ClassicFrameRenderer.Draw), so the depth lies where the things will be drawn.
            var port = new SDL_GPUViewport { x = x0, y = 0, w = cols, h = rows, min_depth = 0, max_depth = 1 };
            SDL_SetGPUViewport(pass, &port);
            var box = new SDL_Rect { x = x0, y = 0, w = cols, h = rows };
            SDL_SetGPUScissor(pass, &box);
        }
        if (scene.Vertices != null)
        {
            var vb = new SDL_GPUBufferBinding { buffer = scene.Vertices };
            var ib = new SDL_GPUBufferBinding { buffer = scene.Indices };
            SDL_BindGPUVertexBuffers(pass, 0, &vb, 1);
            SDL_BindGPUIndexBuffer(pass, &ib, SDL_GPUIndexElementSize.SDL_GPU_INDEXELEMENTSIZE_32BIT);
            var planes = ModernMath.FrustumPlanes(viewProj);
            // What is seen and not blended, by sidedness, the sidedness of the first one met first: lists kept from
            // frame to frame (ours; ModernDrawOrder — LINQ made them anew each frame).
            int firstSide = depthOrder.OpaqueWithin(scene.Draws, scene.Source, planes);
            // The shadow shader's vertex constants: the matrix and, ours, a foliage sway of none.
            var vu = stackalloc Matrix4x4[2];
            vu[0] = viewProj; vu[1] = default;
            for (int turn = 0; turn < 2 && firstSide >= 0; turn++)
            {
                int side = turn == 0 ? firstSide : 1 - firstSide;
                var group = depthOrder.Cascade[side];
                if (group.Count == 0) continue;
                SDL_BindGPUGraphicsPipeline(pass, side == 1 ? depthPipeline : depthPipelineCulled);
                SDL_PushGPUVertexUniformData(cmd, 0, (nint)vu, (uint)(sizeof(Matrix4x4) + 16));
                foreach (var d in group)
                {
                    var m = scene.Material(d.Material);
                    Gpu.BindFragmentSampler(pass, scene.Binding(m.BaseColorTexture, srgb: true));
                    var p = new Vector4(m.AlphaMode == GltfAlphaMode.Mask ? m.AlphaCutoff : -1f, m.BaseColorFactor.W, m.BaseColorTexture.TexCoord, 0);
                    SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&p), (uint)sizeof(Vector4));
                    SDL_DrawGPUIndexedPrimitives(pass, d.IndexCount, 1, d.FirstIndex, d.BaseVertex, 0);
                }
            }
        }
        SDL_EndGPURenderPass(pass);
        return true;
    }

    readonly ModernDrawOrder depthOrder = new();
    SDL_GPUTexture* part;
    int partWidth, partHeight;

    /// <summary>The picture the world is drawn into when it is not the whole frame.</summary>
    void Part(int w, int h)
    {
        if (part != null && partWidth == w && partHeight == h) return;
        if (part != null) SDL_ReleaseGPUTexture(gpu.Device, part);
        part = gpu.CreateTarget(w, h, gpu.ColorFormat,
                                SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);
        (partWidth, partHeight) = (w, h);
    }

    /// <summary>
    /// The camera with its vertical view narrowed by <paramref name="share"/> the way the game's world
    /// viewport narrows it (<c>PlaceFrame.WorldViewport</c>: the tangent of half the angle
    /// scaled), for the modern renderer, which takes its angle from the camera.
    /// </summary>
    static FreeCamera Narrowed(FreeCamera c, float share)
    {
        float half43 = MathF.Atan(MathF.Tan(c.HorizontalFov43 * MathF.PI / 360f) * share);
        return new FreeCamera
        {
            Position = c.Position, Yaw = c.Yaw, Pitch = c.Pitch, Roll = c.Roll,
            HorizontalFov43 = half43 * 360f / MathF.PI, Near = c.Near, Far = c.Far,
        };
    }

    /// <summary>The depth-only pipeline, the shadow pass's shaders with the game's depth format and convention (nearer is smaller).</summary>
    SDL_GPUGraphicsPipeline* DepthPipeline(SDL_GPUShader* vs, SDL_GPUShader* fs, bool cull)
    {
        var desc = stackalloc SDL_GPUVertexBufferDescription[1];
        var attrs = stackalloc SDL_GPUVertexAttribute[6];
        *desc = new SDL_GPUVertexBufferDescription { slot = 0, pitch = (uint)sizeof(ModernVertex), input_rate = SDL_GPUVertexInputRate.SDL_GPU_VERTEXINPUTRATE_VERTEX };
        attrs[0] = new SDL_GPUVertexAttribute { location = 0, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT3, offset = 0 };
        attrs[1] = new SDL_GPUVertexAttribute { location = 1, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT3, offset = 12 };
        attrs[2] = new SDL_GPUVertexAttribute { location = 2, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT4, offset = 24 };
        attrs[3] = new SDL_GPUVertexAttribute { location = 3, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2, offset = 40 };
        attrs[4] = new SDL_GPUVertexAttribute { location = 4, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2, offset = 48 };
        attrs[5] = new SDL_GPUVertexAttribute { location = 5, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_UBYTE4_NORM, offset = 56 };
        var info = new SDL_GPUGraphicsPipelineCreateInfo
        {
            vertex_shader = vs, fragment_shader = fs,
            vertex_input_state = new SDL_GPUVertexInputState { vertex_buffer_descriptions = desc, num_vertex_buffers = 1, vertex_attributes = attrs, num_vertex_attributes = 6 },
            primitive_type = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST,
            rasterizer_state = new SDL_GPURasterizerState
            {
                fill_mode = SDL_GPUFillMode.SDL_GPU_FILLMODE_FILL,
                cull_mode = cull ? SDL_GPUCullMode.SDL_GPU_CULLMODE_BACK : SDL_GPUCullMode.SDL_GPU_CULLMODE_NONE,
                front_face = SDL_GPUFrontFace.SDL_GPU_FRONTFACE_COUNTER_CLOCKWISE,
            },
            depth_stencil_state = new SDL_GPUDepthStencilState
            {
                compare_op = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_LESS_OR_EQUAL, enable_depth_test = true, enable_depth_write = true,
            },
            target_info = new SDL_GPUGraphicsPipelineTargetInfo { num_color_targets = 0, depth_stencil_format = gpu.DepthFormat, has_depth_stencil_target = true },
        };
        var p = SDL_CreateGPUGraphicsPipeline(gpu.Device, &info);
        if (p == null) throw new SdlException("SDL_CreateGPUGraphicsPipeline(cell world depth)");
        return p;
    }

    public void Dispose()
    {
        if (part != null) SDL_ReleaseGPUTexture(gpu.Device, part);
        part = null;
        SDL_ReleaseGPUGraphicsPipeline(gpu.Device, depthPipeline);
        SDL_ReleaseGPUGraphicsPipeline(gpu.Device, depthPipelineCulled);
        Things?.Dispose();
        if (stream is not null) stream.Dispose(); else scene.Dispose();
        renderer.Dispose();
    }
}
