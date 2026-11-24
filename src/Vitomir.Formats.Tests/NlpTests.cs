// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text;

namespace Vitomir.Formats.Tests;

/// <summary>
/// The lip-sync track reader (<see cref="Nlp"/>): the header thrown away, `"%d,%c"` per
/// line with the frame number ignored, a letter past `i` (the `<none>` the tracks end on) as shape 0,
/// a last line with no newline dropped as `fgets` then `feof` drops it — and every track in the
/// install read with nothing out of range.
/// </summary>
public class NlpTests
{
    static byte[] Read(string text) => Nlp.Read(Encoding.Latin1.GetBytes(text));

    [Fact]
    public void A_track_is_its_letters_in_file_order_with_the_header_skipped()
    {
        Assert.Equal([8, 8, 3, 1, 0], Read("Frame#,Mouth\r\n0,I\r\n1,i\r\n2,D\r\n3,B\r\n4,A\r\n"));
    }

    [Fact]
    public void The_frame_number_is_ignored_and_a_line_that_does_not_scan_is_skipped()
    {
        // Out of order, repeated, and a blank and a word among them: the shapes are in file order.
        Assert.Equal([2, 4, 6], Read("Frame#,Mouth\r\n9,C\r\n\r\nnonsense\r\n2,E\r\n2,G\r\n"));
        // The first line goes whatever it is.
        Assert.Equal([1], Read("0,H\r\n1,B\r\n"));
    }

    [Fact]
    public void Anything_past_the_ninth_letter_is_the_first_shape()
    {
        Assert.Equal([0, 0, 0], Read("Frame#,Mouth\r\n0,<none>\r\n1,Z\r\n2,j\r\n"));
    }

    [Fact]
    public void A_last_line_without_a_newline_is_dropped()
    {
        Assert.Equal([2], Read("Frame#,Mouth\r\n0,C\r\n1,D"));
        Assert.Empty(Read("Frame#,Mouth"));
        Assert.Empty(Read(""));
    }

    [Fact]
    public void No_track_has_more_than_the_engine_s_five_thousand_frames()
    {
        var sb = new StringBuilder("Frame#,Mouth\r\n");
        for (int i = 0; i < 6000; i++) sb.Append(i).Append(",B\r\n");
        Assert.Equal(Nlp.MaxFrames, Read(sb.ToString()).Length);
    }

    [Fact]
    public void Every_shipped_track_reads_and_names_only_the_nine_shapes()
    {
        GameData.RequireInstall();
        var dialog = Path.Combine(GameData.Dir!, "Sounds", "Dialog");
        Assert.SkipUnless(Directory.Exists(dialog), "no Sounds\\Dialog in the install");
        var files = Directory.EnumerateFiles(dialog)
            .Where(f => f.EndsWith(".nlp", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.SkipWhen(files.Length == 0, "no .nlp files in the install");
        foreach (var f in files)
        {
            var bytes = File.ReadAllBytes(f);
            var track = Nlp.Read(bytes);
            Assert.True(track.Length > 0, $"{Path.GetFileName(f)} has no frames");
            Assert.All(track, s => Assert.InRange(s, (byte)0, (byte)(Nlp.Shapes - 1)));
            // One frame a line after the header, bar a last line with no newline after it.
            int lines = Encoding.Latin1.GetString(bytes).Split('\n').Count(l => l.Contains(','));
            Assert.InRange(track.Length, lines - 2, lines - 1);
        }
    }
}
