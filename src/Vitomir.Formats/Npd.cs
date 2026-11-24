// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The particle descriptions: `misc/game.npd`, the only `.npd` in the game ("Nihilistic Particle
// Description"), read the way the game reads it at start-up.
//
// ```
// ; a comment — ';' as the very first character of the line
// fire            candleflame
// material        fireSprite
// lifespan        150
// startcolor      0xc0ff8000
// velocity        (0.0,400.0,-10.0)
// end
// ```
//
// The file is a list of blocks. A block opens with a line whose first word **starts with** one of
// the fourteen kind names (`strstr(word, kind) == word`, in the kind table's order)
// followed by the emitter's name; the kind's own parser then reads `key value` lines
// until `end` (keyword 1) or the end of the file. Every key is looked up case-insensitively in one
// table of 67 names (`invalid` first), and each kind's parser takes the keys it knows and
// **silently ignores the rest** — a `maxparticles` in a gas block is read and thrown away, because
// the gas parser has no case for it. So the reader keeps every line and the engine
// decides, per kind, what it means.
//
// Values are the first word after the key (`strtok` on " \r\t\n"): a whole number is `atoi`, a
// float `atof`, a colour `sscanf("%x")` (ARGB, `0xAARRGGBB`), and a vector
// `sscanf("(%f,%f,%f)")` — so a vector must not contain a space. The helpers below are those four
// conversions with the C library's leniency (a number stops at the first character that is not part
// of it, a failed conversion leaves 0 or the field untouched).
//
// Three limits are the loader's: a line is read with `fgets` into 0x200 bytes, **at most 256
// emitters** are kept (the prototype table's size; reading stops at the 257th), and **a name
// already taken is skipped** — the header line is ignored and its block's lines, which match no
// kind, fall through to the header loop and are ignored with it. The shipped file has 218 blocks and
// no repeated name, so neither limit bites. A name is kept to 31 characters (`strncpy` into 0x20
// bytes, byte 0x1f forced to 0) and looked up case-insensitively (the table folds
// case as it hashes and compares with `strnicmp`).
using System.Globalization;
using System.Text;

namespace Vitomir.Formats;

/// <summary>
/// The kinds of emitter, numbered as the engine numbers them: the index into the kind table,
/// which is also what the constructor stores as the emitter's kind and what `GetEmitterType`
/// answers. <see cref="None"/> is the table's first entry and never makes an emitter.
/// </summary>
public enum EmitterKind
{
    None = 0, Gas = 1, Fire = 2, Liquid = 3, Flame = 4, Trail = 5, Rain = 6, Sparks = 7, Shock = 8,
    Beam = 9, Magic = 10, Ring = 11, Pad = 12, Spurt = 13, Object = 14,
}

/// <summary>
/// The keywords, numbered as the keyword table numbers them — the number each kind's parser
/// switches on. <see cref="Invalid"/> is entry 0 and is never answered; <see cref="End"/> closes a
/// block.
/// </summary>
public enum NpdKey
{
    Invalid = 0, End, Subtype, Material, Lifespan, LifespanMin, LifespanMax, StartSize, MinStartSize,
    MaxStartSize, EndSize, MinEndSize, MaxEndSize, BirthRate, MinBirthRate, MaxBirthRate, MaxParticles,
    Count, Size, GrowthRate, MinGrowth, MaxGrowth, StartColor, StartColorMin, StartColorMax, EndColor,
    EndColorMin, EndColorMax, MinDist, MaxDist, Turbulence, Gravity, Drag, Velocity, VelocityVar,
    NumGroups, MinGroup, MaxGroup, Cohesion, GroupCohesion, RotVel, MinRotVel, MaxRotVel, Speed,
    MinSpeed, MaxSpeed, Radius, MinRadius, MaxRadius, Length, Width, UpdateRate, Delay, TextureRate,
    Frequency, StartHeight, EndHeight, NumSegs, EmitShape, EmitRadius, EmitHeight, EmitWidth, EmitDir,
    StartPos, EndPos, Flags, Template,
}

/// <summary>One <c>key value</c> line of a block, in file order.</summary>
/// <param name="Key">The keyword.</param>
/// <param name="Value">The first word after it, or null when the line had none (the parsers then skip it).</param>
/// <param name="Line">The 1-based line number, for messages.</param>
public readonly record struct NpdLine(NpdKey Key, string? Value, int Line);

