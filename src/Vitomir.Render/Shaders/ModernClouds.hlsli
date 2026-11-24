// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Fair-weather cumulus for the modern path (ours). One field drives
// three things: the clouds in the sky (ModernSky.hlsl), their shadows on the world (the sun's term in
// ModernPbr.hlsl, through ModernShadows.hlsli's Shadow, which 1.1's characters share) and in the sun
// shafts (ModernVolume.hlsl). The field is fractal gradient noise over the ground plan, periodic and
// built on an integer hash so that ModernClouds.cs reproduces it exactly on the CPU; its threshold for
// a share of the sky covered comes from the CPU too. Where the field passes its threshold a cloud
// stands on a flat base at the layer's height and rises into a dome: the stronger the field, the
// taller. Everything here works in kilometres, the layer's own scale. Included by ModernCommon.hlsli.

#ifndef MODERN_CLOUDS_HLSLI
#define MODERN_CLOUDS_HLSLI

static const float CloudEarthKm = 6371.0;
static const float CloudKmPerUnit = 1.0 / 72000.0;

bool CloudsOn() { return CloudShape.x < 1.0; }

// The integer hash (a variant of Chris Wellons's lowbias32): the same bits as ModernClouds.Hash.
uint CloudHash(int x, int y, uint seed)
{
    uint h = (uint)x * 0x8da6b343u + (uint)y * 0xd8163841u + seed * 0xcb1ab31fu;
    h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
    return h;
}

// A corner's gradient, picked by the hash's two low bits from the four diagonals, by selects alone
// (a table indexed at run time is slow on some GPUs): its dot product with the offset f.
float CloudGrad(uint h, float2 f) { return ((h & 1u) != 0u ? -f.x : f.x) + ((h & 2u) != 0u ? -f.y : f.y); }

// Gradient noise in lattice units, periodic every `period` cells (a power of two), about -0.7..0.7.
float CloudNoise(float2 p, int period, uint seed)
{
    float2 i = floor(p), f = p - i;
    int m = period - 1;
    int x0 = (int)i.x & m, y0 = (int)i.y & m, x1 = (x0 + 1) & m, y1 = (y0 + 1) & m;
    float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    float a = CloudGrad(CloudHash(x0, y0, seed), f);
    float b = CloudGrad(CloudHash(x1, y0, seed), f - float2(1, 0));
    float c = CloudGrad(CloudHash(x0, y1, seed), f - float2(0, 1));
    float d = CloudGrad(CloudHash(x1, y1, seed), f - float2(1, 1));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y) * 0.70710678;
}

// A corner's gradient in three dimensions: Perlin's twelve cube edges (his improved noise's choice
// from four bits, by selects), dotted with the offset f.
float CloudCorner(int x, int y, int z, uint seed, float3 f)
{
    uint h = CloudHash(x, y, seed + (uint)z * 0x9e3779b9u) & 15u;
    float u = h < 8u ? f.x : f.y;
    float v = h < 4u ? f.y : (h == 12u || h == 14u ? f.x : f.z);
    return ((h & 1u) != 0u ? -u : u) + ((h & 2u) != 0u ? -v : v);
}

// Gradient noise in three dimensions, periodic every `period` cells across the plan (not in height).
float CloudNoise3(float3 p, int period, uint seed)
{
    float3 i = floor(p), f = p - i;
    int m = period - 1;
    int x0 = (int)i.x & m, y0 = (int)i.y & m, x1 = (x0 + 1) & m, y1 = (y0 + 1) & m, z0 = (int)i.z, z1 = z0 + 1;
    float3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    float a = lerp(CloudCorner(x0, y0, z0, seed, f), CloudCorner(x1, y0, z0, seed, f - float3(1, 0, 0)), u.x);
    float b = lerp(CloudCorner(x0, y1, z0, seed, f - float3(0, 1, 0)), CloudCorner(x1, y1, z0, seed, f - float3(1, 1, 0)), u.x);
    float c = lerp(CloudCorner(x0, y0, z1, seed, f - float3(0, 0, 1)), CloudCorner(x1, y0, z1, seed, f - float3(1, 0, 1)), u.x);
    float d = lerp(CloudCorner(x0, y1, z1, seed, f - float3(0, 1, 1)), CloudCorner(x1, y1, z1, seed, f - float3(1, 1, 1)), u.x);
    return lerp(lerp(a, b, u.y), lerp(c, d, u.y), u.z) * 0.7;
}

