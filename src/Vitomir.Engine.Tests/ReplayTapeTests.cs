// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Diagnostics;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The replay tape and the digest, on their own** (ours;
/// <c>--record</c>, <c>--replay</c> and <c>--replay-check</c>). A tape must give back exactly what was
/// written — any text as one field, a float to the bit, its lines in their order — or a replay reads a
/// different run; and the digest's check must stop at the first frame and field that differ, and name them,
/// or a restructuring's first divergence is lost in its consequences. These need no install;
/// <see cref="ReplayBaselineTests"/> replays the recorded runs themselves.
/// </summary>
public class ReplayTapeTests
{
    [Theory]
    [InlineData("")]
    [InlineData("plain")]
    [InlineData("two words")]
    [InlineData("a=b%c\td\ne")]
    [InlineData("Keypad 4;wait:20;text:god mode")]
    [InlineData("Ünïcödé — ✝ 𝄞")]
    public void Any_text_is_one_field_and_comes_back_the_same(string text)
    {
        var field = ReplayTape.Escape(text);
        Assert.DoesNotContain(' ', field);
        Assert.DoesNotContain('=', field);
        Assert.DoesNotContain('\n', field);
        Assert.Equal(text, ReplayTape.Unescape(field));
    }

    [Theory]
    [InlineData(0.033f)]
    [InlineData(0.0166666675f)]
    [InlineData(1e-3f)]
    [InlineData(123.456789f)]
    public void A_step_reads_back_to_the_bit(float step)
    {
        var text = ReplayTape.Float(step);
        var back = float.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(BitConverter.SingleToUInt32Bits(step), BitConverter.SingleToUInt32Bits(back));
    }

    [Fact]
    public void A_tape_reads_back_line_for_line_in_its_order()
    {
        var text = new StringWriter();
        using (var tape = new TapeWriter(text, "a test"))
        {
            tape.Write("arg", ReplayTape.Escape("--level"));
            tape.Write("arg", ReplayTape.Escape("P1 MON1"));
            tape.Write("interactive", "0");
            tape.Frame();
            tape.Write("state", "mouse=10,20,1");
            tape.Write("ev", "key", "down", "sc=4", "mod=0x1");
            tape.Write("none");
            tape.Write("step", ReplayTape.Float(0.033f));
            tape.Frame();
            tape.Write("none");
            tape.Write("step", ReplayTape.Float(0.016f));
        }
        var reader = new TapeReader(new StringReader(text.ToString()));
        Assert.Equal(2, reader.FrameCount);
        Assert.Equal(["--level", "P1 MON1"], reader.HeadOf("arg").Select(f => ReplayTape.Unescape(f[0])));
        string[] kinds = ["arg", "arg", "interactive", "frame", "state", "ev", "none", "step", "frame", "none", "step"];
        foreach (var kind in kinds) Assert.Equal(kind, reader.Next()!.Value.Kind);
        Assert.True(reader.Done);                       // only `end` is left
        var ev = ReplayTape.Parse("ev key down sc=4 key=0x61 mod=0x1")!.Value;
        Assert.Equal(4, ev.Long("sc"));
        Assert.Equal(0x61, ev.Long("key"));
        Assert.Null(ev["b"]);
    }

