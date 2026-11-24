// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours (the): the flurry's cue drawn.
using System.Numerics;
using SDL;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Presentation;

namespace Vitomir.Platform.Render.Overlay;

/// <summary>
/// The flurry's cue (<see cref="FrameView.Cues"/>) drawn over everything with the reach highlight's ribbons: each extra
/// blow a short eight-pointed star at the struck point (each further one a few centimetres off it), facing the eye, that
/// opens from a few centimetres to about a hand's length and fades over its life — pale gold at its heart, the colour of a spark off steel. Presentation only.
/// </summary>
internal sealed unsafe class CuePass : IDisposable
{
    readonly ReachPass lines;
    readonly List<ReachPass.Vertex> vertices = [];

    /// <summary>The star's arm at its birth and at its full size, m, and its ribbons' half-width as a share of the eye's distance.</summary>
    const float ArmFrom = 0.04f, ArmTo = 0.16f, HalfWidth = 0.0024f;

    /// <summary>How far each further flash of a flurry is set from the struck point, m.</summary>
    const float Spread = 0.05f;

    /// <summary>A guard's flash's arm as a share of a star's (ours, presentation).</summary>
    const float GuardedArm = 0.6f;

    public CuePass(Gpu gpu) { lines = new ReachPass(gpu, overAll: true); }

    /// <summary>Draws <paramref name="frame"/>'s cues into <paramref name="target"/>; nothing when there are none.</summary>
    public void Draw(SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* target, SDL_GPUTexture* depth, int w, int h, FrameView frame)
    {
        if (frame.Cues.Count == 0) return;
        Build(frame.Cues, frame.View.Eye, vertices);
        if (vertices.Count > 0) lines.Draw(cmd, target, depth, w, h, frame.View, vertices);
    }

    /// <summary>The triangles of <paramref name="cues"/>' stars seen from <paramref name="eye"/>.</summary>
    internal static void Build(IReadOnlyList<BlowCueMark> cues, Vector3 eye, List<ReachPass.Vertex> into)
    {
        into.Clear();
        for (int i = 0; i < cues.Count; i++)
        {
            var c = cues[i];
            float share = c.Life > 0f ? Math.Clamp(c.Age / c.Life, 0f, 1f) : 1f;
            var toward = c.Point - eye;
            float distance = toward.Length();
            if (distance < 1e-3f) continue;
            var forward = toward / distance;
            var right = Vector3.Cross(forward, Vector3.UnitZ);
            right = right.LengthSquared() > 1e-6f ? Vector3.Normalize(right) : Vector3.UnitX;
            var up = Vector3.Cross(right, forward);
            float arm = ArmFrom + (ArmTo - ArmFrom) * MathF.Sqrt(share);
            float alpha = 1f - share * share;
            float half = MathF.Max(0.002f, HalfWidth * distance);
            var heart = new Vector4(1f, 0.93f, 0.62f, alpha);
            var rim = new Vector4(1f, 0.78f, 0.35f, alpha * 0.8f);
            if (c.Guarded)
            {
                // A blow a guard took — a small steel-grey flash at the shield or the blade (no gold:
                // nothing landed), a four-pointed cross a little more than half the star's size, opening and fading as quickly.
                arm *= GuardedArm;
                var steel = new Vector4(0.80f, 0.84f, 0.90f, alpha);
                var at0 = c.Point;
                for (int k = 0; k < 4; k++)
                {
                    float a0 = MathF.PI / 4f + MathF.PI * k / 2f;
                    var dir = right * MathF.Cos(a0) + up * MathF.Sin(a0);
                    ReachPass.Ribbon(at0, at0 + dir * arm, eye, half, steel, into);
                }
                continue;
            }
            // Four points (the long cross) and four short ones between them, turned a little each blow and each set a few
            // centimetres from the last round the point, so a flurry's k stars are seen as k.
            float turn = 0.35f * c.Index;
            var at = c.Point + (right * MathF.Cos(2.4f * c.Index) + up * MathF.Sin(2.4f * c.Index)) * (Spread * (c.Index - 1));
            for (int k = 0; k < 8; k++)
            {
                float a = turn + MathF.PI * k / 4f;
                var dir = right * MathF.Cos(a) + up * MathF.Sin(a);
                float length = (k % 2 == 0) ? arm : arm * 0.5f;
                ReachPass.Ribbon(at, at + dir * length, eye, k % 2 == 0 ? half : half * 0.7f, k % 2 == 0 ? heart : rim, into);
            }
        }
    }

    public void Dispose() => lines.Dispose();
}
