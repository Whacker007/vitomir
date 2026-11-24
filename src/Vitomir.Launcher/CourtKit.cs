// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The launcher's look: the Court of Night kit (Art/court/uikit.json and its pictures), its text styles and colours.
using System.Text.Json;
using Vitomir.Platform.Interface;

namespace Vitomir.Launcher;

/// <summary>One picture of the kit as its <c>uikit.json</c> describes it: its file, its size and nine-slice insets in its own pixels, and its words' colour.</summary>
public sealed record KitPiece(string Name, string File, int Width, int Height, int Left, int Top, int Right, int Bottom,
                              Rgba? Text, Rgba? TextUnselected, Rgba? Placeholder);

/// <summary>
/// **The Court of Night kit** ("Registers" layout), the launcher's look: its pictures' slices and colours read from the kit's
/// own <c>uikit.json</c> (embedded as <c>Launcher/court/uikit.json</c>), and the launcher's text styles in Vitomir's
/// delivered faces: titles, labels, tabs and buttons in Vitomir Wedge SemiBold, running words, values and notes in Vitomir
/// Text, a warning in Vitomir Text SemiBold (an error's in the kit's red, a risk the player may accept in its gold), the version in Vitomir Knife. **The sizes are the theme's** (an em and a line's
/// height each, as <c>Screens/launcher.json</c> was laid out with): the kit gives each style its face and colour only.
/// The kit's pictures are drawn at twice the laid-out size (<see cref="Scale"/>): a picture's pixels are halved for units.
/// </summary>
public sealed class CourtKit
{
    /// <summary>The kit's pictures' pixels to a laid-out unit.</summary>
    public const float Scale = 2f;

    readonly Dictionary<string, Rgba> colours = new(StringComparer.Ordinal);
    readonly Dictionary<string, KitPiece> pieces = new(StringComparer.Ordinal);

    CourtKit() { }

    /// <summary>The kit, read once from the launcher's resources.</summary>
    public static CourtKit Court => court.Value;
    static readonly Lazy<CourtKit> court = new(() =>
    {
        using var s = typeof(CourtKit).Assembly.GetManifestResourceStream("Launcher/court/uikit.json")
                      ?? throw new InvalidOperationException("the launcher's kit (Launcher/court/uikit.json) is not in vitomir-launcher");
        return Read(s);
    });

    /// <summary>A kit from its <c>uikit.json</c>.</summary>
    public static CourtKit Read(Stream json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var kit = new CourtKit();
        foreach (var c in root.GetProperty("colours").EnumerateObject()) kit.colours[c.Name] = Colour(c.Value);
        var layout = root.GetProperty("layout");
        kit.TitleBand = layout.GetProperty("title_band_height").GetSingle();
        kit.Frieze = layout.GetProperty("frieze_height").GetSingle();
        kit.BodyPadding = (layout.GetProperty("body_padding")[0].GetSingle(), layout.GetProperty("body_padding")[1].GetSingle());
        kit.ActionPadding = (layout.GetProperty("action_band_padding")[0].GetSingle(), layout.GetProperty("action_band_padding")[1].GetSingle());
        foreach (var a in root.GetProperty("assets").EnumerateObject())
        {
            var v = a.Value;
            int l = 0, t = 0, r = 0, b = 0;
            if (v.TryGetProperty("slice_px", out var sl))
                (l, t, r, b) = (sl.GetProperty("left").GetInt32(), sl.GetProperty("top").GetInt32(), sl.GetProperty("right").GetInt32(), sl.GetProperty("bottom").GetInt32());
            var size = v.GetProperty("size_px");
            kit.pieces[a.Name] = new KitPiece(a.Name, v.GetProperty("file").GetString()!, size[0].GetInt32(), size[1].GetInt32(), l, t, r, b,
                v.TryGetProperty("text_rgb", out var tx) ? Colour(tx) : null,
                v.TryGetProperty("text_rgb_unselected", out var tu) ? Colour(tu) : null,
                v.TryGetProperty("placeholder_rgb", out var ph) ? Colour(ph) : null);
            if (a.Name == "panel" && v.TryGetProperty("content_inset_px", out var inset)) kit.ContentInset = inset[0].GetSingle() / Scale;
        }
        return kit;
    }

