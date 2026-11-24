// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Shaders/ModernSky.hlsl at b4185ef, reworked for the platform.
// The modern path's sky (ours): one triangle over the screen at the far depth,
// drawn only where nothing else is, with the sky of ModernCommon.hlsli (procedural, or a look's gradient
// with an overcast's soft cloud pattern and a dusk glow), the sun's disc, fair-weather cumulus
// (ModernClouds.hlsli), and the same height fog and shadowed sun in-scatter as the surfaces, so the
// distant city and the horizon meet in one haze.

// The physically based sky's tables (ours; ModernAtmosphere.hlsli), declared before ModernCommon.hlsli.
[[vk::combinedImageSampler]] [[vk::binding(2, 2)]] Texture2D<float4> SkyViewTex : register(t2, space2);
[[vk::combinedImageSampler]] [[vk::binding(2, 2)]] SamplerState SkyViewSmp : register(s2, space2);
[[vk::combinedImageSampler]] [[vk::binding(3, 2)]] Texture2D<float4> ApInTex : register(t3, space2);
[[vk::combinedImageSampler]] [[vk::binding(3, 2)]] SamplerState ApInSmp : register(s3, space2);
[[vk::combinedImageSampler]] [[vk::binding(4, 2)]] Texture2D<float4> ApTrTex : register(t4, space2);
[[vk::combinedImageSampler]] [[vk::binding(4, 2)]] SamplerState ApTrSmp : register(s4, space2);
[[vk::combinedImageSampler]] [[vk::binding(5, 2)]] Texture2D<float4> Spare2Tex : register(t5, space2);
[[vk::combinedImageSampler]] [[vk::binding(5, 2)]] SamplerState Spare2Smp : register(s5, space2);
#define MODERN_ATMOSPHERE 1
// The clouds' shape and detail noise (ours; ModernCloudNoise.cs), for the march in ModernClouds.hlsli.
[[vk::combinedImageSampler]] [[vk::binding(6, 2)]] Texture3D<float4> CloudShapeTex : register(t6, space2);
[[vk::combinedImageSampler]] [[vk::binding(6, 2)]] SamplerState CloudShapeSmp : register(s6, space2);
[[vk::combinedImageSampler]] [[vk::binding(7, 2)]] Texture3D<float4> CloudDetailTex : register(t7, space2);
[[vk::combinedImageSampler]] [[vk::binding(7, 2)]] SamplerState CloudDetailSmp : register(s7, space2);
#define MODERN_CLOUD_NOISE 1

#include "ModernCommon.hlsli"

cbuffer Vertex : register(b0, space1)
{
    float4x4 InverseRotationViewProj;   // clip space back to a view direction (the eye at the origin)
};

[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] Texture2D<float4> VolTex : register(t0, space2);
[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] SamplerState VolSmp : register(s0, space2);
[[vk::combinedImageSampler]] [[vk::binding(1, 2)]] Texture2D<float4> SpareTex : register(t1, space2);
[[vk::combinedImageSampler]] [[vk::binding(1, 2)]] SamplerState SpareSmp : register(s1, space2);

struct VSOut
{
    float4 Pos : SV_Position;
    float3 Dir : TEXCOORD0;
};

VSOut VSMain(uint id : SV_VertexID)
{
    float2 uv = float2((id << 1) & 2, id & 2);
    VSOut o;
    float2 ndc = uv * float2(2.0, -2.0) + float2(-1.0, 1.0);
    o.Pos = float4(ndc, 0.0, 1.0);
    float4 p = mul(InverseRotationViewProj, float4(ndc, 1.0, 1.0));
    o.Dir = p.xyz / p.w;
    return o;
}

float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

float ValueNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
}

// An overcast's soft pattern: the view direction projected onto a cloud layer, five octaves of value noise.
float Clouds(float3 d)
{
    float2 p = d.xy / max(d.z, 0.03) * 1.2;
    float sum = 0.0, amp = 0.5;
    [unroll] for (int k = 0; k < 5; k++) { sum += ValueNoise(p) * amp; p = p * 2.03 + 17.1; amp *= 0.55; }
    return sum / 0.9;
}

