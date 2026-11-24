// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The interface's colours and our lettering's dress, as its passes give them (moved from Vitomir.Render's Overlay.cs and
// UiRenderer.cs; the passes emit into the draw list from the engine now).
using Vitomir.Platform.Interface;

namespace Vitomir.Engine.Ui;

/// <summary>One colour, as the interface and the overlay want it.</summary>
public readonly record struct Tint(float R, float G, float B, float A = 1f)
{
    public static readonly Tint White = new(1f, 1f, 1f);
    public static readonly Tint Grey = new(0.62f, 0.64f, 0.68f);
    public static readonly Tint Green = new(0.42f, 0.86f, 0.44f);
    public static readonly Tint Amber = new(0.98f, 0.76f, 0.24f);
    public static readonly Tint Red = new(0.95f, 0.34f, 0.32f);
    public static readonly Tint Blue = new(0.46f, 0.72f, 1f);
    public static readonly Tint Backdrop = new(0f, 0f, 0f, 0.55f);

    public Tint Fade(float a) => this with { A = A * a };

    /// <summary>The draw list's colour, the same four numbers.</summary>
    public Rgba Rgba => new(R, G, B, A);

    /// <summary>
    /// A packed 0xAARRGGBB, which is how the game's own UI carries a colour — the transcript's
    /// alternating white and `0xafafaf`, and a control's declared background.
    /// </summary>
    public static Tint From(uint argb) =>
        new(((argb >> 16) & 0xFF) / 255f, ((argb >> 8) & 0xFF) / 255f, (argb & 0xFF) / 255f,
            ((argb >> 24) & 0xFF) / 255f);
}

/// <summary>
/// **How a line of our lettering is dressed** (ours): an outline round the face, a soft shadow off it and a glow
/// round it, each optional (null for none). Sizes are fractions of the em, so a style looks the same at every size.
/// </summary>
public readonly record struct LetterStyle(Tint? Outline = null, float OutlineWidth = 0f,
                                          Tint? Shadow = null, float ShadowX = 0f, float ShadowY = 0f, float ShadowSoftness = 0f,
                                          Tint? Glow = null, float GlowReach = 0f)
{
    /// <summary>Plain: the face alone.</summary>
    public static readonly LetterStyle Plain = new();

    /// <summary>The draw list's look, the same numbers (a dress left out is a colour of all zeros, as the shader took it).</summary>
    public TextLook Look => new(Outline?.Rgba ?? default, OutlineWidth, Shadow?.Rgba ?? default, new System.Numerics.Vector2(ShadowX, ShadowY),
                                ShadowSoftness, Glow?.Rgba ?? default, GlowReach);
}
