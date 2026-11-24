// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// <c>--replay-dump</c> (<see cref="ReplayDumpCommand"/>) — a short tape recorded on the proving ground and dumped
/// through the program's own entry, as <c>vitomir-platform-cli.exe</c> runs it: the table's header is its columns and it has a row a
/// tick; a tape not there, a file not a tape and ranges not ranges are refused plainly. The recording skips without the proving ground
/// (<c>VITOMIR_COURSE_LEVEL</c>) or the 1.1 install, as the other course tests do; the ranges need nothing.
/// </summary>
public sealed class ReplayDumpTests
{
    static string Folder(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vitomir-replay-dump-{name}-{Environment.ProcessId}");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void A_short_tape_is_dumped_a_row_a_tick_under_the_columns_header()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL: a cooked proving-ground.l2)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR, or C:\\Games\\VTMR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-replay-dump-tests-userdata"));
        string folder = Folder("short");
        string path = Path.Combine(folder, "short.tape");
        // A second and a half: standing, then walking ahead and turning the view.
        int ticks = 3 * World.TicksPerSecond / 2;
        using (var session = Session.Create(new SessionOptions
               {
                   Level = course!, Game = game, Switches = new HashSet<string>(StringComparer.Ordinal) { "sandbox" }, Seed = 3, People = 0,
               }))
        {
            var tape = new Tape { Header = session.TapeHeader("test") };
            for (int t = 0; t < ticks; t++)
            {
                var frame = new ActionFrame(t < ticks / 3 ? Vector2.Zero : new Vector2(0f, 1f), new Vector2(t % 40 < 20 ? 0.01f : -0.01f, 0f), Buttons.None, new Vector2(0.5f, 0.5f));
                session.Step(frame);
                tape.Frames.Add(frame);
                if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
            }
            tape.Write(path);
        }

        string dump = Path.Combine(folder, "out");
        Assert.Equal(0, Entry.Run(["--replay-dump", path, "--dump-dir", dump]));
        var lines = File.ReadAllLines(Path.Combine(dump, ReplayDumpCommand.TableFile));
        Assert.Equal("tick\ts\trole\tclip\ttime\tprev\tprevTime\tblend\tlegYaw\tlegMode\tyaw\tspeed\tcrouched\tx\ty\taction\tactionClip"
                     + "\tz\tballL\tballR\tholdL\tholdR"   // the feet's height, the balls as drawn and the locks' holds
                     + "\tchest\thead\twaist\tneck\twaistShift"   // the chain's chest and head, the trunk's turns bone to bone, spine_01 off the pelvis
                     + "\tline\tslope\thips"   // the drawn body's line, the slope clips' share, the hips' drop
                     + "\tcam\tcamYaw\tcamPitch\tboom"   // the view's place, yaw and pitch, the third-person boom
                     + "\tthighs\them", lines[0]);   // the thighs' raise, a skirt's hem's stretch
        Assert.Equal(ReplayDumpCommand.Header, lines[0]);
        Assert.Equal(ticks + 1, lines.Length);
        int columns = ReplayDumpCommand.Columns.Length;
        Assert.All(lines.Skip(1), l => Assert.Equal(columns, l.Split('\t').Length));
        Assert.Equal("1", lines[1].Split('\t')[0]);
        Assert.Equal(ticks.ToString(System.Globalization.CultureInfo.InvariantCulture), lines[^1].Split('\t')[0]);
        // Walking by the end: the speed column (invariant decimals) is above nothing.
        int speed = Array.IndexOf(lines[0].Split('\t'), "speed");
        Assert.True(float.Parse(lines[^1].Split('\t')[speed], System.Globalization.CultureInfo.InvariantCulture) > 0.3f, lines[^1]);
        // Each ball of the foot in the world, three numbers.
        int ball = Array.IndexOf(lines[0].Split('\t'), "ballL");
        Assert.Equal(3, lines[^1].Split('\t')[ball].Split(',').Length);
        // The view's place, three numbers; the boom a length.
        Assert.Equal(3, lines[^1].Split('\t')[Array.IndexOf(lines[0].Split('\t'), "cam")].Split(',').Length);
        Assert.InRange(float.Parse(lines[^1].Split('\t')[Array.IndexOf(lines[0].Split('\t'), "boom")], System.Globalization.CultureInfo.InvariantCulture), 0f, 3f);
        // Both thighs' raise in degrees (a person; - for 1.1's); the hem's stretch a ratio, or - without a skirt hung from the thighs.
        string thighs = lines[^1].Split('\t')[Array.IndexOf(lines[0].Split('\t'), "thighs")];
        Assert.True(thighs == "-" || thighs.Split(',').Length == 2, thighs);   // - for 1.1's Christof
        // The trunk as drawn — the spine on the pelvis (the earlier build's waist), the turns between neighbouring bones within the chain's share.
        var header = lines[0].Split('\t');
        foreach (var line in lines.Skip(1).Select(l => l.Split('\t')))
        {
            if (line[Array.IndexOf(header, "waistShift")] == "-") continue;   // 1.1's Christof (no models repository's files here): not a person
            Assert.Equal("0.0", line[Array.IndexOf(header, "waistShift")]);
            Assert.InRange(float.Parse(line[Array.IndexOf(header, "waist")], System.Globalization.CultureInfo.InvariantCulture), 0f, 45f);
        }
        Assert.False(File.Exists(Path.Combine(dump, "t00001.png")));   // no stills unless asked

        // Refusals, plainly (exit 1): no tape there; a file that is not a tape.
        Assert.Equal(1, Entry.Run(["--replay-dump", Path.Combine(folder, "no such.tape"), "--dump-dir", dump]));
        string notTape = Path.Combine(folder, "not.tape");
        File.WriteAllText(notTape, "not a tape");
        Assert.Equal(1, Entry.Run(["--replay-dump", notTape, "--dump-dir", dump]));
        Assert.Equal(1, Entry.Run(["--replay-dump", path, "--dump-dir", dump, "--dump-stills", "20-10"]));
        Assert.Equal(1, Entry.Run(["--replay-dump", path, "--dump-dir", dump, "--dump-alpha", "2"]));   // a share from 0 to 1
    }

    [Fact]
    public void The_stills_ticks_are_ticks_and_ranges_with_a_step_and_anything_else_is_refused()
    {
        Assert.Empty(ReplayDumpCommand.Ticks(null));
        Assert.Equal([600], ReplayDumpCommand.Ticks("600"));
        Assert.Equal([300, 310, 320, 600], ReplayDumpCommand.Ticks("300-320:10, 600").Order());
        Assert.Equal([5, 6, 7], ReplayDumpCommand.Ticks("5-7").Order());
        Assert.Equal([1, 4], ReplayDumpCommand.Ticks("1-5:3").Order());
        foreach (var bad in new[] { "20-10", "0", "-5", "a", "5:0", "5-8:x", "1-2-3", "1:2:3", "," })
            Assert.Throws<SessionException>(() => ReplayDumpCommand.Ticks(bad));
        Assert.Contains("ends before it starts", Assert.Throws<SessionException>(() => ReplayDumpCommand.Ticks("20-10")).Message);
    }
}
