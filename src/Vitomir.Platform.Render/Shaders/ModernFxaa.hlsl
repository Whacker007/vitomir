// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Shaders/ModernFxaa.hlsl at b4185ef, reworked for the platform.
// The modern path's last pass (ours): FXAA, after Timothy Lottes' FXAA 3.11
// "quality" algorithm. It finds where luma steps across an edge, walks along the edge to its two ends,
// and moves the sample across the edge by how far this pixel is from the nearer end, which smooths
// stair-steps; a sub-pixel term softens single-pixel sparkle. Luma comes in the input's alpha.

cbuffer Params : register(b0, space3)
{
    float4 Params;      // xy: 1 / frame size; z: 1 = on
};

[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] Texture2D<float4> Ldr : register(t0, space2);
[[vk::combinedImageSampler]] [[vk::binding(0, 2)]] SamplerState LdrSmp : register(s0, space2);

struct VSOut
{
    float4 Pos : SV_Position;
    float2 Uv : TEXCOORD0;
};

VSOut VSMain(uint id : SV_VertexID)
{
    float2 uv = float2((id << 1) & 2, id & 2);
    VSOut o;
    o.Uv = uv;
    o.Pos = float4(uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    return o;
}

float Luma(float2 uv) { return Ldr.SampleLevel(LdrSmp, uv, 0).a; }

float4 PSMain(VSOut i) : SV_Target0
{
    float2 uv = i.Uv;
    float4 rgbM = Ldr.SampleLevel(LdrSmp, uv, 0);
    if (Params.z < 0.5) return float4(rgbM.rgb, 1.0);
    float2 rcp = Params.xy;
    float M = rgbM.a;
    float N = Luma(uv + float2(0, -rcp.y)), S = Luma(uv + float2(0, rcp.y));
    float E = Luma(uv + float2(rcp.x, 0)), W = Luma(uv + float2(-rcp.x, 0));
    float hi = max(max(max(N, S), max(E, W)), M), lo = min(min(min(N, S), min(E, W)), M);
    float range = hi - lo;
    if (range < max(0.0312, hi * 0.125)) return float4(rgbM.rgb, 1.0);

    float NW = Luma(uv + float2(-rcp.x, -rcp.y)), NE = Luma(uv + float2(rcp.x, -rcp.y));
    float SW = Luma(uv + float2(-rcp.x, rcp.y)), SE = Luma(uv + float2(rcp.x, rcp.y));
    float edgeH = abs(N + S - 2 * M) * 2 + abs(NE + SE - 2 * E) + abs(NW + SW - 2 * W);
    float edgeV = abs(E + W - 2 * M) * 2 + abs(NE + NW - 2 * N) + abs(SE + SW - 2 * S);
    bool horizontal = edgeH >= edgeV;

    float average = ((N + S + E + W) * 2 + (NW + NE + SW + SE)) / 12.0;
    float sub = saturate(abs(average - M) / range);
    sub = (-2.0 * sub + 3.0) * sub * sub;
    float subOffset = sub * sub * 0.75;

    float step = horizontal ? rcp.y : rcp.x;
    float l1 = horizontal ? N : W, l2 = horizontal ? S : E;
    float g1 = abs(l1 - M), g2 = abs(l2 - M);
    bool steepest1 = g1 >= g2;
    if (steepest1) step = -step;
    float localAvg = 0.5 * ((steepest1 ? l1 : l2) + M);
    float gradScaled = max(g1, g2) * 0.25;

    float2 pos = uv;
    if (horizontal) pos.y += step * 0.5; else pos.x += step * 0.5;
    float2 along = horizontal ? float2(rcp.x, 0) : float2(0, rcp.y);
    float2 pN = pos - along, pP = pos + along;
    float eN = Luma(pN) - localAvg, eP = Luma(pP) - localAvg;
    bool doneN = abs(eN) >= gradScaled, doneP = abs(eP) >= gradScaled;
    static const float Steps[10] = { 1.0, 1.0, 1.0, 1.5, 2.0, 2.0, 2.0, 2.0, 4.0, 8.0 };
    [unroll] for (int k = 0; k < 10; k++)
    {
        if (doneN && doneP) break;
        if (!doneN) { pN -= along * Steps[k]; eN = Luma(pN) - localAvg; doneN = abs(eN) >= gradScaled; }
        if (!doneP) { pP += along * Steps[k]; eP = Luma(pP) - localAvg; doneP = abs(eP) >= gradScaled; }
    }
    float dN = horizontal ? uv.x - pN.x : uv.y - pN.y;
    float dP = horizontal ? pP.x - uv.x : pP.y - uv.y;
    bool nearerN = dN < dP;
    float endLuma = nearerN ? eN : eP;
    float edgeOffset = ((M - localAvg < 0.0) == (endLuma < 0.0)) ? 0.0 : 0.5 - min(dN, dP) / (dN + dP);
    float offset = max(edgeOffset, subOffset);
    float2 f = uv;
    if (horizontal) f.y += offset * step; else f.x += offset * step;
    return float4(Ldr.SampleLevel(LdrSmp, f, 0).rgb, 1.0);
}
