// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Shaders/ModernAo.hlsl at b4185ef, reworked for the platform.
// Screen-space ambient occlusion for the modern path (ours): ground-truth ambient
// occlusion (GTAO; Jimenez, Wu, Pesce and Jarabo, "Practical Realtime Strategies for Accurate Indirect
// Occlusion", 2016, as Intel's XeGTAO lays it out), at half resolution. For each pixel a few slices
// through the view direction are walked both ways across the depth buffer; the highest horizon on each
// side bounds the arc of sky the surface sees, and the cosine-weighted visible part of that arc,
// integrated in closed form, is the visibility. That sees corners, eaves and the gaps between logs, but
// not the far side of a street, which is off the slice or beyond the radius; for that the sky's
// visibility is also read from a height map (the scene seen from straight above, ModernRenderer's
// height pass): eight directions round the point, the highest skyline in each from the occlusion's
// radius outwards (so nothing is counted twice), and the part of the sky above those skylines that the
// surface faces, cosine-weighted. The two multiply. Output:
// visibility and the view distance in metres (for ModernBlur.hlsl's depth-aware blur).

#include "ModernCommon.hlsli"

cbuffer Ao : register(b1, space3)
{
    float4 AoParams;    // x: radius (engine units); y: falloff, as a fraction of the radius; z: slices; w: steps each way
    float4 AoParams2;   // x: strength (a power on the visibility); y: largest radius in pixels; z: 1 = the height map is used; w: its nearest skyline distance (units)
    float4 HeightMap;   // xy: the height map's centre (world); z: half its side (units); w: 1 / texels along a side
    float4 HeightRange; // x: the top of the map's depth range (z); y: its depth (units); zw unused
};

[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] Texture2D<float> DepthTex : register(t0, space2);
[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] SamplerState DepthSmp : register(s0, space2);
[[vk::combinedImageSampler]] [[vk::binding(1, 2)]] Texture2D<float4> NormalTex : register(t1, space2);
[[vk::combinedImageSampler]] [[vk::binding(1, 2)]] SamplerState NormalSmp : register(s1, space2);
[[vk::combinedImageSampler]] [[vk::binding(2, 2)]] Texture2D<float> HeightTex : register(t2, space2);
[[vk::combinedImageSampler]] [[vk::binding(2, 2)]] SamplerState HeightSmp : register(s2, space2);
[[vk::combinedImageSampler]] [[vk::binding(3, 2)]] Texture2D<float> SpareTex : register(t3, space2);
[[vk::combinedImageSampler]] [[vk::binding(3, 2)]] SamplerState SpareSmp : register(s3, space2);

ScreenOut VSMain(uint id : SV_VertexID) { return ScreenTriangle(id); }

// The centre of the depth buffer's texel a pixel of this pass stands on. At half resolution the pixel's
// own centre is the corner four texels share, and which of the four a point sampler answers with there
// is the GPU's own rounding (an NVIDIA card and Mesa's software Vulkan differ): the quarter texel settles
// it the same way on all of them, and the pixel is then placed on the ray through that texel's centre,
// where its depth was measured, not through the corner.
float2 OwnTexel(float2 uv)
{
    return (floor(clamp(uv - 0.25 * Screen.zw, 0.5 * Screen.zw, 1.0 - 0.5 * Screen.zw) * Screen.xy) + 0.5) * Screen.zw;
}