/// <summary>
/// One emitter description: its kind, its name and its lines. What the lines mean depends on the
/// kind — see the engine's <c>Particles.EmitterSettings</c> — so they are kept as written.
/// </summary>
public sealed record NpdEmitter(EmitterKind Kind, string Name, IReadOnlyList<NpdLine> Lines)
{
    /// <summary>The last line with this key, which is the one that wins when a parser overwrites.</summary>
    public string? this[NpdKey key]
    {
        get
        {
            for (int i = Lines.Count - 1; i >= 0; i--)
                if (Lines[i].Key == key) return Lines[i].Value;
            return null;
        }
    }

    public bool Has(NpdKey key)
    {
        foreach (var l in Lines) if (l.Key == key) return true;
        return false;
    }
}

/// <summary>`misc/game.npd` as the loader reads it.</summary>
public sealed class NpdFile
{
    /// <summary>The prototype table's size (0x100 slots; reading stops when they are full).</summary>
    public const int MaxEmitters = 256;
    /// <summary>The line buffer: <c>fgets</c> into 0x200 bytes (at most 0x1ff characters).</summary>
    public const int LineMax = 0x1ff;
    /// <summary>A name is kept in 0x20 bytes with the last forced to 0.</summary>
    public const int NameMax = 31;

    /// <summary>The kind names in table order; a header's first word must start with one.</summary>
    public static readonly string[] KindNames =
        ["none", "gas", "fire", "liquid", "flame", "trail", "rain", "sparks", "shock", "beam", "magic", "ring", "pad", "spurt", "object"];

    /// <summary>The keyword table in its order; the index is the <see cref="NpdKey"/>.</summary>
    public static readonly string[] KeyNames =
    [
        "invalid", "end", "subtype", "material", "lifespan", "lifespanmin", "lifespanmax", "startsize",
        "minstartsize", "maxstartsize", "endsize", "minendsize", "maxendsize", "birthrate", "minbirthrate",
        "maxbirthrate", "maxparticles", "count", "size", "growthrate", "mingrowth", "maxgrowth", "startcolor",
        "startcolormin", "startcolormax", "endcolor", "endcolormin", "endcolormax", "mindist", "maxdist",
        "turbulence", "gravity", "drag", "velocity", "velocityvar", "numgroups", "mingroup", "maxgroup",
        "cohesion", "groupcohesion", "rotvel", "minrotvel", "maxrotvel", "speed", "minspeed", "maxspeed",
        "radius", "minradius", "maxradius", "length", "width", "updaterate", "delay", "texturerate",
        "frequency", "startheight", "endheight", "numsegs", "emitshape", "emitradius", "emitheight",
        "emitwidth", "emitdir", "startpos", "endpos", "flags", "template",
    ];

    static readonly char[] Delims = [' ', '\r', '\t', '\n'];

    public IReadOnlyList<NpdEmitter> Emitters => emitters;
    readonly List<NpdEmitter> emitters = [];
    readonly Dictionary<string, NpdEmitter> byName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Header lines the loader passed over, with why: a repeated name, or the table full.</summary>
    public IReadOnlyList<string> Skipped => skipped;
    readonly List<string> skipped = [];

    /// <summary>An emitter by name, as the game finds one: case-insensitive, cut to 31 characters.</summary>
    public NpdEmitter? this[string name] =>
        byName.GetValueOrDefault(name.Length > NameMax ? name[..NameMax] : name);

    public static NpdFile Read(byte[] data, string name = "game.npd")
    {
        var file = new NpdFile();
        var lines = Split(Encoding.Latin1.GetString(data));
        int i = 0;
        while (i < lines.Count && file.emitters.Count < MaxEmitters)
        {
            var (text, number) = lines[i++];
            if (text.Length == 0 || text[0] == ';') continue;
            var words = text.Split(Delims, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) continue;
            // The first kind whose name the word starts with; "none" (0) makes nothing.
            int kind = Array.FindIndex(KindNames, k => words[0].StartsWith(k, StringComparison.Ordinal));
            if (kind <= 0 || words.Length < 2) continue;
            var emitterName = words[1].Length > NameMax ? words[1][..NameMax] : words[1];
            if (file.byName.ContainsKey(emitterName))
            {
                file.skipped.Add($"{name}:{number}: '{emitterName}' is already defined");
                continue;
            }
            // The kind's parser: key lines until `end` or the end of the file.
            var body = new List<NpdLine>();
            while (i < lines.Count)
            {
                var (line, at) = lines[i++];
                if (line.Length == 0 || line[0] == ';') continue;
                var w = line.Split(Delims, StringSplitOptions.RemoveEmptyEntries);
                if (w.Length == 0) continue;
                int key = Array.FindIndex(KeyNames, k => k.Equals(w[0], StringComparison.OrdinalIgnoreCase));
                if (key <= 0) continue;                      // unknown, or "invalid": read on
                if (key == (int)NpdKey.End) break;
                body.Add(new NpdLine((NpdKey)key, w.Length > 1 ? w[1] : null, at));
            }
            var emitter = new NpdEmitter((EmitterKind)kind, emitterName, body);
            file.emitters.Add(emitter);
            file.byName[emitterName] = emitter;
        }
        return file;
    }

