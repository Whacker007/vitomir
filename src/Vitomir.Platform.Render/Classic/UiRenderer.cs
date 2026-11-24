// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The 1.1 look, in the platform's renderer.
// The game's own UI, drawn: textured quads and text in the original's 640x480 space.
//
// Two things separate this from `Overlay.cs`, which is the developer tool and stays what it is.
// This one draws the *player's* interface, from the player's own `.tga` and `.nfd` files, in the
// coordinate space the `.nui` files are authored in; and it batches by texture, because a pane
// names dozens of them where the overlay has one atlas. The pipeline is the overlay's — same
// vertex layout, same straight-alpha blend — but its own fragment shader, because the overlay's
// takes only the atlas's alpha and a pane needs the artwork's colour as well (`Shaders/Ui.hlsl`).
//
// Coordinates are the original's: 640 x 480 with the origin at the **bottom** left, and the whole
// space is stretched to the window the way the original stretches it (the UI has no aspect ratio of
// its own; that is why every widescreen fix for 1.1 is a hex edit).
//
// **It is the 2D backend**: it draws the
// platform's DrawList that the interface passes emitted from the engine (Engine.Ui.UiCanvas), with the pictures, 1.1's
// fonts, our faces and the scenes the list names taken from its resource channel by id. It reads no engine object
// (UiBoundaryTests). The quads, their order, their batches and their arithmetic are the old immediate layer's.
using System.Numerics;
using System.Runtime.InteropServices;
using SDL;
using Vitomir.Platform.Interface;
using static SDL.SDL3;

using Vitomir.Platform.Presentation.Classic;

namespace Vitomir.Platform.Render.Classic;

/// <summary>
/// A 3D model insert's drawer (<see cref="DrawKind.Model"/>): the party heads, the portrait (<see cref="SceneInserts"/>).
/// </summary>
public unsafe interface IModelInserts
{
    void Draw(SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* target, int width, int height, int id, ClassicScene scene,
              in ModelInsert insert, ReadOnlySpan<ModelDraw> draws);
}

/// <summary>Draws the interface's draw lists: a `.nui` pane's quads, bitmaps and text. Built once with the GPU, a list drawn after each pass.</summary>
public sealed unsafe class UiRenderer : IClassicInterface
{
    [StructLayout(LayoutKind.Sequential)]
    struct Vertex
    {
        public Vector2 Pos;
        public Vector2 Uv;
        public Vector4 Colour;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Params
    {
        public float Width, Height, Pad0, Pad1;
    }

    /// <summary>One run of triangles that share a texture — and, for our lettering, a style (-1 for none).</summary>
    readonly record struct Batch(nint Texture, int First, int Count, int Style);

    /// <summary>The lettering shader's style block (`UiLetters.hlsl`'s <c>Style</c>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    struct StyleBlock
    {
        public Vector4 Outline, Shadow, Glow, Shape, More;
    }

    readonly Gpu gpu;
    readonly SDL_GPUGraphicsPipeline* pipeline;
    readonly SDL_GPUSampler* sampler;
    // Finer art (ours, UiAssets.Finer) is drawn smaller than it was painted on most pictures, so it has
    // its smaller copies made and is read through them; 1.1's own art, a texel to a sheet unit, has none.
    readonly SDL_GPUSampler* finerSampler;
    readonly HashSet<nint> finerTextures = [];
    readonly SDL_GPUTexture* white;
    // What the lists name, by id, as their resource channel sent it: pictures (with their sizes), 1.1's fonts with
    // their strips, our faces with their atlases, and the scenes model inserts draw.
    readonly Dictionary<int, (nint Texture, int Width, int Height)> textures = [];
    readonly Dictionary<int, (DrawBitmapFont Font, nint Texture)> fonts = [];
    readonly Dictionary<int, ClassicScene> scenes = [];
    readonly List<object> taken = [];
    // Our own lettering (ours): each face's atlas once, and the styles this frame's lettering asked for.
    readonly SDL_GPUGraphicsPipeline* lettersPipeline;
    readonly Dictionary<int, (DrawFace Face, nint Atlas)> faces = [];
    readonly List<StyleBlock> styles = [];
    int currentStyle = -1;
    readonly List<Vertex> vertices = [];
    readonly List<Batch> batches = [];
    SDL_GPUBuffer* buffer;
    SDL_GPUTransferBuffer* transfer;
    int capacity;
    float width = 1, height = 1;
    SDL_GPUTexture* current;
    int runStart;

