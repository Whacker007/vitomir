// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the developer's hit volumes overlay drawn.
using System.Numerics;
using SDL;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Presentation;

namespace Vitomir.Platform.Render.Overlay;

/// <summary>
/// The hit volumes' overlay (<see cref="FrameView.HitVolumes"/>) drawn as wire outlines with the reach highlight's
/// ribbons, <b>over</b> everything (the capsules are inside the bodies they outline): each capsule's two end rings, four
/// lines along its sides and the arcs over its round ends, in its region's colour — the head red, the neck orange, the
/// chest yellow, the belly lime, the hips violet, the left arm's parts blues, the right arm's greens, the left leg's
/// purples, the right leg's pinks. Presentation only.
/// </summary>
internal sealed unsafe class HitVolumePass : IDisposable
{
    readonly ReachPass lines;
    readonly List<ReachPass.Vertex> vertices = [];

    /// <summary>The colour of each <see cref="BodyRegion"/>, by its number.</summary>
    internal static readonly Vector4[] Colours =
    [
        new(1f, 1f, 1f, 0.9f),                                                              // none
        new(0.98f, 0.25f, 0.22f, 0.95f), new(0.98f, 0.58f, 0.18f, 0.95f),                 // head, neck
        new(0.98f, 0.90f, 0.25f, 0.95f), new(0.70f, 0.95f, 0.30f, 0.95f),                 // chest, belly
        new(0.68f, 0.42f, 0.98f, 0.95f),                                                    // hips
        new(0.25f, 0.55f, 1.00f, 0.95f), new(0.20f, 0.85f, 0.45f, 0.95f),                 // upper arms (left, right)
        new(0.40f, 0.72f, 1.00f, 0.95f), new(0.40f, 0.95f, 0.60f, 0.95f),                 // forearms
        new(0.62f, 0.85f, 1.00f, 0.95f), new(0.65f, 1.00f, 0.78f, 0.95f),                 // hands
        new(0.62f, 0.30f, 0.90f, 0.95f), new(0.95f, 0.35f, 0.65f, 0.95f),                 // thighs
        new(0.76f, 0.50f, 0.98f, 0.95f), new(0.98f, 0.52f, 0.78f, 0.95f),                 // shins
        new(0.86f, 0.70f, 1.00f, 0.95f), new(1.00f, 0.72f, 0.88f, 0.95f),                 // feet
    ];

    /// <summary>How many pieces a ring is drawn in, and a round end's arc.</summary>
    const int RingPieces = 12, ArcPieces = 6;

    public HitVolumePass(Gpu gpu) { lines = new ReachPass(gpu, overAll: true); }

    /// <summary>Draws <paramref name="frame"/>'s hit volumes into <paramref name="target"/>; nothing when there are none.</summary>
    public void Draw(SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* target, SDL_GPUTexture* depth, int w, int h, FrameView frame)
    {
        if (frame.HitVolumes.Count == 0) return;
        Build(frame.HitVolumes, frame.View.Eye, vertices);
        lines.Draw(cmd, target, depth, w, h, frame.View, vertices);
    }

    /// <summary>The triangles of <paramref name="volumes"/>' outlines seen from <paramref name="eye"/>.</summary>
    internal static void Build(IReadOnlyList<DrawnVolume> volumes, Vector3 eye, List<ReachPass.Vertex> into)
    {
        into.Clear();
        for (int i = 0; i < volumes.Count; i++)
        {
            var v = volumes[i];
            // A volume (or a shield's edge, or the mark of a blow on the level) struck a moment ago: white and thicker.
            var colour = v.Struck ? new Vector4(1f, 1f, 1f, 1f) : Colours[(int)v.Region < Colours.Length ? (int)v.Region : 0];
            var axis = v.B - v.A;
            float length = axis.Length();
            var u = length > 1e-6f ? axis / length : Vector3.UnitZ;
            var e1 = Geometry.Perpendicular(u);
            var e2 = Vector3.Cross(u, e1);
            float half = MathF.Max(0.003f, 0.0011f * Vector3.Distance(eye, (v.A + v.B) * 0.5f)) * (v.Struck ? 2.2f : 1f);   // thin: a body's outlines, not a lip's
            Vector3 Round(Vector3 centre, float angle) => centre + (e1 * MathF.Cos(angle) + e2 * MathF.Sin(angle)) * v.Radius;
            // The two end rings.
            for (int k = 0; k < RingPieces; k++)
            {
                float a0 = MathF.Tau * k / RingPieces, a1 = MathF.Tau * (k + 1) / RingPieces;
                ReachPass.Ribbon(Round(v.A, a0), Round(v.A, a1), eye, half, colour, into);
                if (length > 1e-6f) ReachPass.Ribbon(Round(v.B, a0), Round(v.B, a1), eye, half, colour, into);
            }
            // Four lines along the side.
            if (length > 1e-6f)
                for (int k = 0; k < 4; k++)
                {
                    float a = MathF.Tau * k / 4f;
                    ReachPass.Ribbon(Round(v.A, a), Round(v.B, a), eye, half, colour, into);
                }
            // The arcs over each round end, in two planes through the axis.
            for (int plane = 0; plane < 2; plane++)
            {
                var side = plane == 0 ? e1 : e2;
                for (int end = 0; end < 2; end++)
                {
                    var centre = end == 0 ? v.A : v.B;
                    var outward = end == 0 ? -u : u;
                    for (int k = 0; k < ArcPieces * 2; k++)
                    {
                        float a0 = MathF.PI * k / (ArcPieces * 2), a1 = MathF.PI * (k + 1) / (ArcPieces * 2);
                        var p0 = centre + (side * MathF.Cos(a0) + outward * MathF.Sin(a0)) * v.Radius;
                        var p1 = centre + (side * MathF.Cos(a1) + outward * MathF.Sin(a1)) * v.Radius;
                        ReachPass.Ribbon(p0, p1, eye, half, colour, into);
                    }
                }
            }
        }
    }

    public void Dispose() => lines.Dispose();
}
