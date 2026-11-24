// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Levels.Cells;

/// <summary>What a map holds, which decides how it is filtered into mips and compressed.</summary>
public enum TextureKind
{
    /// <summary>Colour (base colour, emission): mips averaged in linear light, BC7 sRGB.</summary>
    Colour,
    /// <summary>Data (metal-roughness, occlusion): mips averaged as numbers, BC7 linear.</summary>
    Data,
    /// <summary>A tangent-space normal map: mips averaged as vectors and made unit again, X and Y in BC5.</summary>
    Normal,
    /// <summary>A height map (ours; the grey image's red channel, 1 the highest): mips averaged as numbers, BC4.</summary>
    Height,
}

/// <summary>
/// **Ours (Phase 3): the texture cook** — a material map (RGBA8, as the glTF reader decodes it) into a
/// KTX2 file with a full mip chain, block-compressed: BC7 for colour and data (<see cref="Bc7Mode6"/>,
/// ours), BC5 for normals and BC4 for height maps (BCnEncoder.Net, MIT; <c>docs/THIRD_PARTY.md</c>; a
/// normal's Z is rebuilt from X and Y by the renderer). The mips are made here, each from the one above by a 2 × 2 box — in linear
/// light for colour (the sRGB curve undone and done again), renormalised for normals — and each is
/// compressed on its own.
/// </summary>
public static class TextureCook
{
    /// <summary>The BC5 encoder's effort (<see cref="CompressionQuality.Balanced"/> unless the cook is told otherwise).</summary>
    public static CompressionQuality Quality { get; set; } = CompressionQuality.Balanced;

    public static Ktx2Format FormatFor(TextureKind kind) => kind switch
    {
        TextureKind.Colour => Ktx2Format.Bc7Srgb,
        TextureKind.Normal => Ktx2Format.Bc5Unorm,
        TextureKind.Height => Ktx2Format.Bc4Unorm,
        _ => Ktx2Format.Bc7Unorm,
    };

    /// <summary>
    /// The whole chain, compressed: a KTX2 file's model. With <paramref name="alphaCutoff"/> (a cut-out
    /// material's: <c>MASK</c>, the base colour's alpha tested against it), each mip's alpha is scaled so the
    /// share of its texels that pass the test is the full picture's (<see cref="KeepCoverage"/>).
    /// </summary>
    public static Ktx2 Cook(byte[] rgba, int width, int height, TextureKind kind, float? alphaCutoff = null)
    {
        float cutoff = alphaCutoff is { } c && c > 0f && c < 1f ? c : 0f;
        float coverage = cutoff > 0f ? Coverage(rgba, cutoff) : 0f;
        var format = FormatFor(kind);
        var encoder = new BcEncoder(format == Ktx2Format.Bc4Unorm ? CompressionFormat.Bc4 : CompressionFormat.Bc5);
        encoder.OutputOptions.GenerateMipMaps = false;
        encoder.OutputOptions.Quality = Quality;
        encoder.Options.IsParallel = false;
        var levels = new List<byte[]>();
        var image = rgba; int w = width, h = height;
        int count = Ktx2.FullChain(width, height);
        for (int l = 0; l < count; l++)
        {
            // A cut-out map's mips keep its cover; each is made from the one above as it was, not as scaled.
            var level = cutoff > 0f && l > 0 ? KeepCoverage(image, cutoff, coverage) : image;
            var bytes = format is Ktx2Format.Bc5Unorm or Ktx2Format.Bc4Unorm ? encoder.EncodeToRawBytes(level, w, h, PixelFormat.Rgba32, 0, out _, out _)
                                                                             : Bc7Mode6.Encode(level, w, h);
            int want = Ktx2.LevelBytes(format, width, height, l);
            if (bytes.Length != want) throw new InvalidOperationException($"BC encoder gave {bytes.Length} bytes for a {w} × {h} level, not {want}");
            levels.Add(bytes);
            if (l + 1 < count) (image, w, h) = Half(image, w, h, kind);
        }
        return new Ktx2 { Format = format, Width = width, Height = height, Levels = levels };
    }

    /// <summary>The share of an RGBA8 picture's texels whose alpha passes <paramref name="cutoff"/> (0–1).</summary>
    public static float Coverage(byte[] rgba, float cutoff)
    {
        int n = rgba.Length / 4, pass = 0;
        for (int i = 0; i < n; i++) if (rgba[4 * i + 3] / 255f >= cutoff) pass++;
        return n > 0 ? pass / (float)n : 0f;
    }

