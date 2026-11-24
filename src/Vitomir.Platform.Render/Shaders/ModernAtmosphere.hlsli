// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Shaders/ModernAtmosphere.hlsli at b4185ef, reworked for the platform.
// Reading the physically based atmosphere's tables (ours; ModernAtmosphere.cs makes them on the CPU, after Hillaire 2020): the sky's view, and the aerial
// perspective's in-scattered light and transmittance by direction and distance. A shader that includes this
// declares SkyViewTex, ApInTex and ApTrTex (with their samplers) and defines MODERN_ATMOSPHERE before
// including ModernCommon.hlsli. The mappings are ModernAtmosphere.SkyUv and ApV, kept in step by hand.

#ifndef MODERN_ATMOSPHERE_HLSLI
#define MODERN_ATMOSPHERE_HLSLI

static const float AtmoApRangeKm = 64.0;
static const float AtmoApSize = 32.0;      // directions round and up, and slices
static const float2 AtmoApAtlas = float2(256.0, 128.0);   // 8 slices across, 4 down

bool AtmosphereOn() { return Atmo.x > 0.5; }

// A direction's angle round from the sun's bearing (0..π) and its elevation (radians).
float2 AtmoAngles(float3 d)
{
    float2 a = d.xy, b = SunDir.xy;
    float la = length(a), lb = length(b);
    float phi = la < 1e-5 || lb < 1e-5 ? 0.0 : acos(clamp(dot(a, b) / (la * lb), -1.0, 1.0));
    return float2(phi, asin(clamp(d.z, -1.0, 1.0)));
}

// The sky's light from direction d, times the sun's illuminance above the air.
float3 AtmoSky(float3 d)
{
    float2 a = AtmoAngles(d);
    float horizon = Atmo.w;
    float u = sqrt(saturate(a.x / PI));
    float v = a.y >= horizon ? 0.5 - 0.5 * sqrt(saturate((a.y - horizon) / (PI * 0.5 - horizon)))
                             : 0.5 + 0.5 * sqrt(saturate((horizon - a.y) / (horizon + PI * 0.5)));
    return SkyViewTex.SampleLevel(SkyViewSmp, float2(u, v), 0).rgb * Atmo.y;
}

// One slice of the aerial perspective's atlas at a place inside its tile (texels).
float2 AtmoApUv(int k, float2 inTile)
{
    return (float2((k % 8) * AtmoApSize, (k / 8) * AtmoApSize) + inTile) / AtmoApAtlas;
}

// The air between the eye and a point km away along d: the light it adds (times the sun's illuminance) and
// the light it lets through.
void AtmoAerial(float3 d, float km, out float3 inscatter, out float3 transmittance)
{
    float2 a = AtmoAngles(d);
    float u = saturate(a.x / PI);
    float v = 0.5 + 0.5 * sign(a.y) * sqrt(saturate(abs(a.y) / (PI * 0.5)));
    float2 inTile = clamp(float2(u, v) * AtmoApSize, 0.5, AtmoApSize - 0.5);
    float s = sqrt(saturate(km / AtmoApRangeKm)) * AtmoApSize - 1.0;
    float3 l0 = 0, t0 = 1;
    int k = (int)floor(s);
    float f = s - k;
    if (k >= 0)
    {
        float2 uv = AtmoApUv(k, inTile);
        l0 = ApInTex.SampleLevel(ApInSmp, uv, 0).rgb;
        t0 = ApTrTex.SampleLevel(ApTrSmp, uv, 0).rgb;
    }
    int k1 = min(k + 1, (int)AtmoApSize - 1);
    float2 uv1 = AtmoApUv(k1, inTile);
    float3 l1 = ApInTex.SampleLevel(ApInSmp, uv1, 0).rgb, t1 = ApTrTex.SampleLevel(ApTrSmp, uv1, 0).rgb;
    inscatter = lerp(l0, l1, saturate(f)) * Atmo.y;
    transmittance = lerp(t0, t1, saturate(f));
}

// A surface's light seen through the air to it.
float3 AtmoApply(float3 colour, float3 world)
{
    float3 v = world - CameraPos.xyz;
    float dist = length(v);
    float3 l, t;
    AtmoAerial(v / max(dist, 1e-3), dist / 72000.0, l, t);
    return colour * t + l;
}

#endif
