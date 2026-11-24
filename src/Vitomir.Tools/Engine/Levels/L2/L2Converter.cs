// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Part of vitomir-tools.
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Levels.L2;

/// <summary>What one conversion wrote.</summary>
public sealed record L2ConvertResult(string Package, long NilBytes, long PackageBytes, int Sectors, int Anchors, int Exits,
                                     string[] Scenes, string[] UnattributedScenes);

/// <summary>
/// **A 1.1 level converted to L2** (ours; `vitomir --convert-l2`). The whole
/// level becomes one legacy cell carrying the NIL in the L1SC, L1PG and L1LM chunks (engine
/// units, the `.nil`'s own encoding, <see cref="L2Legacy"/>) and an ANCH chunk; the manifest gets
/// the bounds, materials and global light in metres, an anchor for every placed thing, start, spot
/// and sound of the level's scenes, and the chronicles' exits from this level's locations.
///
/// Anchors are recorded, not used: nothing at run time reads them yet. They are what a later phase
/// moves things by.
/// </summary>
public static class L2Converter
{
    /// <summary>The id of the one cell a converted level has.</summary>
    public const string LegacyCell = "legacy";

    /// <param name="anchors">
    /// Whether to read the level's scenes and chronicles for anchors and exits. Reading the GUIDs
    /// means loading the place once per scene, which is most of a conversion's time; the level's
    /// own model does not depend on it.
    /// </param>
    public static L2ConvertResult Convert(GameFiles files, string levelName, string outRoot, bool anchors = true)
    {
        var nilPath = $"levels/{levelName}.nil";
        var data = files.Read(nilPath);
        var level = NilLevel.Read(data, levelName + ".nil");
        var folder = L2Package.PathFor(outRoot, levelName);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);

        var (min, max) = Bounds(level);
        var cellEntry = new L2CellEntry
        {
            Id = LegacyCell, File = $"{L2Package.CellsFolder}/{LegacyCell}.cell", Region = "legacy",
            Bounds = new L2Box { Min = L2Manifest.Metres(min), Max = L2Manifest.Metres(max) },
        };
        var g = level.GlobalLight;
        var manifest = new L2Manifest
        {
            Name = levelName,
            Source = new L2Source
            {
                Kind = "nil", File = nilPath, FormatVersion = level.Version, Bytes = data.Length,
                Sha256 = System.Convert.ToHexStringLower(SHA256.HashData(data)),
            },
            Bounds = new L2Box { Min = cellEntry.Bounds.Min, Max = cellEntry.Bounds.Max },
            Cells = [cellEntry],
            Materials = [.. level.Materials],
            GlobalLight = new L2GlobalLight
            {
                Type = g.Type, Color = [g.Color.X, g.Color.Y, g.Color.Z], Direction = [g.Direction.X, g.Direction.Y, g.Direction.Z],
                Position = L2Manifest.Metres(g.Position), Intensity = g.Intensity, Ambient = [level.Ambient.X, level.Ambient.Y, level.Ambient.Z],
            },
        };
        manifest.Extras["legacyChunks"] = "L1SC, L1PG and L1LM hold the .nil's own records in engine units (72 per metre), not metres";

        string[] scenes = [], unattributed = [];
        if (anchors)
        {
            (scenes, unattributed) = ScenesOf(files, levelName);
            manifest.Anchors = Anchors(files, levelName, scenes);
            manifest.Exits = Exits(files, levelName);
            manifest.Extras["scenes"] = new JsonArray([.. scenes.Select(s => (JsonNode)s)]);
            if (unattributed.Length > 0)
                manifest.Extras["unattributedScenes"] = new JsonArray([.. unattributed.Select(s => (JsonNode)s)]);
        }

