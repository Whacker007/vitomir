// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Temporal anti-aliasing for the modern path (ours): each frame the projection is
// shifted by a different fraction of a pixel (a Halton sequence), and this pass blends the new frame
// into the running history, found where each pixel was last frame (its world position, from the depth
// buffer, through last frame's view-projection; the sky by its direction). The history is clamped to the
// new frame's 3x3 neighbourhood of colours (in YCoCg), so what moved or appeared does not leave ghosts.
// Over a few frames each pixel averages many sub-pixel positions: edges and the shimmer inside surfaces
// smooth out, and so does the ambient occlusion's and the shafts' per-pixel noise, which also changes
// each frame. A light unsharp mask on the new frame keeps textures from softening.

#include "ModernCommon.hlsli"

cbuffer Taa : register(b1, space3)
{
    float4x4 PrevViewProj;
    float4x4 InvUnjittered;   // this frame's view-projection without the jitter, inverted
    float4 TaaParams;         // x: the new frame's weight; y: 1 = there is a history
};

[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] Texture2D<float4> CurrentTex : register(t0, space2);
[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] SamplerState CurrentSmp : register(s0, space2);
[[vk::combinedImageSampler]] [[vk::binding(1, 2)]] Texture2D<float4> HistoryTex : register(t1, space2);
[[vk::combinedImageSampler]] [[vk::binding(1, 2)]] SamplerState HistorySmp : register(s1, space2);
[[vk::combinedImageSampler]] [[vk::binding(2, 2)]] Texture2D<float> DepthTex : register(t2, space2);
[[vk::combinedImageSampler]] [[vk::binding(2, 2)]] SamplerState DepthSmp : register(s2, space2);
[[vk::combinedImageSampler]] [[vk::binding(3, 2)]] Texture2D<float4> SpareTex : register(t3, space2);
[[vk::combinedImageSampler]] [[vk::binding(3, 2)]] SamplerState SpareSmp : register(s3, space2);

ScreenOut VSMain(uint id : SV_VertexID) { return ScreenTriangle(id); }

float3 ToYCoCg(float3 c) { return float3(dot(c, float3(0.25, 0.5, 0.25)), dot(c, float3(0.5, 0.0, -0.5)), dot(c, float3(-0.25, 0.5, -0.25))); }
float3 FromYCoCg(float3 c) { return float3(c.x + c.y - c.z, c.x + c.z, c.x - c.y - c.z); }

float4 PSMain(ScreenOut i) : SV_Target0
{
    float3 current = max(CurrentTex.SampleLevel(CurrentSmp, i.Uv, 0).rgb, 0.0);
    if (TaaParams.y < 0.5) return float4(current, 1.0);
    // The neighbourhood's range of colours; a tone-mapped weight (1 / (1 + luma)) keeps bright pixels from ruling it.
    float3 lo = 1e9, hi = -1e9, mean = 0;
    [unroll] for (int y = -1; y <= 1; y++)
        [unroll] for (int x = -1; x <= 1; x++)
        {
            float3 rgb = max(CurrentTex.SampleLevel(CurrentSmp, i.Uv + float2(x, y) * Screen.zw, 0).rgb, 0.0);
            float3 c = ToYCoCg(rgb);
            lo = min(lo, c); hi = max(hi, c);
            mean += rgb / 9.0;
        }
    // The jittered frames average to a pixel-wide box filter; a light unsharp mask on the new frame
    // gives back the texture detail that softens.
    current = max(current + (current - mean) * 0.35, 0.0);
    // Where this pixel was last frame.
    float depth = DepthTex.SampleLevel(DepthSmp, i.Uv, 0);
    // (Without the jitter on either side, so a still camera finds each pixel exactly where it was.)
    float4 w = mul(InvUnjittered, float4(i.Uv.x * 2.0 - 1.0, 1.0 - i.Uv.y * 2.0, max(depth, 1e-7), 1.0));
    float4 prev = depth > 0.0 ? mul(PrevViewProj, float4(w.xyz / w.w, 1.0))
                              : mul(PrevViewProj, float4(ViewRay(i.Uv), 0.0));
    float2 puv = float2(prev.x / prev.w * 0.5 + 0.5, 0.5 - prev.y / prev.w * 0.5);
    if (prev.w <= 0.0 || any(puv < 0.0) || any(puv > 1.0)) return float4(current, 1.0);
    float3 history = ToYCoCg(max(HistoryTex.SampleLevel(HistorySmp, puv, 0).rgb, 0.0));
    history = FromYCoCg(clamp(history, lo, hi));
    float wc = TaaParams.x / (1.0 + dot(current, float3(0.2126, 0.7152, 0.0722)));
    float wh = (1.0 - TaaParams.x) / (1.0 + dot(history, float3(0.2126, 0.7152, 0.0722)));
    return float4((current * wc + history * wh) / (wc + wh), 1.0);
}
