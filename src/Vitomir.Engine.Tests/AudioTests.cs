// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.IO.Compression;
using System.Numerics;
using Vitomir.Engine.Audio;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The mixer's rules and the two Miles laws, against recordings made up here —
/// no install needed.
/// </summary>
public class AudioTests
{
    /// <summary>A mono recording of a constant level (or a sine) at a rate.</summary>
    internal static Sound Tone(string name, int rate = 22050, int ms = 1000, short level = 16000, bool stereo = false,
                               bool sine = false)
    {
        int frames = rate * ms / 1000, ch = stereo ? 2 : 1;
        var pcm = new short[frames * ch];
        for (int i = 0; i < frames; i++)
            for (int c = 0; c < ch; c++)
                pcm[i * ch + c] = sine ? (short)(level * MathF.Sin(i * 2 * MathF.PI * 441f / rate)) : level;
        return new Sound
        {
            Name = name, Path = name, Rate = rate, Bits = 16, LengthMs = ms, Channels = ch, SourceRate = rate, Pcm = pcm,
            Flags = SoundFlags.Loaded | (stereo ? SoundFlags.Stereo : 0),
        };
    }

    static WorldThing Thing(Vector3 at, ThingType type = ThingType.Item) => new()
    {
        Index = 0, Guid = 1, Scene = NsdThing.Placed("test", at, Vector3.Zero, 0), Template = "test", Type = type,
        Position = at,
    };

    // ------------------------------------------------------------------------------ the Miles laws

    [Fact]
    public void A_centred_sample_plays_at_full_volume_on_both_sides()
    {
        Assert.Equal((2032, 2032), Miles.Gains(127, 63));
        Assert.Equal((2032, 2032), Miles.Gains(127, 64));
        Assert.Equal((2032, 0), Miles.Gains(127, 0));               // hard left
        Assert.Equal((0, 2032), Miles.Gains(127, 127));             // hard right
        Assert.Equal((1008, 1008), Miles.Gains(63, 63));            // volume is linear
        Assert.Equal(63, Miles.Pan(0.5f));
        Assert.Equal(95, Miles.Volume(1f, 0.75f, absolute: false)); // (int)(0.75 × 127)
        Assert.Equal(127, Miles.Volume(1f, 0.75f, absolute: true));
    }

    [Fact]
    public void The_fast_provider_halves_the_volume_for_every_doubling_past_the_near_distance()
    {
        var face = Vector3.UnitY; var right = Vector3.UnitX;
        Assert.Equal(127, Miles.Fast2D(127, new Vector3(0, 100, 0), Vector3.Zero, face, right, 200, 1000).Volume);
        Assert.Equal(64, Miles.Fast2D(127, new Vector3(0, 400, 0), Vector3.Zero, face, right, 200, 1000).Volume);
        Assert.Equal(32, Miles.Fast2D(127, new Vector3(0, 800, 0), Vector3.Zero, face, right, 200, 1000).Volume);
        Assert.True(Miles.Fast2D(127, new Vector3(0, 1001, 0), Vector3.Zero, face, right, 200, 1000).Paused);
        // Behind the listener: 29% more off (integer × 71 ÷ 100).
        Assert.Equal(90, Miles.Fast2D(127, new Vector3(0, -100, 0), Vector3.Zero, face, right, 200, 1000).Volume);
    }

    [Fact]
    public void The_fast_provider_pans_by_direction()
    {
        var face = Vector3.UnitY; var right = Vector3.UnitX;
        Assert.Equal(127, Miles.Fast2D(127, new Vector3(100, 0, 0), Vector3.Zero, face, right, 200, 1000).Pan);
        Assert.Equal(0, Miles.Fast2D(127, new Vector3(-100, 0, 0), Vector3.Zero, face, right, 200, 1000).Pan);
        Assert.Equal(63, Miles.Fast2D(127, new Vector3(0, 100, 0), Vector3.Zero, face, right, 200, 1000).Pan);
        Assert.Equal(64, Miles.Fast2D(127, Vector3.Zero, Vector3.Zero, face, right, 200, 1000).Pan);
    }

    // ------------------------------------------------------------------------------ the mixer

