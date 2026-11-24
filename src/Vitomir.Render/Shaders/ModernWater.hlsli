// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Open water (ours; Modern/ModernWater.cs): the normal of a calm river,
// four deep-water wave trains (each moving at its own speed, w = sqrt(g k)) summed as slopes in metres,
// fading with distance so the far water lies flat instead of shimmering. The lighting is the surface
// shader's own: the sky reflected through the Fresnel term, the sun's glint from the low roughness.

#ifndef MODERN_WATER_HLSLI
#define MODERN_WATER_HLSLI

float3 WaterNormal(float3 world, float t, float3 eye)
{
    float2 p = world.xy / 72.0;
    // direction (xy), wavelength in metres, slope amplitude
    const float4 waves[4] = { float4(0.80, 0.60, 6.5, 0.060), float4(-0.45, 0.89, 3.3, 0.045),
                              float4(0.25, -0.97, 1.6, 0.035), float4(-0.92, -0.38, 0.85, 0.025) };
    float2 g = 0;
    [unroll] for (int k = 0; k < 4; k++)
    {
        float kw = 2.0 * 3.14159265 / waves[k].z;
        float phase = dot(waves[k].xy, p) * kw - sqrt(9.81 * kw) * t;
        g += waves[k].xy * (waves[k].w * cos(phase));
    }
    g *= 1.0 / (1.0 + length(world - eye) / (72.0 * 250.0));
    return normalize(float3(-g.x, -g.y, 1.0));
}

#endif
