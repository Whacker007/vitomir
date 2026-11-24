// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Formats;

public readonly record struct NilPlane(Vector3 Normal, float Dist);

/// <summary>
/// One node of a sector's collision tree (8 B). Decoded in Session 2 from the shipped data: every
/// one of the game's 7,567 sector trees has exactly one more leaf than it has internal nodes, and
/// every internal node's plane index is in range — 744,098 of 744,098.
///
/// <see cref="Kind"/> 0 is an internal node, and then <see cref="Plane"/> indexes the sector's
/// surface planes and <see cref="Front"/>/<see cref="Back"/> are the children (always later in the
/// array than the node itself, so the tree is stored in pre-order and cannot loop). Any other
/// value is a leaf, and its bits say what the space there is: bit 0 marks the leaf, bit 1 marks it
/// solid. A leaf's other three fields are uninitialised memory — the writer never clears them —
/// exactly as in the portal record.
///
/// Leaf kinds across the game: 1 (open) 398,474, 3 (solid) 340,857, 7 and 11 (solid, with a bit we
/// have not named) 12,334. Sampled 48 units above every walkable surface in the game, 99.3% of
/// points land in an open leaf; sampled 48 below, 91.8% land in a solid one.
/// </summary>
public readonly record struct NilBspNode(ushort Plane, ushort Kind, ushort Front, ushort Back)
{
    /// <summary>Kind 0: not a leaf at all.</summary>
    public const ushort Internal = 0;
    /// <summary>Bit 0 of a leaf's kind; bit 1 is set when the space there is solid.</summary>
    public const ushort LeafBit = 0x1, SolidBit = 0x2;

    public bool IsLeaf => Kind != Internal;
    public bool IsSolid => (Kind & SolidBit) != 0;
}

/// <summary>A sector vertex (24 B): a position index plus per-use attributes.</summary>
/// <param name="Vertex">Index into <see cref="NilSector.Vertices"/>.</param>
/// <param name="Pad">The writer's uninitialised padding; ignore.</param>
/// <param name="Rgba">Baked vertex light: the 4 bytes as stored (byte 0 in the low 8 bits); channel order still to be confirmed.</param>
/// <param name="Uv">Texture coordinates.</param>
/// <param name="LightmapUv">Lightmap page coordinates (0..1).</param>
public readonly record struct NilSectorVertex(ushort Vertex, ushort Pad, uint Rgba, Vector2 Uv, Vector2 LightmapUv);

/// <param name="Page">Index into <see cref="NilLevel.Lightmaps"/>.</param>
/// <param name="Style">The light style this layer belongs to (0 = static light; others animate).</param>
public readonly record struct NilLightmapLayer(uint Page, ushort Style);

/// <summary>One material batch of consecutive surfaces, with its flag bits.</summary>
public sealed record NilGroup(int NumSurfaces, int Material, uint SurfaceFlags, int NumVerts, NilLightmapLayer[] Layers,
                              int FirstSurface, int FirstVertex)
{
    public const uint FlagDecalAux = 0x2, FlagDetail = 0x4, FlagNodraw = 0x8, FlagLight = 0x100, FlagSky = 0x200,
                      FlagVertexLit = 0x800, FlagWalk = 0x1000, FlagTrans33 = 0x2000, FlagTrans66 = 0x4000,
                      FlagReflect = 0x8000, FlagNoLightBlock = 0x1000000, FlagNoLightmap = 0x2000000,
                      FlagDecal = 0x4000000, FlagTwoSided = 0x8000000;
}

/// <param name="Plane">Index into <see cref="NilSector.SurfacePlanes"/>.</param>
/// <param name="PathNode">Index into <see cref="NilLevel.PathNodes"/>; −1 = not walkable.</param>
/// <param name="FirstVertex">Index of the surface's first sector vertex (derived).</param>
/// <param name="Group">Index of the owning group (derived).</param>
public readonly record struct NilSurface(ushort Plane, byte NumVerts, int PathNode, int FirstVertex, int Group);

/// <summary>A portal to a neighbouring sector (96 B). Portals come in mirrored pairs with ids 2k/2k+1.</summary>
/// <param name="Flags">0 on disk; runtime state (bit 0 = closed, e.g. by a door).</param>
/// <param name="Runtime">20 bytes the engine overwrites at load (stale memory on disk).</param>
public sealed record NilPortal(uint Flags, uint PortalId, uint NeighbourPortal, uint NeighbourSector,
                               Vector3[] Quad, Vector3 Normal, byte[] Runtime);

