// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Which build of Vitomir this is (ours), and so where the player's saves, settings and logs are kept.
namespace Vitomir.Engine.Diagnostics;

/// <summary>
/// The build's identity (ours; nothing of 1.1): where the player's data — the saves, <c>vitomir.ini</c>, the
/// logs and the screenshots — is kept, <c>%LOCALAPPDATA%\Vitomir</c> (<see cref="UserFolderName"/>), and the
/// environment variable a run that must stay apart from the player's own data names its folder with instead
/// (<see cref="UserDataVariable"/>: a test, a second copy kept apart). <see cref="Diag.UserDataFolder"/> reads both.
/// </summary>
public static class BuildFlavour
{
    /// <summary>A name for a build other than the main one, after the version in the version line; empty for the main build.</summary>
    public const string Name = "";

    /// <summary>The folder under the user's local application data that holds this build's player data.</summary>
    public const string UserFolderName = "Vitomir";

    /// <summary>
    /// An environment variable naming the player-data folder outright (an absolute path). It wins over
    /// <see cref="UserFolderName"/>; a relative or empty value is ignored.
    /// </summary>
    public const string UserDataVariable = "VITOMIR_USERDATA";

    /// <summary>The folder <see cref="UserDataVariable"/> names, when it names an absolute one.</summary>
    public static string? UserDataOverride => OverrideFrom(System.Environment.GetEnvironmentVariable(UserDataVariable));

    /// <summary>The folder a value of <see cref="UserDataVariable"/> names: an absolute path, else none.</summary>
    public static string? OverrideFrom(string? value) =>
        value is { Length: > 0 } && Path.IsPathRooted(value) ? Path.GetFullPath(value) : null;
}
