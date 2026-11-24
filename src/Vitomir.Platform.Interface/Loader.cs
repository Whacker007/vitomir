// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace Vitomir.Platform.Interface;

/// <summary>
/// One piece's object in a screen's file, read field by field: what a kind's maker reads is known, and every field
/// it leaves unread is said (once) as unknown, so a misspelt field is never silently ignored.
/// </summary>
public sealed class PieceReader
{
    readonly JsonElement e;
    readonly HashSet<string> read = new(StringComparer.Ordinal);
    readonly ScreenLoader loader;

    internal PieceReader(JsonElement e, ScreenLoader loader, string type)
    {
        this.e = e;
        this.loader = loader;
        Type = type;
        read.Add("type");
    }

    public string Type { get; }
    public ScreenLoader Loader => loader;

    bool Field(string name, out JsonElement v)
    {
        read.Add(name);
        return e.TryGetProperty(name, out v);
    }

    public bool Has(string name) => e.TryGetProperty(name, out _);

    public string? String(string name) => Field(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public string String(string name, string fallback) => String(name) ?? fallback;

    public float Number(string name, float fallback)
    {
        if (!Field(name, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.Number) return v.GetSingle();
        loader.Problem($"{Type}.{name} should be a number");
        return fallback;
    }

    /// <summary>
    /// A condition: a data flag's name, or <c>true</c> (<see cref="Button.Always"/>); <c>false</c> or left out: null (never).
    /// </summary>
    public string? Condition(string name)
    {
        if (!Field(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.True) return Button.Always;
        if (v.ValueKind == JsonValueKind.String) return v.GetString();
        if (v.ValueKind != JsonValueKind.False) loader.Problem($"{Type}.{name} should be true, false or a data name");
        return null;
    }

    public bool Bool(string name, bool fallback) => Field(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;

    /// <summary>A size: a number of units, <c>"fill"</c>, or <c>"auto"</c> (or left out).</summary>
    public SizeSpec Size(string name)
    {
        if (!Field(name, out var v)) return SizeSpec.Auto;
        if (v.ValueKind == JsonValueKind.Number) return SizeSpec.Fixed(v.GetSingle());
        if (v.ValueKind == JsonValueKind.String && v.GetString() is "fill") return SizeSpec.Fill;
        if (v.ValueKind == JsonValueKind.String && v.GetString() is "auto") return SizeSpec.Auto;
        loader.Problem($"{Type}.{name} should be a number of units, \"fill\" or \"auto\"");
        return SizeSpec.Auto;
    }

    /// <summary>Space on each side: one number for all four, or [across, down], or [left, top, right, bottom].</summary>
    public Edges Edges(string name)
    {
        if (!Field(name, out var v)) return default;
        if (v.ValueKind == JsonValueKind.Number) return Interface.Edges.All(v.GetSingle());
        if (v.ValueKind == JsonValueKind.Array)
        {
            var n = v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetSingle()).ToArray();
            if (n.Length == 2) return new Edges(n[0], n[1], n[0], n[1]);
            if (n.Length == 4) return new Edges(n[0], n[1], n[2], n[3]);
        }
        loader.Problem($"{Type}.{name} should be a number, [across, down] or [left, top, right, bottom]");
        return default;
    }

    public Vector2 Pair(string name)
    {
        if (!Field(name, out var v)) return Vector2.Zero;
        if (v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 2 && v[0].ValueKind == JsonValueKind.Number && v[1].ValueKind == JsonValueKind.Number)
            return new Vector2(v[0].GetSingle(), v[1].GetSingle());
        loader.Problem($"{Type}.{name} should be [x, y]");
        return Vector2.Zero;
    }

    public string[] Strings(string name)
    {
        if (!Field(name, out var v)) return [];
        if (v.ValueKind == JsonValueKind.Array) return v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray();
        loader.Problem($"{Type}.{name} should be a list of words");
        return [];
    }

    public Align Align(string name, Align fallback) => String(name) switch
    {
        null => fallback,
        "start" or "left" or "top" => Interface.Align.Start,
        "centre" or "center" or "middle" => Interface.Align.Centre,
        "end" or "right" or "bottom" => Interface.Align.End,
        "stretch" => Interface.Align.Stretch,
        var s => Bad(name, s, fallback),
    };

    public TextAlign TextAlign(string name, TextAlign fallback) => Align(name, fallback switch
    {
        Interface.TextAlign.Centre => Interface.Align.Centre,
        Interface.TextAlign.End => Interface.Align.End,
        _ => Interface.Align.Start,
    }) switch
    {
        Interface.Align.Centre => Interface.TextAlign.Centre,
        Interface.Align.End => Interface.TextAlign.End,
        _ => Interface.TextAlign.Start,
    };

    Align Bad(string name, string value, Align fallback)
    {
        loader.Problem($"{Type}.{name} \"{value}\" is not start, centre, end or stretch");
        return fallback;
    }

    /// <summary>
    /// A text style by its name: the loader's own styles' first (<see cref="ScreenLoader.Styles"/>, a screen with a look of its
    /// own), else the theme's (unknown: said, and the plain label style).
    /// </summary>
    public TextStyle Style(string name, string fallback)
    {
        var s = String(name) ?? fallback;
        if (loader.Styles is { } own && own.TryGetValue(s, out var mine)) return mine;
        if (Theme.Styles.TryGetValue(s, out var style)) return style;
        var known = loader.Styles is { } o ? o.Keys.Union(Theme.Styles.Keys) : Theme.Styles.Keys;
        loader.Problem($"{Type}.{name}: no text style \"{s}\" (the styles are {string.Join(", ", known)})");
        return Theme.Styles[Theme.DefaultStyle];
    }

    /// <summary>A text style's name as the file gives it (or <paramref name="fallback"/>), for a look that dresses a piece by its style.</summary>
    public string StyleName(string name, string fallback) => String(name) ?? fallback;

    /// <summary>A colour: a theme colour's name, <c>#rrggbb[aa]</c>, or names from the data in braces (looked up when drawn).</summary>
    public string? Colour(string name)
    {
        var s = String(name);
        if (s is null || s.Contains('{')) return s;
        if (Rgba.Parse(s) is null && !Theme.Colours.ContainsKey(s))
        {
            loader.Problem($"{Type}.{name}: no colour \"{s}\"; drawn as nothing");
            return "none";
        }
        return s;
    }

    /// <summary>The pieces this one holds: <c>children</c> (a list) or <c>child</c> (one).</summary>
    public List<Piece> Children(Screen screen)
    {
        var list = new List<Piece>();
        if (Field("children", out var cs) && cs.ValueKind == JsonValueKind.Array)
            foreach (var c in cs.EnumerateArray()) list.Add(loader.Piece(c, screen));
        if (Field("child", out var one) && one.ValueKind == JsonValueKind.Object) list.Add(loader.Piece(one, screen));
        return list;
    }

    /// <summary>An object field read later, again for each use (a list's template).</summary>
    public JsonElement? Element(string name) => Field(name, out var v) && v.ValueKind == JsonValueKind.Object ? v.Clone() : null;

    /// <summary>Says every field the maker did not read.</summary>
    internal void SayUnread()
    {
        foreach (var p in e.EnumerateObject())
            if (!read.Contains(p.Name)) loader.Problem($"{Type} has no field \"{p.Name}\"; it is passed over");
    }
}

/// <summary>
/// The kinds of piece a screen's file may name, each with its maker (ours). The two screens need the ones
/// registered here; a later piece (a picture, a bar, tabs, a tooltip) is one more <see cref="Register"/>.
/// </summary>
public static class PieceTypes
{
    static readonly Dictionary<string, Func<PieceReader, Screen, Piece>> Makers = new(StringComparer.Ordinal);

    public static void Register(string type, Func<PieceReader, Screen, Piece> make)
    {
        lock (Makers) Makers[type] = make;
    }

    public static bool TryGet(string type, out Func<PieceReader, Screen, Piece> make)
    {
        lock (Makers) return Makers.TryGetValue(type, out make!);
    }

    public static IReadOnlyCollection<string> Names
    {
        get { lock (Makers) return Makers.Keys.Order(StringComparer.Ordinal).ToArray(); }
    }

    static PieceTypes()
    {
        Register("column", (r, s) => StackOf(new Stack { Down = true }, r, s));
        Register("row", (r, s) => StackOf(new Stack { Down = false, Across = Align.Centre }, r, s));
        Register("layer", (r, s) => Holding(new Layer(), r, s));
        Register("anchor", (r, s) =>
        {
            var at = r.String("at", "centre");
            var (across, down) = AnchorPoint(at, r);
            var a = new Anchor { AcrossAt = across, DownAt = down, Margin = r.Number("margin", 0f), Offset = r.Pair("offset") };
            return Holding(a, r, s);
        });
        Register("space", (r, s) => new Space());
        Register("panel", (r, s) => Holding(new Panel
        {
            Fill = r.Colour("fill") ?? "panel", Edge = r.Colour("edge"), Framed = r.Bool("framed", false), Padding = r.Edges("padding"),
        }, r, s));
        Register("scroll", (r, s) => Holding(new Scroll { Step = r.Number("step", 60f) }, r, s));
        Register("list", (r, s) =>
        {
            var template = r.Element("template");
            // "across": true lays the rows out across (the HUD's quick slots), each sitting by "align" (the middle).
            bool across = r.Bool("across", false);
            var list = new ListPiece
            {
                Items = r.String("items", ""), Gap = r.Number("gap", 0f), Stripes = r.Strings("stripes"),
                Down = !across, Across = across ? r.Align("align", Align.Centre) : Align.Stretch, Reorder = r.String("reorder"),
            };
            if (template is { } t)
            {
                // The template is read once now (so its problems are said once) and again for every row.
                r.Loader.Piece(t, s);
                list.Template = screen => r.Loader.Piece(t, screen);
            }
            else r.Loader.Problem("list has no template; it shows nothing");
            return list;
        });
        // A list row's drag handle (its list's "reorder"): the pointer drags the row; not a stop of the keyboard's focus.
        Register("handle", (r, s) => new DragHandle { Colour = r.Colour("fill") ?? "muted" });
        Register("text", (r, s) => new TextPiece
        {
            Text = r.String("text", ""), Style = r.Style("style", "label"), StyleName = r.StyleName("style", "label"),
            Align = r.TextAlign("align", TextAlign.Start), Wrap = r.Bool("wrap", false),
        });
        Register("meter", (r, s) => new Meter     // the HUD's bars
        {
            Field = r.String("bind", ""), Fill = r.Colour("fill") ?? "trackFill", Track = r.Colour("track") ?? "track", Edge = r.Colour("edge"),
        });
        Register("button", (r, s) => new Button
        {
            Text = r.String("text", ""), Style = r.Style("style", "button"), StyleName = r.StyleName("style", "button"), Action = r.String("action"),
            Argument = r.String("arg", ""), SelectedWhen = r.String("selected"), EnabledWhen = r.String("enabled"), Why = r.String("why"),
            DefaultWhen = r.Condition("default"), CancelWhen = r.Condition("cancel"), Quiet = r.Bool("quiet", false), Icon = r.String("icon"),
        });
        // A checkbox (a flag of the data's, on or off) and a radio button (one value of a group's field): a mark and a label, or what
        // it holds as a richer label; the whole piece is worked.
        Register("checkbox", (r, s) => Choosing(new Checkbox { Field = r.String("bind", "") }, r, s));
        Register("switch", (r, s) => Choosing(new Switch { Field = r.String("bind", "") }, r, s));
        Register("radio", (r, s) => Choosing(new Radio { Field = r.String("bind", ""), Value = r.String("value", ""), GroupName = r.String("group") }, r, s));
        Register("keycell", (r, s) => new KeyCell
        {
            Text = r.String("text", ""), State = r.String("state"), Action = r.String("action"), Argument = r.String("arg", ""),
            Style = r.Style("style", "value"), WaitingText = r.String("waiting", "Press a Key"), EmptyText = r.String("empty", "—"),
        });
        Register("slider", (r, s) => new Slider
        {
            Field = r.String("bind", ""), Min = r.Number("min", 0f), Max = r.Number("max", 1f), Step = r.Number("step", 0f),
            Action = r.String("action"), Argument = r.String("arg", ""),
        });
        Register("toggle", (r, s) =>
        {
            var options = r.Strings("options");
            return new Toggle
            {
                Field = r.String("bind", ""), Options = options.Length > 0 ? options : ["Off", "On"], Action = r.String("action"),
                Argument = r.String("arg", ""), Style = r.Style("style", "value"), Segments = r.Bool("segments", false),
                EnabledWhen = r.String("enabled"), Why = r.String("why"),
            };
        });
    }

    static Piece Choosing(Choice c, PieceReader r, Screen s)
    {
        c.Text = r.String("text", "");
        c.Style = r.Style("style", "label");
        c.Wrap = r.Bool("wrap", false);
        c.Padding = r.Edges("padding");
        c.MarkSize = r.Number("mark", Choice.DefaultMark);
        c.Action = r.String("action");
        c.Argument = r.String("arg", "");
        c.EnabledWhen = r.String("enabled");
        c.Why = r.String("why");
        return Holding(c, r, s);
    }

    static Piece StackOf(Stack stack, PieceReader r, Screen s)
    {
        stack.Gap = r.Number("gap", 0f);
        stack.Padding = r.Edges("padding");
        stack.Across = r.Align("align", stack.Across);
        return Holding(stack, r, s);
    }

    static Piece Holding(Piece p, PieceReader r, Screen s)
    {
        p.Children.AddRange(r.Children(s));
        return p;
    }

    static (Align Across, Align Down) AnchorPoint(string at, PieceReader r)
    {
        Align across = Align.Centre, down = Align.Centre;
        foreach (var part in at.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            switch (part)
            {
                case "top": down = Align.Start; break;
                case "bottom": down = Align.End; break;
                case "left": across = Align.Start; break;
                case "right": across = Align.End; break;
                case "centre" or "center" or "middle": break;
                default: r.Loader.Problem($"anchor.at \"{at}\": \"{part}\" is not top, bottom, left, right or centre"); break;
            }
        return (across, down);
    }
}

/// <summary>
/// Reads a screen's file (JSON): its name, the veil over the picture behind it, the action Escape fires, and its tree
/// of pieces. What a file gets wrong — a kind of piece no one registered, a field no kind has — is said once in
/// <see cref="Problems"/> (and to the host's log) and drawn as nothing; the rest of the screen still works.
/// </summary>
public sealed class ScreenLoader
{
    readonly List<string> problems = [];
    readonly HashSet<string> said = new(StringComparer.Ordinal);
    readonly Action<string>? say;
    string file = "";

    public ScreenLoader(Action<string>? say = null) { this.say = say; }

    /// <summary>
    /// Text styles of the screens this loader reads, by name, taken before the theme's (none: the theme's alone). The
    /// launcher's look sets its words in its own faces and colours this way, at the theme's sizes.
    /// </summary>
    public IReadOnlyDictionary<string, TextStyle>? Styles { get; init; }

    /// <summary>Everything said about the files read, each once.</summary>
    public IReadOnlyList<string> Problems => problems;

    public void Problem(string what)
    {
        var line = $"{file}: {what}";
        if (!said.Add(line)) return;
        problems.Add(line);
        say?.Invoke($"interface: {line}");
    }

    /// <summary>A screen of this assembly's by name (<c>pause</c>, <c>controls</c>).</summary>
    public Screen LoadEmbedded(string name, ScreenData? data = null)
    {
        using var s = typeof(ScreenLoader).Assembly.GetManifestResourceStream($"Screens/{name}.json")
                      ?? throw new FileNotFoundException($"no screen \"{name}\" (Screens/{name}.json)");
        using var reader = new StreamReader(s);
        return Parse(reader.ReadToEnd(), name, data);
    }

    /// <summary>A screen from its file's text; <paramref name="name"/> is what problems are said against.</summary>
    public Screen Parse(string json, string name, ScreenData? data = null)
    {
        file = name;
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        var screen = new Screen(name, data ?? new ScreenData(), this);
        var top = new PieceReader(root, this, "screen");
        screen.Back = top.String("back");
        screen.Veil = top.Colour("veil") ?? "none";
        screen.Title = top.String("title", name);
        screen.Note = top.String("note", "");     // a word about the screen for whoever reads its file (the HUD: a stand-in)
        if (top.Element("root") is { } r) screen.Root = Piece(r, screen);
        else Problem("the screen has no root piece");
        top.SayUnread();
        return screen;
    }

    /// <summary>One piece from its object (an unknown kind: said, and <see cref="Nothing"/>).</summary>
    public Piece Piece(JsonElement e, Screen screen)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            Problem("a piece is not an object; passed over");
            return new Nothing { Type = "nothing" };
        }
        var type = e.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "";
        if (!PieceTypes.TryGet(type, out var make))
        {
            Problem(type.Length == 0 ? "a piece has no type; drawn as nothing" : $"no piece \"{type}\"; drawn as nothing (the pieces are {string.Join(", ", PieceTypes.Names)})");
            return new Nothing { Type = type };
        }
        var r = new PieceReader(e, this, type);
        var piece = make(r, screen);
        piece.Type = type;
        piece.Id = r.String("id");
        piece.Width = r.Size("width");
        piece.Height = r.Size("height");
        piece.MinWidth = r.Number("minWidth", 0f);
        piece.MinHeight = r.Number("minHeight", 0f);
        piece.MaxWidth = r.Number("maxWidth", float.PositiveInfinity);
        piece.MaxHeight = r.Number("maxHeight", float.PositiveInfinity);
        piece.VisibleWhen = r.String("visible");
        // A container's own size defaults (an anchor or a scroll fills) stay unless the file gives one.
        if (!r.Has("width") && piece is Anchor or Scroll) piece.Width = SizeSpec.Fill;
        if (!r.Has("height") && piece is Anchor or Scroll) piece.Height = SizeSpec.Fill;
        r.SayUnread();
        return piece;
    }

    internal static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
