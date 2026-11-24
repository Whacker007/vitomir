// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A play-test's files written compressed (ours; about 200 MB an hour as plain text).
// `LogFile` writes gzip when the name ends in `.gz` and plain otherwise, and reads either whatever the
// name. What is pinned: the rule itself, the companions' names, both forms read back by the tape's and
// the digest's own readers, a log added to under a second header, and a compressed file cut short
// by a crash still read up to where it stops — the case a play-test's evidence most needs.
using System.IO.Compression;
using Vitomir.Engine.Diagnostics;

namespace Vitomir.Engine.Tests;

public sealed class LogFileTests : IDisposable
{
    readonly string folder = Directory.CreateTempSubdirectory("vitomir-logfile-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    string At(string name) => Path.Combine(folder, name);

    static bool IsGzip(string path)
    {
        var head = new byte[2];
        using var f = File.OpenRead(path);
        return f.Read(head) == 2 && head[0] == 0x1f && head[1] == 0x8b;
    }

    [Fact]
    public void A_name_ending_in_gz_is_written_compressed_and_any_other_plain()
    {
        foreach (var name in new[] { "a.tsv", "a.tsv.gz", "b.tape", "b.tape.GZ" })
        {
            using (var w = LogFile.CreateText(At(name))) w.WriteLine("hello");
            Assert.Equal(name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase), IsGzip(At(name)));
            Assert.Equal(["hello"], LogFile.ReadLines(At(name)));
            Assert.False(File.Exists(At(name + ".gz")), "nothing is renamed");
        }
    }

    [Fact]
    public void A_tapes_companions_follow_its_form()
    {
        Assert.Equal("s.tape.digest", LogFile.Beside("s.tape", ".digest"));
        Assert.Equal("s.tape.digest.gz", LogFile.Beside("s.tape.gz", ".digest"));
        Assert.Equal("s.tape.replay.digest.gz", LogFile.Beside("s.tape.gz", ".replay.digest"));
    }

    [Fact]
    public void A_name_that_is_not_there_is_found_with_gz_added_or_taken_off()
    {
        File.WriteAllText(At("x.tape.gz"), "");
        File.WriteAllText(At("y.tape"), "");
        Assert.Equal(At("x.tape.gz"), LogFile.Find(At("x.tape")));
        Assert.Equal(At("y.tape"), LogFile.Find(At("y.tape.gz")));
        Assert.Equal(At("y.tape"), LogFile.Find(At("y.tape")));
        Assert.Null(LogFile.Find(At("z.tape")));
    }

    [Fact]
    public void A_file_is_read_through_gzip_by_its_bytes_not_its_name()
    {
        using (var w = LogFile.CreateText(At("c.tsv.gz"))) w.WriteLine("compressed");
        File.Move(At("c.tsv.gz"), At("c.tsv"));
        Assert.Equal(["compressed"], LogFile.ReadLines(At("c.tsv")));
    }

    /// <summary>
    /// The tape and its digest, compressed, read back by their own readers exactly as plain ones are — the
    /// replay and the replay check read nothing else.
    /// </summary>
    [Fact]
    public void A_compressed_tape_and_digest_read_back_as_plain_ones_do()
    {
        foreach (var tape in new[] { At("t.tape"), At("t.tape.gz") })
        {
            using (var w = new TapeWriter(LogFile.CreateText(tape), "a test"))
            {
                w.Write("arg", "--level");
                w.Write("arg", "P1_MON1");
                for (int i = 0; i < 300; i++) { w.Frame(); w.Write("step", "33"); }
            }
            var r = TapeReader.Open(tape);
            Assert.Equal(300, r.FrameCount);
            Assert.Equal(["--level", "P1_MON1"], r.HeadOf("arg").Select(f => f[0]));

            string digest = LogFile.Beside(tape, ".digest");
            using (var d = new DigestWriter(LogFile.CreateText(digest), "a test"))
                for (int f = 1; f <= 300; f++) d.Write(new DigestFrame { Frame = f, Header = [new("clock", (f * 33).ToString())] });
            using var back = DigestReader.Open(digest);
            int frames = 0;
            while (back.Next() is not null) frames++;
            Assert.Equal(300, frames);
        }
        Assert.True(IsGzip(At("t.tape.digest.gz")));
        Assert.False(IsGzip(At("t.tape.digest")));
    }

    /// <summary>
    /// A log a play adds to from each later run, under its own header: compressed, each run is another gzip member, and
    /// the reader reads on through all of them.
    /// </summary>
    [Fact]
    public void A_compressed_log_added_to_reads_as_one()
    {
        using (var w = LogFile.CreateText(At("w.tsv.gz"))) w.WriteLine("first");
        using (var w = LogFile.CreateText(At("w.tsv.gz"), append: true)) w.WriteLine("second");
        Assert.Equal(["first", "second"], LogFile.ReadLines(At("w.tsv.gz")));
    }

    /// <summary>
    /// **A crash's file.** A compressed file whose writer never closed it has no gzip trailer and may stop
    /// inside a block; it reads up to where it stops rather than throwing the play-test away.
    /// </summary>
    [Fact]
    public void A_compressed_file_cut_short_reads_up_to_where_it_stops()
    {
        var lines = Enumerable.Range(0, 20000).Select(i => $"frame {i} step 33 mouse {i % 1600} {i % 900}").ToArray();
        var whole = new MemoryStream();
        using (var gz = new GZipStream(whole, CompressionLevel.Optimal, leaveOpen: true))
        using (var w = new StreamWriter(gz)) foreach (var l in lines) w.WriteLine(l);
        File.WriteAllBytes(At("cut.tape.gz"), whole.ToArray()[..(int)(whole.Length / 2)]);

        var read = LogFile.ReadLines(At("cut.tape.gz")).ToList();
        Assert.InRange(read.Count, 1000, lines.Length - 1);
        Assert.Equal(lines[..(read.Count - 1)], read[..^1]);   // the last line may be cut mid-way
    }

    /// <summary>
    /// The writer's own flushes (a tape flushes every frame) reach the compressor at most once a second, so
    /// a frame's worth of text is not a deflate block of its own: flushed a frame at a time, a tape-like
    /// text still compresses better than four to one.
    /// </summary>
    [Fact]
    public void Flushing_every_frame_still_compresses()
    {
        long plain = 0;
        using (var w = LogFile.CreateText(At("f.tape.gz")))
            for (int i = 0; i < 20000; i++)
            {
                var line = $"frame {i}\nstep 16.{i % 7}\nmouse {i % 1600} {i % 900}";
                w.WriteLine(line);
                plain += line.Length + 1;
                w.Flush();
            }
        Assert.True(new FileInfo(At("f.tape.gz")).Length * 4 < plain,
                    $"{plain} bytes of text came to {new FileInfo(At("f.tape.gz")).Length}");
    }
}
