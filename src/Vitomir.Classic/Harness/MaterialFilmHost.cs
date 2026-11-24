// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The host's half of a film on a material.
using Vitomir.Engine.Vfs;
using Vitomir.Engine.Video;
using Vitomir.Scripting;

namespace Vitomir;

static class MaterialFilmHost
{
    /// <summary>
    /// What `CodexScenes.OpenMaterialFilm` is: the player's FFmpeg, looked for the first time a script
    /// asks (a mod's call; no shipped script makes one), and the film found as a story film is found
    /// (<see cref="Films.Find(GameFiles, string)"/>). 1.1 hands the script's name to <c>BinkOpen</c>
    /// as it is, so it is found from the game's folder; ours looks in <c>Video\</c> first,
    /// as for every film. Null — no FFmpeg, <paramref name="off"/>, no file — is a film that will not
    /// open, and the call does nothing.
    /// </summary>
    public static Func<string, IFilmPicture?> Opener(GameFiles files, bool off, string? ffmpegArg)
    {
        FFmpegTool? tool = null;
        bool looked = off;
        return name =>
        {
            if (!looked) { looked = true; tool = FFmpegTool.Find(ffmpegArg); }
            if (tool is null) return null;
            if (Films.Find(files, name) is not { } path)
            {
                Console.WriteLine($"video: no {name} in the install for a material");
                return null;
            }
            return VideoClip.Open(tool, path);
        };
    }

    /// <summary>
    /// `--material-film &lt;material&gt;,&lt;film.bik&gt;[,loop][,nohw]`: a film played onto a material as a
    /// mod's <c>MaterialPlayback(material, film, false, loop, !nohw)</c> would (a harness switch; no
    /// shipped script calls it).
    /// </summary>
    public static void Play(CodexScenes scenes, string spec)
    {
        var parts = spec.Split(',');
        if (parts.Length < 2) { Console.WriteLine("--material-film wants <material>,<film.bik>"); return; }
        bool loop = parts.Contains("loop"), hw = !parts.Contains("nohw");
        int slot = scenes.MaterialPlayback(parts[0], parts[1], false, loop, hw);
        Console.WriteLine(slot < 0
            ? $"material film: {parts[1]} on {parts[0]} did not start (no such material, no film, or four playing)"
            : $"material film: {parts[1]} on {parts[0]} in slot {slot}{(loop ? ", looping" : "")}{(hw ? "" : ", card not updated")}"
              + (scenes.MaterialFilms.SoundChannel(slot) is var c and not 0 ? $", its sound in channel {c & 0xff}" : ", no sound"));
    }
}