    /// <summary>How the list's space maps onto the picture: set by each <see cref="DrawKind.Space"/> entry.</summary>
    float scaleX = 1f, scaleY = 1f;
    /// <summary>Where the sheet's left edge and its bottom edge are on the surface (ours, the engine's UiLayout).</summary>
    float originX, sheetBottom = 1f;

    /// <summary>Who draws the lists' model inserts (the heads, the portrait); none draws none.</summary>
    public IModelInserts? Models { get; set; }

    /// <summary>The scene's insert renderer made now (<see cref="SceneInserts.Make"/>; the controller's handle).</summary>
    public void MakeInsert(int scene) => (Models as SceneInserts)?.Make(scene);

    /// <summary>The model inserts' renderers let go (the run let them go before the backend).</summary>
    public void DisposeInserts() => (Models as IDisposable)?.Dispose();

    public UiRenderer(Gpu gpu)
    {
        this.gpu = gpu;
        sampler = gpu.CreateSampler(repeat: false, mipmaps: false);
        finerSampler = gpu.CreateSampler(repeat: false, mipmaps: true);
        white = gpu.CreateTexture(1, 1, [255, 255, 255, 255], mipmaps: false);

        var vs = gpu.LoadShader("Ui", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, samplers: 0, uniformBuffers: 1);
        var fs = gpu.LoadShader("Ui", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, samplers: Gpu.FragmentSamplers, uniformBuffers: 0);

        var attributes = stackalloc SDL_GPUVertexAttribute[3];
        attributes[0] = new SDL_GPUVertexAttribute { location = 0, buffer_slot = 0, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2, offset = 0 };
        attributes[1] = new SDL_GPUVertexAttribute { location = 1, buffer_slot = 0, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2, offset = 8 };
        attributes[2] = new SDL_GPUVertexAttribute { location = 2, buffer_slot = 0, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT4, offset = 16 };
        var binding = new SDL_GPUVertexBufferDescription { slot = 0, pitch = (uint)sizeof(Vertex), input_rate = SDL_GPUVertexInputRate.SDL_GPU_VERTEXINPUTRATE_VERTEX };

        var blend = new SDL_GPUColorTargetBlendState
        {
            enable_blend = true,
            src_color_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_SRC_ALPHA,
            dst_color_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
            color_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
            src_alpha_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE,
            dst_alpha_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
            alpha_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
        };
        var target = new SDL_GPUColorTargetDescription { format = gpu.ColorFormat, blend_state = blend };
        var info = new SDL_GPUGraphicsPipelineCreateInfo
        {
            vertex_shader = vs, fragment_shader = fs,
            primitive_type = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST,
            vertex_input_state = new SDL_GPUVertexInputState
            {
                vertex_buffer_descriptions = &binding, num_vertex_buffers = 1,
                vertex_attributes = attributes, num_vertex_attributes = 3,
            },
            rasterizer_state = new SDL_GPURasterizerState { fill_mode = SDL_GPUFillMode.SDL_GPU_FILLMODE_FILL, cull_mode = SDL_GPUCullMode.SDL_GPU_CULLMODE_NONE },
            target_info = new SDL_GPUGraphicsPipelineTargetInfo { color_target_descriptions = &target, num_color_targets = 1 },
        };
        pipeline = SDL_CreateGPUGraphicsPipeline(gpu.Device, &info);
        SDL_ReleaseGPUShader(gpu.Device, vs);
        SDL_ReleaseGPUShader(gpu.Device, fs);
        if (pipeline == null) throw new SdlException("SDL_CreateGPUGraphicsPipeline(Ui)");

        // Our lettering: the same vertices and blend, a fragment stage that reads distances (ours).
        var lvs = gpu.LoadShader("UiLetters", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, samplers: 0, uniformBuffers: 1);
        var lfs = gpu.LoadShader("UiLetters", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, samplers: Gpu.FragmentSamplers, uniformBuffers: 1);
        info.vertex_shader = lvs;
        info.fragment_shader = lfs;
        lettersPipeline = SDL_CreateGPUGraphicsPipeline(gpu.Device, &info);
        SDL_ReleaseGPUShader(gpu.Device, lvs);
        SDL_ReleaseGPUShader(gpu.Device, lfs);
        if (lettersPipeline == null) throw new SdlException("SDL_CreateGPUGraphicsPipeline(UiLetters)");
    }

