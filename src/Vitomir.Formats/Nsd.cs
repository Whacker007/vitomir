// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Formats;

public readonly record struct NsdKeyframe(Vector3 Orientation, Vector3 Position);

/// <summary>
/// A CODEXPARAMS value. The engine passes these to the script's constructor by position and
/// converts them by the <em>constructor's</em> parameter type, reinterpreting the raw 32 bits
/// (Technical Inventory, "CODEXPARAMS → constructor"). So the value is kept as raw bits.
/// </summary>
/// <param name="Type">The stored type code (it only decides how the file is parsed).</param>
/// <param name="Bits">The 4-byte value (types 1–5, 8–11); 0 otherwise.</param>
/// <param name="Text">The string value (type 7); null otherwise.</param>
public readonly record struct NsdParam(uint Type, int Bits, string? Text)
{
    public const uint Int = 1, Float = 2, Thing = 3, Sector = 4, Region = 5, Vector = 6, String = 7,
                      Actor = 8, Player = 9, Item = 10, Prop = 11;

    public float AsFloat => BitConverter.Int32BitsToSingle(Bits);

    /// <summary>Types whose value is an index into the scene's thing list (−1 = null) when the constructor expects a thing.</summary>
    public static bool IsThingReference(uint type) => type is Thing or Region or Actor or Player or Item or Prop;

    public static string TypeName(uint type) => type switch
    {
        Int => "int", Float => "float", Thing => "CodexThing", Sector => "CodexSector", Region => "CodexRegion",
        Vector => "CodexVector", String => "String", Actor => "CodexActor", Player => "CodexPlayer",
        Item => "CodexItem", Prop => "CodexProp", _ => $"type{type}",
    };
}

/// <summary>An inline static mesh in NOD layout (doors, lifts, triggers), plus its convex hull planes.</summary>
public sealed class NsdBrushModel
{
    public string[] Materials { get; init; } = [];
    /// <summary>0x6 (INLINE | STATIC) in every shipped model.</summary>
    public uint Flags { get; init; }
    public NodVertex[] Vertices { get; init; } = [];
    /// <summary>Triangle indices, 3 per face, relative to the owning group's first vertex.</summary>
    public ushort[] Indices { get; init; } = [];
    public NodMeshGroup[] Groups { get; init; } = [];
    public Vector3 BoundsMin { get; init; }
    public Vector3 BoundsMax { get; init; }
    /// <summary>Convex hull planes: (normal.xyz, dist).</summary>
    public Vector4[] Planes { get; init; } = [];
}

/// <summary>A placed thing: a tag list (template + overrides) with optional script parameters and geometry.</summary>
public sealed class NsdThing
{
    public const uint TagBasedOn = 0, TagOrientation = 1, TagPosition = 2, TagSector = 3, TagScript = 11;

    public NodTag[] Tags { get; init; } = [];
    public NsdKeyframe[] Keyframes { get; init; } = [];
    public NsdParam[] Params { get; init; } = [];
    public NsdBrushModel? Model { get; init; }
    /// <summary>The brush model as the file holds it (its materials to its planes), for writing a scene back (<see cref="NsdWrite"/>).</summary>
    public byte[]? ModelBytes { get; init; }
    /// <summary>Region things only: the sectors they touch.</summary>
    public uint[] Sectors { get; init; } = [];
    /// <summary>Baked per-vertex light for the thing's model (4 bytes each, as stored).</summary>
    public uint[] VertexColors { get; init; } = [];
    public int Offset { get; init; }

    public NodTag? Tag(uint id)
    {
        foreach (var t in Tags) if (t.Id == id) return t;
        return null;
    }

    public string Template => Tag(TagBasedOn)?.AsString() ?? "";
    public Vector3 Orientation => Tag(TagOrientation)?.AsVector3() ?? default;
    public Vector3 Position => Tag(TagPosition)?.AsVector3() ?? default;
    public long Sector => Tag(TagSector) is { } t ? t.AsUInt() : -1;
    public string Script => Tag(TagScript)?.AsString() ?? "";
    /// <summary>Tags that override the template (everything but template, orientation, position, sector).</summary>
    public IEnumerable<NodTag> Overrides => Tags.Where(t => t.Id > TagSector);

    /// <summary>
    /// A thing placed by hand rather than read from a scene — the player, and anything a script
    /// spawns. It carries the four tags every placed thing has and nothing else, so its template
    /// supplies the rest exactly as it would for one the level builder placed.
    /// </summary>
    public static NsdThing Placed(string template, Vector3 position, Vector3 orientation, long sector = -1) =>
        new()
        {
            Tags =
            [
                NodTag.Of(TagBasedOn, template),
                NodTag.Of(TagOrientation, orientation),
                NodTag.Of(TagPosition, position),
                NodTag.Of(TagSector, (uint)Math.Max(0, sector)),
            ],
        };
}

public sealed record NsdSectorSound(string Wav, int Volume, int Balance, int Flags, uint[] Sectors);

