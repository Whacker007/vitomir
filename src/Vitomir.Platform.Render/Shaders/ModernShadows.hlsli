// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Shaders/ModernShadows.hlsli at b4185ef, reworked for the platform.
// The sun's cascaded shadow maps, read (ours), and the clouds' shadows over them. The including shader declares
// ShadowTex (the depth atlas: four cascades, one per quarter) and ShadowSmp (a comparison sampler).

#ifndef MODERN_SHADOWS_HLSLI
#define MODERN_SHADOWS_HLSLI

float2 AtlasUv(float2 ndc, int cascade)
{
    float2 uv = ndc * float2(0.5, -0.5) + 0.5;
    float2 cell = float2(cascade & 1, cascade >> 1);
    return (uv + cell) * 0.5;
}

// Sixteen points of a Poisson disc of radius 1, for the soft (large-sun) filter.
static const float2 Poisson16[16] =
{
    float2(-0.9465, -0.1484), float2(-0.7431, 0.5353), float2(-0.5863, -0.5946), float2(-0.3946, 0.1143),
    float2(-0.2537, 0.8711), float2(-0.1714, -0.9493), float2(-0.0917, -0.3355), float2(0.0584, 0.4238),
    float2(0.1623, -0.6603), float2(0.2519, 0.0381), float2(0.3291, 0.8521), float2(0.4613, -0.3222),
    float2(0.5754, 0.4612), float2(0.6975, -0.7016), float2(0.8472, 0.1082), float2(0.9543, -0.2615),
};

// One cascade's light at a world point: a 4x4 grid of hardware 2x2 comparisons (a 5x5-texel tent-like
// filter) for a small sun; for a large one (a look's soft overcast sun) a rotated Poisson disc as wide
// as the penumbra, ShadowSoft.x in world units.
float SampleCascade(float3 world, float3 n, int c, float2 pixel)
{
    float3 p = world + n * CascadeTexel[c] * ShadowParams.y;
    float4 lp = mul(Cascade[c], float4(p, 1.0));
    float3 ndc = lp.xyz / lp.w;
    if (any(abs(ndc.xy) > 0.995) || ndc.z > 1.0) return 1.0;
    float2 uv = AtlasUv(ndc.xy, c);
    float texel = ShadowParams.x;
    float radius = ShadowSoft.x / CascadeTexel[c];     // the penumbra in this cascade's texels
    float sum = 0.0;
    if (radius > 2.5)
    {
        radius = min(radius, 48.0);
        float a = Ign(pixel) * 6.2831853;
        float2x2 rot = float2x2(cos(a), -sin(a), sin(a), cos(a));
        [unroll] for (int k = 0; k < 16; k++)
            sum += ShadowTex.SampleCmpLevelZero(ShadowSmp, uv + mul(rot, Poisson16[k]) * radius * texel, ndc.z);
        return sum / 16.0;
    }
    [unroll] for (int y = 0; y < 4; y++)
        [unroll] for (int x = 0; x < 4; x++)
            sum += ShadowTex.SampleCmpLevelZero(ShadowSmp, uv + (float2(x, y) - 1.5) * texel, ndc.z);
    return sum / 16.0;
}

int CascadeFor(float viewDepth)
{
    int count = (int)ShadowParams.z;
    int c = 0;
    [unroll] for (int i = 0; i < 3; i++) if (i < count - 1 && viewDepth > Splits[i]) c = i + 1;
    return c;
}

float MapShadow(float3 world, float3 n, float viewDepth, float2 pixel)
{
    if (ShadowParams.w < 0.5 || viewDepth > CameraForward.w) return 1.0;
    int count = (int)ShadowParams.z;
    int c = CascadeFor(viewDepth);
    float s = SampleCascade(world, n, c, pixel);
    // Blend into the next cascade over the last tenth of this one, so the seam does not show.
    float end = Splits[c], begin = c == 0 ? 0.0 : Splits[c - 1];
    float f = saturate((viewDepth - (end - (end - begin) * 0.1)) / max((end - begin) * 0.1, 1.0));
    if (f > 0.0 && c + 1 < count) s = lerp(s, SampleCascade(world, n, c + 1, pixel), f);
    // And fade out towards where shadows end.
    float fade = saturate((CameraForward.w - viewDepth) / (CameraForward.w * 0.1));
    return lerp(1.0, s, fade);
}

// The sun's light at a surface: its shadow maps' and the clouds' (ModernClouds.hlsli), which reach
// beyond where the maps end.
float Shadow(float3 world, float3 n, float viewDepth, float2 pixel)
{
    return MapShadow(world, n, viewDepth, pixel) * CloudShadow(world);
}

// One hardware comparison, no filter: whether the sun reaches a point in the air (the volumetric pass).
float ShadowPoint(float3 world, float viewDepth)
{
    if (ShadowParams.w < 0.5 || viewDepth > CameraForward.w) return 1.0;
    int c = CascadeFor(viewDepth);
    float4 lp = mul(Cascade[c], float4(world, 1.0));
    float3 ndc = lp.xyz / lp.w;
    if (any(abs(ndc.xy) > 0.995) || ndc.z > 1.0) return 1.0;
    return ShadowTex.SampleCmpLevelZero(ShadowSmp, AtlasUv(ndc.xy, c), ndc.z);
}

#endif
