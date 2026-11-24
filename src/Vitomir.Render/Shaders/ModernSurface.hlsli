// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The modern path's surface vertex stage (ours), shared by the depth-and-normal prepass and the shading
// pass. Both pipelines are built from ModernPbr's compiled vertex shader, so the two passes compute
// bit-identical depths and the shading pass can test for equality (ModernRenderer).

#ifndef MODERN_SURFACE_HLSLI
#define MODERN_SURFACE_HLSLI

#include "ModernFoliage.hlsli"

cbuffer Vertex : register(b0, space1)
{
    float4x4 VertexViewProj;
    float4 VertexSway;      // foliage (ModernFoliage.hlsli): x its sway (0: none), y the clock (s), zw the wind's direction
};

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
    float3 World : TEXCOORD0;
    float3 Normal : TEXCOORD1;
    float4 Tangent : TEXCOORD2;
    float4 Uv : TEXCOORD3;      // xy set 0, zw set 1
    float4 Color : TEXCOORD4;
};

VSOut VSMain(VSIn i)
{
    VSOut o;
    float3 p = i.Pos + SwayOffset(i.Pos, i.Color.a, VertexSway);
    o.Pos = mul(VertexViewProj, float4(p, 1.0));
    o.World = p;
    o.Normal = i.Normal;
    o.Tangent = i.Tangent;
    o.Uv = float4(i.Uv0, i.Uv1);
    o.Color = i.Color;
    return o;
}

float2 Set(float4 uv, float which) { return which > 0.5 ? uv.zw : uv.xy; }

#endif
