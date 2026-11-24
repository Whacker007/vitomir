// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Host;
using Vitomir.Platform.Setup;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// **The Desktop and Start menu shortcuts**: <see cref="Shortcuts"/> over a faked writer (the real one is the shell's COM
/// ShellLink, Windows' only), <c>--setup shortcut</c>, and the launcher's two switches and Update.
/// </summary>
public sealed class ShortcutsTests
{
    /// <summary>The machine's two folders and the links in them, in memory.</summary>
    sealed class FakeWriter : IShortcutWriter
    {
        public readonly Dictionary<string, ShortcutLink> Links = new(StringComparer.OrdinalIgnoreCase);
        public string? Desktop = Path.Combine(Root, "Desktop"), Programs = Path.Combine(Root, "Start Menu", "Programs");
        public string? Folder(ShortcutPlace place) => place == ShortcutPlace.Desktop ? Desktop : Programs;
        public ShortcutLink? Read(string path) => Links.GetValueOrDefault(path);
        public void Write(string path, ShortcutLink link) => Links[path] = link;
        public void Delete(string path) => Links.Remove(path);
    }

    static readonly string Root = Path.Combine(Path.GetTempPath(), "vitomir-shortcuts-test");
    static readonly string Here = Path.Combine(Root, "Games", "Vitomir"), Before = Path.Combine(Root, "Old", "Vitomir");
    static string DesktopLink(FakeWriter w) => Path.Combine(w.Desktop!, Shortcuts.FileName);

    [Fact]
    public void Off_by_default_on_makes_Vitomir_lnk_to_the_launcher_in_its_folder_and_off_removes_only_Vitomir_s()
    {
        var w = new FakeWriter();
        var s = new Shortcuts(w, Here);
        Assert.Equal(ShortcutState.Off, s.Status(ShortcutPlace.Desktop).State);           // nothing made without the player
        Assert.Empty(w.Links);

        var (on, _) = s.Set(ShortcutPlace.Desktop, true);
        Assert.Equal(ShortcutState.On, on.State);
        var link = w.Links[DesktopLink(w)];
        Assert.Equal(new ShortcutLink(Path.Combine(Here, "vitomir-launcher.exe"), Here, Path.Combine(Here, "vitomir-launcher.exe")), link);
        Assert.Equal(ShortcutState.Off, s.Status(ShortcutPlace.StartMenu).State);         // each place its own
        s.Set(ShortcutPlace.StartMenu, true);
        Assert.True(w.Links.ContainsKey(Path.Combine(w.Programs!, "Vitomir.lnk")));

        Assert.Equal(ShortcutState.Off, s.Set(ShortcutPlace.Desktop, false).Status.State);
        Assert.False(w.Links.ContainsKey(DesktopLink(w)));

        // A Vitomir.lnk that opens something else is not Vitomir's: never removed, never overwritten.
        var theirs = new ShortcutLink(@"C:\Games\VTMR\vampire.exe", @"C:\Games\VTMR", "");
        w.Links[DesktopLink(w)] = theirs;
        Assert.Equal(ShortcutState.NotOurs, s.Set(ShortcutPlace.Desktop, false).Status.State);
        Assert.Equal(ShortcutState.NotOurs, s.Set(ShortcutPlace.Desktop, true).Status.State);
        Assert.Equal(theirs, w.Links[DesktopLink(w)]);

        // No folder: said, nothing written.
        w.Desktop = null;
        Assert.Equal(ShortcutState.NoFolder, s.Set(ShortcutPlace.Desktop, true).Status.State);
    }

    [Fact]
    public void A_shortcut_to_the_old_place_of_a_moved_Vitomir_folder_is_said_and_Update_points_it_here()
    {
        var w = new FakeWriter();
        new Shortcuts(w, Before).Set(ShortcutPlace.Desktop, true);                       // made before the folder moved
        var s = new Shortcuts(w, Here);
        var moved = s.Status(ShortcutPlace.Desktop);
        Assert.Equal(ShortcutState.Moved, moved.State);
        Assert.Equal(Path.Combine(Before, "vitomir-launcher.exe"), moved.PointsAt);
        Assert.Contains(Before, Shortcuts.Note(moved));
        Assert.Equal(ShortcutState.On, s.Update(ShortcutPlace.Desktop).Status.State);
        Assert.Equal(Path.Combine(Here, "vitomir-launcher.exe"), w.Links[DesktopLink(w)].Target);
        // Off on one of Vitomir's that opens the old place removes it too (its target is a Vitomir launcher).
        new Shortcuts(w, Before).Set(ShortcutPlace.StartMenu, true);
        Assert.Equal(ShortcutState.Off, s.Set(ShortcutPlace.StartMenu, false).Status.State);
    }

