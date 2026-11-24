// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Vfs;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The recorded runs, replayed and checked** (the replay harness before any
/// restructuring or threading; ours; <c>--replay</c> and <c>--replay-check</c></i>).
/// <c>Replays/</c> holds sixteen short runs recorded with <c>vitomir --record</c>, each a tape of inputs and
/// names only and the digest of the simulation at every frame the
/// recording took (<see cref="SimDigest"/>). Each is replayed with <c>--replay-check</c>, which stops at the
/// first frame and field whose digest differs: so any change that alters what the game does shows here,
/// at its first frame, before its consequences hide it. A tape with one frame's step changed, or one click
/// changed, must be caught at that frame. They run the built platform's program, <c>vitomir-platform --game classic --developer</c>,
/// beside this project's (build <c>src/Vitomir.Platform.Host</c> too; <c>vitomir.exe</c> before) on the install, each
/// with a temporary user data folder, and skip without either. A digest recorded on another platform (its <c>recorded on</c> line) is
/// not compared — the C runtimes' <c>sinf</c> and its fellows may round differently — and the replay is
/// checked against itself instead, twice over. They are the slowest tests in the project (eighteen runs of the
/// game; about eight minutes on a machine with no graphics card): <c>VITOMIR_NO_REPLAYS=1</c> leaves them out
/// of a quick run, never a final one.
/// <para>
/// **What each run is for**, so that a system's change shows in a run made to exercise it (each one's launch
/// and what was seen in it):
/// </para>
/// <list type="bullet">
/// <item><c>mon1-opening</c>: the Monastery's opening conversation, the camera turned, the console, the
/// inventory, a walk, the rats and zombus at the half-corpse.</item>
/// <item><c>arc3-fight</c>: Christof among the hoppers of <c>ARC3_17_1</c> with Erik, clicking to attack until he
/// falls.</item>
/// <item><c>new-game</c>: a new game's first minute in the Convent, as a human, with its autosave.</item>
/// <item><c>net-host</c>, <c>net-player</c>: a network game on 127.0.0.1 from each side, each replayed alone from
/// the datagrams it read.</item>
/// <item><c>vienna-party</c>: Vienna. Christof with Wilhem, Serena and Erik against three Teutonic knights: the
/// stance buttons, the companions' own disciplines (Fortitude, Awe, Blood Healing), each member's fall, the
/// steering passing on when Christof falls, the end when the last one does.</item>
/// <item><c>london-guns</c>: London. The modern Christof's revolver against the Society's soldiers: the instant
/// shot, the reload from the pack (a seventh shot from a six-round cylinder), their shotgun and incinerator.</item>
/// <item><c>newyork-warehouse</c>: New York. The modern Christof among the Giovanni, Wilhem in the party: two
/// grenades thrown and their blasts, a blood vial drunk from the belt, rockets, revolvers and an assault
/// rifle, Christof's death by a rocket and the steering passing to Wilhem.</item>
/// <item><c>chantry-disciplines</c>: the Tremere chantry's scripted entrance (a line and the cutscene camera),
/// then Fortitude, a Fireball, Fire Storm and Shape of the Beast cast by the player with the blood spent,
/// the wolf's bites, and the Tremere's own Fireballs, Feed and Ignore the Searing Flame.</item>
/// <item><c>arc3-hammer-fixes</c>: **not a faithful run** — six Fixes on. The hoppers again: a run on the driving
/// keys into a wall (<c>bodycollision</c> keeps the body off it), the Berserker war hammer taken off and put
/// on by <c>quickequip</c>, Fortitude, and the hammer's blows: with <c>berserker</c> its frenzy takes hold,
/// where 1.1's script throws, until Christof frenzies and takes no more clicks.</item>
/// <item><c>monastery-bow</c>: Christof and Wilhem in the Monastery's hall: a skeleton beheaded at the start,
/// four arrows from a short bow, holy water and Greek fire thrown from the belt, a skeleton archer's arrows
/// coming back.</item>
/// <item><c>club-feed</c>: London's Club Tenebrae: Feed cast on a dancer and held to her death by the drain, the
/// Humanity lost for it, and the console's <c>frenzyme</c>.</item>
/// <item><c>smithy-shop-travel</c>: the smith's conversation with a reply clicked, his shop opened by his own
/// script, a buckler bought and the sword sold, the door clicked, Old Town loaded, the autosave written and the
/// place's opening camera path begun.</item>
/// <item><c>convent-morning</c>: a chapter scene played through: the Convent's morning conversation with its
/// second reply clicked (and the Humanity it costs), the quest and experience, the script's jump to the
/// Convent by day and the autosave there.</item>
/// <item><c>inn-inventory</c>: Potence cast, a blood vial drunk, a shield and a torch changed over in the pack, the
/// sword put down on the floor and picked up again, <c>quickequip</c>, and the console's stake put in and
/// pulled out.</item>
/// <item><c>blastwall-grenade</c>: the <c>blastwall</c> scenario: a grenade thrown at the soldier in sight, the
/// one behind the wall unhurt.</item>
/// <item><c>gunrange-fixes</c>: **not a faithful run** — six Fixes on. A held soldier at the very edge of a
/// pistol's reach: with <c>gunreach</c> the shots land, with <c>wakeonhit</c> he is let go and comes, and with
/// <c>shapetorch</c> Christof takes the wolf's shape with a torch in his hand and bites him.</item>
/// </list>
/// </summary>
public class ReplayBaselineTests
{
    static readonly string Here = AppContext.BaseDirectory;

