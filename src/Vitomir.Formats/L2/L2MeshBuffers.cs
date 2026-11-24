// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Vitomir.Formats.L2;

/// <summary>
/// One vertex of a <c>MESH</c> version 2 buffer (ours): the modern renderer's own interleaved layout,
/// 60 bytes — position (metres, Z up), normal, tangent (xyz along u, w the bitangent's side), two
/// texture coordinates and a linear RGBA8 colour, R in the lowest byte — so a loader copies a cell's
/// bytes to the GPU as they lie, after scaling the positions to engine units.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct L2Vertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector4 Tangent;
    public Vector2 Uv0;
    public Vector2 Uv1;
    public uint Color;

    /// <summary>Bytes per vertex.</summary>
    public const int Size = 60;
}

/// <summary>
/// One draw of a level of detail: a material (an index into the manifest's <c>materials</c>), its
/// indices' range in the level's index buffer, its box in metres, and its texture density — how many
/// texture repeats (UV units) a metre of its surface holds, the median over its triangles — from which
/// the texture streamer works out the mip a draw needs at a distance.
/// </summary>
public readonly record struct L2MeshDraw(int Material, int FirstIndex, int IndexCount, Vector3 Min, Vector3 Max, float UvDensity);

/// <summary>
/// One level of detail of a cell's mesh: its vertices and indices (u32, relative to its own vertices)
/// and its draws. <see cref="Error"/> is how far (metres) the simplifier let the surface move from the
/// full mesh; 0 for the full mesh.
/// </summary>
public sealed class L2MeshLod
{
    public float Error { get; init; }
    public L2Vertex[] Vertices { get; init; } = [];
    public uint[] Indices { get; init; } = [];
    public List<L2MeshDraw> Draws { get; init; } = [];
    public int Triangles => Indices.Length / 3;
    /// <summary>The bytes this level takes on the GPU.</summary>
    public long Bytes => (long)Vertices.Length * L2Vertex.Size + Indices.Length * 4L;
}

/// <summary>
/// <c>MESH</c> version 2 (ours, Phase 3): a cell's mesh as GPU-ready buffers — one or more levels of
/// detail, each interleaved vertices (<see cref="L2Vertex"/>), u32 indices and per-material draws. A
/// block's far version is a cell file holding one of these with a single level. Version 1 (a reference
/// to the package's glTF asset, <see cref="L2Mesh"/>) is still read.
///
/// Bytes, little-endian: u32 vertex stride (60); u32 levels; per level: f32 error (metres), u32
/// vertices, u32 indices, u32 draws, then each draw as u16 material, u16 0, u32 first index, u32 index
/// count, its box (six f32, metres) and f32 texture density; then the vertices (stride bytes each) and
/// the indices (u32 each).
/// </summary>
public sealed class L2MeshBuffers
{
    public const string Tag = L2Mesh.Tag;
    public const uint Version2 = 2;
    const int DrawBytes = 2 + 2 + 4 + 4 + 24 + 4;

    public List<L2MeshLod> Lods { get; init; } = [];

    public L2Chunk Encode()
    {
        long size = 8;
        foreach (var l in Lods) size += 16 + l.Draws.Count * DrawBytes + (long)l.Vertices.Length * L2Vertex.Size + l.Indices.Length * 4L;
        var bytes = new byte[checked((int)size)];
        int at = 0;
        void U32(uint v) { BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), v); at += 4; }
        void F32(float v) { BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at), v); at += 4; }
        U32(L2Vertex.Size); U32((uint)Lods.Count);
        foreach (var l in Lods)
        {
            F32(l.Error); U32((uint)l.Vertices.Length); U32((uint)l.Indices.Length); U32((uint)l.Draws.Count);
            foreach (var d in l.Draws)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), checked((ushort)d.Material)); at += 2;
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), 0); at += 2;
                U32((uint)d.FirstIndex); U32((uint)d.IndexCount);
                F32(d.Min.X); F32(d.Min.Y); F32(d.Min.Z); F32(d.Max.X); F32(d.Max.Y); F32(d.Max.Z);
                F32(d.UvDensity);
            }
            var v = MemoryMarshal.AsBytes(l.Vertices.AsSpan());
            v.CopyTo(bytes.AsSpan(at)); at += v.Length;
            var ix = MemoryMarshal.AsBytes(l.Indices.AsSpan());
            ix.CopyTo(bytes.AsSpan(at)); at += ix.Length;
        }
        if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("MESH buffers are written little-endian as they lie");
        return new L2Chunk(Tag, Version2, bytes);
    }

    /// <summary>Reads a version 2 chunk; refuses another version, a stride that is not 60, or an index or draw out of range.</summary>
    public static L2MeshBuffers Decode(L2Chunk chunk)
    {
        if (chunk.Version != Version2) throw new NodFormatException($"{Tag}: version {chunk.Version} is not GPU buffers (2)");
        var data = chunk.Payload;
        int at = 0;
        NodFormatException Error(string what) => new($"{Tag}: {what} at 0x{at:x}");
        uint U32() { if (data.Length - at < 4) throw Error("truncated"); uint v = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at)); at += 4; return v; }
        float F32() => BitConverter.UInt32BitsToSingle(U32());
        if (U32() != L2Vertex.Size) throw Error("vertex stride is not 60");
        uint levels = U32();
        if (levels > 16) throw Error($"{levels} levels of detail");
        var lods = new List<L2MeshLod>((int)levels);
        for (int l = 0; l < levels; l++)
        {
            float error = F32();
            uint nv = U32(), ni = U32(), nd = U32();
            if (nd > 1_000_000 || (long)nv * L2Vertex.Size + ni * 4L > data.Length - at) throw Error("level runs past the end");
            var draws = new List<L2MeshDraw>((int)nd);
            for (int d = 0; d < nd; d++)
            {
                if (data.Length - at < DrawBytes) throw Error("truncated draw");
                int material = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at)); at += 4;
                uint first = U32(), count = U32();
                var min = new Vector3(F32(), F32(), F32()); var max = new Vector3(F32(), F32(), F32());
                float density = F32();
                if ((ulong)first + count > ni) throw Error($"draw {d} outside the level's indices");
                draws.Add(new L2MeshDraw(material, (int)first, (int)count, min, max, density));
            }
            if ((long)nv * L2Vertex.Size + ni * 4L > data.Length - at) throw Error("level runs past the end");
            var vertices = MemoryMarshal.Cast<byte, L2Vertex>(data.AsSpan(at, (int)(nv * L2Vertex.Size))).ToArray();
            at += (int)(nv * L2Vertex.Size);
            var indices = MemoryMarshal.Cast<byte, uint>(data.AsSpan(at, (int)(ni * 4))).ToArray();
            at += (int)(ni * 4);
            foreach (var i in indices) if (i >= nv) throw Error($"index {i} of {nv} vertices");
            lods.Add(new L2MeshLod { Error = error, Vertices = vertices, Indices = indices, Draws = draws });
        }
        if (at != data.Length) throw Error($"{data.Length - at} bytes left over");
        return new L2MeshBuffers { Lods = lods };
    }
}
