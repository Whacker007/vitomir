// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The kinds 1.1's interface adds to the draw list:
// pictures by id, 1.1's bitmap letters, our distance-field lettering, all in a 2D space of their own, and model inserts.
using System.Numerics;
using Vitomir.Platform.Presentation.Classic;

namespace Vitomir.Platform.Interface;

/// <summary>
/// **A 2D space**: a point (x, y) of it is the picture's pixel (<see cref="X"/> + x·<see cref="ScaleX"/>,
/// <see cref="Bottom"/> − y·<see cref="ScaleY"/>), so y runs up. 1.1's sheet is 640x480 stretched to the picture, fitted
/// in its own proportions (ours), or the 16:9 stretch (ours); the engine's <c>UiLayout</c> says which.
/// </summary>
public readonly record struct DrawSpace(float ScaleX, float ScaleY, float X, float Bottom);

/// <summary>How a picture is laid over what is under it.</summary>
public enum DrawBlend : byte
{
    /// <summary>Straight alpha: the colour times its alpha over the rest (1.1's interface, every picture and letter).</summary>
    Straight = 0,
}

/// <summary>
/// **A picture onto a quad**: its bottom left (<see cref="X"/>, <see cref="Y"/>) and size in the space, the part of the
/// picture as texture coordinates (v = 0 at its top), the tint it is multiplied by. <see cref="Picture"/> 0 is no picture:
/// the quad in the tint alone.
/// </summary>
public readonly record struct DrawImage(int Picture, float X, float Y, float W, float H, float U0, float V0, float U1, float V1,
                                        Rgba Tint, DrawBlend Blend = DrawBlend.Straight);

/// <summary>A box of the space a run is cut at: its left, bottom, right and top.</summary>
public readonly record struct DrawCut(float Left, float Bottom, float Right, float Top);

/// <summary>
/// **A run of 1.1's bitmap letters** (an NFD font's, by id): the pen on the baseline at (<see cref="X"/>, <see cref="Y"/>),
/// the letters <see cref="DrawList.Chars"/>(<see cref="First"/>, <see cref="Length"/>), each glyph at its own offset and
/// advance, tinted; cut at <see cref="Cut"/> when it has one, so a run is clipped at the surface's edges.
/// </summary>
public readonly record struct GlyphRun(int Font, float X, float Y, int First, int Length, Rgba Tint, DrawCut? Cut);

/// <summary>
/// **A run of our lettering** (a face's, by id): the baseline's left end at (<see cref="X"/>, <see cref="Y"/>), an em of
/// <see cref="Size"/> units, filled with <see cref="Fill"/> and dressed by <see cref="Look"/>; <see cref="Tracking"/> spaces
/// the letters by that share of the em; cut at <see cref="Cut"/> when it has one.
/// </summary>
public readonly record struct LetterRun(int Face, float X, float Y, float Size, int First, int Length, Rgba Fill, TextLook Look,
                                        float Tracking, DrawCut? Cut);

/// <summary>
/// **A 3D model drawn into the picture** (the party heads, the character screens' portrait): the snapshot scene by id, the
/// viewport and scissor in the picture's pixels (y down), and its draws (<see cref="DrawList.ModelDraws"/>, from
/// <see cref="FirstDraw"/>), each one thing of the scene alone through its own transform. The scene's depth is cleared first.
/// An insert with no draws only takes the scene's fill in (every fill is read, as a place's are).
/// </summary>
public readonly record struct ModelInsert(int Scene, PixelRect Viewport, int FirstDraw, int Draws);

/// <summary>One draw of a model insert: the thing's identity in the scene (its <see cref="ClassicThing.Identity"/>) and its transform (row vectors).</summary>
public readonly record struct ModelDraw(int Identity, Matrix4x4 Transform);

/// <summary>A picture sent by id: RGBA, top row first; mipmapped when it is drawn smaller than it was painted (ours).</summary>
public sealed record DrawPicture(int Id, int Width, int Height, byte[]? Rgba, bool Mipmaps = false)
{
    /// <summary>The picture's id let go (a film ended): <see cref="Rgba"/> null.</summary>
    public bool Gone => Rgba is null;
}

/// <summary>One glyph of a bitmap font: where its picture sits off the pen, its size, the pen's advance, its column in the strip (−1: none).</summary>
public readonly record struct BitmapGlyph(int XOffset, int YOffset, int Width, int Height, int Advance, int PixelX)
{
    public bool Present => PixelX >= 0;
}

/// <summary>
/// **One of 1.1's bitmap fonts sent by id** (an NFD font, Formats' <c>NfdFont</c>, as the engine read it): its strip as
/// RGBA, top row first, with 1.1's ramp already taken (the coverage as grey, opaque wherever it is not zero),
/// and its 256 glyphs by the character's low byte. Character 0xff is neither drawn nor advanced over.
/// </summary>
public sealed class DrawBitmapFont
{
    public required int Id { get; init; }
    public required int StripWidth { get; init; }
    public required int StripHeight { get; init; }
    public required byte[] Rgba { get; init; }
    public required BitmapGlyph[] Glyphs { get; init; }

    public BitmapGlyph this[char c] => Glyphs[(byte)c];

    /// <summary>True for character 0xff, which is neither drawn nor advanced over.</summary>
    public static bool Skipped(char c) => (byte)c == 0xff;
}

