// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Where a host other than vitomir-tools keeps the game's things (the module's folder, settings
// and switches).
namespace Vitomir.Engine.Diagnostics;

/// <summary>
/// **The folders a host gives the game for its run**: the player's own folder (settings, saves, the multiplayer
/// characters, logs, screenshots), the folder its installed <c>vitomir.ini</c> layer is read from, and the folder for what
/// can be made again (the scripts' compiled cache). <c>vitomir-tools</c> gives none, and everything stays where it always was
/// (<see cref="Diag.UserDataFolder"/>: <c>%LOCALAPPDATA%\Vitomir</c> or <c>VITOMIR_USERDATA</c>; the installed layer beside
/// the program; the cache in the Vitomir home). The Classic module under the platform's program gives the module's own
/// (its user folder, its own read-only folder, its cache folder: the platform's <c>LoopContext</c>), once, before the
/// controller's first run; the engine's every reader of those places asks here first.
/// </summary>
public static class HostFolders
{
    /// <summary>The player's folder the host gave, or null (the build's own: <see cref="Diag.UserDataFolder"/>'s rules).</summary>
    public static string? User { get; private set; }

    /// <summary>The folder of the installed <c>vitomir.ini</c> layer the host gave, or null (beside the program).</summary>
    public static string? Installed { get; private set; }

    /// <summary>The folder of what can be made again the host gave (the scripts' cache goes in its <c>codex</c>), or null (the Vitomir home's <c>cache</c>).</summary>
    public static string? Cache { get; private set; }

    /// <summary>The host's folders, in force until <see cref="Clear"/> (each made whole: an absolute path).</summary>
    public static void Use(string user, string installed, string cache)
    {
        User = Path.GetFullPath(user);
        Installed = Path.GetFullPath(installed);
        Cache = Path.GetFullPath(cache);
    }

    /// <summary>Back to the build's own places.</summary>
    public static void Clear() => (User, Installed, Cache) = (null, null, null);
}
