// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Platform.Interface;

/// <summary>
/// How a run of letters is dressed (the outline, the dark shadow, the glow of the chosen
/// item are a style the renderer draws from the distance field, not painted pictures). Widths and offsets are in
/// ems of the run, so a style looks the same at every size; a colour with alpha 0 is none.
/// </summary>
public readonly record struct TextLook(Rgba Outline, float OutlineEm, Rgba Shadow, Vector2 ShadowEm, float ShadowSoftEm, Rgba Glow, float GlowEm)
{
    /// <summary>No outline, no shadow, no glow.</summary>
    public static readonly TextLook Plain = new(default, 0f, default, Vector2.Zero, 0f, default, 0f);
}

/// <summary>What one entry of a <see cref="DrawList"/> is.</summary>
public enum DrawKind : byte
{
    /// <summary>A solid rectangle (<see cref="DrawCommand.Rect"/>, <see cref="DrawCommand.Colour"/>).</summary>
    Solid = 0,
    /// <summary>A run of letters in one face: <see cref="DrawCommand.Text"/> with its baseline's left end at <see cref="DrawCommand.Pen"/>, <see cref="DrawCommand.Size"/> pixels to the em.</summary>
    Text = 1,
    /// <summary>From here on, nothing outside <see cref="DrawCommand.Rect"/> (the clip in force, intersected with the one before).</summary>
    PushClip = 2,
    /// <summary>The clip before the last <see cref="PushClip"/> again.</summary>
    PopClip = 3,
    /// <summary>
    /// From here on, the 2D space the kinds below are placed in (<see cref="DrawList.Spaces"/>[<see cref="DrawCommand.Item"/>]):
    /// 1.1's 640x480 sheet, y up, stretched or fitted to the picture.
    /// </summary>
    Space = 4,
    /// <summary>A picture by id, a part of it onto a quad of the space, tinted (<see cref="DrawList.Images"/>[<see cref="DrawCommand.Item"/>]).</summary>
    Image = 5,
    /// <summary>A run of 1.1's bitmap (NFD) letters by font id, perhaps cut at a box (<see cref="DrawList.GlyphRuns"/>[<see cref="DrawCommand.Item"/>]).</summary>
    Glyphs = 6,
    /// <summary>A run of our distance-field lettering in the space, by face id (<see cref="DrawList.LetterRuns"/>[<see cref="DrawCommand.Item"/>]).</summary>
    Letters = 7,
    /// <summary>A 3D model drawn into a viewport of the picture, from a snapshot scene by id (<see cref="DrawList.Models"/>[<see cref="DrawCommand.Item"/>]).</summary>
    Model = 8,
    /// <summary>
    /// A picture by id (sent once through <see cref="DrawList.Resources"/>), a part of it onto a rectangle of the picture's pixels,
    /// y down as the rest of a screen's kinds, tinted (<see cref="DrawList.Pictures"/>[<see cref="DrawCommand.Item"/>]): a look's
    /// art (the launcher's kit), cut by the clip in force.
    /// </summary>
    Picture = 9,
}

/// <summary>
/// A picture onto a rectangle of the picture's pixels (<see cref="DrawKind.Picture"/>): its top left (<see cref="X"/>, <see cref="Y"/>,
/// y down) and size, the part of the picture as texture coordinates (v = 0 at its top), and the tint it is multiplied by.
/// </summary>
public readonly record struct PictureQuad(int Picture, float X, float Y, float W, float H, float U0, float V0, float U1, float V1, Rgba Tint);

/// <summary>
/// One entry of a <see cref="DrawList"/>, in the picture's pixels. A text run's alignment is already resolved into its
/// pen (where its baseline starts), so the renderer only places each glyph at <c>pen + box × size</c>, its advance
/// times the size apart; <see cref="Align"/> says how it was set, for tests and for a later right-to-left script.
/// The kinds 1.1's interface adds (<see cref="DrawKind.Space"/> on) keep their fields in the list's own tables, and
/// <see cref="Item"/> is the entry's place in its kind's table.
/// </summary>
public readonly record struct DrawCommand(DrawKind Kind, PixelRect Rect, Rgba Colour, string? Text, FaceId Face, float Size,
                                          Vector2 Pen, TextAlign Align, TextLook Look, int Item = 0);

