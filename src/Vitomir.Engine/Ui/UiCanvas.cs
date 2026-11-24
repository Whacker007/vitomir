// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// **1.1's interface emitted into the platform's draw list**.
// The interface passes draw with this exactly as they drew with the old renderer's immediate 2D layer (its calls,
// arguments and arithmetic are that layer's, Vitomir.Render/UiRenderer.cs at 38e81a4): a pane's quads, icons, sub-pictures,
// boxes, 1.1's bitmap text and our lettering in the 640x480 sheet with y up. What they say goes into a DrawList, and the
// pictures, fonts and faces it names go once through its resource channel by id; the old renderer's 2D backend draws the
// list and reads no engine object. Engine code, so the Classic module and vitomir-tools both emit with it.
using Vitomir.Engine.World;
using Vitomir.Formats;
using Vitomir.Platform.Interface;
using Vitomir.Platform.Presentation.Classic;

namespace Vitomir.Engine.Ui;

/// <summary>The interface's 2D drawing as draw-list entries: one per program (or test), shared by its passes.</summary>
public sealed class UiCanvas(UiAssets assets)
{
    readonly Dictionary<string, int> pictures = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<NfdFont, int> fonts = [];
    readonly Dictionary<Lettering, int> faces = [];
    readonly Dictionary<ClassicScene, int> scenes = [];
    int nextPicture = 1, nextFont = 1, nextFace = 1, nextScene = 1;
    int finerRefusedSaid;

    public UiAssets Assets => assets;

    /// <summary>What the passes have said since the last <see cref="Clear"/>; the backend draws it.</summary>
    public DrawList List { get; } = new();

    /// <summary>How the 640x480 space maps onto the picture. Set by <see cref="Begin"/>.</summary>
    public float ScaleX { get; private set; } = 1f;
    public float ScaleY { get; private set; } = 1f;

    /// <summary>Empties the list (what its resources hold unread stays): the start of each pass's drawing.</summary>
    public void Clear() => List.Clear();

    /// <summary>Fixes the 640x480 space onto a picture of this size, from here on (<see cref="UiLayout"/>: 1.1's stretch, fitted, or the 16:9 stretch).</summary>
    public void Begin(int surfaceWidth, int surfaceHeight)
    {
        int width = Math.Max(1, surfaceWidth), height = Math.Max(1, surfaceHeight);
        var map = UiLayout.On(width, height);
        (ScaleX, ScaleY) = (map.ScaleX, map.ScaleY);
        List.SizeTo(width, height);
        List.Space(new DrawSpace(map.ScaleX, map.ScaleY, map.X, map.Bottom));
    }

    /// <summary>
    /// The same for **a pane that sits above the status bar** (ours): the sheet through the panes' own map
    /// (<see cref="UiLayout.Panes(float, float)"/>: centred in the rows above the bar where the sheet is shorter than
    /// the picture, the sheet's own map everywhere else). The pack's, the character sheet's, the quests', the
    /// disciplines', the vault's and the shop's passes begin with this; nothing else does.
    /// </summary>
    public void BeginPanes(int surfaceWidth, int surfaceHeight)
    {
        int width = Math.Max(1, surfaceWidth), height = Math.Max(1, surfaceHeight);
        var map = UiLayout.Panes(width, height);
        (ScaleX, ScaleY) = (map.ScaleX, map.ScaleY);
        List.SizeTo(width, height);
        List.Space(new DrawSpace(map.ScaleX, map.ScaleY, map.X, map.Bottom));
    }

    /// <summary>
    /// **A pane's own quad**: (x, y) its bottom left, the artwork taken from the texture's origin 1:1 and clipped to the smaller
    /// rectangle, as the engine's blit (the old layer's <c>Draw</c>, which says why: 12 of the shipped panes' 98 quads).
    /// </summary>
    public void Draw(string texture, float x, float y, float w, float h, Tint? tint = null)
    {
        var image = assets.Texture(texture);
        if (image is null || image.Width <= 0 || image.Height <= 0) return;
        float cw = Math.Min(w, image.Width), ch = Math.Min(h, image.Height);
        Sub(texture, x, y, cw, ch, 0f, 1f - ch / image.Height, cw / image.Width, 1f, tint);
    }

    /// <summary>Part of a texture — what a progress bar's fill needs. UVs run 0..1 with v = 0 at the top.</summary>
    public void Sub(string texture, float x, float y, float w, float h, float u0, float v0, float u1, float v1, Tint? tint = null)
    {
        int id = Picture(texture);
        if (id <= 0) return;
        List.Image(new DrawImage(id, x, y, w, h, u0, v0, u1, v1, (tint ?? Tint.White).Rgba));
    }

    /// <summary>
    /// An **icon**: a w x h rectangle from the picture's own bottom-left origin put down 1:1, a source
    /// smaller than the cell drawn whole. False when the texture is not in the install, so a caller can fall back.
    /// </summary>
    public bool Icon(string texture, float x, float y, float w, float h, Tint? tint = null)
    {
        var image = assets.Texture(texture);
        if (image is null || image.Width <= 0 || image.Height <= 0) return false;
        float cw = Math.Min(w, image.Width), ch = Math.Min(h, image.Height);
        Sub(texture, x, y, cw, ch, 0f, 1f - ch / image.Height, cw / image.Width, 1f, tint);
        return true;
    }

