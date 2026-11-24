// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.IO.Compression;
using System.Security.Cryptography;
using Vitomir.Engine.Save;
using Vitomir.Engine.Video;
using Vitomir.Engine.Vfs;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Vitomir's own game folder** (`GameData`): made from a stand-in install with no game
/// content in it — archives unpacked and laid out by kind, the install only read, every file checked,
/// a stop picked up again, nothing deleted — and, above all, **read back exactly as the install reads**:
/// every path, every listing, every look-up by file name alone.
/// </summary>
public sealed class GameDataTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), $"vitomir-data-{Guid.NewGuid():N}");
    string Install => Path.Combine(root, "VTMR");
    string Game => Path.Combine(root, "Vitomir", "game");

    public GameDataTests()
    {
        // Two archives, the second a mod's that holds a path the first has too; a loose file over an archive's.
        Nob("Codex.nob", ("Guard.class", 40), ("3d/models/chair.nod", 90));
        Nob("Resource.nob", ("3d/models/chair.nod", 91), ("3d/anims/sit.nad", 50), ("3d/materials/wood.tga", 60),
            ("materials/prague/wall.tga", 70), ("ui/icons/cup.tga", 20), ("ui/main.nui", 30), ("misc/templates/_a.not", 80),
            ("misc/game.ndd", 25), ("levels/P1_HAVN.nil", 300), ("scenes/HAVN_1.nsd", 45), ("sounds/voice/hi.wav", 64),
            ("strings/credits.nls", 12), ("paths/cam.ncp", 13));
        Nob("ZZMod.nob", ("materials/prague/wall.tga", 71), ("newstuff/thing.dat", 5));
        Put("ui/icons/cup.tga", 21);                                    // a texture pack's loose file, over the archive's
        Put("Codex/SP/Guard.java", 120);
        Put("Chronicles/prague.nsc", 90);
        Put("Video/intro.bik", 2000);
        Put("Video/NYC.BIK", 200);
        Put("Sounds/Dialog/a.mp3", 64);
        Put("Materials/lightbox.tga", 33);
        Put("masquerade.ini", 40);
        Put("classes.zip", 70);
        Put("SaveGames/SP0/!main.nsg", 200);
        Put("NodSDK/noteditortags.txt", 10);
        Put("Extras/cs/sysinfo.exe", 10);
        Put("Miles/mss.dll", 10);
        Put("Vampire.exe", 100);
        Put("unins000.dat", 10);
        Put("goggame-1207658831.info", 10);
        Put("SHOT0001.BMP", 10);
    }

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    static readonly DateTime Then = new(2000, 5, 31, 12, 0, 0, DateTimeKind.Utc);

    void Put(string relative, int size, byte fill = 7)
    {
        string path = Path.Combine(Install, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Enumerable.Repeat(fill, size).ToArray());
        File.SetLastWriteTimeUtc(path, Then);
    }

    void Nob(string name, params (string Path, int Size)[] entries)
    {
        Directory.CreateDirectory(Install);
        using var zip = ZipFile.Open(Path.Combine(Install, name), ZipArchiveMode.Create);
        foreach (var (p, size) in entries)
        {
            var e = zip.CreateEntry(p, CompressionLevel.NoCompression);
            e.LastWriteTime = new DateTimeOffset(2000, 5, 30, 10, 0, 0, TimeSpan.Zero);
            using var s = e.Open();
            s.Write(Enumerable.Repeat((byte)(size & 0xff), size).ToArray());
        }
    }

    static Dictionary<string, (string Hash, DateTime Written)> Snapshot(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToDictionary(
            f => Path.GetRelativePath(dir, f).Replace('\\', '/'),
            f => (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))), File.GetLastWriteTimeUtc(f)));

    /// <summary>
    /// The heart of it: every path the install answers, the game folder answers with the same bytes;
    /// every listing is the same; every look-up by file name alone finds the same path.
    /// </summary>
    static void SameAsTheInstall(GameFiles install, GameFiles game)
    {
        var paths = install.List("").ToList();
        Assert.NotEmpty(paths);
        foreach (var p in paths.Where(p => !GameData.LeftOut(p) && !(p.EndsWith(".nob", StringComparison.OrdinalIgnoreCase) && !p.Contains('/'))))
        {
            Assert.True(install.TryRead(p, out var a), p);
            Assert.True(game.TryRead(p, out var b), $"the game folder has no {p}");
            Assert.True(a.AsSpan().SequenceEqual(b), $"{p} differs");
            string name = Path.GetFileName(p);
            Assert.Equal(install.FindByFileName(name), game.FindByFileName(name));
            Assert.Equal(install.TryReadMaterialFile(name, out var m1), game.TryReadMaterialFile(name, out var m2));
            Assert.True(m1.AsSpan().SequenceEqual(m2), $"{name} by the material search differs");
        }
        foreach (var folder in new[] { "misc/templates/", "chronicles/", "levels/", "scenes/", "ui/" })
            Assert.Equal(install.List(folder).Where(p => !GameData.LeftOut(p)).Order(StringComparer.OrdinalIgnoreCase),
                         game.List(folder).Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_game_folder_reads_exactly_as_the_install_does()
    {
        var report = GameData.Make(Install, Game, cancel: TestContext.Current.CancellationToken);
        Assert.True(report.Ok, string.Join("; ", report.Problems));
        using var install = new GameFiles(Install);
        using var game = new GameFiles(Game);
        Assert.True(game.IsLayout);
        Assert.False(install.IsLayout);
        SameAsTheInstall(install, game);
        // The loose file wins over the archives, and the first archive over the mod's.
        Assert.Equal(21, game.Read("ui/icons/cup.tga").Length);
        Assert.Equal(90, game.Read("3d/models/chair.nod").Length);
        Assert.Equal(70, game.Read("materials/prague/wall.tga").Length);
    }

    [Fact]
    public void The_files_are_laid_out_by_kind()
    {
        Assert.True(GameData.Make(Install, Game, cancel: TestContext.Current.CancellationToken).Ok);
        var placed = Snapshot(Game).Keys.Where(k => k != GameData.ManifestName).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        Assert.Equal(new[]
        {
            "animations/sit.nad", "camera-paths/cam.ncp", "chronicles/prague.nsc", "interface/main.nui", "levels/P1_HAVN.nil",
            "models/chair.nod", "other/newstuff/thing.dat", "rules/game.ndd", "rules/templates/_a.not", "scenes/HAVN_1.nsd",
            "scripts/classes.zip", "scripts/compiled/Guard.class", "scripts/source/SP/Guard.java", "settings/masquerade.ini",
            "sounds/Dialog/a.mp3", "sounds/voice/hi.wav", "text/credits.nls", "textures/icons/cup.tga", "textures/lightbox.tga",
            "textures/models/wood.tga", "textures/prague/wall.tga", "video/intro.bik", "video/NYC.BIK",
        }.Order(StringComparer.OrdinalIgnoreCase), placed);
        using var game = new GameFiles(Game);
        Assert.Equal(Path.Combine(Game, "video", "NYC.BIK"), Films.Find(game, "nyc"));      // any case, as the install's own spelling
        Assert.Equal(Path.Combine(Game, "video", "intro.bik"), Films.Find(game, Films.NewGame));
        Assert.True(File.Exists(Path.Combine(game.CodexFolder, "SP", "Guard.java")));
        Assert.True(File.Exists(game.ClassesZip));
        Assert.Equal(Path.Combine(Game, "settings", "masquerade.ini"), GameData.OptionsIni(Game));
        Assert.Equal(Path.Combine(Install, "masquerade.ini"), GameData.OptionsIni(Install));
    }

    [Fact]
    public void The_install_is_only_read_and_every_file_is_checked()
    {
        var before = Snapshot(Install);
        var report = GameData.Make(Install, Game, cancel: TestContext.Current.CancellationToken);
        Assert.True(report.Ok);
        Assert.Equal(before, Snapshot(Install));
        var m = GameData.ReadManifest(Game)!;
        Assert.Equal(Path.GetFullPath(Install), m.Source);
        Assert.Equal(["Codex.nob", "Resource.nob", "ZZMod.nob"], m.Archives);
        Assert.All(m.Files, e => Assert.Equal(64, e.Sha256.Length));
        Assert.Contains("SaveGames/SP0/!main.nsg", m.LeftOut);
        Assert.DoesNotContain(m.Files, e => e.One.EndsWith(".nob", StringComparison.OrdinalIgnoreCase));
        Assert.True(GameData.Check(Game, hashes: true)!.Whole);
        Assert.All(m.Files, e => Assert.Equal(e.WrittenUtc, File.GetLastWriteTimeUtc(GameData.Target(Game, e.Path))));
    }

    [Fact]
    public void Run_again_it_writes_only_what_changed_and_sets_aside_what_the_install_no_longer_has()
    {
        Assert.True(GameData.Make(Install, Game, cancel: TestContext.Current.CancellationToken).Ok);
        int files = GameData.ReadManifest(Game)!.Files.Count;
        var again = GameData.Make(Install, Game, cancel: TestContext.Current.CancellationToken);
        Assert.Equal((0, files, 0), (again.Written, again.Kept, again.SetAside));

        Put("Codex/SP/Guard.java", 121, fill: 9);                                   // edited
        File.Delete(Path.Combine(Install, "Materials", "lightbox.tga"));             // taken out
        File.WriteAllText(Path.Combine(Game, "models", "stray.nod.partial"), "x");  // a run that stopped half way
        var third = GameData.Make(Install, Game, cancel: TestContext.Current.CancellationToken);
        Assert.True(third.Ok);
        Assert.Equal((1, files - 2, 1), (third.Written, third.Kept, third.SetAside));
        Assert.False(File.Exists(Path.Combine(Game, "textures", "lightbox.tga")));
        Assert.False(File.Exists(Path.Combine(Game, "models", "stray.nod.partial")));
        var aside = Directory.EnumerateFiles(Path.Combine(root, "Vitomir", "set-aside"), "lightbox.tga", SearchOption.AllDirectories).Single();
        Assert.Equal(33, new FileInfo(aside).Length);                                 // kept, not deleted
    }

    [Fact]
    public void A_folder_that_stops_half_way_has_no_manifest_and_is_not_used_then_carries_on()
    {
        using var stop = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() =>
            GameData.Make(Install, Game, line => { if (line.StartsWith("  ", StringComparison.Ordinal)) stop.Cancel(); }, stop.Token));
        Assert.False(GameData.IsLayout(Game));
        Assert.Null(GameData.Check(Game));
        Assert.Empty(Directory.EnumerateFiles(Game, "*.partial", SearchOption.AllDirectories));
        var finished = GameData.Make(Install, Game, cancel: TestContext.Current.CancellationToken);
        Assert.True(finished.Ok);
        Assert.True(finished.Kept > 0);
        Assert.True(GameData.IsLayout(Game));
    }

    [Fact]
    public void The_check_finds_a_file_gone_or_changed_and_counts_one_added()
    {
        Assert.True(GameData.Make(Install, Game, cancel: TestContext.Current.CancellationToken).Ok);
        File.Delete(Path.Combine(Game, "video", "intro.bik"));
        File.WriteAllBytes(Path.Combine(Game, "settings", "masquerade.ini"), new byte[41]);   // another size
        File.WriteAllBytes(Path.Combine(Game, "scripts", "classes.zip"), new byte[70]);       // the same size, other bytes
        File.WriteAllText(Path.Combine(Game, "extra.txt"), "a stray file");
        var quick = GameData.Check(Game)!;
        Assert.Equal(["video/intro.bik"], quick.Missing);
        Assert.Equal(["settings/masquerade.ini"], quick.Changed);
        var full = GameData.Check(Game, hashes: true)!;
        Assert.Equal(["scripts/classes.zip", "settings/masquerade.ini"], full.Changed.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(["extra.txt"], full.Extra);
        Assert.False(full.Whole);
    }

    [Fact]
    public void It_will_not_work_from_something_that_is_not_an_install_or_into_the_install()
    {
        Assert.False(GameData.Make(Path.Combine(root, "nowhere"), Game, cancel: TestContext.Current.CancellationToken).Ok);
        Assert.Contains("inside", GameData.Make(Install, Path.Combine(Install, "game"), cancel: TestContext.Current.CancellationToken).Problems.Single());
        Assert.False(Directory.Exists(Path.Combine(Install, "game")));
        Assert.True(GameData.Make(Install, Game, cancel: TestContext.Current.CancellationToken).Ok);
        Assert.Contains("Vitomir's own", GameData.Make(Game, Path.Combine(root, "again"), cancel: TestContext.Current.CancellationToken).Problems.Single());
    }

    [Fact]
    public void The_1_1_saves_of_a_game_folder_are_the_install_s_while_it_is_there()
    {
        Assert.True(GameData.Make(Install, Game, cancel: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(Path.GetFullPath(Install), GameData.OriginalInstall(Game));
        Assert.Equal(Install, GameData.OriginalInstall(Install));
        Assert.Equal(Path.Combine(Path.GetFullPath(Install), "SaveGames"), OriginalSaves.For(Game).Folder);
        Directory.Move(Install, Install + "-moved");
        Assert.Null(GameData.OriginalInstall(Game));
        Assert.Empty(OriginalSaves.For(Game).List());
    }

    [Fact]
    public void Only_the_install_is_made_a_game_folder_from()
    {
        Assert.True(GameData.Make(Install, Game, cancel: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(Game, GameFiles.Locate(Game));                         // a folder named is used as it is
        Assert.Null(GameFiles.LocateInstall(Game));                         // but it is not an install
        Assert.Equal(Install, GameFiles.LocateInstall(Install));
    }

    [Fact]
    public void The_Vitomir_folder_is_the_one_above_bin()
    {
        Assert.Equal(Path.Combine(root, "Vitomir"), GameData.HomeOf(Path.Combine(root, "Vitomir", "bin") + Path.DirectorySeparatorChar));
        Assert.Equal(Path.Combine(root, "Vitomir", "BIN", "x"), GameData.HomeOf(Path.Combine(root, "Vitomir", "BIN", "x")));   // a build's output folder
    }

    [Theory]
    [InlineData("3d/models/x.nod", "models/x.nod")]
    [InlineData("3D/Anims/x.nad", "animations/x.nad")]
    [InlineData("3d/materials/x.tga", "textures/models/x.tga")]
    [InlineData("materials/new_york/x.tga", "textures/new_york/x.tga")]
    [InlineData("ui/materials/x.tga", "textures/interface/x.tga")]
    [InlineData("ui/x.nui", "interface/x.nui")]
    [InlineData("misc/templates/x.not", "rules/templates/x.not")]
    [InlineData("Codex/System/Codex.java", "scripts/source/System/Codex.java")]
    [InlineData("masquerade.ini", "settings/masquerade.ini")]
    [InlineData("masquerade.ini.bak", "other/masquerade.ini.bak")]
    [InlineData("Guard.class", "scripts/compiled/Guard.class")]
    [InlineData("readme.txt", "other/readme.txt")]
    public void Where_a_file_goes(string oneOne, string ours) => Assert.Equal(ours, GameData.Place(oneOne));

    [Theory]
    [InlineData("Resource.nob", true)]            // unpacked, not copied
    [InlineData("masquerade.ini", false)]
    [InlineData("Codex/SP/Guard.java", false)]
    [InlineData("3d/models/x.nod", false)]
    [InlineData("Vampire.exe", true)]
    [InlineData("rlDirect.dll", true)]
    [InlineData("SaveGames/SP4/location_0.nsw", true)]
    [InlineData("savegames/SP4/!main.nsg", true)]
    [InlineData("NodSDK/noteditordata.txt", true)]
    [InlineData("unins000.msg", true)]
    [InlineData("dxcfg.ini", true)]
    [InlineData("SHOT0002.BMP", true)]
    [InlineData("Launch Vampire The Masquerade - Redemption.lnk", true)]
    public void What_stays_out(string relative, bool leftOut) => Assert.Equal(leftOut, GameData.LeftOut(relative));

    /// <summary>
    /// **The real install**, laid out and read back against itself path by path — run where
    /// <c>VITOMIR_LAYOUT_CHECK</c> names an empty folder to lay it out in (it is 1.1's whole install, so
    /// it is not made on every run).
    /// </summary>
    [Fact]
    public void The_real_install_laid_out_reads_as_the_install()
    {
        string? into = Environment.GetEnvironmentVariable("VITOMIR_LAYOUT_CHECK");
        Assert.SkipWhen(string.IsNullOrEmpty(into), "set VITOMIR_LAYOUT_CHECK to a folder to lay the install out in");
        using var install = Install_();
        var report = GameData.Make(install.Root, into!, cancel: TestContext.Current.CancellationToken);
        Assert.True(report.Ok, string.Join("; ", report.Problems));
        using var game = new GameFiles(into!);
        SameAsTheInstall(install, game);

        static GameFiles Install_() => Tests.Install.Open();
    }
}