    [Fact]
    public void There_are_four_flat_channels_and_a_fifth_sound_is_refused()
    {
        var m = new Mixer();
        for (int i = 0; i < 4; i++) Assert.NotEqual(0, m.Play2D(Tone($"t{i}"), 1f, 0.5f, 0));
        Assert.Equal(0, m.Play2D(Tone("t4"), 1f, 0.5f, 0));
        Assert.Equal((4, 0, 0), m.ActiveCounts);
    }

    [Fact]
    public void A_hipriority_sound_takes_the_first_channel_that_is_not()
    {
        var m = new Mixer();
        int first = m.Play2D(Tone("a"), 1f, 0.5f, ChannelFlag.HiPriority);
        int second = m.Play2D(Tone("b"), 1f, 0.5f, 0);
        m.Play2D(Tone("c"), 1f, 0.5f, 0);
        m.Play2D(Tone("d"), 1f, 0.5f, 0);
        int urgent = m.Play2D(Tone("e"), 1f, 0.5f, ChannelFlag.HiPriority);
        Assert.NotEqual(0, urgent);
        Assert.NotNull(m.Get(first));
        Assert.Null(m.Get(second));                                 // stopped to make room
        Assert.Equal(second & 0xff, urgent & 0xff);
    }

    [Fact]
    public void A_fourth_play_of_one_recording_stops_the_oldest()
    {
        var m = new Mixer();
        var tone = Tone("same");
        m.Now = 10; int a = m.Play2D(tone, 1f, 0.5f, 0);
        m.Now = 20; int b = m.Play2D(tone, 1f, 0.5f, 0);
        m.Now = 30; int c = m.Play2D(tone, 1f, 0.5f, 0);
        Assert.Equal(3, tone.PlayCount);
        m.Now = 40; int d = m.Play2D(tone, 1f, 0.5f, 0);
        Assert.Null(m.Get(a));
        Assert.All(new[] { b, c, d }, id => Assert.NotNull(m.Get(id)));
        Assert.Equal(3, tone.PlayCount);
    }

    [Fact]
    public void A_positional_sound_beyond_its_far_distance_is_refused_and_a_stereo_one_plays_flat()
    {
        var m = new Mixer { ListenerPosition = Vector3.Zero };
        Assert.Equal(0, m.PlayAtPoint(Tone("far"), new Vector3(0, 900, 0), 800, 100, 1f, 0));
        int near = m.PlayAtPoint(Tone("near"), new Vector3(0, 500, 0), 800, 100, 1f, 0);
        Assert.Equal(ChannelKind.Positional, m.Get(near)!.Kind);
        int wide = m.PlayAtPoint(Tone("wide", stereo: true), new Vector3(0, 500, 0), 800, 100, 1f, 0);
        Assert.Equal(ChannelKind.Flat, m.Get(wide)!.Kind);
        // Flat and placed: a straight line from full at the listener to nothing at far − near.
        Assert.Equal(1f - 500f / 700f, m.Get(wide)!.Volume, 3);
    }

    [Fact]
    public void A_point_sound_the_listener_walks_away_from_stops_and_a_things_sound_follows_it()
    {
        var m = new Mixer();
        int id = m.PlayAtPoint(Tone("p"), new Vector3(0, 500, 0), 800, 100, 1f, ChannelFlag.Looping);
        m.ListenerPosition = new Vector3(0, -400, 0);
        m.Update(1 / 30f);
        Assert.Null(m.Get(id));

        var thing = Thing(new Vector3(100, 0, 0));
        m.ListenerPosition = Vector3.Zero;
        int on = m.PlayAtThing(Tone("t"), thing, 1000, 200, 1f, 0);
        thing.Position = new Vector3(300, 0, 0);
        m.Update(1 / 30f);
        Assert.Equal(thing.Position, m.Get(on)!.MilesPosition);
        Assert.True((thing.Flags & WorldThing.Flag.SoundLink) != 0);
        m.Forget(thing);
        Assert.True((m.Get(on)!.Flags & ChannelFlag.LinkToPos) != 0);
    }

