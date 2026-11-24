// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Final step: copy the rendered frame to the screen through the game's brightness curve. The
// original asks the graphics hardware for a gamma ramp of output = input^(1/gamma) (default 1.4),
// which a modern window can't do without changing the whole desktop, so we apply it here.
cbuffer Params : register(b0, space3)
{
    float4 Params;      // x: 1 / gamma
};

[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] Texture2D<float4> SceneTex : register(t0, space2);
[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] SamplerState SceneSmp : register(s0, space2);

struct VSOut
{
    float4 Pos : SV_Position;
    float2 Uv : TEXCOORD0;
};

// One triangle covering the screen, from the vertex index alone (no vertex buffer).
VSOut VSMain(uint id : SV_VertexID)
{
    float2 uv = float2((id << 1) & 2, id & 2);
    VSOut o;
    o.Uv = uv;
    o.Pos = float4(uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    return o;
}

float4 PSMain(VSOut i) : SV_Target0
{
    float3 c = SceneTex.Sample(SceneSmp, i.Uv).rgb;
    return float4(pow(saturate(c), Params.x), 1.0);
}
