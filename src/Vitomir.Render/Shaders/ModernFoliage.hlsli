// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Foliage swaying in the wind (ours). Shared by every vertex
// stage that draws the world — the surfaces and their prepass (ModernSurface.hlsli) and the shadows
// (ModernShadow.hlsl: the sun's cascades, the lights' cubes, the sky's height map) — so a leaf is where
// its shadow says it is. Deterministic: a function of the place and the game's clock, nothing else.

#ifndef MODERN_FOLIAGE_HLSLI
#define MODERN_FOLIAGE_HLSLI

// How far a vertex moves, in engine units (72 a metre). sway: x the material's sway (its extras' wind
// times the wind's strength; 0 for anything else), y the clock in seconds, zw the direction the wind
// blows towards across the ground (unit). weight is the vertex's sway weight (its colour's alpha: 0 at
// the root, 1 at the tips). Three motions: a slow lean downwind that swells and fades in gusts rolling
// down the wind across the ground (a gust's front some 25 m long, crossing a place every 9 s or so),
// and a quick flutter along and across it whose phase changes from place to place, so neighbouring
// plants do not move in step. At full sway and weight a tip moves some 12 cm.
float3 SwayOffset(float3 pos, float weight, float4 sway)
{
    if (sway.x <= 0.0 || weight <= 0.0) return float3(0.0, 0.0, 0.0);
    float t = sway.y;
    float2 dir = sway.zw;
    float2 m = pos.xy / 72.0;                         // metres
    float along = dot(m, dir);
    float gust = 0.55 + 0.45 * sin(t * 0.70 - along * 0.25) * (0.65 + 0.35 * sin(t * 0.23 - along * 0.06 + 1.3));
    float ph = dot(m, float2(1.7, 2.3)) + pos.z / 72.0 * 3.1;
    float flutter = sin(t * 3.1 + ph) * 0.6 + sin(t * 5.7 + ph * 1.9) * 0.4;
    float amp = sway.x * weight * 0.12 * 72.0;
    float2 side = float2(-dir.y, dir.x);
    float2 xy = dir * (gust * 0.75 + flutter * 0.18) + side * (flutter * 0.22);
    // The tip moves on an arc: it dips as it leans.
    return float3(xy * amp, -0.35 * dot(xy, xy) * amp * weight);
}

// Whether a face seen from behind turns its normal round (ours). Not for foliage: a leaf card's
// normals are bent outward from the crown's centre (the levels generator's trees), so that the crown
// shades as one soft volume, lit on the sun's side and dim on the far one, whichever side of a card the
// camera sees; turned round on the cards seen from behind, half the crown would face inward and speckle.
bool FoliageFlipsNormal(bool front, float leaf)
{
    return !front && leaf < 0.5;
}

// How much light a leaf lets through (ours): the material's translucency times the atlas's mask,
// kept in the occlusion-roughness-metal map's alpha (1 on a thin blade, less on its veins, 0 on wood and
// fruit; a map without alpha reads 1, so older materials are as they were).
float FoliageTranslucency(float material, float4 orm)
{
    return material * orm.a;
}

// How much of a dielectric's reflection a leaf keeps (ours): its normal is the crown's, bent, not
// the leaf's own, so its glancing-angle Fresnel would reflect the bright sky off cards whose normal looks away
// from the camera (the crown's rim turned white). A leaf's sheen is kept where its normal faces the camera and
// fades out as it turns away; 1 for anything else.
float FoliageSpecular(float leaf, float nDotV)
{
    return leaf > 0.5 ? 0.6 * saturate(nDotV * 2.5 + 0.1) : 1.0;
}

// A leaf's roughness (ours): a leaf's waxy cuticle gives a broad sheen, not a glint (measured leaves'
// surface reflection has slopes of some 0.3–0.5 rms, a GGX alpha of that order), and a card's normal is the
// crown's, bent, so a narrow lobe would light the few cards whose bent normal happens to mirror the sun as white
// specks across a sunlit crown. Never below 0.7 (alpha 0.49).
float FoliageRoughness(float leaf, float roughness)
{
    return leaf > 0.5 ? max(roughness, 0.7) : roughness;
}

// How much of a smooth dielectric's rise in reflection towards grazing angles a leaf keeps (ours): 0.4.
// Schlick's term takes any dielectric to a mirror at grazing incidence; a leaf's surface, rough with veins and
// hairs at every scale, does not get there, and a crown seen against the sun turned grey-white with it.
float FoliageGrazing(float leaf)
{
    return leaf > 0.5 ? 0.4 : 1.0;
}

// The cosine between a leaf's normal and the eye that its light's specular sees (ours). The card itself
// faces the eye (it is drawn from either side) while its bent normal may graze away from it, and the GGX
// visibility term grows without bound as that cosine goes to 0: on the sunlit crown's far rim, seen against the
// light, it turned single leaves white. A leaf's specular takes the cosine as at least 0.35.
float FoliageViewCosine(float leaf, float nv)
{
    return leaf > 0.5 ? max(nv, 0.35) : nv;
}

#endif