// The field at a lattice point and height fraction y: 0.5 plus fractal noise, `octaves` of them (1..5;
// a fraction weights the last, so the detail can fade with distance without a seam), each twice as fine
// and half as strong as the last; about 0..1. The two broadest, over the plan, place the clouds; the
// finer ones are billows in three dimensions (the noise's magnitude: round bumps, sharp creases), a
// cumulus's cauliflower top and sides — billows on a map laid flat would be squashed into stripes on a
// cloud's side seen from afar. CloudMotion.xy (the wind's drift) is already added.
float CloudField(float2 q, float octaves, float y)
{
    float sum = 0.0, amp = 0.9;
    int period = 64;
    float z = y * CloudShape.w * CloudMotion.z;      // the height in lattice cells
    [loop] for (int k = 0; k < 5; k++)
    {
        float w = saturate(octaves - k);
        if (w <= 0.0) break;
        float n = k < 2 ? CloudNoise(q, period, 1141u + (uint)k) : CloudNoise3(float3(q, z), period, 1141u + (uint)k);
        sum += (k < 2 ? n : abs(n) * 1.6 - 0.25) * amp * w;
        q *= 2.0; z *= 2.0; period *= 2; amp *= 0.5;
    }
    return 0.5 + sum;
}

// A world position (engine units) as a lattice point of the drifting field.
float2 CloudLattice(float2 worldXy) { return worldXy * CloudKmPerUnit * CloudMotion.z + CloudMotion.xy; }
float2 CloudLatticeKm(float2 km) { return km * CloudMotion.z + CloudMotion.xy; }

// How much of the layer's depth a cloud fills over a point of the field, 0 (its edge or clear sky) to 1.
float CloudColumn(float field) { return saturate((field - CloudShape.x) * CloudShape.y); }

// The cloud's density at height fraction y (0 its base, 1 the layer's top). A cloud stands on a flat
// base, its top at the column to the power 0.6 — a dome, rounded where the column thins to its edge;
// soft at its rim (a thin column) and over the last quarter below its top.
float CloudDensity(float field, float y)
{
    float column = CloudColumn(field);
    return saturate((pow(column, 0.6) - y) * 4.0) * saturate(column * 8.0) * step(0.0, y);
}

// The sun's light through the clouds at a world point: 1 under clear sky, down to almost nothing under
// a thick cloud's middle, with soft edges. The cloud's column along the sun's ray, three octaves (soft).
float CloudShadow(float3 world)
{
    if (!CloudsOn() || CloudLight.z <= 0.0) return 1.0;
    float sz = max(SunDir.z, 0.05);
    float groundKm = (world.z - Fog.z) * CloudKmPerUnit;
    float t = (CloudShape.z + CloudShape.w * 0.5 - groundKm) / sz;       // km along the sun to the layer's middle
    float2 km = world.xy * CloudKmPerUnit + SunDir.xy * t;
    float column = CloudColumn(CloudField(CloudLatticeKm(km), 2.0, 0.0));
    float tau = CloudLight.x * column * CloudShape.w / sz * 0.5;
    return lerp(1.0, exp(-tau), CloudLight.z);
}

// The distance (km) along a ray at sine-elevation s from height h0 to the shell at height h over a round
// earth, from below; the stable root of the quadratic.
float CloudShellDistance(float h0, float h, float s)
{
    float b = (CloudEarthKm + h0) * s;
    float c = (h - h0) * (2.0 * CloudEarthKm + h + h0);
    float r = sqrt(max(b * b + c, 0.0));
    return b >= 0.0 ? c / (r + b) : r - b;
}

// Height above the ground (km) at distance t along a ray at sine-elevation s from height h0.
float CloudAltitude(float h0, float t, float s)
{
    float r0 = CloudEarthKm + h0;
    return sqrt(r0 * r0 + t * t + 2.0 * r0 * t * s) - CloudEarthKm;
}

// Henyey-Greenstein's phase, times 4π (so an even scatter is 1).
float CloudHg(float mu, float g)
{
    float g2 = g * g;
    return (1.0 - g2) / pow(max(1.0 + g2 - 2.0 * g * mu, 1e-4), 1.5);
}

