// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours. The lettering's atlas and shader are taken from src/Vitomir.Engine/Fonts and the old renderer's
// UiLetters.hlsl; the layout here is new and small. The interface's
// draw list (src/Vitomir.Platform.Interface) is drawn here too, over the rest. The freed pointer's cursor
// is 1.1's art drawn white, as the old renderer's cursor pass drew it (255 for every
// vertex colour), through Ui.hlsl.
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using SDL;
using Vitomir.Platform.Interface;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Render.Modern;
using static SDL.SDL3;

namespace Vitomir.Platform.Render.Overlay;

/// <summary>
/// What is drawn over the finished picture: the reticle at the centre, the status line in the bottom-left
/// corner and the banner across the top, in our own lettering (the book face's distance-field atlas through
/// the <c>UiLetters</c> shader, so it is sharp at any size and carries an outline and a shadow). Sizes follow
/// the picture's height, so a 2560 × 1440 picture and a 640 × 360 test still look alike. Over them, a screen of
/// the interface (<see cref="LayScreen"/>): its solid rectangles, its words in the three faces' atlases, each run
/// cut to its scrolling area's clip by a scissor rectangle. With the pointer freed, 1.1's cursor for the verb
/// under it, <see cref="Cursors.Units"/> interface units square with its point at its anchor, and the thing's name
/// beside it, dimmed where no verb applies; without 1.1's art a plain arrow. With the reticle on a person to talk to,
/// the person's name under it.
/// </summary>
internal sealed unsafe partial class OverlayPass : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct Vertex(Vector2 pos, Vector2 uv, Vector4 colour)
    {
        public Vector2 Pos = pos;
        public Vector2 Uv = uv;
        public Vector4 Colour = colour;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Style
    {
        public Vector4 Outline, Shadow, Glow, Shape, More;
    }

    /// <summary>A run of the frame's vertices drawn with one texture (0 the solid white, else 1 + a face) and one style, cut to <c>Clip</c> when it has one.</summary>
    readonly record struct Run(int First, int Count, int Texture, Style Style, PixelRect? Clip = null);

    /// <summary>A run's texture number for 1.1's cursor <c>id</c>: drawn by the image pipeline (Ui.hlsl), its colour as it is.</summary>
    const int CursorTextures = 100;

    /// <summary>A run's texture number for a screen's picture (<see cref="DrawKind.Picture"/>): this plus its place in <see cref="pictureTextures"/>, drawn by the image pipeline.</summary>
    const int PictureTextures = 1000;

    /// <summary>The screens' pictures on the GPU (mipmapped: a look's art is drawn smaller than it was painted), and each id's place among them.</summary>
    readonly List<nint> pictureTextures = [];
    readonly Dictionary<int, (int Slot, int Width, int Height)> pictureIds = [];
    readonly List<object> taken = [];

    readonly Gpu gpu;
    readonly SDL_GPUGraphicsPipeline* pipeline, imagePipeline;
    /// <summary>1.1's cursors' art on the GPU, made the first time each is drawn; tried once.</summary>
    readonly SDL_GPUTexture*[] cursorTextures = new SDL_GPUTexture*[Cursors.Count];
    readonly bool[] cursorTried = new bool[Cursors.Count];

    /// <summary>1.1's art for a cursor (the program's, from the install), or null for a plain arrow.</summary>
    public Func<CursorId, Assets.TextureImage?>? CursorArt { get; set; }
    readonly SDL_GPUTexture* atlas, solid;
    /// <summary>
    /// The interface's faces' atlases, by <see cref="FaceId"/> (the book face's is <see cref="atlas"/>): our three made with the
    /// pass, the delivered faces' (the launcher's) the first time a screen sets words in one.
    /// </summary>
    readonly SDL_GPUTexture*[] faceAtlases = new SDL_GPUTexture*[Interface.Face.Count];
    readonly Interface.Face?[] faces = new Interface.Face?[Interface.Face.Count];
    readonly Stack<PixelRect> clips = new();
    readonly List<PlacedGlyph> placed = [];
    readonly SDL_GPUSampler* sampler;
    readonly Face face;
    readonly List<Vertex> vertices = [];
    readonly List<Run> runs = [];
    SDL_GPUBuffer* buffer;
    SDL_GPUTransferBuffer* transfer;
    uint bufferBytes;

    public OverlayPass(Gpu gpu)
    {
        this.gpu = gpu;
        face = Face.Load("book");
        var up = new ModernDevice.Uploader(gpu);
        atlas = up.Texture(face.AtlasWidth, face.AtlasHeight, face.Pixels, srgb: false, mips: false);
        solid = up.Texture(1, 1, [255, 255, 255, 255], srgb: false, mips: false);
        faceAtlases[(int)FaceId.Book] = atlas;
        foreach (var id in new[] { FaceId.Uncial, FaceId.Capitals })
        {
            // Without mips, as the book face's (a mip of a distance field loses thin strokes).
            var pixels = Face.Atlas(Interface.Face.FileName(id), out int aw, out int ah);
            faceAtlases[(int)id] = up.Texture(aw, ah, pixels, srgb: false, mips: false);
        }
        up.Submit();
        sampler = ModernDevice.CreateSampler(gpu, false, false, false, false, 1f);

        var vs = gpu.LoadShader("UiLetters", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, samplers: 0, uniformBuffers: 1);
        var fs = gpu.LoadShader("UiLetters", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, samplers: Gpu.FragmentSamplers, uniformBuffers: 1);
        var desc = new SDL_GPUVertexBufferDescription { slot = 0, pitch = (uint)sizeof(Vertex), input_rate = SDL_GPUVertexInputRate.SDL_GPU_VERTEXINPUTRATE_VERTEX };
        var attrs = stackalloc SDL_GPUVertexAttribute[3];
        attrs[0] = new SDL_GPUVertexAttribute { location = 0, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2, offset = 0 };
        attrs[1] = new SDL_GPUVertexAttribute { location = 1, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2, offset = 8 };
        attrs[2] = new SDL_GPUVertexAttribute { location = 2, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT4, offset = 16 };
        var blend = new SDL_GPUColorTargetBlendState
        {
            enable_blend = true,
            color_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD, alpha_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
            src_color_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_SRC_ALPHA,
            dst_color_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
            src_alpha_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE,
            dst_alpha_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
        };
        var colour = new SDL_GPUColorTargetDescription { format = gpu.ColorFormat, blend_state = blend };
        var info = new SDL_GPUGraphicsPipelineCreateInfo
        {
            vertex_shader = vs, fragment_shader = fs,
            vertex_input_state = new SDL_GPUVertexInputState { vertex_buffer_descriptions = &desc, num_vertex_buffers = 1, vertex_attributes = attrs, num_vertex_attributes = 3 },
            primitive_type = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST,
            rasterizer_state = new SDL_GPURasterizerState { fill_mode = SDL_GPUFillMode.SDL_GPU_FILLMODE_FILL, cull_mode = SDL_GPUCullMode.SDL_GPU_CULLMODE_NONE },
            target_info = new SDL_GPUGraphicsPipelineTargetInfo { color_target_descriptions = &colour, num_color_targets = 1 },
        };
        pipeline = SDL_CreateGPUGraphicsPipeline(gpu.Device, &info);
        SDL_ReleaseGPUShader(gpu.Device, vs);
        SDL_ReleaseGPUShader(gpu.Device, fs);
        // The same vertices through Ui.hlsl, which takes the texture's colour as well as its alpha (the cursors' art).
        var ivs = gpu.LoadShader("Ui", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, samplers: 0, uniformBuffers: 1);
        var ifs = gpu.LoadShader("Ui", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, samplers: Gpu.FragmentSamplers, uniformBuffers: 0);
        info.vertex_shader = ivs;
        info.fragment_shader = ifs;
        imagePipeline = SDL_CreateGPUGraphicsPipeline(gpu.Device, &info);
        SDL_ReleaseGPUShader(gpu.Device, ivs);
        SDL_ReleaseGPUShader(gpu.Device, ifs);
        if (imagePipeline == null) throw new SdlException("SDL_CreateGPUGraphicsPipeline(overlay images)");
        if (pipeline == null) throw new SdlException("SDL_CreateGPUGraphicsPipeline(overlay)");
    }

    // ----- what is drawn -----

    /// <summary>The reticle's colours: light on a dark rim; a warm red when the answer is blocked.</summary>
    static readonly Vector4 ReticleLight = new(0.95f, 0.95f, 0.92f, 0.9f), ReticleBlocked = new(0.95f, 0.35f, 0.25f, 0.95f),
                            ReticleRim = new(0f, 0f, 0f, 0.55f);

    /// <summary>
    /// The overlay for <paramref name="frame"/> on a picture of <paramref name="w"/> × <paramref name="h"/>:
    /// the reticle (a dot when it is on nothing, a cross otherwise, tinted when blocked), the banner and the
    /// status line.
    /// </summary>
    public void Lay(FrameView frame, int w, int h)
    {
        vertices.Clear();
        runs.Clear();
        float unit = MathF.Max(1f, h / 360f);   // a pixel of a 640 × 360 picture
        // The reticle at its opacity (faint in melee); none at all at 0.
        float reticleAlpha = Math.Clamp(frame.ReticleOpacity, 0f, 1f);
        if (frame.ShowReticle && reticleAlpha > 0f)
        {
            var colour = Faded(frame.Reticle.Blocked ? ReticleBlocked : ReticleLight, reticleAlpha);
            var rim = Faded(ReticleRim, reticleAlpha);
            // On whole pixels, so a one-pixel line is one pixel and not two half-lit ones.
            int cx = w / 2, cy = h / 2;
            if (frame.Reticle.Kind == ReticleTarget.Nothing)
            {
                int dot = Math.Max(2, (int)MathF.Round(3f * unit));
                Solid(Pixels(cx - dot / 2 - 1, cy - dot / 2 - 1, dot + 2, dot + 2), rim);
                Solid(Pixels(cx - dot / 2, cy - dot / 2, dot, dot), colour);
            }
            else
            {
                int arm = (int)MathF.Round(6f * unit), thick = Math.Max(1, (int)MathF.Round(unit));
                int a0 = cx - arm, b0 = cy - thick / 2, a1 = cy - arm, b1 = cx - thick / 2;
                Solid(Pixels(a0 - 1, b0 - 1, 2 * arm + thick + 2, thick + 2), rim);
                Solid(Pixels(b1 - 1, a1 - 1, thick + 2, 2 * arm + thick + 2), rim);
                Solid(Pixels(a0, b0, 2 * arm + thick, thick), colour);
                Solid(Pixels(b1, a1, thick, 2 * arm + thick), colour);
            }
        }
        if (frame.CombatLog is { } log) LayLog(log, frame.Seconds, frame.Banner is { Length: > 0 }, w, h, frame.InterfaceScale);
        if (frame.Banner is { Length: > 0 } banner)
        {
            float size = 14f * unit;
            float band = size * 1.6f;
            Solid((Vector2.Zero, new Vector2(w, band)), new Vector4(0.08f, 0.02f, 0.02f, 0.72f));
            float width = face.Measure(banner) * size;
            Text(banner, new Vector2(MathF.Max(4f, (w - width) * 0.5f), band * 0.5f + size * 0.3f), size, new Vector4(1f, 0.82f, 0.45f, 1f));
        }
        if (frame.StatusLine is { Length: > 0 } status)
        {
            float size = 9f * unit;
            Text(status, new Vector2(6f * unit, h - 6f * unit), size, new Vector4(0.92f, 0.92f, 0.88f, 1f));
        }
        // The reticle on a person to talk to says who, under it (in melee as faint as the reticle; none when it is hidden).
        if (frame.ShowReticle && reticleAlpha > 0f && frame.ReticleVerb == Cameras.PointerVerb.Talk && frame.Reticle.Name is { Length: > 0 } who)
        {
            float size = 8f * unit;
            Text(who, new Vector2(w * 0.5f - face.Measure(who) * size * 0.5f, h * 0.5f + 16f * unit), size, Faded(NameLit, reticleAlpha));   // as faint as the reticle
        }
        if (frame.Cursor is { } cursor) LayCursor(cursor, w, h, frame.InterfaceScale);
    }

    /// <summary>The name beside the cursor: lit where a verb applies, dimmed where none does.</summary>
    static readonly Vector4 NameLit = new(1f, 0.9f, 0.7f, 1f), NameDim = new(0.78f, 0.76f, 0.72f, 0.6f);

    /// <summary>The cursor's size in pixels on a picture <paramref name="h"/> high: <see cref="Cursors.Units"/> interface units (the interface's 1440 lines, the player's scale).</summary>
    public static float CursorPixels(int w, int h, float scale) => Cursors.Units * new UiMetrics(w, h, scale).Pixel;

    /// <summary>The freed pointer's cursor: 1.1's art for its verb with the point at its anchor (or a plain arrow), and the name beside it.</summary>
    void LayCursor(PointerCursor cursor, int w, int h, float scale)
    {
        float size = CursorPixels(w, h, scale);
        var at = new Vector2(cursor.Place.X * w, cursor.Place.Y * h);
        var art = Art(cursor.Cursor);
        var min = at - (art is not null ? Cursors.Hotspot(cursor.Cursor, size) : Vector2.Zero);
        if (art is not null)
        {
            int first = vertices.Count;
            Quad(min, min + new Vector2(size), Vector2.Zero, Vector2.One, Vector4.One);
            Add(first, CursorTextures + (int)cursor.Cursor, default);
        }
        else Arrow(at, size, cursor.Cursor);
        if (cursor.Name is { Length: > 0 } name)
        {
            float text = MathF.Max(9f, size * 0.42f);
            Text(name, new Vector2(min.X + size * 1.05f, min.Y + size * 0.62f), text, cursor.Dimmed ? NameDim : NameLit);
        }
    }

    /// <summary>A plain arrow with its tip at <paramref name="tip"/> (no 1.1 install): white on a dark rim, warm for talk, red for a refusal.</summary>
    void Arrow(Vector2 tip, float size, CursorId id)
    {
        var fill = id switch
        {
            CursorId.Talk => new Vector4(1f, 0.85f, 0.5f, 1f),
            CursorId.Negative => new Vector4(0.95f, 0.35f, 0.25f, 1f),
            CursorId.Target => new Vector4(0.6f, 0.9f, 1f, 1f),
            _ => new Vector4(0.95f, 0.95f, 0.92f, 1f),
        };
        var rim = new Vector4(0f, 0f, 0f, 0.8f);
        // Rows down from the tip, each a little wider: a right triangle along the left edge, its stem below.
        int rows = Math.Max(8, (int)MathF.Round(size * 0.7f));
        float px = MathF.Max(1f, MathF.Round(size / 32f));
        for (int i = 0; i < rows; i++)
        {
            float width = MathF.Max(px, (i + 1) * 0.6f);
            Solid((tip + new Vector2(-px, i - px), tip + new Vector2(width + px, i + 1 + px)), rim);
        }
        for (int i = 0; i < rows; i++)
        {
            float width = MathF.Max(px, (i + 1) * 0.6f);
            Solid((tip + new Vector2(0, i), tip + new Vector2(width, i + 1)), fill);
        }
    }

    /// <summary>A cursor's texture, made from 1.1's art the first time it is asked for, or null without art.</summary>
    SDL_GPUTexture* Art(CursorId id)
    {
        int i = (int)id;
        if ((uint)i >= cursorTextures.Length) return null;
        if (!cursorTried[i])
        {
            cursorTried[i] = true;
            if (CursorArt?.Invoke(id) is { Width: > 0, Height: > 0 } art && art.Rgba.Length >= art.Width * art.Height * 4)
            {
                var up = new ModernDevice.Uploader(gpu);
                cursorTextures[i] = up.Texture(art.Width, art.Height, art.Rgba, srgb: false, mips: false);
                up.Submit();
            }
        }
        return cursorTextures[i];
    }

    static (Vector2 Min, Vector2 Max) Pixels(int x, int y, int width, int height) => (new Vector2(x, y), new Vector2(x + width, y + height));

    void Solid((Vector2 Min, Vector2 Max) r, Vector4 colour)
    {
        int first = vertices.Count;
        Quad(r.Min, r.Max, Vector2.Zero, Vector2.One, colour);
        Add(first, 0, default);
    }

    /// <summary>A colour with its alpha times <paramref name="alpha"/>.</summary>
    internal static Vector4 Faded(Vector4 c, float alpha) => c with { W = c.W * alpha };

    /// <summary>A line of text with its baseline's left end at <paramref name="pen"/>, <paramref name="size"/> pixels to the em.</summary>
    void Text(string text, Vector2 pen, float size, Vector4 colour) => Text(text.AsSpan(), pen, size, colour);

    /// <summary>The same for a part of a string (the combat log's wrapped rows, drawn without making a string for each).</summary>
    void Text(ReadOnlySpan<char> text, Vector2 pen, float size, Vector4 colour)
    {
        int first = vertices.Count;
        float x = pen.X;
        foreach (var rune in text.EnumerateRunes())
        {
            if (!face.Glyphs.TryGetValue(rune.Value, out var g) && !face.Glyphs.TryGetValue('?', out g)) continue;
            if (g.AtlasW > 0 && g.AtlasH > 0)
            {
                var min = new Vector2(x + g.X0 * size, pen.Y - g.Y1 * size);
                var max = new Vector2(x + g.X1 * size, pen.Y - g.Y0 * size);
                var uv0 = new Vector2(g.AtlasX / (float)face.AtlasWidth, g.AtlasY / (float)face.AtlasHeight);
                var uv1 = new Vector2((g.AtlasX + g.AtlasW) / (float)face.AtlasWidth, (g.AtlasY + g.AtlasH) / (float)face.AtlasHeight);
                Quad(min, max, uv0, uv1, colour);
            }
            x += g.Advance * size;
        }
        // Distances: an em is Em / (2 · Spread) units (0.5 is the whole spread), as the old interface's styles.
        float unit = face.Em / (2f * face.Spread);
        float shadowEm = 0.07f;
        var style = new Style
        {
            Outline = new Vector4(0f, 0f, 0f, 0.85f),
            Shadow = new Vector4(0f, 0f, 0f, 0.6f),
            Shape = new Vector4(MathF.Min(0.45f, 0.07f * unit), shadowEm * face.Em / face.AtlasWidth, shadowEm * face.Em / face.AtlasHeight, 0f),
            More = new Vector4(MathF.Min(0.45f, 0.08f * unit), 0f, 0f, 0f),
        };
        Add(first, 1 + (int)FaceId.Book, style);
    }

    void Quad(Vector2 min, Vector2 max, Vector2 uv0, Vector2 uv1, Vector4 colour)
    {
        vertices.Add(new Vertex(min, uv0, colour));
        vertices.Add(new Vertex(new Vector2(max.X, min.Y), new Vector2(uv1.X, uv0.Y), colour));
        vertices.Add(new Vertex(max, uv1, colour));
        vertices.Add(new Vertex(min, uv0, colour));
        vertices.Add(new Vertex(max, uv1, colour));
        vertices.Add(new Vertex(new Vector2(min.X, max.Y), new Vector2(uv0.X, uv1.Y), colour));
    }

    void Add(int first, int texture, Style style)
    {
        int count = vertices.Count - first;
        if (count > 0) runs.Add(new Run(first, count, texture, style, clips.Count > 0 ? clips.Peek() : null));
    }

    // ----- a screen of the interface -----

    /// <summary>
    /// Lays a screen's draw list (made for this picture's size) over what <see cref="Lay"/> laid: its rectangles, its
    /// words (each run in its face's atlas, at its pen, <see cref="DrawCommand.Size"/> pixels to the em, dressed by
    /// its <see cref="TextLook"/>), and its clips.
    /// </summary>
    public void LayScreen(DrawList list)
    {
        clips.Clear();
        TakePictures(list.Resources);
        foreach (var c in list.Commands)
            switch (c.Kind)
            {
                case DrawKind.Picture:
                    {
                        var q = list.Pictures[c.Item];
                        if (!pictureIds.TryGetValue(q.Picture, out var at)) break;
                        int start = vertices.Count;
                        Quad(new Vector2(q.X, q.Y), new Vector2(q.X + q.W, q.Y + q.H), new Vector2(q.U0, q.V0), new Vector2(q.U1, q.V1), Colour(q.Tint));
                        Add(start, PictureTextures + at.Slot, default);
                        break;
                    }
                case DrawKind.Solid:
                    int first = vertices.Count;
                    Quad(new Vector2(c.Rect.X, c.Rect.Y), new Vector2(c.Rect.Right, c.Rect.Bottom), Vector2.Zero, Vector2.One, Colour(c.Colour));
                    Add(first, 0, default);
                    break;
                case DrawKind.Text:
                    ScreenText(c);
                    break;
                case DrawKind.PushClip:
                    var r = c.Rect;
                    if (clips.Count > 0)
                    {
                        var o = clips.Peek();
                        int x0 = Math.Max(r.X, o.X), y0 = Math.Max(r.Y, o.Y), x1 = Math.Min(r.Right, o.Right), y1 = Math.Min(r.Bottom, o.Bottom);
                        r = new PixelRect(x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
                    }
                    clips.Push(r);
                    break;
                case DrawKind.PopClip:
                    if (clips.Count > 0) clips.Pop();
                    break;
            }
        clips.Clear();
    }

    static Vector4 Colour(Rgba c) => new(c.R, c.G, c.B, c.A);

    /// <summary>
    /// The pictures a screen's list sent (<see cref="DrawList.Resources"/>): each made on the GPU the first time with its mips (sent
    /// again at its size: its pixels replaced). A screen of the game sends none; the rest of what a list may send is the 1.1
    /// interface's and is not for this pass.
    /// </summary>
    void TakePictures(DrawResources resources)
    {
        if (resources.Pending == 0) return;
        taken.Clear();
        resources.Take(taken);
        foreach (var r in taken)
        {
            if (r is not DrawPicture { Gone: false } p) continue;
            if (pictureIds.TryGetValue(p.Id, out var had) && had.Width == p.Width && had.Height == p.Height)
            {
                SDL_ReleaseGPUTexture(gpu.Device, (SDL_GPUTexture*)pictureTextures[had.Slot]);
                pictureTextures[had.Slot] = (nint)Upload(p);
                continue;
            }
            pictureIds[p.Id] = (pictureTextures.Count, p.Width, p.Height);
            pictureTextures.Add((nint)Upload(p));
        }
        taken.Clear();
    }

    SDL_GPUTexture* Upload(DrawPicture p)
    {
        var up = new ModernDevice.Uploader(gpu);
        var tex = up.Texture(p.Width, p.Height, p.Rgba!, srgb: false, mips: p.Mipmaps);
        up.Submit();
        return tex;
    }

    void ScreenText(in DrawCommand c)
    {
        var face = faces[(int)c.Face] ??= Interface.Face.Get(c.Face);
        if (faceAtlases[(int)c.Face] == null)
        {
            // A delivered face's atlas, the first time (without mips, as the others').
            var pixels = Face.Atlas(Interface.Face.FileName(c.Face), out int aw, out int ah);
            var up = new ModernDevice.Uploader(gpu);
            faceAtlases[(int)c.Face] = up.Texture(aw, ah, pixels, srgb: false, mips: false);
            up.Submit();
        }
        int first = vertices.Count;
        float size = c.Size;
        var colour = Colour(c.Colour);
        // The glyphs where the interface measured them (a face with pairs kerned: Face.Place).
        placed.Clear();
        face.Place(c.Text!, c.Pen.X, size, placed);
        foreach (var (g, x) in placed)
        {
            if (!g.HasInk) continue;
            var min = new Vector2(x + g.X0 * size, c.Pen.Y - g.Y1 * size);
            var max = new Vector2(x + g.X1 * size, c.Pen.Y - g.Y0 * size);
            var uv0 = new Vector2(g.AtlasX / (float)face.AtlasWidth, g.AtlasY / (float)face.AtlasHeight);
            var uv1 = new Vector2((g.AtlasX + g.AtlasW) / (float)face.AtlasWidth, (g.AtlasY + g.AtlasH) / (float)face.AtlasHeight);
            Quad(min, max, uv0, uv1, colour);
        }
        // Distances: an em is Em / (2 · Spread) distance units (0.5 is the whole spread), as Text's above.
        float unit = face.Em / (2f * face.Spread);
        var look = c.Look;
        var style = new Style
        {
            Outline = Colour(look.Outline),
            Shadow = Colour(look.Shadow),
            Glow = Colour(look.Glow),
            Shape = new Vector4(MathF.Min(0.45f, look.OutlineEm * unit), look.ShadowEm.X * face.Em / face.AtlasWidth, look.ShadowEm.Y * face.Em / face.AtlasHeight,
                                MathF.Min(0.48f, look.GlowEm * unit)),
            More = new Vector4(MathF.Min(0.45f, look.ShadowSoftEm * unit), 0f, 0f, 0f),
        };
        Add(first, 1 + (int)c.Face, style);
    }

    /// <summary>Whether <see cref="Lay"/> left anything to draw.</summary>
    public bool Empty => runs.Count == 0;

    /// <summary>Draws what <see cref="Lay"/> laid over <paramref name="target"/> (its contents kept).</summary>
    public void Draw(SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* target, int w, int h)
    {
        if (runs.Count == 0) return;
        Upload(cmd);
        var colour = new SDL_GPUColorTargetInfo { texture = target, load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE };
        var pass = SDL_BeginGPURenderPass(cmd, &colour, 1, null);
        var port = new SDL_GPUViewport { x = 0, y = 0, w = w, h = h, min_depth = 0, max_depth = 1 };
        SDL_SetGPUViewport(pass, &port);
        SDL_BindGPUGraphicsPipeline(pass, pipeline);
        var vb = new SDL_GPUBufferBinding { buffer = buffer };
        SDL_BindGPUVertexBuffers(pass, 0, &vb, 1);
        var screen = new Vector4(w, h, 0, 0);
        SDL_PushGPUVertexUniformData(cmd, 0, (nint)(&screen), (uint)sizeof(Vector4));
        var whole = new SDL_Rect { x = 0, y = 0, w = w, h = h };
        PixelRect? scissor = null;
        bool hidden = false;    // the clip in force has nothing of the picture in it
        bool images = false;    // the image pipeline (a cursor's art) is bound, not the letters'
        SDL_SetGPUScissor(pass, &whole);
        foreach (var r in runs)
        {
            if (r.Texture >= CursorTextures != images)
            {
                images = !images;
                SDL_BindGPUGraphicsPipeline(pass, images ? imagePipeline : pipeline);
            }
            if (r.Clip != scissor)
            {
                scissor = r.Clip;
                var s = whole;
                if (r.Clip is { } c)
                {
                    int x0 = Math.Clamp(c.X, 0, w), y0 = Math.Clamp(c.Y, 0, h);
                    s = new SDL_Rect { x = x0, y = y0, w = Math.Max(0, Math.Min(c.Right, w) - x0), h = Math.Max(0, Math.Min(c.Bottom, h) - y0) };
                }
                hidden = s.w <= 0 || s.h <= 0;
                if (!hidden) SDL_SetGPUScissor(pass, &s);
            }
            if (hidden) continue;
            if (images)
            {
                var texture = r.Texture >= PictureTextures ? (SDL_GPUTexture*)pictureTextures[r.Texture - PictureTextures] : cursorTextures[r.Texture - CursorTextures];
                Gpu.BindFragmentSampler(pass, new SDL_GPUTextureSamplerBinding { texture = texture, sampler = sampler });
                SDL_DrawGPUPrimitives(pass, (uint)r.Count, 1, (uint)r.First, 0);
                continue;
            }
            Gpu.BindFragmentSampler(pass, new SDL_GPUTextureSamplerBinding { texture = r.Texture == 0 ? solid : faceAtlases[r.Texture - 1], sampler = sampler });
            var style = r.Style;
            SDL_PushGPUFragmentUniformData(cmd, 0, (nint)(&style), (uint)sizeof(Style));
            SDL_DrawGPUPrimitives(pass, (uint)r.Count, 1, (uint)r.First, 0);
        }
        SDL_EndGPURenderPass(pass);
    }

    void Upload(SDL_GPUCommandBuffer* cmd)
    {
        uint bytes = (uint)(vertices.Count * sizeof(Vertex));
        if (bytes > bufferBytes)
        {
            if (buffer != null) SDL_ReleaseGPUBuffer(gpu.Device, buffer);
            if (transfer != null) SDL_ReleaseGPUTransferBuffer(gpu.Device, transfer);
            bufferBytes = Math.Max(bytes * 2, 4096u);
            var binfo = new SDL_GPUBufferCreateInfo { usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX, size = bufferBytes };
            buffer = SDL_CreateGPUBuffer(gpu.Device, &binfo);
            var tinfo = new SDL_GPUTransferBufferCreateInfo { usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD, size = bufferBytes };
            transfer = SDL_CreateGPUTransferBuffer(gpu.Device, &tinfo);
            if (buffer == null || transfer == null) throw new SdlException("SDL_CreateGPUBuffer (overlay)");
        }
        var map = (byte*)SDL_MapGPUTransferBuffer(gpu.Device, transfer, true);
        MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(vertices)).CopyTo(new Span<byte>(map, (int)bytes));
        SDL_UnmapGPUTransferBuffer(gpu.Device, transfer);
        var copy = SDL_BeginGPUCopyPass(cmd);
        var from = new SDL_GPUTransferBufferLocation { transfer_buffer = transfer, offset = 0 };
        var to = new SDL_GPUBufferRegion { buffer = buffer, offset = 0, size = bytes };
        SDL_UploadToGPUBuffer(copy, &from, &to, true);
        SDL_EndGPUCopyPass(copy);
    }

    public void Dispose()
    {
        if (buffer != null) SDL_ReleaseGPUBuffer(gpu.Device, buffer);
        if (transfer != null) SDL_ReleaseGPUTransferBuffer(gpu.Device, transfer);
        SDL_ReleaseGPUTexture(gpu.Device, atlas);
        SDL_ReleaseGPUTexture(gpu.Device, solid);
        SDL_ReleaseGPUTexture(gpu.Device, faceAtlases[(int)FaceId.Uncial]);
        SDL_ReleaseGPUTexture(gpu.Device, faceAtlases[(int)FaceId.Capitals]);
        for (int i = (int)FaceId.Text; i < faceAtlases.Length; i++)
            if (faceAtlases[i] != null) SDL_ReleaseGPUTexture(gpu.Device, faceAtlases[i]);
        SDL_ReleaseGPUSampler(gpu.Device, sampler);
        SDL_ReleaseGPUGraphicsPipeline(gpu.Device, pipeline);
        SDL_ReleaseGPUGraphicsPipeline(gpu.Device, imagePipeline);
        foreach (var t in cursorTextures) if (t != null) SDL_ReleaseGPUTexture(gpu.Device, t);
        foreach (var t in pictureTextures) if (t != 0) SDL_ReleaseGPUTexture(gpu.Device, (SDL_GPUTexture*)t);
    }

    // ----- the face -----

    /// <summary>One glyph: its advance and its quad's box in ems (y up from the baseline), and its place in the atlas in texels.</summary>
    internal readonly record struct Glyph(float Advance, float X0, float Y0, float X1, float Y1, int AtlasX, int AtlasY, int AtlasW, int AtlasH);

    /// <summary>A distance-field face as tools/ui/font writes it: <c>&lt;name&gt;.json</c> and its atlas <c>&lt;name&gt;.png</c>.</summary>
    internal sealed class Face
    {
        public required int AtlasWidth { get; init; }
        public required int AtlasHeight { get; init; }
        public required float Em { get; init; }
        public required float Spread { get; init; }
        public required Dictionary<int, Glyph> Glyphs { get; init; }
        public required byte[] Pixels { get; init; }

        /// <summary>A line's width in ems.</summary>
        public float Measure(string text)
        {
            float x = 0;
            foreach (var r in text.EnumerateRunes())
                if (Glyphs.TryGetValue(r.Value, out var g) || Glyphs.TryGetValue('?', out g)) x += g.Advance;
            return x;
        }

        /// <summary>A face's atlas from this assembly's resources, decoded to RGBA.</summary>
        public static byte[] Atlas(string name, out int w, out int h)
        {
            using var ps = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Fonts/{name}.png") ?? throw new FileNotFoundException($"embedded atlas Fonts/{name}.png missing");
            var bytes = new byte[ps.Length];
            ps.ReadExactly(bytes);
            return PngDecoder.Decode(bytes, out w, out h) ?? throw new InvalidDataException($"Fonts/{name}.png does not decode");
        }

        public static Face Load(string name)
        {
            var asm = Assembly.GetExecutingAssembly();
            using var js = asm.GetManifestResourceStream($"Fonts/{name}.json") ?? throw new FileNotFoundException($"embedded face Fonts/{name}.json missing");
            using var doc = JsonDocument.Parse(js);
            var root = doc.RootElement;
            var glyphs = new Dictionary<int, Glyph>();
            foreach (var g in root.GetProperty("glyphs").EnumerateObject())
            {
                var v = g.Value;
                float advance = v.GetProperty("advance").GetSingle();
                // A glyph with no ink (a space) has no box and no place in the atlas: only its advance.
                if (!v.TryGetProperty("box", out var box) || !v.TryGetProperty("atlas", out var at))
                {
                    glyphs[int.Parse(g.Name)] = new Glyph(advance, 0, 0, 0, 0, 0, 0, 0, 0);
                    continue;
                }
                glyphs[int.Parse(g.Name)] = new Glyph(advance, box[0].GetSingle(), box[1].GetSingle(), box[2].GetSingle(), box[3].GetSingle(),
                                                      at[0].GetInt32(), at[1].GetInt32(), at[2].GetInt32(), at[3].GetInt32());
            }
            using var ps = asm.GetManifestResourceStream($"Fonts/{name}.png") ?? throw new FileNotFoundException($"embedded atlas Fonts/{name}.png missing");
            var bytes = new byte[ps.Length];
            ps.ReadExactly(bytes);
            var pixels = PngDecoder.Decode(bytes, out int w, out int h) ?? throw new InvalidDataException($"Fonts/{name}.png does not decode");
            return new Face
            {
                AtlasWidth = w, AtlasHeight = h, Em = root.GetProperty("em").GetSingle(), Spread = root.GetProperty("spread").GetSingle(),
                Glyphs = glyphs, Pixels = pixels,
            };
        }
    }
}
