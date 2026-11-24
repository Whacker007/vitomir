// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Shaders/ModernTonemap.hlsl at b4185ef, reworked for the platform.
// The modern path's tone mapping (ours): the linear HDR frame plus its bloom and
// the sun's lens flare, times the exposure, through a filmic curve — ACES's (Stephen Hill's fit,
// ModernMath.AcesFitted) or AgX (Blender's since 4.0, in Benjamin Wrensch's minimal fit, ModernMath.AgX)
// with a contrast round mid-grey as Blender's AgX looks have — encoded for an sRGB screen, then a light
// grade (saturation, a lift of the blacks, a vignette), with the result's luma in alpha for FXAA to read.

cbuffer Params : register(b0, space3)
{
    float4 P0;      // x: exposure (a multiplier); y: curve (0 ACES, 1 AgX); z: contrast; w: saturation
    float4 P1;      // x: lift; y: vignette; z: bloom share; w: flare strength
    float4 P2;      // xy: the sun's texture coordinates; z: aspect; w: 1 = the sun is in front of the camera
    float4 P3;      // rgb: the sun's light (colour times intensity); w: 1 / the bloom's levels
    float4 P4;      // x: a debugging view (1: SpareTex's red channel, ambient occlusion or the shafts, as grey); yzw unused
};

[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] Texture2D<float4> HdrTex : register(t0, space2);
[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] SamplerState HdrSmp : register(s0, space2);
[[vk::combinedImageSampler]] [[vk::binding(1, 2)]] Texture2D<float4> BloomTex : register(t1, space2);
[[vk::combinedImageSampler]] [[vk::binding(1, 2)]] SamplerState BloomSmp : register(s1, space2);
[[vk::combinedImageSampler]] [[vk::binding(2, 2)]] Texture2D<float4> SunVisTex : register(t2, space2);
[[vk::combinedImageSampler]] [[vk::binding(2, 2)]] SamplerState SunVisSmp : register(s2, space2);
[[vk::combinedImageSampler]] [[vk::binding(3, 2)]] Texture2D<float4> SpareTex : register(t3, space2);
[[vk::combinedImageSampler]] [[vk::binding(3, 2)]] SamplerState SpareSmp : register(s3, space2);

struct VSOut
{
    float4 Pos : SV_Position;
    float2 Uv : TEXCOORD0;
};

