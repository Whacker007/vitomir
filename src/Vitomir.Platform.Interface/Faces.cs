// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours. The faces' files are tools/ui/font's; a name cut into the uncial's lower case and the
// capitals' capitals at 0.85 of the em is taken from src/Vitomir.Engine/Ui/MenuLettering.cs (Runs, CapitalScale)
// at b4185ef, reworked for the platform.
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Vitomir.Platform.Interface;

/// <summary>
/// Our faces: the book face for sentences and values, the uncial and its capitals for names (drawn by tools/ui/font); and
/// the faces delivered as TrueType files (assets/fonts/), whose atlases tools/ui/font builds from them, kerned: Vitomir Text
/// (Regular and SemiBold), Vitomir Wedge SemiBold and Vitomir Knife, the launcher's.
/// </summary>
public enum FaceId : byte
{
    Book = 0,
    Uncial = 1,
    Capitals = 2,
    Text = 3,
    TextSemiBold = 4,
    WedgeSemiBold = 5,
    Knife = 6,
}

/// <summary>
/// One glyph of a face: its advance and its quad's box in ems (Y up from the baseline), the box of its ink in
/// ems, and where it lies in the atlas, in texels (zero for a glyph with no ink, a space).
/// </summary>
public readonly record struct Glyph(float Advance, float X0, float Y0, float X1, float Y1, float InkBottom, float InkTop,
                                    int AtlasX, int AtlasY, int AtlasW, int AtlasH)
{
    public bool HasInk => AtlasW > 0 && AtlasH > 0;
}

/// <summary>
/// A distance-field face's measures, as tools/ui/font writes them (<c>&lt;name&gt;.json</c>): every glyph's
/// advance and box, and its place in the atlas — which stays with the renderer (the picture is not read here).
/// </summary>
public sealed class Face
{
    public required FaceId Id { get; init; }
    public required string Family { get; init; }
    public required int AtlasWidth { get; init; }
    public required int AtlasHeight { get; init; }
    /// <summary>The atlas's texels to an em, and the distance field's reach in texels.</summary>
    public required float Em { get; init; }
    public required float Spread { get; init; }
    public required float Ascender { get; init; }
    public required float Descender { get; init; }
    readonly Dictionary<int, Glyph> glyphs = [];

    Face(Dictionary<long, float>? pairs) { this.pairs = pairs; }

    /// <summary>The glyph for <paramref name="codePoint"/>, or the question mark's when the face has none.</summary>
    public bool TryGlyph(int codePoint, out Glyph g) => glyphs.TryGetValue(codePoint, out g) || glyphs.TryGetValue('?', out g);

    public bool Has(int codePoint) => glyphs.ContainsKey(codePoint);

    /// <summary>
    /// The face's pair kerning (the atlas's <c>kern</c>, from the font's GPOS <c>kern</c> feature): how far, in ems, the second
    /// character of a pair moves when it follows the first (negative: closer). Null for a face without pairs (our own
    /// code-drawn faces), whose lines are then set exactly as before.
    /// </summary>
    readonly Dictionary<long, float>? pairs;

    /// <summary>Whether the face kerns its pairs.</summary>
    public bool HasPairs => pairs is not null;

    /// <summary>How far <paramref name="second"/> moves after <paramref name="first"/>, in ems (0 when the pair has no kerning).</summary>
    public float Kern(int first, int second) => pairs is not null && pairs.TryGetValue(PairKey(first, second), out var v) ? v : 0f;

    static long PairKey(int first, int second) => ((long)first << 32) | (uint)second;

    /// <summary>A line's width in ems (its pairs kerned, when the face has them).</summary>
    public float Measure(string text)
    {
        float x = 0;
        if (pairs is null)
        {
            foreach (var r in text.EnumerateRunes())
                if (TryGlyph(r.Value, out var g)) x += g.Advance;
            return x;
        }
        int before = -1;
        foreach (var r in text.EnumerateRunes())
        {
            if (!TryGlyph(r.Value, out var g)) continue;
            if (before >= 0) x += Kern(before, r.Value);
            x += g.Advance;
            before = r.Value;
        }
        return x;
    }

