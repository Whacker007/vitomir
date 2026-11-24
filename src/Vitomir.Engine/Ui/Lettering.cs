// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Reflection;
using System.Text.Json;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **A face of our own lettering** (ours): the measures of one of Vitomir's own faces and its
/// distance-field atlas, both made by `tools/ui/font/` and shipped inside the engine (`Fonts/&lt;name&gt;.json` and
/// `.png`). 1.1's own fonts are bitmap strips (<c>NfdFont</c>); this is drawn from distances, so one atlas serves every
/// size and the renderer can give it an outline, a shadow or a glow.
///
/// Sizes here are an **em**: a glyph's box and advance are fractions of it, so the caller says how many of its own
/// units an em is (sheet units on 1.1's screens) and everything scales with it.
/// </summary>
public sealed class Lettering
{
    /// <summary>
    /// One glyph: how far the pen moves, its box (ems, y up from the baseline: the letter and the room its distances
    /// run into), its place in the atlas, and the letter's own ink, bottom and top (ems), which is what centres a
    /// word in a box.
    /// </summary>
    public readonly record struct Glyph(float Advance, float Left, float Bottom, float Right, float Top, int X, int Y, int W, int H,
                                        float InkBottom = 0f, float InkTop = 0f)
    {
        /// <summary>Whether it has a picture at all (a space has only its advance).</summary>
        public bool Drawn => W > 0 && H > 0;
    }

    readonly Dictionary<int, Glyph> glyphs;

    public string Name { get; }
    public string Family { get; }
    /// <summary>An em in the atlas's pixels, and how far the distance runs each side of the outline.</summary>
    public int Em { get; }
    public int Spread { get; }
    public int AtlasWidth { get; }
    public int AtlasHeight { get; }
    /// <summary>The face's ascender and descender, in ems.</summary>
    public float Ascender { get; }
    public float Descender { get; }
    /// <summary>The atlas as shipped: a greyscale PNG, 128 on the outline (the renderer decodes and uploads it).</summary>
    public byte[] AtlasPng { get; }

    Lettering(string name, JsonElement meta, byte[] png)
    {
        Name = name;
        Family = meta.GetProperty("family").GetString() ?? name;
        Em = meta.GetProperty("em").GetInt32();
        Spread = meta.GetProperty("spread").GetInt32();
        AtlasWidth = meta.GetProperty("atlasWidth").GetInt32();
        AtlasHeight = meta.GetProperty("atlasHeight").GetInt32();
        Ascender = meta.GetProperty("ascender").GetSingle();
        Descender = meta.GetProperty("descender").GetSingle();
        AtlasPng = png;
        glyphs = [];
        foreach (var g in meta.GetProperty("glyphs").EnumerateObject())
        {
            int code = int.Parse(g.Name, System.Globalization.CultureInfo.InvariantCulture);
            float advance = g.Value.GetProperty("advance").GetSingle();
            if (g.Value.TryGetProperty("box", out var box) && g.Value.TryGetProperty("atlas", out var at))
            {
                bool inked = g.Value.TryGetProperty("ink", out var ink);
                glyphs[code] = new Glyph(advance, box[0].GetSingle(), box[1].GetSingle(), box[2].GetSingle(), box[3].GetSingle(),
                                         at[0].GetInt32(), at[1].GetInt32(), at[2].GetInt32(), at[3].GetInt32(),
                                         inked ? ink[1].GetSingle() : box[1].GetSingle(), inked ? ink[3].GetSingle() : box[3].GetSingle());
            }
            else
                glyphs[code] = new Glyph(advance, 0, 0, 0, 0, 0, 0, 0, 0);
        }
    }

    /// <summary>A character's glyph; one the face does not have is null (and drawn as nothing).</summary>
    public Glyph? this[char c] => glyphs.TryGetValue(c, out var g) ? g : null;

    /// <summary>The characters the face has.</summary>
    public IEnumerable<char> Characters => glyphs.Keys.Select(k => (char)k);

    /// <summary>
    /// How wide <paramref name="text"/> is at an em of <paramref name="size"/>, with <paramref name="tracking"/> (a
    /// fraction of the em) between letters: the sum of the advances, less the tracking after the last.
    /// </summary>
    public float Measure(ReadOnlySpan<char> text, float size, float tracking = 0f)
    {
        float w = 0f;
        int n = 0;
        foreach (char c in text)
        {
            if (this[c] is { } g) { w += g.Advance; n++; }
        }
        return (w + tracking * Math.Max(0, n - 1)) * size;
    }

    /// <summary>
    /// How far <paramref name="text"/>'s ink reaches below its baseline and above it at an em of <paramref name="size"/>
    /// (the lowest bottom and the highest top of its letters, negative below): zero for a line with nothing drawn.
    /// </summary>
    public (float Bottom, float Top) Ink(ReadOnlySpan<char> text, float size)
    {
        float bottom = float.MaxValue, top = float.MinValue;
        foreach (char c in text)
            if (this[c] is { Drawn: true } g)
            {
                bottom = Math.Min(bottom, g.InkBottom);
                top = Math.Max(top, g.InkTop);
            }
        return bottom == float.MaxValue ? (0f, 0f) : (bottom * size, top * size);
    }

    static readonly Dictionary<string, Lettering> Loaded = [];

    /// <summary>A face shipped in the engine (<c>Fonts/&lt;name&gt;.json</c> and <c>.png</c>), read once.</summary>
    public static Lettering Load(string name)
    {
        lock (Loaded)
        {
            if (Loaded.TryGetValue(name, out var face)) return face;
            var assembly = typeof(Lettering).Assembly;
            byte[] json = Read(assembly, $"Fonts/{name}.json");
            byte[] png = Read(assembly, $"Fonts/{name}.png");
            using var doc = JsonDocument.Parse(json);
            face = new Lettering(name, doc.RootElement.Clone(), png);
            Loaded[name] = face;
            return face;
        }
    }

    static byte[] Read(Assembly assembly, string resource)
    {
        using var s = assembly.GetManifestResourceStream(resource)
                      ?? throw new FileNotFoundException($"the engine has no {resource} (tools/ui/font/atlas.py makes it)");
        using var m = new MemoryStream();
        s.CopyTo(m);
        return m.ToArray();
    }

    /// <summary>
    /// The faces shipped: the display capitals; the lower case in 1.1's own style for its screens (with its figures, marks
    /// and accented letters); and the book face, our text face, for sentences.
    /// </summary>
    public const string Capitals = "capitals", Uncial = "uncial", Book = "book";
}
