// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Shaders/ModernPbr.hlsl at b4185ef, reworked for the platform.
// The modern path's surfaces (ours): glTF metallic-roughness materials lit by the
// sun (GGX distribution, height-correlated Smith visibility, Schlick Fresnel) with cascaded shadow maps,
// by the scene's point and spot lights through a clustered light list, and by the sky, the ground and the
// surroundings as ambient light, occluded on screen (ModernAo.hlsl); then the height fog and the sun's
// shadowed in-scatter (ModernVolume.hlsl). Output is linear HDR; ModernTonemap.hlsl maps it.

// Ours: parallax occlusion from a height map, a large-scale variation against tiling, foliage lit
// through, and the physically based sky and air (ModernAtmosphere.hlsli). Hex tiling (ModernHex.hlsli).
// The reflections: the screen's reflections read before shading
// (ModernSsr.hlsl) in place of the sky and probes' reflection where they found something; specular anti-aliasing;
// a water film's reflectance on wet and puddled stone; the material's occlusion shadowing the lights' reflections
// in crevices, and reflections turned into the surface itself put out.

// The atmosphere's tables, declared before ModernCommon.hlsli so its sky and haze can read them.
[[vk::combinedImageSampler]] [[vk::binding(11, 2)]] Texture2D<float4> SkyViewTex : register(t11, space2);
[[vk::combinedImageSampler]] [[vk::binding(11, 2)]] SamplerState SkyViewSmp : register(s11, space2);
[[vk::combinedImageSampler]] [[vk::binding(12, 2)]] Texture2D<float4> ApInTex : register(t12, space2);
[[vk::combinedImageSampler]] [[vk::binding(12, 2)]] SamplerState ApInSmp : register(s12, space2);
[[vk::combinedImageSampler]] [[vk::binding(13, 2)]] Texture2D<float4> ApTrTex : register(t13, space2);
[[vk::combinedImageSampler]] [[vk::binding(13, 2)]] SamplerState ApTrSmp : register(s13, space2);
#define MODERN_ATMOSPHERE 1

#include "ModernSurface.hlsli"
#include "ModernCommon.hlsli"
#include "ModernHex.hlsli"

cbuffer Material : register(b1, space3)
{
    float4 BaseColorFactor;
    float4 MrFactors;       // x metallic, y roughness, z normal scale, w occlusion strength
    float4 Emissive;        // rgb emissive (strength applied); w: 0, or 1 + the waves' time for open water (ModernWater.hlsli)
    float4 TexSets;         // coordinate set of base, metal-rough, normal, occlusion
    float4 Flags;           // x: emissive set; y: 1 = has a normal map; z: 1 = blended; w: specular factor (KHR_materials_specular)
    float4 Relief;          // ours: x the height map's span (engine units; 0: none); y parallax's most steps (0: off); z the distance it is gone at (units); w 1 = it shadows itself
    float4 Leaf;            // ours: x 1 = foliage; y its translucency; z 1 = the large-scale variation; w the height map's coordinate set
    float4 Tiling;          // ours (ModernHex.hlsli): x cells per texture unit (0: a plain repeat); y 1 = the cells turn, +2 = the
                            // occlusion map is the metal-roughness map (read once); z the coordinate set the cells lie on; w what weights the
                            // blend: 0 nothing, 1 the height map (slot 10: the material's own, or a decal's surface's), 2 the base colour's brightness
};

