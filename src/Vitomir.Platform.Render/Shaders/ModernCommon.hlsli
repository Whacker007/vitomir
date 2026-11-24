// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Shaders/ModernCommon.hlsli at b4185ef, reworked for the platform.
// What the modern path's shaders share (ours): the frame's constants, the sky, the
// height fog and the sun's in-scatter, the ambient light's spherical harmonics, and reading the depth
// buffer back into positions. Included by the Modern*.hlsl shaders; the C# side is ModernRenderer.FrameUniforms.
// World space is the engine's: Z up, 72 units a metre.

#ifndef MODERN_COMMON_HLSLI
#define MODERN_COMMON_HLSLI

cbuffer Frame : register(b0, space3)
{
    float4 CameraPos;       // xyz eye; w: 1 = the volumetric pass owns the sun's in-scatter
    float4 CameraForward;   // xyz; w: distance where shadows end
    float4 CameraRight;     // xyz; w: x scale of the projection (1 / tan(half horizontal fov))
    float4 CameraUp;        // xyz; w: y scale of the projection
    float4 SunDir;          // xyz towards the sun; w: intensity
    float4 SunColor;        // rgb colour; w: cos of the sun disc's angular radius
    float4 SkyZenith;       // rgb radiance straight up; w: cloud cover 0..1
    float4 SkyHorizon;      // rgb radiance at the horizon; w: 1 = a gradient sky (a look's), 0 = the procedural one
    float4 Ground;          // rgb radiance of the ground seen from above; w: sun disc brightness
    float4 SkyGlow;         // rgb a glow low on the horizon (dusk); w: the screen's reflections are read (ModernPbr.hlsl's SsrTex) below this roughness, 0: none this frame (ours)
    float4 SkyGlowDir;      // xyz towards the glow; w: the clouds from the half-resolution pass (ModernCloudPass.hlsl): 1, read 4 × 4; 2 (TAA), 2 × 2; 0 marched by the sky
    float4 Fog;             // x: extinction per unit at height z; y: height falloff per unit; z: reference height; w: ambient in-scatter scale
    float4 FogColour;       // rgb: the haze's ambient in-scatter colour; w: Henyey-Greenstein g of the sun's in-scatter
    float4 FogSun;          // x: the sun's in-scatter scale; y: near plane; z: the frame's number (0 without temporal anti-aliasing); w: 1 = specular anti-aliasing (ours)
    float4 Splits;          // far distance of cascades 0..3
    float4 ShadowParams;    // x: 1 / atlas size; y: normal offset in texels; z: cascade count; w: 1 = shadows on
    float4 CascadeTexel;    // world size of one texel in each cascade
    float4 ShadowSoft;      // x: penumbra radius in world units (the sun's size); yzw unused
    float4 Screen;          // xy: target size in pixels; zw: 1 / size
    float4 Options;         // x: 1 = ambient occlusion; y: light count; z: specular occlusion; w: 1 = lights on
    float4 Cluster;         // x, y: tiles across and down; z: depth slices; w: slices / ln(far / near)
    float4 ClusterDepth;    // x: near of the first slice; y: tile size in pixels; z: 1 / the light shadow atlas's width; w: its rows (lights)
    float4 ShSky[9];        // irradiance from the open sky and ground, as convolved spherical harmonics (rgb)
    float4 ShBounce[9];     // irradiance from the surroundings, for directions ambient occlusion finds blocked
    float4x4 ViewProj;
    float4x4 InvViewProj;
    float4x4 Cascade[4];
    float4 CloudShape;      // cumulus (ModernClouds.hlsli): x the field's threshold (1 or more: none); y 1 / its headroom; z base, w depth (km)
    float4 CloudMotion;     // xy: the wind's drift (lattice units); z: lattice units per km; w: march steps (0: a flat layer)
    float4 CloudLight;      // x: extinction per km; y: the sun's scale in them; z: their shadows' strength 0..1; w: their fade distance (km)
    float4 Atmo;            // the physically based sky (ModernAtmosphere.hlsli; ours): x 1 = on; y the sun's illuminance above the air; z the eye's altitude (km); w the horizon's elevation there (radians)
    float4 ProbeOrigin;     // the light probes (ModernProbes.hlsli; ours): xyz the first probe (world); w 1 = on
    float4 ProbeGrid;       // x: probes per world unit (1 / spacing); y: how far a surface's point is lifted off it along its normal (units); z: a room's rise (units); w: the spacing (units)
    float4 ProbeDims;       // xyz: probes along x, y, z; w: how many rooms follow
    float4 ProbeRoomA[4];   // the rooms in the probes (ours; ModernProbeRoom.cs): xyz the box's lowest corner (world); w the cosine of its turn
    float4 ProbeRoomB[4];   // xyz its sizes along its own sides; w the sine of its turn
};

static const float PI = 3.14159265;

// ----- the full-screen triangle -----

struct ScreenOut
{
    float4 Pos : SV_Position;
    float2 Uv : TEXCOORD0;
};