float4 PSMain(VSOut i) : SV_Target0
{
    float3 d = normalize(i.Dir);
    float3 c = SkyRadiance(d);
    if (SkyZenith.w > 0.0 && d.z > 0.0 && !AtmosphereOn())
    {
        float cl = clamp((Clouds(d) - 0.4) / 0.35 * 2.0 - 1.0, -1.0, 1.0) * SkyZenith.w;
        c *= 1.0 + cl * saturate(d.z / 0.3);
    }
    // The sun's disc, with a little limb darkening.
    float mu = dot(d, SunDir.xyz);
    if (mu > SunColor.w && Ground.w > 0.0)
    {
        float edge = saturate((mu - SunColor.w) / (1.0 - SunColor.w));
        c += SunColor.rgb * SunDir.w * Ground.w * (0.6 + 0.4 * sqrt(edge));
    }
    // The haze to a far distance: thick along the horizon, thin overhead, where the air's density has fallen away.
    float t = FogTransmittance(CameraPos.xyz, d, 2.0e6);
    c = c * t + InscatterColour(d) * (1.0 - t);
    // Cumulus in front of all that (ModernClouds.hlsli): the sky, its sun and aureole behind them dimmed
    // by their transmittance, their own light hazed over the distance to them.
    if (CloudsOn() && d.z > -0.01)
    {
        // At half resolution with dithered steps (ModernCloudPass.hlsl, SkyGlowDir.w = 1), read through a
        // box filter over the 4 × 4 half-size pixels round the nearest corner between them, which holds the
        // dither's sixteen places once each: four bilinear taps, each at a corner, so each averages its
        // 2 × 2 evenly. Else marched here, per pixel.
        // Under temporal anti-aliasing (SkyGlowDir.w = 2) the dither turns each frame and the frames average
        // it, so one tap at the corner, a 2 × 2 box, is enough and keeps the clouds' edges sharper.
        float4 cloud;
        float2 halfSize = floor((Screen.xy + 1.0) * 0.5);
        float2 corner = round(i.Pos.xy * 0.5);
        if (SkyGlowDir.w > 1.5) cloud = SpareTex.SampleLevel(SpareSmp, corner / halfSize, 0);
        else if (SkyGlowDir.w > 0.5)
        {
            cloud = 0.25 * (SpareTex.SampleLevel(SpareSmp, (corner + float2(-1.0, -1.0)) / halfSize, 0)
                          + SpareTex.SampleLevel(SpareSmp, (corner + float2( 1.0, -1.0)) / halfSize, 0)
                          + SpareTex.SampleLevel(SpareSmp, (corner + float2(-1.0,  1.0)) / halfSize, 0)
                          + SpareTex.SampleLevel(SpareSmp, (corner + float2( 1.0,  1.0)) / halfSize, 0));
        }
        else cloud = CloudMarch(CameraPos.xyz, d);
        if (cloud.a < 1.0)
        {
            float s = max(d.z, 0.02);
            float dist = CloudShellDistance(max((CameraPos.z - Fog.z) * CloudKmPerUnit, 0.0), CloudShape.z, s) / CloudKmPerUnit;
            float tc = FogTransmittance(CameraPos.xyz, d, min(dist, 2.0e6));
            if (AtmosphereOn())
            {
                // The air between the eye and the cloud (ours): the sky behind it shows through
                // its transmittance, the air's own light in front of it is added, and its light is dimmed
                // by the air's transmittance to it.
                float3 la, ta;
                AtmoAerial(d, dist * CloudKmPerUnit, la, ta);
                float3 cl = cloud.rgb * ta * tc + InscatterColour(d) * (1.0 - tc) * (1.0 - cloud.a);
                c = c * cloud.a + cl + la * (1.0 - cloud.a);
            }
            else c = c * cloud.a + cloud.rgb * tc + InscatterColour(d) * (1.0 - tc) * (1.0 - cloud.a);
        }
    }
    if (CameraPos.w > 0.5) c += SunInscatter(d) * VolTex.SampleLevel(VolSmp, i.Pos.xy * Screen.zw, 0).r;
    return float4(c, 1.0);
}
