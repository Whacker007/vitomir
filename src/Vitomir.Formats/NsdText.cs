// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A scene's recooked text and its geometry file (ours), and a scene written back as the engine reads it.
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Vitomir.Formats;

/// <summary>
/// **A scene as text** (ours): <c>scenes/X.nsd</c> recooked is <c>scenes/X.txt</c>, a person's to read and change, and
/// <c>scenes/X.geometry</c>, which holds what no person edits by hand — the brush models of doors, lifts and triggers, and the light
/// baked into each model's vertices — by the thing it belongs to.
/// <list type="bullet">
/// <item><c>[scene]</c>: the scene's own settings — music, fog, sky and the rest.</item>
/// <item><c>[thing n]</c>: a placed thing, its tags as a template's (<see cref="TagTable"/>), then its <c>keyframe</c> lines, its script's
/// <c>param</c> lines in order, the sectors a region touches, and whether it has <c>geometry</c> and baked <c>lighting</c>. <c>n</c> is the
/// thing's own number: a parameter that names a thing names it by that number, and the geometry file keys by it, so things may be taken
/// out, added or moved about in the file and the references still hold. The things are placed in the file's order.</item>
/// <item><c>[start n]</c>, <c>[conversation n]</c>, <c>[spot n]</c>, <c>[sound n]</c> and <c>[point sound n]</c>: the player's and the
/// conversations' starts, the AI's interest spots and the sounds, in order.</item>
/// </list>
/// What the original file holds that nothing reads — the editor's two scratch buffers, the spots' runtime word and tail — is not kept.
/// </summary>
public static class NsdText
{
    public const string Extension = ".txt", GeometryExtension = ".geometry";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static string N(float f) => TagTable.Number(f);
    static string V(Vector3 v) => TagTable.Numbers(v);
    static string Floats(float[] f) => string.Join(", ", f.Select(N));
    static string UInts(uint[] u) => string.Join(", ", u.Select(x => x.ToString(Inv)));
    static string Str(string s) => s.Length == 0 || s != s.Trim() || s.StartsWith('"') ? $"\"{s}\"" : s;
    static string Unq(string s) => s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s[1..^1] : s;

    static readonly (uint Type, string Word)[] ParamWords =
    [
        (NsdParam.Int, "int"), (NsdParam.Float, "float"), (NsdParam.Thing, "thing"), (NsdParam.Sector, "sector"), (NsdParam.Region, "region"),
        (NsdParam.Vector, "vector"), (NsdParam.String, "text"), (NsdParam.Actor, "actor"), (NsdParam.Player, "player"), (NsdParam.Item, "item"),
        (NsdParam.Prop, "prop"),
    ];

