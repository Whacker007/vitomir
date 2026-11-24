// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text;

namespace Vitomir.Formats;

/// <summary>A textured quad of a UI pane. Coordinates are 640×480 with the origin at the bottom left.</summary>
public sealed record NuiQuad(int[] Pos, int[] Size, string Material, int Line);

/// <summary>A UI control: its type and raw key=value attributes (values are lower-case, as the engine sees them).</summary>
public sealed record NuiElement(string Type, IReadOnlyDictionary<string, string> Attrs, int Line)
{
    public string? Get(string key) => Attrs.TryGetValue(key, out var v) ? v : null;
    public string Name => Get("name") ?? "";
    public int[] Pos => Get("pos") is { } v ? NuiPane.Ints(v) : [0, 0];
    public int[] Size => Get("size") is { } v ? NuiPane.Ints(v) : [0, 0];
    public int Flags => NuiPane.CInt(Get("flags") ?? "0");
}

/// <summary>
/// NUI UI pane (text). The parser follows the engine's pane loader so edge
/// cases resolve the same way: lines up to 511 chars; ';' comments only in column 0; the whole
/// line is lower-cased; tokens split on whitespace; key=value split like sscanf("%[^=]=%[^=]").
/// </summary>
public sealed class NuiPane
{
    public static readonly string[] ElementTypes =
    [
        "static", "button", "textbox", "checkbox", "icon", "icongrid", "progressbar", "edit", "scrollbar",
        "scrollslider", "textlistbox", "imagearray", "disciplineslot", "weargrid", "soakslice",
    ];

    static readonly HashSet<string> QuadKeys = ["pos", "size", "mat"];

    /// <summary>Every key the engine's control parsers compare against.</summary>
    public static readonly HashSet<string> ElementKeys =
    [
        "name", "pos", "size", "flags", "parent", "hintstring", "key", "flash", "hide", "restore", "bitmap", "color",
        "font", "maxitems", "textboxflags", "tbtextcolor", "hscrollbar", "vscrollbar", "itemspacing", "tlbcurcolor",
        "tlbselcolor", "quanta", "slider", "vertical", "fillbitmap", "gridid", "gridsize", "radiogroup", "checkedbitmap",
        "buttonvalue", "depressed", "greyed", "rollover", "sliceprefix",
        "image0", "image1", "image2", "image3", "image4", "image5", "image6", "image7", "image8", "image9",
    ];

    public string Name { get; private set; } = "";
    public int[] Pos { get; private set; } = [0, 0];
    public bool Dialog { get; private set; }
    public bool OnTop { get; private set; }
    public string VisGroup { get; private set; } = "";
    public string Music { get; private set; } = "";
    public List<NuiQuad> Quads { get; } = [];
    public List<NuiElement> Elements { get; } = [];
    /// <summary>Lines the engine would skip or drop, described. Empty for every shipped pane.</summary>
    public List<string> Warnings { get; } = [];

    public static NuiPane Read(byte[] data, string name = "<nui>") => Parse(Encoding.Latin1.GetString(data), name);

    /// <summary>A copy whose lists of quads and controls can be changed without touching this one's (the quads and controls themselves are records, never changed in place).</summary>
    public NuiPane Copy()
    {
        var copy = new NuiPane { Name = Name, Pos = (int[])Pos.Clone(), Dialog = Dialog, OnTop = OnTop, VisGroup = VisGroup, Music = Music };
        copy.Quads.AddRange(Quads);
        copy.Elements.AddRange(Elements);
        copy.Warnings.AddRange(Warnings);
        return copy;
    }