[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] Texture2D<float4> BaseTex : register(t0, space2);
[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] SamplerState BaseSmp : register(s0, space2);
[[vk::combinedImageSampler]] [[vk::binding(1, 2)]] Texture2D<float4> MrTex : register(t1, space2);
[[vk::combinedImageSampler]] [[vk::binding(1, 2)]] SamplerState MrSmp : register(s1, space2);
[[vk::combinedImageSampler]] [[vk::binding(2, 2)]] Texture2D<float4> NormalTex : register(t2, space2);
[[vk::combinedImageSampler]] [[vk::binding(2, 2)]] SamplerState NormalSmp : register(s2, space2);
[[vk::combinedImageSampler]] [[vk::binding(3, 2)]] Texture2D<float4> OccTex : register(t3, space2);
[[vk::combinedImageSampler]] [[vk::binding(3, 2)]] SamplerState OccSmp : register(s3, space2);
[[vk::combinedImageSampler]] [[vk::binding(4, 2)]] Texture2D<float4> EmTex : register(t4, space2);
[[vk::combinedImageSampler]] [[vk::binding(4, 2)]] SamplerState EmSmp : register(s4, space2);
[[vk::combinedImageSampler]] [[vk::binding(5, 2)]] Texture2D<float> ShadowTex : register(t5, space2);
[[vk::combinedImageSampler]] [[vk::binding(5, 2)]] SamplerComparisonState ShadowSmp : register(s5, space2);
[[vk::combinedImageSampler]] [[vk::binding(6, 2)]] Texture2D<float4> AoTex : register(t6, space2);
[[vk::combinedImageSampler]] [[vk::binding(6, 2)]] SamplerState AoSmp : register(s6, space2);
[[vk::combinedImageSampler]] [[vk::binding(7, 2)]] Texture2D<float4> VolTex : register(t7, space2);
[[vk::combinedImageSampler]] [[vk::binding(7, 2)]] SamplerState VolSmp : register(s7, space2);
[[vk::combinedImageSampler]] [[vk::binding(8, 2)]] Texture2D<float> PointShadowTex : register(t8, space2);
[[vk::combinedImageSampler]] [[vk::binding(8, 2)]] SamplerComparisonState PointShadowSmp : register(s8, space2);
[[vk::combinedImageSampler]] [[vk::binding(9, 2)]] Texture2D<float> SpareTex : register(t9, space2);
[[vk::combinedImageSampler]] [[vk::binding(9, 2)]] SamplerComparisonState SpareSmp : register(s9, space2);
[[vk::combinedImageSampler]] [[vk::binding(10, 2)]] Texture2D<float4> HeightTex : register(t10, space2);
[[vk::combinedImageSampler]] [[vk::binding(10, 2)]] SamplerState HeightSmp : register(s10, space2);
// The light probes (ours; ModernProbes.hlsli), and a spare slot that keeps the count even.
[[vk::combinedImageSampler]] [[vk::binding(14, 2)]] Texture3D<float4> ProbeTex : register(t14, space2);
[[vk::combinedImageSampler]] [[vk::binding(14, 2)]] SamplerState ProbeSmp : register(s14, space2);
// The screen's reflections (ours; ModernSsrResolve.hlsl): light times confidence, and the confidence.
[[vk::combinedImageSampler]] [[vk::binding(15, 2)]] Texture2D<float4> SsrTex : register(t15, space2);
[[vk::combinedImageSampler]] [[vk::binding(15, 2)]] SamplerState SsrSmp : register(s15, space2);
// The scene's lights, four float4 each (ModernLights.cs); the clusters: an (offset, count) pair per
// cluster, then the light indices the offsets point at; the shadowed lights' cube faces' matrices.
[[vk::binding(16, 2)]] StructuredBuffer<float4> LightData : register(t16, space2);
[[vk::binding(17, 2)]] StructuredBuffer<uint> ClusterData : register(t17, space2);
[[vk::binding(18, 2)]] StructuredBuffer<float4x4> ShadowMatrices : register(t18, space2);

#include "ModernShadows.hlsli"
#include "ModernWater.hlsli"
#include "ModernProbes.hlsli"

// ----- the BRDF -----

float D_GGX(float nh, float a)
{
    float a2 = a * a;
    float d = nh * nh * (a2 - 1.0) + 1.0;
    return a2 / (PI * d * d);
}

// Height-correlated Smith visibility (the G term divided by 4 n.l n.v).
float V_SmithGGX(float nl, float nv, float a)
{
    float a2 = a * a;
    float gv = nl * sqrt(nv * nv * (1.0 - a2) + a2);
    float gl = nv * sqrt(nl * nl * (1.0 - a2) + a2);
    return 0.5 / max(gv + gl, 1e-5);
}

float3 F_Schlick(float3 f0, float f90, float vh)
{
    return f0 + (f90 - f0) * pow(1.0 - vh, 5.0);
}

// The split-sum's environment term without its lookup table (Karis, "Physically Based Shading on Mobile").
float3 EnvBrdf(float3 f0, float f90, float roughness, float nv)
{
    const float4 c0 = float4(-1, -0.0275, -0.572, 0.022);
    const float4 c1 = float4(1, 0.0425, 1.04, -0.04);
    float4 r = roughness * c0 + c1;
    float a004 = min(r.x * r.x, exp2(-9.28 * nv)) * r.x + r.y;
    float2 ab = float2(-1.04, 1.04) * a004 + r.zw;
    return f0 * ab.x + ab.y * f90;
}