public sealed record NsdPointSound(string Wav, int Volume, int Balance, int Flags, Vector3 Position,
                                   float MinDist, float MaxDist, uint[] Sectors);

/// <summary>A player or conversation start (60 B).</summary>
/// <param name="Rotation">3×3, row-major.</param>
/// <param name="Group">The entity's class number (playerstart03 → 3).</param>
/// <param name="Number">Explicit number within the group (0 = unnumbered).</param>
public sealed record NsdStart(uint Sector, float[] Rotation, Vector3 Position, uint Group, uint Number);

/// <summary>An AI interest spot (72 B).</summary>
/// <param name="Runtime">Overwritten at load.</param>
/// <param name="Radius">128 in every shipped spot (meaning inferred).</param>
/// <param name="Tail">8 bytes, zero in every shipped spot.</param>
public sealed record NsdInterestSpot(uint Sector, uint Runtime, float[] Rotation, Vector3 Position, uint Group,
                                     float Radius, byte[] Tail);

/// <summary>
/// NSD scene, version 8: placed things, keyframes, script parameters, brush models, region
/// sectors, vertex colours, sounds, starts, interest spots, conversation starts and the scene's
/// audio/fog/sky settings. Layout = the engine's scene loader.
/// </summary>
public sealed class NsdScene
{
    public const uint Version8 = 8;

    public uint Version { get; init; }
    /// <summary>Two 32-byte editor scratch buffers (junk); the scene is named by its file.</summary>
    public byte[] ScratchA { get; init; } = [];
    public byte[] ScratchB { get; init; } = [];
    public uint SceneFlags { get; init; }
    /// <summary>Extra .not files to load first (only n1_utwn uses one).</summary>
    public string[] TemplateFiles { get; init; } = [];
    public NsdThing[] Things { get; init; } = [];
    /// <summary>Whether the file has its sounds' part (a word before them says so; none of the sounds follow when it is 0).</summary>
    public bool HasSounds { get; init; }
    public NsdSectorSound[] SectorSounds { get; init; } = [];
    public NsdPointSound[] PointSounds { get; init; } = [];
    public NsdStart[] Starts { get; init; } = [];
    public NsdInterestSpot[] InterestSpots { get; init; } = [];
    public NsdStart[] ConversationStarts { get; init; } = [];
    public string Music { get; init; } = "";
    public string Ambient { get; init; } = "";
    public uint SceneFlags2 { get; init; }
    public uint Volume { get; init; }
    public uint AmbientVolume { get; init; }
    public uint Eax { get; init; }
    public uint FogType { get; init; }
    public Vector3 FogColor { get; init; }
    public float FogDist1 { get; init; }
    public float FogDist2 { get; init; }
    public float ZClip { get; init; }
    public uint AtmosphereEffects { get; init; }
    public string SkyModel { get; init; } = "";
    public string SkyAnimation { get; init; } = "";

    public static NsdScene Read(byte[] data, string name = "<nsd>")
    {
        var r = new BinReader(data, name);
        if (data.Length < 4 || data[0] != (byte)'N' || data[1] != (byte)'S' || data[2] != (byte)'D' || data[3] != 0x10)
            throw r.Error("bad magic");
        r.Skip(4);
        uint version = r.U32();
        if (version != Version8) throw r.Error($"unexpected NSD version {version}");
        var scratchA = r.Bytes(32); var scratchB = r.Bytes(32);
        uint sceneFlags = r.U32();
        var templateFiles = new string[r.Count32(1000)];
        for (int i = 0; i < templateFiles.Length; i++) templateFiles[i] = r.CStr(32);
        var things = new NsdThing[r.Count32(1_000_000)];
        for (int i = 0; i < things.Length; i++) things[i] = ReadThing(r, (a, b) => data[a..b]);
        for (int i = 0; i < things.Length; i++)
            foreach (var p in things[i].Params)
                if (NsdParam.IsThingReference(p.Type) && (p.Bits < -1 || p.Bits >= things.Length))
                    throw r.Error($"thing {i} parameter refers to thing {p.Bits} of {things.Length}");
        NsdSectorSound[] sectorSounds = []; NsdPointSound[] pointSounds = [];
        bool hasSounds = r.U32() != 0;
        if (hasSounds)
        {
            sectorSounds = new NsdSectorSound[r.Count32(100_000)];
            for (int i = 0; i < sectorSounds.Length; i++)
            {
                string wav = r.CStr(32); int vol = r.I32(), bal = r.I32(), fl = r.I32();
                sectorSounds[i] = new NsdSectorSound(wav, vol, bal, fl, r.U32s(r.Count32(100_000)));
            }
            pointSounds = new NsdPointSound[r.Count32(100_000)];
            for (int i = 0; i < pointSounds.Length; i++)
            {
                string wav = r.CStr(32); int vol = r.I32(), bal = r.I32(), fl = r.I32();
                var pos = r.Vec3(); float mn = r.F32(), mx = r.F32();
                pointSounds[i] = new NsdPointSound(wav, vol, bal, fl, pos, mn, mx, r.U32s(r.Count32(100_000)));
            }
        }
        var starts = ReadStarts(r);
        var spots = new NsdInterestSpot[r.Count32(100_000)];
        for (int i = 0; i < spots.Length; i++)
            spots[i] = new NsdInterestSpot(r.U32(), r.U32(), r.Floats(9), r.Vec3(), r.U32(), r.F32(), r.Bytes(8));
        var convs = ReadStarts(r);
        var scene = new NsdScene
        {
            Version = version, ScratchA = scratchA, ScratchB = scratchB, SceneFlags = sceneFlags, TemplateFiles = templateFiles,
            Things = things, HasSounds = hasSounds, SectorSounds = sectorSounds, PointSounds = pointSounds, Starts = starts, InterestSpots = spots,
            ConversationStarts = convs,
            Music = r.CStr(32), Ambient = r.CStr(32), SceneFlags2 = r.U32(), Volume = r.U32(), AmbientVolume = r.U32(),
            Eax = r.U32(), FogType = r.U32(), FogColor = r.Vec3(), FogDist1 = r.F32(), FogDist2 = r.F32(), ZClip = r.F32(),
            AtmosphereEffects = r.U32(), SkyModel = r.CStr(32), SkyAnimation = r.CStr(32),
        };
        r.ExpectEof();
        return scene;
    }