/// <summary>A light record (72 B), used for the level's global light and for sector lights.</summary>
/// <param name="Flags">Head byte 0: bit 1 set on box lights; bit 0's meaning is open.</param>
/// <param name="InSector">The light's origin lies inside this sector.</param>
/// <param name="Type">0 point, 1 spot, 3 box/parallel.</param>
/// <param name="Style">The <c>.map</c> STYLE (0 = steady).</param>
/// <param name="Color">0..255 per channel.</param>
/// <param name="Intensity">The <c>.map</c> LIGHT (box lights: range squared).</param>
/// <param name="Range">LIGHT / FALLOFF.</param>
public sealed record NilLight(byte Flags, bool InSector, uint Type, uint Style, Vector3 Color, float Intensity, float Intensity2,
                              Vector3 Direction, Vector3 Position, float CosUmbra, float CosPenumbra, float Range, float Falloff,
                              byte[] RawHead)
{
    public const uint Point = 0, Spot = 1, Box = 3;

    internal static NilLight Read(BinReader r)
    {
        var head = r.Bytes(12);
        uint type = BitConverter.ToUInt32(head, 4), style = BitConverter.ToUInt32(head, 8);
        return new NilLight(head[0], head[3] != 0, type, style, r.Vec3(), r.F32(), r.F32(), r.Vec3(), r.Vec3(),
                            r.F32(), r.F32(), r.F32(), r.F32(), head);
    }
}

public sealed class NilSector
{
    public uint Flags { get; init; }
    public Vector3 Ambient { get; init; }
    /// <summary>Boundary planes; normals point inward.</summary>
    public NilPlane[] SectorPlanes { get; init; } = [];
    public NilPlane[] SurfacePlanes { get; init; } = [];
    /// <summary>
    /// The sector's collision tree, 4 u16 per node, in the file's own order. Read it through
    /// <see cref="Bsp"/>; <see cref="NilBspNode"/> says what the fields mean.
    /// </summary>
    public ushort[] BspNodes { get; init; } = [];

    /// <summary>How many nodes the collision tree has. Node 0 is the root.</summary>
    public int BspCount => BspNodes.Length / 4;

    /// <summary>One node of the collision tree.</summary>
    public NilBspNode Bsp(int i) =>
        new(BspNodes[i * 4], BspNodes[i * 4 + 1], BspNodes[i * 4 + 2], BspNodes[i * 4 + 3]);
    public Vector3[] Vertices { get; init; } = [];
    public NilSectorVertex[] SectorVertices { get; init; } = [];
    public NilGroup[] Groups { get; init; } = [];
    public NilSurface[] Surfaces { get; init; } = [];
    public NilPortal[] Portals { get; init; } = [];
    public NilLight[] Lights { get; init; } = [];
    /// <summary>File offset of this sector's record.</summary>
    public int Offset { get; init; }
}

/// <summary>An edge shared with a neighbouring walkable surface.</summary>
/// <param name="Flags">1/2: clamp the edge's start/end by the walker's radius; 4: impassable for some walkers;
/// 8: the target is a pass-through node that isn't emitted as a waypoint.</param>
/// <param name="Direction">Unit vector along the shared edge.</param>
/// <param name="Origin">The edge's start point.</param>
public readonly record struct NilPathLink(ushort Flags, uint Target, float Length, Vector3 Direction, Vector3 Origin);

/// <summary>One node per walkable surface. <see cref="Planes"/> are 2-D half-planes (a, b, d).</summary>
public sealed record NilPathNode(NilPathLink[] Links, Vector3[] Planes);

/// <summary>A lightmap page: RGB565 pixels, row-major.</summary>
public sealed record NilLightmap(int Width, int Height, ushort[] Pixels)
{
    /// <summary>Expands a pixel to 8-bit RGB (the usual 5/6/5 → 8 scaling).</summary>
    public (byte R, byte G, byte B) Rgb(int index)
    {
        int v = Pixels[index];
        return ((byte)(((v >> 11) & 31) * 255 / 31), (byte)(((v >> 5) & 63) * 255 / 63), (byte)((v & 31) * 255 / 31));
    }
}

/// <summary>
/// NIL level, version 27: geometry, sectors, portals, lights, the walkable-surface path graph and
/// lightmaps. The SDK spec stops mid-sector; this layout is the engine's own loader's, fully
/// sequential. All internal cross-references are validated.
/// </summary>
public sealed class NilLevel
{
    public const uint Version27 = 27;

