// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Vitomir.Formats.L2;

/// <summary>
/// The legacy chunks of an L2 cell (ours): a 1.1 level carried whole, in the
/// `.nil`'s own encoding, so a converted level loads into exactly the model <see cref="NilLevel.Read"/>
/// builds.
///
/// - <c>L1SC</c> (version 1): the NIL's bytes from offset 8 — the global light, the ambient colour,
///   the materials list and every sector (planes, surface planes, collision tree, vertices, sector
///   vertices, groups, surfaces, portals, lights) — laid out as the NIL lays them out.
/// - <c>L1PG</c> (version 1): the path graph, as the NIL stores it.
/// - <c>L1LM</c> (version 1): the lightmap pages, RGB565 with the NIL's zero-run packing.
///
/// Positions in these chunks stay in **engine units** (72 to the metre), as in the `.nil`; only the
/// manifest speaks metres. The encoder writes from the model, not by copying the file, and the
/// decoder rebuilds the NIL stream and reads it with the one NIL reader, so every check that reader
/// makes (mirrored portals, indices in range, the file consumed to the end) is made again.
/// </summary>
public static class L2Legacy
{
    public const string Sectors = "L1SC", PathGraph = "L1PG", Lightmaps = "L1LM";
    public const uint ChunkVersion1 = 1;

    /// <summary>The NIL's magic as every shipped level has it: <c>NIL</c> and 0x10 (the reader checks only the letters).</summary>
    static ReadOnlySpan<byte> Magic => "NIL\x10"u8;

    /// <summary>The three legacy chunks of a level, in the order they are written.</summary>
    public static L2Chunk[] Encode(NilLevel level) =>
    [
        new(Sectors, ChunkVersion1, Write(w => WriteSectors(w, level))),
        new(PathGraph, ChunkVersion1, Write(w => WritePathGraph(w, level.PathNodes))),
        new(Lightmaps, ChunkVersion1, Write(w => WriteLightmaps(w, level.Lightmaps))),
    ];

    /// <summary>The model the legacy chunks of <paramref name="cell"/> hold.</summary>
    public static NilLevel Decode(L2Cell cell, string name = "<l2>")
    {
        var sc = cell.Need(Sectors, name); var pg = cell.Need(PathGraph, name); var lm = cell.Need(Lightmaps, name);
        foreach (var c in new[] { sc, pg, lm })
            if (c.Version != ChunkVersion1) throw new NodFormatException($"{name}: {c.Tag} version {c.Version}, this build reads {ChunkVersion1}");
        var nil = new byte[8 + sc.Payload.Length + pg.Payload.Length + lm.Payload.Length];
        Magic.CopyTo(nil);
        BinaryPrimitives.WriteUInt32LittleEndian(nil.AsSpan(4), NilLevel.Version27);
        sc.Payload.CopyTo(nil, 8);
        pg.Payload.CopyTo(nil, 8 + sc.Payload.Length);
        lm.Payload.CopyTo(nil, 8 + sc.Payload.Length + pg.Payload.Length);
        return NilLevel.Read(nil, name);
    }

    /// <summary>
    /// The whole level as a `.nil` (version 27) written from the model. On the shipped levels this
    /// is the file byte for byte (the Formats tests check all 112), which is what makes the chunks
    /// above a faithful carrier.
    /// </summary>
    public static byte[] WriteNil(NilLevel level) => Write(w =>
    {
        w.Write(Magic);
        w.Write(NilLevel.Version27);
        WriteSectors(w, level);
        WritePathGraph(w, level.PathNodes);
        WriteLightmaps(w, level.Lightmaps);
    });

