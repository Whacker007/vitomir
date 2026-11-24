// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Formats.Tests;

/// <summary>
/// Every shipped file of each format must parse to exactly EOF with every internal cross-check
/// passing. The totals (files read, and the counts of what is in them) are the GOG 1.1 install's,
/// so a reader that starts skipping or misreading part of a file changes them.
/// </summary>
public class CorpusTests
{
    static string Report(Dictionary<string, string> failures) =>
        string.Join("\n", failures.Take(20).Select(kv => $"{kv.Key}: {kv.Value}"));

    [Fact]
    public void Nod_all_models()
    {
        using var nob = GameData.Open("Resource.nob");
        long verts = 0, faces = 0, groups = 0, bones = 0;
        var (parsed, failures) = GameData.ParseAll(nob, ".nod", NodModel.Read, (_, m) =>
        {
            verts += m.Vertices.Length; faces += m.NumFaces; groups += m.Groups.Length; bones += m.Bones.Length;
        });
        Assert.True(failures.Count == 0, Report(failures));
        Assert.Equal(1476, parsed);
        Assert.Equal((536_832L, 714_296L, 6_242L, 27_716L), (verts, faces, groups, bones));
    }

    [Fact]
    public void Nad_all_animations_except_the_v2_leftover()
    {
        using var nob = GameData.Open("Resource.nob");
        long tracks = 0, keys = 0, tags = 0;
        var (parsed, failures) = GameData.ParseAll(nob, ".nad", NadAnimation.Read, (_, a) =>
        {
            tracks += a.Tracks.Length; keys += a.Tracks.Sum(t => t.Keys.Length); tags += a.Tags.Length;
        });
        // test.nad is a version-2 dev leftover nothing references.
        Assert.Equal(["3d/Anims/test.nad"], failures.Keys);
        Assert.Equal(1423, parsed);
        Assert.Equal((55_467L, 626_233L, 1_056L), (tracks, keys, tags));
    }

    [Fact]
    public void Not_all_template_files()
    {
        using var nob = GameData.Open("Resource.nob");
        int templates = 0, tags = 0, slack = 0;
        var (parsed, failures) = GameData.ParseAll(nob, ".not", NotFile.Read, (_, f) =>
        {
            templates += f.Templates.Length; tags += f.Templates.Sum(t => t.Tags.Length); slack += f.SlackBytes;
        });
        Assert.True(failures.Count == 0, Report(failures));
        Assert.Equal(4, parsed);
        Assert.Equal((2_619, 20_924, 6_786), (templates, tags, slack));
    }

    [Fact]
    public void Nil_all_levels()
    {
        using var nob = GameData.Open("Levels.nob");
        long sectors = 0, groups = 0, surfaces = 0, portals = 0, lights = 0, nodes = 0, links = 0, pages = 0,
             materials = 0, verts = 0, sverts = 0, litPixels = 0;
        var (parsed, failures) = GameData.ParseAll(nob, ".nil", NilLevel.Read, (_, l) =>
        {
            sectors += l.Sectors.Length; materials += l.Materials.Length;
            foreach (var s in l.Sectors)
            {
                groups += s.Groups.Length; surfaces += s.Surfaces.Length; portals += s.Portals.Length;
                lights += s.Lights.Length; verts += s.Vertices.Length; sverts += s.SectorVertices.Length;
            }
            nodes += l.PathNodes.Length; links += l.PathNodes.Sum(n => n.Links.Length); pages += l.Lightmaps.Length;
            foreach (var lm in l.Lightmaps) foreach (var p in lm.Pixels) if (p != 0) litPixels++;
        });
        Assert.True(failures.Count == 0, Report(failures));
        Assert.Equal(112, parsed);
        Assert.Equal((7_712L, 84_815L, 835_471L, 32_552L, 67_509L), (sectors, groups, surfaces, portals, lights));
        Assert.Equal((43_083L, 135_319L, 774L, 3_986L), (nodes, links, pages, materials));
        Assert.Equal((1_211_615L, 3_837_567L, 35_146_846L), (verts, sverts, litPixels));
    }

    [Fact]
    public void Nsd_all_scenes()
    {
        using var nob = GameData.Open("Resource.nob");
        long things = 0, scripted = 0, prms = 0, keyframes = 0, models = 0, colors = 0,
             ssounds = 0, psounds = 0, starts = 0, spots = 0, convs = 0;
        var (parsed, failures) = GameData.ParseAll(nob, ".nsd", NsdScene.Read, (_, s) =>
        {
            things += s.Things.Length; scripted += s.Things.Count(t => t.Script.Length > 0);
            prms += s.Things.Sum(t => t.Params.Length); keyframes += s.Things.Sum(t => t.Keyframes.Length);
            models += s.Things.Count(t => t.Model is not null); colors += s.Things.Sum(t => t.VertexColors.Length);
            ssounds += s.SectorSounds.Length; psounds += s.PointSounds.Length; starts += s.Starts.Length;
            spots += s.InterestSpots.Length; convs += s.ConversationStarts.Length;
        });
        Assert.True(failures.Count == 0, Report(failures));
        Assert.Equal(291, parsed);
        Assert.Equal((15_942L, 1_066L, 3_111L, 2_058L, 1_132L, 390_453L), (things, scripted, prms, keyframes, models, colors));
        Assert.Equal((58L, 131L, 1_132L, 251L, 473L), (ssounds, psounds, starts, spots, convs));
    }

