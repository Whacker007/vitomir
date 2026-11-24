// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the developer's awareness overlay drawn.
using System.Numerics;
using SDL;
using Vitomir.Platform.Perception;
using Vitomir.Platform.Presentation;

namespace Vitomir.Platform.Render.Overlay;

/// <summary>
/// The awareness overlay's marks (<see cref="FrameView.Awareness"/>) drawn in the world as the reach highlight's lines
/// are (ribbons turned to the eye, against the scene's depth, with <see cref="ReachPass"/>'s own pipeline): over each
/// observer's head a mark by its rung — nothing unaware, a pale yellow <b>?</b> aware, an orange <b>!</b> alerted, a red
/// <b>!</b> alarmed — and on the ground, faintly, its cone to how far it sees a running body where it stands (a keeper of
/// the peace's in blue). and the outline of each of the level's light areas (violet) and conceal areas (green) on
/// the ground, so the bush and the dark corner can be found. Presentation only.
/// </summary>
internal sealed unsafe class AwarenessPass : IDisposable
{
    readonly ReachPass lines;
    readonly List<ReachPass.Vertex> vertices = [];

    internal static readonly Vector4 Aware = new(0.98f, 0.92f, 0.45f, 0.95f), Alerted = new(0.98f, 0.55f, 0.12f, 0.95f),
                                      Alarmed = new(0.92f, 0.12f, 0.10f, 0.97f), Cone = new(0.85f, 0.85f, 0.85f, 0.28f),
                                      KeeperCone = new(0.35f, 0.55f, 0.95f, 0.30f),
                                      LightArea = new(0.62f, 0.38f, 0.98f, 0.85f), ConcealArea = new(0.22f, 0.85f, 0.30f, 0.85f);

    /// <summary>How tall a mark is near (m), and as a share of its distance far (so it keeps its shape at any distance); and how many pieces a cone's far edge is drawn in.</summary>
    const float MarkHeight = 0.4f, MarkShare = 0.06f;
    const int ArcPieces = 12;

    public AwarenessPass(Gpu gpu) { lines = new ReachPass(gpu); }

    /// <summary>Draws <paramref name="frame"/>'s awareness marks into <paramref name="target"/>, tested against <paramref name="depth"/>; nothing when there are none.</summary>
    public void Draw(SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* target, SDL_GPUTexture* depth, int w, int h, FrameView frame)
    {
        if (frame.Awareness.Count == 0 && frame.AwarenessAreas.Count == 0) return;
        Build(frame.Awareness, frame.AwarenessAreas, frame.View.Eye, vertices);
        lines.Draw(cmd, target, depth, w, h, frame.View, vertices);
    }

    /// <summary>The triangles for <paramref name="marks"/> seen from <paramref name="eye"/>, in the renderer's units.</summary>
    internal static void Build(IReadOnlyList<AwarenessMark> marks, Vector3 eye, List<ReachPass.Vertex> into) => Build(marks, [], eye, into);

    /// <summary>The triangles for <paramref name="marks"/> and the areas' outlines <paramref name="edges"/> seen from <paramref name="eye"/>.</summary>
    internal static void Build(IReadOnlyList<AwarenessMark> marks, IReadOnlyList<AwarenessEdge> edges, Vector3 eye, List<ReachPass.Vertex> into)
    {
        into.Clear();
        for (int i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            ReachPass.Ribbon(edge.From, edge.To, eye, ReachPass.HalfWidth(Vector3.Distance(eye, edge.To)), edge.Conceal ? ConcealArea : LightArea, into);
        }
        for (int i = 0; i < marks.Count; i++)
        {
            var m = marks[i];
            // The cone, faintly, a few centimetres over the ground.
            var foot = m.Feet + new Vector3(0, 0, 0.05f);
            var colour = m.Keeper ? KeeperCone : Cone;
            float half = ReachPass.HalfWidth(Vector3.Distance(eye, foot)) * 0.7f;
            Vector3 Edge(float angle) => foot + new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0) * m.Sight;
            var left = Edge(m.Yaw + m.HalfAngle);
            var right = Edge(m.Yaw - m.HalfAngle);
            ReachPass.Ribbon(foot, left, eye, half, colour, into);
            ReachPass.Ribbon(foot, right, eye, half, colour, into);
            var last = right;
            for (int k = 1; k <= ArcPieces; k++)
            {
                var next = Edge(m.Yaw - m.HalfAngle + 2f * m.HalfAngle * k / ArcPieces);
                ReachPass.Ribbon(last, next, eye, ReachPass.HalfWidth(Vector3.Distance(eye, next)) * 0.7f, colour, into);
                last = next;
            }
            if (m.Level == Awareness.Unaware) continue;
            Glyph(m.OverHead, eye, m.Level, into);
        }
    }

    /// <summary>A ? (aware) or ! (alerted, alarmed) standing on <paramref name="at"/>, in a plane turned to the eye.</summary>
    static void Glyph(Vector3 at, Vector3 eye, Awareness level, List<ReachPass.Vertex> into)
    {
        var toward = new Vector2(eye.X - at.X, eye.Y - at.Y);
        float l = toward.Length();
        var side = l < 1e-4f ? new Vector3(1, 0, 0) : new Vector3(-toward.Y / l, toward.X / l, 0);
        var up = Vector3.UnitZ;
        float d = Vector3.Distance(eye, at);
        float stroke = MathF.Max(0.02f, ReachPass.HalfWidth(d) * 1.2f);
        var colour = level switch { Awareness.Aware => Aware, Awareness.Alerted => Alerted, _ => Alarmed };
        float s = MathF.Max(MarkHeight, MarkShare * d);
        Vector3 P(float x, float y) => at + side * (x * s) + up * (y * s);
        // The dot under either mark.
        ReachPass.Ribbon(P(0, 0), P(0, 0.12f), eye, stroke, colour, into);
        if (level == Awareness.Aware)
        {
            ReadOnlySpan<Vector2> hook = [new(-0.22f, 0.78f), new(-0.1f, 0.95f), new(0.1f, 0.95f), new(0.22f, 0.8f), new(0.2f, 0.62f), new(0f, 0.48f), new(0f, 0.3f)];
            for (int k = 1; k < hook.Length; k++) ReachPass.Ribbon(P(hook[k - 1].X, hook[k - 1].Y), P(hook[k].X, hook[k].Y), eye, stroke, colour, into);
        }
        else
        {
            ReachPass.Ribbon(P(0, 0.3f), P(0, 1f), eye, stroke * (level == Awareness.Alarmed ? 1.4f : 1f), colour, into);
        }
    }

    public void Dispose() => lines.Dispose();
}
