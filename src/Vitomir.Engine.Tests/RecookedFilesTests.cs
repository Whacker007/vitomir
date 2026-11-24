// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text;
using Vitomir.Engine.Vfs;
using Vitomir.Formats;
using Vitomir.Platform.PlayerModules;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The recooked base game as the game reads it** (ours, <see cref="Recooked"/>): its text read as the original's files, over the
/// archives; a change to it read; a module's single template's text over it, the rest kept and in order; a module's original binary
/// read whole as ever; a scene with a thing taken out, its references to the things after it kept; and a folder with no marker
/// passed over.
/// </summary>
public sealed class RecookedFilesTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), $"vitomir-recooked-{Guid.NewGuid():n}");
    static readonly string? Game = GameFiles.Locate();

    public RecookedFilesTests() => Directory.CreateDirectory(root);
    public void Dispose() { try { Directory.Delete(root, recursive: true); } catch (IOException) { } }

    string Made()
    {
        Assert.SkipWhen(Game is null, "game install not found (set VTMR_GAME_DIR to the folder holding Resource.nob)");
        string to = Path.Combine(root, Recooked.FolderName);
        var r = Recooked.Make(Game!, to, "test");
        Assert.Equal((4, 2619, 291, 174, 24), (r.TemplateFiles, r.Templates, r.Scenes, r.Paths, r.TextFiles));
        return to;
    }

    /// <summary>The game as the original's files alone: no recooked folder, even when the imported data has one beside it.</summary>
    GameFiles Original(IReadOnlyList<PlayerModuleLayer>? modules = null) => new(Game!, modules: modules, recookedFolder: Path.Combine(root, "none"));

    static NotFile Templates(GameFiles files, string which = "darkAges") => NotFile.Read(files.Read($"misc/Templates/{which}.not"), which);

    PlayerModuleLayer Module(string id, params (string Path, byte[] Data)[] files)
    {
        string data = Path.Combine(root, "modules", id, "Data");
        foreach (var (path, bytes) in files)
        {
            string full = Path.Combine(data, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, bytes);
        }
        return new PlayerModuleLayer(id, "1.0.0", new string(id[0], 64), PackKinds.World, data, []);
    }

    [Fact]
    public void The_recooked_text_is_read_as_the_original_files()
    {
        string rc = Made();
        using var original = Original();
        using var recooked = new GameFiles(Game!, recookedFolder: rc);
        Assert.Null(original.RecookedFolder);
        Assert.Equal(rc, recooked.RecookedFolder);
        foreach (var which in new[] { "Global", "darkAges", "modernDay", "Multiplayer" })
        {
            var (a, b) = (Templates(original, which), Templates(recooked, which));
            Assert.Equal(a.Templates.Select(t => t.Name), b.Templates.Select(t => t.Name));
            for (int i = 0; i < a.Templates.Length; i++)
                Assert.Equal(a.Templates[i].Tags.Select(TagTable.Meaningful).Select(Convert.ToHexString), b.Templates[i].Tags.Select(t => Convert.ToHexString(t.Raw)));
        }
        var scene = NsdScene.Read(recooked.Read("scenes/P1_HAVN.nsd"));
        Assert.Equal(NsdScene.Read(original.Read("scenes/P1_HAVN.nsd")).Things.Length, scene.Things.Length);
        Assert.Equal(NcpCameraPath.Read(original.Read("paths/intro.ncp")).Keyframes.Length, NcpCameraPath.Read(recooked.Read("paths/intro.ncp")).Keyframes.Length);
    }

    [Fact]
    public void A_change_to_a_template_s_text_is_read_and_a_module_s_template_goes_over_it_alone()
    {
        string rc = Made();
        string boss = Path.Combine(rc, "misc", "Templates", "darkAges", "NosferatuBoss.txt");
        var text = File.ReadAllText(boss, Encoding.Latin1).Replace("ACTOR_HEALTH = 200", "ACTOR_HEALTH = 250");
        File.WriteAllText(boss, text, Encoding.Latin1);
        using (var files = new GameFiles(Game!, recookedFolder: rc))
            Assert.Equal(250u, Templates(files).ByName["NosferatuBoss"].Tag(TagTable.Of("ACTOR_HEALTH")!.Id)!.Value.AsUInt());

        // A module that carries one template's text: that one is the module's, every other the recooked one's, the order kept.
        var module = Module("bossmod", ("misc/Templates/darkAges/NosferatuBoss.txt", Encoding.Latin1.GetBytes(
            "PARENT = Nosferatu_DA\r\nACTOR_HEALTH = 999\r\nTREASURECLASS = 249\r\n")));
        using var modded = new GameFiles(Game!, modules: [module], recookedFolder: rc);
        using var plain = Original();
        var (m, p) = (Templates(modded), Templates(plain));
        Assert.Equal(p.Templates.Select(t => t.Name), m.Templates.Select(t => t.Name));
        var mine = m.ByName["NosferatuBoss"];
        Assert.Equal(["PARENT", "ACTOR_HEALTH", "TREASURECLASS"], mine.Tags.Select(t => TagTable.NameOf(t.Id)));
        Assert.Equal(249u, m.Resolve("NosferatuBoss", Templates(modded, "Global"))[TagTable.Of("TREASURECLASS")!.Id].AsUInt());
        Assert.Equal(p.ByName["Wilhem"].Tags.Length, m.ByName["Wilhem"].Tags.Length);

        // A template the module adds goes after the others.
        var adding = Module("adding", ("misc/Templates/darkAges/MyNewSword.txt", Encoding.Latin1.GetBytes("PARENT = broadSword\r\nWEAPON_DAMAGE1 = 55\r\n")));
        using var added = new GameFiles(Game!, modules: [adding], recookedFolder: rc);
        Assert.Equal("MyNewSword", Templates(added).Templates[^1].Name);
    }

    /// <summary>The text data files are in it laid out as tables, and read from it: as the original reads, a change to one read, a module's over it.</summary>
    [Fact]
    public void The_text_data_files_are_laid_out_in_it_and_read_from_it()
    {
        string rc = Made();
        using (var original = Original())
        using (var recooked = new GameFiles(Game!, recookedFolder: rc))
            foreach (var path in original.List("misc/").Where(TextTable.Knows).Where(p => p.IndexOf('/', 5) < 0))
            {
                var laid = recooked.Read(path);
                Assert.Equal(TextTable.Checked(original.Read(path), out _), laid);                     // every one passes the check
                Assert.Equal(FormatCommands.Reading(path, original.Read(path)), FormatCommands.Reading(path, laid));
            }
        string ntt = Path.Combine(rc, "misc", "game.ntt");
        var edited = Encoding.Latin1.GetString(File.ReadAllBytes(ntt)).Replace("GSTATSTRENGTH ", "GSTATSTRENGTX ");
        File.WriteAllBytes(ntt, Encoding.Latin1.GetBytes(edited));
        using (var recooked = new GameFiles(Game!, recookedFolder: rc))
            Assert.Contains("GSTATSTRENGTX", Encoding.Latin1.GetString(recooked.Read("misc/game.ntt")));
        var mine = Encoding.Latin1.GetBytes("GROUP: MINE ef_stat_wits\r\n");
        using (var moded = new GameFiles(Game!, modules: [Module("mine", ("misc/game.ntt", mine))], recookedFolder: rc))
            Assert.Equal(mine, moded.Read("misc/game.ntt"));
    }

    [Fact]
    public void A_module_s_original_binary_is_read_whole_as_ever()
    {
        string rc = Made();
        using var plain = Original();
        var original = Templates(plain);
        var sword = original.ByName["broadSword"];
        var only = NotFileWrite.Write([sword]);
        using var files = new GameFiles(Game!, modules: [Module("old", ("misc/Templates/darkAges.not", only))], recookedFolder: rc);
        Assert.Equal([sword.Name], Templates(files).Templates.Select(t => t.Name));
        // ... unless a module over it carries a template's text: then that one goes over the binary's.
        var over = Module("over", ("misc/Templates/darkAges/" + NotText.FileName(sword.Name), Encoding.Latin1.GetBytes("PARENT = _Swords\r\nWEAPON_DAMAGE1 = 77\r\n")));
        using var both = new GameFiles(Game!, modules: [Module("old2", ("misc/Templates/darkAges.not", only)), over], recookedFolder: rc);
        Assert.Equal("77", TagTable.Write(Templates(both).ByName["broadSword"].Tag(TagTable.Of("WEAPON_DAMAGE1")!.Id)!.Value));
    }

    [Fact]
    public void A_thing_taken_out_of_a_scene_keeps_the_references_to_the_others()
    {
        string rc = Made();
        string path = Path.Combine(rc, "scenes", "P1_HAVN.txt");
        using var plain = Original();
        var before = NsdScene.Read(plain.Read("scenes/P1_HAVN.nsd"));
        // Take out the first thing that no parameter names.
        var named = before.Things.SelectMany(t => t.Params).Where(p => NsdParam.IsThingReference(p.Type) && p.Bits >= 0).Select(p => p.Bits).ToHashSet();
        int gone = Enumerable.Range(0, before.Things.Length).First(i => !named.Contains(i) && before.Things[i].ModelBytes is null);
        var lines = File.ReadAllLines(path, Encoding.Latin1).ToList();
        int at = lines.IndexOf($"[thing {gone}]");
        int end = lines.FindIndex(at + 1, l => l.StartsWith('['));
        lines.RemoveRange(at, (end < 0 ? lines.Count : end) - at);
        File.WriteAllLines(path, lines, Encoding.Latin1);
        using var files = new GameFiles(Game!, recookedFolder: rc);
        var after = NsdScene.Read(files.Read("scenes/P1_HAVN.nsd"));
        Assert.Equal(before.Things.Length - 1, after.Things.Length);
        var left = Enumerable.Range(0, before.Things.Length).Where(i => i != gone).ToList();
        for (int i = 0; i < after.Things.Length; i++)
        {
            var (was, now) = (before.Things[left[i]], after.Things[i]);
            Assert.Equal(was.Template, now.Template);
            for (int k = 0; k < was.Params.Length; k++)
                if (NsdParam.IsThingReference(was.Params[k].Type) && was.Params[k].Bits >= 0)
                    Assert.Equal(before.Things[was.Params[k].Bits].Position, after.Things[now.Params[k].Bits].Position);
        }
    }

    /// <summary>A module's recooked text is the game's data at its import, not a document set aside; a readme is still a readme.</summary>
    [Fact]
    public void A_module_s_text_is_taken_in_as_data()
    {
        foreach (var path in new[] { "misc/Templates/darkAges/NosferatuBoss.txt", "scenes/P1_HAVN.txt", "paths/intro.txt", "scenes/P1_HAVN.geometry" })
        {
            var c = PackFiles.Classify(path);
            Assert.True(c.Class == PackFileClass.Data && c.Kind == PackKinds.World, $"{path}: {c.Class} {c.Kind}");
        }
        Assert.Equal(PackFileClass.Document, PackFiles.Classify("readme.txt").Class);
        Assert.Equal(PackFileClass.Document, PackFiles.Classify("scenes/notes/readme.txt").Class);
        Assert.Equal("a template", PackFiles.What("misc/Templates/darkAges/NosferatuBoss.txt"));
        Assert.Equal("a scene", PackFiles.What("scenes/P1_HAVN.txt"));
    }

    [Fact]
    public void A_folder_with_no_marker_is_not_read()
    {
        string rc = Made();
        File.Delete(Path.Combine(rc, Recooked.MarkerName));
        using var files = new GameFiles(Game!, recookedFolder: rc);
        Assert.Null(files.RecookedFolder);
        Assert.Equal(Path.Combine(root, "Imported", Recooked.FolderName), GameFiles.RecookedBeside(Path.Combine(root, "Imported", "vtmr-1.1")));
    }
}
