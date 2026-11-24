// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The chunks a new level's cells are made of. All of this is
// ours: 1.1 has no such thing. Lengths are metres, Z up, as the manifest; the loader scales by 72.
using System.Numerics;
using System.Text;

namespace Vitomir.Formats.L2;

/// <summary>What a collision triangle is to someone standing on it or walking into it (the <c>COLL</c> chunk's flag byte).</summary>
[Flags]
public enum L2Surface : byte
{
    /// <summary>Nothing: seen, never touched (no triangle is written with no flags today).</summary>
    None = 0,
    /// <summary>Stops a body and a ray: walls, roofs, fences, the ground itself.</summary>
    Solid = 1,
    /// <summary>Can be stood on: faces up (normal Z at least 0.7) and its material is ground.</summary>
    Walk = 2,
    /// <summary>
    /// Not to be climbed (ours): a lip on this triangle is never taken by a
    /// grab, mantle, climb onto, hop up or vault; walking, stepping and standing on it are as without it. The cook
    /// sets it on the triangles of the meshes a layout names (<c>traversal.noClimbMeshes</c>). An addition to
    /// <c>COLL</c> 1: every reader tests the flags by mask, so one that does not know it passes it by.
    /// </summary>
    NoClimb = 4,
    /// <summary>
    /// Not to be stood on (ours): no body stands on this triangle — walking
    /// meets it as a slope too steep, a body on it slides off, no move ends on it, and the navigation mesh leaves it
    /// out. The cook sets it on the triangles of the meshes a layout names (<c>traversal.noStandMeshes</c>) and on those
    /// wholly inside a layout's area marked <c>"stand": "none"</c>, and never sets <see cref="Walk"/> beside it. An
    /// addition to <c>COLL</c> 1, as <see cref="NoClimb"/> is: a reader that does not know it passes it by.
    /// </summary>
    NoStand = 8,
}

/// <summary>
/// One node of the <c>COLL</c> chunk's bounding-volume tree, flattened depth first: an inner node's
/// first child is the next node and <see cref="Right"/> is the second; a leaf holds
/// <see cref="Count"/> triangles from <see cref="First"/>, the triangles being stored in the tree's order.
/// </summary>
public readonly record struct L2BvhNode(Vector3 Min, Vector3 Max, int First, int Count)
{
    public bool IsLeaf => Count > 0;
    /// <summary>An inner node's second child.</summary>
    public int Right => First;
}

/// <summary>
/// <c>COLL</c> version 1: the cell's collision triangles, a flag and a material per triangle, and the
/// bounding-volume tree over them. Vertices are shared; triangles are stored in the tree's leaf order.
/// </summary>
public sealed class L2Collision
{
    public const string Tag = "COLL";
    public const uint Version1 = 1;

    public Vector3[] Vertices { get; init; } = [];
    /// <summary>Three vertex indices per triangle.</summary>
    public int[] Indices { get; init; } = [];
    public L2Surface[] Surfaces { get; init; } = [];
    /// <summary>Each triangle's material, an index into the manifest's <c>materials</c>.</summary>
    public ushort[] Materials { get; init; } = [];
    public L2BvhNode[] Nodes { get; init; } = [];

    public int Triangles => Indices.Length / 3;

    public L2Chunk Encode()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Vertices.Length);
            foreach (var v in Vertices) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
            w.Write(Triangles);
            foreach (int i in Indices) w.Write(i);
            foreach (var s in Surfaces) w.Write((byte)s);
            foreach (var m in Materials) w.Write(m);
            w.Write(Nodes.Length);
            foreach (var n in Nodes)
            {
                w.Write(n.Min.X); w.Write(n.Min.Y); w.Write(n.Min.Z);
                w.Write(n.Max.X); w.Write(n.Max.Y); w.Write(n.Max.Z);
                w.Write(n.First); w.Write(n.Count);
            }
        }
        return new L2Chunk(Tag, Version1, ms.ToArray());
    }

    public static L2Collision Decode(L2Chunk chunk)
    {
        if (chunk.Version != Version1) throw new NodFormatException($"{Tag}: version {chunk.Version} unknown");
        var r = new BinReader(chunk.Payload, Tag);
        var vertices = new Vector3[r.Count32()];
        for (int i = 0; i < vertices.Length; i++) vertices[i] = r.Vec3();
        int triangles = r.Count32();
        var indices = new int[triangles * 3];
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = r.I32();
            if ((uint)indices[i] >= (uint)vertices.Length) throw r.Error($"vertex {indices[i]} of {vertices.Length}");
        }
        var surfaces = r.Bytes(triangles).Select(b => (L2Surface)b).ToArray();
        var materials = r.U16s(triangles);
        var nodes = new L2BvhNode[r.Count32()];
        for (int i = 0; i < nodes.Length; i++)
        {
            nodes[i] = new L2BvhNode(r.Vec3(), r.Vec3(), r.I32(), r.I32());
            var n = nodes[i];
            bool ok = n.IsLeaf ? n.First >= 0 && n.First + n.Count <= triangles : n.Right > i && n.Right < nodes.Length;
            if (!ok) throw r.Error($"tree node {i} out of range");
        }
        r.ExpectEof();
        return new L2Collision { Vertices = vertices, Indices = indices, Surfaces = surfaces, Materials = materials, Nodes = nodes };
    }
}