    /// <summary>
    /// **Draws a list** onto <paramref name="target"/> (its pictures, fonts, faces and scenes taken in first, whatever it
    /// holds): its entries in order, the quads batched by texture and style as the old layer batched them, a model insert
    /// breaking the 2D stream (1.1 flushes the pending 2D batch on the way in and on the way out).
    /// </summary>
    public void DrawTo(SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* target, DrawList list)
    {
        Take(list.Resources);
        width = Math.Max(1, list.Width);
        height = Math.Max(1, list.Height);
        Start();
        var commands = list.Commands;
        for (int i = 0; i < commands.Count; i++)
        {
            var c = commands[i];
            switch (c.Kind)
            {
                case DrawKind.Space:
                    var space = list.Spaces[c.Item];
                    (scaleX, scaleY, originX, sheetBottom) = (space.ScaleX, space.ScaleY, space.X, space.Bottom);
                    break;
                case DrawKind.Image:
                    Image(list.Images[c.Item]);
                    break;
                case DrawKind.Glyphs:
                    var run = list.GlyphRuns[c.Item];
                    Glyphs(run, list.Chars(run.First, run.Length));
                    break;
                case DrawKind.Letters:
                    var letters = list.LetterRuns[c.Item];
                    Letters(letters, list.Chars(letters.First, letters.Length));
                    break;
                case DrawKind.Model:
                    var insert = list.Models[c.Item];
                    Submit(cmd, target);
                    Start();
                    if (Models is { } models && scenes.TryGetValue(insert.Scene, out var scene))
                    {
                        var draws = new ModelDraw[insert.Draws];
                        for (int d = 0; d < insert.Draws; d++) draws[d] = list.ModelDraws[insert.FirstDraw + d];
                        models.Draw(cmd, target, (int)width, (int)height, insert.Scene, scene, insert, draws);
                    }
                    break;
            }
        }
        Submit(cmd, target);
    }

    /// <summary>The resources the list's channel sent since the last list, in order: made, repainted or let go.</summary>
    void Take(DrawResources resources)
    {
        if (resources.Pending == 0) return;
        taken.Clear();
        resources.Take(taken);
        foreach (var r in taken)
            switch (r)
            {
                case DrawPicture p:
                    Picture(p);
                    break;
                case DrawBitmapFont font:
                    if (!fonts.ContainsKey(font.Id))
                        fonts[font.Id] = (font, (nint)gpu.CreateTexture(font.StripWidth, font.StripHeight, font.Rgba, mipmaps: false));
                    break;
                case DrawFace face:
                    if (!faces.ContainsKey(face.Id)) faces[face.Id] = (face, (nint)Atlas(face));
                    break;
                case (int id, ClassicScene scene):
                    scenes[id] = scene;
                    break;
            }
        taken.Clear();
    }

    /// <summary>
    /// A picture by id: made the first time (mipmapped when finer art is read through its smaller copies, ours),
    /// overwritten when it is sent again at its size (a film's next frame), made again at another, let go when gone.
    /// </summary>
    void Picture(DrawPicture p)
    {
        textures.TryGetValue(p.Id, out var had);
        if (p.Gone || had.Texture != 0 && (had.Width != p.Width || had.Height != p.Height))
        {
            if (had.Texture != 0) Release((SDL_GPUTexture*)had.Texture);
            textures.Remove(p.Id);
            had = default;
            if (p.Gone) return;
        }
        if (had.Texture != 0)
        {
            gpu.UpdateTexture((SDL_GPUTexture*)had.Texture, p.Width, p.Height, p.Rgba);
            return;
        }
        if (textures.ContainsKey(p.Id)) return;
        var tex = gpu.CreateTexture(p.Width, p.Height, p.Rgba, mipmaps: p.Mipmaps);
        textures[p.Id] = ((nint)tex, p.Width, p.Height);
        if (p.Mipmaps && tex != null) finerTextures.Add((nint)tex);
    }

    void Release(SDL_GPUTexture* tex)
    {
        finerTextures.Remove((nint)tex);
        SDL_ReleaseGPUTexture(gpu.Device, tex);
    }

    /// <summary>
    /// A picture onto its quad (<see cref="DrawImage"/>): picture 0 is the flat white the tint colours. A pane's quad, an
    /// icon, a sub-picture, a fitted icon, a film's frame, a box: the engine's emitter worked out which part (UiCanvas).
    /// </summary>
    void Image(in DrawImage image)
    {
        SDL_GPUTexture* tex;
        if (image.Picture == 0) tex = white;
        else if (textures.TryGetValue(image.Picture, out var t) && t.Texture != 0) tex = (SDL_GPUTexture*)t.Texture;
        else return;
        Use(tex);
        Quad(image.X, image.Y, image.W, image.H, image.U0, image.V0, image.U1, image.V1, image.Tint);
    }

