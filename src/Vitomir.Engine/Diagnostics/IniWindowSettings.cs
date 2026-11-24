// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The window's settings in the player's own vitomir.ini: what the window code (the platform's) reads and
// writes as `[window]`, read and written here exactly as the window code did itself until then (`IniFile.Read`, `SettingsFile.Set`).
using Vitomir.Platform.Windowing;

namespace Vitomir.Engine.Diagnostics;

/// <summary>
/// **<c>[window]</c> of a <c>vitomir.ini</c>**: the file read afresh at each <see cref="Read"/> (missing or
/// unreadable, no keys), and <see cref="Write"/> one <see cref="SettingsFile.Set"/> of the keys given, which a replay never
/// writes (<see cref="SettingsFile.Replaying"/>). With no path given it is the player's own file, asked of
/// <see cref="Diag.UserSettingsPath"/> at each read and write, as the window code asked it.
/// </summary>
public sealed class IniWindowSettings(string? path = null) : IWindowSettings
{
    string Path => path ?? Diag.UserSettingsPath;

    public WindowKeys Read() => IniFile.Read(Path).Window();

    public void Write(IEnumerable<KeyValuePair<string, string>> values) => SettingsFile.Set(Path, WindowPlacement.Section, values);
}

/// <summary>An ini's <c>[window]</c> as the window's rules read it.</summary>
public static class IniWindowKeys
{
    /// <summary>The keys of <paramref name="ini"/>'s <see cref="WindowPlacement.Section"/>, without the section's name.</summary>
    public static WindowKeys Window(this IniFile ini) => new(ini.Section(WindowPlacement.Section));
}