    /// <summary>The scene's text and its geometry file (null when no thing has any).</summary>
    public static (string Text, byte[]? Geometry) Write(NsdScene scene, string? from = null)
    {
        var w = new RecookText.Writer();
        if (from is not null) w.Comment($"recooked from {from}");
        w.Section("scene");
        w.Key("flags", scene.SceneFlags.ToString(Inv));
        if (scene.TemplateFiles.Length > 0) w.Key("templates", string.Join(", ", scene.TemplateFiles));
        w.Key("music", Str(scene.Music));
        w.Key("ambient", Str(scene.Ambient));
        w.Key("flags2", scene.SceneFlags2.ToString(Inv));
        w.Key("volume", scene.Volume.ToString(Inv));
        w.Key("ambient volume", scene.AmbientVolume.ToString(Inv));
        w.Key("eax", scene.Eax.ToString(Inv));
        w.Key("fog type", scene.FogType.ToString(Inv));
        w.Key("fog colour", V(scene.FogColor));
        w.Key("fog near", N(scene.FogDist1));
        w.Key("fog far", N(scene.FogDist2));
        w.Key("z clip", N(scene.ZClip));
        w.Key("atmosphere", scene.AtmosphereEffects.ToString(Inv));
        w.Key("sky", Str(scene.SkyModel));
        w.Key("sky animation", Str(scene.SkyAnimation));
        w.Key("sounds", scene.HasSounds ? "yes" : "no");

        var geometry = new List<(uint Label, byte[]? Model, uint[] Colours)>();
        for (int i = 0; i < scene.Things.Length; i++)
        {
            var t = scene.Things[i];
            w.Section("thing", i.ToString(Inv));
            foreach (var tag in t.Tags) w.Key(TagTable.NameOf(tag.Id), TagTable.Write(tag));
            foreach (var k in t.Keyframes) w.Key("keyframe", $"{V(k.Orientation)}, {V(k.Position)}");
            foreach (var p in t.Params) w.Key("param", Param(p));
            if (t.Sectors.Length > 0) w.Key("sectors", UInts(t.Sectors));
            if (t.ModelBytes is not null) w.Key("geometry", "yes");
            if (t.VertexColors.Length > 0) w.Key("lighting", "yes");
            if (t.ModelBytes is not null || t.VertexColors.Length > 0) geometry.Add(((uint)i, t.ModelBytes, t.VertexColors));
        }
        if (scene.HasSounds)
        {
            for (int i = 0; i < scene.SectorSounds.Length; i++)
            {
                var s = scene.SectorSounds[i];
                w.Section("sound", (i + 1).ToString(Inv));
                w.Key("wav", Str(s.Wav)).Key("volume", s.Volume.ToString(Inv)).Key("balance", s.Balance.ToString(Inv))
                 .Key("flags", s.Flags.ToString(Inv)).Key("sectors", UInts(s.Sectors));
            }
            for (int i = 0; i < scene.PointSounds.Length; i++)
            {
                var s = scene.PointSounds[i];
                w.Section("point", $"sound {i + 1}");
                w.Key("wav", Str(s.Wav)).Key("volume", s.Volume.ToString(Inv)).Key("balance", s.Balance.ToString(Inv))
                 .Key("flags", s.Flags.ToString(Inv)).Key("position", V(s.Position)).Key("near", N(s.MinDist)).Key("far", N(s.MaxDist))
                 .Key("sectors", UInts(s.Sectors));
            }
        }
        Starts(w, "start", scene.Starts);
        for (int i = 0; i < scene.InterestSpots.Length; i++)
        {
            var s = scene.InterestSpots[i];
            w.Section("spot", (i + 1).ToString(Inv));
            w.Key("sector", s.Sector.ToString(Inv)).Key("rotation", Floats(s.Rotation)).Key("position", V(s.Position))
             .Key("group", s.Group.ToString(Inv)).Key("radius", N(s.Radius));
        }
        Starts(w, "conversation", scene.ConversationStarts);
        return (w.ToString(), geometry.Count == 0 ? null : WriteGeometry(geometry));
    }

    static void Starts(RecookText.Writer w, string name, NsdStart[] starts)
    {
        for (int i = 0; i < starts.Length; i++)
        {
            var s = starts[i];
            w.Section(name, (i + 1).ToString(Inv));
            w.Key("sector", s.Sector.ToString(Inv)).Key("rotation", Floats(s.Rotation)).Key("position", V(s.Position))
             .Key("group", s.Group.ToString(Inv)).Key("number", s.Number.ToString(Inv));
        }
    }

    static string Param(NsdParam p)
    {
        string word = ParamWords.FirstOrDefault(x => x.Type == p.Type).Word ?? $"type {p.Type}";
        return p.Type switch
        {
            NsdParam.String => $"text {Str(p.Text ?? "")}",
            NsdParam.Float => $"float {N(p.AsFloat)}",
            NsdParam.Int or NsdParam.Sector => $"{word} {p.Bits.ToString(Inv)}",
            _ when NsdParam.IsThingReference(p.Type) => p.Bits < 0 ? $"{word} none" : $"{word} {p.Bits.ToString(Inv)}",
            _ => word,
        };
    }

    // ---- reading -------------------------------------------------------------------------------------------------------------

