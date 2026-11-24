// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Audio;
using Vitomir.Engine.Scene;
using Vitomir.Engine.Video;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A material film's sound** (*"Play the video volume but let's have it obey the
/// sound slider volume level"*). 1.1 opens a material film with its sound on through the
/// Miles sound system Bink was given before every story film, so its own track plays, flat;
/// Vitomir plays it as the story films' is played, a streaming mixer channel at full level that the
/// effects volume — the Options' Sound slider — scales. A test run has no
/// speakers, so these render the mixer into a buffer and measure what comes out: the film's sound is
/// in it from the start, as loud as the slider says, and gone when the film is closed; a loop starts
/// it again. A fake film stands in for FFmpeg, a steady tone for its track.
/// </summary>
public class MaterialFilmSoundTests
{
    const short Level = 12000;

    /// <summary>A film of <paramref name="frames"/> frames at 10 a second whose track is a steady tone as long as the film.</summary>
    sealed class ToneFilm(int frames, bool withSound = true) : IFilmPicture
    {
        byte[] pixels = new byte[4 * 2 * 4];
        public int FrameIndex { get; private set; } = -1;
        public ReadOnlySpan<byte> Pixels => pixels;
        public FilmInfo Info => new(4, 2, 10, frames / 10.0, withSound);
        public bool Advance(int ms)
        {
            if (ms / 100 >= frames) return false;
            FrameIndex = ms / 100;
            return true;
        }
        public Sound? Sound()
        {
            if (!withSound) return null;
            var pcm = Enumerable.Repeat(Level, frames * 100 * Miles.OutputRate / 1000 * 2).ToArray();
            return new Sound
            {
                Name = "tone", Path = "tone.bik", Stream = true, Channels = 2, Bits = 16,
                Rate = Miles.OutputRate, SourceRate = Miles.OutputRate, LengthMs = frames * 100,
                Open = () => new MemoryPcm(pcm, Miles.OutputRate, 2),
            };
        }
        public void Dispose() { }
    }

    static (MaterialCache Cache, LoadedMaterial Material) Wall()
    {
        var cache = new MaterialCache(null!);
        var m = new LoadedMaterial
        {
            Name = "wall", Nam = NamMaterial.Parse("texture wall.tga"),
            Images = [new TgaImage { Width = 4, Height = 4, Rgba = new byte[4 * 4 * 4] }],
            Frames = [0],
        };
        cache.Items.Add(m);
        return (cache, m);
    }

    /// <summary>The loudest sample in the next <paramref name="ms"/> of the mix.</summary>
    static int Peak(Mixer mixer, int ms = 50)
    {
        int frames = Miles.OutputRate * ms / 1000;
        var buffer = new short[frames * 2];
        mixer.Render(buffer, frames);
        return buffer.Max(x => Math.Abs((int)x));
    }

    [Fact]
    public void The_film_s_sound_plays_flat_from_its_start_as_loud_as_the_sound_slider_says()
    {
        var (cache, wall) = Wall();
        var mixer = new Mixer();
        using var films = new MaterialFilms();
        Assert.Equal(0, films.Start(wall, () => new ToneFilm(30), false, false, true, cache, mixer));
        var channel = mixer.Get(films.SoundChannel(0))!;
        Assert.NotNull(channel);
        // As a story film's (StoryFilm: Play2D(track, 1, 0.5, 0)): a stream, centred, full, not absolute.
        Assert.Equal(ChannelKind.Stream, channel.Kind);
        Assert.Equal(1f, channel.Volume);
        Assert.Equal(0.5f, channel.Pan);
        Assert.Equal(0u, channel.Flags & ChannelFlag.Absolute);

        // The slider at its default, 0.75: Miles is handed 95 of 127, and the mix carries it.
        Assert.Equal(Miles.Volume(1f, 0.75f, false), channel.MilesVolume);
        int loud = Peak(mixer);
        Assert.True(loud > 0, "the film's sound is in the mix");

        // Down to a quarter (every channel is told again): a third as loud as before, as Miles' gains say.
        mixer.SetEffectsVolume(0.25f);
        mixer.Update(0.01f);
        Assert.Equal(Miles.Volume(1f, 0.25f, false), channel.MilesVolume);
        int quiet = Peak(mixer);
        int pan = Miles.Pan(0.5f);
        double expected = (double)Miles.Gains(Miles.Volume(1f, 0.25f, false), pan).Left / Miles.Gains(Miles.Volume(1f, 0.75f, false), pan).Left;
        Assert.InRange((double)quiet / loud, expected * 0.97, expected * 1.03);

        // And off: nothing.
        mixer.SetEffectsVolume(0f);
        mixer.Update(0.01f);
        Assert.Equal(0, Peak(mixer));
    }

    [Fact]
    public void The_sound_stops_with_the_film_and_a_loop_starts_it_again()
    {
        var (cache, wall) = Wall();
        var mixer = new Mixer();
        using var films = new MaterialFilms();

        // Played out without bLoop: the slot is freed and the sound with it.
        films.Start(wall, () => new ToneFilm(3), false, false, true, cache, mixer);
        int once = films.SoundChannel(0);
        films.Step(100);
        Assert.NotNull(mixer.Get(once));
        films.Step(300);
        Assert.False(films.InUse(0));
        Assert.Null(mixer.Get(once));
        Assert.Equal(0, Peak(mixer));

        // Looping: the film opened again, and its sound from the start in a channel of its own.
        films.Start(wall, () => new ToneFilm(3), false, true, true, cache, mixer);
        int first = films.SoundChannel(0);
        films.Step(350);
        int second = films.SoundChannel(0);
        Assert.True(films.InUse(0));
        Assert.NotEqual(0, second);
        Assert.NotEqual(first, second);
        Assert.Null(mixer.Get(first));
        Assert.True(Peak(mixer) > 0);

        // Closed and closed all: silent.
        films.Close(0);
        Assert.Null(mixer.Get(second));
        films.Start(wall, () => new ToneFilm(30), false, true, true, cache, mixer);
        films.Start(wall, () => new ToneFilm(30), false, true, true, cache, mixer);
        Assert.True(Peak(mixer) > 0);
        films.Dispose();
        Assert.Equal(0, Peak(mixer));
    }

    [Fact]
    public void A_film_with_no_track_or_no_mixer_plays_no_sound()
    {
        var (cache, wall) = Wall();
        var mixer = new Mixer();
        using var films = new MaterialFilms();
        Assert.Equal(0, films.Start(wall, () => new ToneFilm(30, withSound: false), false, false, true, cache, mixer));
        Assert.Equal(0, films.SoundChannel(0));
        Assert.Equal(1, films.Start(wall, () => new ToneFilm(30), false, false, true, cache));
        Assert.Equal(0, films.SoundChannel(1));
        Assert.Equal(0, Peak(mixer));
    }
}
