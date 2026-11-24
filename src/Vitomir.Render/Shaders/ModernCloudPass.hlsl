// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The cumulus at half resolution (ours): each pixel of a half-size
// target marches its view ray through the cloud layer (ModernClouds.hlsli) with its steps placed by a
// per-pixel ordered dither (which turns each frame under temporal anti-aliasing), so the bands a march
// with fixed steps leaves on a cloud's side become a fine pattern; the sky (ModernSky.hlsl) reads the
// result through a 4 × 4 box filter, which averages the pattern away exactly. What a pixel sees of the
// clouds depends on its direction alone, not on what stands in front, so the sky can read the half-size
// picture anywhere without bleeding across a roof's edge; pixels whose neighbourhood holds no sky at all
// are skipped. Output: rgb the clouds' light, a their transmittance.

// The clouds' shape and detail noise (ours; ModernCloudNoise.cs), for the march in ModernClouds.hlsli.
[[vk::combinedImageSampler]] [[vk::binding(2, 2)]] Texture3D<float4> CloudShapeTex : register(t2, space2);
[[vk::combinedImageSampler]] [[vk::binding(2, 2)]] SamplerState CloudShapeSmp : register(s2, space2);
[[vk::combinedImageSampler]] [[vk::binding(3, 2)]] Texture3D<float4> CloudDetailTex : register(t3, space2);
[[vk::combinedImageSampler]] [[vk::binding(3, 2)]] SamplerState CloudDetailSmp : register(s3, space2);
#define MODERN_CLOUD_NOISE 1
#include "ModernCommon.hlsli"

[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] Texture2D<float> DepthTex : register(t0, space2);
[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] SamplerState DepthSmp : register(s0, space2);
[[vk::combinedImageSampler]] [[vk::binding(1, 2)]] Texture2D<float> SpareTex : register(t1, space2);
[[vk::combinedImageSampler]] [[vk::binding(1, 2)]] SamplerState SpareSmp : register(s1, space2);

// The 2 × 2 ordered dither: 0 2 / 3 1.
float Bayer2(uint2 p) { return (float)(((p.x ^ p.y) & 1u) * 2u + (p.y & 1u)); }

ScreenOut VSMain(uint id : SV_VertexID) { return ScreenTriangle(id); }

float4 PSMain(ScreenOut i) : SV_Target0
{
    // The sky's filter reads this texel's neighbours too: march only if sky (reversed depth 0) shows
    // within about two and a half of its texels, five of the picture's pixels.
    bool sky = false;
    [unroll] for (int y = -1; y <= 1; y++)
        [unroll] for (int x = -1; x <= 1; x++)
            sky = sky || DepthTex.SampleLevel(DepthSmp, i.Uv + float2(x, y) * Screen.zw * 5.0, 0) <= 0.0;
    if (!sky) return float4(0, 0, 0, 1);
    float3 d = ViewRay(i.Uv);
    // Where this pixel's steps end, (0, 1] of a step: a 4 × 4 ordered (Bayer) dither, so that any 4 × 4
    // block of pixels holds all sixteen places once and the sky's 4 × 4 box filter averages them exactly
    // (sixteen times the steps, for a smooth cloud); turned by the golden ratio each frame under
    // temporal anti-aliasing (FogSun.z counts frames; 0 without), which averages them over time too.
    uint2 p = (uint2)i.Pos.xy & 3u;
    float bayer = 4.0 * Bayer2(p & 1u) + Bayer2(p >> 1u);
    float u = 1.0 - frac((bayer + 0.5) / 16.0 + FogSun.z * 0.618034);
    return CloudMarch(CameraPos.xyz, d, u, 1.0);
}
