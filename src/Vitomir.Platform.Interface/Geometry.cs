// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;

namespace Vitomir.Platform.Interface;

/// <summary>
/// A rectangle in units of the interface (ours): a screen is 1440 units high, so on 2560 × 1440 a unit is a
/// pixel. X to the right, Y down, from the screen's top-left corner.
/// </summary>
public readonly record struct UiRect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public bool Contains(float x, float y) => x >= X && x < Right && y >= Y && y < Bottom;
    public UiRect Inset(float by) => new(X + by, Y + by, MathF.Max(0f, Width - 2f * by), MathF.Max(0f, Height - 2f * by));
    public UiRect Inset(Edges e) => new(X + e.Left, Y + e.Top, MathF.Max(0f, Width - e.Left - e.Right), MathF.Max(0f, Height - e.Top - e.Bottom));

    /// <summary>The part of this rectangle inside <paramref name="other"/> (empty when they do not meet).</summary>
    public UiRect Intersect(UiRect other)
    {
        float x0 = MathF.Max(X, other.X), y0 = MathF.Max(Y, other.Y);
        float x1 = MathF.Min(Right, other.Right), y1 = MathF.Min(Bottom, other.Bottom);
        return new UiRect(x0, y0, MathF.Max(0f, x1 - x0), MathF.Max(0f, y1 - y0));
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"({X:0.##}, {Y:0.##}, {Width:0.##} × {Height:0.##})");
}

/// <summary>Space on each side of a box, in units.</summary>
public readonly record struct Edges(float Left, float Top, float Right, float Bottom)
{
    public static Edges All(float v) => new(v, v, v, v);
    public float Horizontal => Left + Right;
    public float Vertical => Top + Bottom;
}

/// <summary>A rectangle in whole pixels of the picture (what the renderer draws and scissors by).</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool Empty => Width <= 0 || Height <= 0;
}

/// <summary>A colour, straight (not premultiplied) alpha, 0 to 1 each.</summary>
public readonly record struct Rgba(float R, float G, float B, float A = 1f)
{
    public Rgba WithAlpha(float a) => this with { A = a };

    /// <summary><c>#rrggbb</c> or <c>#rrggbbaa</c>; null when it is neither.</summary>
    public static Rgba? Parse(string? s)
    {
        if (s is null || s.Length is not (7 or 9) || s[0] != '#') return null;
        if (!uint.TryParse(s.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v)) return null;
        if (s.Length == 7) v = (v << 8) | 0xFF;
        return new Rgba(((v >> 24) & 0xFF) / 255f, ((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f);
    }
}

/// <summary>
/// How units become pixels on a screen of <see cref="PixelWidth"/> × <see cref="PixelHeight"/> at the player's
/// own interface <see cref="PlayerScale"/> (75 % to 150 %): a unit is <c>height / 1440 × scale</c> pixels, and the
/// screen is <see cref="Width"/> units wide, so a wider screen gets more room, not stretched pieces.
/// </summary>
public readonly record struct UiMetrics(int PixelWidth, int PixelHeight, float PlayerScale = 1f)
{
    /// <summary>The height of the design, in units (designed natively at 2560 × 1440).</summary>
    public const float DesignHeight = 1440f;

    public const float MinScale = 0.75f, MaxScale = 1.5f;

    /// <summary>Pixels to a unit.</summary>
    public float Pixel => PixelHeight / DesignHeight * Math.Clamp(PlayerScale, MinScale, MaxScale);

    /// <summary>The screen's size in units.</summary>
    public float Width => PixelWidth / Pixel;
    public float Height => PixelHeight / Pixel;

    /// <summary>A rectangle in units to whole pixels: each edge rounded on its own, so neighbours meet without a gap.</summary>
    public PixelRect ToPixels(UiRect r)
    {
        float p = Pixel;
        int x0 = (int)MathF.Round(r.X * p), y0 = (int)MathF.Round(r.Y * p);
        int x1 = (int)MathF.Round(r.Right * p), y1 = (int)MathF.Round(r.Bottom * p);
        return new PixelRect(x0, y0, x1 - x0, y1 - y0);
    }
}
