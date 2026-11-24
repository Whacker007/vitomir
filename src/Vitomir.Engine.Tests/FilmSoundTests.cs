// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Audio;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The films' volume switch** (*"Qualified yes"*). In 1.1 no slider reaches a film's sound — Bink
/// plays it on a Miles sample of its own and the driver's master volume stays at full (its setter has
/// no caller). The QoL <c>filmvolume</c>, on by default, puts the story films and the material films under the Sound
/// slider (the mixer's effects volume); off, they play at 1.1's full volume. Both kinds of film start their
/// track through <see cref="FilmSound.Play"/>, the one place the level is decided, which these drive with a steady tone
/// rendered into a buffer.
/// </summary>
public class FilmSoundTests
{
    const short Tone = 12000;

    static string Nowhere => Path.Combine(Path.GetTempPath(), "vitomir-no-such-settings");

    static Sound Track(int ms = 3000)
    {
        var pcm = Enumerable.Repeat(Tone, ms * Miles.OutputRate / 1000 * 2).ToArray();
        return new Sound
        {
            Name = "tone", Path = "tone.bik", Stream = true, Channels = 2, Bits = 16,
            Rate = Miles.OutputRate, SourceRate = Miles.OutputRate, LengthMs = ms,
            Open = () => new MemoryPcm(pcm, Miles.OutputRate, 2),
        };
    }

    static int Peak(Mixer mixer, int ms = 50)
    {
        int frames = Miles.OutputRate * ms / 1000;
        var buffer = new short[frames * 2];
        mixer.Render(buffer, frames);
        return buffer.Max(x => Math.Abs((int)x));
    }

    [Fact]
    public void The_switch_is_on_by_default_and_named_filmvolume()
    {
        Assert.True(new Qol().FilmVolume);
        Assert.True(Qol.Known("filmvolume"));
        Assert.False(Qol.Parse(["--no-qol", "filmvolume"], Nowhere, userPath: Path.Combine(Nowhere, "user.ini")).FilmVolume);
    }

    [Fact]
    public void On_a_film_s_sound_follows_the_sound_slider()
    {
        var mixer = new Mixer();
        mixer.SetEffectsVolume(0.25f);
        var channel = mixer.Get(FilmSound.Play(mixer, Track(), new Qol { FilmVolume = true }))!;
        Assert.Equal(ChannelKind.Stream, channel.Kind);
        Assert.Equal(1f, channel.Volume);
        Assert.Equal(0.5f, channel.Pan);
        Assert.Equal(0u, channel.Flags & ChannelFlag.Absolute);
        Assert.Equal(Miles.Volume(1f, 0.25f, false), channel.MilesVolume);   // a quarter of 127
        mixer.SetEffectsVolume(0f);
        mixer.Update(0.01f);
        Assert.Equal(0, Peak(mixer));                                       // the slider down: silent
    }

    [Fact]
    public void Off_a_film_s_sound_plays_at_full_volume_whatever_the_slider_says()
    {
        var mixer = new Mixer();
        mixer.SetEffectsVolume(0.25f);
        var channel = mixer.Get(FilmSound.Play(mixer, Track(), new Qol { FilmVolume = false }))!;
        Assert.NotEqual(0u, channel.Flags & ChannelFlag.Absolute);
        Assert.Equal(127, channel.MilesVolume);                             // full, as Bink's own sample
        int full = Peak(mixer);
        mixer.SetEffectsVolume(0f);
        mixer.Update(0.01f);
        Assert.Equal(127, channel.MilesVolume);
        Assert.Equal(full, Peak(mixer));                                    // the slider down: as loud as before
    }

    [Fact]
    public void No_track_is_no_channel()
    {
        var mixer = new Mixer();
        Assert.Equal(0, FilmSound.Play(mixer, null, new Qol()));
    }
}