/// <summary>One dynamic light placed in a cell (the <c>LITE</c> chunk): a point light for now.</summary>
/// <param name="Colour">Linear RGB, 0..1.</param>
/// <param name="Intensity">Relative to the sun's 6: a torch is about 2.</param>
/// <param name="Range">Metres at which it has faded out.</param>
public sealed record L2Light(string Name, int Kind, Vector3 Position, Vector3 Colour, float Intensity, float Range)
{
    public const int Point = 0;
}

/// <summary><c>LITE</c> version 1: the cell's dynamic lights.</summary>
public static class L2LightChunk
{
    public const string Tag = "LITE";
    public const uint Version1 = 1;

    public static L2Chunk Encode(IReadOnlyList<L2Light> lights)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(lights.Count);
            foreach (var l in lights)
            {
                L2Strings.Write(w, l.Name);
                w.Write(l.Kind);
                foreach (var v in new[] { l.Position, l.Colour }) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
                w.Write(l.Intensity); w.Write(l.Range);
            }
        }
        return new L2Chunk(Tag, Version1, ms.ToArray());
    }

    public static List<L2Light> Decode(L2Chunk chunk)
    {
        if (chunk.Version != Version1) throw new NodFormatException($"{Tag}: version {chunk.Version} unknown");
        var r = new BinReader(chunk.Payload, Tag);
        var list = new List<L2Light>();
        int n = r.Count32(1_000_000);
        for (int i = 0; i < n; i++)
            list.Add(new L2Light(L2Strings.Read(r), r.I32(), r.Vec3(), r.Vec3(), r.F32(), r.F32()));
        r.ExpectEof();
        return list;
    }
}

/// <summary>One draw of a cell's mesh: a glTF primitive's material and size, and its box in metres.</summary>
public sealed record L2Draw(string Material, int Triangles, Vector3 Min, Vector3 Max);

/// <summary>
/// <c>MESH</c> version 1 — <b>a reference, not buffers yet</b>: the package's glTF asset the cell is
/// drawn from (a path inside the package, <c>assets/…glb</c>) and its draws in the asset's own
/// order. The renderer reads the asset itself. GPU-ready buffers and levels of detail are a later
/// version of this chunk; a reader refuses a version it does not know, so the change is safe.
/// </summary>
public sealed class L2Mesh
{
    public const string Tag = "MESH";
    public const uint Version1 = 1;

    public string Asset { get; init; } = "";
    public List<L2Draw> Draws { get; init; } = [];

    public L2Chunk Encode()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            L2Strings.Write(w, Asset);
            w.Write(Draws.Count);
            foreach (var d in Draws)
            {
                L2Strings.Write(w, d.Material);
                w.Write(d.Triangles);
                foreach (var v in new[] { d.Min, d.Max }) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
            }
        }
        return new L2Chunk(Tag, Version1, ms.ToArray());
    }

    public static L2Mesh Decode(L2Chunk chunk)
    {
        if (chunk.Version != Version1) throw new NodFormatException($"{Tag}: version {chunk.Version} unknown");
        var r = new BinReader(chunk.Payload, Tag);
        var asset = L2Strings.Read(r);
        var draws = new List<L2Draw>();
        int n = r.Count32(10_000_000);
        for (int i = 0; i < n; i++) draws.Add(new L2Draw(L2Strings.Read(r), r.I32(), r.Vec3(), r.Vec3()));
        r.ExpectEof();
        return new L2Mesh { Asset = asset, Draws = draws };
    }
}

/// <summary>
/// <c>NAVM</c> version 1: the cell's navigation mesh as one Detour tile (DotRecast's own
/// <c>DtMeshDataWriter</c> bytes, little-endian), with the agent it was built for. The tile's
/// coordinates are Recast's: metres, <b>Y up</b> (x, z, −y of the manifest's frame).
/// </summary>
public sealed class L2NavMesh
{
    public const string Tag = "NAVM";
    public const uint Version1 = 1;

    public float AgentRadius { get; init; }
    public float AgentHeight { get; init; }
    public float AgentClimb { get; init; }
    public float MaxSlope { get; init; }
    public float CellSize { get; init; }
    public float CellHeight { get; init; }
    public byte[] Tile { get; init; } = [];

    public L2Chunk Encode()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            foreach (var f in new[] { AgentRadius, AgentHeight, AgentClimb, MaxSlope, CellSize, CellHeight }) w.Write(f);
            w.Write(Tile.Length); w.Write(Tile);
        }
        return new L2Chunk(Tag, Version1, ms.ToArray());
    }

    public static L2NavMesh Decode(L2Chunk chunk)
    {
        if (chunk.Version != Version1) throw new NodFormatException($"{Tag}: version {chunk.Version} unknown");
        var r = new BinReader(chunk.Payload, Tag);
        var f = r.Floats(6);
        var tile = r.Bytes(r.Count32(1 << 30));
        r.ExpectEof();
        return new L2NavMesh
        {
            AgentRadius = f[0], AgentHeight = f[1], AgentClimb = f[2], MaxSlope = f[3], CellSize = f[4], CellHeight = f[5], Tile = tile,
        };
    }
}

/// <summary>The cell chunks' strings: a u16 length and UTF-8, as <c>ANCH</c>'s.</summary>
static class L2Strings
{
    public static void Write(BinaryWriter w, string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        w.Write(checked((ushort)b.Length)); w.Write(b);
    }

    public static string Read(BinReader r) => Encoding.UTF8.GetString(r.Bytes(r.U16()));
}
