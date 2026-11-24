// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the developer's swing trail drawn (where the blade goes against where the crosshair points).
using System.Numerics;
using SDL;
using Vitomir.Platform.Presentation;

namespace Vitomir.Platform.Render.Overlay;

/// <summary>
/// The swing's trail (<see cref="FrameView.SwingTrail"/>) drawn over everything with the reach highlight's ribbons: the
/// blade's tip path a bright cyan line, the blade at every few swept places a faint pale stroke (the surface it swept), the
/// reticle's point a magenta star, the tip's nearest pass to the reticle's ray a yellow star and the miss between them an
/// orange line; each fading as its swing's trail is let go. Presentation only.
/// </summary>
internal sealed unsafe class SwingTrailPass : IDisposable
{
    readonly ReachPass lines;
    readonly List<ReachPass.Vertex> vertices = [];

    /// <summary>The colours by <see cref="SwingTrailKind"/>, by its number (alpha before the fade).</summary>
    internal static readonly Vector4[] Colours =
    [
        new(0.20f, 0.95f, 1.00f, 0.95f),   // the tip's path
        new(0.80f, 0.92f, 1.00f, 0.30f),   // the blade
        new(1.00f, 0.25f, 0.90f, 1.00f),   // the reticle's point
        new(1.00f, 0.90f, 0.15f, 1.00f),   // the nearest pass
        new(1.00f, 0.45f, 0.15f, 0.95f),   // the miss
        new(1.00f, 1.00f, 1.00f, 1.00f),   // an arrow's shaft
        new(0.35f, 1.00f, 0.35f, 0.85f),   // the bow's forecast path (green)
        new(1.00f, 0.20f, 0.20f, 1.00f),   // the point of impact (red)
        new(1.00f, 0.85f, 0.20f, 0.90f),   // a shot's real path (amber)
    ];

    /// <summary>A line's half-width as a share of the eye's distance, and its least, m, by kind; a star's arm, m.</summary>
    const float TipWidth = 0.0016f, BladeWidth = 0.0007f, MissWidth = 0.0012f, Least = 0.002f, Arm = 0.06f;

    public SwingTrailPass(Gpu gpu) { lines = new ReachPass(gpu, overAll: true); }

    /// <summary>Draws <paramref name="frame"/>'s swing trail into <paramref name="target"/>; nothing when there is none.</summary>
    public void Draw(SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* target, SDL_GPUTexture* depth, int w, int h, FrameView frame)
    {
        if (frame.SwingTrail.Count == 0) return;
        Build(frame.SwingTrail, frame.View.Eye, vertices);
        if (vertices.Count > 0) lines.Draw(cmd, target, depth, w, h, frame.View, vertices);
    }

    /// <summary>The triangles of <paramref name="trail"/>'s lines and points seen from <paramref name="eye"/>.</summary>
    internal static void Build(IReadOnlyList<SwingTrailLine> trail, Vector3 eye, List<ReachPass.Vertex> into)
    {
        into.Clear();
        for (int i = 0; i < trail.Count; i++)
        {
            var l = trail[i];
            if (l.Fade <= 0f) continue;
            int kind = (int)l.Kind;
            var colour = Colours[kind < Colours.Length ? kind : 0];
            colour.W *= Math.Clamp(l.Fade, 0f, 1f);
            float distance = Vector3.Distance(eye, (l.A + l.B) * 0.5f);
            if (l.Kind is SwingTrailKind.Reticle or SwingTrailKind.Nearest or SwingTrailKind.BowImpact)
            {
                // The point of impact a star that keeps its size on the screen (its arms a share of the distance).
                float arm = l.Kind == SwingTrailKind.BowImpact ? MathF.Max(Arm, 0.012f * distance) : l.Kind == SwingTrailKind.Reticle ? Arm * 1.4f : Arm;
                Star(l.A, eye, arm, MathF.Max(Least, TipWidth * distance), colour, into);
                continue;
            }
            if (Vector3.DistanceSquared(l.A, l.B) < 1e-10f) continue;
            float width = l.Kind switch { SwingTrailKind.Tip => TipWidth, SwingTrailKind.Blade => BladeWidth, _ => MissWidth };
            ReachPass.Ribbon(l.A, l.B, eye, MathF.Max(Least * (l.Kind == SwingTrailKind.Blade ? 0.5f : 1f), width * distance), colour, into);
        }
    }

    /// <summary>A six-armed star at <paramref name="at"/>, facing <paramref name="eye"/>: its arms <paramref name="arm"/> long.</summary>
    static void Star(Vector3 at, Vector3 eye, float arm, float half, Vector4 colour, List<ReachPass.Vertex> into)
    {
        var toward = at - eye;
        float distance = toward.Length();
        if (distance < 1e-3f) return;
        var forward = toward / distance;
        var right = Vector3.Cross(forward, Vector3.UnitZ);
        right = right.LengthSquared() > 1e-6f ? Vector3.Normalize(right) : Vector3.UnitX;
        var up = Vector3.Cross(right, forward);
        for (int k = 0; k < 3; k++)
        {
            float a = MathF.PI * k / 3f + MathF.PI / 2f;
            var dir = right * MathF.Cos(a) + up * MathF.Sin(a);
            ReachPass.Ribbon(at - dir * arm, at + dir * arm, eye, half, colour, into);
        }
    }

    public void Dispose() => lines.Dispose();
}
