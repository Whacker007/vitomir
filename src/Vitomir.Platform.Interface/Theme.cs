// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Platform.Interface;

/// <summary>A named way of setting words: which lettering, how big (an em in units), what colour, how dressed.</summary>
public readonly record struct TextStyle(Lettering Lettering, float Em, Rgba Colour, TextLook Look, float LineHeight);

/// <summary>
/// Vitomir's own look for its screens (ours: <i>"original but following somewhat the
/// same aesthetic"</i> as 1.1's): dark, warm panels edged in bronze, names in the uncial in parchment and gold, values
/// in the book face, and the chosen item lit with a red glow (2a). Screens name these colours and styles; a name
/// the theme does not have is said once and the plainest is used.
/// </summary>
public static class Theme
{
    static readonly Rgba Black = new(0f, 0f, 0f, 1f);

    /// <summary>The colours a screen may name instead of writing <c>#rrggbb</c>.</summary>
    public static readonly IReadOnlyDictionary<string, Rgba> Colours = new Dictionary<string, Rgba>(StringComparer.Ordinal)
    {
        ["veil"] = new(0.02f, 0.01f, 0.01f, 0.66f),
        ["panel"] = new(0.067f, 0.045f, 0.038f, 0.94f),
        ["panelEdge"] = new(0.40f, 0.29f, 0.18f, 1f),
        ["panelInner"] = new(0.22f, 0.15f, 0.10f, 1f),
        ["rule"] = new(0.30f, 0.21f, 0.14f, 1f),
        ["band"] = new(0.10f, 0.07f, 0.055f, 0.9f),
        ["bandAlt"] = new(0.085f, 0.058f, 0.048f, 0.9f),
        ["button"] = new(0.11f, 0.075f, 0.06f, 1f),
        ["buttonEdge"] = new(0.33f, 0.23f, 0.15f, 1f),
        ["chosen"] = new(0.20f, 0.07f, 0.05f, 1f),
        ["chosenEdge"] = new(0.72f, 0.20f, 0.14f, 1f),
        ["pressed"] = new(0.28f, 0.07f, 0.05f, 1f),
        ["cell"] = new(0.045f, 0.03f, 0.025f, 1f),
        ["cellEdge"] = new(0.26f, 0.18f, 0.12f, 1f),
        ["waiting"] = new(0.30f, 0.06f, 0.05f, 1f),
        ["waitingEdge"] = new(0.90f, 0.30f, 0.20f, 1f),
        ["lost"] = new(0.27f, 0.19f, 0.05f, 1f),
        ["lostEdge"] = new(0.88f, 0.66f, 0.25f, 1f),
        ["track"] = new(0.17f, 0.12f, 0.09f, 1f),
        ["trackFill"] = new(0.55f, 0.17f, 0.12f, 1f),
        ["knob"] = new(0.85f, 0.72f, 0.48f, 1f),
        ["gold"] = new(0.85f, 0.72f, 0.48f, 1f),
        ["parchment"] = new(0.86f, 0.81f, 0.71f, 1f),
        ["bright"] = new(0.98f, 0.92f, 0.80f, 1f),
        ["value"] = new(0.91f, 0.89f, 0.84f, 1f),
        ["muted"] = new(0.62f, 0.56f, 0.47f, 1f),
        ["amber"] = new(0.93f, 0.72f, 0.33f, 1f),
        ["none"] = new(0f, 0f, 0f, 0f),
        // The stand-in HUD: its plate, the vitals' bars, and the quick slots' looks by state.
        ["hudPlate"] = new(0.05f, 0.035f, 0.03f, 0.72f),
        ["health"] = new(0.70f, 0.17f, 0.11f, 1f),
        ["blood"] = new(0.44f, 0.03f, 0.12f, 1f),
        ["slotReady"] = new(0.10f, 0.07f, 0.055f, 0.86f),
        ["slotLive"] = new(0.27f, 0.07f, 0.05f, 0.90f),
        ["slotDim"] = new(0.045f, 0.03f, 0.025f, 0.62f),
        ["slotShort"] = new(0.16f, 0.05f, 0.08f, 0.86f),
        ["slotTime"] = new(0.90f, 0.30f, 0.20f, 1f),
    };

