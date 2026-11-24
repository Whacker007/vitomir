// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Engine.Levels.Cells;

/// <summary>
/// **Ours (Phase 3): the streamers' decisions**, as pure functions of where the focus is and what is
/// loaded, so they can be tested without a thread or a file. Distances are across the ground (X and Y),
/// from the focus to the nearest point of a cell's box — so a tall tower is as near as its footprint.
///
/// <b>Hysteresis.</b> A cell is wanted when its box comes within the radius and let go only when it is
/// more than the radius and the hysteresis away, so walking back and forth along the radius's edge does
/// not load and unload the same cell every few steps. The levels of detail keep a band the same way.
/// </summary>
public static class CellStreamPlan
{
    /// <summary>The distance across the ground from <paramref name="p"/> to the box <paramref name="min"/>..<paramref name="max"/>; 0 inside it.</summary>
    public static float Distance(Vector2 p, Vector2 min, Vector2 max)
    {
        float dx = MathF.Max(MathF.Max(min.X - p.X, 0f), p.X - max.X);
        float dy = MathF.Max(MathF.Max(min.Y - p.Y, 0f), p.Y - max.Y);
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>One cell as the plan sees it: its box across the ground, and whether it is always in.</summary>
    public readonly record struct Cell(Vector2 Min, Vector2 Max, bool Resident = false);

    /// <summary>
    /// The cells that should be in, given the focus and what is in now: every resident cell, every cell
    /// within <paramref name="radius"/>, and every cell already in that is within
    /// <paramref name="radius"/> + <paramref name="hysteresis"/>. Nearest first.
    /// </summary>
    public static List<int> Wanted(Vector2 focus, IReadOnlyList<Cell> cells, IReadOnlySet<int> current, float radius, float hysteresis)
    {
        var wanted = new List<(int Index, float Distance)>();
        for (int i = 0; i < cells.Count; i++)
        {
            float d = cells[i].Resident ? 0f : Distance(focus, cells[i].Min, cells[i].Max);
            if (cells[i].Resident || d <= radius || (current.Contains(i) && d <= radius + hysteresis)) wanted.Add((i, d));
        }
        return [.. wanted.OrderBy(w => w.Distance).ThenBy(w => w.Index).Select(w => w.Index)];
    }

    /// <summary>
    /// Which of <paramref name="wanted"/> to load (not in <paramref name="current"/>, nearest first) and
    /// which of <paramref name="current"/> to let go (not wanted).
    /// </summary>
    public static (List<int> Load, List<int> Unload) Changes(IReadOnlyList<int> wanted, IReadOnlySet<int> current)
    {
        var want = wanted.ToHashSet();
        return ([.. wanted.Where(i => !current.Contains(i))], [.. current.Where(i => !want.Contains(i)).Order()]);
    }

    /// <summary>
    /// The level of detail for a distance: the highest <c>k</c> with <paramref name="from"/>[k] ≤ distance
    /// (<paramref name="from"/> rising, from[0] = 0), except that a level already drawn is kept until the
    /// distance is <paramref name="hysteresis"/> past its band, so a cell on the line does not flicker.
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
