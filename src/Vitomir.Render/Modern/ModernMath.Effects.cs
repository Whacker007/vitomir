// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Render.Modern;

/// <summary>
/// The arithmetic of the modern path's lighting and screen effects (ours), the C# twins of what the
/// shaders do, kept in step by hand so it can be tested without a GPU: AgX, the bloom's soft threshold,
/// the haze's phase function, ground-truth ambient occlusion's arc integral and the lens flare's ghosts.
/// </summary>
public static partial class ModernMath
{
    /// <summary>
    /// AgX (Troy Sobotka's, in Benjamin Wrensch's minimal fit; ModernTonemap.hlsl): into AgX's working
    /// space, a log2 encoding over 16.5 stops (12.47 below mid-grey, 4.03 above), <paramref name="contrast"/>
    /// round mid-grey in that encoding (Blender's AgX looks: 1 base, about 1.2 "medium high"), the sigmoid,
    /// and back to linear light, 0..1.
    /// </summary>
    public static Vector3 AgX(Vector3 v, float contrast = 1f)
    {
        v = new Vector3(
            0.842479062253094f * v.X + 0.0784335999999992f * v.Y + 0.0792237451477643f * v.Z,
            0.0423282422610123f * v.X + 0.878468636469772f * v.Y + 0.0791661274605434f * v.Z,
            0.0423756549057051f * v.X + 0.0784336f * v.Y + 0.879142973793104f * v.Z);
        const float minEv = -12.47393f, maxEv = 4.026069f;
        float pivot = (MathF.Log2(0.18f) - minEv) / (maxEv - minEv);
        static float Curve(float x, float minEv, float maxEv, float pivot, float contrast)
        {
            x = (Math.Clamp(MathF.Log2(MathF.Max(x, 1e-10f)), minEv, maxEv) - minEv) / (maxEv - minEv);
            x = Math.Clamp(pivot + (x - pivot) * contrast, 0f, 1f);
            float x2 = x * x, x4 = x2 * x2;
            return 15.5f * x4 * x2 - 40.14f * x4 * x + 31.96f * x4 - 6.868f * x2 * x + 0.4298f * x2 + 0.1191f * x - 0.00232f;
        }
        v = new Vector3(Curve(v.X, minEv, maxEv, pivot, contrast), Curve(v.Y, minEv, maxEv, pivot, contrast), Curve(v.Z, minEv, maxEv, pivot, contrast));
        v = new Vector3(
            1.19687900512017f * v.X - 0.0980208811401368f * v.Y - 0.0990297440797205f * v.Z,
            -0.0528968517574562f * v.X + 1.15190312990417f * v.Y - 0.0989611768448433f * v.Z,
            -0.0529716355144438f * v.X - 0.0980434501171241f * v.Y + 1.15107367264116f * v.Z);
        return new Vector3(MathF.Pow(Math.Clamp(v.X, 0f, 1f), 2.2f), MathF.Pow(Math.Clamp(v.Y, 0f, 1f), 2.2f), MathF.Pow(Math.Clamp(v.Z, 0f, 1f), 2.2f));
    }

    /// <summary>
    /// The bloom's soft-knee threshold (ModernBloom.hlsl): of a colour whose brightest channel is l,
    /// nothing passes below <paramref name="threshold"/> − knee, everything above threshold + knee loses
    /// just the threshold, and a quadratic joins the two, knee = threshold × <paramref name="knee"/>.
    /// A threshold of 0 passes all light, as a real lens's glare does.
    /// </summary>
    public static Vector3 BloomThreshold(Vector3 c, float threshold, float knee)
    {
        if (threshold <= 0f) return c;
        float k = MathF.Max(threshold * knee, 1e-4f);
        float l = MathF.Max(c.X, MathF.Max(c.Y, c.Z));
        float soft = Math.Clamp(l - threshold + k, 0f, 2f * k);
        soft = soft * soft / (4f * k);
        return c * MathF.Max(soft, l - threshold) / MathF.Max(l, 1e-4f);
    }

    /// <summary>Henyey and Greenstein's phase function: the share of scattered light per steradian at angle acos(<paramref name="mu"/>).</summary>
    public static float PhaseHg(float mu, float g)
    {
        float g2 = g * g;
        return (1f - g2) / (4f * MathF.PI * MathF.Pow(MathF.Max(1f + g2 - 2f * g * mu, 1e-4f), 1.5f));
    }

    /// <summary>
    /// Ground-truth ambient occlusion's visibility for one slice (ModernAo.hlsl): the view-facing normal
    /// projected into the slice makes angle <paramref name="n"/> with the view direction, and the highest
    /// horizons on the two sides are at angles <paramref name="h0"/> (negative side) and <paramref name="h1"/>;
    /// each is clamped to the normal's hemisphere and the cosine-weighted arc between them integrated in
    /// closed form. An open slice (horizons at n ± 90°) sees 1.
    /// </summary>
    public static float GtaoSlice(float n, float h0, float h1)
    {
        h0 = n + Math.Clamp(h0 - n, -MathF.PI / 2f, MathF.PI / 2f);
        h1 = n + Math.Clamp(h1 - n, -MathF.PI / 2f, MathF.PI / 2f);
        float cosN = MathF.Cos(n), sinN = MathF.Sin(n);
        float a0 = (cosN + 2f * h0 * sinN - MathF.Cos(2f * h0 - n)) * 0.25f;
        float a1 = (cosN + 2f * h1 * sinN - MathF.Cos(2f * h1 - n)) * 0.25f;
        return a0 + a1;
    }

    /// <summary>
    /// The Halton sequence in bases 2 and 3 at <paramref name="index"/> (from 1): evenly spread points in
    /// the unit square, the sub-pixel offsets temporal anti-aliasing cycles through.
    /// </summary>
    public static Vector2 Halton(int index)
    {
        static float Radical(int i, int b)
        {
            float f = 1f, r = 0f;
            for (; i > 0; i /= b) { f /= b; r += f * (i % b); }
            return r;
        }
        return new Vector2(Radical(index, 2), Radical(index, 3));
    }

    /// <summary>
    /// Where the lens flare's ghosts sit (ModernTonemap.hlsl): along the line from the sun through the
    /// picture's centre, at these multiples of the sun-to-centre step, in texture coordinates.
    /// </summary>
    public static readonly float[] FlareGhosts = [0.45f, 0.8f, 1.25f, 1.55f, 1.9f, 2.3f];

    /// <summary>The ghosts' centres for a sun at <paramref name="sun"/> (texture coordinates).</summary>
    public static Vector2[] FlarePositions(Vector2 sun) =>
        FlareGhosts.Select(f => sun + (new Vector2(0.5f) - sun) * f).ToArray();
}
