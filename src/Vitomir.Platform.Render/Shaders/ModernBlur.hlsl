// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Shaders/ModernBlur.hlsl at b4185ef, reworked for the platform.
// A depth-aware (bilateral) blur for the modern path's half-resolution effects (ours): nine taps along
// one axis, Gaussian, each weighted down as its view distance departs from the centre's, so ambient
// occlusion and sun shafts smooth out their noise without bleeding across an object's edge. The input's
// red channel is blurred; green carries the view distance in metres and is passed on.

cbuffer Params : register(b0, space3)
{
    float4 Params;      // xy: one texel along the blur's axis (texture coordinates); z: depth sharpness; w unused
};

[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] Texture2D<float4> SrcTex : register(t0, space2);
[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] SamplerState SrcSmp : register(s0, space2);
[[vk::combinedImageSampler]] [[vk::binding(1, 2)]] Texture2D<float4> SpareTex : register(t1, space2);
[[vk::combinedImageSampler]] [[vk::binding(1, 2)]] SamplerState SpareSmp : register(s1, space2);

struct ScreenOut
{
    float4 Pos : SV_Position;
    float2 Uv : TEXCOORD0;
};

ScreenOut VSMain(uint id : SV_VertexID)
{
    float2 uv = float2((id << 1) & 2, id & 2);
    ScreenOut o;
    o.Uv = uv;
    o.Pos = float4(uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    return o;
}

float4 PSMain(ScreenOut i) : SV_Target0
{
    // r is blurred, g is the depth that steers it; b, when the target has it (ambient occlusion's: the screen's
    // own occlusion, ours), is blurred alongside.
    float3 c = SrcTex.SampleLevel(SrcSmp, i.Uv, 0).rgb;
    float2 sum = c.rb;
    float wsum = 1.0;
    [unroll] for (int k = 1; k <= 4; k++)
    {
        float g = exp(-k * k / 8.0);
        [unroll] for (int side = -1; side <= 1; side += 2)
        {
            float3 s = SrcTex.SampleLevel(SrcSmp, i.Uv + Params.xy * (k * side), 0).rgb;
            float w = g * exp(-abs(s.g - c.g) / max(c.g, 0.5) * Params.z);
            sum += s.rb * w;
            wsum += w;
        }
    }
    return float4(sum.x / wsum, c.g, sum.y / wsum, 0);
}