    public static NuiPane Parse(string text, string name = "<nui>")
    {
        var pane = new NuiPane();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            int ln = i + 1;
            string line = lines[i].Length > 511 ? lines[i][..511] : lines[i];
            if (line.Length == 0 || line[0] == ';') continue;
            var toks = line.ToLowerInvariant().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (toks.Length == 0) continue;
            if (toks[0] == "quad")
            {
                var kv = new Dictionary<string, string>();
                foreach (var t in toks.Skip(1)) { var (k, v) = SplitKv(t); kv[k] = v; }
                if (!kv.Keys.All(QuadKeys.Contains) || !QuadKeys.All(kv.ContainsKey))
                {
                    pane.Warnings.Add($"{name}:{ln}: quad keys [{string.Join(", ", kv.Keys)}] (the engine would drop it)");
                    continue;
                }
                pane.Quads.Add(new NuiQuad(Ints(kv["pos"]), Ints(kv["size"]), kv["mat"], ln));
            }
            else if (toks[0] == "element")
            {
                if (toks.Length < 2 || Array.IndexOf(ElementTypes, toks[1]) < 0)
                {
                    pane.Warnings.Add($"{name}:{ln}: unknown control type (the engine skips it)");
                    continue;
                }
                var attrs = new Dictionary<string, string>();
                foreach (var t in toks.Skip(2))
                {
                    var (k, v) = SplitKv(t);
                    if (!ElementKeys.Contains(k)) pane.Warnings.Add($"{name}:{ln}: unknown key '{k}' (the engine drops the control)");
                    attrs[k] = v;
                }
                pane.Elements.Add(new NuiElement(toks[1], attrs, ln));
            }
            else
            {
                var (k, v) = SplitKv(toks[0]);          // only the first token counts for pane keys
                switch (k)
                {
                    case "name": pane.Name = v.Length > 31 ? v[..31] : v; break;
                    case "pos": pane.Pos = Ints(v); break;
                    case "dialog": pane.Dialog = FirstOrZero(Ints(v)) != 0; break;
                    case "ontop": pane.OnTop = FirstOrZero(Ints(v)) != 0; break;
                    case "visgroup": pane.VisGroup = v; break;
                    case "music": pane.Music = v; break;
                    default: pane.Warnings.Add($"{name}:{ln}: unknown pane key '{k}'"); break;
                }
            }
        }
        return pane;
    }

    static int FirstOrZero(int[] a) => a.Length > 0 ? a[0] : 0;

    /// <summary>sscanf("%[^=]=%[^=]"): the key up to the first '=', the value up to the next.</summary>
    public static (string Key, string Value) SplitKv(string token)
    {
        int eq = token.IndexOf('=');
        if (eq < 0) return (token, "");
        string rest = token[(eq + 1)..];
        int eq2 = rest.IndexOf('=');
        return (token[..eq], eq2 < 0 ? rest : rest[..eq2]);
    }

    /// <summary>
    /// **What `textboxflags` does** — settled, after
    /// a base-game screenshot of the character sheet showed every value **centred** in its
    /// box where Vitomir drew it flush right. The first reading had the right bits
    /// and the **axes swapped**: it took the control's width for its height and its height for its
    /// width. The pane's own hit test settles which is which (the pointer's x is tested against
    /// the left edge and the width, its y against the bottom edge and the height), and with that the
    /// same rules say something else entirely.
    ///
    /// The value is parsed with `%i` into the control, which is why the shipped panes
    /// write both `0x01` and a bare `7`. Positions inside the box count **up from its bottom**, as
    /// everything in the UI does — which the screenshot settles where the rules alone do
    /// not: with a 15-tall box and a shorter line, "the item at `height − itemHeight`" is the top
    /// of the box, and that is where the base game's sheet has its numbers.
    /// <list type="bullet">
    /// <item><b>bit 0 — centred across the box.</b> The line draw puts each line at
    /// `(width − inset − lineWidth) / 2` when it is set (a signed halving, so a line wider
    /// than its box starts left of it), and at the inset when it is not — plain left.</item>
    /// <item><b>bit 3 — where the items start.</b> Clear, the relayout puts the first
    /// item against the **top** — its bottom at `height − itemHeight` — and each later one below
    /// it, which is how the status bar's log reads down the page. Set, the newest item
    /// sits a quarter of the font's height above the **bottom** and older ones stack above it.</item>
    /// <item><b>bit 1 — the block centred down the box.</b> After the relayout, the box moves the
    /// items by <c>−0.5</c> of the space they leave (rounded toward zero, so the half
    /// is truncated).</item>
    /// <item><b>bit 2 — word wrap.</b> Adding a line splits it at the box's width,
    /// between words, when this is set, and adds it whole when it is not. The large description
    /// box (`0x07`) and the status bar's log (`0x04`) wrap; nothing else does.</item>
    /// <item><b>bit 4</b> gates the editing path.</item>
    /// </list>
    /// So the character pane's `0x01` is **centred across, against the top**; the small description
    /// box's `0x03` is centred both ways; the subtitle's `0x0C` is left-aligned against the bottom;
    /// the status bar's log, `0x04`, is left-aligned from the top down.
    /// </summary>
    [Flags]
    public enum TextBoxFlags
    {
        None = 0,
        /// <summary>Each line centred across the box; without it, lines start at the inset.</summary>
        Centre = 0x1,
        /// <summary>The block of items centred down the box.</summary>
        VCentre = 0x2,
        /// <summary>A line longer than the box is broken between words.</summary>
        WordWrap = 0x4,
        /// <summary>
        /// The newest item against the bottom, older ones above; without it, the first item against
        /// the top and later ones below.
        /// </summary>
        FromBottom = 0x8,
        /// <summary>The editing path, not a layout bit.</summary>
        Editable = 0x10,
    }

    /// <summary>The `textboxflags` word, or <see cref="TextBoxFlags.None"/> when the box has none.</summary>
    public static TextBoxFlags TextBox(NuiElement box) =>
        box.Get("textboxflags") is { } value && TryNumber(value, out int flags)
            ? (TextBoxFlags)flags : TextBoxFlags.None;

    /// <summary>
    /// Where a one-line, one-item textbox puts its line, relative to the box's own corner, in the
    /// pane's own space (y counting up from the bottom, to the line's bottom edge).
    /// </summary>
    public static (int X, int Y) TextBoxOrigin(NuiElement box, int width, int height,
                                               int textWidth, int lineHeight) =>
        TextBoxLine(box, width, height, textWidth, lineHeight, 0, 1);

    /// <summary>
    /// Line <paramref name="index"/> (counting from the top) of a box holding one item of
    /// <paramref name="lines"/> lines — or of several one-line items <paramref name="spacing"/>
    /// apart, which lay out the same way with the gap added — relative to the box's corner, y
    /// counting up to the line's bottom edge. The inset is zeroed by the
    /// constructor and no pane sets it.
    /// </summary>
    public static (int X, int Y) TextBoxLine(NuiElement box, int width, int height, int textWidth,
                                             int lineHeight, int index, int lines, int spacing = 0)
    {
        var flags = TextBox(box);
        // `(width − inset − lineWidth) / 2`: C#'s division truncates toward zero, as `idiv` does.
        int x = flags.HasFlag(TextBoxFlags.Centre) ? (width - textWidth) / 2 : 0;
        int block = lines * lineHeight + Math.Max(0, lines - 1) * spacing;
        // The block's bottom edge, counted up from the box's bottom.
        int bottom = flags.HasFlag(TextBoxFlags.FromBottom) ? lineHeight / 4 : height - block;
        // The free space halved and truncated toward zero.
        if (flags.HasFlag(TextBoxFlags.VCentre)) bottom += (int)(-0.5f * (height - block));
        return (x, bottom + (lines - 1 - index) * (lineHeight + spacing));
    }

    /// <summary>`%i`: a leading `0x` means hexadecimal, anything else is decimal.</summary>
    public static bool TryNumber(string value, out int number)
    {
        var text = value.Trim();
        bool hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        return hex
            ? int.TryParse(text.AsSpan(2), System.Globalization.NumberStyles.HexNumber,
                           System.Globalization.CultureInfo.InvariantCulture, out number)
            : int.TryParse(text, out number);
    }

    /// <summary>sscanf-style "%d,%d,…": stops at the first field that doesn't parse, so "53,15," gives [53, 15].</summary>
    public static int[] Ints(string value)
    {
        var result = new List<int>();
        foreach (var part in value.Split(','))
        {
            if (!TryDecimal(part, out int v)) break;
            result.Add(v);
        }
        return [.. result];
    }

    static bool TryDecimal(string s, out int value)
    {
        value = 0;
        int i = 0;
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) { neg = s[i] == '-'; i++; }
        int start = i; long acc = 0;
        while (i < s.Length && char.IsAsciiDigit(s[i])) { acc = acc * 10 + (s[i] - '0'); i++; }
        if (i == start) return false;
        value = (int)(neg ? -acc : acc);
        return true;
    }

    /// <summary>sscanf "%i" (strtol base 0): 0x.. hex, leading-0 octal, else decimal; 0 if it doesn't parse.</summary>
    public static int CInt(string value)
    {
        var s = value.TrimStart(' ', '\t');
        bool neg = false;
        if (s.Length > 0 && (s[0] == '+' || s[0] == '-')) { neg = s[0] == '-'; s = s[1..]; }
        long n = 0;
        if (s.Length > 2 && s[0] == '0' && (s[1] == 'x' || s[1] == 'X') && Uri.IsHexDigit(s[2]))
        {
            foreach (char c in s[2..]) { if (!Uri.IsHexDigit(c)) break; n = n * 16 + Convert.ToInt32(c.ToString(), 16); }
        }
        else if (s.Length > 0 && s[0] == '0')
        {
            foreach (char c in s[1..]) { if (c is < '0' or > '7') break; n = n * 8 + (c - '0'); }
        }
        else
        {
            foreach (char c in s) { if (!char.IsAsciiDigit(c)) break; n = n * 10 + (c - '0'); }
        }
        return (int)(neg ? -n : n);
    }
}
