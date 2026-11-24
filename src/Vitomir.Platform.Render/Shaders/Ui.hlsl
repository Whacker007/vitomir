// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The game's own UI: the player's textures and fonts, in the original's 640x480 space.
//
// The difference from Overlay.hlsl, which is the developer tool's shader, is one line: this one
// takes the texture's colour as well as its alpha, because a pane draws the game's artwork rather
// than a one-colour font atlas. The vertex colour is a tint, so white leaves a texture alone and a
// font strip - white with coverage in its alpha - comes out in whatever colour the caller asked for.
cbuffer Params : register(b0, space1)
{
    float4 Screen;      // xy: the surface size in pixels
};

[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] Texture2D<float4> UiTex : register(t0, space2);
[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] SamplerState UiSmp : register(s0, space2);

struct VSIn
{
    float2 Pos : TEXCOORD0;     // pixels, origin top left
    float2 Uv : TEXCOORD1;
    float4 Colour : TEXCOORD2;  // a tint
};

struct VSOut
{
    float4 Pos : SV_Position;
    float2 Uv : TEXCOORD0;
    float4 Colour : TEXCOORD1;
};

VSOut VSMain(VSIn i)
{
    VSOut o;
    float2 ndc = i.Pos / Screen.xy * float2(2.0, -2.0) + float2(-1.0, 1.0);
    o.Pos = float4(ndc, 0.0, 1.0);
    o.Uv = i.Uv;
    o.Colour = i.Colour;
    return o;
}

float4 PSMain(VSOut i) : SV_Target0
{
    return UiTex.Sample(UiSmp, i.Uv) * i.Colour;
}
