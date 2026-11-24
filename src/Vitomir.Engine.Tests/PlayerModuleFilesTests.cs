// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.IO.Compression;
using Vitomir.Engine.Net;
using Vitomir.Engine.Save;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.Video;
using Vitomir.Engine.World;
using Vitomir.Platform.PlayerModules;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The player's modules in the game's files** (<see cref="GameFiles"/>), on a stand-in game folder and modules made by the test:
/// a later module wins, one not given changes nothing, a module's archive is mounted at its place, the developer's data folders stay
/// over them, a module's film comes first and an empty one is skipped, a module's chronicle is listed as 1.1's are; the data's
/// fingerprint leaves the modules out and the network game's identity carries the rules modules, refusing a join whose list differs
/// and allowing different look modules; a save names its rules modules and a load with others warns, naming the difference.
/// </summary>
public sealed class PlayerModuleFilesTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("vitomir-module-files-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    static void Put(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
    }

    static byte[] Text(string s) => System.Text.Encoding.ASCII.GetBytes(s);

    static void Nob(string path, params (string Name, byte[] Data)[] files)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, data) in files)
        {
            using var s = zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
            s.Write(data);
        }
    }

    /// <summary>A stand-in game folder: an archive with a wall and a template, a loose film.</summary>
    string Game()
    {
        string dir = Path.Combine(root, "game");
        if (!Directory.Exists(dir))
        {
            Nob(Path.Combine(dir, "Resource.nob"), ("materials/Prague/Wall.TGA", Text("base wall")), ("misc/templates/x.not", Text("base templates")));
            Put(Path.Combine(dir, "Video", "logo.bik"), Text("BIKi base film"));
        }
        return dir;
    }

    /// <summary>A module's layer: its Data folder with <paramref name="files"/>, of the kinds given.</summary>
    PlayerModuleLayer Module(string id, PackKinds kinds, params (string Path, byte[] Data)[] files)
    {
        string data = Path.Combine(root, "modules", id, "Data");
        Directory.CreateDirectory(data);
        foreach (var (path, bytes) in files) Put(Path.Combine(data, path.Replace('/', Path.DirectorySeparatorChar)), bytes);
        return new PlayerModuleLayer(id, "1.0.0", new string(id[0], 64), kinds, data, []);
    }

    static string Read(GameFiles files, string path) => System.Text.Encoding.ASCII.GetString(files.Read(path));

    [Fact]
    public void A_later_module_wins_one_not_given_changes_nothing_and_the_data_folders_stay_over_them()
    {
        var a = Module("a", PackKinds.Look, ("materials/Prague/Wall.TGA", Text("a's wall")), ("materials/Prague/new.tga", Text("a's new")));
        var b = Module("b", PackKinds.Look, ("MATERIALS/prague/wall.tga", Text("b's wall")));
        using (var none = new GameFiles(Game()))
            Assert.Equal("base wall", Read(none, "materials/Prague/Wall.TGA"));
        using (var onlyA = new GameFiles(Game(), null, [a]))
        {
            Assert.Equal("a's wall", Read(onlyA, "materials/Prague/Wall.TGA"));
            Assert.Equal("base templates", Read(onlyA, "misc/templates/x.not"));
        }
        using (var both = new GameFiles(Game(), null, [a, b]))
        {
            Assert.Equal("b's wall", Read(both, "materials/prague/wall.tga"));
            Assert.Equal("a's new", Read(both, "materials/Prague/new.tga"));
            Assert.Contains("materials/Prague/new.tga", both.List("materials/"));
            Assert.Equal("materials/Prague/new.tga", both.FindByFileName("new.tga"));
        }
        using (var reversed = new GameFiles(Game(), null, [b, a]))
            Assert.Equal("a's wall", Read(reversed, "materials/Prague/Wall.TGA"));
        string developer = Path.Combine(root, "dev");
        Put(Path.Combine(developer, "materials", "Prague", "Wall.TGA"), Text("the developer's wall"));
        using var over = new GameFiles(Game(), [developer], [a, b]);
        Assert.Equal("the developer's wall", Read(over, "materials/Prague/Wall.TGA"));
    }

    [Fact]
    public void A_modules_archive_is_mounted_at_its_place_in_the_order()
    {
        var loose = Module("loose", PackKinds.World, ("misc/templates/x.not", Text("loose templates")));
        var archived = Module("archived", PackKinds.World);
        string nob = Path.Combine(archived.DataFolder, "Rabbit.nob");
        Nob(nob, ("misc/templates/x.not", Text("the archive's templates")), ("misc/templates/rabbit.not", Text("rabbit")));
        archived = archived with { Archives = [nob] };
        using (var later = new GameFiles(Game(), null, [loose, archived]))
        {
            Assert.Equal("the archive's templates", Read(later, "misc/templates/x.not"));
            Assert.Equal("rabbit", Read(later, "misc/templates/rabbit.not"));
            Assert.DoesNotContain(later.List(""), n => n.EndsWith("Rabbit.nob", StringComparison.OrdinalIgnoreCase));   // the archive is no file of the game's
        }
        using var earlier = new GameFiles(Game(), null, [archived, loose]);
        Assert.Equal("loose templates", Read(earlier, "misc/templates/x.not"));
    }

    [Fact]
    public void A_modules_film_comes_first_and_an_empty_one_takes_the_film_out()
    {
        using (var none = new GameFiles(Game()))
            Assert.Equal(Path.Combine(Game(), "Video", "logo.bik"), Films.Find(none, "logo.bik"));
        var film = Module("film", PackKinds.Look, ("Video/Logo.bik", Text("BIKi a module's film")));
        using (var with = new GameFiles(Game(), null, [film]))   // Windows' file system answers in the case asked for, Linux's index in the file's
            Assert.Equal(Path.Combine(film.DataFolder, "Video", "Logo.bik"), Films.Find(with, "logo.bik"), ignoreCase: OperatingSystem.IsWindows());
        var empty = Module("empty", PackKinds.Look, ("Video/Logo.bik", []));
        using (var taken = new GameFiles(Game(), null, [film, empty]))
            Assert.Null(Films.Find(taken, "logo"));
        var blanked = Module("blanked", PackKinds.Look, ("Video/LOGO.BIK", Text("#Dummy.bik")));   // a JSGME-era pack's blank: not a Bink film
        using var dummy = new GameFiles(Game(), null, [film, blanked]);
        Assert.Null(Films.Find(dummy, "logo.bik"));
    }

    [Fact]
    public void A_modules_chronicle_is_listed_as_1_1s_are()
    {
        var install = GameFiles.LocateInstall(Environment.GetEnvironmentVariable("VTMR_GAME_DIR"));
        Assert.SkipWhen(install is null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        using var original = new GameFiles(install!);
        var source = original.List("chronicles/").First(n => n.EndsWith(".nsc", StringComparison.OrdinalIgnoreCase));
        // A fan chronicle reusing 1.1's assets: here, one of 1.1's own chronicles under a new name, made at the test's time.
        var fan = Module("fan", PackKinds.World, ("Chronicles/FanMade.nsc", original.Read(source)));
        using var files = new GameFiles(install!, null, [fan]);
        var set = ChronicleSet.Load(files);
        Assert.Equal(ChronicleSet.Load(original).All.Count + 1, set.All.Count);
        Assert.Contains("chronicles/FanMade.nsc", files.List("chronicles/"), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_fingerprint_leaves_the_modules_out_and_the_identity_carries_the_rules_modules()
    {
        var look = Module("look", PackKinds.Look, ("materials/Prague/Wall.TGA", Text("look's wall")));
        var rules = Module("rules", PackKinds.World | PackKinds.Look, ("misc/templates/x.not", Text("rules' templates")));
        using var none = new GameFiles(Game());
        using var lookOnly = new GameFiles(Game(), null, [look]);
        using var both = new GameFiles(Game(), null, [look, rules]);
        Assert.Equal(none.Fingerprint(), lookOnly.Fingerprint());
        Assert.Equal(none.Fingerprint(), both.Fingerprint());
        Assert.Empty(lookOnly.RulesModules);
        Assert.Equal([$"rules 1.0.0 {new string('r', 16)}"], both.RulesModules);

        var plain = NetIdentity.Of(none) with { Build = "b", BuildName = "Vitomir" };
        var looks = NetIdentity.Of(lookOnly) with { Build = "b", BuildName = "Vitomir" };
        var ruled = NetIdentity.Of(both) with { Build = "b", BuildName = "Vitomir" };
        Assert.Null(looks.Differs(plain));                                                // look modules are each player's own
        Assert.Equal("The host plays with other rules modules: rules 1.0.0 is on here and not at the host. Both must have the same rules modules on, "
            + "in the same order (look modules - textures, sounds, text, films - may differ).", ruled.Differs(plain));
        Assert.Contains("rules 1.0.0 is on at the host and not here", plain.Differs(ruled), StringComparison.Ordinal);
        var other = ruled with { Modules = [$"rules 1.1.0 {new string('r', 16)}"] };
        Assert.Contains("rules is 1.0.0 at the host and 1.1.0 here", other.Differs(ruled), StringComparison.Ordinal);
        var changed = ruled with { Modules = [$"rules 1.0.0 {new string('x', 16)}"] };
        Assert.Contains("rules 1.0.0 has other files or interface bindings at the host than here", changed.Differs(ruled), StringComparison.Ordinal);

        // On the wire: a Hello with no module is as it was; one with modules reads back with them.
        var w = new NetWriter(Msg.Hello);
        plain.Write(w);
        var old = new NetWriter(Msg.Hello).Str(plain.Build).Str(plain.BuildName).Str(plain.Data).I32(plain.DataFiles).Str(plain.Store);
        Assert.Equal(old.ToArray(), w.ToArray());
        var w2 = new NetWriter(Msg.Hello);
        ruled.Write(w2);
        var read = NetIdentity.Read(new NetReader(w2.ToArray()));
        Assert.Equal(ruled.Data, read.Data);
        Assert.Equal(ruled.Modules, read.Modules);
        Assert.Null(read.Differs(ruled));
    }

    /// <summary>A NOD model with no geometry, naming one material.</summary>
    static byte[] Nod(string material)
    {
        var s = new MemoryStream();
        var w = new BinaryWriter(s);
        w.Write(7u);
        w.Write(1);
        var name = new byte[32];
        System.Text.Encoding.ASCII.GetBytes(material).CopyTo(name, 0);
        w.Write(name);
        w.Write((ushort)0); w.Write((ushort)0); w.Write(0); w.Write(0); w.Write((ushort)0); w.Write(0u);
        for (int i = 0; i < 6; i++) w.Write(0f);
        return s.ToArray();
    }

    /// <summary>A pack imported as the setup imports one, and its layer.</summary>
    PlayerModuleLayer Imported(string name, params (string Path, byte[] Data)[] files)
    {
        string pack = Path.Combine(root, "packs", name);
        foreach (var (path, data) in files) Put(Path.Combine(pack, path.Replace('/', Path.DirectorySeparatorChar)), data);
        var report = PlayerModuleImport.Run(pack, Path.Combine(root, "store"), new ModuleImportOptions(), TestContext.Current.CancellationToken);
        Assert.True(report.Accepted, report.Refusal);
        return report.Module!.Layer;
    }

    [Fact]
    public void A_pack_of_textures_is_each_players_own_and_one_with_a_model_must_be_the_same_to_join()
    {
        var textures = Imported("textures", ("materials/Prague/Wall.TGA", Text("not even a picture: the join reads no file")));
        var model = Imported("new-model", ("3d/models/hero.nod", Nod("hero_skin")));
        Assert.False(textures.Rules);
        Assert.True(model.Rules);
        using var none = new GameFiles(Game());
        using var withTextures = new GameFiles(Game(), null, [textures]);
        using var withModel = new GameFiles(Game(), null, [textures, model]);
        Assert.Empty(withTextures.RulesModules);
        Assert.Equal([model.Stamp], withModel.RulesModules);
        var plain = NetIdentity.Of(none) with { Build = "b", BuildName = "Vitomir" };
        var looks = NetIdentity.Of(withTextures) with { Build = "b", BuildName = "Vitomir" };
        var modelled = NetIdentity.Of(withModel) with { Build = "b", BuildName = "Vitomir" };
        Assert.Null(looks.Differs(plain));
        Assert.Null(plain.Differs(looks));
        Assert.Contains("new-model 0.0.0 is on here and not at the host", modelled.Differs(looks), StringComparison.Ordinal);
        Assert.Contains("new-model 0.0.0 is on at the host and not here", looks.Differs(modelled), StringComparison.Ordinal);
        Assert.Null(modelled.Differs(modelled with { }));
    }

    [Fact]
    public void A_save_names_its_rules_modules_and_a_load_with_others_warns_naming_the_difference()
    {
        var game = new SaveGame { Header = new SaveHeader { Description = "test", Mods = ["rules 1.0.0 0123456789abcdef"] } };
        string path = Path.Combine(root, "test.vsave");
        SaveArchive.Write(game, path);
        var header = SaveArchive.ReadHeader(path);
        Assert.Equal(["rules 1.0.0 0123456789abcdef"], header.Mods);

        Assert.Empty(LoadModules.Report(header, ["rules 1.0.0 0123456789abcdef"]));
        var said = new List<string>();
        var none = LoadModules.Report(header, [], said.Add);
        Assert.Equal(["load: the save was made with the rules module rules 1.0.0, which is not on here: the game may not play as it was saved"], said);
        Assert.Single(none);
        Assert.Equal(["the rules module more 2.0.0 is on here, and the save was made without it: the game may not play as it was saved"],
            LoadModules.Differences(header.Mods, ["rules 1.0.0 0123456789abcdef", "more 2.0.0 fedcba9876543210"]));
        Assert.Equal(["the save was made with the rules module rules 1.0.0, and 1.2.0 is on here: the game may not play as it was saved"],
            LoadModules.Differences(header.Mods, ["rules 1.2.0 0123456789abcdef"]));
        Assert.Equal(["the save was made with the rules module rules 1.0.0 with other files or interface bindings than the one on here: the game may not play as it was saved"],
            LoadModules.Differences(header.Mods, ["rules 1.0.0 ffffffffffffffff"]));
        Assert.Empty(LoadModules.Differences([], []));                                   // a save of the base game, loaded with none on
    }

    /// <summary>An authored module imported (its manifest with <paramref name="actions"/>, the same file each time), and its layer.</summary>
    PlayerModuleLayer Bound(string folder, string actions)
    {
        string pack = Path.Combine(root, "packs", folder);
        Put(Path.Combine(pack, "module.json"), System.Text.Encoding.UTF8.GetBytes(
            $"{{ \"id\": \"bar\", \"name\": \"bar\", \"version\": \"1.0.0\", \"game\": \"classic\"{(actions.Length > 0 ? ", \"actions\": " + actions : "")} }}"));
        Put(Path.Combine(pack, "Data", "ui", "statusBarSP.nui"), Text("name=statusBarSP"));
        var report = PlayerModuleImport.Run(pack, Path.Combine(root, "store-" + folder), new ModuleImportOptions(), TestContext.Current.CancellationToken);
        Assert.True(report.Accepted, report.Refusal);
        return report.Module!.Layer;
    }

    [Fact]
    public void Two_machines_with_the_same_files_and_other_bindings_do_not_join_and_a_save_names_the_difference()
    {
        var vault = Bound("vault", "{ \"sbVault\": \"open-vault\" }");
        var advance = Bound("advance", "{ \"sbVault\": \"open-advancement\" }");
        var vaultAgain = Bound("vault-again", "{ \"SBVAULT\": \"open-vault\" }");
        var none = Bound("none", "");
        Assert.Equal(vault.ContentHash, advance.ContentHash);                              // the same files
        Assert.True(vault.Rules && advance.Rules);
        Assert.False(none.Rules);                                                          // an interface module that binds nothing is no rules module
        Assert.Equal($"bar 1.0.0 {none.ContentHash[..16]}", none.Stamp);                    // no bindings: today's stamp
        using var a = new GameFiles(Game(), null, [vault]);
        using var b = new GameFiles(Game(), null, [advance]);
        using var c = new GameFiles(Game(), null, [vaultAgain]);
        var host = NetIdentity.Of(a) with { Build = "b", BuildName = "Vitomir" };
        var other = NetIdentity.Of(b) with { Build = "b", BuildName = "Vitomir" };
        var same = NetIdentity.Of(c) with { Build = "b", BuildName = "Vitomir" };
        Assert.Contains("bar 1.0.0 has other files or interface bindings at the host than here", other.Differs(host), StringComparison.Ordinal);
        Assert.Null(same.Differs(host));                                                  // identical bindings join
        // A save written with one binding, loaded with the other: the difference named.
        Assert.Equal(["the save was made with the rules module bar 1.0.0 with other files or interface bindings than the one on here: the game may not play as it was saved"],
            LoadModules.Differences(a.RulesModules, b.RulesModules));
        Assert.Empty(LoadModules.Differences(a.RulesModules, c.RulesModules));
    }
}
