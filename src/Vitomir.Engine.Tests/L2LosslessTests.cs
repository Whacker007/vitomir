// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using System.Text.Json.Nodes;
using Vitomir.Engine.Levels.L2;
using Vitomir.Engine.World;
using Vitomir.Formats;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **1.1's levels converted to L2, and nothing lost** (ours). Every shipped
/// level is converted into the temporary folder and read back from its package, and the model it
/// gives is compared with the `.nil`'s field by field (<see cref="ModelComparer"/>): every plane,
/// tree node, vertex, surface, portal, light, path link and lightmap pixel. The pilot, the daytime
/// Old Town, is converted with its anchors, which must name every placed thing of its three scenes
/// with a GUID of its own. A package is only read when an L2 folder is given — a level whose
/// package was altered shows the alteration with the folder, and the `.nil` without it — and a
/// chunk the loader does not know is skipped.
///
/// It converts all 112 levels (12 s on two cores, some 99 million fields compared), so the class runs on its own. The
/// packages are deleted afterwards.
/// </summary>
[Collection(nameof(L2LosslessTests))]
[CollectionDefinition(nameof(L2LosslessTests), DisableParallelization = true)]
public class L2LosslessTests
{
    static string[] Levels(Vfs.GameFiles files) =>
        [.. files.List("levels/").Where(n => n.EndsWith(".nil", StringComparison.OrdinalIgnoreCase))
                 .Select(n => Path.GetFileNameWithoutExtension(n)).Distinct(StringComparer.OrdinalIgnoreCase).Order()];