// One light's diffuse and specular reflection towards v, per unit of irradiance. The specular is shadowed by
// the material's occlusion `occ` (ours; Chan, "Material Advances in Call of Duty: WWII", 2018): a
// crevice whose occlusion is ao is open to a cone whose aperture is about 2·ao² of the hemisphere, and a light
// lower than that over the surface does not reach its bottom — so cobble joints and the cracks in a scan do not
// glint in the sun. The diffuse is left as it was (the occlusion map's job there is the ambient light's).
float MicroShadow(float nl, float occ) { return saturate(nl + 2.0 * occ * occ - 1.0); }

float3 Brdf(float3 n, float3 v, float3 l, float nv, float3 f0, float f90, float3 diffuse, float a, float occ)
{
    float3 h = normalize(l + v);
    float nl = saturate(dot(n, l));
    float nh = saturate(dot(n, h)), vh = saturate(dot(v, h));
    float3 F = F_Schlick(f0, f90, vh);
    float3 spec = D_GGX(nh, a) * V_SmithGGX(nl, nv, a) * F * MicroShadow(nl, occ);
    return ((1.0 - F) * diffuse / PI + spec) * nl;
}

// ----- local lights -----

// A point or spot light's irradiance at a surface, per ModernLights.Attenuation: the inverse square in
// metres, windowed to zero at the light's range, and a spot's cone.
float LightFalloff(float4 posRange, float4 dirCone, float4 extra, float3 world, out float3 l)
{
    float3 toLight = posRange.xyz - world;
    float d2 = dot(toLight, toLight);
    l = toLight * rsqrt(max(d2, 1e-4));
    float r = d2 / (posRange.w * posRange.w);
    float window = saturate(1.0 - r * r);
    float metres2 = d2 / (72.0 * 72.0);
    float e = window * window / max(metres2, 0.01);
    if (dirCone.w > -1.5)
    {
        float t = saturate((dot(-l, dirCone.xyz) - dirCone.w) * extra.y);
        e *= t * t;
    }
    return e;
}

// A shadowed light's cube (ModernLights.CubeFace and CubeFaceViewProjection): the face the point lies
// towards, its depth there against the face's map in the atlas (a row of six faces per light), four
// bilinear comparisons half a texel apart. The point is pushed off its surface by about a texel.
float PointShadow(int slot, float3 lightPos, float3 world, float3 ng)
{
    float3 d = world - lightPos;
    float3 a = abs(d);
    int face = a.x >= a.y && a.x >= a.z ? (d.x >= 0.0 ? 0 : 1) : a.y >= a.z ? (d.y >= 0.0 ? 2 : 3) : (d.z >= 0.0 ? 4 : 5);
    float res = 1.0 / (6.0 * ClusterDepth.z);
    float3 p = world + ng * length(d) * (3.0 / res);
    float4 c = mul(ShadowMatrices[slot * 6 + face], float4(p, 1.0));
    float3 ndc = c.xyz / c.w;
    float2 uv = clamp(float2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5), 1.5 / res, 1.0 - 1.5 / res);
    float2 texel = float2(ClusterDepth.z, 1.0 / (ClusterDepth.w * res));
    float2 at = (uv + float2(face, slot)) * float2(1.0 / 6.0, 1.0 / ClusterDepth.w);
    float sum = 0.0;
    [unroll] for (int k = 0; k < 4; k++)
        sum += PointShadowTex.SampleCmpLevelZero(PointShadowSmp, at + (float2(k & 1, k >> 1) - 0.5) * texel, ndc.z);
    return sum * 0.25;
}

