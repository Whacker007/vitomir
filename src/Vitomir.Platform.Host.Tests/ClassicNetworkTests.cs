// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Vitomir.Platform.Assets;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// **The network game under the platform's program** (running it on one laptop):
/// real processes talking over loopback, each with a temporary
/// <c>VITOMIR_USERDATA</c> and each ending by itself (<c>--settle</c>, <c>--frames</c> or the host's console's <c>/quit</c>), so a run
/// on a Windows PC ends whatever the desktop's size (the windows are given <c>--size</c> and nothing clicks by place). They need the
/// 1.1 install (VTMR_GAME_DIR) and skip without it. Each says what it proves. They run by themselves, not beside the other tests (a joining player's first load
/// must stay within the transport's ten seconds of silence, which a machine busy with other tests can pass). Now each
/// process's user folder starts empty, as a player's first game does: a fresh folder compiles 1.1's scripts in its first load
/// (eight seconds here), and the joining player's machine keeps its connection alive meanwhile (the transport's heartbeat while it
/// loads); the scripts' cache compiled once for the class is gone.
/// </summary>
[Collection(nameof(ClassicNetworkTests))]
public sealed class ClassicNetworkTests
{
    static readonly string Here = AppContext.BaseDirectory;

    /// <summary>The platform's program beside the tests (its Modules\ and Games\ are the tests').</summary>
    static string Platform => Path.Combine(Here, OperatingSystem.IsWindows() ? "vitomir-platform.exe" : "vitomir-platform");

    /// <summary>A UDP port nobody holds now (the system's choice, let go again).</summary>
    static int FreePort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

    /// <summary>
    /// One game process: the platform's program (<c>vitomir-platform --game classic --developer</c>, the 1.1 install given), its own
    /// temporary user folder, its lines kept as they come, its console written to.
    /// </summary>
    sealed class Game : IDisposable
    {
        readonly Process process;
        readonly List<string> lines = [];
        public string UserFolder { get; }