// The light a cloud scatters at height fraction y with optical depth tau towards the sun, seen at cosine
// mu from the sun (ours; made it physically motivated). The sun's light: Wrenninge's multiple-
// scattering octaves (each order half as strong, lit through 0.4 of the depth, its phase half as forward),
// the first a strong forward lobe over a weak back one, so a cloud's edge towards the sun lines with silver;
// times Schneider's "powder" term, which darkens the sunlit side's crevices seen from away from the sun
// (light not yet scattered out towards the eye there). The ambient light: the sky's from above and the
// sunlit ground's from below, as the round sky and ground give them (SkyZenith, SkyHorizon, Ground — the
// physically based sky's when it is on), the top seeing mostly sky and the base mostly ground, both
// shielded deeper in: the bases go grey, the tops white.
float3 CloudLightAt(float y, float tau, float mu)
{
    float3 sun = SunColor.rgb * SunDir.w * CloudLight.y;
    float s = 0.0, a = 1.0, b = 1.0, c = 1.0;
    [unroll] for (int k = 0; k < 4; k++)
    {
        float phase = lerp(CloudHg(mu, 0.8 * c), CloudHg(mu, -0.25 * c), 0.25);
        s += a * exp(-tau * b) * phase;
        a *= 0.5; b *= 0.4; c *= 0.5;
    }
    float powder = lerp(1.0, 1.0 - 0.45 * exp(-2.0 * tau), saturate(0.5 - 0.5 * mu));
    float3 skyLight = SkyZenith.rgb * 0.55 + SkyHorizon.rgb * 0.45;
    float up = saturate(y * 1.4);
    float3 ambient = lerp(Ground.rgb * 0.9 + skyLight * 0.35, skyLight * 0.9 + Ground.rgb * 0.2, up);
    ambient *= lerp(0.6, 1.0, up) * (0.55 + 0.45 * exp(-tau * 0.15));
    return sun * s * powder * 1.5 + ambient;
}

#ifdef MODERN_CLOUD_NOISE
// ----- the shapes (ours): Schneider's Nubis -----
// The shaders that march the clouds (ModernSky.hlsl, ModernCloudPass.hlsl) define MODERN_CLOUD_NOISE and
// declare CloudShapeTex and CloudDetailTex (ModernCloudNoise.cs: tiling Perlin–Worley and Worley noise,
// wrapping sampler) before including ModernCommon.hlsli. A cloud is the coverage map (the field's two broad
// octaves, which place the clouds and say how much of the sky they cover), times a cumulus' height profile
// (a flat base, a rounded top whose height grows with the coverage, so the clouds come in many sizes),
// carved by the shape noise (the cauliflower lobes) and eaten at its edges by the detail noise (wisps at
// the base, billows higher up).

static const float CloudShapeTileKm = 3.2;    // the shape texture repeats every this many km
static const float CloudDetailTileKm = 0.45;  // the detail texture's

float CloudRemap(float v, float lo, float hi, float a, float b) { return a + (v - lo) / max(hi - lo, 1e-5) * (b - a); }

// The coverage map at a point of the plan (km): 0 clear, 1 the thickest; reaching a little past the
// threshold the shadows and the flat layer use (CloudColumn), so that the noise's erosion carves the
// clouds back to about the share of the sky asked for.
float CloudCoverage(float2 km)
{
    float field = CloudField(CloudLatticeKm(km), 2.0, 0.0);
    return saturate((field - CloudShape.x) * CloudShape.y * 0.7 + 0.32) * 0.85;
}

