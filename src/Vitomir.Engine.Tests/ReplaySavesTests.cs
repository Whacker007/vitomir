// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Ui;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A recording carries the saves it began with, and a folder of recordings is checked in one go** (ours:
/// the replay harness's: the saves a recording carries, and
/// <c>--replay-folder</c>, a folder of recordings, checked; <c>src/Vitomir.Classic/Input/TapeSaves.cs</c>,
/// <c>src/Vitomir.Tools/ReplayFolder.cs</c>). A run reads the player's save folder wherever it lists the slots or
/// loads one, so until a tape carried that folder a recording that loaded a slot replayed only until the
/// player saved over it, and one that saved and then loaded its own save replayed against whatever the
/// folder held by then — which is why the play-throughs recorded before it no longer replay. Pinned
/// here, each by running the built platform's program (<c>vitomir-platform --game classic --developer</c>; <c>vitomir.exe</c>
/// until then) as <see cref="ReplayBaselineTests"/> does, and the folder by <c>vitomir-tools</c>, whose tapes it plays (and skipping as
/// they do, without the install or the programs):
///
/// - the baseline <c>Replays/save-load.tape</c> — a fight among the hoppers of <c>ARC3_17_1</c> in which the
///   quick save is pressed (the save screen, a slot, a name), the fight goes on, and the quick load brings
///   the save back — replays the same every frame, saving into its own scratch folder and loading from it,
///   whatever the player's folder holds; and the repository keeps no save beside it;
/// - a recording copies the save folder beside its tape and lists it on the tape, and a replay says which
///   of the listed files are missing or changed;
/// - a replay of a recording that loaded a slot reads the kept copy, not the player's folder, which is
///   changed between the recording and the replay to prove it;
/// - a tape with no <c>saves</c> line (one recorded before there was one) reads the player's folder, as it
///   always did;
/// - <c>--replay-folder</c> gives a row a tape — the same, diverged at a frame, could not run — writes the
///   table, answers 0 only when every tape agreed, and with <c>--rebase</c> gives a diverging tape the
///   digest of its replay, keeping the digest before once.
///
/// Each test's player's folder is its own (<c>VITOMIR_USERDATA</c>), so none touches the machine's. They
/// are replays: <c>VITOMIR_NO_REPLAYS=1</c> leaves them out of a quick run with the baselines.
/// </summary>
public class ReplaySavesTests
{
    static readonly string Here = AppContext.BaseDirectory;

    /// <summary>
    /// A short harness run with things going on: Christof and Erik among the hoppers of <c>ARC3_17_1</c>, as the
    /// <c>arc3-fight</c> baseline's. In 1.1's stretch, said: <see cref="LoadTheFirstSlot"/>'s window pixels are the
    /// stretched sheet's on this 4:3 picture, where the default layout's sheet is shorter than the picture.
    /// </summary>
    static readonly string[] Fight =
        ["--level", "p1_arc3", "--scene", "ARC3_17_1", "--player", "--party", "--start", "-560,160,0", "--face", "180",
         "--size", "320x240", "--ui-layout", "stretch", "--frame-ms", "33", "--seed", "7", "--mute"];

    /// <summary>The quick load pressed with no slot yet (the load screen), its one row picked, and Load: at 320x240, stretched.</summary>
    const string LoadTheFirstSlot = "wait:10;key:Keypad -;wait:5;move:158,36;wait:2;158,36;wait:3;188,220;wait:10";