        public Game(string program, string folder, string name, string install, IEnumerable<string> args,
                    bool developer = true, bool logs = false)
        {
            UserFolder = Directory.CreateDirectory(Path.Combine(folder, "user-" + name)).FullName;
            // The scripts already compiled (the class's one compile), as a player's second game finds them.
            var start = new ProcessStartInfo(program)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false,
                WorkingDirectory = folder,
            };
            string[] head = ["--game", "classic", .. developer ? new[] { "--developer" } : [], "--oneone", install, "--log-file", Path.Combine(folder, name + ".platform.log")];
            foreach (var a in head.Concat(args).Concat(logs ? ["--mute", "--no-video"] : ["--no-log-file", "--mute", "--no-video"])) start.ArgumentList.Add(a);
            start.Environment["VITOMIR_USERDATA"] = UserFolder;
            start.Environment["SDL_AUDIODRIVER"] = "dummy";
            // No window where there is no screen to put one on (a container, a CI box): SDL's own offscreen driver.
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && Environment.GetEnvironmentVariable("DISPLAY") is null
                && Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is null && Environment.GetEnvironmentVariable("SDL_VIDEO_DRIVER") is null)
                start.Environment["SDL_VIDEO_DRIVER"] = "offscreen";
            process = new Process { StartInfo = start };
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (lines) lines.Add(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (lines) lines.Add(e.Data); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        /// <summary>Everything it has said so far, one line a line.</summary>
        public string Output { get { lock (lines) return string.Join('\n', lines); } }

        /// <summary>Whether it has said a line holding <paramref name="text"/>.</summary>
        public bool Said(string text) { lock (lines) return lines.Any(l => l.Contains(text, StringComparison.Ordinal)); }

        /// <summary>Waits up to <paramref name="seconds"/> for a line holding <paramref name="text"/>; false when it did not come (or the process ended first).</summary>
        public bool WaitFor(string text, double seconds)
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                if (Said(text)) return true;
                if (process.HasExited) { process.WaitForExit(); return Said(text); }
                Thread.Sleep(100);
            }
            return false;
        }

        /// <summary>A line typed on its console (the headless host's).</summary>
        public void Type(string line) { process.StandardInput.WriteLine(line); process.StandardInput.Flush(); }

        /// <summary>Waits for it to end by itself, up to <paramref name="seconds"/>; its exit code, or null when it had to be stopped.</summary>
        public int? End(double seconds)
        {
            if (process.WaitForExit(TimeSpan.FromSeconds(seconds))) { process.WaitForExit(); return process.ExitCode; }
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            process.WaitForExit();
            return null;
        }

        public void Dispose()
        {
            if (!process.HasExited) { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } process.WaitForExit(); }
            process.Dispose();
        }
    }

    static string Folder(string name) => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"vitomir-net-{name}-{Guid.NewGuid():N}")).FullName;

    static void Clear(string folder) { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    static string Tail(Game game) => string.Join('\n', game.Output.Split('\n').Where(l => !l.StartsWith("modules: ", StringComparison.Ordinal)).TakeLast(25));

    /// <summary>A windowed player joining <paramref name="port"/>: a small window of a given size (nothing placed by the desktop), ending after its frames.</summary>
    static string[] Player(int port, string name, int frames) =>
        ["--join", $"127.0.0.1:{port}", "--name", name, "--player-template", "MP_Erik", "--size", "640x360", "--frames", $"{frames}"];

    /// <summary>
    /// **A headless host and a windowed player, both through <c>vitomir-platform --game classic --developer</c>, and the host moving
    /// her.** Proves: the headless host (<c>--headless-server</c>) is the program's run a frame at a time on a host that opens no
    /// window, its console read between frames (<c>/jump</c>, <c>/players</c>, <c>/quit</c> end it by itself); the player joins, is let
    /// in and plays in the platform's window; the host's <c>/jump</c> sends her alone to another place of the chronicle and her
    /// machine follows — her run ends and the next loads the new place with the session carried (the frame's <c>Restart</c>), the
    /// window and the GPU kept (one GPU opened for both runs); and her character is written back to the module's own folder
    /// (<c>Modules\classic\Crypt\</c>), not the old program's.
    /// </summary>
    [Fact]
    public void A_headless_host_and_a_windowed_player_play_through_the_platform_and_the_host_moves_her()
    {
        var install = GameInstall.Locate();
        Assert.SkipWhen(install is null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        var folder = Folder("platform");
        try
        {
            int port = FreePort();
            using var host = new Game(Platform, folder, "host", install!,
                ["--headless-server", "--mp-chronicle", "MP_CurseDarkness", "--no-upnp", "--port", $"{port}", "--settle", "600"]);
            Assert.True(host.WaitFor("net: listening on UDP", 240), Tail(host));
            using var anna = new Game(Platform, folder, "anna", install!, Player(port, "Anna", 400));
            Assert.True(host.WaitFor("net: Anna is MP_Erik", 240), Tail(host) + "\n----\n" + Tail(anna));
            host.Type("/jump Anna Temesvar");
            Assert.True(host.WaitFor("jump: Anna goes to Temesvar", 30), Tail(host));
            Assert.Equal(0, anna.End(600));
            host.Type("/players");
            host.Type("/quit");
            Assert.Equal(0, host.End(60));

            Assert.True(host.Said("net: Anna pressed Start and is let in"), Tail(host));
            Assert.True(host.Said("net: the host has stopped after"), Tail(host));
            // Her machine followed: two runs, the second the new place's, one window and one GPU for both.
            Assert.True(anna.Said("net: in the game's room") && anna.Said("net: Anna (MP_Erik) joining"), Tail(anna));
            Assert.True(anna.Output.Split('\n').Count(l => l.StartsWith("chronicle: MP_CurseDarkness", StringComparison.Ordinal)) >= 2, Tail(anna));
            Assert.Contains(anna.Output.Split('\n'), l => l.StartsWith("chronicle: MP_CurseDarkness", StringComparison.Ordinal) && l.Contains("Temesvar", StringComparison.Ordinal));
            Assert.Single(anna.Output.Split('\n'), l => l.StartsWith("gpu: ", StringComparison.Ordinal));
            Assert.True(anna.Said("game classic: ended, exit code 0"), Tail(anna));
            Assert.True(File.Exists(Path.Combine(anna.UserFolder, "Modules", "classic", "Crypt", "Anna.vplayer")), Tail(anna));
        }
        finally { Clear(folder); }
    }

    /// <summary>A made-up rules module (one file, the status bar's vault button bound to <paramref name="action"/>) put in a game's user folder, on.</summary>
    static void BoundModule(string folder, string name, string action)
    {
        string user = Directory.CreateDirectory(Path.Combine(folder, "user-" + name)).FullName;
        string pack = Path.Combine(folder, "pack-" + name);
        Directory.CreateDirectory(Path.Combine(pack, "Data", "ui"));
        File.WriteAllText(Path.Combine(pack, "module.json"),
            $"{{ \"id\": \"vault-bar\", \"name\": \"Vault bar\", \"version\": \"1.0.0\", \"game\": \"classic\", \"actions\": {{ \"sbVault\": \"{action}\" }} }}");
        File.WriteAllBytes(Path.Combine(pack, "Data", "ui", "made-up.nui"), "name=madeUp\n"u8.ToArray());
        var report = new Vitomir.Platform.PlayerModules.PlayerModuleStore(user).Add(pack, new Vitomir.Platform.PlayerModules.ModuleImportOptions());
        Assert.True(report.Accepted, report.Refusal);
    }

    /// <summary>
    /// **A join compares the rules modules' interface bindings**: a host and a player with the same module's files, its vault button
    /// bound to the vault at the host and to the advancement screen at the player, refused, naming the module; the same bindings at
    /// both, let in.
    /// </summary>
    [Fact]
    public void A_player_whose_module_binds_its_buttons_otherwise_is_refused_naming_it_and_the_same_bindings_join()
    {
        var install = GameInstall.Locate();
        Assert.SkipWhen(install is null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        var folder = Folder("bindings");
        try
        {
            int port = FreePort();
            BoundModule(folder, "host", "open-vault");
            BoundModule(folder, "cara", "open-advancement");
            BoundModule(folder, "dana", "open-vault");
            using var host = new Game(Platform, folder, "host", install!,
                ["--headless-server", "--no-upnp", "--port", $"{port}", "--settle", "600"]);
            Assert.True(host.WaitFor("net: listening on UDP", 240), Tail(host));
            using (var cara = new Game(Platform, folder, "cara", install!, Player(port, "Cara", 300)))
            {
                // The knock carries no name (protocol 23): the host's line is the join's, refused for its bindings. Matched without the
                // line's dash: a redirected console on Windows writes it in its own code page.
                Assert.True(host.WaitFor("UI_NET_BADPROTOCOL: The host plays with other rules modules", 240), Tail(host) + "\n----\n" + Tail(cara));
                cara.End(300);
            }
            Assert.True(host.Said("vault-bar 1.0.0 has other files or interface bindings at the host than here"), Tail(host));
            using (var dana = new Game(Platform, folder, "dana", install!, Player(port, "Dana", 300)))
            {
                Assert.True(host.WaitFor("net: Dana pressed Start and is let in", 240), Tail(host) + "\n----\n" + Tail(dana));
                dana.End(300);
            }
            host.Type("/quit");
            host.End(60);
            Assert.Single(host.Output.Split('\n'), l => l.Contains("refused", StringComparison.Ordinal)
                                                       && l.Contains("UI_NET_BADPROTOCOL", StringComparison.Ordinal));   // Cara's alone; no dash, as above
        }
        finally { Clear(folder); }
    }

    /// <summary>
    /// **A windowed player walks by the keys against a headless host**, both through the platform's program: the join, the
    /// welcome (her character's clothes, <c>wears nothing</c>), the game, his keyboard walk predicted on his machine and borne out by
    /// the host's snapshots, and the leaving, each process ending by itself. (Earlier this ran each way between the platform and
    /// the old program, proving the wire the same under both; the old program is retired.)
    /// </summary>
    [Fact]
    public void A_headless_host_and_a_player_walking_by_the_keys_play_through_the_platform()
    {
        var install = GameInstall.Locate();
        Assert.SkipWhen(install is null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        var folder = Folder("keys");
        try
        {
            int port = FreePort();
            using var host = new Game(Platform, folder, "host", install!,
                ["--headless-server", "--no-upnp", "--port", $"{port}", "--settle", "600"]);
            Assert.True(host.WaitFor("net: listening on UDP", 240), Tail(host));
            // And W held a second and a half once he is in the inn — his keyboard walk, predicted on his machine.
            using var bob = new Game(Platform, folder, "bob", install!,
                [.. Player(port, "Bob", 400), "--ui-click", "wait:250;down:W;wait:45;up:W"]);
            Assert.Equal(0, bob.End(600));
            Assert.True(host.WaitFor("net: Bob disconnected", 30), Tail(host));
            host.Type("/quit");
            Assert.Equal(0, host.End(60));

            Assert.True(host.Said("net: Bob pressed Start and is let in") && host.Said("net: Bob is MP_Erik"), Tail(host));
            Assert.True(bob.Said("wears nothing") && bob.Said("net: out of the game"), Tail(bob));
            // His keyboard walk moved him on his machine at once and the host's snapshots bore it out (OwnWalk.Line).
            Assert.True(bob.Said("net: own walk predicted: 0 clicks, 1 keyboard walks;") && bob.Said("0 keyboard walks given back"), Tail(bob));
        }
        finally { Clear(folder); }
    }

    /// <summary>
    /// **The Storyteller looks into a place with a player in it** (<c>--moderated</c>):
    /// a playing host in the platform's window (<c>--host</c>) and a player (the bot, <c>--net-bot</c>, on the platform's windowless
    /// host) in the inn. The host's chat line — opened with 1.1's Enter after the intro's OK, in a window of a given size — sends the
    /// player alone to Temesvar (<c>/jump</c>; her machine follows, loading it) and carries a townsman there as the Storyteller
    /// (<c>/carry</c>: target, Possess, Jump to Location), and the window's camera goes with him: the host's window draws Temesvar, a
    /// place the host's own frame does not step (its list of places steps it, <c>StoryView</c>), through the host's renderer (the
    /// module never draws); a scripted still of the window is that view. Both end by themselves (<c>--frames</c>,
    /// <c>--settle</c>).
    /// </summary>
    [Fact]
    public void The_storyteller_looks_into_another_place_with_a_player_in_it_through_the_platform()
    {
        var install = GameInstall.Locate();
        Assert.SkipWhen(install is null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        var folder = Folder("storyteller");
        try
        {
            int port = FreePort();
            string still = Path.Combine(folder, "storyteller.png");
            // The intro's OK at its place in a 640x360 window, then the chat line opened, typed and sent, twice; a still of the view.
            string steps = "wait:900;468,232;wait:60;key:Enter;wait:5;text:/jump Bot Temesvar;key:Enter;wait:30;"
                         + $"key:Enter;wait:5;text:/carry civ07drunk Temesvar;key:Enter;wait:120;shot:{still}";
            using var host = new Game(Platform, folder, "host", install!,
                ["--host", $"{port}", "--moderated", "--mp-chronicle", "MP_CurseDarkness", "--name", "Hosta", "--player-template", "MP_Christof_DA",
                 "--no-upnp", "--size", "640x360", "--frames", "1400", "--ui-click", steps]);
            Assert.True(host.WaitFor("P1_INN4: ", 240), Tail(host));
            using var bot = new Game(Platform, folder, "bot", install!, ["--net-bot", "--join", $"127.0.0.1:{port}", "--name", "Bot", "--settle", "600"]);
            Assert.Equal(0, host.End(600));
            Assert.Equal(0, bot.End(60));

            Assert.True(host.Said("net: Bot is MP_Erik"), Tail(host));
            Assert.True(host.Said("jump: Bot goes to Temesvar"), "the player did not go (was she in before the host's line?)\n" + Tail(host));
            Assert.True(bot.Said("net: loading p1_nqtr"), Tail(bot));
            Assert.True(host.Said("story: the camera goes with civ07drunk to p1_nqtr") && host.Said("carry: civ07drunk to Temesvar"), Tail(host));
            Assert.True(File.Exists(still) && new FileInfo(still).Length > 10_000, $"no still of the Storyteller's view at {still}");
            // Plain words only: on Windows a redirected program's output is in the console's code page, so a dash in a line does not
            // come through as written (a lesson learnt).
            Assert.True(bot.Said("net: Bot stops") && bot.Said("Gone, UI_NET_DISCONNECTED"), Tail(bot));
        }
        finally { Clear(folder); }
    }

    /// <summary>
    /// **The Storyteller's carried body is in hand in its new place**, through
    /// the platform's program: a moderated playing host in a window of a given size carries the inn's drunk to Temesvar (<c>/carry</c> on the
    /// chat line: target, Possess, Jump to Location). Proves what earlier builds did not do: the body is still possessed there (no
    /// "not in hand"), a click on the street walks it there (the walk's line, whose order puts the ring), the keys' push drives it
    /// (<c>--qol driving</c>, W held: the view moves with it), and the window's camera is on it — the harness finds the body on the
    /// world's part of the window (<c>move:@civ07drunk</c>), and the still has the body's pixels there, not the street's around it.
    /// (Earlier, the opening document's pause was never taken off a network game's host, so its camera stood in the inn while the
    /// view drew Temesvar: a dark wall, and the body nowhere on the screen.)
    /// </summary>
    [Fact]
    public void The_storyteller_s_carried_body_is_in_hand_walks_and_drives_in_its_new_place()
    {
        var install = GameInstall.Locate();
        Assert.SkipWhen(install is null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        var folder = Folder("carry-platform");
        try
        {
            int port = FreePort();
            string a = Path.Combine(folder, "a.png"), b = Path.Combine(folder, "b.png"), c = Path.Combine(folder, "c.png");
            // The intro's OK at its place in a 640x360 window, the chat line's carry; a still, the body found on the window; a click on
            // the street in front of it, a still; W held for 90 frames, a still.
            string steps = "wait:900;468,232;wait:60;key:Enter;wait:5;text:/carry civ07drunk Temesvar;key:Enter;wait:150;"
                         + $"shot:{a};move:@civ07drunk;wait:2;440,215;wait:240;shot:{b};down:W;wait:90;up:W;wait:10;shot:{c}";
            using var host = new Game(Platform, folder, "host", install!,
                ["--host", $"{port}", "--moderated", "--mp-chronicle", "MP_CurseDarkness", "--name", "Hosta", "--player-template", "MP_Christof_DA",
                 "--no-upnp", "--qol", "driving", "--size", "640x360", "--frames", "1800", "--ui-click", steps]);
            Assert.Equal(0, host.End(900));

            Assert.True(host.Said("story: the camera goes with civ07drunk to p1_nqtr") && host.Said("carry: civ07drunk to Temesvar"), Tail(host));
            Assert.False(host.Said("is not in hand"), Tail(host));
            Assert.True(host.Said("story: walking to") && host.Said("in p1_nqtr"), Tail(host));
            // The body on the world's part of the window, where the view's camera draws it.
            var at = host.Output.Split('\n').Select(l => System.Text.RegularExpressions.Regex.Match(l, @"harness: civ07drunk is at (\d+),(\d+)"))
                                 .FirstOrDefault(m => m.Success);
            Assert.True(at is not null, "the harness found no civ07drunk on the screen\n" + Tail(host));
            int x = int.Parse(at!.Groups[1].Value), y = int.Parse(at.Groups[2].Value);
            var still = Vitomir.Platform.Render.Png.Read(a, out int w, out int h);
            Assert.True(still is not null && w == 640 && h == 360, $"no still of the view at {a}");
            var body = Patch(still!, w, x, y);
            foreach (int side in new[] { -40, 40 })
                Assert.True(Apart(body, Patch(still!, w, x + side, y)) >= 12,
                            $"the still at {x},{y} looks like the street {side} pixels beside it: {string.Join(',', body)} against {string.Join(',', Patch(still!, w, x + side, y))}");
            // The drive moved the body, and the camera with it: the world's rows of the last two stills are not the same picture.
            // The bar is 0.5 a channel: the same 90 frames with no drive differ by about 0.08 (the street's own flicker and passers-by),
            // a drive by about 5 in the cloud and about 2 on a Windows PC (the game steps by wall time, so a faster PC drives less
            // far in 90 frames; 2.0 failed there at 1.94).
            var before = Vitomir.Platform.Render.Png.Read(b, out _, out _)!;
            var after = Vitomir.Platform.Render.Png.Read(c, out _, out _)!;
            double moved = 0;
            for (int i = 0; i < w * 240 * 4; i++) moved += Math.Abs(before[i] - after[i]);
            Assert.True(moved / (w * 240 * 4) >= 0.5, $"the view did not move with the drive ({moved / (w * 240 * 4):0.00} a channel)");
        }
        finally { Clear(folder); }
    }

    /// <summary>
    /// **A dedicated host's switches are a player's, and the password is written nowhere**. The
    /// dedicated host — <c>--headless-server</c>, <c>--port</c>, <c>--no-upnp</c>, <c>--mp-chronicle</c>, <c>--moderated</c>,
    /// <c>--no-auto-st</c>, <c>--game-name</c>, <c>--password</c>, <c>--max-players</c> — starts under the platform's program
    /// <b>without</b> <c>--developer</c>; a player (behind <c>--developer</c>: its
    /// <c>--join</c>, <c>--name</c>, the tape's <c>--record</c>) joins with the password and is let in, and the host's <c>/quit</c> ends it.
    /// Then the password's value is in nothing the program wrote: not their consoles, not the platform's log (<c>its command
    /// line</c>, with <c>--password ****</c>), not the game's logs in the user folders, not the player's tape (its <c>arg</c> line is
    /// <c>****</c>, and the host's answer to the join is taped instead). The players' load, a player's console and the tape are read as
    /// plain words.
    /// </summary>
    [Fact]
    public void A_dedicated_host_takes_the_players_switches_and_its_password_is_written_nowhere()
    {
        var install = GameInstall.Locate();
        Assert.SkipWhen(install is null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        var folder = Folder("pw-platform");
        const string secret = "Qx7ZebraLantern";
        try
        {
            int port = FreePort();
            using var host = new Game(Platform, folder, "host", install!,
                ["--headless-server", "--port", $"{port}", "--no-upnp", "--mp-chronicle", "MP_CurseDarkness", "--moderated", "--no-auto-st",
                 "--game-name", "Lantern", "--password", secret, "--max-players", "3"], developer: false, logs: true);
            Assert.True(host.WaitFor("net: listening on UDP", 240), Tail(host));
            string tapes = Directory.CreateDirectory(Path.Combine(folder, "tapes")).FullName;
            string annaSaid;
            using (var anna = new Game(Platform, folder, "anna", install!,
                       [.. Player(port, "Anna", 300), "--password", secret, "--record", tapes], logs: true))
            {
                Assert.Equal(0, anna.End(600));
                annaSaid = anna.Output;
            }
            Assert.True(annaSaid.Contains("wears nothing", StringComparison.Ordinal) && !annaSaid.Contains(secret, StringComparison.Ordinal), annaSaid);
            Assert.True(host.WaitFor("net: Anna disconnected", 30), Tail(host));
            host.Type("/quit");
            Assert.Equal(0, host.End(60));

            Assert.True(host.Said("net: Anna pressed Start and is let in"), Tail(host));
            Assert.True(host.Said("net: hosting \"Lantern\""), Tail(host));
            Assert.DoesNotContain("refused", host.Output);
            Assert.True(host.Said("its command line: ") && host.Said("--password ****"), Tail(host));
            Assert.NotEmpty(Directory.EnumerateFiles(tapes));
            var tape = Directory.EnumerateFiles(tapes).First(f => !f.Contains(".digest", StringComparison.Ordinal));
            Assert.Contains(Text(tape).Split('\n'), l => l.TrimEnd('\r') == "arg ****");
            Assert.True(host.Output.Length > 0 && !host.Output.Contains(secret, StringComparison.Ordinal), "the host's console has the password");
            var written = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                                   .Where(f => !f.Contains(Path.Combine("Modules", "classic", "cache"), StringComparison.Ordinal))
                                   .Where(f => Text(f).Contains(secret, StringComparison.Ordinal)).ToList();
            Assert.True(written.Count == 0, "the password is in " + string.Join(", ", written));
            Assert.NotEmpty(Directory.EnumerateFiles(folder, "*.log", SearchOption.AllDirectories));
            // Why the host stopped is in the game's own log, not only on a console that may be a window closing.
            Assert.Contains("net: the host stops: /quit typed on its console", GameLog(host));
        }
        finally { Clear(folder); }
    }

    /// <summary>The game's own log in a process's user folder (the module's under the platform), all of it.</summary>
    static string GameLog(Game game) =>
        string.Join('\n', Directory.EnumerateFiles(game.UserFolder, "*.log", SearchOption.AllDirectories)
                                   .Where(f => f.Contains(Path.Combine("classic", "Logs"), StringComparison.Ordinal) || Path.GetFileName(Path.GetDirectoryName(f)) == "Logs")
                                   .Select(Text));

    /// <summary>
    /// **A host whose port another program holds** (a second host while the first still ran died with an unhandled
    /// <c>SocketException (10013)</c>): a dedicated host and a playing one, through the platform's program, say so in one plain line — the port,
    /// that another program holds it, <c>--port</c> — on the console and in the game's log, and end by themselves with 2.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_host_whose_port_is_held_says_so_and_ends(bool dedicated)
    {
        var install = GameInstall.Locate();
        Assert.SkipWhen(install is null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        var folder = Folder("held");
        try
        {
            int port = FreePort();
            using var holder = new UdpClient(new IPEndPoint(IPAddress.Any, port));
            using var host = new Game(Platform, folder, "host", install!,
                dedicated ? ["--headless-server", "--port", $"{port}", "--no-upnp", "--settle", "30"]
                          : ["--host", $"{port}", "--no-upnp", "--name", "Hosta", "--player-template", "MP_Christof_DA", "--size", "640x360", "--frames", "300"],
                developer: true, logs: true);
            Assert.Equal(2, host.End(300));
            string line = $"UDP port {port} is already held by another program";
            Assert.True(host.Said(line) && host.Said("--port"), Tail(host));
            Assert.DoesNotContain("Unhandled", host.Output);
            Assert.Contains(line, GameLog(host));
        }
        finally { Clear(folder); }
    }

    /// <summary>A file's text, a gzipped one (a tape, a log kept compressed) unpacked.</summary>
    static string Text(string path)
    {
        using var file = File.OpenRead(path);
        Stream read = file;
        if (file.Length > 2 && file.ReadByte() == 0x1f && file.ReadByte() == 0x8b) { file.Position = 0; read = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Decompress); }
        else file.Position = 0;
        using var text = new StreamReader(read);
        return text.ReadToEnd();
    }

    /// <summary>The mean red, green and blue of the 16 by 16 pixels about <paramref name="x"/>, <paramref name="y"/> of an RGBA picture.</summary>
    static int[] Patch(byte[] rgba, int width, int x, int y)
    {
        var sum = new int[3];
        for (int py = y - 8; py < y + 8; py++)
            for (int px = x - 8; px < x + 8; px++)
                for (int k = 0; k < 3; k++) sum[k] += rgba[(py * width + px) * 4 + k];
        return [.. sum.Select(v => v / 256)];
    }

    static int Apart(int[] one, int[] other) => one.Zip(other, (p, q) => Math.Abs(p - q)).Sum();
}

/// <summary>The network tests run by themselves (<see cref="ClassicNetworkTests"/>).</summary>
[CollectionDefinition(nameof(ClassicNetworkTests), DisableParallelization = true)]
public sealed class ClassicNetworkCollection;
