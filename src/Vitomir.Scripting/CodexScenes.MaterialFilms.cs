// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// `Codex.MaterialPlayback`: a film played onto a material.
// No shipped script calls it; only a mod would.
using Vitomir.Engine.Video;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    /// <summary>1.1's four material films (<see cref="MaterialFilms"/>): the whole game's, not the place's.</summary>
    public MaterialFilms MaterialFilms { get; } = new();

    /// <summary>
    /// Opens a film by the name a script gave, for a material, or null when there is no film to be
    /// had (no FFmpeg, no such file). The host sets it; without one `MaterialPlayback` does nothing,
    /// as 1.1's does when <c>BinkOpen</c> fails.
    /// </summary>
    public Func<string, IFilmPicture?>? OpenMaterialFilm { get; set; }

    /// <summary>
    /// `Codex.MaterialPlayback(material, binkName, bPreload, bLoop, bHWUpdate)`: the two
    /// names are cut to 31 characters, the material looked up by name among those
    /// loaded — none, nothing happens — and its showing texture handed to
    /// the film player with the film. Answers the slot, or −1, which the native throws away.
    /// </summary>
    public int MaterialPlayback(string material, string film, bool preload, bool loop, bool hardwareUpdate)
    {
        material = Cut(material);
        film = Cut(film);
        if (location?.Materials is not { } cache || cache.Find(material) is not { } m) return -1;
        if (OpenMaterialFilm is not { } open) return -1;
        return MaterialFilms.Start(m, () => open(film), preload, loop, hardwareUpdate, cache, Audio?.Mixer);   // with its sound

        // strncpy(buffer, s, 0x20) and then buffer[0x1f] = 0.
        static string Cut(string s) => s.Length > 31 ? s[..31] : s;
    }

    /// <summary>The frame's film step: see <see cref="MaterialFilms.Step"/>.</summary>
    public void StepFilms(float seconds) => MaterialFilms.Step(seconds * 1000.0);

    void RegisterMaterialFilms() =>
        Engine.On("Codex.MaterialPlayback", (_, a) =>
        {
            MaterialPlayback(CodexHost.Str(a, 0) ?? "", CodexHost.Str(a, 1) ?? "",
                             CodexHost.Bool(a, 2), CodexHost.Bool(a, 3), CodexHost.Bool(a, 4));
            return null;
        });
}
