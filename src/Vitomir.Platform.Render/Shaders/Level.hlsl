// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Level geometry, things and the sky. The original renderer draws these through Direct3D 7's two
// texture stages, in one of 43 named modes (see RenderKind.cs); this shader is those stages: stage 0
// combines the surface texture with the vertex colour (which carries the surface's light) or with a
// constant, stage 1 multiplies the lightmap in, then the scene's fog is applied on view depth.
cbuffer Frame : register(b0, space1)
{
    float4x4 ViewProj;
};

cbuffer Material : register(b0, space3)
{
    float4 Params;      // x: 1 = stage 1 samples the lightmap; y: alpha scale; z: alpha-test threshold (0 = off); w: 1 = fully lit
    float4 Fog;         // x: 0 off, 1 linear, 2 exp, 3 exp2; y: start; z: end; w: density
    float4 FogColor;    // rgb 0..1
    float4 Combiner;    // x: stage-0 colour op; y: colour operand 2 (0 vertex colour, 1 texture factor); z: stage-0 alpha op; w: stage-1 colour op
    float4 Slide;       // xy: stage-0 texture-coordinate offset; z: 1 = stage 0 samples the second coordinate set; w: its scale (a detail texture repeats)
    float4 Factor;      // the texture-factor constant
    // Ours: the soft windows a surface in the camera's way is seen through (the engine's SeeThrough). HoleEye.xyz is the
    // eye, HoleEye.w the surface's fade level (0: no window, the surface drawn as ever); each HoleAxis is a window's unit
    // line from the eye with .w the cosine at which it is clear; HoleOuter holds each one's cosine at which it is solid
    // again, HoleFar how far along its line it reaches (0: no window).
    float4 HoleEye;
    float4 HoleAxis[4];
    float4 HoleOuter;
    float4 HoleFar;
};

[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] Texture2D<float4> BaseTex : register(t0, space2);
[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] SamplerState BaseSmp : register(s0, space2);
[[vk::combinedImageSampler]] [[vk::binding(1, 2)]] Texture2D<float4> LightTex : register(t1, space2);
[[vk::combinedImageSampler]] [[vk::binding(1, 2)]] SamplerState LightSmp : register(s1, space2);

// The texture-stage operations the modes use, numbered as TexOp is in RenderKind.cs.
#define OP_DISABLE   0
#define OP_ARG1      1
#define OP_ARG2      2
#define OP_MODULATE  3
#define OP_MOD_ADDA  4

struct VSIn
{
    float3 Pos : TEXCOORD0;
    float2 Uv : TEXCOORD1;
    float2 LmUv : TEXCOORD2;
    float4 Color : TEXCOORD3;
};

struct VSOut
{
    float4 Pos : SV_Position;
    float2 Uv : TEXCOORD0;
    float2 LmUv : TEXCOORD1;
    float4 Color : TEXCOORD2;
    float Depth : TEXCOORD3;    // view-space depth (clip w), for fog
    float3 World : TEXCOORD4;   // the world position, for the see-through windows (ours)
};

// How faded a surface in the camera's way is drawn at its level of 1: the engine's SeeThrough.Clear.
#define SEE_THROUGH_CLEAR 0.1

// Ours: how clear the windows make a point, 0 to 1 — the engine's SeeThrough.ClearnessOf, which the pointer uses, so the
// picture and the pointer agree: the most of each window's smoothstep from its rim's cosine to its middle's, nothing
// beyond its reach.
float Clearness(float3 world)
{
    float3 d = world - HoleEye.xyz;
    float len = length(d);
    if (len < 1e-3) return 1.0;
    float3 v = d / len;
    float best = 0.0;
    [unroll] for (int k = 0; k < 4; k++)
    {
        if (HoleFar[k] <= 0.0 || dot(d, HoleAxis[k].xyz) >= HoleFar[k]) continue;
        best = max(best, smoothstep(HoleOuter[k], HoleAxis[k].w, dot(v, HoleAxis[k].xyz)));
    }
    return best;
}

VSOut VSMain(VSIn i)
{
    VSOut o;
    o.Pos = mul(ViewProj, float4(i.Pos, 1.0));
    o.Uv = i.Uv;
    o.LmUv = i.LmUv;
    o.Color = i.Color;
    o.Depth = o.Pos.w;
    o.World = i.Pos;
    return o;
}

float4 PSMain(VSOut i) : SV_Target0
{
    // A mode can point stage 0 at either coordinate set; a material's "slide" line drifts it and a
    // detail texture repeats over it.
    float2 uv = (Slide.z > 0.5 ? i.LmUv : i.Uv) * Slide.w + Slide.xy;
    float4 tex = BaseTex.Sample(BaseSmp, uv);
    float4 diffuse = Params.w > 0.5 ? float4(1.0, 1.0, 1.0, i.Color.a) : i.Color;
    float3 arg2 = Combiner.y > 0.5 ? Factor.rgb : diffuse.rgb;

    float3 c = tex.rgb * arg2;
    if (Combiner.x < OP_ARG2 + 0.5) c = Combiner.x < OP_ARG1 + 0.5 ? tex.rgb : arg2;
    else if (Combiner.x > OP_MODULATE + 0.5) c = tex.rgb * arg2 + tex.a;   // modulate colour, add alpha

    float a = tex.a;
    if (Combiner.z < OP_DISABLE + 0.5) a = 1.0;
    else if (Combiner.z > OP_ARG1 + 0.5) a = Combiner.z < OP_ARG2 + 0.5 ? diffuse.a : tex.a * diffuse.a;   // select arg 2: the vertex; modulate: both

    // Stage 1 is the lightmap stage: it multiplies the light in and leaves alpha alone.
    if (Combiner.w > OP_DISABLE + 0.5) c *= LightTex.Sample(LightSmp, i.LmUv).rgb;

    // The original enables alpha test with "greater than 0": it sets the reference value never.
    if (Params.z > 0.0 && a < Params.z) discard;
    a *= Params.y;
    if (HoleEye.w > 0.0) a *= 1.0 - saturate(HoleEye.w * Clearness(i.World)) * (1.0 - SEE_THROUGH_CLEAR);

    if (Fog.x > 0.5)
    {
        float f;
        if (Fog.x < 1.5) f = Fog.z > Fog.y ? saturate((Fog.z - i.Depth) / (Fog.z - Fog.y)) : (i.Depth < Fog.z ? 1.0 : 0.0);
        else if (Fog.x < 2.5) f = exp(-Fog.w * i.Depth);
        else { float e = Fog.w * i.Depth; f = exp(-e * e); }
        c = lerp(FogColor.rgb, c, f);
    }
    return float4(c, a);
}
