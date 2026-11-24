// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Audio;
using Vitomir.Engine.Scene;
using Vitomir.Engine.Video;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Films played onto a material, `Codex.MaterialPlayback`: four
/// slots and no fifth, a film that will not open taking none, each due frame copied into the
/// texture the material was showing — its top row to the texture's bottom edge, as 1.1's buffer takes
/// it — the card told only with <c>bHWUpdate</c>, and a film closed on its last frame unless it loops.
/// No shipped script calls it, so a fake film stands in for FFmpeg and a made-up material for a mod's.
/// </summary>
public class MaterialFilmTests
{
    /// <summary>A film of <paramref name="frames"/> frames at 10 a second, each one flat grey of its number × 10 + 10.</summary>
    sealed class FakeFilm(int frames, int width = 4, int height = 2) : IFilmPicture
    {
        byte[] pixels = [];
        public bool Disposed;
        public int FrameIndex { get; private set; } = -1;
        public ReadOnlySpan<byte> Pixels => pixels;
        public FilmInfo Info => new(width, height, 10, frames / 10.0, false);
        public bool Advance(int ms)
        {
            int want = ms / 100;
            if (Disposed || want >= frames) return false;
            if (want != FrameIndex)
            {
                FrameIndex = want;
                pixels = new byte[width * height * 4];
                for (int i = 0; i < width * height; i++)
                    pixels.AsSpan(i * 4, 4).Fill((byte)(want * 10 + 10));
                pixels[0] = 1;                                  // the film's top-left pixel marked
            }
            return true;
        }
        public Sound? Sound() => null;
        public void Dispose() => Disposed = true;
    }

    static TgaImage Picture(int width = 4, int height = 4) =>
        new() { Width = width, Height = height, Rgba = Enumerable.Repeat((byte)200, width * height * 4).ToArray() };

    static (MaterialCache Cache, LoadedMaterial Material) Wall(int images = 1, int[]? frames = null)
    {
        var cache = new MaterialCache(null!);
        var m = new LoadedMaterial
        {
            Name = "wall", Nam = NamMaterial.Parse("texture wall.tga"),
            Images = Enumerable.Range(0, images).Select(_ => (TgaImage?)Picture()).ToArray(),
            Frames = frames ?? [0],
        };
        cache.Items.Add(m);
        return (cache, m);
    }

    static byte Grey(TgaImage p, int x, int row) => p.Rgba[(row * p.Width + x) * 4 + 1];

    [Fact]
    public void A_films_frame_goes_into_the_picture_upside_down_from_its_bottom_edge_and_the_card_is_told()
    {
        var (cache, wall) = Wall();
        var films = new MaterialFilms();
        Assert.Equal(0, films.Start(wall, () => new FakeFilm(5), preload: false, loop: false, hardwareUpdate: true, cache));
        films.Step(16);
        var p = wall.Image!;
        // Film row 0 is the texture's first row, which is its bottom edge: TgaImage's last row.
        Assert.Equal(1, p.Rgba[(3 * 4 + 0) * 4]);                       // the film's top-left pixel, bottom-left here
        Assert.Equal(10, Grey(p, 1, 3));
        Assert.Equal(10, Grey(p, 1, 2));
        Assert.Equal(255, p.Rgba[(3 * 4 + 1) * 4 + 3]);                 // opaque
        // A film two rows high leaves the top two rows of the texture's own picture alone.
        Assert.Equal(200, Grey(p, 1, 0));
        Assert.Equal(200, Grey(p, 1, 1));
        Assert.Equal(new[] { (0, 0) }, cache.Repainted);
    }

    [Fact]
    public void Without_bHWUpdate_the_picture_changes_and_the_card_is_never_told()
    {
        var (cache, wall) = Wall();
        var films = new MaterialFilms();
        films.Start(wall, () => new FakeFilm(5), false, false, hardwareUpdate: false, cache);
        films.Step(16);
        Assert.Equal(10, Grey(wall.Image!, 1, 3));
        Assert.Empty(cache.Repainted);
    }

    [Fact]
    public void Four_films_play_at_once_a_fifth_finds_no_slot_and_one_that_will_not_open_takes_none()
    {
        var (cache, wall) = Wall();
        var films = new MaterialFilms();
        Assert.Equal(-1, films.Start(wall, () => null, false, false, true, cache));
        Assert.Equal(0, films.Playing);
        for (int i = 0; i < MaterialFilms.Slots; i++)
            Assert.Equal(i, films.Start(wall, () => new FakeFilm(5), false, false, true, cache));
        var fifth = new FakeFilm(5);
        Assert.Equal(-1, films.Start(wall, () => fifth, false, false, true, cache));
        Assert.Equal(4, films.Playing);
    }

    [Fact]
    public void A_film_that_does_not_loop_closes_after_its_last_frame_and_leaves_it_in_the_picture()
    {
        var (cache, wall) = Wall();
        var films = new MaterialFilms();
        var film = new FakeFilm(3);
        films.Start(wall, () => film, false, loop: false, true, cache);
        for (int i = 0; i < 20; i++) films.Step(50);                    // a second: three frames and out
        Assert.False(films.InUse(0));
        Assert.True(film.Disposed);
        Assert.Equal(30, Grey(wall.Image!, 1, 3));                      // the last frame stays
    }

    [Fact]
    public void A_looping_film_opens_again_and_starts_from_its_first_frame()
    {
        var (cache, wall) = Wall();
        var films = new MaterialFilms();
        int opened = 0;
        films.Start(wall, () => { opened++; return new FakeFilm(3); }, false, loop: true, true, cache);
        for (int i = 0; i < 7; i++) films.Step(50);                     // 350 ms: frames 0, 1, 2, then out
        Assert.True(films.InUse(0));
        Assert.Equal(2, opened);
        Assert.Equal(10, Grey(wall.Image!, 1, 3));                      // frame 0 again
    }

    [Fact]
    public void The_film_goes_on_the_texture_the_material_was_showing_when_it_was_asked()
    {
        var (cache, wall) = Wall(images: 2, frames: [0, 1]);
        wall.SetFrame(1);                                               // MaterialSetFrame points the material at frame 1
        var films = new MaterialFilms();
        films.Start(wall, () => new FakeFilm(5), false, false, true, cache);
        wall.SetFrame(0);
        films.Step(16);
        Assert.Equal(200, Grey(wall.Images[0]!, 1, 3));
        Assert.Equal(10, Grey(wall.Images[1]!, 1, 3));
        Assert.Equal(new[] { (0, 1) }, cache.Repainted);
    }

    [Fact]
    public void A_film_wider_than_its_texture_is_clipped_to_it()
    {
        var p = Picture(width: 2, height: 2);
        var frame = new byte[4 * 3 * 4];
        for (int i = 0; i < frame.Length; i++) frame[i] = (byte)(i / 4);   // pixel number
        MaterialFilms.CopyUpsideDown(frame, 4, 3, p);
        Assert.Equal(0, p.Rgba[(1 * 2 + 0) * 4]);                       // film (0, 0) at the bottom-left
        Assert.Equal(1, p.Rgba[(1 * 2 + 1) * 4]);
        Assert.Equal(4, p.Rgba[(0 * 2 + 0) * 4]);                       // film row 1 above it
    }
}
