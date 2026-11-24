// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Shaders/ModernVolume.hlsl at b4185ef, reworked for the platform.
// Volumetric sun shafts for the modern path (ours): at half resolution, each
// pixel's view ray is marched through the height fog to the surface the depth buffer holds (or far out,
// for the sky), and at each step the sun's cascaded shadow map says whether the sun reaches that bit of
// air. The steps crowd towards the eye and are jittered per pixel; each step's weight is the fog's
// exact share of scattering over its segment, T(a) − T(b), so the result is the lit fraction of the
// ray's in-scatter: 1 − T(end) with no shadows at all. Beyond where the shadows end the air counts as
// lit, under the clouds' shadows. The shaders multiply it by the sun's colour and phase (ModernCommon.hlsli's SunInscatter).
// Output: that fraction and the view distance in metres (for ModernBlur.hlsl).

#include "ModernCommon.hlsli"

cbuffer Volume : register(b1, space3)
{
    float4 VolParams;   // x: steps; yzw unused
};

[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] Texture2D<float> DepthTex : register(t0, space2);
[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] SamplerState DepthSmp : register(s0, space2);
[[vk::combinedImageSampler]] [[vk::binding(1, 2)]] Texture2D<float> ShadowTex : register(t1, space2);
[[vk::combinedImageSampler]] [[vk::binding(1, 2)]] SamplerComparisonState ShadowSmp : register(s1, space2);

#include "ModernShadows.hlsli"

ScreenOut VSMain(uint id : SV_VertexID) { return ScreenTriangle(id); }

float4 PSMain(ScreenOut i) : SV_Target0
{
    float depth = DepthTex.SampleLevel(DepthSmp, i.Uv, 0);
    float3 d = ViewRay(i.Uv);
    float fwd = max(dot(d, CameraForward.xyz), 1e-3);
    float dist = depth > 0.0 ? LinearDepth(depth) / fwd : 2.0e6;
    float end = min(dist, CameraForward.w / fwd);
    int steps = (int)VolParams.x;
    float jitter = Ign(i.Pos.xy);
    // The clouds' shadows (ModernClouds.hlsli) at the eye, where the shadow maps end and far out,
    // between them in straight lines: they are hundreds of metres across, so three looks are enough.
    float cloudEye = CloudShadow(CameraPos.xyz), cloudEnd = CloudShadow(CameraPos.xyz + d * end);
    float cloudFar = CloudShadow(CameraPos.xyz + d * min(dist, end + 72000.0));
    float lit = 0.0, a = 0.0, ta = 1.0;
    for (int k = 0; k < steps; k++)
    {
        float f = (k + 1.0) / steps;
        float b = end * f * f;
        float s = lerp(a, b, jitter);
        float tb = FogTransmittance(CameraPos.xyz, d, b);
        lit += ShadowPoint(CameraPos.xyz + d * s, s * fwd) * lerp(cloudEye, cloudEnd, s / max(end, 1.0)) * (ta - tb);
        a = b; ta = tb;
    }
    lit += (ta - FogTransmittance(CameraPos.xyz, d, dist)) * lerp(cloudEnd, cloudFar, 0.5);
    return float4(lit, depth > 0.0 ? LinearDepth(depth) / 72.0 : 60000.0, 0, 0);
}