// A point of the screen in view space (x right, y up, z forward: the distance along the view axis, as
// XeGTAO's), from the four texels of the depth buffer round it. The buffer holds near / distance, which
// runs straight across the screen over a flat surface, so where the four lie on one surface their
// values weighed by hand are that surface's own depth at the point, and a sample taken anywhere along
// a slice lies on the surface it sees. (The nearest texel's depth instead, a point sampler's, is up to
// half a texel of the surface's slope out: enough at a low angle for a flat floor to rise above its own
// horizon and shade itself.) Where they do not (an edge: their values more than a tenth apart, or not
// in a plane), weighing them would invent a point hanging between the two surfaces, so the nearest
// texel is taken and the point moved to that texel's centre, where its depth was measured. The four are
// gathered at the corner they share, where no GPU's rounding chooses between texels. A point off the
// screen keeps its own ray and takes the depth of the screen's edge beside it (as XeGTAO's clamped
// sampler does), so a slice that leaves the screen goes on in its own direction.
float3 ViewPos(float2 uv)
{
    float2 at = clamp(uv, 0.5 * Screen.zw, 1.0 - 0.5 * Screen.zw);
    float2 q = at * Screen.xy - 0.5;
    float2 base = floor(q), f = q - base;
    float4 d = DepthTex.Gather(DepthSmp, (base + 1.0) * Screen.zw);   // x: (0, 1); y: (1, 1); z: (1, 0); w: (0, 0)
    float hi = max(max(d.x, d.y), max(d.z, d.w)), lo = min(min(d.x, d.y), min(d.z, d.w));
    float depth;
    if (hi - lo <= 0.1 * hi && abs(d.w + d.y - d.x - d.z) <= 0.002 * hi)
        depth = lerp(lerp(d.w, d.z, f.x), lerp(d.x, d.y, f.x), f.y);
    else
    {
        float2 nearest = step(0.5, f);
        depth = nearest.y > 0.5 ? (nearest.x > 0.5 ? d.y : d.x) : (nearest.x > 0.5 ? d.z : d.w);
        if (all(at == uv)) uv = (base + nearest + 0.5) * Screen.zw;
    }
    float z = LinearDepth(depth);
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    return float3(ndc.x / CameraRight.w * z, ndc.y / CameraUp.w * z, z);
}

float FastAcos(float x)
{
    float a = abs(x);
    float r = (-0.156583 * a + PI * 0.5) * sqrt(1.0 - a);
    return x >= 0.0 ? r : PI - r;
}

// The scene's height at a world point from the height map; far below everything outside it.
float HeightAt(float2 xy)
{
    float2 uv = (xy - HeightMap.xy) / HeightMap.z * float2(0.5, -0.5) + 0.5;
    if (any(uv < 0.0) || any(uv > 1.0)) return -1e9;
    return HeightRange.x - HeightTex.SampleLevel(HeightSmp, uv, 0) * HeightRange.y;
}

// The cosine-weighted share of the sky a surface at p facing n sees past the skylines round it, as a
// share of all the light its hemisphere takes (the part below the horizon, the ground, is not touched).
float SkyVisibility(float3 p, float3 n, float2 pixel)
{
    float blocked = 0.0, total = 0.0;
    float spin = Ign(pixel + float2(5.0, 11.0)) * 0.785398;
    float reach = Ign(pixel + float2(41.0, 3.0));
    [unroll] for (int k = 0; k < 8; k++)
    {
        float phi = k * 0.785398 + spin;
        float2 dir = float2(cos(phi), sin(phi));
        float horizon = 0.0;   // the skyline's tangent of elevation
        [unroll] for (int j = 0; j < 5; j++)
        {
            float d = AoParams2.w * exp2(j * 0.8 + reach);
            horizon = max(horizon, (HeightAt(p.xy + dir * d) - p.z) / d);
        }
        float h = atan(horizon);
        // Six bands of elevation, each weighted by its solid angle and the surface's cosine.
        [unroll] for (int m = 0; m < 6; m++)
        {
            float e = (m + 0.5) * (PI / 12.0);
            float3 w = float3(dir * cos(e), sin(e));
            float c = saturate(dot(n, w)) * cos(e);
            total += c;
            if (e < h) blocked += c;
        }
    }
    // The ground's share of the cosine lobe stays visible: the sky's share of it is (1 + n.z) / 2.
    float skyShare = saturate(n.z * 0.5 + 0.5);
    return total > 0.0 ? 1.0 - skyShare * blocked / total : 1.0;
}