    /// <summary>
    /// **A line's glyphs placed**: each glyph of <paramref name="text"/> with the x of its pen, the line's pen starting at
    /// <paramref name="x"/> and moving by each glyph's advance at <paramref name="size"/> units (or pixels) to the em, and by a
    /// pair's kerning when the face has pairs (between two glyphs, before the second). What every renderer of our lettering
    /// draws from, so a line is drawn where it was measured.
    /// </summary>
    public void Place(string text, float x, float size, List<PlacedGlyph> into)
    {
        int before = -1;
        foreach (var r in text.EnumerateRunes())
        {
            if (!TryGlyph(r.Value, out var g)) continue;
            if (pairs is not null)
            {
                if (before >= 0) x += Kern(before, r.Value) * size;
                before = r.Value;
            }
            into.Add(new PlacedGlyph(g, x));
            x += g.Advance * size;
        }
    }

    /// <summary>How far a line's ink reaches below and above the baseline, in ems ((0, 0) for no ink).</summary>
    public (float Bottom, float Top) Ink(string text)
    {
        float bottom = float.MaxValue, top = float.MinValue;
        foreach (var r in text.EnumerateRunes())
            if (TryGlyph(r.Value, out var g) && g.HasInk)
            {
                bottom = MathF.Min(bottom, g.InkBottom);
                top = MathF.Max(top, g.InkTop);
            }
        return bottom == float.MaxValue ? (0f, 0f) : (bottom, top);
    }

    static readonly Dictionary<FaceId, Face> Loaded = [];
    static readonly Lock Gate = new();

    /// <summary>One of our faces, read once from this assembly's resources.</summary>
    public static Face Get(FaceId id)
    {
        lock (Gate)
        {
            if (Loaded.TryGetValue(id, out var f)) return f;
            f = Load(id);
            Loaded[id] = f;
            return f;
        }
    }

    /// <summary>The resource name of a face (<c>book</c>, <c>uncial</c>, <c>capitals</c>, <c>text</c> …), which the renderer's atlases share.</summary>
    public static string FileName(FaceId id) => id switch
    {
        FaceId.Uncial => "uncial",
        FaceId.Capitals => "capitals",
        FaceId.Text => "text",
        FaceId.TextSemiBold => "text-semibold",
        FaceId.WedgeSemiBold => "wedge-semibold",
        FaceId.Knife => "knife",
        _ => "book",
    };

    /// <summary>How many faces there are (the renderer's tables of atlases are this long).</summary>
    public const int Count = 7;

    /// <summary>
    /// Where a line's middle is, in ems above its baseline: half the height of the face's capital H's ink (a box's words are
    /// set with this much room over their capitals as under their baseline), or a third of an em for a face without an H.
    /// </summary>
    public float CapMiddle => capMiddle ??= glyphs.TryGetValue('H', out var h) && h.HasInk ? h.InkTop * 0.5f : 0.33f;
    float? capMiddle;

    static Face Load(FaceId id)
    {
        var name = FileName(id);
        using var js = typeof(Face).Assembly.GetManifestResourceStream($"Fonts/{name}.json")
                       ?? throw new FileNotFoundException($"embedded face Fonts/{name}.json missing");
        return Read(id, js, name);
    }