    /// <summary>A scene read back from its text and its geometry file (null when it has none).</summary>
    public static NsdScene Read(RecookText text, byte[]? geometry)
    {
        var geo = geometry is null ? new Dictionary<uint, (byte[]? Model, uint[] Colours)>() : ReadGeometry(geometry, text.Name);
        var scene = text.Of("scene").FirstOrDefault() ?? throw text.Error(1, "no [scene] section");
        var thingSections = text.Of("thing").ToList();
        // The things' own numbers, to the place each takes in the file's order.
        var place = new Dictionary<int, int>();
        for (int i = 0; i < thingSections.Count; i++)
        {
            if (!int.TryParse(thingSections[i].Label, NumberStyles.Integer, Inv, out int label) || label < 0)
                throw text.Error(thingSections[i].Line, "a [thing] needs its number");
            if (!place.TryAdd(label, i)) throw text.Error(thingSections[i].Line, $"thing {label} is in the file twice");
        }
        var things = new NsdThing[thingSections.Count];
        for (int i = 0; i < things.Length; i++) things[i] = Thing(text, thingSections[i], place, geo);

        bool sounds = Yes(text, scene, "sounds");
        var sectorSounds = text.Of("sound").Select(s => new NsdSectorSound(Unq(Req(text, s, "wav")), I(text, s, "volume"), I(text, s, "balance"),
                                                                            I(text, s, "flags"), U32s(text, s, "sectors"))).ToArray();
        var pointSounds = text.Of("point").Select(s => new NsdPointSound(Unq(Req(text, s, "wav")), I(text, s, "volume"), I(text, s, "balance"),
                                                                           I(text, s, "flags"), Vec(text, s, "position"), F(text, s, "near"),
                                                                           F(text, s, "far"), U32s(text, s, "sectors"))).ToArray();
        if (!sounds && (sectorSounds.Length > 0 || pointSounds.Length > 0)) throw text.Error(scene.Line, "sounds = no, but the file has sounds");
        return new NsdScene
        {
            Version = NsdScene.Version8, ScratchA = new byte[32], ScratchB = new byte[32],
            SceneFlags = U(text, scene, "flags"),
            TemplateFiles = scene["templates"] is { } tf ? tf.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [],
            Things = things, HasSounds = sounds, SectorSounds = sectorSounds, PointSounds = pointSounds,
            Starts = ReadStarts(text, "start"),
            InterestSpots = [.. text.Of("spot").Select(s => new NsdInterestSpot(U(text, s, "sector"), 0, Rot(text, s), Vec(text, s, "position"),
                                                                                U(text, s, "group"), F(text, s, "radius"), new byte[8]))],
            ConversationStarts = ReadStarts(text, "conversation"),
            Music = Unq(scene["music"] ?? ""), Ambient = Unq(scene["ambient"] ?? ""), SceneFlags2 = U(text, scene, "flags2"),
            Volume = U(text, scene, "volume"), AmbientVolume = U(text, scene, "ambient volume"), Eax = U(text, scene, "eax"),
            FogType = U(text, scene, "fog type"), FogColor = Vec(text, scene, "fog colour"), FogDist1 = F(text, scene, "fog near"),
            FogDist2 = F(text, scene, "fog far"), ZClip = F(text, scene, "z clip"), AtmosphereEffects = U(text, scene, "atmosphere"),
            SkyModel = Unq(scene["sky"] ?? ""), SkyAnimation = Unq(scene["sky animation"] ?? ""),
        };
    }

    static NsdStart[] ReadStarts(RecookText text, string name) =>
        [.. text.Of(name).Select(s => new NsdStart(U(text, s, "sector"), Rot(text, s), Vec(text, s, "position"), U(text, s, "group"), U(text, s, "number")))];