    /// <summary>A folder for one test: the tapes in <see cref="Dir"/>, and a player's folder of its own (<see cref="User"/>, <see cref="Saves"/>).</summary>
    sealed class Bench : IDisposable
    {
        public string Dir { get; } = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"vitomir-replay-saves-{Guid.NewGuid():N}")).FullName;
        public string User => Path.Combine(Dir, "player");
        /// <summary>The player's save folder: the game's own under the platform's user data (<c>Modules/classic/Saves</c>).</summary>
        public string Saves => ReplayBaselineTests.SavesOf(User);
        public Bench() => Directory.CreateDirectory(Saves);
        public string At(string name) => Path.Combine(Dir, name);
        public void Dispose() { try { Directory.Delete(Dir, recursive: true); } catch (IOException) { } }
    }

    /// <summary>The platform's program run with <paramref name="user"/> as the player's user data; its exit code and everything it printed (vitomir.exe until then).</summary>
    static (int Code, string Output) Run(string app, string game, string user, params string[] args) =>
        ReplayBaselineTests.RunWith(new() { [BuildFlavour.UserDataVariable] = user }, app, game, args);

    /// <summary><c>vitomir-tools --replay-folder …</c>, its tapes played by the platform's program (its default), with <paramref name="user"/> as the user data.</summary>
    static (int Code, string Output) Folder(string game, string user, params string[] args)
    {
        var tools = ReplayBaselineTests.Tools();
        Assert.SkipWhen(tools is null, "vitomir-tools is not built beside the tests (build src/Vitomir.Tools/Vitomir.Tools.csproj in the same configuration)");
        return ReplayBaselineTests.RunProgram(new() { [BuildFlavour.UserDataVariable] = user }, tools!, [.. args, "--game", game]);
    }

    /// <summary>The lines a test's failure shows: the harness's own, and the saves and loads.</summary>
    static string Said(string output) =>
        string.Join('\n', output.Split('\n').Select(l => l.TrimEnd('\r')).Where(l =>
            l.StartsWith("replay", StringComparison.Ordinal) || l.StartsWith("record", StringComparison.Ordinal)
            || l.StartsWith("save:", StringComparison.Ordinal) || l.StartsWith("load:", StringComparison.Ordinal) || l.Contains(".tape", StringComparison.Ordinal)));

    static List<string> Lines(string output, string start) =>
        [.. output.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith(start, StringComparison.Ordinal))];

    static string Sha(string file) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));

    /// <summary>Every file under a folder, by its name under it (<c>/</c> between folders), with its bytes' hash.</summary>
    static SortedDictionary<string, string> Files(string folder) =>
        new(Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(folder, f).Replace('\\', '/'), Sha)
            : [], StringComparer.Ordinal);

    /// <summary>A save of the fight a few frames in, written as the player's slot 1.</summary>
    static void SaveSlotOne(string app, string game, Bench bench)
    {
        string slot = Path.Combine(bench.Saves, "SP1.vsave");
        var (code, output) = Run(app, game, bench.User, [.. Fight, "--frames", "8", "--no-log-file", "--save", slot]);
        Assert.True(code == 0 && File.Exists(slot), Said(output));
    }

    /// <summary>A recording of the fight in which the load screen's first slot is loaded, and the game goes on from it.</summary>
    static string RecordALoad(string app, string game, Bench bench)
    {
        string tape = bench.At("loaded.tape");
        var (code, output) = Run(app, game, bench.User, [.. Fight, "--frames", "40", "--no-log-file", "--ui-click", LoadTheFirstSlot, "--record", tape]);
        Assert.True(code == 0, Said(output));
        Assert.True(Lines(output, "load: ").Count >= 1, "the recorded run loaded nothing:\n" + output);
        return tape;
    }

    [Fact]
    public void The_save_and_load_baseline_keeps_no_save_in_the_repository()
    {
        string tape = Path.Combine(Here, "Replays", "save-load.tape");
        Assert.True(File.Exists(tape) && File.Exists(tape + ".digest"), "Replays/save-load.tape and its digest are test data");
        var reader = TapeReader.Open(tape);
        // Recorded on an empty save folder: the line is there and says none, and no folder was made beside the tape.
        Assert.Equal(["0", "bytes=0"], Assert.Single(reader.HeadOf("saves")));
        Assert.Empty(reader.HeadOf("savefile"));
        Assert.False(Directory.Exists(tape + ".saves"));
        // And what it is a recording of: the quick save and the quick load, pressed.
        string clicks = string.Join(' ', reader.HeadOf("arg").Select(f => ReplayTape.Unescape(f[0])));
        Assert.Contains("key:Keypad +", clicks);
        Assert.Contains("key:Keypad -", clicks);
    }

    [Fact]
    public void The_save_and_load_baseline_loads_the_save_it_made_and_replays_the_same_every_frame()
    {
        var (app, game) = ReplayBaselineTests.Needs();
        using var bench = new Bench();
        string tape = bench.At("save-load.tape");
        foreach (var suffix in new[] { "", ".digest" })
            File.Copy(Path.Combine(Here, "Replays", "save-load.tape" + suffix), tape + suffix);
        // The player's own slot 1 is something else entirely: a replay that read it could not load it.
        string players = Path.Combine(bench.Saves, "SP1.vsave");
        File.WriteAllText(players, "the player's own save, which a replay neither reads nor writes");
        var before = Files(bench.Saves);

        string output;
        if (ReplayBaselineTests.RecordedOn(tape + ".digest") == RuntimeInformation.RuntimeIdentifier)
        {
            (int code, output) = Run(app, game, bench.User, "--replay-check", tape, "--mute");
            Assert.True(code == 0 && output.Contains("replay-check: the same"), Said(output));
        }
        else
        {
            // Another platform's digest: the replay against itself. Twice replayed, the same every frame.
            (int c1, output) = Run(app, game, bench.User, "--replay", tape, "--replay-fast", "--mute", "--no-log-file");
            Assert.True(c1 == 0, Said(output));
            File.Move(tape + ".replay.digest", tape + ".first.digest");
            var (c2, o2) = Run(app, game, bench.User, "--replay", tape, "--replay-fast", "--mute", "--no-log-file");
            Assert.True(c2 == 0, Said(o2));
            using var checker = new DigestChecker(DigestReader.Open(tape + ".first.digest"));
            using var second = DigestReader.Open(tape + ".replay.digest");
            while (second.Next() is { } frame) Assert.Null(checker.Check(frame));
            Assert.False(checker.RecordedLeft);
        }

        // The save was made — in the replay's own folder, not the player's — and the load found it there.
        var saved = Assert.Single(Lines(output, "save: "));
        Assert.Contains("SP1.vsave", saved);
        Assert.DoesNotContain(bench.User, saved);
        Assert.True(Lines(output, "load: ").Count >= 1, Said(output));
        Assert.DoesNotContain("could not be read", output);
        Assert.Equal(before, Files(bench.Saves));
    }

    [Fact]
    public void A_recording_keeps_the_save_folder_it_began_with_beside_its_tape()
    {
        var (app, game) = ReplayBaselineTests.Needs();
        using var bench = new Bench();
        // Whatever the folder holds, copied as files: both families' slots, a previous version, a save kept as a folder of its parts.
        File.WriteAllText(Path.Combine(bench.Saves, "SP1.vsave"), "slot one");
        File.WriteAllText(Path.Combine(bench.Saves, "SP1.vsave.previous"), "slot one, before");
        File.WriteAllText(Path.Combine(bench.Saves, "MP2.vsave"), "a network game's");
        Directory.CreateDirectory(Path.Combine(bench.Saves, "plain"));
        File.WriteAllText(Path.Combine(bench.Saves, "plain", "party.jsonc"), "{ }");
        var players = Files(bench.Saves);

        // Compressed, to pin the folder's name as well: beside `kept.tape.gz` it is `kept.tape.saves`.
        string tape = bench.At("kept.tape.gz");
        var (code, output) = Run(app, game, bench.User, [.. Fight, "--frames", "12", "--no-log-file", "--record", tape]);
        Assert.True(code == 0, Said(output));
        Assert.Contains("record: 4 files of the save folder kept beside the tape", output);
        string kept = bench.At("kept.tape.saves");
        Assert.Equal(players, Files(kept));
        Assert.Equal(players, Files(bench.Saves));                        // and the player's folder is as it was
        Assert.False(Directory.Exists(tape + ".saves"));

        // The tape says what was kept: the count and the bytes, then each file with its size and SHA-256, after the launch.
        var reader = TapeReader.Open(tape);
        long bytes = players.Keys.Sum(n => new FileInfo(Path.Combine(kept, n)).Length);
        Assert.Equal(["4", "bytes=" + bytes.ToString(CultureInfo.InvariantCulture)], Assert.Single(reader.HeadOf("saves")));
        Assert.Equal(players.Select(p => $"{p.Key} sha256={p.Value}"),
                     reader.HeadOf("savefile").Select(f => $"{ReplayTape.Unescape(f[0])} {f.Single(x => x.StartsWith("sha256=", StringComparison.Ordinal))}"));
        var kinds = reader.Head.Select(l => l.Kind).ToList();
        Assert.True(kinds.LastIndexOf("arg") < kinds.IndexOf("saves") && kinds.IndexOf("saves") < kinds.IndexOf("fixes"), string.Join(' ', kinds.Distinct()));

        // A replay with one kept file gone and another changed says which, and carries on (this run reads neither).
        Directory.Delete(bench.Saves, recursive: true);
        File.Delete(Path.Combine(kept, "MP2.vsave"));
        File.WriteAllText(Path.Combine(kept, "SP1.vsave"), "slot one, meddled with");
        var (c2, o2) = Run(app, game, bench.User, "--replay-check", tape);
        Assert.True(c2 == 0 && o2.Contains("replay-check: the same"), Said(o2));
        Assert.Contains("replay: the save folder is a scratch copy of 3 of the 4 files the recording began with", o2);
        Assert.Contains("replay: not beside the tape: MP2.vsave", o2);
        Assert.Contains("SHA-256 differs): SP1.vsave", o2);
        Assert.False(Directory.Exists(bench.Saves));                        // the replay wrote nothing of the player's
    }

    [Fact]
    public void A_replay_reads_the_saves_kept_beside_its_tape_and_not_the_players()
    {
        var (app, game) = ReplayBaselineTests.Needs();
        using var bench = new Bench();
        SaveSlotOne(app, game, bench);
        string tape = RecordALoad(app, game, bench);
        Assert.True(File.Exists(Path.Combine(tape + ".saves", "SP1.vsave")));

        // Since the recording the player has played on: slot 1 is another save now (here, not even a save).
        string players = Path.Combine(bench.Saves, "SP1.vsave");
        File.WriteAllText(players, "saved over since the recording");
        var before = Files(bench.Saves);
        var (code, output) = Run(app, game, bench.User, "--replay-check", tape);
        Assert.True(code == 0 && output.Contains("replay-check: the same"), Said(output));
        Assert.True(Lines(output, "load: ").Count >= 1, Said(output));
        Assert.Equal(before, Files(bench.Saves));

        // And with no folder of the player's at all.
        Directory.Delete(bench.Saves, recursive: true);
        var (c2, o2) = Run(app, game, bench.User, "--replay-check", tape);
        Assert.True(c2 == 0 && o2.Contains("replay-check: the same"), Said(o2));

        // It is the kept copy the load reads: with that gone the replay says which file it has not got, and has nothing to load.
        Directory.Delete(tape + ".saves", recursive: true);
        var (c3, o3) = Run(app, game, bench.User, "--replay-check", tape);
        Assert.Contains(Lines(o3, "replay: "), l => l.StartsWith("replay: not beside the tape (", StringComparison.Ordinal) && l.Contains(".saves is missing): SP1.vsave"));
        Assert.True(c3 != 0 && !o3.Contains("replay-check: the same"), Said(o3));
    }

    [Fact]
    public void An_older_tape_with_no_saves_line_reads_the_players_folder_as_before()
    {
        var (app, game) = ReplayBaselineTests.Needs();
        using var bench = new Bench();
        SaveSlotOne(app, game, bench);
        string tape = RecordALoad(app, game, bench);
        // The tape as one recorded before a tape carried its saves: no `saves` line, nothing beside it.
        File.WriteAllLines(tape, File.ReadAllLines(tape).Where(l => !l.StartsWith("saves ", StringComparison.Ordinal) && !l.StartsWith("savefile ", StringComparison.Ordinal)));
        Directory.Delete(tape + ".saves", recursive: true);

        // With the player's slot as it was, it replays: the load reads the player's folder.
        var before = Files(bench.Saves);
        var (code, output) = Run(app, game, bench.User, "--replay-check", tape);
        Assert.True(code == 0 && output.Contains("replay-check: the same"), Said(output));
        Assert.DoesNotContain("scratch", string.Join('\n', Lines(output, "replay: ")));
        Assert.Equal(before, Files(bench.Saves));

        // With the slot gone it cannot: there is nothing on the load screen to load.
        File.Delete(Path.Combine(bench.Saves, "SP1.vsave"));
        var (c2, o2) = Run(app, game, bench.User, "--replay-check", tape);
        Assert.True(c2 != 0 && !o2.Contains("replay-check: the same"), Said(o2));
    }

    /// <summary>A tape's lines with one frame's step made a quarter of a second: every system of that frame is stepped by it.</summary>
    static void ChangeStep(string from, string to, int frame)
    {
        var lines = File.ReadAllLines(from).ToList();
        int at = lines.IndexOf($"frame {frame.ToString(CultureInfo.InvariantCulture)}");
        Assert.True(at > 0);
        lines[lines.FindIndex(at, l => l.StartsWith("step ", StringComparison.Ordinal))] = "step 0.25";
        File.WriteAllLines(to, lines);
    }

    [Fact]
    public void A_folder_of_recordings_is_checked_tape_by_tape_and_a_diverging_one_is_rebased()
    {
        var (app, game) = ReplayBaselineTests.Needs();
        using var bench = new Bench();
        string folder = Directory.CreateDirectory(bench.At("tapes")).FullName;
        string T(string name) => Path.Combine(folder, name);
        string here = RuntimeInformation.RuntimeIdentifier;
        string build = Vitomir.Platform.ReleaseVersion.Word;

        // a: a recording as made. b: the same with frame 10's step changed, beside a's digest. c: a tape with no digest.
        var (code, output) = Run(app, game, bench.User, [.. Fight, "--frames", "30", "--no-log-file", "--record", T("a.tape")]);
        Assert.True(code == 0, Said(output));
        ChangeStep(T("a.tape"), T("b.tape"), 10);
        File.Copy(T("a.tape.digest"), T("b.tape.digest"));
        File.Copy(T("a.tape"), T("c.tape"));
        File.WriteAllText(T("notes.txt"), "not a tape");

        (code, output) = Folder(game, bench.User, "--replay-folder", folder, "--mute");
        Assert.True(code == 1, output);
        var rows = Lines(output, "");
        Assert.Contains(rows, l => l.StartsWith("a.tape ", StringComparison.Ordinal) && l.Contains(" 30 frames ") && l.EndsWith(" s  the same", StringComparison.Ordinal));
        Assert.Contains(rows, l => l.StartsWith("b.tape ", StringComparison.Ordinal) && l.Contains(" s  DIVERGED at frame 10: the frame's clock differs"));
        Assert.Contains(rows, l => l.StartsWith("c.tape ", StringComparison.Ordinal) && l.Contains(" s  could not run: ") && l.Contains("no digest beside the tape"));
        Assert.Contains("replay-folder: 3 tapes: 1 the same, 1 diverged, 1 could not run", output);
        Assert.Contains("a.tape: running", output);
        // The table, in the folder, under the build and the date.
        var table = File.ReadAllLines(T("replay-folder.txt"));
        Assert.StartsWith(FrontEnd.Version + ", " + DateTime.Now.Year.ToString(CultureInfo.InvariantCulture), table[0]);
        Assert.Contains(table, l => l.StartsWith("a.tape ", StringComparison.Ordinal) && l.EndsWith("the same", StringComparison.Ordinal));
        Assert.Contains(table, l => l.StartsWith("b.tape ", StringComparison.Ordinal) && l.Contains("DIVERGED at frame 10:"));
        Assert.Contains(table, l => l.StartsWith("3 tapes: 1 the same, 1 diverged, 1 could not run", StringComparison.Ordinal));
        Assert.Equal(File.ReadAllBytes(T("a.tape.digest")), File.ReadAllBytes(T("b.tape.digest")));   // a check changes nothing

        // d: diverging too, with a digest "before" of this build's name kept already. e: diverging, its digest another platform's.
        ChangeStep(T("a.tape"), T("d.tape"), 12);
        File.Copy(T("a.tape.digest"), T("d.tape.digest"));
        File.WriteAllText(T($"d.tape.digest.{build}-before"), "the digest before this build's first rebase");
        File.Copy(T("b.tape"), T("e.tape"));
        File.WriteAllText(T("e.tape.digest"), File.ReadAllText(T("a.tape.digest")).Replace("recorded on " + here, "recorded on another-machine"));
        var untouched = Sha(T("a.tape.digest"));
        var foreign = Sha(T("e.tape.digest"));

        (code, output) = Folder(game, bench.User, "--replay-folder", folder, "--rebase", "--mute");
        Assert.True(code == 1, output);
        rows = Lines(output, "");
        Assert.Contains(rows, l => l.StartsWith("a.tape ", StringComparison.Ordinal) && l.EndsWith(" s  the same", StringComparison.Ordinal));
        Assert.Contains(rows, l => l.StartsWith("b.tape ", StringComparison.Ordinal) && l.Contains(" s  rebased (had diverged at frame 10)"));
        Assert.Contains(rows, l => l.StartsWith("d.tape ", StringComparison.Ordinal) && l.Contains(" s  rebased (had diverged at frame 12)"));
        Assert.Contains(rows, l => l.StartsWith("e.tape ", StringComparison.Ordinal) && l.Contains("DIVERGED at frame 10:") && l.Contains("recorded on another-machine")
                                   && l.Contains("not rebased: a tape is replayed on the platform that recorded it"));
        Assert.Contains("replay-folder: 5 tapes: 1 the same, 1 diverged, 2 rebased, 1 could not run", output);
        // b's digest is its replay's now, and says so; the one it had is kept, once, under the build's name.
        Assert.Equal(untouched, Sha(T("a.tape.digest")));
        Assert.Equal(untouched, Sha(T($"b.tape.digest.{build}-before")));
        Assert.NotEqual(untouched, Sha(T("b.tape.digest")));
        var head = File.ReadLines(T("b.tape.digest")).Take(4).ToList();
        Assert.Equal(SimDigest.Magic, head[0]);
        Assert.Equal(here, ReplayBaselineTests.RecordedOn(T("b.tape.digest")));
        Assert.Contains(head, l => l.StartsWith("# rebased ", StringComparison.Ordinal) && l.Contains(FrontEnd.Version) && l.Contains("had diverged at frame 10"));
        Assert.False(File.Exists(T("b.tape.replay.digest")));
        // d's is replaced as well, and the "before" that was there already is left alone.
        Assert.NotEqual(untouched, Sha(T("d.tape.digest")));
        Assert.Equal("the digest before this build's first rebase", File.ReadAllText(T($"d.tape.digest.{build}-before")));
        Assert.Equal(foreign, Sha(T("e.tape.digest")));
        Assert.False(File.Exists(T($"e.tape.digest.{build}-before")));

        // The rebased tapes agree with themselves from now on: with the two that cannot taken out, the folder is clean.
        File.Delete(T("c.tape"));
        File.Delete(T("e.tape"));
        File.Delete(T("e.tape.digest"));
        (code, output) = Folder(game, bench.User, "--replay-folder", folder, "--mute");
        Assert.True(code == 0, output);
        Assert.Contains("replay-folder: 3 tapes: 3 the same, 0 diverged, 0 could not run", output);
        Assert.DoesNotContain(File.ReadAllLines(T("replay-folder.txt")), l => l.Contains("c.tape"));   // written afresh
    }
}