    /// <summary>A face from its measures' JSON (an atlas's <c>&lt;name&gt;.json</c>, as tools/ui/font writes it).</summary>
    public static Face Read(FaceId id, Stream json, string name = "")
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Dictionary<long, float>? pairs = null;
        if (root.TryGetProperty("kern", out var kern) && kern.GetArrayLength() > 0)
        {
            pairs = [];
            foreach (var k in kern.EnumerateArray()) pairs[PairKey(k[0].GetInt32(), k[1].GetInt32())] = k[2].GetSingle();
        }
        var face = new Face(pairs)
        {
            Id = id,
            Family = root.GetProperty("family").GetString() ?? name,
            AtlasWidth = root.GetProperty("atlasWidth").GetInt32(),
            AtlasHeight = root.GetProperty("atlasHeight").GetInt32(),
            Em = root.GetProperty("em").GetSingle(),
            Spread = root.GetProperty("spread").GetSingle(),
            Ascender = root.GetProperty("ascender").GetSingle(),
            Descender = root.GetProperty("descender").GetSingle(),
        };
        foreach (var g in root.GetProperty("glyphs").EnumerateObject())
        {
            var v = g.Value;
            float advance = v.GetProperty("advance").GetSingle();
            int code = int.Parse(g.Name, System.Globalization.CultureInfo.InvariantCulture);
            // A glyph with no ink (a space) has no box and no place in the atlas: only its advance.
            if (!v.TryGetProperty("box", out var box) || !v.TryGetProperty("atlas", out var at))
            {
                face.glyphs[code] = new Glyph(advance, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                continue;
            }
            float inkBottom = 0, inkTop = 0;
            if (v.TryGetProperty("ink", out var ink)) { inkBottom = ink[1].GetSingle(); inkTop = ink[3].GetSingle(); }
            face.glyphs[code] = new Glyph(advance, box[0].GetSingle(), box[1].GetSingle(), box[2].GetSingle(), box[3].GetSingle(),
                                          inkBottom, inkTop, at[0].GetInt32(), at[1].GetInt32(), at[2].GetInt32(), at[3].GetInt32());
        }
        return face;
    }
}

/// <summary>
/// Which kind of words a text is: a name in the uncial, or a sentence or a value in the book face; or words set whole in
/// one of the delivered faces (the launcher's).
/// </summary>
public enum Lettering : byte
{
    /// <summary>A heading, a button, a label, an action's name: Vitomir Uncial, a capital in Vitomir Capitals.</summary>
    Name = 0,
    /// <summary>A sentence or a value (a key's name, a number): Vitomir Book.</summary>
    Sentence = 1,
    /// <summary>Vitomir Text Regular, whole: running words, values, notes.</summary>
    Text = 2,
    /// <summary>Vitomir Text SemiBold, whole: a warning.</summary>
    TextSemiBold = 3,
    /// <summary>Vitomir Wedge SemiBold, whole: a title, a label, a tab, a button.</summary>
    Wedge = 4,
    /// <summary>Vitomir Knife, whole: a number.</summary>
    Knife = 5,
}

/// <summary>A glyph of a line, and the x of its pen (<see cref="Face.Place"/>).</summary>
public readonly record struct PlacedGlyph(Glyph Glyph, float X);

/// <summary>A run of a line in one face, at its own em (a capital in a name is smaller than the line's em).</summary>
public readonly record struct FaceRun(string Text, FaceId Face, float EmScale);

/// <summary>Lines of our lettering cut into the runs each face draws, measured.</summary>
public static class Letters
{
    /// <summary>A capital's em in a name, as a share of the line's (so it sits a little over the uncial's x-height).</summary>
    public const float CapitalScale = 0.85f;

    /// <summary>The runs of <paramref name="text"/> in <paramref name="kind"/>: a sentence whole; a name cut at each change between capitals and the rest.</summary>
    public static void Runs(Lettering kind, string text, List<FaceRun> into)
    {
        if (text.Length == 0) return;
        if (kind == Lettering.Sentence)
        {
            into.Add(new FaceRun(text, FaceId.Book, 1f));
            return;
        }
        if (Whole(kind) is { } face)
        {
            into.Add(new FaceRun(text, face, 1f));
            return;
        }
        var uncial = Face.Get(FaceId.Uncial);
        var spelled = Spelled(text, uncial);
        int start = 0;
        for (int i = 1; i <= spelled.Length; i++)
        {
            if (i < spelled.Length && char.IsUpper(spelled[i]) == char.IsUpper(spelled[start])) continue;
            bool caps = char.IsUpper(spelled[start]);
            into.Add(new FaceRun(spelled[start..i], caps ? FaceId.Capitals : FaceId.Uncial, caps ? CapitalScale : 1f));
            start = i;
        }
    }

    /// <summary>A line's width in units at an em of <paramref name="em"/> units.</summary>
    public static float Measure(Lettering kind, string text, float em)
    {
        var runs = Scratch();
        Runs(kind, text, runs);
        float w = 0;
        foreach (var r in runs) w += Face.Get(r.Face).Measure(r.Text) * em * r.EmScale;
        return w;
    }

