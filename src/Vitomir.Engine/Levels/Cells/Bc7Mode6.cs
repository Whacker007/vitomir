// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Engine.Levels.Cells;

/// <summary>
/// **Ours (Phase 3): a BC7 encoder that writes mode 6 only** — every 4 × 4 block as one line through
/// RGBA space, its two ends 7 bits a channel plus a shared low bit each (the "p-bits"), and sixteen
/// 4-bit steps along it (Microsoft, "BC7 format", and the Khronos Data Format Specification, section
/// 21.2). One mode of eight is a legal BC7 file every decoder reads; it is the mode that suits smooth
/// colour and data best, and it is quick: the line is the block's principal axis (power iteration on
/// its covariance), its ends the projections' extremes, both p-bit choices tried, then the ends fitted
/// again by least squares to the steps chosen and kept if the block comes out closer. Written because
/// the NuGet encoder at hand takes about a minute over one 1024 × 1024 map; this takes well under a second.
/// </summary>
public static class Bc7Mode6
{
    /// <summary>Mode 6's sixteen interpolation weights, out of 64.</summary>
    static readonly int[] Weights = [0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64];

    /// <summary>
    /// A whole image (RGBA8, <paramref name="width"/> × <paramref name="height"/>, any size — the edge
    /// blocks repeat their last pixels) as BC7 blocks, left to right, top to bottom, 16 bytes each.
    /// </summary>
    public static byte[] Encode(ReadOnlySpan<byte> rgba, int width, int height)
    {
        int bw = (width + 3) / 4, bh = (height + 3) / 4;
        var output = new byte[bw * bh * 16];
        var pixels = new Vector4[16];
        for (int by = 0; by < bh; by++)
            for (int bx = 0; bx < bw; bx++)
            {
                for (int i = 0; i < 16; i++)
                {
                    int x = Math.Min(width - 1, bx * 4 + (i & 3)), y = Math.Min(height - 1, by * 4 + (i >> 2));
                    int o = (y * width + x) * 4;
                    pixels[i] = new Vector4(rgba[o], rgba[o + 1], rgba[o + 2], rgba[o + 3]);
                }
                EncodeBlock(pixels, output.AsSpan((by * bw + bx) * 16, 16));
            }
        return output;
    }

    /// <summary>One block: sixteen pixels (0–255 a channel) into sixteen bytes.</summary>
    public static void EncodeBlock(ReadOnlySpan<Vector4> px, Span<byte> block)
    {
        var mean = Vector4.Zero;
        for (int i = 0; i < 16; i++) mean += px[i];
        mean /= 16f;
        // The principal axis, by power iteration on the covariance.
        Span<float> cov = stackalloc float[10];
        for (int i = 0; i < 16; i++)
        {
            var d = px[i] - mean;
            cov[0] += d.X * d.X; cov[1] += d.X * d.Y; cov[2] += d.X * d.Z; cov[3] += d.X * d.W;
            cov[4] += d.Y * d.Y; cov[5] += d.Y * d.Z; cov[6] += d.Y * d.W;
            cov[7] += d.Z * d.Z; cov[8] += d.Z * d.W; cov[9] += d.W * d.W;
        }
        var axis = new Vector4(1, 1, 1, 0.5f);
        for (int it = 0; it < 8; it++)
        {
            var n = new Vector4(
                cov[0] * axis.X + cov[1] * axis.Y + cov[2] * axis.Z + cov[3] * axis.W,
                cov[1] * axis.X + cov[4] * axis.Y + cov[5] * axis.Z + cov[6] * axis.W,
                cov[2] * axis.X + cov[5] * axis.Y + cov[7] * axis.Z + cov[8] * axis.W,
                cov[3] * axis.X + cov[6] * axis.Y + cov[8] * axis.Z + cov[9] * axis.W);
            float len = n.Length();
            if (len < 1e-8f) break;
            axis = n / len;
        }
        float lo = float.MaxValue, hi = float.MinValue;
        for (int i = 0; i < 16; i++) { float t = Vector4.Dot(px[i] - mean, axis); lo = MathF.Min(lo, t); hi = MathF.Max(hi, t); }
        var e0 = Vector4.Clamp(mean + axis * lo, Vector4.Zero, new Vector4(255));
        var e1 = Vector4.Clamp(mean + axis * hi, Vector4.Zero, new Vector4(255));

        Span<int> idx = stackalloc int[16];
        Span<int> bestIdx = stackalloc int[16];
        var (q0, q1, err) = Quantise(px, e0, e1, idx);
        idx.CopyTo(bestIdx);
        // Fit the ends again to the steps chosen (least squares on the weights) and keep it if closer.
        for (int pass = 0; pass < 2; pass++)
        {
            float aa = 0, ab = 0, bb = 0; Vector4 ax = Vector4.Zero, bx = Vector4.Zero;
            for (int i = 0; i < 16; i++)
            {
                float w = Weights[bestIdx[i]] / 64f, a = 1f - w;
                aa += a * a; ab += a * w; bb += w * w; ax += a * px[i]; bx += w * px[i];
            }
            float det = aa * bb - ab * ab;
            if (MathF.Abs(det) < 1e-6f) break;
            var f0 = Vector4.Clamp((ax * bb - bx * ab) / det, Vector4.Zero, new Vector4(255));
            var f1 = Vector4.Clamp((bx * aa - ax * ab) / det, Vector4.Zero, new Vector4(255));
            var (r0, r1, e) = Quantise(px, f0, f1, idx);
            if (e >= err) break;
            (q0, q1, err) = (r0, r1, e);
            idx.CopyTo(bestIdx);
        }
        Pack(q0, q1, bestIdx, block);
    }

