// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;

namespace Vitomir.Platform.Modules;

/// <summary>
/// A module's or the platform's version, <c>major.minor.patch</c> (ours). The major is raised for a change
/// that breaks dependants — for a content module, removing or renaming an asset id — so
/// a dependency names the least version it accepts and, by it, the major it needs (<see cref="Satisfies"/>).
/// </summary>
public readonly record struct ModuleVersion(int Major, int Minor, int Patch) : IComparable<ModuleVersion>
{
    /// <summary>Reads <c>major.minor.patch</c>: three whole numbers, nothing else (no <c>v</c>, no pre-release tag).</summary>
    public static bool TryParse(string? text, out ModuleVersion version)
    {
        version = default;
        var parts = text?.Split('.') ?? [];
        if (parts.Length != 3) return false;
        var n = new int[3];
        for (int i = 0; i < 3; i++)
        {
            var p = parts[i];
            if (p.Length == 0 || p.Length > 9 || (p.Length > 1 && p[0] == '0')) return false;
            foreach (char c in p) if (c is < '0' or > '9') return false;
            n[i] = int.Parse(p, NumberStyles.None, CultureInfo.InvariantCulture);
        }
        version = new ModuleVersion(n[0], n[1], n[2]);
        return true;
    }

    public static ModuleVersion Parse(string text) =>
        TryParse(text, out var v) ? v : throw new FormatException($"'{text}' is not a version (major.minor.patch)");

    /// <summary>
    /// Whether this version is accepted by a dependency asking for at least <paramref name="least"/>: the same
    /// major (another major is a breaking change either way) and not lower.
    /// </summary>
    public bool Satisfies(ModuleVersion least) => Major == least.Major && CompareTo(least) >= 0;

    public int CompareTo(ModuleVersion other) =>
        Major != other.Major ? Major.CompareTo(other.Major)
        : Minor != other.Minor ? Minor.CompareTo(other.Minor)
        : Patch.CompareTo(other.Patch);

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
}

/// <summary>
/// The platform's public surface as modules see it ("the platform's public surface is
/// versioned; a module states the version it was built for and the loader refuses a module built for an
/// incompatible one, naming it"). The one place the platform's API version is written.
/// </summary>
public static class PlatformApi
{
    /// <summary>
    /// The platform API's version. Raise the major when a change can break a module built for the old one (a
    /// member removed or changed in meaning), the minor when something is added, the patch for a fix.
    /// 1.1.0: a module's own frame loop added (<see cref="IModule.Loop"/>, <see cref="IGameLoop"/>); a module built
    /// for 1.0.0 runs as it did. 1.2.0: the audio output (<c>Vitomir.Platform.Audio</c>) and a loop's
    /// <see cref="LoopContext.Sound"/>. 1.3.0: the neutral input filled in (<see cref="LoopInput.Key"/>,
    /// <see cref="LoopInput.Mods"/>, <see cref="LoopInput.Buttons"/>; the window's events in <see cref="LoopInputKind"/>;
    /// <see cref="LoopMods"/>, <see cref="LoopScancode"/>, <see cref="LoopKey"/>, <see cref="LoopButton"/>). 1.4.0 (the end of
    /// ): the window's rules (<c>Vitomir.Platform.Windowing</c>, the modes,
    /// the desktop's room, the picture's bars, the kept place, <c>WindowKeys</c> and <c>IWindowSettings</c>), the window's settings
    /// kept per game (<c>GameWindowSettings</c>, <c>ReadOnlyWindowSettings</c>), and the developer switch
    /// (<see cref="DeveloperSwitch"/>: a module's loop takes the switches its manifest declares, any other only with it). 1.5.0
    /// (the rest of ): the pushed frame — a loop played in a window is run frame by
    /// frame by the program's host, handed the host's services at its start (<see cref="LoopContext.Host"/>) and each frame's time on
    /// the host's clock (<see cref="HostFrame.TimeNs"/>), and says when the host may begin the next (<see cref="LoopResult.NextFrameNs"/>,
    /// the host's wait); with it the platform renderer's host contract lost its pulls (the films' raw poll, the waits) and the
    /// windowed game's door (<c>IClassicGame</c>).
    /// </summary>
    public static readonly ModuleVersion Version = new(1, 5, 0);

    /// <summary>
    /// Why a module built for <paramref name="builtFor"/> cannot run on this platform, or null when it can: the
    /// same major, and not a newer minor than this platform (it may use what this one does not have yet).
    /// </summary>
    public static string? Refuses(ModuleVersion builtFor) =>
        builtFor.Major != Version.Major
            ? $"it was built for platform API {builtFor}, and this platform is {Version} (another major: incompatible)"
            : builtFor.Minor > Version.Minor
                ? $"it was built for platform API {builtFor}, a newer minor than this platform's {Version}"
                : null;
}