    /// <summary>How far a line's ink reaches below (negative) and above the baseline, in units at an em of <paramref name="em"/>.</summary>
    public static (float Bottom, float Top) Ink(Lettering kind, string text, float em)
    {
        var runs = Scratch();
        Runs(kind, text, runs);
        float bottom = float.MaxValue, top = float.MinValue;
        foreach (var r in runs)
        {
            var (b, t) = Face.Get(r.Face).Ink(r.Text);
            if (b == 0f && t == 0f) continue;
            bottom = MathF.Min(bottom, b * em * r.EmScale);
            top = MathF.Max(top, t * em * r.EmScale);
        }
        return bottom == float.MaxValue ? (0f, 0f) : (bottom, top);
    }

    /// <summary>
    /// The baseline that sets a line in the middle of a box whose middle is at <paramref name="middle"/>: the middle
    /// of the face's lower case and capitals (not of this line's own ink, so a row of names stays level whatever
    /// letters each has), as much room over the letters as under them.
    /// </summary>
    public static float Baseline(Lettering kind, float em, float middle) =>
        middle + em * (kind == Lettering.Name ? NameMiddle : Whole(kind) is { } face ? Face.Get(face).CapMiddle : SentenceMiddle);

    /// <summary>The face a lettering sets its words in whole (the delivered faces'), or null for the book face's and the uncial's.</summary>
    public static FaceId? Whole(Lettering kind) => kind switch
    {
        Lettering.Text => FaceId.Text,
        Lettering.TextSemiBold => FaceId.TextSemiBold,
        Lettering.Wedge => FaceId.WedgeSemiBold,
        Lettering.Knife => FaceId.Knife,
        _ => null,
    };

    /// <summary>Where a line's middle is, in ems above its baseline: half its capital's height (the uncial's ascenders and the capitals at 0.85 reach about 0.6).</summary>
    public const float NameMiddle = 0.29f, SentenceMiddle = 0.32f;

    /// <summary>
    /// The words of <paramref name="text"/> broken into lines no wider than <paramref name="width"/> units (at spaces). A word longer than a
    /// line stands alone, except a path: one with <c>/</c> or <c>\</c> breaks after them, so a long folder's path is not cut at the edge.
    /// </summary>
    public static List<string> Wrap(Lettering kind, string text, float em, float width)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            var line = new StringBuilder();
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = word.IndexOfAny(['/', '\\']) >= 0 && Measure(kind, word, em) > width ? AfterSeparators(word) : [word];
                for (int k = 0; k < parts.Count; k++)
                {
                    string tried = line.Length == 0 ? parts[k] : line + (k == 0 ? " " : "") + parts[k];
                    if (line.Length > 0 && Measure(kind, tried, em) > width)
                    {
                        lines.Add(line.ToString());
                        line.Clear().Append(parts[k]);
                    }
                    else line.Clear().Append(tried);
                }
            }
            lines.Add(line.ToString());
        }
        return lines;
    }

    /// <summary>A path cut after each <c>/</c> and <c>\</c>: <c>Games\</c>, <c>Vitomir\</c>, <c>Saves</c>.</summary>
    static List<string> AfterSeparators(string word)
    {
        var parts = new List<string>();
        int from = 0;
        for (int i = 0; i < word.Length; i++)
            if (word[i] is '/' or '\\') { parts.Add(word[from..(i + 1)]); from = i + 1; }
        if (from < word.Length) parts.Add(word[from..]);
        return parts;
    }

    /// <summary>Letters the uncial does not draw as one, written as their plain letters (a capital stays for the capitals face).</summary>
    static string Spelled(string text, Face uncial)
    {
        StringBuilder? sb = null;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            string? instead = c switch { 'Æ' => "AE", 'Œ' => "OE", 'ß' => "ss", 'æ' => "ae", 'œ' => "oe", _ => null };
            if (instead is null && !char.IsUpper(c) && !uncial.Has(c) && c != ' ') instead = "?";
            if (instead is null) { sb?.Append(c); continue; }
            sb ??= new StringBuilder(text, 0, i, text.Length + 4);
            sb.Append(instead);
        }
        return sb?.ToString() ?? text;
    }

    [ThreadStatic] static List<FaceRun>? scratch;
    static List<FaceRun> Scratch()
    {
        scratch ??= [];
        scratch.Clear();
        return scratch;
    }
}
