// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Shaders/ModernHex.hlsli at b4185ef, reworked for the platform.
// Hex tiling (ours): hex-tile stochastic sampling after Mikkelsen,
// "Practical Real-Time Hex-Tiling" (JCGT 11(2), 2022), itself after Heitz and Neyret, "High-Performance By-Example
// Noise using a Histogram-Preserving Blending Operator" (2018). The texture coordinates are covered by a lattice of
// triangles whose corners are the centres of hexagonal cells; each cell shows the maps at a random offset (and, if
// asked, turned by a random angle about its centre), and a point blends the three cells at the corners of its
// triangle by its barycentric weights, sharpened (raised to the seventh power) and weighted by each sample's own
// height or brightness, so that where two cells meet the higher stones of either show and the seam runs through the
// joints. Every map of a material reads through the same cells and the same weights.
//
// Levels of detail: each cell keeps one of three slots wherever it is read — the lattice's corners are coloured
// 0, 1, 2 by (x − y) mod 3, so a triangle's three are always distinct (Mikkelsen's ProduceHexWeights) — so the
// screen's own derivatives of a slot's coordinates are those of one cell across a 2 × 2 quad, except where a quad
// straddles a triangle's edge, and there the slot that changes cell is the corner opposite the edge, whose weight
// is next to nothing. So the maps are read with implicit derivatives (anisotropic, as any map), every tap read.
// (Explicit gradients, Mikkelsen's SampleGrad, are filtered isotropically by lavapipe, the software Vulkan the
// review renders use: the ground blurred at a glancing angle.)

#ifndef MODERN_HEX_HLSLI
#define MODERN_HEX_HLSLI

// The lattice: texture units (times the cells per unit) into the skewed grid whose integer points are the cells'
// centres (Mikkelsen's TriangleGrid; neighbouring centres one unit apart), and back.
static const float2x2 HexSkew = float2x2(1.0, -0.57735027, 0.0, 1.15470054);
// Mikkelsen's sharpening of the barycentric weights.
static const float HexExponent = 7.0;
// How strongly a sample's height decides between cells (ours): its weight is 2^(sharpness · (height − 1)), so a
// stone's top (1) outweighs a joint (0) 256 times.
static const float HexHeightSharpness = 8.0;
// How strongly a sample's brightness does, for a material without a height map (Mikkelsen's fall-off contrast).
static const float HexLumaContrast = 0.6;

// The three cells round a point: the lattice's triangle (its skewed square and which half), the slot each of its
// corners takes (e1..e3, one-hot), and each slot's cell's transform of the texture coordinates, st' = r · st + o
// (a turn about the cell's centre and an offset).
struct HexFrame
{
    float k;
    float2 base;
    float s;
    float3 e1, e2, e3;
    float2x2 r1, r2, r3;
    float2 o1, o2, o3;
    float3 live;    // 1 for a slot whose cell can show at the pixel, 0 for one too far off to (parallax's march skips it)
};

// A lattice corner's slot, 0..2, as a one-hot vector.
float3 HexSlot(float2 v)
{
    float c = v.x - v.y;
    c -= 3.0 * floor(c / 3.0);
    return float3(c < 0.5 ? 1.0 : 0.0, c > 0.5 && c < 1.5 ? 1.0 : 0.0, c > 1.5 ? 1.0 : 0.0);
}