float3 LocalLights(float3 world, float3 n, float3 ng, float3 v, float nv, float3 f0, float f90, float3 diffuse, float a, float occ, float2 pixel)
{
    float z = dot(world - CameraPos.xyz, CameraForward.xyz);
    uint slices = (uint)Cluster.z;
    uint slice = (uint)clamp(floor(log(max(z, 1.0) / ClusterDepth.x) * Cluster.w), 0.0, Cluster.z - 1.0);
    uint2 tile = min((uint2)(pixel / ClusterDepth.y), (uint2)Cluster.xy - 1);
    uint ci = (slice * (uint)Cluster.y + tile.y) * (uint)Cluster.x + tile.x;
    uint off = ClusterData[ci * 2], count = ClusterData[ci * 2 + 1];
    float3 sum = 0;
    for (uint k = 0; k < count; k++)
    {
        uint li = ClusterData[off + k] * 4;
        float4 posRange = LightData[li], colour = LightData[li + 1], dirCone = LightData[li + 2], extra = LightData[li + 3];
        float3 l;
        float e = LightFalloff(posRange, dirCone, extra, world, l);
        if (e <= 0.0 || dot(ng, l) < -0.05) continue;
        if (extra.z >= 0.0) e *= PointShadow((int)extra.z, posRange.xyz, world, ng);
        sum += Brdf(n, v, l, nv, f0, f90, diffuse, a, occ) * colour.rgb * e;
    }
    return sum;
}

// ----- relief and variation (ours) -----

// The relief's height at `at` (ours): the height map's, or with hex tiling the cells' blend of it — the
// same function of the coordinates the surface's maps are read through, so the march finds the blended relief.
float ReliefHeight(float2 at, float lod, HexFrame f, bool hex)
{
    if (!hex) return HeightTex.SampleLevel(HeightSmp, at, lod).r;
    // A level given, so a cell that cannot show here is skipped (ModernHex.hlsli's `live`).
    float3 h = 0.0;
    [branch] if (f.live.x > 0.0) h.x = HeightTex.SampleLevel(HeightSmp, HexAt1(f, at), lod).r;
    [branch] if (f.live.y > 0.0) h.y = HeightTex.SampleLevel(HeightSmp, HexAt2(f, at), lod).r;
    [branch] if (f.live.z > 0.0) h.z = HeightTex.SampleLevel(HeightSmp, HexAt3(f, at), lod).r;
    return dot(HexWeights(HexBary(f, at), h, 1.0), h);
}

// Parallax occlusion mapping (Tatarchuk 2006): the view ray marched down through the height map (1 the
// surface, 0 its span below) in up to Relief.y steps, fewer seen square on, from the surface's own
// texture-space frame (worked out from the screen's derivatives, so it needs no tangents); the crossing
// found between the last two steps. Fades out with distance (Relief.z) and at grazing angles, where its
// error grows. Returns the texture coordinates' offset, and through `depth` how deep the ray went (0..1),
// through `jac` the world-to-texture map for the self-shadow march.
float2 ParallaxOffset(float3 world, float3 ng, float3 v, float2 uv, HexFrame hf, bool hex, out float depth, out float3x2 jac, out float lod)
{
    depth = 0.0;
    // One bilinear tap a step at the level the surface's footprint calls for (and half a level coarser),
    // not an anisotropic one: a step costs one texel fetch's worth wherever the surface is seen edge-on.
    lod = HeightTex.CalculateLevelOfDetail(HeightSmp, uv) + 0.5;
    float3 dp1 = ddx(world), dp2 = ddy(world);
    float2 duv1 = ddx(uv), duv2 = ddy(uv);
    float3 p2 = cross(dp2, ng), p1 = cross(ng, dp1);
    float det = dot(dp1, p2);
    float3 tu = (p2 * duv1.x + p1 * duv2.x), tv = (p2 * duv1.y + p1 * duv2.y);
    jac = float3x2(tu.x, tv.x, tu.y, tv.y, tu.z, tv.z) / (abs(det) > 1e-12 ? det : 1e-12);
    float dist = length(CameraPos.xyz - world);
    float vn = dot(v, ng);
    float fade = saturate((Relief.z - dist) / (Relief.z * 0.4)) * saturate((vn - 0.06) / 0.2);
    if (fade <= 0.0) return float2(0.0, 0.0);
    // Down the full span, the ray moves across the surface by -v's part along it, span / (v·n) of it.
    float3 across = -(v - ng * vn) / max(vn, 0.15) * Relief.x * fade;
    float2 total = mul(across, jac);
    float steps = clamp(round(lerp(Relief.y, Relief.y * 0.35, vn)), 4.0, 64.0);
    float stepDepth = 1.0 / steps;
    float2 stepUv = total / steps;
    float layer = 0.0;
    float2 at = uv;
    float h = ReliefHeight(at, lod, hf, hex);
    float prevH = h, prevLayer = 0.0;
    [loop] for (int k = 0; k < 64; k++)
    {
        if (k >= (int)steps || 1.0 - h <= layer) break;
        prevH = h; prevLayer = layer;
        at += stepUv; layer += stepDepth;
        h = ReliefHeight(at, lod, hf, hex);
    }
    // Between the last two layers: where the ray's line crosses the height's.
    float after = (1.0 - h) - layer, before = (1.0 - prevH) - prevLayer;
    float w = after / min(after - before, -1e-5);
    float x = saturate(1.0 - w);
    float found = lerp(prevLayer, layer, x);
    depth = found;
    return total * found;
}