    [Fact]
    public void A_line_on_a_characters_mouth_is_refused_while_the_last_one_is_still_being_said()
    {
        var m = new Mixer { GameMs = 1000 };
        var who = Thing(Vector3.Zero, ThingType.Actor);
        Assert.NotEqual(0, m.PlayAtThing(Tone("a", ms: 2000), who, 1000, 200, 1f, ChannelFlag.Voice | ChannelFlag.LinkToThing));
        Assert.Equal(3000, who.MouthBusyUntil);
        m.GameMs = 2000;
        Assert.Equal(0, m.PlayAtThing(Tone("b"), who, 1000, 200, 1f, ChannelFlag.Voice | ChannelFlag.LinkToThing));
        Assert.NotEqual(0, m.PlayAtThing(Tone("c"), who, 1000, 200, 1f, ChannelFlag.LinkToThing));   // not a voice
        m.GameMs = 3000;
        Assert.NotEqual(0, m.PlayAtThing(Tone("d"), who, 1000, 200, 1f, ChannelFlag.Voice | ChannelFlag.LinkToThing));
    }

    [Fact]
    public void A_volume_can_be_lowered_and_raised_again_no_further_than_it_started()
    {
        var m = new Mixer();
        var c = m.Get(m.Play2D(Tone("v"), 0.4f, 0.5f, 0))!;
        Mixer.SetVolume(c, 0.1f);
        Assert.Equal(0.1f, c.Volume);
        Mixer.SetVolume(c, 0.9f);
        Assert.Equal(0.4f, c.Volume);
    }

    [Fact]
    public void A_fade_out_stops_the_channel_and_a_fade_in_stops_at_the_ceiling()
    {
        var m = new Mixer();
        var c = m.Get(m.Play2D(Tone("f"), 0.5f, 0.5f, ChannelFlag.Looping))!;
        Mixer.Fade(c, -c.Volume);
        for (int i = 0; i < 29; i++) m.Update(1 / 30f);
        Assert.True(c.Active);
        m.Update(2 / 30f);
        Assert.False(c.Active);
    }

    [Fact]
    public void A_finished_sound_is_freed_on_the_next_frame_and_a_release_at_end_sound_is_let_go()
    {
        var m = new Mixer();
        var tone = Tone("short", ms: 10);
        int id = m.Play2D(tone, 1f, 0.5f, ChannelFlag.ReleaseAtEnd);
        var out_ = new short[2 * 2205];
        m.Render(out_, 2205);
        Assert.True(m.Get(id)!.Done);
        m.Update(1 / 30f);
        Assert.Null(m.Get(id));
        Assert.Equal(0, tone.PlayCount);
    }

    [Fact]
    public void Rendering_a_centred_constant_gives_the_2D_laws_level_on_both_sides()
    {
        var m = new Mixer();
        m.Play2D(Tone("c", level: 16000), 1f, 0.5f, ChannelFlag.Absolute);
        var out_ = new short[200];
        m.Render(out_, 100);
        Assert.Equal((short)(16000 * 2032 >> 11), out_[0]);
        Assert.Equal(out_[0], out_[1]);
    }

    [Fact]
    public void A_44100_recording_plays_at_its_own_rate_and_lasts_its_own_length()
    {
        var m = new Mixer();
        int id = m.Play2D(Tone("hi", rate: 44100, ms: 100), 1f, 0.5f, 0);
        Assert.Equal(44100, m.Get(id)!.MilesRate);                 // the recording's own rate, not 22,050
        var out_ = new short[2 * 2205];
        m.Render(out_, 2204);
        Assert.False(m.Get(id)!.Done);
        m.Render(out_, 2);
        Assert.True(m.Get(id)!.Done);                               // 100 ms is 2,205 output frames
    }

    [Fact]
    public void A_stream_plays_through_its_refills_without_a_break()
    {
        var m = new Mixer();
        var pcm = Tone("s", ms: 2000, sine: true).Pcm!;
        var stream = new Sound
        {
            Name = "s.mp3", Path = "s", Stream = true, LengthMs = 2000, Channels = 1, SourceRate = 22050,
            Open = () => new MemoryPcm(pcm, 22050, 1),
        };
        int id = m.Play2D(stream, 1f, 0.5f, ChannelFlag.Absolute);
        Assert.Equal(ChannelKind.Stream, m.Get(id)!.Kind);
        var all = new List<short>();
        var buf = new short[2 * 735];
        for (int f = 0; f < 50; f++) { m.Render(buf, 735); for (int i = 0; i < buf.Length; i += 2) all.Add(buf[i]); }
        // The same sine, sample for sample, at the law's gain.
        for (int i = 0; i < all.Count; i++) Assert.Equal((short)(pcm[i] * 2032 >> 11), all[i]);
    }