    /// <summary>
    /// **The program a tape is played by**: the platform's, <c>vitomir-platform --game classic --developer</c>, built
    /// beside this test project's build (<c>src/Vitomir.Platform.Host/bin/&lt;config&gt;/&lt;tfm&gt;/</c>, its modules laid out beside it).
    /// Earlier, the tapes were played by <c>vitomir.exe</c>; the program and its tools answer the tapes through the same frame.
    /// </summary>
    internal static string? Program()
    {
        var dir = new DirectoryInfo(Here.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        // …/src/Vitomir.Engine.Tests/bin/Release/net10.0 → …/src/Vitomir.Platform.Host/bin/Release/net10.0
        var tfm = dir.Name;
        var config = dir.Parent?.Name;
        var src = dir.Parent?.Parent?.Parent?.Parent;
        if (src is null || config is null) return null;
        var app = Path.Combine(src.FullName, "Vitomir.Platform.Host", "bin", config, tfm, OperatingSystem.IsWindows() ? "vitomir-platform.exe" : "vitomir-platform");
        return File.Exists(app) && Directory.Exists(Path.Combine(Path.GetDirectoryName(app)!, "Modules", "Classic")) ? app : null;
    }

    /// <summary><c>vitomir-tools</c> beside this test project's build (<c>src/Vitomir.Tools/bin/&lt;config&gt;/&lt;tfm&gt;/</c>), for <c>--replay-folder</c>.</summary>
    internal static string? Tools()
    {
        var dir = new DirectoryInfo(Here.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var src = dir.Parent?.Parent?.Parent?.Parent;
        if (src is null || dir.Parent is null) return null;
        var app = Path.Combine(src.FullName, "Vitomir.Tools", "bin", dir.Parent.Name, dir.Name, OperatingSystem.IsWindows() ? "vitomir-tools.exe" : "vitomir-tools");
        return File.Exists(app) ? app : null;
    }

    internal static (string Program, string Game) Needs()
    {
        var game = GameFiles.Locate();
        Assert.SkipWhen(game is null, "game install not found (set VTMR_GAME_DIR to the folder holding Resource.nob)");
        Assert.SkipWhen(Environment.GetEnvironmentVariable("VITOMIR_NO_REPLAYS") == "1", "VITOMIR_NO_REPLAYS=1: the replays left out of this run");
        var app = Program();
        Assert.SkipWhen(app is null, "the platform's program is not built beside the tests (build src/Vitomir.Platform.Host/Vitomir.Platform.Host.csproj in the same configuration)");
        return (app!, game!);
    }

    /// <summary>The game's own folder in a user data folder under the platform (<c>&lt;user data&gt;/Modules/classic</c>): its saves, settings and caches.</summary>
    internal static string GameFolder(string userData) => Path.Combine(userData, "Modules", "classic");

    /// <summary>The player's save folder in a user data folder under the platform.</summary>
    internal static string SavesOf(string userData) => Path.Combine(GameFolder(userData), "Saves");

    /// <summary>The scripts compiled once for the test run, copied into each run's fresh user folder (a compile is eight seconds here).</summary>
    static readonly string CompiledOnce = Path.Combine(Path.GetTempPath(), $"vitomir-replay-scripts-{Environment.ProcessId}");
    static readonly object Compiling = new();

    /// <summary>
    /// A user data folder made ready for a run: the scripts' compiled cache of an earlier run of this test process put in it, so only
    /// the first run compiles (the cache is the module's user folder's own under the platform). Nothing else is put there.
    /// </summary>
    internal static void Seed(string userData)
    {
        string cache = Path.Combine(GameFolder(userData), "cache", "codex");
        lock (Compiling)
            if (Directory.Exists(CompiledOnce) && !Directory.Exists(cache)) CopyTree(CompiledOnce, cache);
    }

    /// <summary>After a run: its compiled cache kept for the next runs, when none is kept yet.</summary>
    internal static void Keep(string userData)
    {
        string cache = Path.Combine(GameFolder(userData), "cache", "codex");
        lock (Compiling)
            if (!Directory.Exists(CompiledOnce) && Directory.Exists(cache) && Directory.EnumerateFileSystemEntries(cache).Any())
            {
                string making = CompiledOnce + ".making";
                if (Directory.Exists(making)) Directory.Delete(making, recursive: true);
                CopyTree(cache, making);
                Directory.Move(making, CompiledOnce);
                // Gone with the test process: a temporary folder, the test run's own.
                AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { Directory.Delete(CompiledOnce, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { } };
            }
    }

    static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var d in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, d)));
        foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories)) File.Copy(f, Path.Combine(to, Path.GetRelativePath(from, f)), overwrite: true);
    }

    /// <summary>A copy of a baseline in a folder of its own, so a replay's outputs never land in the tests' folder and two tests never share one.</summary>
    static string Copy(string name)
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"vitomir-replay-test-{Guid.NewGuid():N}")).FullName;
        foreach (var suffix in new[] { "", ".digest" })
            File.Copy(Path.Combine(Here, "Replays", name + suffix), Path.Combine(dir, name + suffix));
        return Path.Combine(dir, name);
    }

    static (int Code, string Output) Run(string app, string game, params string[] args) => RunWith(null, app, game, args);

    /// <summary>
    /// The platform's program (<c>vitomir-platform --game classic --developer --oneone &lt;install&gt;</c>, then <paramref name="args"/>),
    /// run to its end with a user data folder of its own (<paramref name="env"/>'s <c>VITOMIR_USERDATA</c>, or a temporary one made and
    /// cleared for the run), its scripts' cache seeded; its exit code and everything it printed.
    /// </summary>
    internal static (int Code, string Output) RunWith(Dictionary<string, string>? env, string app, string game, params string[] args) =>
        RunProgram(env, app, ["--game", "classic", "--developer", "--oneone", game, .. args]);

    /// <summary>Any program run as <see cref="RunWith"/> runs the platform's (a user data folder of its own, seeded), with these arguments as given.</summary>
    internal static (int Code, string Output) RunProgram(Dictionary<string, string>? env, string app, IEnumerable<string> args)
    {
        string? scratch = env is not null && env.ContainsKey("VITOMIR_USERDATA") ? null
            : Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"vitomir-replay-user-{Guid.NewGuid():N}")).FullName;
        string user = scratch ?? env!["VITOMIR_USERDATA"];
        Seed(user);
        var start = new ProcessStartInfo(app)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(app)!,
        };
        foreach (var (k, v) in env ?? []) if (v.Length == 0) start.Environment.Remove(k); else start.Environment[k] = v;   // "" takes one out
        start.Environment["VITOMIR_USERDATA"] = user;
        foreach (var a in args) start.ArgumentList.Add(a);
        // No window where there is no screen to put one on (a container, a CI box): SDL's own offscreen driver.
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && Environment.GetEnvironmentVariable("DISPLAY") is null
            && Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is null && Environment.GetEnvironmentVariable("SDL_VIDEO_DRIVER") is null)
            start.Environment["SDL_VIDEO_DRIVER"] = "offscreen";
        try
        {
            using var p = Process.Start(start)!;
            var err = p.StandardError.ReadToEndAsync();
            var output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(TimeSpan.FromMinutes(10))) { p.Kill(true); Assert.Fail("the replay did not finish in ten minutes"); }
            Keep(user);
            return (p.ExitCode, output + err.Result);
        }
        finally { if (scratch is not null) try { Directory.Delete(scratch, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    internal static string? RecordedOn(string digest) =>
        File.ReadLines(digest).Take(4).Select(l => l.Split("recorded on ")).FirstOrDefault(p => p.Length == 2)?[1].Trim();

    internal static string Checked(string output) =>
        string.Join('\n', output.Split('\n').Where(l => l.StartsWith("replay", StringComparison.Ordinal)));

    [Theory]
    [InlineData("mon1-opening.tape")]
    [InlineData("arc3-fight.tape")]
    [InlineData("new-game.tape")]
    [InlineData("net-host.tape")]
    [InlineData("net-player.tape")]
    [InlineData("vienna-party.tape")]
    [InlineData("london-guns.tape")]
    [InlineData("newyork-warehouse.tape")]
    [InlineData("chantry-disciplines.tape")]
    [InlineData("arc3-hammer-fixes.tape")]
    [InlineData("monastery-bow.tape")]
    [InlineData("club-feed.tape")]
    [InlineData("smithy-shop-travel.tape")]
    [InlineData("convent-morning.tape")]
    [InlineData("inn-inventory.tape")]
    [InlineData("gunrange-fixes.tape")]
    [InlineData("title-new-game.tape")]          // begun at the title screen, as a player's are
    public void A_recorded_run_replays_to_the_same_simulation_every_frame(string name)
    {
        var (app, game) = Needs();
        var tape = Copy(name);
        try
        {
            if (RecordedOn(tape + ".digest") == RuntimeInformation.RuntimeIdentifier)
            {
                var (code, output) = Run(app, game, "--replay-check", tape);
                Assert.True(code == 0 && output.Contains("replay-check: the same"), Checked(output));
                return;
            }
            // Another platform's digest: the replay against itself. Twice replayed, the same every frame.
            // Not waiting for the screen (`--replay-fast`), as a check does not: sixteen recordings at the pace they
            // were made would be minutes of the test run for nothing.
            var (c1, o1) = Run(app, game, "--replay", tape, "--replay-fast", "--mute", "--no-log-file");
            Assert.True(c1 == 0, Checked(o1));
            File.Move(tape + ".replay.digest", tape + ".first.digest");
            var (c2, o2) = Run(app, game, "--replay", tape, "--replay-fast", "--mute", "--no-log-file");
            Assert.True(c2 == 0, Checked(o2));
            using var checker = new DigestChecker(DigestReader.Open(tape + ".first.digest"));
            using var second = DigestReader.Open(tape + ".replay.digest");
            while (second.Next() is { } frame) Assert.Null(checker.Check(frame));
            Assert.False(checker.RecordedLeft);
        }
        finally { Directory.Delete(Path.GetDirectoryName(tape)!, recursive: true); }
    }

    /// <summary>
    /// This machine's digest of the untouched run, where the tape's was recorded on another platform — or the
    /// recorded one, here.
    /// </summary>
    static void DigestForHere(string app, string game, string tape)
    {
        if (RecordedOn(tape + ".digest") == RuntimeInformation.RuntimeIdentifier) return;
        var clean = Copy(Path.GetFileName(tape));
        try
        {
            var (c0, o0) = Run(app, game, "--replay", clean, "--replay-fast", "--mute", "--no-log-file");
            Assert.True(c0 == 0, Checked(o0));
            File.Copy(clean + ".replay.digest", tape + ".digest", overwrite: true);
        }
        finally { Directory.Delete(Path.GetDirectoryName(clean)!, recursive: true); }
    }

    [Fact]
    public void A_replay_with_a_click_changed_is_caught_at_that_clicks_frame()
    {
        var (app, game) = Needs();
        var tape = Copy("arc3-fight.tape");
        try
        {
            DigestForHere(app, game, tape);
            // The fight's second click — Christof's attack on a hopper (the first found him still busy) — made a right click.
            var lines = File.ReadAllLines(tape).ToList();
            int down = lines.Select((l, i) => (l, i)).Where(p => p.l.StartsWith("ev button down b=1 ", StringComparison.Ordinal)).Skip(1).First().i;
            int up = lines.FindIndex(down, l => l.StartsWith("ev button up b=1 ", StringComparison.Ordinal));
            lines[down] = lines[down].Replace(" b=1 ", " b=3 ");
            lines[up] = lines[up].Replace(" b=1 ", " b=3 ");
            int at = int.Parse(lines[lines.FindLastIndex(down, l => l.StartsWith("frame ", StringComparison.Ordinal))][6..], CultureInfo.InvariantCulture);
            File.WriteAllLines(tape, lines);
            var (code, output) = Run(app, game, "--replay-check", tape);
            Assert.Equal(1, code);
            Assert.Contains($"replay-check: DIVERGED at frame {at.ToString(CultureInfo.InvariantCulture)}:", output);
        }
        finally { Directory.Delete(Path.GetDirectoryName(tape)!, recursive: true); }
    }

    [Fact]
    public void A_replay_with_one_step_changed_is_caught_at_that_frame()
    {
        var (app, game) = Needs();
        var tape = Copy("arc3-fight.tape");
        try
        {
            const int At = 150;
            // Frame 150's step made a quarter of a second: every system of that frame is stepped by it.
            var lines = File.ReadAllLines(tape).ToList();
            int frame = lines.IndexOf($"frame {At}");
            Assert.True(frame > 0);
            int step = lines.FindIndex(frame, l => l.StartsWith("step ", StringComparison.Ordinal));
            lines[step] = "step 0.25";
            File.WriteAllLines(tape, lines);
            DigestForHere(app, game, tape);
            var (code, output) = Run(app, game, "--replay-check", tape);
            Assert.Equal(1, code);
            Assert.Contains($"replay-check: DIVERGED at frame {At.ToString(CultureInfo.InvariantCulture)}: the frame's clock differs", output);
            Assert.Contains($"{At - 1} frames agreed before it", output);
        }
        finally { Directory.Delete(Path.GetDirectoryName(tape)!, recursive: true); }
    }

    /// <summary>
    /// **A recording begun at the title screen**, as a player's are, replayed the way <c>--replay-folder</c>
    /// replays it: not watched, in a small window at a slot of its own, the opening films and the new game's film
    /// skipped (they played when it was made: `title-new-game` was recorded with stand-in films in the install). In
    /// Once the window's switches were left in the replay's launch, read as a start, and every such replay skipped the
    /// title screen and left its tape at frame 0 — while all the other baselines, begun with <c>--level</c>, passed.
    /// </summary>
    [Fact]
    public void A_recording_begun_at_the_title_screen_replays_with_the_folders_switches()
    {
        var (app, game) = Needs();
        var tape = Copy("title-new-game.tape");
        try
        {
            DigestForHere(app, game, tape);
            var (code, output) = Run(app, game, "--replay-check", tape, "--replay-slot", "2", "--mute");
            Assert.True(code == 0 && output.Contains("replay-check: the same"), Checked(output));
            Assert.DoesNotContain("the run left its tape", output);
            Assert.Contains("shown in a window of 640x360", output);     // drawn at 1280x720, shown small
            var (fullCode, full) = Run(app, game, "--replay-check", tape, "--replay-full", "--mute");
            Assert.True(fullCode == 0 && full.Contains("replay-check: the same"), Checked(full));
            Assert.DoesNotContain("shown in a window of", full);
        }
        finally { Directory.Delete(Path.GetDirectoryName(tape)!, recursive: true); }
    }

    /// <summary>
    /// **A tape of the menus only says so** (ours). A run that sat at the title screen and quit: its
    /// check said "the same — 0 frames agreed", which reads as a check that found nothing wrong rather than one that
    /// had nothing to compare. Recorded here at the title screen (<c>--menu</c>), the films skipped, for forty frames, with the
    /// settings kept in the tape's own folder (<c>VITOMIR_USERDATA</c>) so the player's are never written.
    /// </summary>
    [Fact]
    public void A_tape_of_the_menus_only_says_it_had_no_frames_to_compare()
    {
        var (app, game) = Needs();
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"vitomir-replay-test-{Guid.NewGuid():N}")).FullName;
        try
        {
            var env = new Dictionary<string, string> { ["VITOMIR_USERDATA"] = Path.Combine(dir, "user") };
            Directory.CreateDirectory(env["VITOMIR_USERDATA"]);
            var (recorded, recording) = RunWith(env, app, game, "--record", Path.Combine(dir, "tapes") + Path.DirectorySeparatorChar,
                                                "--menu", "--skip-intro", "--size", "640x480", "--frames", "40", "--seed", "3", "--mute", "--no-log-file");
            Assert.True(recorded == 0, recording);
            var tape = Assert.Single(Directory.GetFiles(Path.Combine(dir, "tapes"), "play-*.tape.gz"));

            var (code, output) = RunWith(env, app, game, "--replay-check", tape, "--mute");
            // Matched without the line's dash: a dash comes through a redirected output in the console's own code page
            // on Windows, which the reader here need not share (as a Windows run showed).
            Assert.True(code == 0 && output.Contains("replay-check: the same") && output.Contains("the menus only: no game ran on this tape"),
                        Checked(output));
            Assert.DoesNotContain("0 frames agreed", output);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// **A walk through the menus is checked step by step** (ours, <c>MenuTrail</c>): until then a tape of the
    /// menus alone compared nothing; now its digest keeps each step (a screen shown or put away, a button clicked) and
    /// the check compares them, and the next batches move 1.1's menus' buttons about. Recorded here at 640 x 480, in
    /// our lettering: the Options screen, the Fixes page and back, the Gameplay screen and back, done. Then the
    /// recording's digest is changed to say the Controls button was clicked where the Gameplay one was, and the check
    /// finds it at that step.
    /// </summary>
    [Fact]
    public void A_walk_through_the_menus_is_checked_step_by_step()
    {
        var (app, game) = Needs();
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"vitomir-replay-test-{Guid.NewGuid():N}")).FullName;
        try
        {
            var env = new Dictionary<string, string> { ["VITOMIR_USERDATA"] = Path.Combine(dir, "user") };
            Directory.CreateDirectory(env["VITOMIR_USERDATA"]);
            // Window pixels at 640 x 480: the main menu's options, the Options screen's fixes, the Fixes page's cancel,
            // game, the Gameplay screen's cancel, done. In 1.1's stretch, said, where a window pixel at this size
            // is the sheet's point: the default layout's sheet is shorter than a 4:3 picture.
            var (recorded, recording) = RunWith(env, app, game, "--record", Path.Combine(dir, "tapes") + Path.DirectorySeparatorChar,
                                                "--menu", "--skip-intro", "--size", "640x480", "--ui-layout", "stretch", "--frames", "200", "--seed", "3", "--mute", "--no-log-file",
                                                "--menu-lettering", "ours",
                                                "--menu-steps", "321,220;wait:10;320,296;wait:10;265,447;wait:10;320,120;wait:10;265,447;wait:10;320,330");
            Assert.True(recorded == 0, recording);
            var tape = Assert.Single(Directory.GetFiles(Path.Combine(dir, "tapes"), "play-*.tape.gz"));
            var digest = tape[..^".gz".Length] + ".digest.gz";
            var steps = ReadGz(digest).Split('\n').Where(l => l.StartsWith("m ", StringComparison.Ordinal)).ToList();
            Assert.Contains(steps, l => l.EndsWith("show vitomirFixes", StringComparison.Ordinal));
            Assert.Contains(steps, l => l.EndsWith("click Options opgameplaybutton", StringComparison.Ordinal));

            var (code, output) = RunWith(env, app, game, "--replay-check", tape, "--mute");
            Assert.True(code == 0 && output.Contains("replay-check: the same") && output.Contains($"{steps.Count} steps through the menus agreed"),
                        Checked(output));

            WriteGz(digest, ReadGz(digest).Replace("click Options opgameplaybutton", "click Options opcontrolsbutton"));
            var (changed, said) = RunWith(env, app, game, "--replay-check", tape, "--mute");
            Assert.True(changed == 1 && said.Contains("the menus step differs") && said.Contains("opcontrolsbutton"), Checked(said));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    static string ReadGz(string path)
    {
        using var gz = new System.IO.Compression.GZipStream(File.OpenRead(path), System.IO.Compression.CompressionMode.Decompress);
        using var r = new StreamReader(gz);
        return r.ReadToEnd();
    }

    static void WriteGz(string path, string text)
    {
        using var gz = new System.IO.Compression.GZipStream(File.Create(path), System.IO.Compression.CompressionLevel.Fastest);
        using var w = new StreamWriter(gz);
        w.Write(text);
    }
}
