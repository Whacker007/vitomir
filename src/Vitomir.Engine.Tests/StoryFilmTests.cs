// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Audio;
using Vitomir.Engine.Video;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The film state a story film puts the game in (`StoryFilm`; the main loop's request 14,
/// What stops when it starts, that the script is told exactly once
/// when it ends or is skipped, that a film which will not open is refused so the caller can tell the
/// script at once, and where it is drawn. A fake film stands in for FFmpeg.
/// </summary>
public class StoryFilmTests
{
    sealed class FakeFilm(int frames) : IFilm
    {
        public bool Disposed;
        public FilmInfo Info => new(640, 346, 25, frames / 25.0, false);
        public bool Advance(int ms) => !Disposed && ms * 25 / 1000 < frames;
        public Sound? Sound() => null;
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void A_film_stops_every_sound_silences_the_music_and_tells_its_script_once_when_it_runs_out()
    {
        using var install = new AudioTests.FakeInstall();
        var audio = new GameAudio(install.Files);
        Assert.True(audio.StartMusic(audio.Bank.Load("a", stream: true)));
        Assert.True(audio.StartAmbient(audio.Bank.Load("a", stream: true)));
        int effect = audio.Mixer.Play2D(audio.Bank.Load("b"), 1f, 0.5f, 0);
        Assert.NotNull(audio.Mixer.Get(effect));

        var fake = new FakeFilm(50);                    // two seconds
        int ended = 0;
        var film = new StoryFilm(name => name == "embrace.bik" ? fake : null) { Audio = audio, Ended = () => ended++ };
        Assert.False(film.Start("nothing.bik"));        // will not open: the caller tells the script at once
        Assert.False(film.Playing);

        Assert.True(film.Start("embrace.bik"));
        Assert.True(film.Playing);
        Assert.True(audio.Silenced);
        Assert.Null(audio.Mixer.Get(effect));           // Every channel
        Assert.Null(audio.MusicChannel);

        film.Frame(1000, skip: false);
        Assert.True(film.Playing);
        Assert.Equal(0, ended);                         // not while it shows
        film.Frame(2500, skip: false);                  // run out (the film's last frame)
        Assert.False(film.Playing);
        Assert.True(fake.Disposed);
        Assert.Equal(1, ended);
        Assert.False(audio.Silenced);                   // the sound back on ...
        Assert.NotNull(audio.MusicChannel);             // ... which restarts the stopped music
        Assert.Null(audio.AmbientChannel);              // the ambient is not restarted, as in 1.1 (Stephansdom's
                                                        // Sunrise.bik: the flat loop gone for good)
        Assert.Equal(-1, audio.ListenerSector);         // The sector is taken afresh

        film.Frame(3000, skip: false);
        film.Close();
        Assert.Equal(1, ended);                         // once
    }

    [Fact]
    public void Escape_or_Space_ends_a_film_at_once_and_the_script_hears_it()
    {
        int ended = 0;
        var film = new StoryFilm(_ => new FakeFilm(1000)) { Ended = () => ended++ };
        Assert.True(film.Start("Sunset.bik"));
        film.Frame(100, skip: false);
        film.Frame(140, skip: true);
        Assert.False(film.Playing);
        Assert.Equal(1, ended);
    }

    [Fact]
    public void A_second_film_closes_the_first_and_tells_its_script()
    {
        var names = new List<string>();
        var film = new StoryFilm(_ => new FakeFilm(1000)) { };
        film.Ended = () => names.Add(film.Name!);
        Assert.True(film.Start("NYC.bik"));
        Assert.True(film.Start("Sunrise.bik"));         // the film's start
        Assert.Equal(["NYC.bik"], names);
        Assert.Equal("Sunrise.bik", film.Name);
    }

    [Fact]
    public void A_story_film_is_drawn_25_lines_down_and_the_logo_at_the_top()
    {
        var film = new StoryFilm(_ => new FakeFilm(10));
        film.Start("embrace.bik");
        Assert.Equal(25, film.Top);                     // `push 0x19` with the flag set
        film.Start("logo.bik", flagged: false);
        Assert.Equal(0, film.Top);
    }
}