VSOut VSMain(uint id : SV_VertexID)
{
    float2 uv = float2((id << 1) & 2, id & 2);
    VSOut o;
    o.Uv = uv;
    o.Pos = float4(uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    return o;
}

float3 RrtOdt(float3 v)
{
    float3 a = v * (v + 0.0245786) - 0.000090537;
    float3 b = v * (0.983729 * v + 0.4329510) + 0.238081;
    return a / b;
}

float3 AcesFitted(float3 c)
{
    float3 a = float3(
        dot(float3(0.59719, 0.35458, 0.04823), c),
        dot(float3(0.07600, 0.90834, 0.01566), c),
        dot(float3(0.02840, 0.13383, 0.83777), c));
    a = RrtOdt(a);
    float3 o = float3(
        dot(float3(1.60475, -0.53108, -0.07367), a),
        dot(float3(-0.10208, 1.10813, -0.00605), a),
        dot(float3(-0.00327, -0.07276, 1.07602), a));
    return saturate(o);
}

float3 ToSrgb(float3 x)
{
    x = saturate(x);
    return select(x <= 0.0031308, 12.92 * x, 1.055 * pow(x, 1.0 / 2.4) - 0.055);
}

// AgX: into its working space, a log2 encoding from 12.47 stops below mid-grey to 4.03 above, the
// contrast round mid-grey, the sigmoid (a sixth-degree fit), and back out to linear light.
float3 AgX(float3 v, float contrast)
{
    v = float3(dot(float3(0.842479062253094, 0.0784335999999992, 0.0792237451477643), v),
               dot(float3(0.0423282422610123, 0.878468636469772, 0.0791661274605434), v),
               dot(float3(0.0423756549057051, 0.0784336, 0.879142973793104), v));
    const float minEv = -12.47393, maxEv = 4.026069;
    v = clamp(log2(max(v, 1e-10)), minEv, maxEv);
    v = (v - minEv) / (maxEv - minEv);
    const float pivot = (-2.4739311883 - minEv) / (maxEv - minEv);
    v = saturate(pivot + (v - pivot) * contrast);
    float3 x2 = v * v, x4 = x2 * x2;
    v = 15.5 * x4 * x2 - 40.14 * x4 * v + 31.96 * x4 - 6.868 * x2 * v + 0.4298 * x2 + 0.1191 * v - 0.00232;
    v = float3(dot(float3(1.19687900512017, -0.0980208811401368, -0.0990297440797205), v),
               dot(float3(-0.0528968517574562, 1.15190312990417, -0.0989611768448433), v),
               dot(float3(-0.0529716355144438, -0.0980434501171241, 1.15107367264116), v));
    return pow(saturate(v), 2.2);
}

// The sun's lens flare: ghosts of the aperture strung along the line from the sun through the centre
// of the picture, each a soft disc with its own faint tint, as a camera's coated elements reflect them.
float3 Flare(float2 uv)
{
    if (P1.w <= 0.0 || P2.w <= 0.0) return 0;
    float vis = SunVisTex.SampleLevel(SunVisSmp, float2(0.5, 0.5), 0).r;
    if (!(vis > 0.0)) return 0;
    float2 toCentre = 0.5 - P2.xy;
    static const float4 ghosts[6] =
    {
        float4(0.45, 0.030, 0.008, 0.0), float4(0.8, 0.060, 0.005, 1.0), float4(1.25, 0.022, 0.012, 2.0),
        float4(1.55, 0.110, 0.003, 0.0), float4(1.9, 0.045, 0.006, 1.0), float4(2.3, 0.160, 0.002, 2.0),
    };
    static const float3 tints[3] = { float3(1.0, 0.75, 0.45), float3(0.55, 0.85, 1.0), float3(0.75, 1.0, 0.6) };
    float3 f = 0;
    [unroll] for (int k = 0; k < 6; k++)
    {
        float2 p = P2.xy + toCentre * ghosts[k].x;
        float d = length((uv - p) * float2(P2.z, 1.0));
        float disc = smoothstep(ghosts[k].y, ghosts[k].y * 0.55, d);
        f += tints[(int)ghosts[k].w] * disc * ghosts[k].z;
    }
    // A faint ring round the centre, from the sun's light caught on the lens barrel.
    float ring = length((uv - 0.5) * float2(P2.z, 1.0));
    f += float3(0.9, 0.8, 1.0) * 0.004 * exp(-pow((ring - 0.42) / 0.025, 2.0)) * saturate(1.0 - length(toCentre));
    return f * P3.rgb * vis * P1.w;
}

float4 PSMain(VSOut i) : SV_Target0
{
    if (P4.x > 0.5) { float g = SpareTex.Sample(SpareSmp, i.Uv).r; return float4(g, g, g, g); }
    float3 hdr = HdrTex.Sample(HdrSmp, i.Uv).rgb;
    hdr = max(hdr, 0.0);
    if (any(isnan(hdr))) hdr = 0.0;
    if (P1.z > 0.0) hdr = lerp(hdr, max(BloomTex.Sample(BloomSmp, i.Uv).rgb * P3.w, 0.0), P1.z);
    hdr += Flare(i.Uv);
    float3 x = hdr * P0.x;
    float3 c = ToSrgb(P0.y > 0.5 ? AgX(x, P0.z) : AcesFitted(x));
    // The grade, on display values (as the concept stills' grade is).
    float l = dot(c, float3(0.2126, 0.7152, 0.0722));
    c = saturate(l + (c - l) * P0.w);
    c = P1.x + c * (1.0 - P1.x);
    float2 q = (i.Uv - 0.5) * 2.0;
    float r = length(q) / 1.41421356;
    c *= 1.0 - P1.y * pow(saturate(r - 0.25), 1.6) / pow(0.75, 1.6);
    return float4(c, dot(c, float3(0.299, 0.587, 0.114)));
}