    /// <summary>
    /// The ends quantised (7 bits a channel and a p-bit, each p-bit choice tried) and each pixel's
    /// nearest step: the ends as their 8-bit values' codes (c7, p), and the squared error.
    /// </summary>
    static ((int R, int G, int B, int A, int P) Q0, (int R, int G, int B, int A, int P) Q1, float Error) Quantise(
        ReadOnlySpan<Vector4> px, Vector4 e0, Vector4 e1, Span<int> idx)
    {
        float best = float.MaxValue;
        (int, int, int, int, int) b0 = default, b1 = default;
        Span<int> tmp = stackalloc int[16];
        Span<Vector4> palette = stackalloc Vector4[16];
        for (int p0 = 0; p0 < 2; p0++)
            for (int p1 = 0; p1 < 2; p1++)
            {
                var c0 = Code(e0, p0); var c1 = Code(e1, p1);
                var v0 = Value(c0); var v1 = Value(c1);
                for (int k = 0; k < 16; k++)
                {
                    int w = Weights[k];
                    palette[k] = new Vector4(((64 - w) * v0.X + w * v1.X + 32) >> 6, ((64 - w) * v0.Y + w * v1.Y + 32) >> 6,
                                             ((64 - w) * v0.Z + w * v1.Z + 32) >> 6, ((64 - w) * v0.W + w * v1.W + 32) >> 6);
                }
                float total = 0;
                for (int i = 0; i < 16; i++)
                {
                    float bd = float.MaxValue; int bk = 0;
                    for (int k = 0; k < 16; k++)
                    {
                        float d = Vector4.DistanceSquared(px[i], palette[k]);
                        if (d < bd) { bd = d; bk = k; }
                    }
                    tmp[i] = bk; total += bd;
                }
                if (total < best) { best = total; b0 = c0; b1 = c1; tmp.CopyTo(idx); }
            }
        return (b0, b1, best);
    }

    static (int R, int G, int B, int A, int P) Code(Vector4 v, int p)
    {
        static int C(float x, int p) => Math.Clamp((int)MathF.Round((x - p) / 2f), 0, 127);
        return (C(v.X, p), C(v.Y, p), C(v.Z, p), C(v.W, p), p);
    }

    static (int X, int Y, int Z, int W) Value((int R, int G, int B, int A, int P) c) =>
        ((c.R << 1) | c.P, (c.G << 1) | c.P, (c.B << 1) | c.P, (c.A << 1) | c.P);

    /// <summary>
    /// The block's 128 bits, least significant first: mode 6 (six 0 bits and a 1), R0 R1 G0 G1 B0 B1 A0 A1
    /// (7 bits each), the two p-bits, then the sixteen indices, the first with 3 bits — its top bit is
    /// implied 0, so if it would be set the ends swap and every index turns round (15 − i).
    /// </summary>
    static void Pack((int R, int G, int B, int A, int P) q0, (int R, int G, int B, int A, int P) q1, Span<int> idx, Span<byte> block)
    {
        if (idx[0] >= 8)
        {
            (q0, q1) = (q1, q0);
            for (int i = 0; i < 16; i++) idx[i] = 15 - idx[i];
        }
        UInt128 bits = 0;
        int at = 0;
        void Put(int value, int count) { bits |= (UInt128)(uint)value << at; at += count; }
        Put(1 << 6, 7);
        Put(q0.R, 7); Put(q1.R, 7); Put(q0.G, 7); Put(q1.G, 7); Put(q0.B, 7); Put(q1.B, 7); Put(q0.A, 7); Put(q1.A, 7);
        Put(q0.P, 1); Put(q1.P, 1);
        Put(idx[0], 3);
        for (int i = 1; i < 16; i++) Put(idx[i], 4);
        for (int i = 0; i < 16; i++) block[i] = (byte)(bits >> (8 * i));
    }
}
