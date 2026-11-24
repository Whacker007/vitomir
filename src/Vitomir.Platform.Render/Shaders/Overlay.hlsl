// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The diagnostic overlay: coloured quads and text, drawn straight onto the finished frame.
// Positions arrive in pixels and are turned into clip space here, so nothing in the CPU side has
// to know which way the Y axis runs on a given backend.
cbuffer Params : register(b0, space1)
{
    float4 Screen;      // xy: the surface size in pixels
};

[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] Texture2D<float4> FontTex : register(t0, space2);
[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] SamplerState FontSmp : register(s0, space2);

struct VSIn
{
    float2 Pos : TEXCOORD0;     // pixels, origin top left
    float2 Uv : TEXCOORD1;      // into the font atlas; (-1,-1) means "solid, no texture"
    float4 Colour : TEXCOORD2;
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
    if (i.Uv.x < 0.0) return i.Colour;                  // a solid quad: a panel, a rule, a bar
    float mask = FontTex.Sample(FontSmp, i.Uv).a;       // a glyph: the atlas carries coverage
    return float4(i.Colour.rgb, i.Colour.a * mask);
}