// The density (0..1) at a point of the plan (km) and height fraction y (0 the base, 1 the layer's top), for
// coverage cov; `detail` 0..1 fades the detail noise in (none far off or for the light's looks).
float CloudShapeDensity(float2 km, float y, float cov, float detail)
{
    if (cov <= 0.0 || y < 0.0 || y > 1.0) return 0.0;
    // The cumulus profile: a flat base (dense within 5% of the depth), a top from a third of the depth (the
    // thinnest) to all of it (the thickest), rounded over its upper half.
    float top = 0.32 + 0.68 * pow(cov, 0.75);
    float profile = saturate(y / 0.05) * saturate((top - y) / (top * 0.55));
    if (profile <= 0.0) return 0.0;
    float2 drift = CloudMotion.xy / CloudMotion.z;    // the wind's drift, km
    float3 p = float3(km + drift, y * CloudShape.w);
    float4 n = CloudShapeTex.SampleLevel(CloudShapeSmp, p / CloudShapeTileKm, 0);
    float fbm = n.g * 0.625 + n.b * 0.25 + n.a * 0.125;
    float base = saturate(CloudRemap(n.r, -(1.0 - fbm) * 0.6, 1.0, 0.0, 1.0)) * profile;
    float c = saturate(CloudRemap(base, 1.0 - cov, 1.0, 0.0, 1.0)) * cov;
    if (c <= 0.0 || detail <= 0.0) return c;
    float3 dn = CloudDetailTex.SampleLevel(CloudDetailSmp, (p + float3(0.0, 0.0, y * 0.12)) / CloudDetailTileKm, 0).rgb;
    float hf = dn.r * 0.625 + dn.g * 0.25 + dn.b * 0.125;
    float erode = lerp(hf, 1.0 - hf, saturate(y * 6.0)) * 0.38 * detail;
    return saturate(CloudRemap(c, erode, 1.0, 0.0, 1.0));
}

// The clouds as one flat layer, the cheapest (and what far clouds become, where steps would be too long
// to see their shapes): the shapes at two heights where the ray meets the base, as a thickness, and the
// coverage towards the sun as the light's depth (the shapes' noise carves the layer's outlines as it
// does the marched clouds', so far and near clouds match). rgb their light, a their transmittance.
float4 CloudFlat(float2 eyeKm, float3 d, float t0, float octaves, float phase)
{
    float2 km = eyeKm + d.xy * t0;
    float cov = CloudCoverage(km);
    if (cov <= 0.0) return float4(0, 0, 0, 1);
    float column = (CloudShapeDensity(km, 0.12, cov, 0.0) + CloudShapeDensity(km, 0.4, cov, 0.0) + CloudShapeDensity(km, 0.7, cov, 0.0)) / 3.0;
    if (column <= 0.0) return float4(0, 0, 0, 1);
    float ext = CloudLight.x;
    float trans = exp(-ext * column * CloudShape.w / max(d.z, 0.2) * 0.5);
    float sz = max(SunDir.z, 0.05);
    float2 toSun = SunDir.xy / sz * CloudShape.w * 0.4;
    float above = CloudCoverage(km + toSun);
    // Seen from below a cloud shows its shaded base; low on the horizon, its sides, as much sunlit as not.
    float side = saturate(1.0 - d.z * 2.5);
    float y = lerp(0.1, 0.5, side);
    // (Never thinner than one optical depth: a flat layer has no rim lit through, which far off would
    // show as bright outlines round hazed bodies.)
    float tau = max(ext * CloudShape.w * lerp((column + above) * 0.5 / sz, (column * 0.5 + above * 1.5) * 0.1, side), 1.0);
    return float4(CloudLightAt(y, tau, phase) * (1.0 - trans), trans);
}

// The clouds marched through in `steps` steps from the base at t0 to the top at t1 (km), the eye h0 km over
// the ground: rgb their light, a their transmittance. The steps crowd towards the base, where a cloud seen
// from below takes most of its opacity; each step's sample sits at u of the step (u in (0, 1]), which the
// half-resolution pass dithers from pixel to pixel so that the sky's filter averages sixteen placings (no
// bands). Each lit sample looks three times towards the sun for the light's depth (the shapes without the
// detail). Ours.
float4 CloudSteps(float2 eyeKm, float3 d, float h0, float t0, float t1, int steps, float octaves, float phase, float u)
{
    float base = CloudShape.z, s = d.z, ext = CloudLight.x;
    float3 light = 0;
    float trans = 1.0;
    float span = t1 - t0;
    float detail = saturate(octaves - 2.0) * saturate(2.0 - t0 / 8.0);
    float sz = max(SunDir.z, 0.05);
    [loop] for (int i = 0; i < steps; i++)
    {
        float fa = (float)i / steps, fb = (float)(i + 1) / steps, f = (i + u) / steps;
        float ta = t0 + fa * sqrt(fa) * span, tb = t0 + fb * sqrt(fb) * span, ds = tb - ta;
        float t = t0 + f * sqrt(f) * span;
        float2 km = eyeKm + d.xy * t;
        float cov = CloudCoverage(km);
        if (cov <= 0.0) continue;
        float y = (CloudAltitude(h0, t, s) - base) / CloudShape.w;
        float dens = CloudShapeDensity(km, y, cov, detail);
        if (dens <= 0.0) continue;
        float sigma = ext * dens;
        // Three looks towards the sun (the shapes alone), 60, 200 and 450 m along it.
        float tau = 0.0;
        [unroll] for (int j = 0; j < 3; j++)
        {
            float along = j == 0 ? 0.06 : (j == 1 ? 0.2 : 0.45);
            float len = j == 0 ? 0.1 : (j == 1 ? 0.2 : 0.3);
            float2 kmj = km + SunDir.xy * along;
            float yj = y + SunDir.z * along / CloudShape.w;
            tau += CloudShapeDensity(kmj, yj, CloudCoverage(kmj), 0.0) * len;
        }
        float seg = exp(-sigma * ds);
        light += trans * CloudLightAt(y, ext * tau, phase) * (1.0 - seg);
        trans *= seg;
        if (trans < 0.01) break;
    }
    return float4(light, trans);
}

