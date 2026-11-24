// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Audio;

namespace Vitomir.Platform.Tests;

/// <summary>
/// **The platform's audio output**: it tops a device's queue up from a
/// source to a tenth of a second, more after a long frame; the amount hook is told what the device wants and its answer is
/// what is mixed; the tap sees what the device was sent; with no device only the hook's frames are mixed, and with neither
/// nothing is.
/// </summary>
public class AudioOutputTests
{
    static readonly AudioFormat Mixer = new(22_050, 2);

    /// <summary>A source that counts the frames asked of it and writes each sample as its number since the start.</summary>
    sealed class Counting(AudioFormat format) : IAudioSource
    {
        public AudioFormat Format => format;
        public long Made;
        public void Render(Span<short> into, int frames)
        {
            for (int i = 0; i < frames * format.Channels; i++) into[i] = (short)(Made * format.Channels + i);
            Made += frames;
        }
    }

    /// <summary>A device whose speakers play nothing unless told (<see cref="Play"/>), on a clock the test moves.</summary>
    sealed class Device(AudioFormat format) : IAudioDevice
    {
        public AudioFormat Format => format;
        public int Queued { get; private set; }
        public ulong NowNs { get; set; } = 1_000_000_000;
        public List<short> Sent { get; } = [];
        public bool Closed;
        public void Queue(ReadOnlySpan<short> samples) { Sent.AddRange(samples.ToArray()); Queued += samples.Length / format.Channels; }
        public void Play(int frames) => Queued = Math.Max(Queued - frames, 0);
        public void Dispose() => Closed = true;
    }

    [Fact]
    public void It_keeps_a_tenth_of_a_second_queued_and_more_after_a_long_frame()
    {
        var device = new Device(Mixer);
        var source = new Counting(Mixer);
        using var output = new AudioOutput(device);
        Assert.True(output.Live);
        Assert.Equal(2205, output.Ahead);
        Assert.Equal(2205, output.Pump(source));            // the first: a tenth
        device.NowNs += 16_666_667;                          // a sixtieth later, a sixtieth played
        device.Play(368);
        Assert.Equal(368, output.Pump(source));             // back up to the tenth
        Assert.Equal(0, output.Pump(source));                // nothing played since: nothing wanted
        device.NowNs += 250_000_000;                         // a quarter-second frame
        device.Play(2205);
        Assert.Equal((int)Math.Ceiling(0.375 * 22_050), output.Pump(source));   // one and a half of the gap (OutputQueue)
        Assert.Equal(source.Made, output.Frames);
        Assert.Equal(source.Made * 2, device.Sent.Count);
        for (int i = 0; i < device.Sent.Count; i++) Assert.Equal((short)i, device.Sent[i]);   // in order, nothing lost
    }

    [Fact]
    public void The_amount_hook_is_told_what_the_device_wants_and_its_answer_is_mixed()
    {
        var device = new Device(Mixer);
        var source = new Counting(Mixer);
        using var output = new AudioOutput(device);
        var told = new List<int>();
        Assert.Equal(700, output.Pump(source, wants => { told.Add(wants); return 700; }));   // a replay's recorded number
        Assert.Equal(700, device.Queued);
        Assert.Equal(0, output.Pump(source, wants => { told.Add(wants); return 0; }));
        output.Pump(source, wants => { told.Add(wants); return wants; });                    // a recording's: what was wanted
        Assert.Equal(new[] { 2205, 2205 - 700, 2205 - 700 }, told);
        Assert.Equal(2205, device.Queued);
        // Told 0, never less, when the queue is past what it keeps.
        output.Pump(source, wants => { told.Add(wants); return 50; });
        Assert.Equal(0, told[^1]);
    }

    [Fact]
    public void The_tap_sees_what_the_device_was_sent()
    {
        var device = new Device(Mixer);
        var tapped = new List<short>();
        using var output = new AudioOutput(device) { Tap = s => tapped.AddRange(s.ToArray()) };
        output.Pump(new Counting(Mixer));
        Assert.Equal(device.Sent, tapped);
    }

    [Fact]
    public void With_no_device_only_the_hooks_frames_are_mixed_and_with_no_hook_nothing()
    {
        var source = new Counting(Mixer);
        var tapped = 0;
        using var output = new AudioOutput { Tap = s => tapped += s.Length };
        Assert.False(output.Live);
        Assert.Equal(0, output.Pump(source));                       // a muted run: the mixer makes nothing
        Assert.Equal(0, source.Made);
        int told = -1;
        Assert.Equal(1234, output.Pump(source, wants => { told = wants; return 1234; }));   // a muted replay: the recorded frames
        Assert.Equal(0, told);                                      // a muted recording writes 0
        Assert.Equal(1234, source.Made);
        Assert.Equal(0, tapped);                                    // mixed and dropped
    }

    [Fact]
    public void A_source_of_another_format_is_refused_and_the_device_is_closed_with_the_output()
    {
        var device = new Device(Mixer);
        var output = new AudioOutput(device);
        Assert.Throws<InvalidOperationException>(() => output.Pump(new Counting(new AudioFormat(44_100, 2))));
        output.Dispose();
        Assert.True(device.Closed);
    }
}