    public uint Version { get; init; }
    /// <summary>Sun/moon light, the same record as sector lights.</summary>
    public NilLight GlobalLight { get; init; } = null!;
    public Vector3 Ambient { get; init; }
    public string[] Materials { get; init; } = [];
    public NilSector[] Sectors { get; init; } = [];
    public NilPathNode[] PathNodes { get; init; } = [];
    public NilLightmap[] Lightmaps { get; init; } = [];

    public static NilLevel Read(byte[] data, string name = "<nil>")
    {
        var r = new BinReader(data, name);
        if (data.Length < 4 || data[0] != (byte)'N' || data[1] != (byte)'I' || data[2] != (byte)'L') throw r.Error("bad magic");
        r.Skip(4);
        uint version = r.U32();
        if (version != Version27) throw r.Error($"unexpected NIL version {version}");
        var global = NilLight.Read(r);
        var ambient = r.Vec3();
        var mats = new string[r.Count32(100_000)];
        for (int i = 0; i < mats.Length; i++) mats[i] = r.CStr(32);
        var sectors = new NilSector[r.Count32(100_000)];
        for (int i = 0; i < sectors.Length; i++) sectors[i] = ReadSector(r);
        CheckPortals(r, sectors);
        var nodes = ReadPathGraph(r);
        for (int si = 0; si < sectors.Length; si++)
            foreach (var sf in sectors[si].Surfaces)
                if (sf.PathNode >= nodes.Length) throw r.Error($"sector {si} surface path node {sf.PathNode} >= {nodes.Length}");
        var lightmaps = ReadLightmaps(r);
        for (int si = 0; si < sectors.Length; si++)
            foreach (var g in sectors[si].Groups)
                foreach (var ly in g.Layers)
                    if (ly.Page >= lightmaps.Length) throw r.Error($"sector {si} lightmap page {ly.Page} >= {lightmaps.Length}");
        r.ExpectEof();
        return new NilLevel
        {
            Version = version, GlobalLight = global, Ambient = ambient, Materials = mats, Sectors = sectors,
            PathNodes = nodes, Lightmaps = lightmaps,
        };
    }

    static NilPlane Plane(BinReader r) => new(r.Vec3(), r.F32());

    static NilSector ReadSector(BinReader r)
    {
        int offset = r.Pos;
        uint flags = r.U32();
        var ambient = r.Vec3();
        var sectorPlanes = new NilPlane[r.U8()];
        for (int i = 0; i < sectorPlanes.Length; i++) sectorPlanes[i] = Plane(r);
        var surfacePlanes = new NilPlane[r.U16()];
        for (int i = 0; i < surfacePlanes.Length; i++) surfacePlanes[i] = Plane(r);
        var bsp = r.U16s(r.U16() * 4);
        int nv = r.U16();
        if (nv > 0x3fff) throw r.Error($"numVertices {nv} exceeds the engine maximum");
        var verts = new Vector3[nv];
        for (int i = 0; i < nv; i++) verts[i] = r.Vec3();
        int nsv = r.U16();
        var svs = new NilSectorVertex[nsv];
        for (int i = 0; i < nsv; i++)
        {
            ushort vi = r.U16(), pad = r.U16(); uint rgba = r.U32(); var uv = r.Vec2(); var luv = r.Vec2();
            if (vi >= nv) throw r.Error($"sector vertex {vi} >= {nv}");
            svs[i] = new NilSectorVertex(vi, pad, rgba, uv, luv);
        }
        int nsurf = r.Count32(0xfff);
        int ng = r.U16();
        if (ng > 0x1ff) throw r.Error($"numGroups {ng} exceeds the engine maximum");
        var groups = new NilGroup[ng];
        int s0 = 0, v0 = 0;
        for (int i = 0; i < ng; i++)
        {
            int gs = r.U16(); int mat = r.I32(); int nl = r.U8(); uint fl = r.U32(); int gv = r.U16();
            var layers = new NilLightmapLayer[nl];
            for (int l = 0; l < nl; l++) layers[l] = new NilLightmapLayer(r.U32(), r.U16());
            groups[i] = new NilGroup(gs, mat, fl, gv, layers, s0, v0);
            s0 += gs; v0 += gv;
        }
        if (s0 != nsurf || v0 != nsv) throw r.Error($"group totals {s0}/{v0} != {nsurf} surfaces/{nsv} sector vertices");
        var surfaces = new NilSurface[nsurf];
        int si = 0; v0 = 0;
        for (int gi = 0; gi < ng; gi++)
        {
            int gv = 0;
            for (int k = 0; k < groups[gi].NumSurfaces; k++)
            {
                ushort pl = r.U16(); byte n = r.U8(); int node = r.I32();
                if (pl >= surfacePlanes.Length) throw r.Error($"surface plane {pl} >= {surfacePlanes.Length}");
                surfaces[si++] = new NilSurface(pl, n, node, v0, gi);
                v0 += n; gv += n;
            }
            if (gv != groups[gi].NumVerts) throw r.Error($"group {gi} vertex count mismatch");
        }
        var portals = new NilPortal[r.U8()];
        for (int i = 0; i < portals.Length; i++)
        {
            uint fl = r.U32(), pid = r.U32(), npo = r.U32(), nsec = r.U32();
            var quad = new[] { r.Vec3(), r.Vec3(), r.Vec3(), r.Vec3() };
            portals[i] = new NilPortal(fl, pid, npo, nsec, quad, r.Vec3(), r.Bytes(20));
        }
        var lights = new NilLight[r.U16()];
        for (int i = 0; i < lights.Length; i++) lights[i] = NilLight.Read(r);
        return new NilSector
        {
            Flags = flags, Ambient = ambient, SectorPlanes = sectorPlanes, SurfacePlanes = surfacePlanes, BspNodes = bsp,
            Vertices = verts, SectorVertices = svs, Groups = groups, Surfaces = surfaces, Portals = portals, Lights = lights,
            Offset = offset,
        };
    }

