// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Render.Tests;

/// <summary>
/// **A frame dump's pace** (ours, <c>--frame-dump</c>): a picture at once, then one every <c>1/n</c> of a second of the
/// frames' steps, so a run stepped faster than the dump's rate is thinned to it and one stepped slower gives each of its
/// frames once, never one twice to make up the rate.
/// </summary>
public class FrameDumpTests
{
    static int Count(float fps, float step, int frames)
    {
        string folder = Path.Combine(Path.GetTempPath(), $"vitomir-dump-{Guid.NewGuid():n}");
        try
        {
            var dump = FrameDump.From(folder, fps.ToString(System.Globalization.CultureInfo.InvariantCulture))!;
            int due = 0;
            for (int i = 0; i < frames; i++) if (dump.Due(step)) due++;
            return due;
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }

    [Theory]
    [InlineData(60f, 1f / 144f, 1440, 600)]   // ten seconds at 144 Hz, thinned to sixty a second
    [InlineData(60f, 1f / 60f, 600, 600)]     // a frame for a frame
    [InlineData(60f, 1f / 30f, 300, 300)]     // ten seconds at 30: each frame once, none twice
    [InlineData(30f, 1f / 60f, 600, 300)]
    public void Pictures_come_at_the_rate_asked_or_each_frame_once(float fps, float step, int frames, int pictures) =>
        Assert.InRange(Count(fps, step, frames), pictures - 1, pictures + 1);

    [Fact]
    public void No_folder_no_dump() => Assert.Null(FrameDump.From(null, "60"));
    [Fact]
    public void A_run_after_a_load_carries_the_pictures_on()
    {
        // The run after a load asks for the same folder and gets the same dump: its pictures are numbered on from the last
        // run's, not written again from frame00000.png over them.
        string folder = Path.Combine(Path.GetTempPath(), $"vitomir-dump-{Guid.NewGuid():n}");
        try
        {
            var first = FrameDump.From(folder, "30")!;
            first.Write(new byte[4 * 2 * 2], 2, 2);
            first.Write(new byte[4 * 2 * 2], 2, 2);
            var second = FrameDump.From(folder, "30")!;
            Assert.Same(first, second);
            second.Write(new byte[4 * 2 * 2], 2, 2);
            Assert.Equal(3, second.Written);
            Assert.True(File.Exists(Path.Combine(folder, "frame00002.png")));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }
}
