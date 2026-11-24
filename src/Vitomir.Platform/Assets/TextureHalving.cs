// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Moved here from the renderer's CharacterTextureSize, unchanged, so the people's library can cap a map
// as it is decoded with the very filter the renderer caps it with on upload.
namespace Vitomir.Platform.Assets;

/// <summary>
/// An RGBA8 image halved by a 2 × 2 box filter (an earlier build moved): a base colour averaged in linear light and encoded again,
/// a data map (normal, occlusion-roughness-metal) as it is stored, alpha linearly either way.
/// </summary>
public static class TextureHalving
{
    /// <summary><paramref name="image"/> halved until neither side is above <paramref name="limit"/> (itself when it fits).</summary>
    public static TextureImage Fit(TextureImage image, int limit, bool srgb)
    {
        while (image.Width > limit || image.Height > limit) image = Halve(image, srgb);
        return image;
    }

    /// <summary>One halving by a 2 × 2 box filter (an odd last row or column is folded into its neighbour's texel).</summary>
    public static TextureImage Halve(TextureImage image, bool srgb)
    {
        int w = image.Width, h = image.Height, nw = Math.Max(1, w / 2), nh = Math.Max(1, h / 2);
        var src = image.Rgba;
        var dst = new byte[nw * nh * 4];
        var toLinear = srgb ? ToLinear : null;
        Parallel.For(0, nh, y =>
        {
            int y0 = Math.Min(2 * y, h - 1), y1 = Math.Min(2 * y + 1, h - 1);
            for (int x = 0; x < nw; x++)
            {
                int x0 = Math.Min(2 * x, w - 1), x1 = Math.Min(2 * x + 1, w - 1);
                int a = (y0 * w + x0) * 4, b = (y0 * w + x1) * 4, c = (y1 * w + x0) * 4, d = (y1 * w + x1) * 4;
                int o = (y * nw + x) * 4;
                for (int k = 0; k < 3; k++)
                {
                    if (toLinear is not null)
                    {
                        float mean = (toLinear[src[a + k]] + toLinear[src[b + k]] + toLinear[src[c + k]] + toLinear[src[d + k]]) * 0.25f;
                        dst[o + k] = ToSrgb[(int)(mean * (ToSrgb.Length - 1) + 0.5f)];
                    }
                    else dst[o + k] = (byte)((src[a + k] + src[b + k] + src[c + k] + src[d + k] + 2) >> 2);
                }
                dst[o + 3] = (byte)((src[a + 3] + src[b + 3] + src[c + 3] + src[d + 3] + 2) >> 2);
            }
        });
        return new TextureImage(image.Name, nw, nh, dst);
    }

    /// <summary>An sRGB byte's linear value (the sRGB transfer function).</summary>
    static readonly float[] ToLinear = MakeToLinear();
    /// <summary>A linear value's sRGB byte, by 4096 steps of linear light.</summary>
    static readonly byte[] ToSrgb = MakeToSrgb();

    static float[] MakeToLinear()
    {
        var t = new float[256];
        for (int i = 0; i < 256; i++)
        {
            double c = i / 255.0;
            t[i] = (float)(c <= 0.04045 ? c / 12.92 : Core.DetMath.PowD((c + 0.055) / 1.055, 2.4));
        }
        return t;
    }

    static byte[] MakeToSrgb()
    {
        var t = new byte[4096];
        for (int i = 0; i < t.Length; i++)
        {
            double l = i / (double)(t.Length - 1);
            double s = l <= 0.0031308 ? l * 12.92 : 1.055 * Core.DetMath.PowD(l, 1 / 2.4) - 0.055;
            t[i] = (byte)Math.Clamp((int)Math.Round(s * 255.0), 0, 255);
        }
        return t;
    }
}
