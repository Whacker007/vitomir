// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Text.Json;
using Vitomir.Engine.Diagnostics;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **`--frame-dump-log`: a line per dumped picture** (ours). The web site's clips draw their overlay from it, so what is
/// pinned is that a line reads back as the frame it describes: every field under its own name, numbers the same on any
/// machine's locale, the camera's wrapped angles made signed, and a file of one line per picture.
/// </summary>
public class FrameDumpLogTests
{
    static FrameDumpEntry Entry(int n = 7) => new(
        n, 120, 1984, 1920, 1080, [26, 92], ["Forward", "TurnLeft"], ["SwivelLock"], 0b101, 0b10, -1,
        960.5f, 540.25f, 1920, 1080, true, "Game", -42.4567f, -30.25f, -80f, -20f, 0.5f, 1f, true, true, true, false);

    [Fact]
    public void A_line_reads_back_as_its_entry_whatever_the_locale()
    {
        var was = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");          // a comma for the decimal point, if the line let it
            using var doc = JsonDocument.Parse(Entry().Line());
            var r = doc.RootElement;
            Assert.Equal(7, r.GetProperty("n").GetInt32());
            Assert.Equal(120, r.GetProperty("frame").GetInt32());
            Assert.Equal(1984, r.GetProperty("simMs").GetInt64());
            Assert.Equal([26, 92], r.GetProperty("keys").EnumerateArray().Select(k => k.GetInt32()));
            Assert.Equal(["Forward", "TurnLeft"], r.GetProperty("held").EnumerateArray().Select(k => k.GetString()));
            Assert.Equal(5u, r.GetProperty("buttons").GetUInt32());
            Assert.Equal(["SwivelLock"], r.GetProperty("pressed").EnumerateArray().Select(k => k.GetString()));
            Assert.Equal(2u, r.GetProperty("clicks").GetUInt32());
            Assert.Equal(-1, r.GetProperty("wheel").GetInt32());
            Assert.Equal(960.5, r.GetProperty("pointerX").GetDouble());
            Assert.Equal(-42.457, r.GetProperty("yaw").GetDouble());          // three places
            Assert.Equal("Game", r.GetProperty("mode").GetString());
            Assert.True(r.GetProperty("lock").GetBoolean());
            Assert.False(r.GetProperty("menu").GetBoolean());
            Assert.DoesNotContain(',', r.GetProperty("pitch").GetRawText());
        }
        finally { CultureInfo.CurrentCulture = was; }
    }

    [Theory]
    [InlineData(280f, -80f)]      // the pitch's lower limit as the camera stores it
    [InlineData(340f, -20f)]
    [InlineData(90f, 90f)]
    [InlineData(-270f, 90f)]
    [InlineData(540f, 180f)]
    public void Wrapped_angles_are_made_signed(float stored, float signed) =>
        Assert.Equal(signed, FrameDumpEntry.Signed(stored), 3);

    [Fact]
    public void The_default_keys_are_written_for_every_command()
    {
        using var doc = JsonDocument.Parse(FrameDumpLog.Defaults());
        var commands = doc.RootElement.GetProperty("commands").EnumerateArray().ToDictionary(c => c.GetProperty("command").GetString()!);
        Assert.Equal(Vitomir.Engine.Ui.KeyBindings.All.Length, commands.Count);
        // The swivel lock is ours, Ctrl with the middle button; driving's forward is W; turning left is the keypad's 4.
        var lockChord = commands["SwivelLock"].GetProperty("defaults")[0];
        Assert.Equal(2, lockChord.GetProperty("button").GetInt32());
        Assert.True(lockChord.GetProperty("ctrl").GetBoolean());
        Assert.Equal(26, commands["Forward"].GetProperty("defaults")[0].GetProperty("key").GetInt32());
        Assert.Equal(92, commands["TurnLeft"].GetProperty("defaults")[0].GetProperty("key").GetInt32());
    }

    [Fact]
    public void The_file_has_a_line_per_picture()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vitomir-dumplog-{Guid.NewGuid():n}", "log.jsonl");
        try
        {
            using (var log = FrameDumpLog.Open(path)!)
                for (int i = 0; i < 3; i++) log.Write(Entry(i));
            var lines = File.ReadAllLines(path);
            Assert.Equal(3, lines.Length);
            Assert.Equal([0, 1, 2], lines.Select(l => JsonDocument.Parse(l).RootElement.GetProperty("n").GetInt32()));
            Assert.Null(FrameDumpLog.Open(null));
            Assert.True(File.Exists(FrameDumpLog.DefaultsPath(path)));      // the default keys, written beside the log
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }
    [Fact]
    public void A_run_after_a_load_carries_the_log_on()
    {
        // A load ends the game's run and begins another in the same process, which opens the log again: its lines go on
        // after the first run's instead of the file starting over (a tape with a quick load in it lost all but its last run).
        string path = Path.Combine(Path.GetTempPath(), $"vitomir-dumplog-{Guid.NewGuid():n}", "log.jsonl");
        try
        {
            var first = FrameDumpLog.Open(path)!;
            first.Write(Entry(0));
            first.Write(Entry(1));
            first.Flush();                                                    // the first run's end
            var second = FrameDumpLog.Open(path)!;
            Assert.Same(first, second);
            second.Write(Entry(2));
            second.Dispose();
            Assert.Equal([0, 1, 2], File.ReadAllLines(path).Select(l => JsonDocument.Parse(l).RootElement.GetProperty("n").GetInt32()));
            using var again = FrameDumpLog.Open(path)!;                       // closed, the path opens afresh
            Assert.NotSame(first, again);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }
}
