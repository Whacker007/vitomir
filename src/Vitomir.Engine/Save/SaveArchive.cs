// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The file on disk. Decided: **one file, which is a zip of readable parts** — exactly
// what a `.nob` already is — so the player sees one save and anyone can open it with any zip tool:
//
//   save.jsonc              what the load list shows, and what the game was played under
//   chronicle.jsonc         where the story is
//   session.jsonc           the purse, the quests, the shops
//   party.jsonc             who travels with the player
//   world/<location>.jsonc  every place the game still remembers, one each
//
// `WritePlain` writes the same parts as a loose folder, for development and for anyone who would
// rather edit files than an archive; `Read` takes either.
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Save;

public static class SaveArchive
{
    /// <summary>
    /// This format's version. Every version ever written stays readable. 2: one GUID counter
    /// for the whole game (`SessionSave.ThingCounter`) and every thing's GUID unique across places;
    /// a format-1 save is converted as it is read (<see cref="SaveGuids.Convert"/>).
    /// </summary>
    public const int FormatVersion = 2;

    /// <summary>A save file's extension.</summary>
    public const string Extension = ".vsave";

    public const string HeaderPart = "save.jsonc", ChroniclePart = "chronicle.jsonc", SessionPart = "session.jsonc",
                        PartyPart = "party.jsonc", WorldFolder = "world/";

    // ---- writing -------------------------------------------------------------------------------