// The clouds along a view ray from the eye: rgb the light they send it, a their transmittance. Marched
// in CloudMotion.w steps (0: the flat layer only) out to 10 km from the eye, the flat layer beyond
// 20 km and low on the horizon, the two blended between; far clouds sink into the haze at the horizon.
// u places the steps (CloudSteps; 1: fixed); pixelScale is how many of the picture's pixels one pixel
// of the target spans (2 for the half-resolution cloud pass, ModernCloudPass.hlsl), for the octaves.
float4 CloudMarch(float3 eye, float3 d, float u, float pixelScale)
{
    if (!CloudsOn() || d.z < -0.01) return float4(0, 0, 0, 1);
    float h0 = max((eye.z - Fog.z) * CloudKmPerUnit, 0.0);
    float base = CloudShape.z, top = CloudShape.z + CloudShape.w;
    if (h0 >= base) return float4(0, 0, 0, 1);   // from inside or above the layer: not drawn (ours, not needed yet)
    float t0 = CloudShellDistance(h0, base, d.z), t1 = CloudShellDistance(h0, top, d.z);
    float fade = CloudLight.w;
    if (t0 > fade * 4.0) return float4(0, 0, 0, 1);
    float2 eyeKm = eye.xy * CloudKmPerUnit;
    float mu = dot(d, SunDir.xyz);
    float phase = mu;   // the light's octaves work out their phases from it (CloudLightAt)
    // Fewer octaves far away, where finer ones would only shimmer, and low down, where the layer is seen
    // edge-on and a pixel spans a long way across it (its length over the sine of the elevation): no
    // octave finer than four of those, else the billows stretch into streaks.
    float pixel = 2.0 * pixelScale / (CameraUp.w * Screen.y);         // radians a pixel, up the screen
    float across = t0 * pixel / max(d.z, 0.02);
    float octaves = clamp(min(5.0 - log2(max(t0 / 12.0, 1.0)), log2(1.0 / (CloudMotion.z * 4.0 * across)) + 1.0), 1.0, 5.0);
    int steps = (int)CloudMotion.w;
    // The flat layer also takes over low down, where a ray crosses the layer over so many kilometres
    // that its steps grow longer than a fifth of the clouds' spacing: the shapes' lookups would slide
    // over them from pixel to pixel and stripe them.
    float stepAcross = (t1 - t0) / max(steps, 1) * CloudMotion.z;     // a step, in lattice cells
    float far = steps > 0 ? max(saturate((t0 - 10.0) / 10.0), saturate((stepAcross - 0.2) / 0.2)) : 1.0;
    float4 c = float4(0, 0, 0, 1);
    if (far < 1.0) c = CloudSteps(eyeKm, d, h0, t0, t1, steps, octaves, phase, u);
    if (far > 0.0) c = lerp(c, CloudFlat(eyeKm, d, t0, octaves, phase), far);
    float keep = exp(-t0 / fade);
    return float4(c.rgb * keep, lerp(1.0, c.a, keep));
}

// The clouds per pixel at fixed steps, as the sky drew them before the half-resolution pass (ours).
float4 CloudMarch(float3 eye, float3 d) { return CloudMarch(eye, d, 1.0, 1.0); }
#endif

#endif