float4 PSMain(ScreenOut i) : SV_Target0
{
    float2 uv = OwnTexel(i.Uv);
    float depth = DepthTex.SampleLevel(DepthSmp, uv, 0);
    if (depth <= 0.0) return float4(1.0, 60000.0, 1.0, 0);
    float3 P = ViewPos(uv);
    float3 nw = normalize(NormalTex.SampleLevel(NormalSmp, uv, 0).xyz * 2.0 - 1.0);
    float sky = 1.0;
    if (AoParams2.z > 0.5)
    {
        float3 world = WorldFromDepth(uv, depth) + nw * 8.0;
        sky = SkyVisibility(world, nw, i.Pos.xy);
    }
    float3 N = normalize(float3(dot(nw, CameraRight.xyz), dot(nw, CameraUp.xyz), dot(nw, CameraForward.xyz)));
    float3 V = normalize(-P);
    float radiusPx = min(AoParams.x * CameraUp.w * 0.5 * Screen.y / P.z, AoParams2.y);
    float metres = P.z / 72.0;
    if (radiusPx < 2.0) return float4(pow(sky, AoParams2.x), metres, 1.0, 0);

    float falloffRange = AoParams.y * AoParams.x;
    float falloffFrom = AoParams.x - falloffRange;
    int slices = (int)AoParams.z, steps = (int)AoParams.w;
    float noiseSlice = Ign(i.Pos.xy);
    float noiseStep = Ign(i.Pos.xy + float2(23.0, 71.0));
    float vis = 0.0;
    for (int s = 0; s < slices; s++)
    {
        float phi = (s + noiseSlice) * PI / slices;
        float2 omega = float2(cos(phi), -sin(phi));             // on the screen, rows running down
        float3 dir = float3(cos(phi), sin(phi), 0.0);           // the same direction in view space
        float3 ortho = dir - dot(dir, V) * V;
        float3 axis = normalize(cross(ortho, V));
        float3 projN = N - axis * dot(N, axis);
        float projLen = length(projN);
        float cosN = saturate(dot(projN, V) / max(projLen, 1e-4));
        float n = sign(dot(ortho, projN)) * FastAcos(cosN);
        float low0 = cos(n + PI * 0.5), low1 = cos(n - PI * 0.5);
        float hc0 = low0, hc1 = low1;
        for (int j = 0; j < steps; j++)
        {
            float t = (j + frac(noiseStep + j * 0.618034)) / steps;
            float2 off = omega * (t * t * radiusPx + 1.0) * Screen.zw;
            float3 d0 = ViewPos(uv + off) - P, d1 = ViewPos(uv - off) - P;
            float l0 = length(d0), l1 = length(d1);
            float c0 = dot(d0, V) / max(l0, 1e-3), c1 = dot(d1, V) / max(l1, 1e-3);
            float w0 = saturate(1.0 - (l0 - falloffFrom) / falloffRange);
            float w1 = saturate(1.0 - (l1 - falloffFrom) / falloffRange);
            hc0 = max(hc0, lerp(low0, c0, w0));
            hc1 = max(hc1, lerp(low1, c1, w1));
        }
        float h0 = -FastAcos(hc1), h1 = FastAcos(hc0);
        h0 = n + clamp(h0 - n, -PI * 0.5, PI * 0.5);
        h1 = n + clamp(h1 - n, -PI * 0.5, PI * 0.5);
        float sinN = sin(n);
        float arc0 = (cosN + 2.0 * h0 * sinN - cos(2.0 * h0 - n)) * 0.25;
        float arc1 = (cosN + 2.0 * h1 * sinN - cos(2.0 * h1 - n)) * 0.25;
        vis += projLen * (arc0 + arc1);
    }
    vis = saturate(vis / slices);
    // b: the screen's occlusion alone, for surfaces lit by the light probes, which hold the sky's (ours).
    return float4(pow(max(vis * sky, 0.03), AoParams2.x), metres, pow(max(vis, 0.03), AoParams2.x), 0);
}