    static Rgba Colour(JsonElement rgb) =>
        new(rgb[0].GetInt32() / 255f, rgb[1].GetInt32() / 255f, rgb[2].GetInt32() / 255f, rgb.GetArrayLength() > 3 ? rgb[3].GetInt32() / 255f : 1f);

    /// <summary>The title band's height, the frieze's, in units; the body's and the action band's padding (down, across).</summary>
    public float TitleBand { get; private set; }
    public float Frieze { get; private set; }
    public (float Down, float Across) BodyPadding { get; private set; }
    public (float Down, float Across) ActionPadding { get; private set; }

    /// <summary>How far inside a framed panel's edge its bands and friezes are laid, units (the panel's content inset).</summary>
    public float ContentInset { get; private set; }

    /// <summary>A colour of the kit's by name (<c>field</c>, <c>text_gold</c>, <c>band</c>, <c>scrim</c> …).</summary>
    public Rgba Colour(string name) => colours.TryGetValue(name, out var c) ? c : throw new KeyNotFoundException($"the kit has no colour \"{name}\"");

    /// <summary>A picture of the kit's by name (<c>panel</c>, <c>button_hover</c> …).</summary>
    public KitPiece Piece(string name) => pieces.TryGetValue(name, out var p) ? p : throw new KeyNotFoundException($"the kit has no piece \"{name}\"");

    public IReadOnlyDictionary<string, KitPiece> Pieces => pieces;

    /// <summary>
    /// **The launcher's text styles** in the kit's faces and colours, at the theme's sizes (each style's em and line height
    /// as the theme's of the same name; <c>number</c>, the version's, as <c>note</c>'s). Plain letters: no outline, no shadow.
    /// <c>title</c> is the wordmark's picture, drawn into the box its words would have had.
    /// </summary>
    public IReadOnlyDictionary<string, TextStyle> Styles => styles ??= MakeStyles();
    Dictionary<string, TextStyle>? styles;

    Dictionary<string, TextStyle> MakeStyles()
    {
        TextStyle Of(string size, Lettering lettering, string colour) =>
            new(lettering, Theme.Styles[size].Em, Colour(colour), TextLook.Plain, Theme.Styles[size].LineHeight);
        return new Dictionary<string, TextStyle>(StringComparer.Ordinal)
        {
            ["title"] = Of("title", Lettering.Wedge, "text_gold"),
            ["heading"] = Of("heading", Lettering.Wedge, "text_gold"),
            ["group"] = Of("group", Lettering.Wedge, "text_gold"),
            ["label"] = Of("label", Lettering.Wedge, "text"),
            ["body"] = Of("body", Lettering.Text, "text"),
            ["value"] = Of("value", Lettering.Text, "text"),
            ["note"] = Of("note", Lettering.Text, "text_muted"),
            ["number"] = Of("note", Lettering.Knife, "text_muted"),
            ["warn"] = Of("warn", Lettering.TextSemiBold, "text_warn"),
            // A warning the player may accept (the modules' problems list): the warning's face in the kit's gold, apart from an error's red.
            ["caution"] = Of("warn", Lettering.TextSemiBold, "text_caution"),   // a risk the player may take: the kit's amber
            ["rules"] = Of("note", Lettering.TextSemiBold, "text_gold"),       // a rules module's tag (not a warning: the gold)
            ["tab"] = Of("tab", Lettering.Wedge, "text_gold"),
            ["button"] = Of("button", Lettering.Wedge, "text"),
            ["buttonSmall"] = Of("buttonSmall", Lettering.Wedge, "text_gold"),
        };
    }
}