/// <summary>One letter of a face: its advance, its quad's box in ems (y up from the baseline) and its place in the atlas, texels.</summary>
public readonly record struct LetterGlyph(float Advance, float Left, float Bottom, float Right, float Top, int X, int Y, int W, int H)
{
    public bool Drawn => W > 0 && H > 0;
}

/// <summary>A distance-field face sent by id: its measures, its atlas as shipped (a greyscale PNG) and its letters.</summary>
public sealed class DrawFace
{
    public required int Id { get; init; }
    public required int Em { get; init; }
    public required int Spread { get; init; }
    public required int AtlasWidth { get; init; }
    public required int AtlasHeight { get; init; }
    public required byte[] AtlasPng { get; init; }
    public required IReadOnlyDictionary<int, LetterGlyph> Glyphs { get; init; }

    public LetterGlyph? this[char c] => Glyphs.TryGetValue(c, out var g) ? g : null;
}

/// <summary>
/// **What the list's entries name, sent once** (as the Classic snapshot's resource channel): pictures by id (and a
/// streamed one sent again each time it is repainted, or let go), 1.1's bitmap fonts by id, our faces by id, and the snapshot scenes model inserts draw. Whoever draws the list takes what is
/// new before it draws (<see cref="Take"/>); what nobody has taken stays.
/// </summary>
public sealed class DrawResources
{
    readonly List<object> pending = [];

    public void Picture(DrawPicture picture) => pending.Add(picture);
    public void Font(DrawBitmapFont font) => pending.Add(font);
    public void Face(DrawFace face) => pending.Add(face);
    public void Scene(int id, ClassicScene scene) => pending.Add((id, scene));

    /// <summary>How many are waiting to be taken.</summary>
    public int Pending => pending.Count;

    /// <summary>Everything sent since the last take, in the order it was sent: a <see cref="DrawPicture"/>, a <see cref="DrawBitmapFont"/>, a <see cref="DrawFace"/> or an (id, <see cref="ClassicScene"/>).</summary>
    public void Take(List<object> into)
    {
        into.AddRange(pending);
        pending.Clear();
    }
}

public sealed partial class DrawList
{
    readonly List<DrawSpace> spaces = [];
    readonly List<DrawImage> images = [];
    readonly List<GlyphRun> glyphRuns = [];
    readonly List<LetterRun> letterRuns = [];
    readonly List<ModelInsert> models = [];
    readonly List<ModelDraw> modelDraws = [];
    readonly List<char> chars = [];

    /// <summary>The pictures, fonts, faces and scenes the entries name, sent once (not emptied by <see cref="Clear"/>).</summary>
    public DrawResources Resources { get; } = new();

    public IReadOnlyList<DrawSpace> Spaces => spaces;
    public IReadOnlyList<DrawImage> Images => images;
    public IReadOnlyList<GlyphRun> GlyphRuns => glyphRuns;
    public IReadOnlyList<LetterRun> LetterRuns => letterRuns;
    public IReadOnlyList<ModelInsert> Models => models;
    public IReadOnlyList<ModelDraw> ModelDraws => modelDraws;

    /// <summary>A run's letters (<see cref="GlyphRun.First"/>, <see cref="GlyphRun.Length"/>).</summary>
    public ReadOnlySpan<char> Chars(int first, int length) => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(chars).Slice(first, length);

    /// <summary>The space the next pictures and runs are placed in.</summary>
    public void Space(DrawSpace space)
    {
        commands.Add(new DrawCommand(DrawKind.Space, default, default, null, FaceId.Book, 0f, default, TextAlign.Start, default, spaces.Count));
        spaces.Add(space);
    }

    public void Image(in DrawImage image)
    {
        commands.Add(new DrawCommand(DrawKind.Image, default, image.Tint, null, FaceId.Book, 0f, default, TextAlign.Start, default, images.Count));
        images.Add(image);
    }

    public void Glyphs(int font, float x, float y, ReadOnlySpan<char> text, Rgba tint, DrawCut? cut = null)
    {
        int first = Keep(text);
        commands.Add(new DrawCommand(DrawKind.Glyphs, default, tint, null, FaceId.Book, 0f, new Vector2(x, y), TextAlign.Start, default, glyphRuns.Count));
        glyphRuns.Add(new GlyphRun(font, x, y, first, text.Length, tint, cut));
    }

    public void Letters(int face, float x, float y, float size, ReadOnlySpan<char> text, Rgba fill, TextLook look, float tracking = 0f, DrawCut? cut = null)
    {
        int first = Keep(text);
        commands.Add(new DrawCommand(DrawKind.Letters, default, fill, null, FaceId.Book, size, new Vector2(x, y), TextAlign.Start, look, letterRuns.Count));
        letterRuns.Add(new LetterRun(face, x, y, size, first, text.Length, fill, look, tracking, cut));
    }

    /// <summary>A model insert: <paramref name="draws"/> through their transforms into <paramref name="viewport"/>.</summary>
    public void Model(int scene, PixelRect viewport, ReadOnlySpan<ModelDraw> draws)
    {
        commands.Add(new DrawCommand(DrawKind.Model, viewport, default, null, FaceId.Book, 0f, default, TextAlign.Start, default, models.Count));
        models.Add(new ModelInsert(scene, viewport, modelDraws.Count, draws.Length));
        foreach (var d in draws) modelDraws.Add(d);
    }

    int Keep(ReadOnlySpan<char> text)
    {
        int first = chars.Count;
        foreach (char c in text) chars.Add(c);
        return first;
    }
}