// How much of the sun reaches the relief's point (ours): the height map marched from it towards the sun;
// a ridge in the way above the ray darkens it, softly by how far above.
float ParallaxShadow(float2 uv, float depth, float3 l, float3 ng, float3x2 jac, float lod, HexFrame hf, bool hex)
{
    float ln = dot(l, ng);
    if (ln <= 0.0 || depth <= 0.0) return 1.0;
    float3 across = (l - ng * ln) / max(ln, 0.15) * Relief.x;
    float2 total = mul(across, jac) * depth;
    float lit = 1.0;
    [unroll] for (int k = 1; k <= 8; k++)
    {
        float f = k / 8.0;
        float h = ReliefHeight(uv + total * f, lod, hf, hex);
        float rayHeight = 1.0 - depth * (1.0 - f);
        lit = min(lit, saturate(1.0 - (h - rayHeight) * 12.0));
    }
    return lit;
}

// A hash without transcendentals (Hoskins's "hash without sine"): cheap on any GPU and on a software one.
float MacroHash(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

float MacroNoise(float3 p)
{
    float3 i = floor(p), f = frac(p);
    float3 u = f * f * (3.0 - 2.0 * f);
    float a = lerp(lerp(MacroHash(i), MacroHash(i + float3(1, 0, 0)), u.x), lerp(MacroHash(i + float3(0, 1, 0)), MacroHash(i + float3(1, 1, 0)), u.x), u.y);
    float b = lerp(lerp(MacroHash(i + float3(0, 0, 1)), MacroHash(i + float3(1, 0, 1)), u.x), lerp(MacroHash(i + float3(0, 1, 1)), MacroHash(i + float3(1, 1, 1)), u.x), u.y);
    return lerp(a, b, u.z);
}

// The large-scale variation (ours): two octaves of value noise in the world, some 4 and 13 m
// across, as a brightness of ±10%, a slight warm or cool cast (where the two octaves disagree) and a
// roughness of ±8% — what weather, dirt and repair do over a town's surfaces, so a material's tile does
// not repeat visibly across a square. x: albedo scale, y: warmth (−1..1), z: roughness scale.
float3 MacroVariation(float3 world)
{
    float3 m = world / 72.0;
    float a = MacroNoise(m / 4.0 + 13.1), b = MacroNoise(m / 13.0 + 71.7);
    float bright = 1.0 + (a - 0.5) * 0.12 + (b - 0.5) * 0.10;
    return float3(bright, clamp((a - b) * 2.5, -1.0, 1.0), 1.0 + (b - 0.5) * 0.16);
}

// ----- the surface -----

// ----- specular anti-aliasing (ours) -----

// The GGX alpha `a` widened by the spread of the normal `n` over the pixel (Tokuyoshi and Kaplanyan 2019): the
// variance of a pixel-wide filter (σ² = 0.25) over the normal's screen derivatives, at most κ = 0.18 added to α².
float SpecularAntiAliasing(float3 n, float a)
{
    float3 dx = ddx(n), dy = ddy(n);
    float variance = 0.25 * (dot(dx, dx) + dot(dy, dy));
    float kernel = min(2.0 * variance, 0.18);
    return sqrt(saturate(a * a + kernel));
}

float4 PSMain(VSOut i, bool front : SV_IsFrontFace) : SV_Target0
{
    float3 ng = normalize(i.Normal);
    if (FoliageFlipsNormal(front, Leaf.x)) ng = -ng;
    float3 v3 = CameraPos.xyz - i.World;
    float3 v = normalize(v3);

    // Hex tiling (ours; ModernHex.hlsli): the three cells round the pixel, laid over the tiled set before
    // the relief moves it.
    bool hex = Tiling.x > 0.0;
    float2 hexUv = Set(i.Uv, Tiling.z);
    HexFrame hf = (HexFrame)0;
    if (hex) hf = HexFrameAt(hexUv, Tiling.x, frac(Tiling.y * 0.5) * 2.0);

    // Parallax: every map read at the relief's point (ours), the march through the cells' blended
    // height when the height map lies on the tiled set.
    bool hexRelief = hex && abs(Leaf.w - Tiling.z) < 0.5;
    float reliefDepth = 0.0, pomLod = 0.0;
    float3x2 jac = (float3x2)0;
    float2 pomUv = Set(i.Uv, Leaf.w);
    if (Relief.x > 0.0 && Relief.y > 0.0)
    {
        float2 offset = ParallaxOffset(i.World, ng, v, pomUv, hf, hexRelief, reliefDepth, jac, pomLod);
        if (Leaf.w > 0.5) i.Uv.zw += offset; else i.Uv.xy += offset;
        pomUv += offset;
    }

    // The blend's weights at the relief's point: from the three cells' heights or their colours' brightness.
    float2 tuv = Set(i.Uv, Tiling.z);
    float3 hw = 0.0;
    float4 base;
    if (hex)
    {
        float3 bary = HexBary(hf, tuv);
        if (Tiling.w > 1.5)
        {
            float4 c1 = BaseTex.Sample(BaseSmp, HexAt1(hf, tuv));
            float4 c2 = BaseTex.Sample(BaseSmp, HexAt2(hf, tuv));
            float4 c3 = BaseTex.Sample(BaseSmp, HexAt3(hf, tuv));
            hw = HexWeights(bary, float3(HexLuma(c1.rgb), HexLuma(c2.rgb), HexLuma(c3.rgb)), 2.0);
            base = hw.x * c1 + hw.y * c2 + hw.z * c3;
        }
        else
        {
            float3 hv = Tiling.w > 0.5 ? HexTaps(HeightTex, HeightSmp, hf, tuv) : float3(1.0, 1.0, 1.0);
            hw = HexWeights(bary, hv, Tiling.w);
            base = abs(TexSets.x - Tiling.z) < 0.5 ? HexSample(BaseTex, BaseSmp, hf, hw, tuv)
                                                   : BaseTex.Sample(BaseSmp, Set(i.Uv, TexSets.x));
        }
    }
    else base = BaseTex.Sample(BaseSmp, Set(i.Uv, TexSets.x));
    base = base * BaseColorFactor * i.Color;
    float3 macro = float3(1.0, 0.0, 1.0);
    if (Leaf.z > 0.5)
    {
        macro = MacroVariation(i.World);
        base.rgb *= macro.x * (1.0 + float3(0.035, 0.0, -0.035) * macro.y);
    }

    float3 n = ng;
    if (Flags.y > 0.5)
    {
        float3 t = normalize(i.Tangent.xyz - ng * dot(ng, i.Tangent.xyz));
        float3 b = cross(ng, t) * (i.Tangent.w < 0.0 ? -1.0 : 1.0);
        if (FoliageFlipsNormal(front, Leaf.x)) b = -b;
        // x and y from the map, z rebuilt from them (ours): a unit normal either way, and a two-channel
        // BC5 map (a cooked level's) has no third channel to read.
        float2 txy = hex && abs(TexSets.z - Tiling.z) < 0.5 ? HexNormalXY(NormalTex, NormalSmp, hf, hw, tuv)
                                                            : NormalTex.Sample(NormalSmp, Set(i.Uv, TexSets.z)).xy * 2.0 - 1.0;
        float3 tn = float3(txy, sqrt(saturate(1.0 - dot(txy, txy))));
        tn.xy *= MrFactors.z;
        n = normalize(t * tn.x + b * tn.y + ng * max(tn.z, 1e-3));
    }

    if (Emissive.w > 0.5) n = WaterNormal(i.World, Emissive.w - 1.0, CameraPos.xyz);   // open water (ours, ModernWater.hlsli)

    bool hexMr = hex && abs(TexSets.y - Tiling.z) < 0.5;
    float4 mr = hexMr ? HexSample(MrTex, MrSmp, hf, hw, tuv) : MrTex.Sample(MrSmp, Set(i.Uv, TexSets.y));
    float metallic = saturate(MrFactors.x * mr.b);
    float roughness = clamp(MrFactors.y * mr.g * macro.z, 0.045, 1.0);
    // Water (ours): the film on wet or puddled stone reflects as water does, 2% at normal incidence
    // (n = 1.33), not stone's 4%. The town's materials paint their wetness into the roughness map, so a dielectric
    // whose map makes it smooth (under 0.15, and under half its factor: not a uniformly smooth material such as
    // glass) is taken as wet, fully so under 0.08; open water always.
    float film = Emissive.w > 0.5 ? 1.0 : saturate((0.15 - roughness) / 0.07) * step(mr.g, 0.5) * (1.0 - Leaf.x);
    roughness = FoliageRoughness(Leaf.x, roughness);
    // Specular anti-aliasing (ours; Tokuyoshi and Kaplanyan, "Improved Geometric Specular
    // Antialiasing", 2019): where the shading normal turns within a pixel — a bump map's relief far off, the
    // river's waves, a leaf's own normal on a card a few pixels wide — the lobe is widened by the normal's spread
    // over the pixel (its variance, from its screen derivatives), so the pixel shows the average glint of what it
    // covers rather than one point's: no sparkle, no crawling highlights.
    float a = FogSun.w > 0.5 ? SpecularAntiAliasing(n, roughness * roughness) : roughness * roughness;
    roughness = sqrt(a);
    float occMap;
    if (hexMr && Tiling.y >= 2.0) occMap = mr.r;   // the packed map's occlusion, already read through the cells
    else if (hex && abs(TexSets.w - Tiling.z) < 0.5) occMap = HexSample(OccTex, OccSmp, hf, hw, tuv).r;
    else occMap = OccTex.Sample(OccSmp, Set(i.Uv, TexSets.w)).r;
    float occ = lerp(1.0, occMap, MrFactors.w);
    float2 pixel = i.Pos.xy;
    float2 suv = pixel * Screen.zw;

    float nv = max(dot(n, v), 1e-4);
    float3 albedo = base.rgb;
    // KHR_materials_specular's factor scales a dielectric's reflection at every angle (F0 and F90).
    float specular = Flags.w * FoliageSpecular(Leaf.x, dot(n, v));
    float f90 = lerp(specular * FoliageGrazing(Leaf.x), 1.0, metallic);
    float3 f0 = lerp(float3(0.04, 0.04, 0.04) * lerp(1.0, 0.5, film) * specular, albedo, metallic);
    float3 diffuse = albedo * (1.0 - metallic);

    // The sun. Light that reaches a face from behind its geometric normal is blocked by the face itself.
    float3 l = SunDir.xyz;
    float3 colour = 0;
    if (dot(n, l) > 0.0 && dot(ng, l) > -0.05)
    {
        float viewDepth = dot(i.World - CameraPos.xyz, CameraForward.xyz);
        float sh = Shadow(i.World, ng, viewDepth, pixel);
        if (Relief.w > 0.5 && reliefDepth > 0.0) sh *= ParallaxShadow(pomUv, reliefDepth, l, ng, jac, pomLod, hf, hexRelief);
        colour += Brdf(n, v, l, FoliageViewCosine(Leaf.x, nv), f0, f90, diffuse, a, occ) * SunColor.rgb * SunDir.w * sh;
    }
    // Foliage (ours): the sun through the leaf from behind — a diffuse transmission by its
    // translucency, shadowed where its far side is (the shadow looked up from the sun's side), a little
    // stronger looking towards the sun — and the sky's light through it. What comes through is a brighter,
    // yellower green than what the leaf reflects (light passing through picks up the chlorophyll's colour
    // over a thin path): the albedo's square root, mostly.
    if (Leaf.x > 0.5)
    {
        float3 through = lerp(albedo, sqrt(albedo), 0.75) * FoliageTranslucency(1.0, mr);
        float back = dot(-ng, l);
        if (back > 0.0)
        {
            float viewDepth = dot(i.World - CameraPos.xyz, CameraForward.xyz);
            float sh = Shadow(i.World, -ng, viewDepth, pixel);
            float forward = 1.0 + 2.0 * pow(saturate(dot(-v, l)), 6.0);
            colour += through * Leaf.y * saturate(back) * forward / PI * SunColor.rgb * SunDir.w * sh;
        }
        colour += through * Leaf.y * ShIrradiance(ShSky, -n) / PI * 0.5;
    }

    // The point and spot lights.
    if (Options.w > 0.5) colour += LocalLights(i.World, n, ng, v, nv, f0, f90, diffuse, a, occ, pixel);

    // Ambient light: the open sky and ground where ambient occlusion finds the view of them clear, the
    // surroundings' own light where it finds it blocked.
    float vis = 1.0, gtao = 1.0;
    if (Options.x > 0.5 && Flags.z < 0.5) { float4 ao = AoTex.SampleLevel(AoSmp, suv, 0); vis = ao.r; gtao = ao.b; }
    float3 irradiance = lerp(ShIrradiance(ShBounce, n), ShIrradiance(ShSky, n), vis);
    float3 r = reflect(-v, n);
    float3 envSpec = lerp(SkyRadiance(r), ShIrradiance(ShSky, r) / PI, roughness);
    if (r.z < 0.0) envSpec = lerp(envSpec, Ground.rgb, saturate(-r.z * 3.0));
    // Where the level has light probes (ours): their irradiance, the sky they see and the light the
    // surfaces round them bounce, times the screen's own occlusion (the sky's from the height map is in the
    // probes already); what a reflection sees where their sky is blocked is the surroundings' light.
    ProbeSample probe = ProbesAt(i.World, ng);
    if (probe.weight > 0.0)
    {
        irradiance = lerp(irradiance, ProbeIrradiance(probe, n) * gtao, probe.weight);
        float open = ProbeSkyVisibility(probe, r);
        // A smooth surface's reflection looks along one direction, and the probes' sky visibility is a first-order
        // harmonic that knows only how the sky's opening leans: among houses it says about 0.4 for a direction 15°
        // above the horizon, where the house fronts stand, and wet ground mirrored the sky as a pale haze. Its
        // contrast is raised for smooth surfaces (ours; on open ground the same direction's 0.7 stays sky).
        open = lerp(smoothstep(0.3, 0.8, open), open, roughness);
        envSpec = lerp(envSpec, lerp(ProbeIrradiance(probe, r) / PI, envSpec, open), probe.weight);
        vis = lerp(vis, gtao, probe.weight);
    }
    // The screen's reflections (ours; ModernSsr.hlsl, ModernSsrResolve.hlsl): where the trace found
    // something with confidence, its light stands in for the sky and probes' reflection, for surfaces smooth enough
    // (fully up to half the cut-off roughness, fading to none at it), never for leaves.
    float ssr = 0.0;
    if (SkyGlow.w > 0.0 && Leaf.x < 0.5)
    {
        float4 found = SsrTex.SampleLevel(SsrSmp, suv, 0);
        ssr = saturate(found.a) * saturate(2.0 - 2.0 * roughness / SkyGlow.w);
        if (ssr > 0.0) envSpec = lerp(envSpec, found.rgb / max(found.a, 1e-4), ssr);
    }
    // A reflection a bump map turns into the face itself sees the face's own shaded relief, not the sky (ours,
    // the horizon fade of Russell's "Horizon Occlusion for Normal Mapped Reflections", Marmoset, 2014):
    // the glint on the near side of every cobble joint seen at a low angle goes out.
    float horizon = Leaf.x > 0.5 ? 1.0 : saturate(1.0 + 1.3 * dot(r, ng));
    // Specular occlusion (Lagarde and de Rousiers) from the visibility with the material's own occlusion in it
    // A crevice of the scan is a crevice for its reflection too); a reflection the screen found sees its
    // occluders already and keeps only the material's.
    float ao = vis * occ;
    float specVis = Options.z > 0.5 ? saturate(pow(nv + ao, exp2(-16.0 * roughness - 1.0)) - 1.0 + ao) : occ;
    specVis = lerp(specVis, occ, ssr);
    colour += diffuse * irradiance / PI * occ + envSpec * EnvBrdf(f0, f90, roughness, nv) * specVis * horizon * horizon;

    if (hex && abs(Flags.x - Tiling.z) < 0.5)
    {
        if (any(Emissive.rgb > 0.0)) colour += Emissive.rgb * HexSample(EmTex, EmSmp, hf, hw, tuv).rgb;
    }
    else colour += Emissive.rgb * EmTex.Sample(EmSmp, Set(i.Uv, Flags.x)).rgb;

    colour = ApplyFog(colour, i.World);
    if (CameraPos.w > 0.5) colour += SunInscatter(-v) * VolTex.SampleLevel(VolSmp, suv, 0).r;
    return float4(colour, Flags.z > 0.5 ? base.a : 1.0);
}