    /// <summary>
    /// An icon stretched to fill its cell, then moved so its **artwork** is centred in it (matched to the original by eye,
    /// as the old layer's <c>Fit</c> was).
    /// </summary>
    public bool Fit(string texture, float x, float y, float w, float h, Tint? tint = null)
    {
        var image = assets.Texture(texture);
        if (image is null || image.Width <= 0 || image.Height <= 0) return false;
        var (x0, y0, x1, y1) = assets.Bounds(texture);
        float scaleX = w / image.Width, scaleY = h / image.Height;
        float dx = ((image.Width - x1) - x0) / 2f * scaleX;
        float dy = (y0 - (image.Height - y1)) / 2f * scaleY;
        Sub(texture, x + dx, y + dy, w, h, 0f, 0f, 1f, 1f, tint);
        return true;
    }

    /// <summary>A streamed picture (a film's frame, <see cref="NewPicture"/>) stretched whole into the quad; 0 draws nothing.</summary>
    public void Picture(int picture, float x, float y, float w, float h, Tint? tint = null)
    {
        if (picture <= 0) return;
        List.Image(new DrawImage(picture, x, y, w, h, 0f, 0f, 1f, 1f, (tint ?? Tint.White).Rgba));
    }

    /// <summary>A flat rectangle in the UI's space — for outlines and placeholders, not for the game's own art.</summary>
    public void Box(float x, float y, float w, float h, Tint tint) =>
        List.Image(new DrawImage(0, x, y, w, h, 0f, 0f, 1f, 1f, tint.Rgba));

    /// <summary>One line in one of the game's own fonts, the pen on the baseline at (x, y). Returns where the next character would start.</summary>
    public float Text(NfdFont font, float x, float y, ReadOnlySpan<char> text, Tint tint)
    {
        List.Glyphs(Font(font), x, y, text, tint.Rgba);
        return Advance(font, x, text);
    }

    /// <summary><see cref="Text"/>, every glyph cut to a rectangle of the space (the blit clips at the surface's edges).</summary>
    public float TextClipped(NfdFont font, float x, float y, ReadOnlySpan<char> text, Tint tint,
                             float left, float bottom, float right, float top)
    {
        List.Glyphs(Font(font), x, y, text, tint.Rgba, new DrawCut(left, bottom, right, top));
        return Advance(font, x, text);
    }

    static float Advance(NfdFont font, float x, ReadOnlySpan<char> text)
    {
        foreach (char c in text)
        {
            if (NfdFont.Skipped(c)) continue;    // neither drawn nor advanced over
            x += font[c].Advance;
        }
        return x;
    }

    /// <summary>
    /// **A line of our own lettering** (ours): its baseline's left end at (x, y) in the sheet's units, an
    /// em of <paramref name="size"/> units, filled and dressed; <paramref name="tracking"/> spaces the letters by that share of
    /// the em. Returns where the next letter would start.
    /// </summary>
    public float Letters(Lettering face, float x, float y, float size, ReadOnlySpan<char> text, Tint fill,
                         LetterStyle style, float tracking = 0f)
    {
        List.Letters(Face(face), x, y, size, text, fill.Rgba, style.Look, tracking);
        return Advance(face, x, size, text, tracking);
    }

    /// <summary><see cref="Letters"/> cut at a box (ours), its glow and shadow with it.</summary>
    public float LettersClipped(Lettering face, float x, float y, float size, ReadOnlySpan<char> text, Tint fill,
                                LetterStyle style, float left, float bottom, float right, float top, float tracking = 0f)
    {
        List.Letters(Face(face), x, y, size, text, fill.Rgba, style.Look, tracking, new DrawCut(left, bottom, right, top));
        return Advance(face, x, size, text, tracking);
    }

    static float Advance(Lettering face, float x, float size, ReadOnlySpan<char> text, float tracking)
    {
        bool first = true;
        foreach (char c in text)
        {
            if (face[c] is not { } g) continue;
            if (!first) x += tracking * size;
            first = false;
            x += g.Advance * size;
        }
        return x;
    }

    /// <summary>How wide <paramref name="text"/> comes out in the UI's own pixels.</summary>
    public static int Measure(NfdFont font, ReadOnlySpan<char> text) => font.Measure(text);

    /// <summary>
    /// A 3D model drawn into the picture (<see cref="DrawKind.Model"/>): <paramref name="scene"/>'s things through their own
    /// transforms into <paramref name="viewport"/> (pixels, y down); with no draws, the scene's fill taken in only.
    /// </summary>
    public void Model(ClassicScene scene, PixelRect viewport, ReadOnlySpan<ModelDraw> draws) => List.Model(Scene(scene), viewport, draws);

    /// <summary>An id for a picture sent and repainted by its owner (a film's frame): <see cref="Repaint"/>, <see cref="Forget"/>.</summary>
    public int NewPicture() => nextPicture++;

