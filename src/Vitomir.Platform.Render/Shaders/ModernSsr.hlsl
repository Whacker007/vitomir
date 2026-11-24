// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Shaders/ModernSsr.hlsl at b4185ef, reworked for the platform.
// Screen-space reflections, the trace (ours, rebuilt).
// Runs before the surfaces are shaded. For each pixel the prepass marked as smooth (its normal's alpha: puddles,
// the wet gutter, water, wet stone; at half resolution the smoothest of the 2 x 2 pixels it covers, so a puddle's
// thin water between the stones is not lost), the view ray's mirror reflection is marched across the depth buffer
// in even steps on the screen, from the surface to where it would leave the screen, pass the near plane or reach
// 60 m. Where it goes behind the depth buffer between two steps the crossing is found in halvings and, if there
// it lies on the surface (not far behind a nearer one's edge) and that surface faces the ray, it is what the
// pixel reflects: its light is read from the previous frame's
// finished picture, at the place that surface stood in it. Out: the light times a confidence (premultiplied) and
// the confidence, which fades towards the screen's edges, at the ray's far end, for rays turning back towards
// the eye and for hits found deep behind a surface; and the hit's distance and the surface's roughness, for the
// resolve's roughness-aware filter (ModernSsrResolve.hlsl). Where the march finds nothing the confidence is 0 and
// the shading pass keeps its own reflection of the sky and the light probes.

[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] Texture2D<float> DepthTex : register(t0, space2);
[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] SamplerState DepthSmp : register(s0, space2);
[[vk::combinedImageSampler]] [[vk::binding(1, 2)]] Texture2D<float4> NormalTex : register(t1, space2);
[[vk::combinedImageSampler]] [[vk::binding(1, 2)]] SamplerState NormalSmp : register(s1, space2);
[[vk::combinedImageSampler]] [[vk::binding(2, 2)]] Texture2D<float4> PrevTex : register(t2, space2);
[[vk::combinedImageSampler]] [[vk::binding(2, 2)]] SamplerState PrevSmp : register(s2, space2);
[[vk::combinedImageSampler]] [[vk::binding(3, 2)]] Texture2D<float4> SpareTex : register(t3, space2);   // (sampler slots stay even)
[[vk::combinedImageSampler]] [[vk::binding(3, 2)]] SamplerState SpareSmp : register(s3, space2);

#include "ModernCommon.hlsli"
#include "ModernWater.hlsli"

cbuffer Ssr : register(b1, space3)
{
    float4x4 PrevViewProj;  // the previous frame's view-projection, without its jitter
    float4 Params;          // x: steps; y: the roughness above which nothing is traced; z: the waves' clock; w: the longest ray (units)
    float4 Params2;         // x: 1 = the previous frame's picture is there; y: 1 = traced at half resolution; z: the frame's number; w: unused
};

struct TraceOut
{
    float4 Light : SV_Target0;   // rgb: the reflected light times the confidence; a: the confidence
    float2 Hit : SV_Target1;     // x: the hit's distance from the surface (m); y: the surface's roughness (−1: not traced)
};

ScreenOut VSMain(uint id : SV_VertexID) { return ScreenTriangle(id); }

float2 UvOf(float2 ndc) { return float2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5); }

