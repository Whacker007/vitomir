// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Our own lettering on the UI (ours): a distance-field atlas, 0.5 on a glyph's outline, rising
// inside and falling outside over the face's spread. The edge is found anew at whatever size the glyph is drawn, so
// one atlas is sharp at every size, and the same distances give an outline, a soft drop shadow and a glow.
//
// The vertex stage is Ui.hlsl's. The vertex colour is the face's fill; the rest is the style the batch pushes.
cbuffer Params : register(b0, space1)
{
    float4 Screen;      // xy: the surface size in pixels
};

cbuffer Style : register(b0, space3)
{
    float4 OutlineColour;   // the outline's colour, alpha 0 for none
    float4 ShadowColour;    // the shadow's colour, alpha 0 for none
    float4 GlowColour;      // the glow's colour, alpha 0 for none
    float4 Shape;           // x: the outline's width, y and z: the shadow's offset in the atlas (uv), w: the glow's reach
                            // (widths and reach in distance units: 0.5 is the whole spread)
    float4 More;            // x: the shadow's softness (distance units)
};

[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] Texture2D<float4> Atlas : register(t0, space2);
[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] SamplerState AtlasSmp : register(s0, space2);

struct VSIn
{
    float2 Pos : TEXCOORD0;
    float2 Uv : TEXCOORD1;
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

// One layer over another, straight alpha.
float4 Over(float4 top, float4 under)
{
    float a = top.a + under.a * (1.0 - top.a);
    if (a <= 0.0) return float4(0.0, 0.0, 0.0, 0.0);
    float3 rgb = (top.rgb * top.a + under.rgb * under.a * (1.0 - top.a)) / a;
    return float4(rgb, a);
}

float4 PSMain(VSOut i) : SV_Target0
{
    float d = Atlas.Sample(AtlasSmp, i.Uv).r;
    float w = max(fwidth(d) * 0.75, 1e-4);           // a pixel's worth of distance, wherever the glyph is drawn

    // The face, and its outline round it.
    float face = smoothstep(0.5 - w, 0.5 + w, d);
    float outlineEdge = 0.5 - Shape.x;
    float outline = OutlineColour.a > 0.0 ? smoothstep(outlineEdge - w, outlineEdge + w, d) : face;
    float3 rgb = lerp(OutlineColour.rgb, i.Colour.rgb, OutlineColour.a > 0.0 ? face : 1.0);
    float4 text = float4(rgb, outline * i.Colour.a);

    // The glow: outside the outline, fading over its reach.
    float4 glow = float4(GlowColour.rgb, 0.0);
    if (GlowColour.a > 0.0)
        glow.a = smoothstep(outlineEdge - Shape.w, outlineEdge, d) * GlowColour.a * i.Colour.a;

    // The shadow: the same distances a little way off, softer.
    float4 shadow = float4(ShadowColour.rgb, 0.0);
    if (ShadowColour.a > 0.0)
    {
        float ds = Atlas.Sample(AtlasSmp, i.Uv - Shape.yz).r;
        float soft = max(More.x, w);
        shadow.a = smoothstep(outlineEdge - soft, outlineEdge + soft, ds) * ShadowColour.a * i.Colour.a;
    }

    return Over(text, Over(glow, shadow));
}
