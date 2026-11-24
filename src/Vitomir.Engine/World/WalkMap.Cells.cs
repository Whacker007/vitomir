// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Engine.World;

public sealed partial class WalkMap
{
    /// <summary>
    /// **Ours (Phase 3): a streamed cell level's ground changing under the map.** A cell level's walk
    /// nodes are its navigation-mesh polygons, each with a fixed slot (its cell's first slot and its
    /// number in the cell's tile; <c>Levels.Cells.CellStream</c>); when a cell comes in, its polygons
    /// are put in their slots, and when it goes, they are emptied. The slots in
    /// <paramref name="emptied"/> become gaps, the nodes in <paramref name="placed"/> go into theirs
    /// (<see cref="WalkNode.Index"/>), and then the walls are worked out again for every placed node and
    /// every node in <paramref name="recheck"/> — the pieces of ground along the changed cells' borders,
    /// whose edges there were walls with nothing beyond and are now crossings, or the other way round.
    /// Called at the top of a frame (the simulation stays single-threaded).
    /// </summary>
    public void Restream(IReadOnlyList<int> emptied, IReadOnlyList<WalkNode> placed, IReadOnlyList<int> recheck)
    {
        foreach (int slot in emptied)
        {
            if (slot < 0 || slot >= Nodes.Length) continue;
            Ungrid(slot);
            Nodes[slot] = Empty;
        }
        foreach (var node in placed)
        {
            if (node.Index < 0 || node.Index >= Nodes.Length) continue;
            Ungrid(node.Index);
            Nodes[node.Index] = node;
            if (node.Outline.Length == 0) continue;
            var (lo, hi) = (Cell(node.Min), Cell(node.Max));
            for (int x = lo.X; x <= hi.X; x++)
                for (int y = lo.Y; y <= hi.Y; y++)
                {
                    if (!grid.TryGetValue((x, y), out var list)) grid[(x, y)] = list = [];
                    list.Add(node.Index);
                }
        }
        foreach (var node in placed) if (node.Outline.Length > 0) node.Walls = FindWalls(node);
        foreach (int slot in recheck) if (IsGround(slot)) Nodes[slot].Walls = FindWalls(Nodes[slot]);
    }

    void Ungrid(int slot)
    {
        var old = Nodes[slot];
        if (old.Outline.Length == 0) return;
        var (lo, hi) = (Cell(old.Min), Cell(old.Max));
        for (int x = lo.X; x <= hi.X; x++)
            for (int y = lo.Y; y <= hi.Y; y++)
                if (grid.TryGetValue((x, y), out var list))
                {
                    list.Remove(slot);
                    if (list.Count == 0) grid.Remove((x, y));
                }
    }
}