    /// <summary>The lines, cut where <c>fgets</c> would cut them, with their 1-based numbers.</summary>
    static List<(string Text, int Number)> Split(string text)
    {
        var result = new List<(string, int)>();
        int number = 0;
        foreach (var raw in text.Split('\n'))
        {
            number++;
            // A longer line comes back from fgets in pieces; each piece is a line to the loader.
            for (int at = 0; at == 0 || at < raw.Length; at += LineMax)
                result.Add((raw.Substring(at, Math.Min(LineMax, raw.Length - at)), number));
        }
        return result;
    }

    // ---- the C library's conversions, as the parsers use them -------------------------------------

    /// <summary>
    /// <c>atoi</c>: leading blanks, a sign, then digits up to the first non-digit — so
    /// <c>"0x20"</c> is 0 and <c>"2.5"</c> is 2. Nothing numeric is 0.
    /// </summary>
    public static int Int(string? s)
    {
        if (s is null) return 0;
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) neg = s[i++] == '-';
        long v = 0;
        while (i < s.Length && s[i] is >= '0' and <= '9') v = unchecked(v * 10 + (s[i++] - '0'));
        return unchecked((int)(neg ? -v : v));
    }

    /// <summary>
    /// <c>atof</c>: the longest leading run that reads as a decimal number, as a float
    /// (the parsers store the double into a float). Nothing numeric is 0.
    /// </summary>
    public static float Float(string? s)
    {
        if (s is null) return 0f;
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        int start = i;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
        while (i < s.Length && char.IsDigit(s[i])) i++;
        if (i < s.Length && s[i] == '.') { i++; while (i < s.Length && char.IsDigit(s[i])) i++; }
        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
        {
            int e = i + 1;
            if (e < s.Length && (s[e] == '-' || s[e] == '+')) e++;
            if (e < s.Length && char.IsDigit(s[e])) { i = e; while (i < s.Length && char.IsDigit(s[i])) i++; }
        }
        return double.TryParse(s.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? (float)d : 0f;
    }

    /// <summary>
    /// <c>sscanf("%x")</c>: an optional <c>0x</c>, then hex digits up to the first that is not one.
    /// Answers false (and leaves the field alone, as sscanf does) when there are no digits at all.
    /// </summary>
    public static bool Hex(string? s, out uint value)
    {
        value = 0;
        if (s is null) return false;
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        if (i + 1 < s.Length && s[i] == '0' && (s[i + 1] == 'x' || s[i + 1] == 'X')) i += 2;
        int digits = 0;
        uint v = 0;
        for (; i < s.Length && Uri.IsHexDigit(s[i]); i++, digits++) v = unchecked(v * 16 + (uint)Uri.FromHex(s[i]));
        if (digits == 0) return false;
        value = v;
        return true;
    }

    /// <summary>
    /// <c>sscanf("(%f,%f,%f)")</c>: as many of the three as convert, in order. A field that does not
    /// convert, and every one after it, keeps the value it had — which is how sscanf leaves them.
    /// </summary>
    public static int Vector(string? s, ref float x, ref float y, ref float z)
    {
        if (s is null || s.Length == 0 || s[0] != '(') return 0;
        var parts = s[1..].Split(',');
        int got = 0;
        for (int k = 0; k < 3 && k < parts.Length; k++)
        {
            var p = parts[k].TrimEnd(')');
            if (!LooksNumeric(p)) break;
            float v = Float(p);
            if (k == 0) x = v; else if (k == 1) y = v; else z = v;
            got++;
        }
        return got;
    }

    static bool LooksNumeric(string p)
    {
        var t = p.TrimStart();
        if (t.Length > 0 && (t[0] == '-' || t[0] == '+')) t = t[1..];
        return t.Length > 0 && (char.IsDigit(t[0]) || (t[0] == '.' && t.Length > 1 && char.IsDigit(t[1])));
    }
}