    static NsdThing Thing(RecookText text, RecookText.Section s, Dictionary<int, int> place, Dictionary<uint, (byte[]? Model, uint[] Colours)> geo)
    {
        var tags = new List<NodTag>();
        var keyframes = new List<NsdKeyframe>();
        var ps = new List<NsdParam>();
        uint[] sectors = [];
        bool geometry = false, lighting = false;
        var seen = new HashSet<uint>();
        foreach (var e in s.Entries)
        {
            switch (e.Key.ToLowerInvariant())
            {
                case "keyframe":
                    if (TagTable.ReadFloats(e.Value) is not { Length: 6 } k) throw text.Error(e.Line, "a keyframe is six numbers: its orientation, then its position");
                    keyframes.Add(new NsdKeyframe(new Vector3(k[0], k[1], k[2]), new Vector3(k[3], k[4], k[5])));
                    continue;
                case "param":
                    ps.Add(ReadParam(text, e, place));
                    continue;
                case "sectors":
                    sectors = TagTable.ReadUInts(e.Value) ?? throw text.Error(e.Line, "sectors are whole numbers");
                    continue;
                case "geometry": geometry = YesNo(text, e); continue;
                case "lighting": lighting = YesNo(text, e); continue;
            }
            if (TagTable.IdOf(e.Key) is not uint id) throw text.Error(e.Line, $"{e.Key} is no tag the engine has");
            if (!seen.Add(id)) throw text.Error(e.Line, $"{e.Key} is set twice");
            if (TagTable.Read(id, e.Value, out var why) is not { } tag) throw text.Error(e.Line, $"{e.Key} = {e.Value}: {why}");
            tags.Add(tag);
        }
        if (tags.Count == 0) throw text.Error(s.Line, "a thing needs its tags");
        uint label = uint.Parse(s.Label, Inv);
        geo.TryGetValue(label, out var g);
        if (geometry && g.Model is null) throw text.Error(s.Line, $"geometry = yes, but the geometry file has none for thing {label}");
        if (lighting && (g.Colours is null || g.Colours.Length == 0)) throw text.Error(s.Line, $"lighting = yes, but the geometry file has none for thing {label}");
        byte[]? model = geometry ? g.Model : null;
        return new NsdThing
        {
            Tags = [.. tags], Keyframes = [.. keyframes], Params = [.. ps],
            ModelBytes = model, Model = model is null ? null : NsdScene.ReadBrushModelBytes(model, text.Name),
            Sectors = sectors, VertexColors = lighting ? g.Colours! : [],
        };
    }

    static NsdParam ReadParam(RecookText text, RecookText.Entry e, Dictionary<int, int> place)
    {
        string v = e.Value;
        int space = v.IndexOf(' ');
        string word = space < 0 ? v : v[..space];
        string rest = space < 0 ? "" : v[(space + 1)..].Trim();
        if (word.Equals("type", StringComparison.OrdinalIgnoreCase) && uint.TryParse(rest, NumberStyles.None, Inv, out uint other))
            return new NsdParam(other, 0, null);
        var (type, _) = ParamWords.FirstOrDefault(x => x.Word.Equals(word, StringComparison.OrdinalIgnoreCase));
        if (type == 0) throw text.Error(e.Line, $"{word} is no parameter type (int, float, text, thing, sector, region, actor, player, item, prop, vector)");
        switch (type)
        {
            case NsdParam.String: return new NsdParam(type, 0, Unq(rest));
            case NsdParam.Float:
                if (!float.TryParse(rest, NumberStyles.Float, Inv, out float f)) throw text.Error(e.Line, "float wants a number");
                return new NsdParam(type, BitConverter.SingleToInt32Bits(f), null);
            case NsdParam.Int or NsdParam.Sector:
                if (!int.TryParse(rest, NumberStyles.Integer, Inv, out int n)) throw text.Error(e.Line, $"{word} wants a whole number");
                return new NsdParam(type, n, null);
            case NsdParam.Vector: return new NsdParam(type, 0, null);
            default:
                if (rest.Equals("none", StringComparison.OrdinalIgnoreCase)) return new NsdParam(type, -1, null);
                if (!int.TryParse(rest, NumberStyles.Integer, Inv, out int label)) throw text.Error(e.Line, $"{word} wants a thing's number, or none");
                if (!place.TryGetValue(label, out int at)) throw text.Error(e.Line, $"{word} {label}: the scene has no thing {label}");
                return new NsdParam(type, at, null);
        }
    }