ScreenOut ScreenTriangle(uint id)
{
    float2 uv = float2((id << 1) & 2, id & 2);
    ScreenOut o;
    o.Uv = uv;
    o.Pos = float4(uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    return o;
}

// A world position from a texture coordinate on the screen and a reversed depth (0 is infinitely far).
float3 WorldFromDepth(float2 uv, float depth)
{
    float4 p = mul(InvViewProj, float4(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0, max(depth, 1e-7), 1.0));
    return p.xyz / p.w;
}

// The distance along the view axis for a reversed, infinite depth: near / depth.
float LinearDepth(float depth) { return FogSun.y / max(depth, 1e-7); }

// The world direction through a point of the screen.
float3 ViewRay(float2 uv)
{
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    return normalize(CameraForward.xyz + CameraRight.xyz * ndc.x / CameraRight.w + CameraUp.xyz * ndc.y / CameraUp.w);
}

// Interleaved gradient noise (Jimenez): a cheap per-pixel dither that averages out under a small blur.
// With temporal anti-aliasing the pattern moves each frame (FogSun.z counts frames), so it averages away.
float Ign(float2 pixel)
{
    pixel += FogSun.z * 5.588238;
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

// ----- the sky -----

#ifdef MODERN_ATMOSPHERE
#include "ModernAtmosphere.hlsli"
#endif

float3 SkyRadiance(float3 d)
{
#ifdef MODERN_ATMOSPHERE
    if (AtmosphereOn()) return AtmoSky(d);
#endif
    float3 sky;
    if (SkyHorizon.w > 0.5)
    {
        // A look's gradient sky: horizon to zenith, as the concept scenes' Blender world gives it.
        float f = pow(saturate((d.z + 0.02) / 0.57), 0.6);
        sky = lerp(SkyHorizon.rgb, SkyZenith.rgb, f);
        float g = pow(saturate((dot(d, SkyGlowDir.xyz) - 0.3) / 0.7), 3.0) * saturate(1.0 - d.z / 0.3);
        sky += SkyGlow.rgb * g;
    }
    else
    {
        float up = saturate(d.z);
        sky = lerp(SkyHorizon.rgb, SkyZenith.rgb, pow(up, 0.45));
        // Below the horizon: the haze thickens towards the ground's colour.
        float down = saturate(-d.z);
        sky = lerp(sky, SkyHorizon.rgb * 0.6 + Ground.rgb * 0.4, saturate(down * 4.0));
    }
    // The bright aureole round the sun (forward scattering), weaker as the sky clouds over.
    float mu = saturate(dot(d, SunDir.xyz));
    float clear = 1.0 - SkyZenith.w;
    sky += SunColor.rgb * SunDir.w * clear * (0.012 * pow(mu, 8.0) + 0.06 * pow(mu, 200.0));
    // A low sun warms the band of sky round it near the horizon (the long, reddened path of its light).
    float low = 1.0 - saturate(SunDir.z * 4.0);
    float band = exp(-abs(d.z) * 6.0);
    float2 across = normalize(d.xy + 1e-5), sunAcross = normalize(SunDir.xy + 1e-5);
    float side = saturate(dot(across, sunAcross) * 0.5 + 0.5);
    sky += SunColor.rgb * SunDir.w * clear * 0.05 * low * band * pow(side, 4.0);
    return sky;
}

// ----- the haze -----

// Henyey-Greenstein's phase function: how much light scattered by the haze goes off at angle acos(mu).
float PhaseHg(float mu, float g)
{
    float g2 = g * g;
    return (1.0 - g2) / (4.0 * PI * pow(max(1.0 + g2 - 2.0 * g * mu, 1e-4), 1.5));
}

// The sun's light scattered towards the eye per unit of the haze's optical depth.
float3 SunInscatter(float3 d)
{
    return SunColor.rgb * SunDir.w * FogSun.x * PhaseHg(dot(d, SunDir.xyz), FogColour.w);
}

// Light scattered into a view ray by the haze: the sky's ambient light, and the sun's unless the
// volumetric pass owns it (then it comes shadowed, from its own texture).
float3 InscatterColour(float3 d)
{
    float3 c = FogColour.rgb * Fog.w;
    if (CameraPos.w < 0.5) c += SunInscatter(d);
    return c;
}

// Transmittance along a ray through fog whose density falls off exponentially with height.
float FogTransmittance(float3 origin, float3 d, float dist)
{
    float b = Fog.y;
    float start = Fog.x * exp(-b * (origin.z - Fog.z));
    float k = b * d.z * dist;
    float optical = abs(k) > 1e-4 ? start * (1.0 - exp(-k)) / (b * d.z) : start * dist;
    return exp(-max(optical, 0.0));
}

float3 ApplyFog(float3 colour, float3 world)
{
    float3 v = world - CameraPos.xyz;
    float dist = length(v);
    float3 d = v / max(dist, 1e-3);
    float t = FogTransmittance(CameraPos.xyz, d, dist);
    colour = colour * t + InscatterColour(d) * (1.0 - t);
#ifdef MODERN_ATMOSPHERE
    // And the air at large (ours): the distance's blue and its haze, by the atmosphere's tables.
    if (AtmosphereOn()) colour = AtmoApply(colour, world);
#endif
    return colour;
}

// ----- ambient light -----

// Irradiance for a surface facing n from nine convolved spherical-harmonic coefficients (Ramamoorthi
// and Hanrahan's basis; the convolution with the cosine is folded in on the CPU, ModernAmbient.cs).
float3 ShIrradiance(float4 sh[9], float3 n)
{
    float3 e = sh[0].rgb * 0.282095
             + sh[1].rgb * (0.488603 * n.y) + sh[2].rgb * (0.488603 * n.z) + sh[3].rgb * (0.488603 * n.x)
             + sh[4].rgb * (1.092548 * n.x * n.y) + sh[5].rgb * (1.092548 * n.y * n.z)
             + sh[6].rgb * (0.315392 * (3.0 * n.z * n.z - 1.0))
             + sh[7].rgb * (1.092548 * n.x * n.z) + sh[8].rgb * (0.546274 * (n.x * n.x - n.y * n.y));
    return max(e, 0.0);
}

#include "ModernClouds.hlsli"

#endif