    static string TempRoot() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"vitomir-l2-{Guid.NewGuid():N}")).FullName;

    [Fact]
    public void Every_shipped_level_read_back_from_its_package_is_the_nil_field_for_field()
    {
        using var files = Install.Open();
        var root = TempRoot();
        try
        {
            var clock = Stopwatch.StartNew();
            var cmp = new ModelComparer();
            var names = Levels(files);
            foreach (var name in names)
            {
                var r = L2Converter.Convert(files, name, root, anchors: false);
                var fromL2 = L2Levels.Load(r.Package, name);
                var fromNil = NilLevel.Read(files.Read($"levels/{name}.nil"), name + ".nil");
                cmp.Compare(fromNil, fromL2, name);
                Assert.Empty(cmp.Differences);
                Directory.Delete(r.Package, recursive: true);
            }
            Assert.Equal(112, names.Length);
            Assert.True(cmp.Fields > 50_000_000, $"{cmp.Fields:N0} fields compared");
            Console.WriteLine($"L2: {names.Length} levels, {cmp.Fields:N0} fields compared, none differing, {clock.Elapsed.TotalSeconds:0} s");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void The_daytime_Old_Town_converts_with_an_anchor_for_everything_its_scenes_place()
    {
        using var files = Install.Open();
        Assert.SkipUnless(files.TryRead("levels/P1_OTDY.nil", out _), "no P1_OTDY in the install");
        var root = TempRoot();
        try
        {
            var r = L2Converter.Convert(files, "P1_OTDY", root);
            Assert.Equal(["P1_OTDY", "OTDY_4_1", "OTDY_6_1"], r.Scenes);
            var package = L2Package.Open(r.Package);
            var m = package.Manifest;
            int expected = 0, things = 0;
            foreach (var s in r.Scenes)
            {
                var scene = NsdScene.Read(files.Read($"scenes/{s}.nsd"), s);
                things += scene.Things.Length;
                expected += scene.Things.Length + scene.Starts.Length + scene.ConversationStarts.Length + scene.InterestSpots.Length
                          + scene.SectorSounds.Length + scene.PointSounds.Length;
            }
            Assert.Equal(expected, m.Anchors.Count);
            Assert.Equal(m.Anchors.Count, m.Anchors.Select(a => a.Id).Distinct().Count());
            var thingAnchors = m.Anchors.Where(a => a.Id.Contains("/things/")).ToArray();
            Assert.Equal(things, thingAnchors.Length);
            Assert.All(thingAnchors, a => Assert.NotNull(a.Guid));
            // The world scene's things are numbered the same whichever chapter comes after them, and
            // the chapters' numbers follow; within one load no two things share a number.
            foreach (var chapter in thingAnchors.GroupBy(a => a.Scene).Where(g => g.Key != "P1_OTDY"))
                Assert.Equal(chapter.Count(), chapter.Select(a => a.Guid).Distinct().Count());
            Assert.Contains(thingAnchors, a => a.Kind == "actor");
            Assert.Contains(m.Anchors, a => a.Kind == "start");
            // The exits are Old Town's, from the Prague chronicle.
            Assert.NotEmpty(m.Exits);
            Assert.All(m.Exits, e => Assert.Equal("OldTownDay", e.Location));
            // Metres in the manifest: the level is tens of metres across, not thousands.
            Assert.InRange(m.Bounds.Max[0] - m.Bounds.Min[0], 20, 200);
            Assert.Equal(72f, m.Units.EngineUnitsPerMetre);
            // The cell's ANCH chunk holds the same anchors.
            var cell = package.ReadCell(m.Cells.Single());
            var inCell = L2AnchorChunk.Decode(cell.Need(L2AnchorChunk.Tag, "legacy"), "legacy");
            Assert.Equal(m.Anchors.Select(a => (a.Id, a.Guid, a.Position[0], a.Position[1], a.Position[2])),
                         inCell.Select(a => (a.Id, a.Guid, a.Position[0], a.Position[1], a.Position[2])));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_package_is_read_only_when_an_L2_folder_is_given_and_unknown_chunks_are_skipped()
    {
        using var files = Install.Open();
        const string name = "P1_MON1";
        Assert.SkipUnless(files.TryRead($"levels/{name}.nil", out var data), $"no {name} in the install");
        var root = TempRoot();
        var before = L2Levels.Folder;
        try
        {
            var r = L2Converter.Convert(files, name, root, anchors: false);
            // Alter the package's level (its ambient colour) and give its cell a chunk nobody knows.
            var nil = NilLevel.Read(data, name);
            var altered = new NilLevel
            {
                Version = nil.Version, GlobalLight = nil.GlobalLight, Ambient = nil.Ambient + new System.Numerics.Vector3(1, 2, 3),
                Materials = nil.Materials, Sectors = nil.Sectors, PathNodes = nil.PathNodes, Lightmaps = nil.Lightmaps,
            };
            var cellPath = Path.Combine(r.Package, L2Package.CellsFolder, L2Converter.LegacyCell + ".cell");
            var cell = L2Cell.Read(File.ReadAllBytes(cellPath), cellPath);
            var chunks = cell.Chunks.Where(c => !c.Tag.StartsWith("L1")).ToList();
            File.WriteAllBytes(cellPath, new L2Cell { Chunks = [new("ZZZZ", 3, [1, 2, 3]), .. L2Legacy.Encode(altered), .. chunks] }.Write());
            // And its manifest a property nobody knows.
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(r.Package, L2Package.ManifestFile)))!.AsObject();
            json["fromTheFuture"] = true;
            File.WriteAllText(Path.Combine(r.Package, L2Package.ManifestFile), json.ToJsonString());

            L2Levels.Folder = null;
            Environment.SetEnvironmentVariable(L2Levels.Variable, null);
            Assert.Equal(nil.Ambient, Location.Load(files, name, things: false, sky: false).Level.Ambient);
            L2Levels.Folder = root;
            Assert.Equal(altered.Ambient, Location.Load(files, name, things: false, sky: false).Level.Ambient);
            // A level with no package in the folder is still its .nil.
            Assert.Equal(NilLevel.Read(files.Read("levels/P1_HAVN.nil")).Ambient, Location.Load(files, "P1_HAVN", things: false, sky: false).Level.Ambient);
        }
        finally
        {
            L2Levels.Folder = before;
            Directory.Delete(root, recursive: true);
        }
    }
}