    static string Req(RecookText text, RecookText.Section s, string key) => s[key] ?? throw text.Error(s.Line, $"[{s.Name}] needs {key}");
    static uint U(RecookText text, RecookText.Section s, string key) =>
        uint.TryParse(Req(text, s, key), NumberStyles.None, Inv, out uint v) ? v : throw text.Error(s.Line, $"{key} is a whole number of 0 or more");
    static int I(RecookText text, RecookText.Section s, string key) =>
        int.TryParse(Req(text, s, key), NumberStyles.Integer, Inv, out int v) ? v : throw text.Error(s.Line, $"{key} is a whole number");
    static float F(RecookText text, RecookText.Section s, string key) =>
        float.TryParse(Req(text, s, key), NumberStyles.Float, Inv, out float v) ? v : throw text.Error(s.Line, $"{key} is a number");
    static Vector3 Vec(RecookText text, RecookText.Section s, string key) =>
        TagTable.ReadVector(Req(text, s, key)) ?? throw text.Error(s.Line, $"{key} is three numbers");
    static float[] Rot(RecookText text, RecookText.Section s) =>
        TagTable.ReadFloats(Req(text, s, "rotation")) is { Length: 9 } r ? r : throw text.Error(s.Line, "rotation is nine numbers, row by row");
    static uint[] U32s(RecookText text, RecookText.Section s, string key) =>
        TagTable.ReadUInts(s[key] ?? "") ?? throw text.Error(s.Line, $"{key} are whole numbers");
    static bool Yes(RecookText text, RecookText.Section s, string key) => s[key] is { } v && YesNo(text, new RecookText.Entry(key, v, s.Line));
    static bool YesNo(RecookText text, RecookText.Entry e) => e.Value.ToLowerInvariant() switch
    {
        "yes" => true, "no" => false, _ => throw text.Error(e.Line, $"{e.Key} is yes or no"),
    };

    // ---- the geometry file ---------------------------------------------------------------------------------------------------

    /// <summary>The geometry file's start: <c>VGEO</c> and its version.</summary>
    static ReadOnlySpan<byte> Magic => "VGEO"u8;
    const uint GeometryVersion = 1;

    /// <summary>
    /// The geometry file (ours): <c>VGEO</c>, version 1, a count, then for each thing that has any: its number, its brush model as the
    /// original scene holds it (a length, 0 for none, then the bytes), and its baked vertex colours (a count, then each).
    /// </summary>
    static byte[] WriteGeometry(List<(uint Label, byte[]? Model, uint[] Colours)> things)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Magic);
        w.Write(GeometryVersion);
        w.Write(things.Count);
        foreach (var (label, model, colours) in things)
        {
            w.Write(label);
            w.Write(model?.Length ?? 0);
            if (model is not null) w.Write(model);
            w.Write(colours.Length);
            foreach (var c in colours) w.Write(c);
        }
        return ms.ToArray();
    }

    static Dictionary<uint, (byte[]? Model, uint[] Colours)> ReadGeometry(byte[] data, string name)
    {
        var r = new BinReader(data, name + " (geometry)");
        if (data.Length < 8 || !data.AsSpan(0, 4).SequenceEqual(Magic)) throw r.Error("not a scene's geometry file");
        r.Skip(4);
        if (r.U32() != GeometryVersion) throw r.Error("a geometry file of another version");
        var result = new Dictionary<uint, (byte[]? Model, uint[] Colours)>();
        int n = r.Count32(1_000_000);
        for (int i = 0; i < n; i++)
        {
            uint label = r.U32();
            int length = r.Count32(1 << 28);
            byte[]? model = length > 0 ? r.Bytes(length) : null;
            var colours = r.U32s(r.Count32(10_000_000));
            result[label] = (model, colours);
        }
        r.ExpectEof();
        return result;
    }
}

