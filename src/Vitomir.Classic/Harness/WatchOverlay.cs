// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Lays a `WatchReport` out on the screen. The engine decided what to say (Diagnostics/Watch.cs);
// this decides where it goes and what colour it is. Moved from src/Vitomir.Render (the module never draws): it lays the
// overlay's quads out, which the host's renderer draws.
using System.Numerics;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Classic;

/// <summary>Lays the diagnostic overlay's content out.</summary>
public static class WatchOverlay
{
    /// <summary>The margin the corner panel sits in, in font pixels.</summary>
    public const int Margin = 8;

    /// <summary>A label closer to the camera than this is drawn at full strength; it fades out by <see cref="FadeTo"/>.</summary>
    public const float FadeFrom = 900f, FadeTo = 2500f;

    /// <summary>Labels below this alpha are not worth the clutter.</summary>
    public const float FadeFloor = 0.18f;

    public static Tint Colour(WatchTint tint) => tint switch
    {
        WatchTint.Friend => Tint.Green,
        WatchTint.Foe => Tint.Red,
        WatchTint.Looking => Tint.Amber,
        WatchTint.Gone => Tint.Grey,
        _ => Tint.Blue,
    };

    /// <summary>
    /// Queues the whole overlay. <paramref name="viewProj"/> must be the same matrix the frame was
    /// drawn with, or the labels will not sit on the people they belong to.
    /// </summary>
    public static void Draw(Overlay overlay, WatchReport report, Matrix4x4 viewProj,
                            Vector3 eye, int width, int height)
    {
        overlay.Begin(width, height);
        // Labels arrive nearest first, so anything that would land on one already placed is lifted
        // above it — which keeps the near ones where they belong and moves the far ones out of the
        // way. Two characters standing in the same doorway is the ordinary case, not the odd one.
        var taken = new List<(float X, float Y, float W, float H)>();
        foreach (var label in report.Labels) Label(overlay, label, viewProj, eye, width, height, taken);
        // The panel goes on last so a crowd of labels never draws over it.
        overlay.Panel(Margin * overlay.Scale, Margin * overlay.Scale, report.Panel, Tint.White);
    }

    /// <summary>How many times a label is lifted to clear one already placed before it is dropped.</summary>
    public const int Lifts = 6;

    static void Label(Overlay overlay, WatchLabel label, Matrix4x4 viewProj, Vector3 eye,
                      int width, int height, List<(float X, float Y, float W, float H)> taken)
    {
        if (!Project(label.At, viewProj, width, height, out var at)) return;

        float distance = Vector3.Distance(label.At, eye);
        float alpha = distance <= FadeFrom ? 1f
            : distance >= FadeTo ? 0f
            : 1f - (distance - FadeFrom) / (FadeTo - FadeFrom);
        if (alpha < FadeFloor) return;

        var tint = Colour(label.Tint);
        float bar = label.Health >= 0f ? 4f * overlay.Scale : 0f;
        // A long template name and a long target name together can be wider than the window, and a
        // label that runs off the edge is worse than a shortened one.
        var lines = new List<string>(label.Lines.Count);
        int room = Math.Max(8, (int)(width / overlay.CellWidth) - 4);
        foreach (var line in label.Lines)
            lines.Add(line.Length <= room ? line : line[..(room - 1)] + "~");

        float w = 0f;
        foreach (var line in lines) w = Math.Max(w, overlay.Measure(line));
        float pad = 3f * overlay.Scale;
        float block = lines.Count * overlay.CellHeight + bar + pad * 2f;

        // Centred on the body, sitting above the head, and kept on screen plate and all.
        float x = Math.Clamp(at.X - w / 2f, pad, Math.Max(pad, width - w - pad));
        float y = Math.Clamp(at.Y - block - 2f * overlay.Scale, pad, Math.Max(pad, height - block));

        var want = (X: x - pad, Y: y - pad, W: w + pad * 2f, H: block);
        for (int lift = 0; Clash(taken, want) is { } over; lift++)
        {
            if (lift == Lifts) return;                  // a genuine crowd: one label has to lose
            want.Y = over.Y - want.H - 2f * overlay.Scale;
            if (want.Y < 0f) return;
        }
        taken.Add(want);
        y = want.Y + pad;

        var plate = overlay.Panel(x, y, lines, tint, Tint.Backdrop, alpha);
        if (bar > 0f)
            overlay.Bar(plate.X, plate.Y + plate.H, plate.W, bar, label.Health,
                        (label.Health < 0.34f ? Tint.Red : label.Health < 0.67f ? Tint.Amber : Tint.Green).Fade(alpha));
    }

    /// <summary>The first placed label <paramref name="want"/> runs into, if any.</summary>
    static (float X, float Y, float W, float H)? Clash(List<(float X, float Y, float W, float H)> taken,
                                                       (float X, float Y, float W, float H) want)
    {
        foreach (var t in taken)
            if (want.X < t.X + t.W && t.X < want.X + want.W && want.Y < t.Y + t.H && t.Y < want.Y + want.H)
                return t;
        return null;
    }

    /// <summary>
    /// World point to screen pixels. False when it is behind the camera or off screen, which is
    /// the one thing worth getting right here: a point behind the eye projects to a plausible
    /// place on screen if the w divide is not checked first.
    /// </summary>
    public static bool Project(Vector3 world, Matrix4x4 viewProj, int width, int height, out Vector2 screen)
    {
        screen = default;
        var clip = Vector4.Transform(new Vector4(world, 1f), viewProj);
        if (clip.W <= 1e-4f) return false;
        var ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
        if (ndc.X < -1.5f || ndc.X > 1.5f || ndc.Y < -1.5f || ndc.Y > 1.5f) return false;
        screen = new Vector2((ndc.X * 0.5f + 0.5f) * width, (0.5f - ndc.Y * 0.5f) * height);
        return true;
    }
}
