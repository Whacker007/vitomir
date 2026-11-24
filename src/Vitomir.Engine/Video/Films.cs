// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The game's films, and when they play.
using Vitomir.Engine.Vfs;

namespace Vitomir.Engine.Video;

/// <summary>
/// Finding a film in the player's install. The films are **not** in the archives — they sit loose
/// in `Video\` beside the game's program, which is why nothing in `GameFiles` knows about them.
///
/// 1.1 asks for them by bare name (`logo.bik`, `trailer.bik`, `intro.bik` are the three names it
/// uses) and the folder it prefixes is not known, so this looks in `Video\` first and
/// then the install root, and does it **without regard to case** — the shipped folder mixes
/// `Sunset.bik` with `NYC.BIK`, and on Linux that difference is real.
/// </summary>
public static class Films
{
    /// <summary>The folder the films live in, relative to the install.</summary>
    public const string Folder = "Video";

    /// <summary>The two 1.1 plays before the title screen, in this order (the main sequence's phase 0).</summary>
    public static readonly string[] Startup = ["logo.bik", "trailer.bik"];

    /// <summary>The one a new game opens with, before Prague's first location.</summary>
    public const string NewGame = "intro.bik";

    /// <summary>Whether the opening films (<see cref="Startup"/>) are left out: the QoL `skipopening`.</summary>
    public static bool OpeningSkipped(World.Qol qol) => qol.SkipOpening;

    /// <summary>
    /// Whether a story film asked for now ends at once (the QoL `skipstoryfilms`): never in a network game, the host's or a
    /// player's, whose films are as they always were.
    /// </summary>
    public static bool StorySkipped(World.Qol qol, bool networked) => qol.SkipStoryFilms && !networked;

    /// <summary>
    /// The full path of a named film, or null when the install has not got it. A missing film is an
    /// ordinary thing — 1.1 skips one it cannot open too — so nobody throws over it. A player module's
    /// film of that name comes first; a module's film that is no Bink film (empty, or a blanked one's few bytes of text: a pack that
    /// takes the film out) answers null, so the film is skipped. Then the player's improved version of the film, when they chose one
    /// on the launcher's Films page and it is made (<see cref="global::Vitomir.Platform.Films.FilmVersions.Playing"/>); else 1.1's own.
    /// </summary>
    public static string? Find(GameFiles files, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        // A player module's film comes first (the top module's); one that is not a Bink film takes the film out, and it is skipped.
        string bare = Path.GetFileName(name.Replace('\\', '/'));
        if (!Path.HasExtension(bare)) bare += ".bik";
        if ((files.ModuleFile(Folder + "/" + bare) ?? files.ModuleFile(bare)) is { } modules)
            return global::Vitomir.Platform.PlayerModules.PackFiles.IsBinkFilm(modules) ? modules : null;
        // The player's improved version of 1.1's film, when they chose one and it is made (ours): each machine's own, never compared.
        if (global::Vitomir.Platform.Films.FilmVersions.Active?.Playing(bare) is { } better) return better;
        if (!files.IsLayout) return Find(files.Root, name);
        name = bare;
        return files.FullPathOf(Folder + "/" + name) ?? files.FullPathOf(name);   // Vitomir's game folder: video\
    }

    /// <summary>As <see cref="Find(GameFiles, string)"/>, against an install folder.</summary>
    public static string? Find(string root, string name)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(name)) return null;
        name = Path.GetFileName(name.Replace('\\', '/'));           // never let a name climb out
        if (!Path.HasExtension(name)) name += ".bik";
        foreach (var folder in new[] { Path.Combine(root, Folder), root })
        {
            if (!Directory.Exists(folder)) continue;
            string flat = Path.Combine(folder, name);
            if (File.Exists(flat)) return flat;
            // NYC.BIK next to Sunset.bik: the install's own spelling is not to be relied on.
            foreach (var candidate in Directory.EnumerateFiles(folder))
                if (string.Equals(Path.GetFileName(candidate), name, StringComparison.OrdinalIgnoreCase))
                    return candidate;
        }
        return null;
    }
}
