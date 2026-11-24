// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Formats;

/// <summary>One bone of a NOD skeleton.</summary>
public sealed record NodBone(Vector3 RestTranslate, float[] RestMatrixInverse, short Sibling, short Child, short Parent);

/// <summary>A model vertex (40 B on disk). Bound to one bone; the parent bone takes the rest of the weight.</summary>
public readonly record struct NodVertex(Vector3 Position, Vector3 Normal, Vector2 Uv, float Weight, byte Bone);

/// <summary>A mesh group: one material batch of consecutive faces and vertices (28 B on disk).</summary>
public sealed record NodMeshGroup(int Material, byte[] Reserved, int NumFaces, int NumVertices, int MinVertices,
                                  int Flags, byte Bone, byte Mesh)
{
    /// <summary>Offset of this group's first vertex in the model's vertex array (derived).</summary>
    public int FirstVertex { get; internal set; }
    /// <summary>Offset of this group's first face in the model's face list (derived).</summary>
    public int FirstFace { get; internal set; }

    /// <summary>Reads the 28-byte group record shared by NOD models and NSD brush models.</summary>
    internal static NodMeshGroup Read(BinReader r)
    {
        int mat = r.I32(); var res = r.Bytes(12);
        int nf = r.U16(), nv = r.U16(), mv = r.U16(), gf = r.U16();
        byte bone = r.U8(), mesh = r.U8();
        r.Skip(2);                     // struct tail padding (the SDK doc omits it)
        // Only bits 0-1 (HASLOD, NOWEIGHTS) mean anything; bits 2-15 are MSVC uninitialised fill.
        return new NodMeshGroup(mat, res, nf, nv, mv, gf & 0x3, bone, mesh);
    }
}

/// <summary>
/// NOD model, version 7 (spec: NodSDK/Docs/nod.htm, with the C struct padding the doc omits).
/// Layout: header, materials, bones, mesh names, vertices, [LOD collapse table], faces, groups.
/// </summary>
public sealed class NodModel
{
    public const uint Version7 = 7;
    public const uint FlagHasLod = 0x1, FlagInline = 0x2, FlagStatic = 0x4;
    public const int GroupHasLod = 0x1, GroupNoWeights = 0x2;

    public uint Version { get; init; }
    public string[] Materials { get; init; } = [];
    public NodBone[] Bones { get; init; } = [];
    public string[] MeshNames { get; init; } = [];
    public uint Flags { get; init; }
    public Vector3 BoundsMin { get; init; }
    public Vector3 BoundsMax { get; init; }
    public NodVertex[] Vertices { get; init; } = [];
    /// <summary>Per-vertex LOD collapse target (only when <see cref="FlagHasLod"/>).</summary>
    public ushort[] LodCollapse { get; init; } = [];
    /// <summary>Triangle vertex indices, 3 per face, relative to the owning group's first vertex.</summary>
    public ushort[] Indices { get; init; } = [];
    public NodMeshGroup[] Groups { get; init; } = [];

    public int NumFaces => Indices.Length / 3;

    /// <summary>
    /// Just the model's bounding box, without reading the rest of it. This is what a thing's
    /// THING_COLLIDE_BBOX shape is: the engine copies these six floats straight out of the loaded
    /// model into the thing, and its own loader reads them as one 0x18-byte
    /// block immediately after the header.
    /// </summary>
    public static (Vector3 Min, Vector3 Max) ReadBounds(byte[] data, string name = "<nod>")
    {
        var r = new BinReader(data, name);
        uint version = r.U32();
        if (version != Version7) throw r.Error($"unexpected NOD version {version}");
        int numMaterials = r.Count32(4096);
        r.Skip(numMaterials * 32);
        r.Skip(2 + 2 + 4 + 4 + 2 + 4);      // bones, meshes, vertices, faces, groups, flags
        return (r.Vec3(), r.Vec3());
    }

    public static NodModel Read(byte[] data, string name = "<nod>")
    {
        var r = new BinReader(data, name);
        uint version = r.U32();
        if (version != Version7) throw r.Error($"unexpected NOD version {version}");
        int numMaterials = r.Count32(4096);
        var materials = new string[numMaterials];
        for (int i = 0; i < numMaterials; i++) materials[i] = r.CStr(32);
        int numBones = r.U16(), numMeshes = r.U16();
        int numVertices = r.Count32(), numFaces = r.Count32();
        int numGroups = r.U16();
        uint flags = r.U32();
        var bmin = r.Vec3(); var bmax = r.Vec3();
        var bones = new NodBone[numBones];
        for (int i = 0; i < numBones; i++)
        {
            var t = r.Vec3(); var m = r.Floats(12);
            bones[i] = new NodBone(t, m, r.I16(), r.I16(), r.I16());
        }
        var meshNames = new string[numMeshes];
        for (int i = 0; i < numMeshes; i++) meshNames[i] = r.CStr(32);
        var verts = ReadVertices(r, numVertices);
        var lod = (flags & FlagHasLod) != 0 ? r.U16s(numVertices) : [];
        var indices = r.U16s(numFaces * 3);
        var groups = new NodMeshGroup[numGroups];
        for (int i = 0; i < numGroups; i++) groups[i] = NodMeshGroup.Read(r);
        r.ExpectEof();
        AssignOffsets(r, groups, numVertices, numFaces);
        return new NodModel
        {
            Version = version, Materials = materials, Bones = bones, MeshNames = meshNames, Flags = flags,
            BoundsMin = bmin, BoundsMax = bmax, Vertices = verts, LodCollapse = lod, Indices = indices, Groups = groups,
        };
    }

    /// <summary>Reads NOD-layout vertices (shared with NSD brush models).</summary>
    internal static NodVertex[] ReadVertices(BinReader r, int n)
    {
        var verts = new NodVertex[n];
        for (int i = 0; i < n; i++)
        {
            var p = r.Vec3(); var nrm = r.Vec3(); var uv = r.Vec2(); float w = r.F32(); byte b = r.U8();
            r.Skip(3);                 // the bone byte is padded to 4 (struct alignment); the SDK doc omits it
            verts[i] = new NodVertex(p, nrm, uv, w, b);
        }
        return verts;
    }

    /// <summary>Derives each group's offsets and checks the totals against the header.</summary>
    internal static void AssignOffsets(BinReader r, NodMeshGroup[] groups, int numVertices, int numFaces)
    {
        int v0 = 0, f0 = 0;
        foreach (var g in groups)
        {
            g.FirstVertex = v0; g.FirstFace = f0;
            v0 += g.NumVertices; f0 += g.NumFaces;
        }
        if (v0 != numVertices || f0 != numFaces)
            throw r.Error($"group totals ({v0} verts, {f0} faces) != header ({numVertices}, {numFaces})");
    }
}