    [Fact]
    public void Ncp_all_camera_paths()
    {
        using var nob = GameData.Open("Resource.nob");
        int keys = 0;
        var (parsed, failures) = GameData.ParseAll(nob, ".ncp", NcpCameraPath.Read, (_, p) => keys += p.Keyframes.Length);
        Assert.True(failures.Count == 0, Report(failures));
        Assert.Equal(174, parsed);
        Assert.Equal(530, keys);
    }

    [Fact]
    public void Nms_all_motion_sets()
    {
        using var nob = GameData.Open("Resource.nob");
        var animations = new HashSet<string>(nob.NamesWithExtension(".nad").Select(Path.GetFileName)!,
                                             StringComparer.OrdinalIgnoreCase);
        int modes = 0, tags = 0, mouths = 0, heads = 0, stands = 0, missingAnimation = 0;
        var dropped = new List<string>();
        var (parsed, failures) = GameData.ParseAll(nob, ".nms",
            (data, name) => NmsMotionSet.Read(data, name), (name, set) =>
        {
            modes += set.Modes.Count; tags += set.Tags.Count; mouths += set.Mouths.Count;
            if (set.IsHead) heads++;
            if (set.Stand is not null) stands++;
            foreach (var d in set.Dropped) dropped.Add($"{name}: {d}");
            missingAnimation += set.Modes.Count(m => !animations.Contains(m.File));
        });
        Assert.True(failures.Count == 0, Report(failures));
        Assert.Equal(214, parsed);
        Assert.Equal((5_489, 1_101, 1_250), (modes, tags, mouths));
        Assert.Equal((7, 169), (heads, stands));
        // Three authoring slips, which the engine drops just as we do: a mouth line one number
        // short, a stray half-line, and a mode name ("attack") the engine has never had.
        Assert.Equal(3, dropped.Count);
        // Nine modes across five sets name an animation that does not ship; the engine drops those
        // modes when it cannot load them, so nothing in the game ever plays one.
        Assert.Equal(9, missingAnimation);
    }

    [Fact]
    public void Nui_all_panes_parse_without_warnings()
    {
        using var nob = GameData.Open("Local_Eng.nob");
        int quads = 0, elements = 0;
        var warnings = new List<string>();
        var (parsed, failures) = GameData.ParseAll(nob, ".nui", NuiPane.Read, (_, p) =>
        {
            quads += p.Quads.Count; elements += p.Elements.Count; warnings.AddRange(p.Warnings);
        });
        Assert.True(failures.Count == 0, Report(failures));
        Assert.Empty(warnings);
        Assert.Equal(63, parsed);
        Assert.Equal((506, 617), (quads, elements));
    }

    [Fact]
    public void Movespeed_is_on_the_locomotion_cycles_and_nowhere_else()
    {
        // The flag the 1.1 engine never reads. What it was *for* is
        // not in doubt, because across all 214 shipped sets it appears on exactly three mode
        // families and on nothing else in the game.
        using var nob = GameData.Open("Resource.nob");
        var carried = new Dictionary<string, int>();
        var total = new Dictionary<string, int>();
        int modes = 0;
        var without = new List<string>();
        foreach (var name in nob.NamesWithExtension(".nms"))
        {
            var set = NmsMotionSet.Read(nob.Read(name), Path.GetFileName(name)!);
            foreach (var mode in set.Modes)
            {
                modes++;
                string family = mode.Name.Split('_')[0];
                total[family] = total.GetValueOrDefault(family) + 1;
                if (mode.Has(NmsFlags.MoveSpeed)) carried[family] = carried.GetValueOrDefault(family) + 1;
                else if (family is "walk" or "run" or "limp") without.Add($"{set.Name}:{mode.Name}");
            }
        }
        Assert.Equal(5_489, modes);      // the reader's own total, pinned by NmsTests
        Assert.Equal(new[] { "limp", "run", "walk" }, carried.Keys.OrderBy(k => k).ToArray());
        Assert.Equal((479, 482), (carried["walk"], total["walk"]));
        Assert.Equal((479, 482), (carried["run"], total["run"]));
        Assert.Equal((306, 306), (carried["limp"], total["limp"]));
        // The six that go without are three sets: a bat whose walk and run are the same flight
        // loop, and two dev leftovers whose `run` points at a jump.
        Assert.Equal(6, without.Count);
        Assert.Equal(new[] { "batflying", "praguesoldier", "test" },
                     without.Select(w => w.Split(':')[0].ToLowerInvariant()).Distinct().Order().ToArray());
    }

    [Fact]
    public void A_mode_listed_more_than_once_is_a_variant_and_the_game_is_full_of_them()
    {
        // 1.1 rolls between the consecutive entries of a mode, which is what gives a character
        // more than one swing. The count matters because it is what the roll is over.
        using var nob = GameData.Open("Resource.nob");
        int doubled = 0, special = 0, locomotion = 0;
        foreach (var name in nob.NamesWithExtension(".nms"))
        {
            var set = NmsMotionSet.Read(nob.Read(name), Path.GetFileName(name)!);
            foreach (var group in set.Modes.GroupBy(m => (m.Kind, m.Index)).Where(g => g.Count() > 1))
            {
                doubled++;
                if (group.Any(m => m.Has(NmsFlags.Special))) special++;
                if (group.Key.Index is 1 or 17 or 33 or 195) locomotion++;
            }
            special += 0;
        }
        Assert.Equal(142, doubled);
        // Only three sets double a locomotion mode, and each time both lines name the same file —
        // a copy-and-paste slip, so the roll cannot make a walk stutter.
        Assert.Equal(3, locomotion);
    }
}