    [Fact]
    public void Setup_shortcut_on_the_command_line_turns_each_on_and_off_and_says_how_both_stand()
    {
        var w = new FakeWriter();
        var where = new UserFolderPlace(null, Path.Combine(Here, "bin"), "");          // the package's programs are in Vitomir\bin
        var lines = new List<string>();
        int Run(params string[] args) => SetupCommand.Run(new Cli(["--setup", .. args]), lines.Add, cancel: TestContext.Current.CancellationToken, where: where, shortcuts: w);

        Assert.Equal(SetupCommand.Exit.Done, Run("shortcut", "desktop", "on"));
        Assert.Equal(Path.Combine(Here, "vitomir-launcher.exe"), w.Links[DesktopLink(w)].Target);
        Assert.Equal(SetupCommand.Exit.Done, Run("shortcut", "startmenu", "on"));
        lines.Clear();
        Assert.Equal(SetupCommand.Exit.Done, Run("shortcut"));
        Assert.Equal(2, lines.Count(l => l.Contains(": on (", StringComparison.Ordinal)));
        Assert.Equal(SetupCommand.Exit.Done, Run("shortcut", "desktop", "off"));
        Assert.Single(w.Links);
        Assert.Equal(SetupCommand.Exit.Usage, Run("shortcut", "taskbar", "on"));
        if (Shortcuts.ThisMachine() is null)                                            // not Windows: none to make, said
            Assert.Equal(SetupCommand.Exit.Failed, SetupCommand.Run(new Cli(["--setup", "shortcut"]), lines.Add, cancel: TestContext.Current.CancellationToken, where: where));
    }

    [Fact]
    public void The_launcher_s_switches_show_the_disk_at_start_act_at_once_and_a_moved_one_offers_Update()
    {
        var w = new FakeWriter();
        new Shortcuts(w, Before).Set(ShortcutPlace.StartMenu, true);                     // left from the folder's old place
        var root = Directory.CreateTempSubdirectory("vitomir-shortcuts-launcher-").FullName;
        try
        {
            var user = Path.Combine(root, "user");
            var options = new Vitomir.Launcher.LauncherOptions
            {
                Developer = true, UserData = user, ProgramFolder = Here, GameProgram = Path.Combine(Here, "vitomir-platform.exe"),
                GameDir = () => null, ModulesFolder = Path.Combine(AppContext.BaseDirectory, "Modules"), ShortcutWriter = w,
            };
            var service = new SetupService(user, DataManifest.Builtin, new LauncherTests.FakeMachine(), new LauncherTests.FakeImporter(), "the tests");
            var launcher = new Vitomir.Launcher.Launcher(options, new Vitomir.Launcher.SetupPage(service, new LauncherTests.FakeShell(), _ => { }),
                                                         _ => throw new InvalidOperationException("no game"), new LauncherTests.FakeShell(), _ => { });
            launcher.Start([]);
            launcher.Fill();
            var d = launcher.Data;
            Assert.True(d.Flag("hasShortcuts"));
            Assert.False(d.Flag("shortcutDesktop"));                                       // off: none there
            Assert.True(d.Flag("shortcutStart") && d.Flag("shortcutStartMoved"));          // the old place's, said with Update
            Assert.Contains(Before, d.Text("shortcutStartNote"));
            Assert.Empty(launcher.Problems);

            d.Set("shortcutDesktop", true);                                                // the switch turned: it acts at once
            launcher.Act("shortcut", "desktop");
            Assert.Equal(Path.Combine(Here, "vitomir-launcher.exe"), w.Links[DesktopLink(w)].Target);
            launcher.Act("shortcutupdate", "startmenu");
            launcher.Fill();
            Assert.False(d.Flag("shortcutStartMoved"));
            Assert.Equal(Path.Combine(Here, "vitomir-launcher.exe"), w.Links[Path.Combine(w.Programs!, "Vitomir.lnk")].Target);
            d.Set("shortcutDesktop", false);
            launcher.Act("shortcut", "desktop");
            launcher.Fill();
            Assert.False(d.Flag("shortcutDesktop"));
            Assert.False(w.Links.ContainsKey(DesktopLink(w)));
        }
        finally { Directory.Delete(root, true); }
    }
}