/// <summary>A scene written back as the engine reads it (<c>NSD\x10</c>, version 8).</summary>
public static class NsdWrite
{
    public static byte[] Write(NsdScene s)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("NSD\x10"u8);
        w.Write(NsdScene.Version8);
        w.Write(Pad(s.ScratchA, 32));
        w.Write(Pad(s.ScratchB, 32));
        w.Write(s.SceneFlags);
        w.Write(s.TemplateFiles.Length);
        foreach (var f in s.TemplateFiles) w.Write(NotFileWrite.Fixed(f, 32));
        w.Write(s.Things.Length);
        foreach (var t in s.Things)
        {
            w.Write(t.Tags.Length);
            foreach (var tag in t.Tags) { w.Write(tag.Id); w.Write(tag.Raw); }
            w.Write(t.Keyframes.Length);
            foreach (var k in t.Keyframes) { Vec(w, k.Orientation); Vec(w, k.Position); }
            w.Write(t.Params.Length);
            foreach (var p in t.Params)
            {
                w.Write(p.Type);
                if (p.Type == NsdParam.String)
                {
                    var bytes = Encoding.Latin1.GetBytes(p.Text ?? "");
                    w.Write(bytes.Length + 1);
                    w.Write(bytes);
                    w.Write((byte)0);
                }
                else if (p.Type is 1 or 2 or 3 or 4 or 5 or 8 or 9 or 10 or 11) w.Write(p.Bits);
            }
            if (t.ModelBytes is { } model) { w.Write(1); w.Write(model); }
            else w.Write(0);
            w.Write(t.Sectors.Length);
            foreach (var x in t.Sectors) w.Write(x);
            w.Write(t.VertexColors.Length);
            foreach (var c in t.VertexColors) w.Write(c);
        }
        w.Write(s.HasSounds ? 1 : 0);
        if (s.HasSounds)
        {
            w.Write(s.SectorSounds.Length);
            foreach (var x in s.SectorSounds)
            {
                w.Write(NotFileWrite.Fixed(x.Wav, 32)); w.Write(x.Volume); w.Write(x.Balance); w.Write(x.Flags);
                w.Write(x.Sectors.Length); foreach (var q in x.Sectors) w.Write(q);
            }
            w.Write(s.PointSounds.Length);
            foreach (var x in s.PointSounds)
            {
                w.Write(NotFileWrite.Fixed(x.Wav, 32)); w.Write(x.Volume); w.Write(x.Balance); w.Write(x.Flags);
                Vec(w, x.Position); w.Write(x.MinDist); w.Write(x.MaxDist);
                w.Write(x.Sectors.Length); foreach (var q in x.Sectors) w.Write(q);
            }
        }
        Starts(w, s.Starts);
        w.Write(s.InterestSpots.Length);
        foreach (var x in s.InterestSpots)
        {
            w.Write(x.Sector); w.Write(x.Runtime); foreach (var f in x.Rotation) w.Write(f); Vec(w, x.Position);
            w.Write(x.Group); w.Write(x.Radius); w.Write(Pad(x.Tail, 8));
        }
        Starts(w, s.ConversationStarts);
        w.Write(NotFileWrite.Fixed(s.Music, 32));
        w.Write(NotFileWrite.Fixed(s.Ambient, 32));
        w.Write(s.SceneFlags2); w.Write(s.Volume); w.Write(s.AmbientVolume); w.Write(s.Eax); w.Write(s.FogType);
        Vec(w, s.FogColor); w.Write(s.FogDist1); w.Write(s.FogDist2); w.Write(s.ZClip); w.Write(s.AtmosphereEffects);
        w.Write(NotFileWrite.Fixed(s.SkyModel, 32));
        w.Write(NotFileWrite.Fixed(s.SkyAnimation, 32));
        return ms.ToArray();
    }

    static void Starts(BinaryWriter w, NsdStart[] starts)
    {
        w.Write(starts.Length);
        foreach (var x in starts) { w.Write(x.Sector); foreach (var f in x.Rotation) w.Write(f); Vec(w, x.Position); w.Write(x.Group); w.Write(x.Number); }
    }

    static void Vec(BinaryWriter w, Vector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }

    static byte[] Pad(byte[] b, int n)
    {
        var r = new byte[n];
        b.AsSpan(0, Math.Min(b.Length, n)).CopyTo(r);
        return r;
    }
}