    /// <summary>
    /// A run of 1.1's bitmap letters with the pen on the baseline; cut, when it has a cut, to a rectangle of the space —
    /// which is what drawing into a control's own surface does (clipped at the surface's edges): a shelf row wider
    /// than the list is cut at its right edge, and the row half off the bottom shows its top two pixels.
    /// </summary>
    void Glyphs(in GlyphRun run, ReadOnlySpan<char> text)
    {
        if (!fonts.TryGetValue(run.Font, out var f)) return;
        var font = f.Font;
        Use((SDL_GPUTexture*)f.Texture);
        float x = run.X, y = run.Y;
        var tint = run.Tint;
        foreach (char c in text)
        {
            if (DrawBitmapFont.Skipped(c)) continue;    // neither drawn nor advanced over
            var g = font[c];
            if (g.Present && g.Width > 0 && g.Height > 0)
            {
                if (run.Cut is not { } cut)
                {
                    float u0 = g.PixelX / (float)font.StripWidth;
                    float u1 = (g.PixelX + g.Width) / (float)font.StripWidth;
                    // The strip's texture is flipped to top-down on upload, so the glyph's rows run
                    // from its top - which is `Height` above its own origin - down to that origin.
                    float v0 = 1f - g.Height / (float)font.StripHeight;
                    Quad(x + g.XOffset, y + g.YOffset, g.Width, g.Height, u0, v0, u1, 1f, tint);
                }
                else
                {
                    float left = cut.Left, bottom = cut.Bottom, right = cut.Right, top = cut.Top;
                    float qx = x + g.XOffset, qy = y + g.YOffset, w = g.Width, h = g.Height;
                    float u0 = g.PixelX / (float)font.StripWidth, u1 = (g.PixelX + g.Width) / (float)font.StripWidth;
                    float v0 = 1f - g.Height / (float)font.StripHeight, v1 = 1f;
                    float du = 1f / font.StripWidth, dv = 1f / font.StripHeight;
                    if (qx < left) { float cutBy = left - qx; qx += cutBy; w -= cutBy; u0 += cutBy * du; }
                    if (qx + w > right) { float cutBy = qx + w - right; w -= cutBy; u1 -= cutBy * du; }
                    if (qy < bottom) { float cutBy = bottom - qy; qy += cutBy; h -= cutBy; v1 -= cutBy * dv; }
                    if (qy + h > top) { float cutBy = qy + h - top; h -= cutBy; v0 += cutBy * dv; }
                    if (w > 0f && h > 0f) Quad(qx, qy, w, h, u0, v0, u1, v1, tint);
                }
            }
            x += g.Advance;
        }
    }

    /// <summary>
    /// **A run of our own lettering** (ours): its baseline's left end at the run's pen in the sheet's
    /// units and an em of its size, filled and dressed by its look; tracking spaces the letters by that share of the em; cut
    /// at a box when it has one (ours), a letter's glow and shadow with it.
    /// </summary>
    void Letters(in LetterRun run, ReadOnlySpan<char> text)
    {
        if (!faces.TryGetValue(run.Face, out var f) || f.Atlas == 0) return;
        var face = f.Face;
        UseStyled((SDL_GPUTexture*)f.Atlas, Style(face, run.Look));
        float aw = face.AtlasWidth, ah = face.AtlasHeight;
        float x = run.X, y = run.Y, size = run.Size, tracking = run.Tracking;
        var fill = run.Fill;
        bool first = true;
        foreach (char c in text)
        {
            if (face[c] is not { } g) continue;
            if (!first) x += tracking * size;
            first = false;
            if (g.Drawn)
            {
                if (run.Cut is not { } cut)
                    Quad(x + g.Left * size, y + g.Bottom * size, (g.Right - g.Left) * size, (g.Top - g.Bottom) * size,
                         g.X / aw, g.Y / ah, (g.X + g.W) / aw, (g.Y + g.H) / ah, fill);
                else
                {
                    float left = cut.Left, bottom = cut.Bottom, right = cut.Right, top = cut.Top;
                    float qx = x + g.Left * size, qy = y + g.Bottom * size, w = (g.Right - g.Left) * size, h = (g.Top - g.Bottom) * size;
                    float u0 = g.X / aw, u1 = (g.X + g.W) / aw, v0 = g.Y / ah, v1 = (g.Y + g.H) / ah;
                    float du = (u1 - u0) / w, dv = (v1 - v0) / h;
                    // the atlas's rows run down the picture while y runs up the sheet: v0 is the glyph's top
                    if (qx < left) { float cutBy = left - qx; qx += cutBy; w -= cutBy; u0 += cutBy * du; }
                    if (qx + w > right) { float cutBy = qx + w - right; w -= cutBy; u1 -= cutBy * du; }
                    if (qy < bottom) { float cutBy = bottom - qy; qy += cutBy; h -= cutBy; v1 -= cutBy * dv; }
                    if (qy + h > top) { float cutBy = qy + h - top; h -= cutBy; v0 += cutBy * dv; }
                    if (w > 0f && h > 0f) Quad(qx, qy, w, h, u0, v0, u1, v1, fill);
                }
            }
            x += g.Advance * size;
        }
    }

