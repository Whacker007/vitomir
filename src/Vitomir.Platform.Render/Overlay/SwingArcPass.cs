// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the swing's arc drawn ("a kind of faint very translucent kind of crescent arc").
using System.Numerics;
using SDL;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Render.Modern;

namespace Vitomir.Platform.Render.Overlay;

/// <summary>
/// The swing's arc (<see cref="FrameView.SwingArc"/>) drawn in the world as a faint translucent crescent: the surface the next
/// blow's blade sweeps from its middle to its tip, its tip edge the brightest and its inner edge fading to nothing, brightest
/// at the strike mark and fading toward its ends (the quads' brightness), at <see cref="FrameView.SwingArcOpacity"/>. Drawn
/// twice with the reach highlight's pipeline: tested against the scene's depth where nothing is nearer, and over everything at
/// <see cref="HiddenShare"/> of that where something is (a wall, a body, the own arm in first person), so the path still
/// reads through what hides it and where it enters a body shows. Presentation only.
/// </summary>
internal sealed unsafe class SwingArcPass : IDisposable
{
    readonly ReachPass seen, hidden;
    readonly List<ReachPass.Vertex> front = [], through = [];

    /// <summary>The arc's colour: a pale, slightly warm white (a blade's gleam), its alpha the opacity's.</summary>
    internal static readonly Vector3 Colour = new(1.00f, 0.97f, 0.88f);

    /// <summary>The share of the opacity drawn where the arc is hidden (ours; SwingArcSettings.HiddenShare's default).</summary>
    internal const float HiddenShare = 0.35f;

    /// <summary>The inner (mid-blade) edge's share of the tip edge's brightness: the crescent fades inward.</summary>
    internal const float InnerShare = 0f;

    /// <summary>The mark where the blade would first meet someone: a warm amber, at this many times the arc's opacity.</summary>
    internal static readonly Vector3 MarkColour = new(1.00f, 0.72f, 0.30f);
    internal const float MarkBoost = 2.2f;

    public SwingArcPass(Gpu gpu)
    {
        seen = new ReachPass(gpu, overAll: false);
        hidden = new ReachPass(gpu, overAll: true);
    }

    public void Draw(SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* target, SDL_GPUTexture* depth, int w, int h, FrameView frame)
    {
        if (frame.SwingArc.Count == 0 || frame.SwingArcOpacity <= 0f) return;
        Build(frame.SwingArc, frame.SwingArcOpacity, HiddenShare, through, front);
        if (through.Count > 0) hidden.Draw(cmd, target, depth, w, h, frame.View, through);
        if (front.Count > 0) seen.Draw(cmd, target, depth, w, h, frame.View, front);
    }

    /// <summary>
    /// The triangles of the arc at <paramref name="opacity"/>: into <paramref name="through"/> the layer drawn over everything
    /// (at <paramref name="hiddenShare"/> of the opacity) and into <paramref name="front"/> the depth-tested layer, whose alpha
    /// makes the two together the full opacity where nothing hides the arc.
    /// </summary>
    internal static void Build(IReadOnlyList<SwingArcQuad> quads, float opacity, float hiddenShare, List<ReachPass.Vertex> through, List<ReachPass.Vertex> front)
    {
        through.Clear();
        front.Clear();
        opacity = Math.Clamp(opacity, 0f, 1f);
        hiddenShare = Math.Clamp(hiddenShare, 0f, 1f);
        float u = ModernMath.UnitsPerMetre;
        for (int i = 0; i < quads.Count; i++)
        {
            var q = quads[i];
            Quad(q, opacity * hiddenShare, u, through);
            Quad(q, -1f, u, front, opacity, hiddenShare);
        }
    }

    /// <summary>
    /// One quad's two triangles: its tip edge at the corners' brightness × <paramref name="alpha"/>, its inner edge at
    /// <see cref="InnerShare"/> of that. With <paramref name="alpha"/> below 0, the alpha that over a layer of
    /// <paramref name="hiddenShare"/> × <paramref name="opacity"/> makes <paramref name="opacity"/>.
    /// </summary>
    static void Quad(in SwingArcQuad q, float alpha, float u, List<ReachPass.Vertex> into, float opacity = 0f, float hiddenShare = 0f)
    {
        float A(float brightness, float share)
        {
            if (alpha >= 0f) return alpha * brightness * share;
            float full = opacity * brightness * share, under = full * hiddenShare;
            return under >= 1f ? 0f : Math.Clamp((full - under) / (1f - under), 0f, 1f);
        }
        // The meeting's mark: whole across the blade, a warmer colour, a little more than the arc's brightest (at most opaque).
        var colour = q.Mark ? MarkColour : Colour;
        float inner = q.Mark ? 1f : InnerShare, boost = q.Mark ? MarkBoost : 1f;
        var ta = new ReachPass.Vertex(q.TipA * u, new Vector4(colour, MathF.Min(1f, A(q.A, 1f) * boost)));
        var tb = new ReachPass.Vertex(q.TipB * u, new Vector4(colour, MathF.Min(1f, A(q.B, 1f) * boost)));
        var ma = new ReachPass.Vertex(q.MidA * u, new Vector4(colour, MathF.Min(1f, A(q.A, inner) * boost)));
        var mb = new ReachPass.Vertex(q.MidB * u, new Vector4(colour, MathF.Min(1f, A(q.B, inner) * boost)));
        into.Add(ma); into.Add(ta); into.Add(tb);
        into.Add(ma); into.Add(tb); into.Add(mb);
    }

    public void Dispose()
    {
        seen.Dispose();
        hidden.Dispose();
    }
}