    /// <summary>A source that answers like <see cref="MemoryPcm"/> and keeps the largest read asked of it.</summary>
    sealed class CountingPcm(short[] pcm, int rate, int channels) : IPcmSource
    {
        readonly MemoryPcm inner = new(pcm, rate, channels);
        public int Largest { get; private set; }
        public int Rate => inner.Rate;
        public int Channels => inner.Channels;
        public int Read(Span<short> into) { Largest = Math.Max(Largest, into.Length); return inner.Read(into); }
        public void Rewind() => inner.Rewind();
        public void Dispose() => inner.Dispose();
    }

    [Fact]
    public void A_stream_after_a_long_frame_still_decodes_in_small_blocks_and_plays_the_same()
    {
        // In play, after a 568 ms frame, the music stopped the game for ~30 ms every 1.6 s.
        var pcm = Tone("s", ms: 4000, sine: true).Pcm!;
        CountingPcm? source = null;
        var stream = new Sound
        {
            Name = "s.mp3", Path = "s", Stream = true, LengthMs = 4000, Channels = 1, SourceRate = 22050,
            Open = () => source = new CountingPcm(pcm, 22050, 1),
        };
        var m = new Mixer();
        int id = m.Play2D(stream, 1f, 0.5f, ChannelFlag.Absolute);
        var all = new List<short>();
        void Mix(int frames) { var buf = new short[2 * frames]; m.Render(buf, frames); for (int i = 0; i < buf.Length; i += 2) all.Add(buf[i]); }
        Mix(735);
        Mix(18_788);                                        // the long frame's 0.85 s: the buffer grows to hold it
        for (int f = 0; f < 60; f++) Mix(735);              // then a second of ordinary frames
        Assert.InRange(source!.Largest, 1, Mixer.RefillFrames);
        for (int i = 0; i < all.Count; i++) Assert.Equal((short)(pcm[i] * 2032 >> 11), all[i]);
    }

    // ------------------------------------------------------------------------------ a small install

    /// <summary>
    /// An install made up here: one archive with a foley file, an interface set and a few
    /// recordings, which is all <see cref="GameAudio"/> needs to be exercised without the game.
    /// </summary>
    internal sealed class FakeInstall : IDisposable
    {
        public string Dir { get; } = Directory.CreateTempSubdirectory("vitomir-audio").FullName;
        public GameFiles Files { get; }

        public FakeInstall()
        {
            using (var zip = ZipFile.Open(Path.Combine(Dir, "Resource.nob"), ZipArchiveMode.Create))
            {
                void Add(string name, byte[] data)
                {
                    using var s = zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
                    s.Write(data);
                }
                Add("misc/Foley/test.nag", System.Text.Encoding.Latin1.GetBytes(
                    "swing a.wav\nswing b.wav\nswing c.wav\nuse d.wav local\nvox_alert d.wav voice\n"));
                Add("misc/Foley/ui.nag", System.Text.Encoding.Latin1.GetBytes("BUTTONDEPRESS a.wav local 0.25\n"));
                foreach (var n in new[] { "a", "b", "c", "d" }) Add($"sounds/objects/{n}.wav", WavBytes(22050, 100));
            }
            Files = new GameFiles(Dir);
        }

        public void Dispose()
        {
            Files.Dispose();
            try { Directory.Delete(Dir, true); } catch (IOException) { }
        }
    }