    static byte[] Write(Action<BinaryWriter> body)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.Latin1, leaveOpen: true)) body(w);
        return ms.ToArray();
    }

    static void Vec(BinaryWriter w, Vector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }

    static void Fixed(BinaryWriter w, string s, int n)
    {
        var b = new byte[n];
        int len = Encoding.Latin1.GetBytes(s, b);
        if (len >= n) throw new ArgumentException($"'{s}' does not fit {n} bytes with its NUL");
        w.Write(b);
    }

    static void Light(BinaryWriter w, NilLight l)
    {
        w.Write(l.RawHead);
        Vec(w, l.Color); w.Write(l.Intensity); w.Write(l.Intensity2);
        Vec(w, l.Direction); Vec(w, l.Position);
        w.Write(l.CosUmbra); w.Write(l.CosPenumbra); w.Write(l.Range); w.Write(l.Falloff);
    }

    static void Plane(BinaryWriter w, NilPlane p) { Vec(w, p.Normal); w.Write(p.Dist); }

    static void WriteSectors(BinaryWriter w, NilLevel level)
    {
        Light(w, level.GlobalLight);
        Vec(w, level.Ambient);
        w.Write(level.Materials.Length);
        foreach (var m in level.Materials) Fixed(w, m, 32);
        w.Write(level.Sectors.Length);
        foreach (var s in level.Sectors)
        {
            w.Write(s.Flags);
            Vec(w, s.Ambient);
            w.Write(checked((byte)s.SectorPlanes.Length));
            foreach (var p in s.SectorPlanes) Plane(w, p);
            w.Write(checked((ushort)s.SurfacePlanes.Length));
            foreach (var p in s.SurfacePlanes) Plane(w, p);
            w.Write(checked((ushort)s.BspCount));
            foreach (var u in s.BspNodes) w.Write(u);
            w.Write(checked((ushort)s.Vertices.Length));
            foreach (var v in s.Vertices) Vec(w, v);
            w.Write(checked((ushort)s.SectorVertices.Length));
            foreach (var sv in s.SectorVertices)
            {
                w.Write(sv.Vertex); w.Write(sv.Pad); w.Write(sv.Rgba);
                w.Write(sv.Uv.X); w.Write(sv.Uv.Y); w.Write(sv.LightmapUv.X); w.Write(sv.LightmapUv.Y);
            }
            w.Write(s.Surfaces.Length);
            w.Write(checked((ushort)s.Groups.Length));
            foreach (var g in s.Groups)
            {
                w.Write(checked((ushort)g.NumSurfaces)); w.Write(g.Material); w.Write(checked((byte)g.Layers.Length));
                w.Write(g.SurfaceFlags); w.Write(checked((ushort)g.NumVerts));
                foreach (var l in g.Layers) { w.Write(l.Page); w.Write(l.Style); }
            }
            foreach (var sf in s.Surfaces) { w.Write(sf.Plane); w.Write(sf.NumVerts); w.Write(sf.PathNode); }
            w.Write(checked((byte)s.Portals.Length));
            foreach (var p in s.Portals)
            {
                w.Write(p.Flags); w.Write(p.PortalId); w.Write(p.NeighbourPortal); w.Write(p.NeighbourSector);
                foreach (var q in p.Quad) Vec(w, q);
                Vec(w, p.Normal);
                w.Write(p.Runtime);
            }
            w.Write(checked((ushort)s.Lights.Length));
            foreach (var l in s.Lights) Light(w, l);
        }
    }

    static void WritePathGraph(BinaryWriter w, NilPathNode[] nodes)
    {
        w.Write(nodes.Length);
        if (nodes.Length == 0) return;
        w.Write((uint)nodes.Sum(n => n.Links.Length));
        w.Write((uint)nodes.Sum(n => n.Planes.Length));
        foreach (var n in nodes)
        {
            w.Write(checked((byte)n.Links.Length));
            foreach (var k in n.Links) { w.Write(k.Flags); w.Write(k.Target); w.Write(k.Length); Vec(w, k.Direction); Vec(w, k.Origin); }
            w.Write(checked((byte)n.Planes.Length));
            foreach (var p in n.Planes) Vec(w, p);
        }
    }

    /// <summary>Each zero pixel is followed by a u16 count of the further zeros after it (at most 65,535).</summary>
    static void WriteLightmaps(BinaryWriter w, NilLightmap[] pages)
    {
        w.Write(pages.Length);
        foreach (var p in pages)
        {
            w.Write(checked((ushort)p.Width)); w.Write(checked((ushort)p.Height));
            var px = p.Pixels;
            for (int i = 0; i < px.Length;)
            {
                ushort v = px[i++];
                w.Write(v);
                if (v != 0) continue;
                int run = 0;
                while (i < px.Length && px[i] == 0 && run < ushort.MaxValue) { run++; i++; }
                w.Write((ushort)run);
            }
        }
    }
}
