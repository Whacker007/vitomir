// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.IO.Compression;
using System.Text;
using Vitomir.Formats;
using Vitomir.Platform.Assets;
using Vitomir.Platform.PlayerModules;

namespace Vitomir.Platform.Tests;

/// <summary>
/// **The player's modules**, on packs and a stand-in base game made by the test (generated textures and models, no file of 1.1's):
/// the manifest read strictly; the import from a folder, a zip and a <c>.nob</c>, a JSGME layout, an authored module, carried on after
/// a stop; code and program files refused and named, documents kept aside; the resolver (missing, off, out of range, cycle, load
/// after, incompatible); each check, error and warning, made-for with a generated model and texture and its <c>fits</c>; and the
/// accepted combination kept, and asked again when anything changes.
/// </summary>
public sealed class PlayerModuleTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("vitomir-player-modules-").FullName;
    readonly List<IDisposable> open = [];

    public void Dispose()
    {
        foreach (var o in open) o.Dispose();
        try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    string UserData => Path.Combine(root, "user");

    // ---- made-up files ---------------------------------------------------------------------------------------------------------

    /// <summary>A TGA of one colour, 24 or 32 bits, uncompressed; with <paramref name="alpha"/> below 255 its alpha is used.</summary>
    static byte[] Tga(int width, int height, bool withAlpha = false, byte alpha = 255, byte red = 200)
    {
        int bytes = withAlpha ? 4 : 3;
        var data = new byte[18 + width * height * bytes];
        data[2] = 2;
        data[12] = (byte)width; data[13] = (byte)(width >> 8); data[14] = (byte)height; data[15] = (byte)(height >> 8);
        data[16] = (byte)(bytes * 8);
        data[17] = (byte)(withAlpha ? 8 : 0);
        for (int i = 18; i < data.Length; i += bytes)
        {
            data[i] = 10; data[i + 1] = 20; data[i + 2] = red;
            if (withAlpha) data[i + 3] = alpha;
        }
        return data;
    }

    /// <summary>A NOD model with no geometry, naming <paramref name="materials"/> (what the checks read of a model).</summary>
    static byte[] Nod(params string[] materials)
    {
        var s = new MemoryStream();
        var w = new BinaryWriter(s);
        w.Write(7u);
        w.Write(materials.Length);
        foreach (var m in materials)
        {
            var name = new byte[32];
            Encoding.ASCII.GetBytes(m).CopyTo(name, 0);
            w.Write(name);
        }
        w.Write((ushort)0); w.Write((ushort)0); w.Write(0); w.Write(0); w.Write((ushort)0); w.Write(0u);
        for (int i = 0; i < 6; i++) w.Write(0f);
        return s.ToArray();
    }

    static void Put(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
    }

    static void Put(string path, string text) => Put(path, Encoding.UTF8.GetBytes(text));

    /// <summary>A <c>.nob</c> (a zip of stored entries) of <paramref name="files"/>.</summary>
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

    /// <summary>A zip of a folder's files, each under <paramref name="prefix"/>.</summary>
    static void Zip(string folder, string path, string prefix = "")
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            using var s = zip.CreateEntry(prefix + Path.GetRelativePath(folder, f).Replace('\\', '/')).Open();
            s.Write(File.ReadAllBytes(f));
        }
    }

    /// <summary>
    /// **A stand-in base game**: one archive with a model (<c>3d/models/hero.nod</c>, its material <c>hero_skin</c>), the model's texture,
    /// a wall, a window of used alpha, a lamp drawn additively (its <c>.nam</c>), and a loose chronicle folder.
    /// </summary>
    GameInstall Base()
    {
        string dir = Path.Combine(root, "base");
        if (!Directory.Exists(dir))
            Nob(Path.Combine(dir, "Resource.nob"),
                ("3d/models/hero.nod", Nod("hero_skin")),
                ("3d/materials/hero_skin.tga", Tga(64, 64)),
                ("materials/Prague/Wall.TGA", Tga(64, 128)),
                ("materials/Prague/window.tga", Tga(32, 32, withAlpha: true, alpha: 100)),
                ("materials/Prague/lamp.tga", Tga(32, 32, withAlpha: true, alpha: 100)),
                ("materials/Prague/lamp.nam", Encoding.ASCII.GetBytes("texture lamp.tga\naddblend\n")),
                ("misc/templates/things.not", Encoding.ASCII.GetBytes("")));
        var install = GameInstall.Open(dir);
        open.Add(install);
        return install;
    }

    PlayerModuleStore Store() => new(UserData);

    PlayerModule Add(string source, string? id = null, bool expectRefused = false)
    {
        var install = Base();
        var report = Store().Add(source, new ModuleImportOptions { Id = id, By = "tests" }, install, TestContext.Current.CancellationToken);
        if (expectRefused) { Assert.Null(report.Module); return null!; }
        Assert.True(report.Accepted, report.Refusal);
        return report.Module!;
    }

    /// <summary>A bare pack folder of 1.1-path files.</summary>
    string Pack(string name, params (string Path, byte[] Data)[] files)
    {
        string dir = Path.Combine(root, "packs", name);
        foreach (var (path, data) in files) Put(Path.Combine(dir, path.Replace('/', Path.DirectorySeparatorChar)), data);
        return dir;
    }

    /// <summary>An authored module folder: its manifest and its Data.</summary>
    string Authored(string name, string manifest, params (string Path, byte[] Data)[] files)
    {
        string dir = Path.Combine(root, "packs", name);
        Put(Path.Combine(dir, "module.json"), manifest);
        foreach (var (path, data) in files) Put(Path.Combine(dir, "Data", path.Replace('/', Path.DirectorySeparatorChar)), data);
        return dir;
    }

    static string Manifest(string id, string version = "1.0.0", string extra = "") =>
        $"{{ \"id\": \"{id}\", \"name\": \"{id}\", \"version\": \"{version}\", \"game\": \"classic\"{(extra.Length > 0 ? ", " + extra : "")} }}";

    ModuleStart Prepare(bool fresh = true) => Store().Prepare(Base(), fresh, TestContext.Current.CancellationToken);

    // ---- the manifest ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_manifest_is_read_strictly_each_problem_named()
    {
        var problems = new List<string>();
        Assert.Null(PlayerModuleManifest.Parse("""{ "id": "x", "name": "X", "version": "1.0", "game": "enhanced", "dependancies": {} }""", "module.json", problems));
        Assert.Contains(problems, p => p.Contains("unknown field 'dependancies'", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("'version' is '1.0', not a version", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("player modules are for the game 'classic'", StringComparison.Ordinal));

        problems.Clear();
        Assert.Null(PlayerModuleManifest.Parse(Manifest("classic"), "module.json", problems));
        Assert.Contains(problems, p => p.Contains("'classic' is taken", StringComparison.Ordinal));

        problems.Clear();
        var m = PlayerModuleManifest.Parse(Manifest("sharp", extra: """
            "author": "someone", "kind": ["look", "interface"], "dependencies": { "base-pack": "1.2.0", "other": "1.0.0 - 1.4.9" },
            "loadAfter": ["late"], "incompatible": ["rival"], "madeFor": ["oneone"], "fits": { "new-models": ["3d/materials/a.tga"], "base-pack": "*" }
            """), "module.json", problems);
        Assert.True(m is not null, string.Join("; ", problems));
        Assert.Equal(PackKinds.Look | PackKinds.Interface, m.Kind);
        Assert.True(m.Dependencies[0].Versions.Accepts(new(1, 9, 0)));
        Assert.False(m.Dependencies[0].Versions.Accepts(new(2, 0, 0)));
        Assert.True(m.Dependencies[1].Versions.Accepts(new(1, 4, 9)));
        Assert.False(m.Dependencies[1].Versions.Accepts(new(1, 5, 0)));
        Assert.True(m.FitsWith("new-models", "3D/Materials/A.tga"));
        Assert.False(m.FitsWith("new-models", "3d/materials/b.tga"));
        Assert.True(m.FitsWith("base-pack", "anything.tga"));
        // What an import writes reads back the same.
        var again = PlayerModuleManifest.Parse(m.ToJson(), "module.json", problems);
        Assert.Equal(m.ToJson(), again!.ToJson());
    }

    // ---- the import ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_bare_folder_is_imported_by_its_1_1_paths_with_a_manifest_made_for_it()
    {
        string pack = Pack("Sharper Prague",
            ("Materials/prague/WALL.tga", Tga(128, 256)),
            ("readme.txt", Encoding.ASCII.GetBytes("a pack")));
        var m = Add(pack);
        Assert.Equal("sharper-prague", m.Id);
        Assert.Equal("0.0.0", m.Manifest.Version.ToString());
        Assert.Equal(new[] { "oneone" }, m.Manifest.MadeFor!);
        Assert.Equal(PackKinds.Look, m.Kinds);
        Assert.False(m.Rules);
        var file = Assert.Single(m.Record.Files);
        Assert.Equal("materials/Prague/Wall.TGA", file.Path);                     // 1.1's own spelling of the path
        Assert.Equal((128, 256, false), (file.Width, file.Height, file.Alpha));
        Assert.True(File.Exists(Path.Combine(m.DataFolder, "materials", "Prague", "Wall.TGA")));
        Assert.True(File.Exists(Path.Combine(m.Folder, "Documents", "readme.txt")));
        Assert.Contains(m.Record.LeftOut, o => o is { Class: "document", Path: "readme.txt" });
        Assert.Empty(m.CheckWhole());
        Assert.Equal("sharper-prague", Assert.Single(Store().List(out _).Modules).Id);
    }

    [Fact]
    public void A_pack_of_only_textures_stays_look_and_one_with_a_model_is_a_rules_module()
    {
        var textures = Add(Pack("only-textures", ("materials/Prague/Wall.TGA", Tga(64, 128)), ("3d/materials/hero_skin.tga", Tga(64, 64)),
            ("sounds/step.wav", Encoding.ASCII.GetBytes("RIFF")), ("strings/Game.nls", Encoding.ASCII.GetBytes("")), ("Video/Logo.bik", [])));
        Assert.False(textures.Rules);
        Assert.False(textures.Layer.Rules);
        Assert.DoesNotContain("world", textures.Record.Kinds);
        var model = Add(Pack("one-model", ("materials/Prague/Wall.TGA", Tga(64, 128)), ("3d/models/hero.nod", Nod("hero_skin"))));
        Assert.True(model.Rules);                                                        // a model's box is its thing's collision box
        Assert.Equal(PackKinds.Look | PackKinds.World, model.Kinds);
        Assert.Equal("world", model.Record.Files.Single(f => f.Path == "3d/models/hero.nod").Kind);
    }

    [Fact]
    public void A_pack_the_import_knows_gets_its_plain_id_name_and_version_and_a_note()
    {
        string pack = Pack("Vampire_The_Masquerade_-_Neural_Redemption_v1.0", ("materials/Prague/Wall.TGA", Tga(128, 256)));
        var report = Store().Add(pack, new ModuleImportOptions(), Base(), TestContext.Current.CancellationToken);
        var m = report.Module!;
        Assert.Equal(("neural-redemption", "Neural Redemption", "1.0.0"), (m.Id, m.Manifest.Name, m.Manifest.Version.ToString()));
        Assert.Contains(report.Notes, n => n.StartsWith("Neural Redemption: every texture replaces one of 1.1's", StringComparison.Ordinal));
        Assert.Equal("vtmr-crimson-edition", PlayerModuleImport.IdFrom("VTMR Crimson Edition"));
        Assert.Equal("crimson-edition", KnownPacks.Of("VTMR Crimson Edition")!.Id);
        Assert.Null(KnownPacks.Of("Crimsonish"));
        Assert.Equal("pack-2-fast", PlayerModuleImport.IdFrom("2 Fast"));
    }

    [Fact]
    public void Code_and_program_files_are_refused_each_named_with_what_it_was_for_and_settings_kept_aside()
    {
        string pack = Pack("Crimsonish",
            ("Vampire.exe", [1]), ("D3D9.dll", [1]), ("Binkw32.dll", [1]), ("Miles/FontFix.asi", [1]), ("Masquerade.ini", [1]),
            ("reshade-shaders/Shaders/x.fx", [1]), ("Codex/Door.java", [1]), ("misc/x.class", [1]),
            ("UI/sbLvlUp.tga", Tga(16, 16)), ("Strings/Game.nls", Encoding.ASCII.GetBytes("[strings]\n")), ("Video/Logo.bik", []));
        var m = Add(pack);
        var refused = m.Record.LeftOut.Where(o => o.Class is "code" or "program").ToDictionary(o => o.Path, o => o.Why);
        Assert.Equal(7, refused.Count);
        Assert.Contains("the game's program, patched", refused["Vampire.exe"], StringComparison.Ordinal);
        Assert.Contains("graphics wrapper", refused["D3D9.dll"], StringComparison.Ordinal);
        Assert.Contains("film player", refused["Binkw32.dll"], StringComparison.Ordinal);
        Assert.Contains("Miles", refused["Miles/FontFix.asi"], StringComparison.Ordinal);
        Assert.Contains("ReShade", refused["reshade-shaders/Shaders/x.fx"], StringComparison.Ordinal);
        Assert.Contains("script code", refused["Codex/Door.java"], StringComparison.Ordinal);
        Assert.Contains("compiled Java class", refused["misc/x.class"], StringComparison.Ordinal);
        Assert.Contains(m.Record.LeftOut, o => o is { Class: "document", Path: "Masquerade.ini" });
        Assert.Equal(["Video/Logo.bik", "strings/Game.nls", "ui/sbLvlUp.tga"], m.Record.Files.Select(f => f.Path).Order(StringComparer.Ordinal));
        Assert.True(m.Record.Files.Single(f => f.Path == "Video/Logo.bik").Empty);   // an empty film takes 1.1's out
        Assert.Equal(PackKinds.Look | PackKinds.Interface, m.Kinds);
        Assert.DoesNotContain(Directory.EnumerateFiles(m.Folder, "*", SearchOption.AllDirectories), f => f.EndsWith(".exe") || f.EndsWith(".dll") || f.EndsWith(".asi"));
    }

    [Fact]
    public void A_zip_with_a_wrapping_folder_and_a_JSGME_layout_are_found_and_two_packs_in_one_are_not_chosen_between()
    {
        string pack = Pack("wrapped", ("Neat Textures v1/materials/Prague/Wall.TGA", Tga(64, 128)));
        Zip(pack, Path.Combine(root, "neat.zip"));
        var wrapped = Add(Path.Combine(root, "neat.zip"));
        Assert.Equal("neat-textures-v1", wrapped.Id);
        Assert.Equal("zip", wrapped.Record.SourceKind);

        string jsgme = Pack("jsgme", ("MODS/Pretty Faces/3d/materials/hero_skin.tga", Tga(64, 64)), ("readme.txt", [1]));
        var m = Add(jsgme);
        Assert.Equal(("pretty-faces", "jsgme"), (m.Id, m.Record.Layout));
        Assert.Equal("3d/materials/hero_skin.tga", Assert.Single(m.Record.Files).Path);

        string two = Pack("two", ("MODS/A/materials/a.tga", Tga(8, 8)), ("MODS/B/materials/b.tga", Tga(8, 8)));
        var report = Store().Add(two, new ModuleImportOptions(), Base(), TestContext.Current.CancellationToken);
        Assert.Contains("it holds 2 packs (MODS/A, MODS/B)", report.Refusal, StringComparison.Ordinal);
        Assert.Contains("nothing to import", Store().Add(Pack("none", ("notes/readme.txt", [1])), new ModuleImportOptions(), Base(),
            TestContext.Current.CancellationToken).Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void A_nob_is_kept_whole_and_mounted_and_one_carrying_code_gives_its_data_loose_with_the_code_refused()
    {
        string nob = Path.Combine(root, "packs", "Rabbit.nob");
        Nob(nob, ("paths/rabbit.ncp", [1, 2, 3]), ("3d/materials/rabbit.tga", Tga(16, 16)));
        var m = Add(nob);
        Assert.Equal("rabbit", m.Id);
        Assert.Equal("Rabbit.nob", Assert.Single(m.Record.Archives).Path);
        Assert.All(m.Record.Files, f => Assert.Equal("Rabbit.nob", f.In));
        Assert.True(m.Rules);                                                          // a camera path: a world file
        Assert.Equal(Path.Combine(m.DataFolder, "Rabbit.nob"), Assert.Single(m.Layer.Archives));

        string coded = Path.Combine(root, "packs", "Coded.nob");
        Nob(coded, ("Door.class", [0xca, 0xfe]), ("3d/materials/coded.tga", Tga(16, 16)));
        var c = Add(coded);
        Assert.Empty(c.Record.Archives);
        Assert.Contains(c.Record.LeftOut, o => o is { Class: "code", Path: "Coded.nob/Door.class" });
        Assert.Null(Assert.Single(c.Record.Files).In);
        Assert.True(File.Exists(Path.Combine(c.DataFolder, "3d", "materials", "coded.tga")));
        Assert.False(File.Exists(Path.Combine(c.DataFolder, "Coded.nob")));
    }

    [Fact]
    public void An_authored_module_keeps_its_id_and_manifest()
    {
        string dir = Authored("authored", Manifest("fine-walls", "1.2.0", "\"author\": \"someone\""), ("materials/Prague/Wall.TGA", Tga(64, 128)));
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "notes");
        Zip(dir, Path.Combine(root, "authored.zip"), "FineWalls/");
        var m = Add(Path.Combine(root, "authored.zip"));
        Assert.Equal(("fine-walls", "1.2.0", "someone"), (m.Id, m.Manifest.Version.ToString(), m.Manifest.Author));
        Assert.Equal(File.ReadAllText(Path.Combine(dir, "module.json")), File.ReadAllText(Path.Combine(m.Folder, "module.json")));
        Assert.Contains(m.Record.LeftOut, o => o is { Class: "document", Path: "FineWalls/notes.txt" });
        var refused = Store().Add(Path.Combine(root, "authored.zip"), new ModuleImportOptions { Id = "other" }, Base(), TestContext.Current.CancellationToken);
        Assert.Contains("an authored module keeps its own id", refused.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stopped_import_carries_on_and_a_file_put_in_by_hand_is_named()
    {
        string pack = Pack("Big", [.. Enumerable.Range(0, 12).Select(i => ($"materials/Prague/t{i:00}.tga", Tga(16, 16)))]);
        using var stop = new CancellationTokenSource();
        int seen = 0;
        var progress = new SyncProgress(p => { if (++seen == 5) stop.Cancel(); });
        Assert.ThrowsAny<OperationCanceledException>(() =>
            PlayerModuleImport.Run(pack, Store().Folder, new ModuleImportOptions { Progress = progress }, stop.Token));
        Assert.Null(PlayerModule.Open(Path.Combine(Store().Folder, "big"), []));          // no record: not usable
        var report = PlayerModuleImport.Run(pack, Store().Folder, new ModuleImportOptions(), TestContext.Current.CancellationToken);
        Assert.True(report.Accepted, report.Refusal);
        Assert.Equal(5, report.Kept);
        Assert.Equal(7, report.Copied);
        Assert.False(File.Exists(Path.Combine(report.Module!.Folder, PlayerModuleImport.JournalName)));

        File.WriteAllBytes(Path.Combine(report.Module.DataFolder, "d3d8.dll"), [1]);
        File.WriteAllBytes(Path.Combine(report.Module.DataFolder, "materials", "Prague", "t00.tga"), [1, 2]);
        var whole = report.Module.CheckWhole();
        Assert.Contains(whole, p => p.StartsWith("d3d8.dll: put there after the import, and refused: a graphics wrapper", StringComparison.Ordinal));
        Assert.Contains(whole, p => p == "materials/Prague/t00.tga: not the size it was imported at");
    }

    /// <summary>Progress reported on the caller's thread (<see cref="Progress{T}"/> posts to the pool).</summary>
    sealed class SyncProgress(Action<Vitomir.Platform.Setup.SetupProgress> on) : IProgress<Vitomir.Platform.Setup.SetupProgress>
    {
        public void Report(Vitomir.Platform.Setup.SetupProgress value) => on(value);
    }

    // ---- the resolver ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_resolver_orders_by_dependencies_and_load_after_and_names_every_problem()
    {
        Add(Authored("lib", Manifest("lib", "1.3.0"), ("materials/lib.tga", Tga(8, 8))));
        Add(Authored("user", Manifest("user", extra: "\"dependencies\": { \"lib\": \"1.2.0\" }, \"loadAfter\": [\"late\"]"), ("materials/user.tga", Tga(8, 8))));
        Add(Authored("late", Manifest("late"), ("materials/late.tga", Tga(8, 8))));
        var store = Store();
        Assert.Null(store.Order(["user", "lib", "late"]));
        var start = Prepare();
        Assert.Equal(["lib", "late", "user"], start.Order.Select(m => m.Id));
        Assert.Contains(start.Problems, p => p is { Level: ProblemLevel.Note, Module: "user", Kind: "order" });
        Assert.True(start.CanStart, string.Join("\n", start.Problems.Select(p => p.Line)));

        Assert.Null(store.Enable("lib", false));
        Assert.Contains(Prepare().Errors, p => p.Line == "error: module user: it needs the module lib, which is off; turn it on (module-enable lib), or turn user off");
        Assert.Null(store.Enable("lib", true));

        Add(Authored("lib2", Manifest("lib", "2.0.0"), ("materials/lib.tga", Tga(8, 8))), expectRefused: true);   // a module of that id from elsewhere
        Add(Authored("needs", Manifest("needs", extra: "\"dependencies\": { \"lib\": \"1.4.0 - 1.9.0\", \"gone\": \"1.0.0\" }"), ("materials/n.tga", Tga(8, 8))));
        var errors = Prepare().Errors.Select(p => p.Line).ToList();
        Assert.Contains("error: module needs: it needs the module lib at 1.4.0 to 1.9.0, and 1.3.0 is imported; add a version of lib it accepts, or turn needs off", errors);
        Assert.Contains("error: module needs: it needs the module gone (1.0.0 or a later 1.x), which is not imported; add gone, or turn needs off", errors);
        Assert.Null(store.Remove("needs"));

        Add(Authored("ring-a", Manifest("ring-a", extra: "\"loadAfter\": [\"ring-b\"]"), ("materials/a.tga", Tga(8, 8))));
        Add(Authored("ring-b", Manifest("ring-b", extra: "\"dependencies\": { \"ring-a\": \"1.0.0\" }"), ("materials/b.tga", Tga(8, 8))));
        Assert.Contains(Prepare().Errors, p => p.Kind == "cycle" && p.Text.Contains("ring-a -> ring-b -> ring-a", StringComparison.Ordinal));
        Assert.Null(store.Remove("ring-a"));
        Assert.Null(store.Remove("ring-b"));

        Add(Authored("rival", Manifest("rival", extra: "\"incompatible\": [\"late\"]"), ("materials/r.tga", Tga(8, 8))));
        Assert.Contains(Prepare().Errors, p => p.Kind == "incompatible" && p.Line.Contains("cannot be played with the module rival (rival's module.json says so)", StringComparison.Ordinal));
        Assert.Null(store.Enable("rival", false));
        Assert.DoesNotContain(Prepare().Problems, p => p.IsError);

        var list = store.List(out _);
        list.Modules.Add(new GameModuleEntry { Id = "ghost" });
        list.Write(store.ListPath);
        Assert.Contains(Prepare().Errors, p => p.Line.StartsWith("error: module ghost: it is on, and is not imported", StringComparison.Ordinal));
    }

    // ---- the checks ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Two_modules_replacing_one_file_warn_which_wins_and_a_file_that_does_not_decode_is_an_error()
    {
        Add(Pack("first", ("materials/Prague/Wall.TGA", Tga(64, 128)), ("materials/Prague/extra.tga", Tga(8, 8))));
        Add(Pack("second", ("materials/Prague/Wall.TGA", Tga(64, 128, red: 50)), ("materials/Prague/broken.tga", [1, 2, 3])));
        var start = Prepare();
        var overlap = Assert.Single(start.Warnings, w => w.Kind == "overlap");
        Assert.Equal("warning: module second: materials/Prague/Wall.TGA: first and second both give 1 file: second's is used, as it loads later; to use first's, put it after second (module-order)", overlap.Line);
        var broken = Assert.Single(start.Errors);
        Assert.Equal("decode", broken.Kind);
        Assert.StartsWith("error: module second: materials/Prague/broken.tga: a texture that cannot be read as a texture", broken.Line, StringComparison.Ordinal);
        Assert.False(start.CanStart);
        Assert.NotNull(Store().Accept(start));                                            // errors are never accepted
    }

    [Fact]
    public void A_texture_made_for_1_1s_model_warns_when_another_module_replaces_the_model_unless_its_author_says_it_fits()
    {
        Add(Pack("new-hero", ("3d/models/hero.nod", Nod("hero_skin"))));
        Add(Pack("skins", ("3d/materials/hero_skin.tga", Tga(64, 64, red: 10))));
        var made = Assert.Single(Prepare().Warnings, w => w.Kind == "made-for");
        Assert.Equal("warning: module skins: 3d/materials/hero_skin.tga (hero.nod): skins's 3d/materials/hero_skin.tga was made for 1.1's hero.nod, "
            + "but new-hero replaces hero.nod: the picture may not fit the model; if they fit, skins's author can say so "
            + "(\"fits\": {\"new-hero\": [...]} in its module.json); else turn one of them off", made.Line);

        // The model's own module loading later with its own texture: its texture is drawn, nothing to say.
        Assert.Null(Store().Remove("skins"));
        Add(Authored("skins-fit", Manifest("skins", extra: "\"fits\": { \"new-hero\": [\"3d/materials/hero_skin.tga\"] }"), ("3d/materials/hero_skin.tga", Tga(64, 64, red: 10))));
        Assert.DoesNotContain(Prepare().Warnings, w => w.Kind == "made-for");
        Assert.Null(Store().Remove("skins"));
        Add(Authored("skins-made", Manifest("skins", extra: "\"madeFor\": [\"new-hero\"]"), ("3d/materials/hero_skin.tga", Tga(64, 64, red: 10))));
        Assert.DoesNotContain(Prepare().Warnings, w => w.Kind == "made-for");
    }

    [Fact]
    public void A_model_whose_texture_is_found_nowhere_is_an_error()
    {
        Add(Pack("lost", ("3d/models/ghoul.nod", Nod("ghoul_skin", "hero_skin"))));
        var error = Assert.Single(Prepare().Errors);
        Assert.Equal("missing", error.Kind);
        Assert.Equal("error: module lost: 3d/models/ghoul.nod (ghoul_skin): 1 model uses a material or texture found nowhere, in 1.1's files or the enabled modules "
            + "(named in brackets); the pack is missing files, or needs another module on: get the pack whole, or turn it off", error.Line);
        Add(Pack("found", ("3d/materials/ghoul_skin.tga", Tga(16, 16))));
        Assert.DoesNotContain(Prepare().Problems, p => p.IsError);
    }

    [Fact]
    public void A_texture_of_another_shape_or_without_the_alpha_its_material_needs_warns()
    {
        Add(Pack("odd", ("materials/Prague/Wall.TGA", Tga(128, 128)), ("materials/Prague/window.tga", Tga(64, 64)), ("materials/Prague/lamp.tga", Tga(32, 32))));
        var warnings = Prepare().Warnings.ToList();
        var shape = Assert.Single(warnings, w => w.Kind == "aspect");
        Assert.Equal(["materials/Prague/Wall.TGA (128x128 over 64x128)"], shape.Files);
        var alpha = Assert.Single(warnings, w => w.Kind == "alpha");
        Assert.Equal(["materials/Prague/window.tga"], alpha.Files);                       // the lamp's material is additive: its alpha is not needed
    }

    [Fact]
    public void A_kind_said_that_is_less_than_the_files_warns()
    {
        Add(Authored("says-look", Manifest("says-look", extra: "\"kind\": [\"look\"]"), ("paths/more.ncp", [1, 2, 3])));
        var w = Assert.Single(Prepare().Warnings);
        Assert.Contains("it says it is look, and its files are world: it is taken as a rules module", w.Line, StringComparison.Ordinal);
    }

    // ---- the accepted combination ------------------------------------------------------------------------------------------------

    [Fact]
    public void An_accepted_combination_is_kept_and_asked_again_when_anything_changes()
    {
        Add(Pack("first", ("materials/Prague/Wall.TGA", Tga(64, 128))));
        Add(Pack("second", ("materials/Prague/Wall.TGA", Tga(64, 128, red: 50))));
        var store = Store();
        var start = Prepare();
        Assert.False(start.CanStart);
        Assert.Null(store.Accept(start));
        var accepted = Prepare(fresh: false);
        Assert.True(accepted.Accepted && accepted.CanStart);
        Assert.True(accepted.Cached);
        var kept = Assert.Single(store.List(out _).Accepted);
        Assert.Equal(accepted.Stamps, kept.Modules);
        Assert.All(kept.Modules, s => Assert.Matches(@"^\w+ 0\.0\.0 [0-9a-f]{16}$", s));

        Assert.Null(store.Order(["second", "first"]));                                    // another order
        Assert.False(Prepare(fresh: false).Accepted);
        Assert.Null(store.Order(["first", "second"]));
        Assert.True(Prepare(fresh: false).Accepted);

        File.WriteAllBytes(Path.Combine(root, "packs", "second", "materials", "Prague", "Wall.TGA"), Tga(64, 128, red: 90));   // an update of a pack
        Add(Path.Combine(root, "packs", "second"));
        Assert.False(Prepare(fresh: false).Accepted);
        Assert.Null(store.Accept(Prepare()));
        Assert.Null(store.Enable("first", false));                                         // one turned off: no warning left
        Assert.True(Prepare().CanStart);
        Assert.Null(store.Enable("first", true));
        Assert.True(Prepare(fresh: false).Accepted);
    }

    [Fact]
    public void The_layered_files_find_the_top_module_and_what_is_under_it()
    {
        var a = Add(Pack("a", ("materials/Prague/Wall.TGA", Tga(64, 128))));
        var b = Add(Pack("b", ("materials/Prague/Wall.TGA", Tga(64, 128, red: 1))));
        using var layers = new LayeredFiles(Base(), [a, b]);
        Assert.Equal(1, layers.Owner("MATERIALS/prague/wall.tga"));
        Assert.Equal(0, layers.Below("materials/Prague/Wall.TGA", 1));
        Assert.Equal(LayeredFiles.Base, layers.Below("materials/Prague/Wall.TGA", 0));
        Assert.Equal("materials/prague/window.tga", layers.AlongSearchPath("window.tga"));               // the search path's own spelling
        Assert.Equal(1, TgaImage.Read(layers.Read("materials/Prague/Wall.TGA")!).Rgba[0]);
    }

    // ---- the launcher's order: sort by dependencies, a place up or down ------------------------------------------------------------

    [Fact]
    public void Sort_by_dependencies_moves_each_module_after_what_it_needs_and_keeps_the_players_order_otherwise()
    {
        Add(Authored("user", Manifest("user", extra: "\"dependencies\": { \"lib\": \"1.0.0\" }"), ("materials/user.tga", Tga(8, 8))));
        Add(Authored("free", Manifest("free"), ("materials/free.tga", Tga(8, 8))));
        Add(Authored("lib", Manifest("lib"), ("materials/lib.tga", Tga(8, 8))));
        Add(Authored("late", Manifest("late", extra: "\"loadAfter\": [\"user\"]"), ("materials/late.tga", Tga(8, 8))));
        var store = Store();
        Assert.Null(store.Order(["late", "user", "free", "lib"]));
        Assert.Null(store.Enable("lib", false));                                         // off, and still sorted with the rest
        Assert.Equal(["user", "late"], store.SortByDependencies());
        Assert.Equal(["free", "lib", "user", "late"], store.List(out _).Modules.Select(e => e.Id));
        Assert.Empty(store.SortByDependencies());                                         // in order already
        Assert.Null(store.Move("free", 1));
        Assert.Equal(["lib", "free", "user", "late"], store.List(out _).Modules.Select(e => e.Id));
        Assert.Equal("lib is first already", store.Move("lib", -1));
        Assert.Equal("late is last already", store.Move("late", 1));
        Assert.NotNull(store.Move("ghost", 1));
    }

    // ---- interface actions ---------------------------------------------------------------------------------------------------------

    /// <summary>A made-up status bar: two quads and the buttons named, at made-up places.</summary>
    static byte[] Bar(params string[] buttons) => Encoding.ASCII.GetBytes(
        "name=statusBarSP\npos=0,0\nquad mat=bar pos=0,0 size=256,128\n"
        + string.Concat(buttons.Select((b, i) => $"element button name={b} flags=0x02 pos={10 + 30 * i},10 size=26,32\n")));

    [Fact]
    public void A_modules_actions_bind_its_elements_and_the_checker_refuses_an_action_not_on_the_list()
    {
        var problems = new List<string>();
        Assert.Null(PlayerModuleManifest.Parse(Manifest("x", extra: "\"actions\": { \"sb Vault\": \"open-vault\", \"sbOther\": 3 }"), "module.json", problems));
        Assert.Contains(problems, p => p.Contains("'sb Vault' is not an interface element's name", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("'sbOther' names one of Vitomir's interface actions", StringComparison.Ordinal));

        var bar = Add(Authored("vault-button", Manifest("vault-button", extra: """
            "actions": { "sbVault": "open-vault", "sbOdd": "open-the-gates", "sbMapButton": "open-vault", "sbNowhere": "regroup" }
            """), ("ui/statusBarSP.nui", Bar("sbVault", "sbOdd", "sbMapButton"))));
        Assert.Equal(PackKinds.Interface, bar.Kinds);
        Assert.True(bar.Rules);                                                           // it opens the vault where 1.1 would not
        Assert.True(bar.RulesByActions);
        Assert.True(bar.Layer.Rules);
        Assert.Equal("open-vault", bar.Layer.Actions["SBVAULT"]);
        var effective = InterfaceActions.Effective(bar.Manifest.Actions);
        Assert.Equal(["sbNowhere", "sbVault"], effective.Keys.Order(StringComparer.Ordinal));   // 1.1's sbMapButton keeps its own; the unknown action is no binding
        Assert.Equal("regroup", effective["sbNowhere"].Name);
        Assert.Equal(("sbMapButton", false), (InterfaceActions.Find("OPEN-MAP")!.BarElement, InterfaceActions.Find("open-map")!.Right));

        var start = Prepare();
        var refused = Assert.Single(start.Errors);
        Assert.Equal("action", refused.Kind);
        Assert.StartsWith("error: module vault-button: module.json: it binds the element sbOdd to \"open-the-gates\", which is not one of Vitomir's interface actions (open-advancement, open-vault,",
                          refused.Line, StringComparison.Ordinal);
        Assert.Contains(start.Problems, p => p is { Level: ProblemLevel.Note, Kind: "action" } && p.Text == "sbMapButton is one of 1.1's own status bar elements: it keeps 1.1's behaviour, and its binding to open-vault is not used");
        Assert.Contains(start.Problems, p => p is { Level: ProblemLevel.Note, Kind: "action" } && p.Text == "no status bar layout of the game with these modules has an element sbNowhere: its binding to regroup does nothing");
        Assert.False(start.CanStart);

        // A binding of regroup alone changes no rule; a later module binding the same element is said, and its binding used.
        Assert.Null(Store().Remove("vault-button"));
        var plain = Add(Authored("plain", Manifest("plain", extra: "\"actions\": { \"sbExtra\": \"regroup\" }"), ("ui/statusBarSP.nui", Bar("sbExtra"))));
        Assert.False(plain.Rules);
        Add(Authored("later", Manifest("later", extra: "\"actions\": { \"sbExtra\": \"open-advancement\" }"), ("materials/l.tga", Tga(8, 8))));
        var two = Prepare();
        Assert.Contains(two.Problems, p => p is { Level: ProblemLevel.Note, Module: "later" } && p.Text == "plain binds sbExtra too: later's binding (open-advancement) is used, as it loads later");
        Assert.True(two.CanStart, string.Join("\n", two.Problems.Select(p => p.Line)));
    }

    [Fact]
    public void A_bare_pack_with_a_recognised_layout_gets_its_buttons_bound_on_import()
    {
        var layout = Bar("sbLvlUp", "sbVault");
        var recognised = new RecognisedFile("A made-up pack", "ui/StatusBarSP.nui", PlayerModule.Hash(layout),
            new Dictionary<string, string> { ["sbLvlUp"] = "open-advancement", ["sbVault"] = "open-vault" });
        string pack = Pack("made-up-bar", ("UI/StatusBarSP.nui", layout), ("materials/Prague/Wall.TGA", Tga(64, 128)));
        var report = Store().Add(pack, new ModuleImportOptions { Recognised = [recognised] }, Base(), TestContext.Current.CancellationToken);
        var m = report.Module!;
        Assert.Equal("open-advancement", m.Manifest.Actions["sbLvlUp"]);
        Assert.Equal("open-vault", m.Manifest.Actions["sbVault"]);
        Assert.True(m.Rules);
        Assert.Contains(report.Notes, n => n.StartsWith("ui/StatusBarSP.nui is A made-up pack's (ui/StatusBarSP.nui): its sbLvlUp button opens the advancement screen", StringComparison.Ordinal)
                                           && n.EndsWith("that makes it a rules module: every player of a network game needs it, and saves name it", StringComparison.Ordinal));
        Assert.Contains("\"actions\"", File.ReadAllText(Path.Combine(m.Folder, "module.json")), StringComparison.Ordinal);
        Assert.True(Prepare().CanStart);
        // Another layout of that path is not recognised: no binding.
        var other = Store().Add(Pack("other-bar", ("ui/statusBarSP.nui", Bar("sbLvlUp"))), new ModuleImportOptions { Recognised = [recognised] }, Base(), TestContext.Current.CancellationToken);
        Assert.Empty(other.Module!.Manifest.Actions);
        // The table names Crimson Edition's file by its hash alone.
        var crimson = Assert.Single(RecognisedPacks.All);
        Assert.Equal(("00c0326d0477a75c241143ff00e1af108a2c31bf20ac5c4eac9eb11c4acd6507", "open-advancement", "open-vault"),
                     (crimson.Sha256, crimson.Actions["sbLvlUp"], crimson.Actions["sbVault"]));
    }

    [Fact]
    public void A_problems_page_words_leave_out_the_command_lines_hints()
    {
        var p = new PlayerModuleProblem(ProblemLevel.Error, "user", "", "it needs the module lib, which is off", "turn it on (module-enable lib), or turn user off");
        Assert.Equal("turn it on, or turn user off", p.PageToDo);
        Assert.Equal("to use a's, put it after b", (p with { ToDo = "to use a's, put it after b (module-order)" }).PageToDo);
    }

    [Fact]
    public void A_modules_stamp_covers_its_interface_bindings_and_one_without_keeps_its_content_hash()
    {
        var plain = Add(Authored("plain-bar", Manifest("plain-bar"), ("materials/p.tga", Tga(8, 8))));
        Assert.Equal($"plain-bar 1.0.0 {plain.Record.ContentHash[..16]}", plain.Stamp);          // no actions: the stamp as before
        Assert.Equal(plain.Record.ContentHash, plain.Layer.RulesHash);
        Assert.Equal(plain.Record.ContentHash, PlayerModuleLayer.RulesHashOf(plain.Record.ContentHash, new Dictionary<string, string>()));

        // The same files, bound one way and another: the content hash is one, the stamps two; the bindings' case and order do not count.
        var vault = Add(Authored("bound-a", Manifest("bound", extra: "\"actions\": { \"sbVault\": \"open-vault\", \"sbLvlUp\": \"open-advancement\" }"), ("materials/b.tga", Tga(8, 8))));
        string vaultStamp = vault.Stamp, content = vault.Record.ContentHash;
        Assert.Null(Store().Remove("bound"));
        var same = Add(Authored("bound-b", Manifest("bound", extra: "\"actions\": { \"SBLVLUP\": \"Open-Advancement\", \"sbvault\": \"open-vault\" }"), ("materials/b.tga", Tga(8, 8))));
        Assert.Equal(content, same.Record.ContentHash);
        Assert.Equal(vaultStamp, same.Stamp);
        Assert.Null(Store().Remove("bound"));
        var other = Add(Authored("bound-c", Manifest("bound", extra: "\"actions\": { \"sbVault\": \"open-advancement\", \"sbLvlUp\": \"open-advancement\" }"), ("materials/b.tga", Tga(8, 8))));
        Assert.Equal(content, other.Record.ContentHash);
        Assert.NotEqual(vaultStamp, other.Stamp);
        Assert.StartsWith("bound 1.0.0 ", other.Stamp, StringComparison.Ordinal);
        Assert.Equal("{\"sblvlup\":\"open-advancement\",\"sbvault\":\"open-vault\"}", PlayerModuleLayer.CanonicalActions(new Dictionary<string, string> { ["sbVault"] = "open-vault", ["sbLvlUp"] = "OPEN-ADVANCEMENT" }));
    }

    // ---- films taken out; a download's version ---------------------------------------------------------------------------------------

    [Fact]
    public void A_film_that_is_not_a_Bink_film_takes_1_1s_out_and_is_a_note_not_an_error()
    {
        Add(Pack("blanked", ("Video/Logo.bik", Encoding.ASCII.GetBytes("#Dummy.bik")), ("Video/Trailer.bik", []),
            ("Video/Intro.bik", Encoding.ASCII.GetBytes("BIKi a film"))));
        var m = Store().Installed().Installed["blanked"];
        Assert.Equal((true, null), (m.Record.Files.Single(f => f.Path == "Video/Logo.bik").NotFilm, m.Record.Files.Single(f => f.Path == "Video/Logo.bik").Problem));
        Assert.Null(m.Record.Files.Single(f => f.Path == "Video/Intro.bik").NotFilm);
        var start = Prepare();
        Assert.Empty(start.Errors);
        Assert.True(start.CanStart);
        Assert.Equal(["note: module blanked: Video/Logo.bik: it is not a film: it takes 1.1's film out, so the game skips it",
                      "note: module blanked: Video/Trailer.bik: it is an empty film: it takes 1.1's film out, so the game skips it"],
                     start.Problems.Where(p => p.Kind == "film-out").Select(p => p.Line));
        Assert.True(PackFiles.IsBinkFilm([0x4b, 0x42, 0x32, 0x6a]) && !PackFiles.IsBinkFilm("#Dummy.bik"u8) && !PackFiles.IsBinkFilm("BIK"u8));   // Bink 2's mark; too short
    }

    [Fact]
    public void A_bare_packs_version_is_read_from_its_name_or_its_downloads_folder_around_it()
    {
        Assert.Equal("1.0.0", KnownPacks.VersionIn("VTMR Crimson Edition-12-1-0-1726673430")?.ToString());   // Nexus: mod 12, version 1-0, its time
        Assert.Equal("2.3.1", KnownPacks.VersionIn("Some Pack-7-2-3-1-1700000000")?.ToString());
        Assert.Equal("12.1.0", KnownPacks.VersionIn("Some Pack-12-1-0")?.ToString());                     // no upload time: a.b.c
        Assert.Equal("1.2.0", KnownPacks.VersionIn("Sharper_Prague_v1.2")?.ToString());
        Assert.Null(KnownPacks.VersionIn("VTMR Crimson Edition"));
        Assert.Null(KnownPacks.VersionIn("Pack-1-2"));

        string download = Pack("VTMR Crimson Edition-12-1-0-1726673430", ("MODS/VTMR Crimson Edition/materials/Prague/Wall.TGA", Tga(64, 128)));
        var outer = Add(download);
        Assert.Equal(("crimson-edition", "1.0.0", "jsgme"), (outer.Id, outer.Manifest.Version.ToString(), outer.Record.Layout));
        Assert.Null(Store().Remove("crimson-edition"));
        var inner = Add(Path.Combine(download, "MODS", "VTMR Crimson Edition"));                       // the pack's own folder, inside the download's
        Assert.Equal(("crimson-edition", "1.0.0"), (inner.Id, inner.Manifest.Version.ToString()));
        Assert.Equal("0.0.0", Add(Pack("Plain Walls", ("materials/Prague/Wall.TGA", Tga(64, 128)))).Manifest.Version.ToString());
    }

    [Fact]
    public void Verify_finds_a_file_changed_at_its_size_and_the_module_is_an_error_until_it_is_added_again()
    {
        string pack = Pack("verified", ("materials/Prague/Wall.TGA", Tga(64, 128)), ("materials/Prague/other.tga", Tga(8, 8)));
        var m = Add(pack);
        Assert.Empty(m.VerifyAndMark());
        Assert.True(Prepare().CanStart);
        var wall = Path.Combine(m.DataFolder, "materials", "Prague", "Wall.TGA");
        File.WriteAllBytes(wall, Tga(64, 128, red: 7));                                  // the same size: the start's check by size does not see it
        Assert.Empty(m.CheckWhole());
        Assert.Equal(["materials/Prague/Wall.TGA: changed since the import"], Store().Installed().Installed["verified"].VerifyAndMark());
        File.WriteAllBytes(wall, Tga(64, 128));                                           // put back by hand: still an error, until added again
        var error = Assert.Single(Prepare().Errors);
        Assert.Equal("error: module verified: materials/Prague/Wall.TGA: changed since the import (found by Verify): remove the module and add it again; "
                     + "add it again from its source (module-add), or turn it off", error.Line);
        Add(pack);
        Assert.False(File.Exists(Path.Combine(m.Folder, PlayerModule.DamagedName)));
        Assert.True(Prepare().CanStart);
    }

    [Fact]
    public void Files_put_in_by_hand_are_one_problem_of_the_module_and_an_import_again_takes_them_out()
    {
        string pack = Pack("Shop Mod", ("materials/Prague/shop.tga", Tga(8, 8)), ("materials/Prague/Wall.TGA", Tga(64, 128)));
        var m = Add(pack);
        Put(Path.Combine(m.DataFolder, "misc", "game.ntt"), "by hand");
        Put(Path.Combine(m.DataFolder, "misc", "Templates", "darkAges.not"), "by hand");
        Assert.Equal(["misc/Templates/darkAges.not", "misc/game.ntt"], m.HandAdded());
        var error = Assert.Single(Prepare().Errors);
        Assert.Equal("added", error.Kind);
        Assert.Equal(["misc/Templates/darkAges.not", "misc/game.ntt"], error.Files);
        Assert.Equal("error: module shop-mod: misc/Templates/darkAges.not, misc/game.ntt: put there after the import: add the module again from its source to take it in; "
                     + "add it again from its source (module-add), or turn it off", error.Line);

        // The pack's own file edited and the hand's files left where they are: the import again takes the edit in and the hand's files out
        // of its copy (never out of the pack).
        Put(Path.Combine(pack, "materials", "Prague", "Wall.TGA"), Tga(64, 128, red: 9));
        Put(Path.Combine(pack, "materials", "Prague", "extra.tga"), Tga(8, 8));
        var report = Store().Add(pack, new ModuleImportOptions { By = "tests" }, Base(), TestContext.Current.CancellationToken);
        Assert.True(report.Accepted, report.Refusal);
        Assert.NotNull(report.There);
        Assert.Equal(["misc/Templates/darkAges.not", "misc/game.ntt"], report.Removed.Order(StringComparer.Ordinal));
        Assert.False(File.Exists(Path.Combine(m.DataFolder, "misc", "game.ntt")));
        Assert.False(File.Exists(Path.Combine(m.DataFolder, "misc", "Templates", "darkAges.not")));
        Assert.Equal(PlayerModule.Hash(Tga(64, 128, red: 9)), report.Module!.Record.Files.Single(f => f.Path == "materials/Prague/Wall.TGA").Sha256);
        Assert.Contains(report.Module.Record.Files, f => f.Path == "materials/Prague/extra.tga");
        Assert.Empty(report.Module.CheckWhole());
        Assert.True(Prepare().CanStart);
    }

    [Fact]
    public void A_module_of_the_same_id_from_another_folder_is_refused_as_already_there_and_replaced_when_asked_in_its_place()
    {
        var first = Add(Pack("first", ("materials/Prague/first.tga", Tga(8, 8))));
        string oldPack = Pack("Texturemod", ("materials/Prague/Wall.TGA", Tga(64, 128)), ("materials/Prague/old.tga", Tga(8, 8)));
        Add(oldPack);
        Add(Pack("last", ("materials/Prague/last.tga", Tga(8, 8))));
        Assert.Null(Store().Enable("texturemod", false));
        string newPack = Path.Combine(root, "moved", "Texturemod");
        Put(Path.Combine(newPack, "materials", "Prague", "Wall.TGA"), Tga(64, 128, red: 5));
        Put(Path.Combine(newPack, "misc", "templates", "things.not"), "");

        var refused = Store().Add(newPack, new ModuleImportOptions { By = "tests" }, Base(), TestContext.Current.CancellationToken);
        Assert.Equal(ModuleRefusal.AlreadyThere, refused.Reason);
        Assert.Equal(Path.GetFullPath(oldPack), refused.There!.Record.Source);
        Assert.Equal($"a module texturemod is there already (from {Path.GetFullPath(oldPack)}): remove it first (module-remove texturemod), give this one another id (--id <id>), or --replace",
                     refused.Refusal);
        Assert.Equal(ModuleRefusal.Other, Store().Add(Pack("nothing", ("readme.txt", [1])), new ModuleImportOptions(), Base(), TestContext.Current.CancellationToken).Reason);
        Assert.Equal(ModuleRefusal.None, Store().Add(Pack("fine", ("materials/Prague/fine.tga", Tga(8, 8))), new ModuleImportOptions(), Base(), TestContext.Current.CancellationToken).Reason);

        var replaced = Store().Add(newPack, new ModuleImportOptions { Replace = true, By = "tests" }, Base(), TestContext.Current.CancellationToken);
        Assert.True(replaced.Accepted, replaced.Refusal);
        Assert.Equal(["materials/Prague/old.tga"], replaced.Removed);
        var list = Store().List(out _).Modules;
        Assert.Equal(["first", "texturemod", "last", "fine"], list.Select(e => e.Id));         // its place kept
        Assert.False(list.Single(e => e.Id == "texturemod").Enabled);                          // and off, as it was
        Assert.Equal(Path.GetFullPath(newPack), replaced.Module!.Record.Source);
        Assert.Equal(["materials/Prague/Wall.TGA", "misc/templates/things.not"], replaced.Module.Record.Files.Select(f => f.Path).Order(StringComparer.Ordinal));
        Assert.True(File.Exists(Path.Combine(oldPack, "materials", "Prague", "old.tga")));     // the old pack is never touched
        Assert.Equal("first", first.Id);
    }
}