    /// <summary>A look as the shader's block: its sizes, given in ems, taken to the atlas's distances.</summary>
    int Style(DrawFace face, TextLook s)
    {
        float unit = face.Em / (2f * face.Spread);          // an em, in distance units (0.5 is the whole spread)
        var block = new StyleBlock
        {
            Outline = new Vector4(s.Outline.R, s.Outline.G, s.Outline.B, s.Outline.A),
            Shadow = new Vector4(s.Shadow.R, s.Shadow.G, s.Shadow.B, s.Shadow.A),
            Glow = new Vector4(s.Glow.R, s.Glow.G, s.Glow.B, s.Glow.A),
            Shape = new Vector4(Math.Min(0.45f, s.OutlineEm * unit),
                                s.ShadowEm.X * face.Em / face.AtlasWidth, s.ShadowEm.Y * face.Em / face.AtlasHeight,
                                Math.Min(0.48f, s.GlowEm * unit)),
            More = new Vector4(Math.Min(0.45f, s.ShadowSoftEm * unit), 0f, 0f, 0f),
        };
        int found = styles.IndexOf(block);
        if (found >= 0) return found;
        styles.Add(block);
        return styles.Count - 1;
    }

    /// <summary>A face's atlas on the GPU, made once: the shipped greyscale picture, its value in every channel.</summary>
    SDL_GPUTexture* Atlas(DrawFace face)
    {
        SDL_GPUTexture* tex = null;
        if (Vitomir.Platform.Render.Modern.PngDecoder.Decode(face.AtlasPng, out int w, out int h) is { } rgba)
            tex = gpu.CreateTexture(w, h, rgba, mipmaps: false);
        return tex;
    }

    void Start()
    {
        vertices.Clear();
        batches.Clear();
        styles.Clear();
        current = null;
        currentStyle = -1;
        runStart = 0;
    }

    void Use(SDL_GPUTexture* tex) => UseStyled(tex, -1);

    void UseStyled(SDL_GPUTexture* tex, int style)
    {
        if (tex == current && style == currentStyle) return;
        Flush();
        current = tex;
        currentStyle = style;
        runStart = vertices.Count;
    }

    void Flush()
    {
        if (current != null && vertices.Count > runStart)
            batches.Add(new Batch((nint)current, runStart, vertices.Count - runStart, currentStyle));
    }

    /// <summary>(x, y) is the quad's bottom left in the UI's space; the surface's y runs the other way.</summary>
    void Quad(float x, float y, float w, float h, float u0, float v0, float u1, float v1, Rgba t)
    {
        float left = originX + x * scaleX, right = originX + (x + w) * scaleX;
        float bottom = sheetBottom - y * scaleY, top = sheetBottom - (y + h) * scaleY;
        var c = new Vector4(t.R, t.G, t.B, t.A);
        var a = new Vertex { Pos = new Vector2(left, top), Uv = new Vector2(u0, v0), Colour = c };
        var b = new Vertex { Pos = new Vector2(right, top), Uv = new Vector2(u1, v0), Colour = c };
        var d = new Vertex { Pos = new Vector2(left, bottom), Uv = new Vector2(u0, v1), Colour = c };
        var e = new Vertex { Pos = new Vector2(right, bottom), Uv = new Vector2(u1, v1), Colour = c };
        vertices.Add(a); vertices.Add(b); vertices.Add(d);
        vertices.Add(b); vertices.Add(e); vertices.Add(d);
    }