    /// <summary>
    /// **Ours: a cut-out mip that keeps its cover** (after Ignacio Castaño, "Computing alpha
    /// mipmaps", 2010). Averaging alpha down the chain thins a cut-out picture — leaves, grass, ivy — until
    /// in the small mips almost nothing passes the alpha test, and a tree seen from a hundred metres is bare
    /// twigs. Here the mip's alpha is scaled by the one factor that makes the share of its texels passing
    /// <paramref name="cutoff"/> as near <paramref name="coverage"/> (the full picture's) as its values
    /// allow: the texel at the alpha that many texels reach is brought up (or down) to the cutoff. A copy;
    /// colour untouched.
    /// </summary>
    public static byte[] KeepCoverage(byte[] rgba, float cutoff, float coverage)
    {
        int n = rgba.Length / 4;
        if (n == 0 || coverage <= 0f || coverage >= 1f) return rgba;
        var count = new int[256];
        for (int i = 0; i < n; i++) count[rgba[4 * i + 3]]++;
        // The alpha t (1–255) whose share of texels at or above it is nearest the cover wanted.
        float want = coverage * n;
        int t = 255, best = 255, above = 0;
        float bestErr = float.MaxValue;
        for (; t >= 1; t--)
        {
            above += count[t];
            float err = MathF.Abs(above - want);
            if (err < bestErr) { bestErr = err; best = t; }
            if (above > want) break;
        }
        float scale = cutoff * 255f / best;
        if (MathF.Abs(scale - 1f) < 1e-3f) return rgba;
        var dst = (byte[])rgba.Clone();
        // Rounded up, so a texel at t passes and one below it does not.
        for (int i = 0; i < n; i++) dst[4 * i + 3] = (byte)Math.Clamp((int)MathF.Ceiling(rgba[4 * i + 3] * scale - 1e-3f), 0, 255);
        return dst;
    }

    /// <summary>The next mip: each 2 × 2 (or 2 × 1 at an odd edge) averaged by the kind's rule.</summary>
    public static (byte[] Rgba, int W, int H) Half(byte[] src, int w, int h, TextureKind kind)
    {
        int nw = Math.Max(1, w / 2), nh = Math.Max(1, h / 2);
        var dst = new byte[nw * nh * 4];
        Span<float> sum = stackalloc float[4];
        for (int y = 0; y < nh; y++)
            for (int x = 0; x < nw; x++)
            {
                sum.Clear();
                int n = 0;
                for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int sx = Math.Min(w - 1, 2 * x + dx), sy = Math.Min(h - 1, 2 * y + dy);
                        int at = (sy * w + sx) * 4;
                        for (int c = 0; c < 4; c++)
                        {
                            float v = src[at + c] / 255f;
                            if (kind == TextureKind.Colour && c < 3) v = ToLinear(v);
                            else if (kind == TextureKind.Normal && c < 3) v = v * 2f - 1f;
                            sum[c] += v;
                        }
                        n++;
                    }
                int o = (y * nw + x) * 4;
                if (kind == TextureKind.Normal)
                {
                    float len = MathF.Sqrt(sum[0] * sum[0] + sum[1] * sum[1] + sum[2] * sum[2]);
                    if (len < 1e-6f) { sum[0] = 0; sum[1] = 0; sum[2] = 1; len = 1; }
                    for (int c = 0; c < 3; c++) dst[o + c] = Byte((sum[c] / len) * 0.5f + 0.5f);
                    dst[o + 3] = Byte(sum[3] / n);
                    continue;
                }
                for (int c = 0; c < 4; c++)
                {
                    float v = sum[c] / n;
                    if (kind == TextureKind.Colour && c < 3) v = ToSrgb(v);
                    dst[o + c] = Byte(v);
                }
            }
        return (dst, nw, nh);
    }

    static byte Byte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
    static float ToLinear(float s) => s <= 0.04045f ? s / 12.92f : MathF.Pow((s + 0.055f) / 1.055f, 2.4f);
    static float ToSrgb(float l) => l <= 0.0031308f ? l * 12.92f : 1.055f * MathF.Pow(l, 1f / 2.4f) - 0.055f;
}