    static NsdStart[] ReadStarts(BinReader r)
    {
        var starts = new NsdStart[r.Count32(100_000)];
        for (int i = 0; i < starts.Length; i++)
            starts[i] = new NsdStart(r.U32(), r.Floats(9), r.Vec3(), r.U32(), r.U32());
        return starts;
    }

    static NsdThing ReadThing(BinReader r, Func<int, int, byte[]> data)
    {
        int offset = r.Pos;
        uint nt = r.U32();
        if (nt is < 1 or > 128) throw r.Error($"thing tag count {nt}");
        var tags = new NodTag[nt];
        for (int i = 0; i < nt; i++) tags[i] = NodTag.Read(r);
        var keyframes = new NsdKeyframe[r.Count32(100_000)];
        for (int i = 0; i < keyframes.Length; i++) keyframes[i] = new NsdKeyframe(r.Vec3(), r.Vec3());
        var ps = new NsdParam[r.Count32(4096)];
        for (int i = 0; i < ps.Length; i++)
        {
            uint type = r.U32();
            ps[i] = type switch
            {
                NsdParam.String => new NsdParam(type, 0, BinReader.CString(r.Bytes(r.Count32(1 << 20)))),
                1 or 2 or 3 or 4 or 5 or 8 or 9 or 10 or 11 => new NsdParam(type, r.I32(), null),
                _ => new NsdParam(type, 0, null),            // the engine reads no value for other types
            };
        }
        NsdBrushModel? model = null;
        byte[]? modelBytes = null;
        if (r.U32() != 0)
        {
            int from = r.Pos;
            model = ReadBrushModel(r);
            modelBytes = data(from, r.Pos);
        }
        var sectors = r.U32s(r.Count32(100_000));
        int nvc = r.Count32(10_000);
        var colors = r.U32s(nvc);
        return new NsdThing { Tags = tags, Keyframes = keyframes, Params = ps, Model = model, ModelBytes = modelBytes, Sectors = sectors,
                              VertexColors = colors, Offset = offset };
    }

    /// <summary>A brush model from its bytes as a scene holds them (<see cref="NsdThing.ModelBytes"/>).</summary>
    public static NsdBrushModel ReadBrushModelBytes(byte[] data, string name)
    {
        var r = new BinReader(data, name + " (brush model)");
        var model = ReadBrushModel(r);
        r.ExpectEof();
        return model;
    }

    static NsdBrushModel ReadBrushModel(BinReader r)
    {
        var mats = new string[r.Count32(4096)];
        for (int i = 0; i < mats.Length; i++) mats[i] = r.CStr(32);
        int nv = r.Count32(), nf = r.Count32(); int ng = r.U16(); uint flags = r.U32();
        var verts = NodModel.ReadVertices(r, nv);
        var indices = r.U16s(nf * 3);
        var groups = new NodMeshGroup[ng];
        for (int i = 0; i < ng; i++) groups[i] = NodMeshGroup.Read(r);
        NodModel.AssignOffsets(r, groups, nv, nf);
        var bmin = r.Vec3(); var bmax = r.Vec3();
        var planes = new Vector4[r.Count32(100_000)];
        for (int i = 0; i < planes.Length; i++) planes[i] = new Vector4(r.F32(), r.F32(), r.F32(), r.F32());
        return new NsdBrushModel { Materials = mats, Flags = flags, Vertices = verts, Indices = indices, Groups = groups,
                                   BoundsMin = bmin, BoundsMax = bmax, Planes = planes };
    }
}