    /// <summary>The quads gathered since the list's start or its last model insert, onto <paramref name="target"/>.</summary>
    void Submit(SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* target)
    {
        Flush();
        current = null;
        if (batches.Count == 0) return;
        Upload(cmd);

        var color = new SDL_GPUColorTargetInfo
        {
            texture = target, load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
        };
        var pass = SDL_BeginGPURenderPass(cmd, &color, 1, null);
        bool letters = false;
        SDL_BindGPUGraphicsPipeline(pass, pipeline);
        var vb = new SDL_GPUBufferBinding { buffer = buffer, offset = 0 };
        SDL_BindGPUVertexBuffers(pass, 0, &vb, 1);
        var prm = new Params { Width = width, Height = height };
        SDL_PushGPUVertexUniformData(cmd, 0, (nint)(&prm), (uint)sizeof(Params));
        int pushed = -1;
        foreach (var batch in batches)
        {
            bool styled = batch.Style >= 0;
            if (styled != letters)
            {
                SDL_BindGPUGraphicsPipeline(pass, styled ? lettersPipeline : pipeline);
                letters = styled;
            }
            if (styled && batch.Style != pushed)
            {
                var block = styles[batch.Style];
                SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&block), (uint)sizeof(StyleBlock));
                pushed = batch.Style;
            }
            var sb = new SDL_GPUTextureSamplerBinding { texture = (SDL_GPUTexture*)batch.Texture, sampler = finerTextures.Contains(batch.Texture) ? finerSampler : sampler };
            Gpu.BindFragmentSampler(pass, sb);
            SDL_DrawGPUPrimitives(pass, (uint)batch.Count, 1, (uint)batch.First, 0);
        }
        SDL_EndGPURenderPass(pass);
    }

    void Upload(SDL_GPUCommandBuffer* cmd)
    {
        if (vertices.Count > capacity)
        {
            if (buffer != null) SDL_ReleaseGPUBuffer(gpu.Device, buffer);
            if (transfer != null) SDL_ReleaseGPUTransferBuffer(gpu.Device, transfer);
            capacity = Math.Max(4096, vertices.Count * 2);
            uint size = (uint)(capacity * sizeof(Vertex));
            var bi = new SDL_GPUBufferCreateInfo { usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX, size = size };
            buffer = SDL_CreateGPUBuffer(gpu.Device, &bi);
            if (buffer == null) throw new SdlException("SDL_CreateGPUBuffer(Ui)");
            var ti = new SDL_GPUTransferBufferCreateInfo { usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD, size = size };
            transfer = SDL_CreateGPUTransferBuffer(gpu.Device, &ti);
            if (transfer == null) throw new SdlException("SDL_CreateGPUTransferBuffer(Ui)");
        }

        uint used = (uint)(vertices.Count * sizeof(Vertex));
        var dst = (Vertex*)SDL_MapGPUTransferBuffer(gpu.Device, transfer, true);
        for (int i = 0; i < vertices.Count; i++) dst[i] = vertices[i];
        SDL_UnmapGPUTransferBuffer(gpu.Device, transfer);

        var copy = SDL_BeginGPUCopyPass(cmd);
        var src = new SDL_GPUTransferBufferLocation { transfer_buffer = transfer, offset = 0 };
        var region = new SDL_GPUBufferRegion { buffer = buffer, offset = 0, size = used };
        SDL_UploadToGPUBuffer(copy, &src, &region, true);
        SDL_EndGPUCopyPass(copy);
    }

    public void Dispose()
    {
        if (buffer != null) SDL_ReleaseGPUBuffer(gpu.Device, buffer);
        if (transfer != null) SDL_ReleaseGPUTransferBuffer(gpu.Device, transfer);
        foreach (var t in textures.Values) if (t.Texture != 0) SDL_ReleaseGPUTexture(gpu.Device, (SDL_GPUTexture*)t.Texture);
        foreach (var t in fonts.Values) if (t.Texture != 0) SDL_ReleaseGPUTexture(gpu.Device, (SDL_GPUTexture*)t.Texture);
        foreach (var t in faces.Values) if (t.Atlas != 0) SDL_ReleaseGPUTexture(gpu.Device, (SDL_GPUTexture*)t.Atlas);
        SDL_ReleaseGPUTexture(gpu.Device, white);
        SDL_ReleaseGPUSampler(gpu.Device, sampler);
        SDL_ReleaseGPUSampler(gpu.Device, finerSampler);
        SDL_ReleaseGPUGraphicsPipeline(gpu.Device, pipeline);
        SDL_ReleaseGPUGraphicsPipeline(gpu.Device, lettersPipeline);
    }
}