    static void CheckPortals(BinReader r, NilSector[] sectors)
    {
        for (int si = 0; si < sectors.Length; si++)
            for (int pi = 0; pi < sectors[si].Portals.Length; pi++)
            {
                var p = sectors[si].Portals[pi];
                if (p.NeighbourSector >= sectors.Length || p.NeighbourPortal >= sectors[p.NeighbourSector].Portals.Length)
                    throw r.Error($"sector {si} portal {p.PortalId} points outside the level");
                var q = sectors[p.NeighbourSector].Portals[p.NeighbourPortal];
                if (q.NeighbourSector != si || q.NeighbourPortal != pi || q.PortalId != (p.PortalId ^ 1))
                    throw r.Error($"sector {si} portal {pi} is not mirrored by its neighbour");
            }
    }

    static NilPathNode[] ReadPathGraph(BinReader r)
    {
        int n = r.Count32(1_000_000);
        if (n == 0) return [];
        uint totalLinks = r.U32(), totalPlanes = r.U32();
        var nodes = new NilPathNode[n];
        long cl = 0, cp = 0;
        for (int i = 0; i < n; i++)
        {
            var links = new NilPathLink[r.U8()];
            for (int k = 0; k < links.Length; k++)
                links[k] = new NilPathLink(r.U16(), r.U32(), r.F32(), r.Vec3(), r.Vec3());
            var planes = new Vector3[r.U8()];
            for (int k = 0; k < planes.Length; k++) planes[k] = r.Vec3();
            cl += links.Length; cp += planes.Length;
            nodes[i] = new NilPathNode(links, planes);
        }
        if (cl != totalLinks || cp != totalPlanes) throw r.Error($"path graph totals {cl}/{cp} != header {totalLinks}/{totalPlanes}");
        for (int i = 0; i < n; i++)
            foreach (var lk in nodes[i].Links)
                if (lk.Target >= n) throw r.Error($"path node {i} links to {lk.Target} >= {n}");
        return nodes;
    }

    /// <summary>Pages of RGB565 pixels where every 0 pixel is followed by a u16 count of additional zeros.</summary>
    static NilLightmap[] ReadLightmaps(BinReader r)
    {
        var pages = new NilLightmap[r.Count32(100_000)];
        for (int p = 0; p < pages.Length; p++)
        {
            int w = r.U16(), h = r.U16(), total = w * h;
            var px = new ushort[total];
            int i = 0;
            while (i < total)
            {
                ushort v = r.U16();
                px[i++] = v;
                if (v == 0)
                {
                    int run = r.U16();
                    if (i + run > total) throw r.Error($"lightmap zero run overruns the page ({i + run} > {total})");
                    i += run;                  // the array is already zero-filled
                }
            }
            pages[p] = new NilLightmap(w, h, px);
        }
        return pages;
    }
}