TraceOut PSMain(ScreenOut i)
{
    TraceOut o;
    o.Light = 0;
    o.Hit = float2(0.0, -1.0);

    // The surface: at half resolution the smoothest of the four pixels this one covers.
    float2 uv = i.Uv;
    float4 nr = NormalTex.SampleLevel(NormalSmp, uv, 0);
    if (Params2.y > 0.5)
    {
        float2 q = Screen.zw * 0.5;
        float2 c[4] = { uv + float2(-q.x, -q.y), uv + float2(q.x, -q.y), uv + float2(-q.x, q.y), uv + float2(q.x, q.y) };
        nr = NormalTex.SampleLevel(NormalSmp, c[0], 0);
        uv = c[0];
        [unroll] for (int k = 1; k < 4; k++)
        {
            float4 t = NormalTex.SampleLevel(NormalSmp, c[k], 0);
            if (t.a > nr.a) { nr = t; uv = c[k]; }
        }
    }
    if (nr.a <= 0.004) return o;
    float depth = DepthTex.SampleLevel(DepthSmp, uv, 0);
    if (depth <= 0.0) return o;
    float3 world = WorldFromDepth(uv, depth);
    float3 toEye = CameraPos.xyz - world;
    float dist = length(toEye);
    float3 v = toEye / dist;
    bool water = nr.a > 0.995;
    float3 n = water ? WaterNormal(world, Params.z, CameraPos.xyz) : normalize(nr.xyz * 2.0 - 1.0);
    float roughness = water ? 0.05 : (1.0 - nr.a / 0.99) * Params.y;
    o.Hit.y = roughness;
    float3 r = reflect(-v, n);
    if (dot(r, n) <= 0.0 || Params2.x < 0.5) return o;

    // The ray's ends on the screen: from just off the surface (so it does not find the surface itself) to its
    // full length, cut where it would pass the near plane.
    float len = Params.w;
    float3 start = world + n * (1.5 + dist * 0.002);
    float4 c0 = mul(ViewProj, float4(start, 1.0));
    float4 c1 = mul(ViewProj, float4(start + r * len, 1.0));
    float nearW = FogSun.y * 2.0;
    if (c1.w < nearW)
    {
        float t = (c0.w - nearW) / max(c0.w - c1.w, 1e-3);
        c1 = lerp(c0, c1, saturate(t));
    }
    // On the screen the ray is a straight line; 1/w varies linearly along it.
    float2 p0 = c0.xy / c0.w, p1 = c1.xy / c1.w;
    float k0 = 1.0 / c0.w, k1 = 1.0 / c1.w;
    // Cut at the screen's edge: the march covers only what the screen shows.
    float2 d = p1 - p0;
    float sEnd = 1.0;
    if (d.x > 1e-6) sEnd = min(sEnd, (1.0 - p0.x) / d.x);
    if (d.x < -1e-6) sEnd = min(sEnd, (-1.0 - p0.x) / d.x);
    if (d.y > 1e-6) sEnd = min(sEnd, (1.0 - p0.y) / d.y);
    if (d.y < -1e-6) sEnd = min(sEnd, (-1.0 - p0.y) / d.y);
    sEnd = saturate(sEnd);
    if (sEnd <= 0.0) return o;

    int steps = max((int)Params.x, 1);
    float jitter = Ign(i.Pos.xy + Params2.z * 7.0);
    float hitS = -1.0, prevS = 0.0, gap = 0.0, tolerance = 1.0;
    bool wasInFront = true;
    [loop] for (int k = 1; k <= steps; k++)
    {
        float s = (k - 1.0 + jitter) / steps;
        s = s * (0.35 + 0.65 * s) * sEnd;           // somewhat finer near the surface
        float2 p = lerp(p0, p1, s);
        float zr = 1.0 / lerp(k0, k1, s);           // the ray's distance along the view axis
        float zs = LinearDepth(DepthTex.SampleLevel(DepthSmp, UvOf(p), 0));
        bool inFront = zr <= zs;
        if (!inFront)
        {
            // The ray is behind the depth buffer. If it was in front a step ago it went behind in between: find
            // where in halvings, and take it as a hit if there it lies on that surface (within half a metre and
            // 2% of the distance), not far behind a nearer one's silhouette. If it was already behind something
            // nearer (a post, a planter) and is now behind this surface by less than it advanced in the step, it
            // met this surface within the step.
            float a = prevS, b = s;
            if (wasInFront)
            {
                [unroll] for (int j = 0; j < 6; j++)
                {
                    float m = (a + b) * 0.5;
                    float2 pm = lerp(p0, p1, m);
                    if (1.0 / lerp(k0, k1, m) > LinearDepth(DepthTex.SampleLevel(DepthSmp, UvOf(pm), 0))) b = m; else a = m;
                }
            }
            float zb = 1.0 / lerp(k0, k1, b);
            float zsb = LinearDepth(DepthTex.SampleLevel(DepthSmp, UvOf(lerp(p0, p1, b)), 0));
            float tol = 36.0 + zsb * 0.02 + (wasInFront ? 0.0 : abs(zr - 1.0 / lerp(k0, k1, prevS)));
            if (zb - zsb < tol) { hitS = b; gap = zb - zsb; tolerance = tol; break; }
        }
        wasInFront = inFront;
        prevS = s;
    }
    if (hitS < 0.0) return o;

    float2 hitUv = UvOf(lerp(p0, p1, hitS));
    float hitDepth = DepthTex.SampleLevel(DepthSmp, hitUv, 0);
    if (hitDepth <= 0.0) return o;   // the sky is no hit
    float3 hitWorld = WorldFromDepth(hitUv, hitDepth);
    // A surface seen from behind (the inside of a wall the ray passed into) is no hit.
    float3 hn = normalize(NormalTex.SampleLevel(NormalSmp, hitUv, 0).xyz * 2.0 - 1.0);
    float facing = saturate(-dot(hn, r) * 4.0 + 0.4);
    // Where that surface stood in the previous frame's picture.
    float4 pc = mul(PrevViewProj, float4(hitWorld, 1.0));
    if (pc.w <= 0.0) return o;
    float2 puv = UvOf(pc.xy / pc.w);
    if (any(puv < 0.0) || any(puv > 1.0)) return o;
    float3 light = max(PrevTex.SampleLevel(PrevSmp, puv, 0).rgb, 0.0);

    // The confidence: faded over the last 10% of the screen towards its edges (on this frame's and the last
    // one's), over the ray's last fifth, for rays turning back towards the eye, and with how deep behind the
    // surface the crossing was found.
    float2 e = min(hitUv, 1.0 - hitUv), pe = min(puv, 1.0 - puv);
    float edge = smoothstep(0.0, 0.1, min(e.x, e.y)) * smoothstep(0.0, 0.05, min(pe.x, pe.y));
    float travelled = length(hitWorld - world) / max(Params.w, 1.0);
    float far = 1.0 - smoothstep(0.8, 1.0, travelled);
    float back = 1.0 - smoothstep(0.6, 0.9, dot(r, v));
    float deep = 1.0 - smoothstep(0.5, 1.0, gap / max(tolerance, 1.0));
    float confidence = saturate(edge * far * back * deep * facing);
    o.Light = float4(light * confidence, confidence);
    o.Hit.x = length(hitWorld - world) / 72.0;
    return o;
}
