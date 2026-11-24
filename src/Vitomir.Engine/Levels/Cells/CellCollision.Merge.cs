// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Levels.Cells;

public sealed partial class CellCollision
{
    CellCollision(Vector3[] vertices, int[] indices, L2Surface[] surfaces, ushort[] materials, L2BvhNode[] nodes)
    {
        Vertices = vertices; Indices = indices; Surfaces = surfaces; Materials = materials; Nodes = nodes;
    }

    /// <summary>An empty space: nothing to stand on or meet.</summary>
    public static CellCollision Empty { get; } = new([], [], [], [], []);

    /// <summary>
    /// **Ours (Phase 3): the loaded cells' collision as one**, for a streamed level. The cells' triangles
    /// and trees are laid end to end (their indices and nodes moved along), and a small tree is built over
    /// the cells' own root boxes — split at the median of the longest axis of their middles, as the cook's
    /// tree is — with each cell's whole tree hung under it where a leaf would be. So every question this
    /// class answers (<see cref="Ray"/>, <see cref="Floor"/>, <see cref="Open"/>) asks every loaded cell
    /// whose box it meets, and a point by a cell's border finds the neighbour's triangles as readily as its
    /// own: there is no seam. A triangle's number in the result is its cell's first plus its own.
    /// </summary>
    public static CellCollision Merge(IReadOnlyList<CellCollision> parts)
    {
        var live = parts.Where(p => p.Nodes.Length > 0).ToList();
        if (live.Count == 0) return Empty;
        if (live.Count == 1) return live[0];
        int vertices = live.Sum(p => p.Vertices.Length), triangles = live.Sum(p => p.Triangles);
        var v = new Vector3[vertices];
        var idx = new int[triangles * 3];
        var surf = new L2Surface[triangles];
        var mats = new ushort[triangles];
        var firstTri = new int[live.Count];
        int vAt = 0, tAt = 0;
        for (int i = 0; i < live.Count; i++)
        {
            var p = live[i];
            p.Vertices.CopyTo(v, vAt);
            for (int k = 0; k < p.Indices.Length; k++) idx[tAt * 3 + k] = p.Indices[k] + vAt;
            p.Surfaces.CopyTo(surf, tAt);
            p.Materials.CopyTo(mats, tAt);
            firstTri[i] = tAt;
            vAt += p.Vertices.Length; tAt += p.Triangles;
        }
        var nodes = new List<L2BvhNode>(live.Sum(p => p.Nodes.Length) + 2 * live.Count);
        var order = Enumerable.Range(0, live.Count).ToArray();
        void Build(int from, int to)
        {
            if (to - from == 1)
            {
                // The cell's whole tree, its leaves' triangles and inner nodes' second children moved along.
                int i = order[from], nodeBase = nodes.Count;
                foreach (var n in live[i].Nodes)
                    nodes.Add(n.IsLeaf ? n with { First = n.First + firstTri[i] } : n with { First = n.Right + nodeBase });
                return;
            }
            var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
            var cmin = min; var cmax = max;
            for (int k = from; k < to; k++)
            {
                var root = live[order[k]].Nodes[0];
                min = Vector3.Min(min, root.Min); max = Vector3.Max(max, root.Max);
                var c = (root.Min + root.Max) * 0.5f;
                cmin = Vector3.Min(cmin, c); cmax = Vector3.Max(cmax, c);
            }
            var extent = cmax - cmin;
            int axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
            Array.Sort(order, from, to - from, Comparer<int>.Create((a, b) =>
                Axis((live[a].Nodes[0].Min + live[a].Nodes[0].Max) * 0.5f, axis).CompareTo(Axis((live[b].Nodes[0].Min + live[b].Nodes[0].Max) * 0.5f, axis))));
            int at = nodes.Count;
            nodes.Add(default);
            int mid = (from + to) / 2;
            Build(from, mid);
            int right = nodes.Count;
            Build(mid, to);
            nodes[at] = new L2BvhNode(min, max, right, 0);
        }
        Build(0, live.Count);
        return new CellCollision(v, idx, surf, mats, [.. nodes]);
    }
}
