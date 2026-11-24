// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Buffers.Binary;
using System.Text;
using Vitomir.Formats;

namespace Vitomir.Formats.Tests;

/// <summary>
/// The two sound formats the engine reads itself: `.wav` and the foley
/// file, `.nag`.
/// </summary>
public class SoundFormatTests
{
    /// <summary>A WAV with the chunks given, `fmt ` first unless told otherwise.</summary>
    static byte[] Make(int channels, int rate, int bits, byte[] data, params (string Id, byte[] Body)[] before)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(0); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)channels); w.Write(rate);
        w.Write(rate * channels * bits / 8); w.Write((short)(channels * bits / 8)); w.Write((short)bits);
        foreach (var (id, body) in before) { w.Write(Encoding.ASCII.GetBytes(id)); w.Write(body.Length); w.Write(body); }
        w.Write("data"u8); w.Write(data.Length); w.Write(data);
        return ms.ToArray();
    }

    [Fact]
    public void A_wav_is_read_for_its_rate_its_bits_and_where_its_samples_are()
    {
        var file = Make(1, 22050, 16, new byte[44100]);
        var info = Wav.Read(file)!;
        Assert.Equal(22050, info.Rate);
        Assert.Equal(16, info.Bits);
        Assert.False(info.Stereo);
        Assert.Equal(44, info.DataOffset);
        Assert.Equal(44100, info.DataSize);
        Assert.Equal(1000, info.LengthMs);                 // 22,050 samples of 16 bits
    }

    /// <summary>
    /// The length formula has no channel count in it — `size × 8 ÷ (bits × rate) × 1000` — and a
    /// stereo file's is halved afterwards, which is what makes it right.
    /// </summary>
    [Fact]
    public void A_stereo_length_is_halved_after_the_formula()
    {
        var info = Wav.Read(Make(2, 22050, 16, new byte[88200]))!;
        Assert.True(info.Stereo);
        Assert.Equal(1000, info.LengthMs);
    }

    /// <summary>
    /// The walk to `data` skips each chunk by its size and **no pad byte** — so an odd-sized chunk
    /// ahead of `data` would throw the walk off by one, where a RIFF reader would not.
    /// </summary>
    [Fact]
    public void Chunks_are_skipped_without_the_riff_pad_byte()
    {
        var even = Wav.Read(Make(1, 22050, 16, new byte[8], ("LIST", new byte[4])))!;
        Assert.Equal(8, even.DataSize);
        var odd = Make(1, 22050, 16, new byte[8], ("LIST", new byte[3]));
        // A RIFF writer pads the odd chunk; the engine's walk does not step over the pad.
        const int afterList = 36 + 8 + 3;
        var padded = odd.Take(afterList).Concat(new byte[1]).Concat(odd.Skip(afterList)).ToArray();
        Assert.Null(Wav.Read(padded));
        Assert.NotNull(Wav.Read(odd));
    }

    [Fact]
    public void Only_WAVE_at_offset_eight_is_a_wav()
    {
        var file = Make(1, 22050, 16, new byte[8]);
        file[0] = (byte)'X';                                // the RIFF is never looked at
        Assert.NotNull(Wav.Read(file));
        file[8] = (byte)'X';
        Assert.Null(Wav.Read(file));
    }

    [Fact]
    public void Every_shipped_recording_is_plain_16_bit_pcm()
    {
        int n = 0, rate44 = 0, stereo = 0;
        foreach (var nob in new[] { "Sounds.nob", "Local_Eng.nob" })
        {
            GameData.RequireInstall();
            if (!File.Exists(Path.Combine(GameData.Dir!, nob))) continue;
            using var archive = new NobArchive(Path.Combine(GameData.Dir!, nob));
            foreach (var name in archive.NamesWithExtension(".wav"))
            {
                var info = Wav.Read(archive.Read(name));
                Assert.True(info is not null, name);
                Assert.Equal(1, info!.FormatTag);
                Assert.Equal(16, info.Bits);
                if (info.Rate == 44100) rate44++;
                if (info.Stereo) stereo++;
                n++;
            }
        }
        Assert.SkipWhen(n == 0, "no sound archives in the install");
        Assert.True(n >= 1462);
        if (n == 2786) { Assert.Equal(34, rate44); Assert.Equal(1, stereo); }
    }

    // ------------------------------------------------------------------------------------ .nag

    [Fact]
    public void The_event_tables_are_the_engines()
    {
        Assert.Equal(151, Nag.ThingEvents.Length);
        Assert.Equal(22, Nag.UiEvents.Length);
        Assert.Equal(28, Nag.EventSlot("walk_carpet"));
        Assert.Equal(37, Nag.EventSlot("run_carpet"));
        Assert.Equal(75, Nag.EventSlot("death_bodyfall"));
        Assert.Equal(115, Nag.EventSlot("flap"));
        Assert.Equal(150, Nag.EventSlot("vox_wakeup"));
        Assert.Equal(0, Nag.EventSlot("BUTTONDEPRESS"));    // the interface shares the slots
        Assert.Equal(19, Nag.EventSlot("newrank"));         // and names are compared without case
        Assert.Equal(-1, Nag.EventSlot("footstep"));
    }

    [Fact]
    public void A_line_is_an_event_a_recording_and_then_numbers_and_words_in_any_order()
    {
        var e = Nag.Read("walk_stone step.wav linktothing 0.5 looping 1.1 300 900 7")[0];
        Assert.Equal(Nag.EventSlot("walk_stone"), e.Event);
        Assert.Equal("step.wav", e.Sound);
        Assert.Equal(0.5f, e.Volume);
        Assert.Equal(1.1f, e.Rate);
        Assert.Equal(300f, e.MinDist);                      // the third number is the near distance
        Assert.Equal(900f, e.MaxDist);                      // the fourth the far; a fifth is dropped
        Assert.Equal(0x4u | 0x2u | 0x8u, e.Flags);          // the default linktopos stays
    }

    [Fact]
    public void The_defaults_are_three_quarters_centred_two_hundred_to_a_thousand_and_linktopos()
    {
        var e = Nag.Read("swing whoosh.wav")[0];
        Assert.Equal((0.75f, 1f, 200f, 1000f, 2u), (e.Volume, e.Rate, e.MinDist, e.MaxDist, e.Flags));
    }

    [Fact]
    public void Local_drops_both_links_and_a_link_drops_local()
    {
        Assert.Equal(0x10u, Nag.Read("use a.wav local")[0].Flags);
        Assert.Equal(0x10u | 0x100u, Nag.Read("use a.wav local voice")[0].Flags);
        Assert.Equal(0x4u, Nag.Read("use a.wav local linktothing")[0].Flags);   // the default went with local
        Assert.Equal(1f, Nag.Read("use a.wav 3.5")[0].Volume);              // held to 0..1
        Assert.Equal(0f, Nag.Read("use a.wav -2")[0].Volume);
    }

    [Fact]
    public void Numbers_are_read_the_way_sscanf_reads_them_and_a_misspelt_word_is_nothing()
    {
        var e = Nag.Read("use a.wav 0.4x loop")[0];         // "loop" is not "looping"
        Assert.Equal(0.4f, e.Volume);
        Assert.Equal(2u, e.Flags);
        Assert.True(Nag.ScanFloat("1e2", out var f) && f == 100f);
        Assert.False(Nag.ScanFloat(".", out _));
    }

    /// <summary>
    /// A semicolon only comments a line out in the first column — indented, it is part of the first
    /// token, which then names no event and is skipped for that reason instead. A line with an
    /// event and no recording is nothing.
    /// </summary>
    [Fact]
    public void A_line_with_no_known_event_or_no_recording_is_skipped()
    {
        var all = Nag.Read("; swing a.wav\n  ;swing b.wav\nkick c.wav\nswing\nstopmove d.wav\n");
        Assert.Equal("d.wav", Assert.Single(all).Sound);
    }

    [Fact]
    public void Every_shipped_foley_file_reads()
    {
        using var resource = GameData.Open("Resource.nob");
        int files = 0, entries = 0;
        foreach (var name in resource.NamesWithExtension(".nag"))
        {
            entries += Nag.Read(Encoding.Latin1.GetString(resource.Read(name))).Count;
            files++;
        }
        Assert.Equal(222, files);
        Assert.Equal(15191, entries);
    }
}
