// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Shaders/ModernProbes.hlsli at b4185ef, reworked for the platform.
// The light probes (ours; ModernProbes.cs): a grid of baked
// irradiance over a level's open air, read trilinearly for a surface's ambient light in place of the flat
// sky. ProbeTex is one 3D texture of four slabs one above the other, slab k holding the probes' k-th
// spherical-harmonic coefficient (Y00; then y, z, x) of irradiance in rgb and of sky visibility in alpha.
// Include after ModernCommon.hlsli, with ProbeTex and ProbeSmp declared.

#ifndef MODERN_PROBES_HLSLI
#define MODERN_PROBES_HLSLI

struct ProbeSample
{
    float4 c0, c1, c2, c3;
    float weight;   // 1 inside the grid, fading to 0 over its outermost cell; 0 without probes
};

float4 ProbeSlab(float3 c, float k)
{
    float3 n = ProbeDims.xyz;
    float3 cc = clamp(c, 0.0, n - 1.0);   // within one slab, so the filter never reaches the next
    float3 uvw = float3((cc.x + 0.5) / n.x, (cc.y + 0.5) / n.y, (cc.z + 0.5 + k * n.z) / (4.0 * n.z));
    return ProbeTex.SampleLevel(ProbeSmp, uvw, 0);
}

// A room (ours; ModernProbeRoom.cs is this function's model and test): a surface inside an interior's
// box reads the probes at a point held a whole spacing inside the box's faces, so the trilinear filter stays in
// the room's own air and never reaches the street's probes through a 0.4 m wall. Over the rise outside the box
// (the doorway's reveals) the point slides from the street's to the room's.
float3 ProbeRoomPoint(float3 world, float3 p)
{
    int n = (int)ProbeDims.w;
    for (int i = 0; i < n; i++)
    {
        float4 a = ProbeRoomA[i], b = ProbeRoomB[i];
        float3 d = world - a.xyz;
        float3 q = float3(a.w * d.x + b.w * d.y, -b.w * d.x + a.w * d.y, d.z);
        float3 inside = min(q, b.xyz - q);
        float t = saturate(min(inside.x, min(inside.y, inside.z)) / ProbeGrid.z + 1.0);
        if (t <= 0.0) continue;
        float3 e = p - a.xyz;
        float3 r = float3(a.w * e.x + b.w * e.y, -b.w * e.x + a.w * e.y, e.z);
        float k = ProbeGrid.w * (abs(a.w) + abs(b.w));
        float3 shrink = min(float3(k, k, ProbeGrid.w), b.xyz * 0.5);
        r = clamp(r, shrink, b.xyz - shrink);
        return lerp(p, a.xyz + float3(a.w * r.x - b.w * r.y, b.w * r.x + a.w * r.y, r.z), t);
    }
    return p;
}

// The probes round a surface's point, read a little off the surface along its geometric normal (so a wall's
// light comes from the air in front of it, not the probes behind it inside the house).
ProbeSample ProbesAt(float3 world, float3 ng)
{
    ProbeSample s = (ProbeSample)0;
    if (ProbeOrigin.w < 0.5) return s;
    float3 c = (ProbeRoomPoint(world, world + ng * ProbeGrid.y) - ProbeOrigin.xyz) * ProbeGrid.x;
    float3 edge = min(c, ProbeDims.xyz - 1.0 - c);
    s.weight = saturate(min(edge.x, min(edge.y, edge.z)));
    if (s.weight <= 0.0) return s;
    s.c0 = ProbeSlab(c, 0.0); s.c1 = ProbeSlab(c, 1.0); s.c2 = ProbeSlab(c, 2.0); s.c3 = ProbeSlab(c, 3.0);
    return s;
}

// The irradiance on a surface facing n.
float3 ProbeIrradiance(ProbeSample s, float3 n)
{
    return max(s.c0.rgb * 0.282095 + (s.c1.rgb * n.y + s.c2.rgb * n.z + s.c3.rgb * n.x) * 0.488603, 0.0);
}

// How open the sky is in direction d, 0..1.
float ProbeSkyVisibility(ProbeSample s, float3 d)
{
    return saturate(s.c0.a * 0.282095 + (s.c1.a * d.y + s.c2.a * d.z + s.c3.a * d.x) * 0.488603);
}

#endif