        var cell = new L2Cell { Chunks = [.. L2Legacy.Encode(level), L2AnchorChunk.Encode(manifest.Anchors)] };
        L2Package.Write(folder, manifest, new Dictionary<string, L2Cell> { [LegacyCell] = cell });
        long size = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        return new L2ConvertResult(folder, data.Length, size, level.Sectors.Length, manifest.Anchors.Count, manifest.Exits.Count,
                                   scenes, unattributed);
    }

    /// <summary>The box round every sector vertex, in engine units.</summary>
    static (Vector3 Min, Vector3 Max) Bounds(NilLevel level)
    {
        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
        foreach (var s in level.Sectors)
            foreach (var v in s.Vertices) { min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
        return min.X > max.X ? (Vector3.Zero, Vector3.Zero) : (min, max);
    }

    /// <summary>
    /// The scenes whose level is this one: the level's own scene (<c>LEVEL.nsd</c>), every scene a
    /// chronicle's <c>location</c> line starts this level in, and the chapter scenes named after the
    /// level (<c>P1_OTDY</c> → <c>OTDY_*.nsd</c>). Where two levels share that name (<c>P1_OLDT</c>
    /// and <c>P2_OLDT</c>), a chapter scene no chronicle names cannot be told apart by its name and
    /// is listed as unattributed instead.
    /// </summary>
    public static (string[] Scenes, string[] Unattributed) ScenesOf(GameFiles files, string levelName)
    {
        var all = files.List("scenes/").Where(p => p.EndsWith(".nsd", StringComparison.OrdinalIgnoreCase))
                       .Select(p => Path.GetFileNameWithoutExtension(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var levels = files.List("levels/").Where(p => p.EndsWith(".nil", StringComparison.OrdinalIgnoreCase))
                          .Select(p => Path.GetFileNameWithoutExtension(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var set = new List<string>();
        void Add(string s)
        {
            var found = all.FirstOrDefault(a => a.Equals(s, StringComparison.OrdinalIgnoreCase));
            if (found is not null && !set.Contains(found, StringComparer.OrdinalIgnoreCase)) set.Add(found);
        }
        Add(levelName);
        foreach (var (_, nsc) in Chronicles(files))
            foreach (var loc in nsc.Locations)
                if (loc.LevelName.Equals(levelName, StringComparison.OrdinalIgnoreCase) && loc.SceneName.Length > 0) Add(loc.SceneName);
        var unattributed = new List<string>();
        static string Stem(string level) => level.IndexOf('_') is var u and >= 0 ? level[(u + 1)..] : level;
        var stem = Stem(levelName);
        bool shared = levels.Count(l => Stem(l).Equals(stem, StringComparison.OrdinalIgnoreCase)) > 1;
        foreach (var s in all.Where(a => a.StartsWith(stem + "_", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (set.Contains(s, StringComparer.OrdinalIgnoreCase)) continue;
            if (shared) unattributed.Add(s); else set.Add(s);
        }
        return ([.. set], [.. unattributed]);
    }

    static IEnumerable<(string Name, NscChronicle Nsc)> Chronicles(GameFiles files)
    {
        foreach (var p in files.List("chronicles/").Where(p => p.EndsWith(".nsc", StringComparison.OrdinalIgnoreCase))
                               .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            NscChronicle nsc;
            try { nsc = NscChronicle.Read(files.Read(p), Path.GetFileName(p)); }
            catch (NodFormatException) { continue; }
            yield return (Path.GetFileNameWithoutExtension(p), nsc);
        }
    }

    /// <summary>
    /// Every exit of every chronicle location on this level, as the chronicle's reader leaves them:
    /// an <c>exit</c> line gives both ends unless it says ONEWAY, so a location's exits include the
    /// ones other locations' lines lead back through.
    /// </summary>
    static List<L2Exit> Exits(GameFiles files, string levelName)
    {
        var list = new List<L2Exit>();
        foreach (var (name, nsc) in Chronicles(files))
            foreach (var loc in nsc.Locations.Where(l => l.LevelName.Equals(levelName, StringComparison.OrdinalIgnoreCase)))
                for (int i = 0; i < loc.ExitCount && i < NscLocation.MaxExits; i++)
                    if (nsc.At(loc.Exits[i].To) is { } to)
                        list.Add(new L2Exit
                        {
                            Chronicle = name, Location = loc.Name, Number = i, To = to.Name, ToLevel = to.LevelName,
                            Entrance = loc.Exits[i].Entrance, Closed = loc.Exits[i].Closed,
                        });
        return list;
    }

    /// <summary>
    /// One anchor per entry of each scene's lists. A thing's kind is its template's TYPE; its GUID
    /// is the one it gets when the place is loaded with that scene on a fresh counter (the world
    /// scene's things first, then the chapter's, as <see cref="Location.Load"/> numbers them).
    /// </summary>
    static List<L2Anchor> Anchors(GameFiles files, string levelName, string[] scenes)
    {
        var list = new List<L2Anchor>();
        NsdScene? Scene(string s) => files.TryRead($"scenes/{s}.nsd", out var d) ? NsdScene.Read(d, s + ".nsd") : null;
        var world = scenes.FirstOrDefault(s => s.Equals(levelName, StringComparison.OrdinalIgnoreCase));
        int worldCount = world is not null ? Scene(world)?.Things.Length ?? 0 : 0;
        foreach (var name in scenes)
        {
            var scene = Scene(name);
            if (scene is null) continue;
            bool isWorld = name.Equals(world, StringComparison.OrdinalIgnoreCase);
            // Loaded once per scene for the numbers: the chapter scene with its level's world scene before it.
            IReadOnlyList<WorldThing> built = [];
            try { built = Location.Load(files, levelName, sky: false, scene: isWorld ? null : name, guids: new ThingGuids()).Drawn; }
            catch (Exception) { }   // a scene the place will not load with keeps its anchors, without GUIDs or kinds
            int offset = isWorld ? 0 : worldCount;
            for (int i = 0; i < scene.Things.Length; i++)
            {
                var t = scene.Things[i];
                var w = offset + i < built.Count && built[offset + i].Index == offset + i ? built[offset + i] : null;
                list.Add(new L2Anchor
                {
                    Id = $"{name}/things/{i}", Kind = w is null ? "thing" : Kind(w.Type), Scene = name, Index = i,
                    Name = t.Template, Template = t.Template, Guid = w?.Guid, Script = t.Script.Length > 0 ? t.Script : w?.Script is { Length: > 0 } s ? s : null,
                    Sector = t.Sector >= 0 ? t.Sector : null, Position = L2Manifest.Metres(t.Position),
                    Rotation = [t.Orientation.X, t.Orientation.Y, t.Orientation.Z],
                    Sectors = t.Sectors.Length > 0 ? t.Sectors : null, Cell = LegacyCell,
                });
            }
            for (int i = 0; i < scene.Starts.Length; i++) list.Add(Start(name, "start", "starts", i, scene.Starts[i]));
            for (int i = 0; i < scene.ConversationStarts.Length; i++)
                list.Add(Start(name, "conversation-start", "conversation-starts", i, scene.ConversationStarts[i]));
            for (int i = 0; i < scene.InterestSpots.Length; i++)
            {
                var s = scene.InterestSpots[i];
                list.Add(new L2Anchor
                {
                    Id = $"{name}/interest-spots/{i}", Kind = "interest-spot", Scene = name, Index = i, Name = $"interestspot{s.Group:00}",
                    Sector = s.Sector, Position = L2Manifest.Metres(s.Position), Rotation = [0, 0, Yaw(s.Rotation)], Group = s.Group, Cell = LegacyCell,
                });
            }
            for (int i = 0; i < scene.SectorSounds.Length; i++)
            {
                var s = scene.SectorSounds[i];
                list.Add(new L2Anchor
                {
                    Id = $"{name}/sector-sounds/{i}", Kind = "sector-sound", Scene = name, Index = i, Name = s.Wav,
                    Sectors = s.Sectors, Cell = LegacyCell,
                });
            }
            for (int i = 0; i < scene.PointSounds.Length; i++)
            {
                var s = scene.PointSounds[i];
                list.Add(new L2Anchor
                {
                    Id = $"{name}/point-sounds/{i}", Kind = "point-sound", Scene = name, Index = i, Name = s.Wav,
                    Position = L2Manifest.Metres(s.Position), Sectors = s.Sectors.Length > 0 ? s.Sectors : null, Cell = LegacyCell,
                });
            }
        }
        return list;
    }

    static L2Anchor Start(string scene, string kind, string listName, int i, NsdStart s) => new()
    {
        Id = $"{scene}/{listName}/{i}", Kind = kind, Scene = scene, Index = i,
        Name = s.Number > 0 ? $"{(kind == "start" ? "playerstart" : "convstart")}{s.Group:00}_{s.Number}" : $"{(kind == "start" ? "playerstart" : "convstart")}{s.Group:00}",
        Sector = s.Sector, Position = L2Manifest.Metres(s.Position), Rotation = [0, 0, Yaw(s.Rotation)], Group = s.Group, Cell = LegacyCell,
    };

    /// <summary>A start's yaw in degrees: its 3×3's element [1] is sin θ and [0] cos θ.</summary>
    static float Yaw(float[] m) => MathF.Atan2(m[1], m[0]) * (180f / MathF.PI);

    static string Kind(ThingType t) => t switch
    {
        ThingType.None => "thing",
        _ => t.ToString().ToLowerInvariant(),
    };
}
