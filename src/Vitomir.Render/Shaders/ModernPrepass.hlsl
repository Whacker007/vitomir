// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The modern path's depth-and-normal prepass (ours): every opaque and cut-out
// surface writes its depth and its world normal (encoded 0..1) before anything is shaded. Ambient
// occlusion and the volumetric sun read them; the shading pass then runs once per pixel, testing for
// equal depth. The pipeline uses ModernPbr's vertex shader (the VSMain here is the same code, unused).
// The normal's alpha (ours) marks the surfaces screen-space reflections trace from
// (ModernSsr.hlsl): 0 none (and for foliage), else 1 less the roughness over the cut-off, water 1. A hex-tiled material's roughness is
// read through its cells (ours; ModernHex.hlsli), weighted by the cells' places alone.

#include "ModernSurface.hlsli"
#include "ModernHex.hlsli"

cbuffer Cut : register(b0, space3)
{
    float4 Cut;     // x: alpha cutoff (< 0: none); y: base colour alpha factor; z: base colour coordinate set; w: 1 = foliage (the vertex alpha sways, not cuts)
    float4 Rough;   // x: roughness factor; y: metal-roughness coordinate set; z: 1 = open water; w: the roughness above which nothing reflects on screen (0: off)
    float4 Tiling;  // ours: as ModernPbr.hlsl's (x cells per texture unit, 0: plain; y 1 = they turn (+2); z their coordinate set)
};

[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] Texture2D<float4> BaseTex : register(t0, space2);
[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] SamplerState BaseSmp : register(s0, space2);
[[vk::combinedImageSampler]] [[vk::binding(1, 2)]] Texture2D<float4> MrTex : register(t1, space2);
[[vk::combinedImageSampler]] [[vk::binding(1, 2)]] SamplerState MrSmp : register(s1, space2);

float4 PSMain(VSOut i, bool front : SV_IsFrontFace) : SV_Target0
{
    if (Cut.x >= 0.0)
        clip(BaseTex.Sample(BaseSmp, Set(i.Uv, Cut.z)).a * Cut.y * (Cut.w > 0.5 ? 1.0 : i.Color.a) - Cut.x);
    float3 n = normalize(i.Normal) * (front ? 1.0 : -1.0);
    float reflective = 0.0;
    if (Rough.w > 0.0 && Cut.w < 0.5)   // never foliage: a leaf's sheen is broad, not a mirror's
    {
        float r;
        if (Tiling.x > 0.0 && abs(Rough.y - Tiling.z) < 0.5)
        {
            float2 st = Set(i.Uv, Tiling.z);
            HexFrame f = HexFrameAt(st, Tiling.x, frac(Tiling.y * 0.5) * 2.0);
            r = Rough.x * HexSample(MrTex, MrSmp, f, HexWeights(HexBary(f, st), 1.0, 0.0), st).g;
        }
        else r = Rough.x * MrTex.Sample(MrSmp, Set(i.Uv, Rough.y)).g;
        reflective = Rough.z > 0.5 ? 1.0 : saturate(1.0 - r / Rough.w) * 0.99;
    }
    return float4(n * 0.5 + 0.5, reflective);
}
