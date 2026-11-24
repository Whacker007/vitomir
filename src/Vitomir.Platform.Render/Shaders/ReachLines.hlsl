// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the reach highlight's lines (Overlay/ReachPass.cs), flat colour in the world, tested against the
// scene's own depth after tone mapping, so a line behind a wall is hidden as the wall would hide it.

cbuffer Params : register(b0, space1)
{
    float4x4 ViewProj;   // the camera's, unjittered (reversed, infinite depth), in the renderer's units
};

// Read in slot 0 only; the draw binds two (Gpu.FragmentSamplers).
[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] Texture2D<float4> Solid : register(t0, space2);
[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] SamplerState SolidSmp : register(s0, space2);

struct VSIn
{
    float3 Pos : TEXCOORD0;     // the world, in the renderer's units
    float4 Colour : TEXCOORD1;  // display colour and coverage
};

struct VSOut
{
    float4 Pos : SV_Position;
    float4 Colour : TEXCOORD0;
};

VSOut VSMain(VSIn i)
{
    VSOut o;
    o.Pos = mul(ViewProj, float4(i.Pos, 1.0));
    o.Colour = i.Colour;
    return o;
}

float4 PSMain(VSOut i) : SV_Target0
{
    return Solid.Sample(SolidSmp, float2(0.5, 0.5)) * i.Colour;
}