    static readonly TextLook Dark = new(Black.WithAlpha(0.8f), 0.05f, Black.WithAlpha(0.7f), new Vector2(0.05f, 0.06f), 0.10f, default, 0f);
    static readonly TextLook Soft = new(Black.WithAlpha(0.6f), 0.035f, Black.WithAlpha(0.5f), new Vector2(0.04f, 0.05f), 0.08f, default, 0f);

    /// <summary>The look of the chosen item's words: the dark rim and a red glow round them (2a).</summary>
    public static readonly TextLook Chosen = new(Black.WithAlpha(0.85f), 0.05f, Black.WithAlpha(0.6f), new Vector2(0.04f, 0.05f), 0.08f,
                                                 new Rgba(0.85f, 0.10f, 0.06f, 0.75f), 0.22f);

    /// <summary>The styles a screen's words may name.</summary>
    public static readonly IReadOnlyDictionary<string, TextStyle> Styles = new Dictionary<string, TextStyle>(StringComparer.Ordinal)
    {
        // A page's title, and a section's heading.
        ["title"] = new(Lettering.Name, 84f, Colours["gold"], Dark, 1.25f),
        ["heading"] = new(Lettering.Name, 46f, Colours["gold"], Dark, 1.3f),
        // A button's words, an action's name, a setting's name.
        ["button"] = new(Lettering.Name, 42f, Colours["parchment"], Soft, 1.3f),
        ["label"] = new(Lettering.Name, 34f, Colours["parchment"], Soft, 1.3f),
        // A group's heading inside a list (Quick Slots on the Controls page).
        ["group"] = new(Lettering.Name, 36f, Colours["gold"], Dark, 1.3f),
        // The heads of a table's columns.
        ["column"] = new(Lettering.Name, 27f, Colours["muted"], Soft, 1.3f),
        // A key's name, a number: the book face.
        ["value"] = new(Lettering.Sentence, 27f, Colours["value"], Soft, 1.3f),
        // A line of help, and a line saying what changed.
        ["hint"] = new(Lettering.Sentence, 25f, Colours["muted"], Soft, 1.4f),
        ["message"] = new(Lettering.Sentence, 27f, Colours["amber"], Soft, 1.4f),
        // The stand-in HUD: a slot's key and state in the book face, its power's name and the vitals' names in the
        // uncial, their numbers in the book face, the masquerade's mode.
        ["hudKey"] = new(Lettering.Sentence, 21f, Colours["gold"], Dark, 1.2f),
        ["hudPower"] = new(Lettering.Name, 27f, Colours["parchment"], Dark, 1.2f),
        ["hudState"] = new(Lettering.Sentence, 20f, Colours["value"], Dark, 1.2f),
        ["hudName"] = new(Lettering.Name, 26f, Colours["parchment"], Dark, 1.2f),
        ["hudValue"] = new(Lettering.Sentence, 22f, Colours["value"], Dark, 1.2f),
        ["hudMode"] = new(Lettering.Name, 27f, Colours["parchment"], Dark, 1.2f),
        // The launcher (vitomir-launcher.exe): a page's running words in the book face, a smaller
        // note, a warning, and a smaller button for a row's own.
        ["body"] = new(Lettering.Sentence, 30f, Colours["value"], Soft, 1.4f),
        ["note"] = new(Lettering.Sentence, 26f, Colours["muted"], Soft, 1.4f),
        ["warn"] = new(Lettering.Sentence, 30f, Colours["amber"], Soft, 1.4f),
        ["buttonSmall"] = new(Lettering.Name, 32f, Colours["parchment"], Soft, 1.3f),
        ["tab"] = new(Lettering.Name, 40f, Colours["parchment"], Soft, 1.3f),
    };

    public const string DefaultStyle = "label";
}