    /// <summary>
    /// Every part's name and text, in the order they go into the archive; without comments when
    /// <paramref name="strict"/> (or `--save-strict-json`, <see cref="Jsonc.Strict"/>) says so.
    /// </summary>
    public static List<(string Name, string Text)> Parts(SaveGame game, bool? strict = null)
    {
        var parts = new List<(string, string)>
        {
            (HeaderPart, Jsonc.Write(game.Header,
                "Vitomir save. One zip of readable parts; this one is what the load list shows.\n" +
                "'profile', 'rules' and 'mods' say what the game was played under: 'rules' names the switches that change the\n" +
                "game's rules and were on. 'notes' is yours: the game keeps it word for word.\n" +
                "Comments in these files are written by the game and rewritten every save.", strict: strict)),
        };
        if (game.Chronicle is { } c)
            parts.Add((ChroniclePart, Jsonc.Write(c,
                "The chronicle: where the story is. 'flags' are the chronicle flags that are set, by number;\n" +
                "'locations' is every location with its scene and its exits as the story has left them;\n" +
                "an exit with no 'to' leads nowhere.", strict: strict)));
        parts.Add((SessionPart, Jsonc.Write(game.Session,
            "What belongs to no one place: the party's gold, the quest log, the shops.\n" +
            "A shop does not keep its .nbs file name - 1.1 does not either - so the first visit after a load restocks it.", strict: strict)));
        parts.Add((PartyPart, Jsonc.Write(game.Party,
            "The party, leader first. Each member carries everything it has on it.", ThingNote, strict)));
        foreach (var (name, place) in game.World.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            parts.Add((WorldFolder + name + ".jsonc", Jsonc.Write(place,
                $"{name}: the level {place.Level}" + (place.Scene.Length > 0 ? $" with the scene {place.Scene}" : "") + ", as it was left.\n" +
                "A thing is filed under the scene and index that placed it (SCENE#12) or, if it was made while the game ran,\n" +
                "the place and the count (PLACE~3). A placed thing that is neither here nor in anybody's pack is gone.",
                ThingNote, strict)));
        return parts;
    }

    /// <summary>The previous version of a slot, kept beside it when it is saved over (<see cref="Write"/>).</summary>
    public static string PreviousOf(string path) => path + ".previous";

    /// <summary>
    /// Writes a save as one zip, **crash-safe** (ours): the new file is written beside the
    /// slot, forced to the disk and read back whole before anything is replaced, so a crash or a power
    /// cut part-way leaves the slot as it was; and the version it replaces is kept as
    /// <see cref="PreviousOf"/> — one bad save (a party saved dead, a save over the wrong slot) never
    /// costs a play-through. The load list does not show the previous versions; renaming one to the
    /// slot's own name brings it back.
    /// </summary>
    public static void Write(SaveGame game, string path)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (dir is not null) Directory.CreateDirectory(dir);
        string temp = path + ".writing";
        using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
                foreach (var (name, text) in Parts(game))
                {
                    var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                    using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                    w.Write(text);
                }
            file.Flush(flushToDisk: true);
        }
        Read(temp);                                                                 // it must read back, or the slot is not touched
        if (File.Exists(path)) File.Copy(path, PreviousOf(path), overwrite: true);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Writes the parts as a loose folder — the development form (<c>--save-plain</c>).</summary>
    public static void WritePlain(SaveGame game, string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var (name, text) in Parts(game))
        {
            string at = Path.Combine(folder, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(at)!);
            File.WriteAllText(at, text, new UTF8Encoding(false));
        }
    }

    // ---- reading -------------------------------------------------------------------------------

    /// <summary>Reads a save, a zip or a loose folder, leniently: comments and trailing commas are fine.</summary>
    public static SaveGame Read(string path)
    {
        if (Directory.Exists(path))
            return FromParts(name =>
            {
                string at = Path.Combine(path, name.Replace('/', Path.DirectorySeparatorChar));
                return File.Exists(at) ? File.ReadAllText(at) : null;
            }, Directory.EnumerateFiles(path, "*.jsonc", SearchOption.AllDirectories)
                        .Select(f => Path.GetRelativePath(path, f).Replace(Path.DirectorySeparatorChar, '/')));
        using var zip = ZipFile.OpenRead(path);
        var texts = zip.Entries.Where(e => e.Name.Length > 0).ToDictionary(e => e.FullName.Replace('\\', '/'), e =>
        {
            using var r = new StreamReader(e.Open(), Encoding.UTF8);
            return r.ReadToEnd();
        }, StringComparer.OrdinalIgnoreCase);
        return FromParts(n => texts.GetValueOrDefault(n), texts.Keys);
    }

    /// <summary>Just the header — what a load list needs, without reading the world.</summary>
    public static SaveHeader ReadHeader(string path)
    {
        if (Directory.Exists(path))
            return Jsonc.Read<SaveHeader>(File.ReadAllText(Path.Combine(path, HeaderPart)));
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry(HeaderPart) ?? throw new InvalidDataException($"{path} has no {HeaderPart}");
        using var r = new StreamReader(entry.Open(), Encoding.UTF8);
        return Jsonc.Read<SaveHeader>(r.ReadToEnd());
    }

    static SaveGame FromParts(Func<string, string?> part, IEnumerable<string> names)
    {
        var header = part(HeaderPart) is { } h ? Jsonc.Read<SaveHeader>(h)
            : throw new InvalidDataException($"a save needs a {HeaderPart}");
        if (header.Format > FormatVersion)
            throw new InvalidDataException($"this save is format {header.Format}; this build reads up to {FormatVersion}");
        var game = new SaveGame
        {
            Header = header,
            Chronicle = part(ChroniclePart) is { } c ? Jsonc.Read<ChronicleSave>(c) : null,
            Session = part(SessionPart) is { } s ? Jsonc.Read<SessionSave>(s) : new(),
            Party = part(PartyPart) is { } p ? Jsonc.Read<PartySave>(p) : new(),
        };
        foreach (var name in names)
        {
            if (!name.StartsWith(WorldFolder, StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".jsonc", StringComparison.OrdinalIgnoreCase)) continue;
            string location = name[WorldFolder.Length..^".jsonc".Length];
            if (part(name) is { } w) game.World[location] = Jsonc.Read<LocationSave>(w);
        }
        SaveGuids.Convert(game);                                        // a format-1 save's per-place GUIDs
        return game;
    }

    // ---- the comments --------------------------------------------------------------------------

    /// <summary>
    /// A line above each thing saying what it is, and the names of the bits beside a flags word —
    /// which is where a hand edit is most likely to go wrong.
    /// </summary>
    static Jsonc.Note ThingNote(string path, JsonNode? node)
    {
        string[] segs = path.Split('/');
        string last = segs[^1];
        // A thing's own record: under "things", "inventory" or the party's "members".
        if (node is JsonObject obj && segs.Length >= 2 && segs[^2] is "things" or "inventory" or "members")
            return new Jsonc.Note(Above: Describe(obj));
        if (node is JsonValue v && v.TryGetValue(out uint bits) && bits != 0)
        {
            string parent = segs.Length >= 2 ? segs[^2] : "";
            if (last == "flags" && parent == "actor") return new Jsonc.Note(Beside: Names(ActorFlags, bits));
            if (last == "flags2" && parent == "actor") return new Jsonc.Note(Beside: Names(ActorFlags2, bits));
            if (last == "flags" && parent is not "item" and not "actor" && segs.Length >= 3 && segs[^3] is "things" or "inventory" or "members")
                return new Jsonc.Note(Beside: Names(ThingFlags, bits));
            if (last == "renderFlags") return new Jsonc.Note(Beside: Names(RenderFlags, bits));
        }
        return Jsonc.Note.None;
    }

    static string Describe(JsonObject t)
    {
        string template = t["template"]?.GetValue<string>() ?? "?";
        if (t["actor"] is JsonObject a)
        {
            string name = a["name"]?.GetValue<string>() ?? "";
            float hp = a["health"]?.GetValue<float>() ?? 0f, of = a["maxHealth"]?.GetValue<float>() ?? 0f;
            return $"{template}{(name.Length > 0 && !name.Equals(template, StringComparison.OrdinalIgnoreCase) ? $" \"{name}\"" : "")}, {hp:0.#}/{of:0.#} health";
        }
        if (t["item"] is JsonObject i)
        {
            int amount = i["amount"]?.GetValue<int>() ?? 0;
            return amount > 1 ? $"{template} x{amount}" : template;
        }
        return template;
    }

    static readonly (string Name, uint Bit)[] ThingFlags = Bits(typeof(WorldThing.Flag));
    static readonly (string Name, uint Bit)[] ActorFlags = Bits(typeof(Actor.Flag));
    static readonly (string Name, uint Bit)[] ActorFlags2 = Bits(typeof(Actor.Flag2));
    static readonly (string Name, uint Bit)[] RenderFlags =
        [("HALO", 0x1), ("DONTRENDER", 0x2), ("DROPSHADOW", 0x4), ("FULLSHADOW", 0x8), ("FLUOROHALO", 0x10),
         ("TWINKLE", 0x20), ("SORTRENDER", 0x40), ("REFLECTIONONLY", 0x80), ("RECREATE", 0x100)];

    static (string, uint)[] Bits(Type holder) =>
        [.. holder.GetFields(BindingFlags.Public | BindingFlags.Static)
                  .Where(f => f.IsLiteral && f.FieldType == typeof(uint))
                  .Select(f => (f.Name.ToUpperInvariant(), (uint)f.GetRawConstantValue()!))
                  .Where(p => p.Item2 != 0 && (p.Item2 & (p.Item2 - 1)) == 0)];

    static string Names((string Name, uint Bit)[] table, uint bits)
    {
        var names = new List<string>();
        uint rest = bits;
        foreach (var (name, bit) in table)
            if ((bits & bit) != 0 && (rest & bit) != 0) { names.Add(name); rest &= ~bit; }
        if (rest != 0) names.Add($"0x{rest:x}");
        return string.Join(" | ", names);
    }
}