/// <summary>Where a line sits in its box across.</summary>
public enum TextAlign : byte { Start = 0, Centre = 1, End = 2 }

/// <summary>
/// What a screen leaves to draw, in order, back to front: solid rectangles, runs of letters, and clip rectangles
/// for the scrolling areas (the renderer turns them into scissor rectangles). Reused frame to frame.
/// <para>**1.1's interface draws through it too**: pictures by id,
/// 1.1's bitmap letters and our distance-field lettering in a 2D space of its own, and 3D model inserts; their pictures,
/// fonts, faces and scenes go once through <see cref="Resources"/> (DrawList.Classic.cs).</para>
/// </summary>
public sealed partial class DrawList
{
    readonly List<DrawCommand> commands = [];

    /// <summary>The picture's size the list was made for, in pixels.</summary>
    public int Width { get; private set; }
    public int Height { get; private set; }

    public IReadOnlyList<DrawCommand> Commands => commands;
    public int Count => commands.Count;

    public void Begin(int width, int height)
    {
        Clear();
        (Width, Height) = (width, height);
    }

    /// <summary>The picture's size, the list kept (an emitter's own start).</summary>
    public void SizeTo(int width, int height) => (Width, Height) = (width, height);

    /// <summary>Empties the list (its size kept); what <see cref="Resources"/> holds unread stays.</summary>
    public void Clear()
    {
        commands.Clear();
        spaces.Clear();
        images.Clear();
        glyphRuns.Clear();
        letterRuns.Clear();
        models.Clear();
        modelDraws.Clear();
        chars.Clear();
        pictures.Clear();
    }

    public void Solid(PixelRect r, Rgba colour)
    {
        if (r.Empty || colour.A <= 0f) return;
        commands.Add(new DrawCommand(DrawKind.Solid, r, colour, null, FaceId.Book, 0f, default, TextAlign.Start, default));
    }

    public void Text(string text, FaceId face, float size, Vector2 pen, Rgba colour, TextLook look, TextAlign align = TextAlign.Start)
    {
        if (text.Length == 0 || size <= 0f) return;
        commands.Add(new DrawCommand(DrawKind.Text, default, colour, text, face, size, pen, align, look));
    }

    /// <summary>A part of a picture (sent by id through <see cref="Resources"/>) onto a rectangle of pixels, y down (<see cref="DrawKind.Picture"/>).</summary>
    public void Picture(in PictureQuad quad)
    {
        if (quad.W <= 0f || quad.H <= 0f || quad.Tint.A <= 0f) return;
        commands.Add(new DrawCommand(DrawKind.Picture, default, quad.Tint, null, FaceId.Book, 0f, default, TextAlign.Start, default, pictures.Count));
        pictures.Add(quad);
    }

    readonly List<PictureQuad> pictures = [];

    /// <summary>The pictures' quads (<see cref="DrawKind.Picture"/>).</summary>
    public IReadOnlyList<PictureQuad> Pictures => pictures;

    public void PushClip(PixelRect r) => commands.Add(new DrawCommand(DrawKind.PushClip, r, default, null, FaceId.Book, 0f, default, TextAlign.Start, default));

    public void PopClip() => commands.Add(new DrawCommand(DrawKind.PopClip, default, default, null, FaceId.Book, 0f, default, TextAlign.Start, default));

    /// <summary>Every text run's words, in order (for tests and logs).</summary>
    public IEnumerable<string> Texts()
    {
        foreach (var c in commands) if (c.Kind == DrawKind.Text) yield return c.Text!;
    }
}