    internal static byte[] WavBytes(int rate, int ms)
    {
        var ms_ = new MemoryStream();
        var w = new BinaryWriter(ms_);
        int bytes = rate * ms / 1000 * 2;
        w.Write("RIFF"u8); w.Write(36 + bytes); w.Write("WAVEfmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16); w.Write("data"u8); w.Write(bytes);
        w.Write(new byte[bytes]);
        return ms_.ToArray();
    }

    [Fact]
    public void A_name_is_one_sound_whatever_its_case_and_extension_but_every_stream_is_a_new_record()
    {
        using var install = new FakeInstall();
        var bank = new SoundBank(install.Files);
        var a = bank.Load("A.WAV")!;
        Assert.Same(a, bank.Load("a.mp3"));
        Assert.Same(a, bank.Load("a", stream: true));               // a hit is a hit, whatever was asked for
        Assert.Equal(3, a.LoadCount);
        Assert.Equal(100, a.LengthMs);
        var s1 = bank.Load("b.mp3", stream: true)!;
        var s2 = bank.Load("b.mp3", stream: true)!;
        Assert.True(s1.Stream);
        Assert.NotSame(s1, s2);                                     // filed as "b.mp3", looked up as "b"
        Assert.Equal("b.mp3", s1.Name);
        Assert.Null(bank.Load("nothing"));
    }

    [Fact]
    public void A_foley_pick_draws_on_rand_only_when_there_is_a_choice_and_entries_are_newest_first()
    {
        using var install = new FakeInstall();
        var audio = new GameAudio(install.Files) { Random = new Ai.Rng(7) };
        var set = audio.Foley.Get("test.nag")!;
        Assert.Equal(["c", "b", "a"], set[FoleyEvent.Swing].Select(e => e.Sound.Name));
        var control = new Ai.Rng(7);
        audio.Play(set, null, FoleyEvent.Use);                      // one entry: no draw
        audio.Play(set, null, FoleyEvent.Swing);                    // three: one draw
        control.Next();
        Assert.Equal(control.Next(), audio.Random.Next());
    }

    [Fact]
    public void An_interface_press_is_heard_for_buttons_and_lists_and_a_release_only_for_buttons()
    {
        using var install = new FakeInstall();
        var audio = new GameAudio(install.Files);
        Assert.NotEqual(0, audio.Control("button", release: false));
        Assert.Equal(0, audio.Control("button", release: true));    // ui.nag has no BUTTONRELEASE here
        Assert.NotEqual(0, audio.Control("textlistbox", release: false));
        Assert.Equal(0, audio.Control("static", release: false));
        Assert.Equal(0.25f, audio.Mixer.Channels.First(c => c.Active).Volume);
    }

    [Fact]
    public void Music_starts_under_its_levels_and_its_ceiling_is_lifted_so_a_script_can_raise_it()
    {
        using var install = new FakeInstall();
        var audio = new GameAudio(install.Files);
        audio.Configure(0.75f, 0.8f);
        audio.SetMusicLevel(50);
        Assert.True(audio.StartMusic(audio.Bank.Load("a", stream: true)));
        var c = audio.MusicChannel!;
        Assert.Equal(0.4f, c.Volume, 5);
        Assert.Equal(1f, c.Target);
        Assert.True((c.Flags & ChannelFlag.Absolute) != 0);
        audio.SetMusicLevel(100);
        Assert.Equal(0.8f, c.Volume, 5);
        audio.Duck(true);
        Assert.Equal(0.4f, c.Volume, 5);
        audio.Duck(false);
        Assert.Equal(0.8f, c.Volume, 5);
    }

    /// <summary>
    /// The ambient is started at the level already set — **0.25** before any scene has set one —
    /// and the scene's own level is applied afterwards, under that ceiling. So the first ambient
    /// of a session can never be louder than a quarter, whatever its scene asks for.
    /// </summary>
    [Fact]
    public void The_first_ambient_is_held_to_a_quarter_whatever_its_scene_asks_for()
    {
        using var install = new FakeInstall();
        var audio = new GameAudio(install.Files);
        audio.StartAmbient(audio.Bank.Load("b", stream: true));
        audio.SetAmbientLevel(60);
        Assert.Equal(0.25f, audio.AmbientChannel!.Volume);
        // And asking for it back loses one: 60 × 0.01 is stored as 0.59999996, and ×100 truncated
        // is 59 — as in 1.1, which keeps it in a single-precision float and truncates.
        Assert.Equal(59, audio.AmbientPercent);
        audio.StartAmbient(audio.Bank.Load("c", stream: true));   // the next one starts at 0.6
        Assert.Equal(0.6f, audio.AmbientChannel!.Volume, 5);
    }

    [Fact]
    public void Coming_out_of_silence_restarts_music_whose_channel_has_gone()
    {
        using var install = new FakeInstall();
        var audio = new GameAudio(install.Files);
        audio.StartMusic(audio.Bank.Load("a", stream: true));
        audio.Mixer.StopAll();                                      // a level change
        Assert.Null(audio.MusicChannel);
        audio.Silence(false);
        Assert.NotNull(audio.MusicChannel);
    }
}
