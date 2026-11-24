// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Shaders/ModernSsrResolve.hlsl at b4185ef, reworked for the platform.
// Screen-space reflections, the resolve (ours): the trace's
// result (ModernSsr.hlsl) filtered in space by the surface's roughness and accumulated over frames, before the
// shading pass reads it (ModernPbr.hlsl). The filter is a disc of twelve taps, turned per pixel and per frame,
// whose radius is the blur a GGX lobe of the surface's roughness gives the reflected surface at the hit's
// distance, seen from the eye (at least one texel, so the march's stepping does not show as speckle); a tap counts
// only on the same surface (its depth within 2% of where the centre's plane would put it: across a plane the
// reversed depth is linear on the screen, so the centre's screen derivatives of it predict it) and is weighted
// down by its brightness so a lone bright hit does not spread. The history is found where the reflecting surface stood in
// the previous frame and clipped to the taps' spread of light (mean ± 1.25 standard deviations) and range of
// confidence, so what the reflection showed a moment ago does not stay behind as a ghost when the camera moves.
// Light and confidence stay premultiplied throughout: a texel with nothing traced is 0 and weighs nothing in a
// blend or in the bilinear read the shading pass makes.

[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] Texture2D<float4> TraceTex : register(t0, space2);
[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] SamplerState TraceSmp : register(s0, space2);
[[vk::combinedImageSampler]] [[vk::binding(1, 2)]] Texture2D<float2> HitTex : register(t1, space2);
[[vk::combinedImageSampler]] [[vk::binding(1, 2)]] SamplerState HitSmp : register(s1, space2);
[[vk::combinedImageSampler]] [[vk::binding(2, 2)]] Texture2D<float> DepthTex : register(t2, space2);
[[vk::combinedImageSampler]] [[vk::binding(2, 2)]] SamplerState DepthSmp : register(s2, space2);
[[vk::combinedImageSampler]] [[vk::binding(3, 2)]] Texture2D<float4> HistoryTex : register(t3, space2);
[[vk::combinedImageSampler]] [[vk::binding(3, 2)]] SamplerState HistorySmp : register(s3, space2);

#include "ModernCommon.hlsli"

cbuffer Ssr : register(b1, space3)
{
    float4x4 PrevViewProj;  // the previous frame's view-projection, without its jitter
    float4 Params;          // as the trace's
    float4 Params2;         // x, y, z as the trace's; w: 1 = the history is there
    float4 Params3;         // xy: one texel of the reflections' target (uv); z: the new frame's weight in the history; w: unused
};

ScreenOut VSMain(uint id : SV_VertexID) { return ScreenTriangle(id); }

float Luma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

float4 PSMain(ScreenOut i) : SV_Target0
{
    float2 uv = i.Uv;
    float depth = DepthTex.SampleLevel(DepthSmp, uv, 0);
    // How the depth changes across this surface, per texel of this target (taken before anything returns).
    float2 slope = float2(ddx(depth), ddy(depth));
    float2 hit = HitTex.SampleLevel(HitSmp, uv, 0);
    if (hit.y < 0.0 || depth <= 0.0) return 0;
    float d0 = LinearDepth(depth);
    float3 world = WorldFromDepth(uv, depth);
    float4 centre = TraceTex.SampleLevel(TraceSmp, uv, 0);

    // The blur: a GGX lobe of roughness r spreads about α = r² radians either side (ours, a fit good enough for
    // the filter's size), so a surface hit h metres away is blurred over h·α, seen from the eye h metres further
    // than the reflecting surface.
    float alpha = hit.y * hit.y;
    float hitUnits = (centre.a > 0.0 ? hit.x : 3.0) * 72.0;
    float spread = hitUnits * alpha;
    float texels = spread / max(d0 + hitUnits, 1.0) * CameraUp.w * 0.5 / Params3.y;
    float radius = clamp(texels, 1.0, 12.0);

    float rot = Ign(i.Pos.xy + Params2.z * 11.0) * 6.2831853;
    float4 sum = centre / (1.0 + Luma(centre.rgb));
    float weights = 1.0 / (1.0 + Luma(centre.rgb));
    float3 m1 = 0, m2 = 0;
    float wl = 0, lo = centre.a, hi = centre.a;
    if (centre.a > 0.02)
    {
        float3 l = centre.rgb / centre.a;
        m1 += l; m2 += l * l; wl += 1.0;
    }
    [unroll] for (int k = 0; k < 12; k++)
    {
        float rr = sqrt((k + 0.5) / 12.0);
        float ang = k * 2.3999632 + rot;
        float2 offset = float2(cos(ang), sin(ang)) * rr * radius;
        float2 tuv = uv + offset * Params3.xy;
        float2 th = HitTex.SampleLevel(HitSmp, tuv, 0);
        if (th.y < 0.0) continue;
        float td = DepthTex.SampleLevel(DepthSmp, tuv, 0);
        if (td <= 0.0 || abs(td - depth - dot(slope, offset)) > 0.02 * depth) continue;
        float4 t = TraceTex.SampleLevel(TraceSmp, tuv, 0);
        float w = exp(-2.0 * rr * rr) / (1.0 + Luma(t.rgb));
        sum += t * w; weights += w;
        lo = min(lo, t.a); hi = max(hi, t.a);
        if (t.a > 0.02)
        {
            float3 l = t.rgb / t.a;
            m1 += l; m2 += l * l; wl += 1.0;
        }
    }
    float4 current = sum / weights;

    // The history, where this surface stood last frame, clipped to what the taps found.
    float4 pc = mul(PrevViewProj, float4(world, 1.0));
    if (Params2.w < 0.5 || pc.w <= 0.0) return current;
    float2 puv = float2(pc.x / pc.w * 0.5 + 0.5, 0.5 - pc.y / pc.w * 0.5);
    if (any(puv < 0.0) || any(puv > 1.0)) return current;
    float4 history = HistoryTex.SampleLevel(HistorySmp, puv, 0);
    float ha = clamp(history.a, lo, hi);
    float3 hl = 0;
    if (wl > 0.0)
    {
        float3 mean = m1 / wl;
        float3 sigma = sqrt(max(m2 / wl - mean * mean, 0.0));
        hl = clamp(history.rgb / max(history.a, 1e-3), mean - 1.25 * sigma, mean + 1.25 * sigma);
    }
    history = float4(hl * ha, ha);
    return lerp(history, current, Params3.z);
}
