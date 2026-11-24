// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Render.Modern;

/// <summary>
/// **Ours (Phase 3): ranges of one big GPU buffer handed out and taken back** — the streamed level's
/// geometry arena, where every loaded cell's vertices and indices live side by side so the renderer
/// binds one vertex buffer and one index buffer for the whole world. First fit over a list of free
/// ranges kept in order, a freed range joined to its free neighbours. Counts are in elements (vertices
/// or indices), not bytes.
/// </summary>
public sealed class ArenaAllocator
{
    readonly List<(long Start, long Length)> free;

    public long Capacity { get; }
    public long Used { get; private set; }

    public ArenaAllocator(long capacity)
    {
        Capacity = capacity;
        free = capacity > 0 ? [(0, capacity)] : [];
    }

    /// <summary>The start of a free range of <paramref name="length"/>, taken; −1 when no range is long enough.</summary>
    public long Allocate(long length)
    {
        if (length <= 0) return 0;
        for (int i = 0; i < free.Count; i++)
        {
            var (start, len) = free[i];
            if (len < length) continue;
            if (len == length) free.RemoveAt(i);
            else free[i] = (start + length, len - length);
            Used += length;
            return start;
        }
        return -1;
    }

    /// <summary>Gives a range back, joining it to the free ranges on either side.</summary>
    public void Free(long start, long length)
    {
        if (length <= 0) return;
        int i = 0;
        while (i < free.Count && free[i].Start < start) i++;
        if (i > 0 && free[i - 1].Start + free[i - 1].Length > start) throw new InvalidOperationException("range freed twice");
        if (i < free.Count && start + length > free[i].Start) throw new InvalidOperationException("range freed twice");
        free.Insert(i, (start, length));
        Used -= length;
        if (i + 1 < free.Count && free[i].Start + free[i].Length == free[i + 1].Start)
        {
            free[i] = (free[i].Start, free[i].Length + free[i + 1].Length);
            free.RemoveAt(i + 1);
        }
        if (i > 0 && free[i - 1].Start + free[i - 1].Length == free[i].Start)
        {
            free[i - 1] = (free[i - 1].Start, free[i - 1].Length + free[i].Length);
            free.RemoveAt(i);
        }
    }

    /// <summary>The longest free range: whether a big cell can still come in without letting another go.</summary>
    public long LargestFree => free.Count == 0 ? 0 : free.Max(f => f.Length);
    public int FreeRanges => free.Count;
}
