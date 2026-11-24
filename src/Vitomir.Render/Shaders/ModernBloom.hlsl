// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Bloom and the sun's visibility for the modern path (ours). Bloom is a lens's
// veiling glare: the picture scattered into a wide, soft halo, a few percent of all its light, not only
// of what is brightest. It is made the way Jimenez's "Next Generation Post Processing in Call of Duty:
// Advanced Warfare" does: a chain of halvings with a 13-tap filter (the first one averaging its four
// groups by 1 / (1 + luma), which keeps one blazing pixel from turning into a blinking square), then back
// up the chain, each level adding a 3x3 tent of the smaller one. An optional soft-knee threshold keeps
// dim light out. Mode 3 is not bloom: one pixel that counts how much of the sun's disc the depth buffer
// leaves open (the lens flare's strength).

cbuffer Params : register(b0, space3)
{
    float4 P0;      // xy: the source's texel size; z: mode (0 first halving, 1 halving, 2 tent up, 3 sun visibility); w unused
    float4 P1;      // x: threshold; y: knee (fraction of the threshold); z: exposure; w unused
    float4 P2;      // mode 3: xy the sun's texture coordinates; z: the disc's radius (texture coordinates, x); w: aspect
};

[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] Texture2D<float4> SrcTex : register(t0, space2);
[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] SamplerState SrcSmp : register(s0, space2);
[[vk::combinedImageSampler]] [[vk::binding(1, 2)]] Texture2D<float> DepthTex : register(t1, space2);
[[vk::combinedImageSampler]] [[vk::binding(1, 2)]] SamplerState DepthSmp : register(s1, space2);

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

float Luma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

// The soft-knee threshold (ModernMath.BloomThreshold): nothing below threshold − knee, all of it above
// threshold + knee, a quadratic between; at threshold 0 everything passes.
float3 Threshold(float3 c)
{
    float t = P1.x;
    if (t <= 0.0) return c;
    float k = max(t * P1.y, 1e-4);
    float l = max(max(c.r, c.g), c.b);
    float soft = clamp(l - t + k, 0.0, 2.0 * k);
    soft = soft * soft / (4.0 * k);
    return c * max(soft, l - t) / max(l, 1e-4);
}

float3 Tap(float2 uv) { return SrcTex.SampleLevel(SrcSmp, uv, 0).rgb; }

float4 PSMain(ScreenOut i) : SV_Target0
{
    int mode = (int)P0.z;
    float2 t = P0.xy;
    if (mode == 3)
    {
        // 32 points over the sun's disc: the share of them where nothing was drawn (depth 0, the sky).
        float open = 0.0;
        [unroll] for (int k = 0; k < 32; k++)
        {
            float r = sqrt((k + 0.5) / 32.0), a = k * 2.3999632;
            float2 uv = P2.xy + float2(cos(a), sin(a) * P2.w) * r * P2.z;
            bool inside = all(uv > 0.0) && all(uv < 1.0);
            open += inside && DepthTex.SampleLevel(DepthSmp, uv, 0) <= 0.0 ? 1.0 : 0.0;
        }
        return float4(open / 32.0, 0, 0, 1);
    }
    if (mode == 2)
    {
        float3 c = Tap(i.Uv) * 4.0;
        c += (Tap(i.Uv + float2(-t.x, 0)) + Tap(i.Uv + float2(t.x, 0)) + Tap(i.Uv + float2(0, -t.y)) + Tap(i.Uv + float2(0, t.y))) * 2.0;
        c += Tap(i.Uv + float2(-t.x, -t.y)) + Tap(i.Uv + float2(t.x, -t.y)) + Tap(i.Uv + float2(-t.x, t.y)) + Tap(i.Uv + float2(t.x, t.y));
        return float4(c / 16.0, 1.0);
    }
    float3 a = Tap(i.Uv + t * float2(-2, -2)), b = Tap(i.Uv + t * float2(0, -2)), c = Tap(i.Uv + t * float2(2, -2));
    float3 d = Tap(i.Uv + t * float2(-2, 0)), e = Tap(i.Uv), f = Tap(i.Uv + t * float2(2, 0));
    float3 g = Tap(i.Uv + t * float2(-2, 2)), h = Tap(i.Uv + t * float2(0, 2)), k = Tap(i.Uv + t * float2(2, 2));
    float3 j = Tap(i.Uv + t * float2(-1, -1)), l = Tap(i.Uv + t * float2(1, -1));
    float3 m = Tap(i.Uv + t * float2(-1, 1)), n = Tap(i.Uv + t * float2(1, 1));
    float3 g0 = (j + l + m + n) * 0.25, g1 = (a + b + d + e) * 0.25, g2 = (b + c + e + f) * 0.25;
    float3 g3 = (d + e + g + h) * 0.25, g4 = (e + f + h + k) * 0.25;
    if (mode == 0)
    {
        float s = P1.z;
        g0 = Threshold(g0 * s); g1 = Threshold(g1 * s); g2 = Threshold(g2 * s); g3 = Threshold(g3 * s); g4 = Threshold(g4 * s);
        float w0 = 0.5 / (1.0 + Luma(g0)), w1 = 0.125 / (1.0 + Luma(g1)), w2 = 0.125 / (1.0 + Luma(g2));
        float w3 = 0.125 / (1.0 + Luma(g3)), w4 = 0.125 / (1.0 + Luma(g4));
        return float4((g0 * w0 + g1 * w1 + g2 * w2 + g3 * w3 + g4 * w4) / (w0 + w1 + w2 + w3 + w4) / s, 1.0);
    }
    return float4(g0 * 0.5 + (g1 + g2 + g3 + g4) * 0.125, 1.0);
}
