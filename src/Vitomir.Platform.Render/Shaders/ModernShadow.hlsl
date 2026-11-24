// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Shaders/ModernShadow.hlsl at b4185ef, reworked for the platform.
// The modern path's sun shadow maps (ours): depth only, seen from the sun for one
// cascade. Cut-out (MASK) materials test their alpha so leaves and grilles cast the shape they show.

#include "ModernFoliage.hlsli"

cbuffer Vertex : register(b0, space1)
{
    float4x4 LightViewProj;
    float4 VertexSway;      // foliage (ModernFoliage.hlsli): x its sway (0: none), y the clock (s), zw the wind's direction
};

cbuffer Material : register(b0, space3)
{
    float4 Params;      // x: alpha cutoff (< 0: none); y: base colour factor's alpha; z: coordinate set; w: 1 = foliage (the vertex alpha sways, not cuts)
};

[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] Texture2D<float4> BaseTex : register(t0, space2);
[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] SamplerState BaseSmp : register(s0, space2);

struct VSIn
{
    float3 Pos : TEXCOORD0;
    float3 Normal : TEXCOORD1;
    float4 Tangent : TEXCOORD2;
    float2 Uv0 : TEXCOORD3;
    float2 Uv1 : TEXCOORD4;
    float4 Color : TEXCOORD5;
};

struct VSOut
{
    float4 Pos : SV_Position;
    float4 Uv : TEXCOORD0;
    float Alpha : TEXCOORD1;
};

VSOut VSMain(VSIn i)
{
    VSOut o;
    o.Pos = mul(LightViewProj, float4(i.Pos + SwayOffset(i.Pos, i.Color.a, VertexSway), 1.0));
    o.Uv = float4(i.Uv0, i.Uv1);
    o.Alpha = i.Color.a;
    return o;
}

void PSMain(VSOut i)
{
    if (Params.x >= 0.0)
    {
        float2 uv = Params.z > 0.5 ? i.Uv.zw : i.Uv.xy;
        clip(BaseTex.Sample(BaseSmp, uv).a * Params.y * (Params.w > 0.5 ? 1.0 : i.Alpha) - Params.x);
    }
}
