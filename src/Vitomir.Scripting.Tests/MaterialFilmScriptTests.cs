// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Audio;
using Vitomir.Engine.Video;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// `Codex.MaterialPlayback` as a script calls it (and the frame's film step),
/// on Monastery 1's own wall material: the names cut to 31 characters, a material the place
/// has not loaded doing nothing, and the film painted into the wall's texture by the simulation's
/// frame and handed to the renderer. No shipped script calls it; a fake film stands in for FFmpeg.
/// </summary>
[Collection("scripts")]
public class MaterialFilmScriptTests(Scripts s)
{
    sealed class RedFilm : IFilmPicture
    {
        byte[] pixels = [];
        public int FrameIndex { get; private set; } = -1;
        public ReadOnlySpan<byte> Pixels => pixels;
        public FilmInfo Info => new(8, 8, 10, 10, false);
        public bool Advance(int ms)
        {
            if (FrameIndex < 0) pixels = Enumerable.Range(0, 8 * 8 * 4).Select(i => (byte)(i % 4 == 0 ? 250 : 5)).ToArray();
            FrameIndex = ms / 100;
            return FrameIndex < 100;
        }
        public Sound? Sound() => null;
        public void Dispose() { }
    }

    [Fact]
    public void A_scripts_film_is_painted_into_the_walls_texture_by_the_frame()
    {
        using var m = Place.Enter(s, "P1_MON1", "MON1_8_2");
        var asked = new List<string>();
        m.Scenes.OpenMaterialFilm = name => { asked.Add(name); return new RedFilm(); };
        // A material the place has not loaded: nothing opened.
        Place.Call(s, "Codex.MaterialPlayback", null, "no_such_material", "a.bik", false, false, true);
        Assert.Empty(asked);

        var wall = m.Where.Materials.Find("PMwall4_p");
        Assert.NotNull(wall);
        var picture = wall!.Image!;
        int bottomLeft = (picture.Height - 1) * picture.Width * 4;
        Assert.NotEqual(250, picture.Rgba[bottomLeft]);
        string longName = new string('f', 40) + ".bik";
        Place.Call(s, "Codex.MaterialPlayback", null, "PMwall4_p", longName, false, true, true);
        Assert.Equal(new string('f', 31), asked.Single());               // strncpy into 32 bytes
        Assert.Equal(1, m.Scenes.MaterialFilms.Playing);

        m.Run(0.2f);
        Assert.Equal(250, picture.Rgba[bottomLeft]);                      // the film's top-left, at the texture's bottom edge
        Assert.Contains((m.Where.Materials.Items.IndexOf(wall), wall.CurrentImage), m.Where.Materials.Repainted);
    }
}
