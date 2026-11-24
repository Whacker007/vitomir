// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Engine/Levels/Cells/CellStreamPlan.cs at b4185ef, reworked for the platform.
using System.Numerics;

namespace Vitomir.Platform.Render.Level;

/// <summary>
/// The streaming's arithmetic the renderer needs (ours, the old program's Phase 3): how far a point
/// is from a box across the ground, and which level of detail a cell is drawn at with hysteresis.
/// </summary>
internal static class CellStreamPlan
{
    /// <summary>The distance across the ground from <paramref name="p"/> to the box (0 inside it).</summary>
    public static float Distance(Vector2 p, Vector2 min, Vector2 max)
    {
        float dx = MathF.Max(MathF.Max(min.X - p.X, 0f), p.X - max.X);
        float dy = MathF.Max(MathF.Max(min.Y - p.Y, 0f), p.Y - max.Y);
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// The level of detail for a distance: the last whose <paramref name="from"/> distance is passed,
    /// keeping <paramref name="current"/> until the distance is <paramref name="hysteresis"/> past its edges.
    /// </summary>
    public static int Lod(float distance, int current, IReadOnlyList<float> from, float hysteresis)
    {
        int lod = 0;
        for (int k = 1; k < from.Count; k++) if (distance >= from[k]) lod = k;
        if (current < 0 || current >= from.Count || current == lod) return lod;
        float lo = from[current] - hysteresis;
        float hi = current + 1 < from.Count ? from[current + 1] + hysteresis : float.MaxValue;
        return distance >= lo && distance < hi ? current : lod;
    }
}