    /// <summary>The streamed picture's new pixels (RGBA, top row first), copied.</summary>
    public void Repaint(int picture, int width, int height, ReadOnlySpan<byte> rgba) =>
        List.Resources.Picture(new DrawPicture(picture, width, height, rgba.ToArray()));

    /// <summary>The streamed picture let go.</summary>
    public void Forget(int picture) => List.Resources.Picture(new DrawPicture(picture, 0, 0, null));

    /// <summary>
    /// A named texture's id, its picture sent the first time: a finer picture of it when a data folder has one (ours:
    /// <see cref="UiAssets.Finer"/>, mipmapped; the layout stays the original's), else the install's; −1 when neither has it.
    /// </summary>
    int Picture(string name)
    {
        if (pictures.TryGetValue(name, out int id)) return id;
        var finer = assets.Finer(name);
        for (; finerRefusedSaid < assets.FinerRefused.Count; finerRefusedSaid++)
            Console.WriteLine($"interface art: {assets.FinerRefused[finerRefusedSaid]}");
        var image = finer?.Image ?? assets.Texture(name);
        if (image is null) id = -1;
        else
        {
            id = nextPicture++;
            List.Resources.Picture(new DrawPicture(id, image.Width, image.Height, image.Rgba, Mipmaps: finer is not null));
        }
        pictures[name] = id;
        return id;
    }

    int Font(NfdFont font)
    {
        if (fonts.TryGetValue(font, out int id)) return id;
        fonts[font] = id = nextFont++;
        // The strip with 1.1's ramp taken, flipped to top row first on the way (it is kept bottom row first, as every
        // texture in this engine): what the old layer made of it for the GPU.
        var rgba = new byte[font.StripWidth * font.StripHeight * 4];
        for (int y = 0; y < font.StripHeight; y++)
            for (int x = 0; x < font.StripWidth; x++)
            {
                int at = ((font.StripHeight - 1 - y) * font.StripWidth + x) * 4;
                var (grey, alpha) = NfdFont.Texel(font.At(x, y));
                rgba[at] = rgba[at + 1] = rgba[at + 2] = grey;
                rgba[at + 3] = alpha;
            }
        var glyphs = new BitmapGlyph[font.Glyphs.Length];
        for (int i = 0; i < glyphs.Length; i++)
        {
            var g = font.Glyphs[i];
            glyphs[i] = new BitmapGlyph(g.XOffset, g.YOffset, g.Width, g.Height, g.Advance, g.PixelX);
        }
        List.Resources.Font(new DrawBitmapFont { Id = id, StripWidth = font.StripWidth, StripHeight = font.StripHeight, Rgba = rgba, Glyphs = glyphs });
        return id;
    }

    int Face(Lettering face)
    {
        if (faces.TryGetValue(face, out int id)) return id;
        faces[face] = id = nextFace++;
        var glyphs = new Dictionary<int, LetterGlyph>();
        foreach (char c in face.Characters)
            if (face[c] is { } g) glyphs[c] = new LetterGlyph(g.Advance, g.Left, g.Bottom, g.Right, g.Top, g.X, g.Y, g.W, g.H);
        List.Resources.Face(new DrawFace
        {
            Id = id, Em = face.Em, Spread = face.Spread, AtlasWidth = face.AtlasWidth, AtlasHeight = face.AtlasHeight,
            AtlasPng = face.AtlasPng, Glyphs = glyphs,
        });
        return id;
    }

    /// <summary>A snapshot scene's id, the scene sent the first time (it is refilled in place each frame by its pass).</summary>
    public int Scene(ClassicScene scene)
    {
        if (scenes.TryGetValue(scene, out int id)) return id;
        scenes[scene] = id = nextScene++;
        List.Resources.Scene(id, scene);
        return id;
    }
}

/// <summary>
/// **An interface pass**: what it draws this frame emitted into its canvas's list from the engine's pane state. The old
/// program's glue draws the list after each pass (Vitomir.Render's <c>ListPass</c>), in the passes' order.
/// </summary>
public interface IUiPass
{
    /// <summary>The canvas it emits into.</summary>
    UiCanvas Ui { get; }

    void Emit(int width, int height, FreeCamera camera, Simulation sim);
}

/// <summary>Several passes into one canvas, in order (the old renderer's <c>Passes</c>, for the interface's).</summary>
public sealed class UiPasses(params IUiPass[] passes) : IUiPass
{
    public IReadOnlyList<IUiPass> All { get; } = passes;

    public UiCanvas Ui => All[0].Ui;

    public void Emit(int width, int height, FreeCamera camera, Simulation sim)
    {
        foreach (var pass in All) pass.Emit(width, height, camera, sim);
    }
}

/// <summary>
/// A pass emitted only while <paramref name="when"/> holds (ours) — so one pass can sit at two places in the order and be
/// drawn at one of them (the description boxes over or under the Storyteller's panes).
/// </summary>
public sealed class UiWhen(IUiPass pass, Func<bool> when) : IUiPass
{
    public UiCanvas Ui => pass.Ui;

    public void Emit(int width, int height, FreeCamera camera, Simulation sim)
    {
        if (when()) pass.Emit(width, height, camera, sim);
    }
}