// Hashes without transcendentals (Hoskins's "hash without sine"), as the large-scale variation's.
float HexHash1(float2 p)
{
    float3 p3 = frac(p.xyx * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float2 HexHash2(float2 p)
{
    float3 p3 = frac(p.xyx * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

// A cell's transform: a random whole-tile offset, and a random turn (by `rotate` of a full circle) about its centre.
void HexCell(float2 v, float k, float rotate, out float2x2 r, out float2 o)
{
    float2 c = float2(v.x + 0.5 * v.y, 0.8660254 * v.y) / k;
    float a = (HexHash1(v + 17.0) - 0.5) * 6.2831853 * rotate;
    float cs = cos(a), sn = sin(a);
    r = float2x2(cs, -sn, sn, cs);
    o = c - mul(r, c) + HexHash2(v + 59.0);
}

// The barycentric weights of st in the frame's triangle (Mikkelsen's w1..w3), by slot. A point a little outside
// it — where parallax's march has carried the coordinates over an edge — keeps the same three cells, its weights
// clamped.
float3 HexBary(HexFrame f, float2 st)
{
    float2 sk = mul(HexSkew, st * f.k) - f.base;
    float z = 1.0 - sk.x - sk.y;
    float s2 = 2.0 * f.s - 1.0;
    float3 w = max(float3(-z * s2, f.s - sk.y * s2, f.s - sk.x * s2), 0.0);
    return (w.x * f.e1 + w.y * f.e2 + w.z * f.e3) * f.live;
}

// The cells round the point st, for k cells per texture unit (1 / the distance between their centres).
HexFrame HexFrameAt(float2 st, float k, float rotate)
{
    HexFrame f;
    f.k = k;
    float2 sk = mul(HexSkew, st * k);
    f.base = floor(sk);
    float2 fr = sk - f.base;
    f.s = fr.x + fr.y > 1.0 ? 1.0 : 0.0;
    float2 v1 = f.base + float2(f.s, f.s), v2 = f.base + float2(f.s, 1.0 - f.s), v3 = f.base + float2(1.0 - f.s, f.s);
    f.e1 = HexSlot(v1); f.e2 = HexSlot(v2); f.e3 = HexSlot(v3);
    // Each slot's corner: the one whose one-hot vector has it.
    float2 c1 = v1 * f.e1.x + v2 * f.e2.x + v3 * f.e3.x;
    float2 c2 = v1 * f.e1.y + v2 * f.e2.y + v3 * f.e3.y;
    float2 c3 = v1 * f.e1.z + v2 * f.e2.z + v3 * f.e3.z;
    HexCell(c1, k, rotate, f.r1, f.o1);
    HexCell(c2, k, rotate, f.r2, f.o2);
    HexCell(c3, k, rotate, f.r3, f.o3);
    // A corner whose barycentric weight is under 0.15 of the largest weighs under 2^-19 of it once sharpened — under
    // 1/256 of it whatever the heights — so it is left out of the blend at this pixel altogether (its weight 0
    // everywhere the pixel's march goes, which then need not read its height).
    f.live = 1.0;
    float3 b = HexBary(f, st);
    f.live = select(b >= 0.15 * max(b.x, max(b.y, b.z)), 1.0, 0.0);
    return f;
}

// The blend's weights from the barycentric ones and each cell's sample `v`: mode 1 its height, 2 its brightness,
// 0 none.
float3 HexWeights(float3 b, float3 v, float mode)
{
    float3 w = b * b * b;
    w = w * w * b;
    float3 d = mode > 1.5 ? lerp(1.0, v, HexLumaContrast) : mode > 0.5 ? exp2(HexHeightSharpness * (v - 1.0)) : float3(1.0, 1.0, 1.0);
    w *= d;
    return w / max(w.x + w.y + w.z, 1e-30);
}

float HexLuma(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }

// A slot's coordinates.
float2 HexAt1(HexFrame f, float2 st) { return mul(f.r1, st) + f.o1; }
float2 HexAt2(HexFrame f, float2 st) { return mul(f.r2, st) + f.o2; }
float2 HexAt3(HexFrame f, float2 st) { return mul(f.r3, st) + f.o3; }

// A map read through the cells: each slot's tap at its cell's coordinates. Every tap is read (no branch), so the
// screen's derivatives are there for each.
float4 HexSample(Texture2D<float4> tex, SamplerState smp, HexFrame f, float3 w, float2 st)
{
    return w.x * tex.Sample(smp, HexAt1(f, st)) + w.y * tex.Sample(smp, HexAt2(f, st)) + w.z * tex.Sample(smp, HexAt3(f, st));
}

// The three cells' samples of one channel (the height, for the blend's weights).
float3 HexTaps(Texture2D<float4> tex, SamplerState smp, HexFrame f, float2 st)
{
    return float3(tex.Sample(smp, HexAt1(f, st)).r, tex.Sample(smp, HexAt2(f, st)).r, tex.Sample(smp, HexAt3(f, st)).r);
}

// A normal map read through the cells: each cell's normal turned back into the surface's frame. The texture is
// turned by r (st' = r · st + o), so a gradient on the surface is rᵀ times the map's; the map's y runs up the image,
// against v (GltfReader's tangents), and flipping y on both sides of rᵀ gives r itself. Returns the blended x and y.
float2 HexNormalXY(Texture2D<float4> tex, SamplerState smp, HexFrame f, float3 w, float2 st)
{
    return w.x * mul(f.r1, tex.Sample(smp, HexAt1(f, st)).xy * 2.0 - 1.0)
         + w.y * mul(f.r2, tex.Sample(smp, HexAt2(f, st)).xy * 2.0 - 1.0)
         + w.z * mul(f.r3, tex.Sample(smp, HexAt3(f, st)).xy * 2.0 - 1.0);
}

#endif