    [Fact]
    public void A_recording_given_a_folder_names_its_own_tape_in_it_and_a_file_is_the_tape()
    {
        var at = new DateTime(2026, 10, 5, 17, 4, 9);
        Assert.Equal("mon1.tape", ReplayTape.NameIn("mon1.tape", at));
        Assert.Equal(Path.Combine("runs", "play07.tape.gz"), ReplayTape.NameIn(Path.Combine("runs", "play07.tape.gz"), at));
        Assert.Equal(Path.Combine("kept/", "play-20261005-170409.tape.gz"), ReplayTape.NameIn("kept/", at));
        Assert.Equal(Path.Combine("kept\\", "play-20261005-170409.tape.gz"), ReplayTape.NameIn("kept\\", at));
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"vitomir-tape-name-{Guid.NewGuid():N}")).FullName;
        try { Assert.Equal(Path.Combine(folder, "play-20261005-170409.tape.gz"), ReplayTape.NameIn(folder, at)); }
        finally { Directory.Delete(folder); }
    }

    /// <summary>
    /// A recording was once made from the title screen in a picture of
    /// 2560 by 1440 and replayed in the default 1600 by 900: the tape named no size until the game's loop, the
    /// menus place a click by the window's size, and the click on Load never landed. A replay's window is made
    /// the first size the tape names, wherever it is.
    /// </summary>
    [Fact]
    public void A_tapes_first_size_is_the_menus_when_it_names_one_and_else_the_games_however_deep()
    {
        static TapeReader Tape(string body) => new(new StringReader(ReplayTape.Magic + "\n" + body));
        // A tape: the menus' frames name none; the game's loop does, 890 frames in.
        Assert.Equal((2560, 1440), Tape("arg --seed\nclock 0\nframe 1\nnone\nframe 2\nnone\nfixes a=0\ninteractive 1 size=2560x1440\nframe 3\n").FirstSize());
        // Now the front end's loop names its own, and that comes first even when the game's differs.
        Assert.Equal((1920, 1080), Tape("window size=1920x1080\nclock 0\nframe 1\ninteractive 1 size=2560x1440\nframe 2\n").FirstSize());
        Assert.Equal((320, 240), Tape("arg --size\narg 320x240\ninteractive 0 size=320x240\nframe 1\n").FirstSize());
        Assert.Null(Tape("arg --seed\nframe 1\nnone\n").FirstSize());
        Assert.Null(Tape("interactive 1 size=0x0\nframe 1\n").FirstSize());
        Assert.Null(Tape("interactive 1\nframe 1\n").FirstSize());
    }

    [Fact]
    public void Something_that_is_not_a_tape_is_refused()
    {
        Assert.Throws<InvalidDataException>(() => new TapeReader(new StringReader("vitomir-digest 1\nf 1\n")));
    }

    // ---- the digest ----------------------------------------------------------------------------

    static DigestFrame Frame(int n, float walkerX, bool withCrate = true, int draws = 0)
    {
        var things = new SortedDictionary<int, string[]>
        {
            [5] = ["template=christof", $"pos={ReplayTape.Float(walkerX)},0,0", "turn=0,0,90", "hp=100", "flags=0x0", "mode=party", "action=Stand"],
            [9] = ["template=torch", "pos=1,2,3", "turn=0,0,0", "hp=10", "flags=0x40", "mode=-", "action=-"],
        };
        if (withCrate) things[12] = ["template=crate", "pos=7,7,7", "turn=0,0,0", "hp=5", "flags=0x0", "mode=-", "action=-"];
        return new DigestFrame
        {
            Frame = n,
            Header = [new("clock", (n * 33).ToString()), new("guid", "100"), new("combat", draws.ToString())],
            Things = things,
        };
    }

    static string Write(IEnumerable<DigestFrame> frames)
    {
        var text = new StringWriter();
        using (var w = new DigestWriter(text)) foreach (var f in frames) w.Write(f);
        return text.ToString();
    }

    [Fact]
    public void A_digest_writes_a_thing_only_when_it_changed_and_reads_back_whole()
    {
        var frames = Enumerable.Range(1, 10).Select(n => Frame(n, n * 2f, withCrate: n < 6)).ToList();
        var text = Write(frames);
        // The torch never changes: one row in the whole file. The walker moves: a row every frame.
        Assert.Equal(1, text.Split('\n').Count(l => l.StartsWith("t 9 ")));
        Assert.Equal(10, text.Split('\n').Count(l => l.StartsWith("t 5 ")));
        Assert.Equal(1, text.Split('\n').Count(l => l == "x 12"));
        using var reader = new DigestReader(new StringReader(text));
        foreach (var expected in frames)
        {
            var got = reader.Next();
            Assert.NotNull(got);
            Assert.Null(SimDigest.Compare(expected, got));
            Assert.Equal(expected.Things.Keys, got.Things.Keys);
        }
        Assert.Null(reader.Next());
    }

    [Fact]
    public void A_perturbed_replay_is_caught_at_its_frame_and_field()
    {
        var recorded = Enumerable.Range(1, 20).Select(n => Frame(n, n * 2f)).ToList();
        // The replay's walker is a hair off from frame 13 on (one ulp), and everything after follows from it.
        var replayed = recorded.Select(f => f.Frame < 13 ? f : Frame(f.Frame, BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(f.Frame * 2f) + 1))).ToList();
        using var checker = new DigestChecker(new DigestReader(new StringReader(Write(recorded))));
        Divergence? first = null;
        foreach (var f in replayed) first ??= checker.Check(f);
        Assert.NotNull(first);
        Assert.Equal(13, first.Value.Frame);
        Assert.Equal("thing 5 (christof)", first.Value.What);
        Assert.Equal("pos", first.Value.Field);
        Assert.Equal(12, checker.Agreed);
    }

    [Fact]
    public void A_stream_drawn_once_more_is_named_before_any_thing()
    {
        var recorded = Enumerable.Range(1, 5).Select(n => Frame(n, 1f)).ToList();
        var replayed = recorded.Select(f => f.Frame < 4 ? f : Frame(f.Frame, 1f, draws: 1)).ToList();
        using var checker = new DigestChecker(new DigestReader(new StringReader(Write(recorded))));
        Divergence? first = null;
        foreach (var f in replayed) first ??= checker.Check(f);
        Assert.Equal(new Divergence(4, "the frame's", "combat", "0", "1"), first);
    }

    [Fact]
    public void A_thing_gone_in_the_replay_only_is_its_presence()
    {
        var recorded = Enumerable.Range(1, 5).Select(n => Frame(n, 1f)).ToList();
        var replayed = recorded.Select(f => f.Frame < 3 ? f : Frame(f.Frame, 1f, withCrate: false)).ToList();
        using var checker = new DigestChecker(new DigestReader(new StringReader(Write(recorded))));
        Divergence? first = null;
        foreach (var f in replayed) first ??= checker.Check(f);
        Assert.Equal(3, first!.Value.Frame);
        Assert.Equal("presence", first.Value.Field);
        Assert.Equal("thing 12 (crate)", first.Value.What);
    }

    [Fact]
    public void A_frame_the_replay_did_not_step_is_named()
    {
        var recorded = Enumerable.Range(1, 5).Select(n => Frame(n, 1f)).ToList();
        using var checker = new DigestChecker(new DigestReader(new StringReader(Write(recorded))));
        Assert.Null(checker.Check(recorded[0]));
        Assert.Null(checker.Check(recorded[1]));
        var first = checker.Check(recorded[3]);                // frame 3 skipped
        Assert.Equal(new Divergence(3, "the frame", "stepped", "yes", "no"), first);
    }

    [Fact]
    public void The_hash_is_the_same_in_every_process()
    {
        Assert.Equal("811c9dc5", SimDigest.Hash(""));           // FNV-1a's offset basis
        Assert.Equal("e40c292c", SimDigest.Hash("a"));
    }

    [Fact]
    public void A_random_generators_state_is_read_and_moves_with_its_draws()
    {
        var a = new Random(7);
        var b = new Random(7);
        Assert.NotEqual("?", SimDigest.RandomState(a));
        Assert.Equal(SimDigest.RandomState(a), SimDigest.RandomState(b));
        a.Next();
        Assert.NotEqual(SimDigest.RandomState(a), SimDigest.RandomState(b));
        b.Next();
        Assert.Equal(SimDigest.RandomState(a), SimDigest.RandomState(b));
    }
}
