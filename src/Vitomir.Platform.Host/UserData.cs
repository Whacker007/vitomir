// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Setup;

namespace Vitomir.Platform.Host;

/// <summary>
/// Where the platform's program keeps the player's things — settings, bindings, logs, recordings,
/// screenshots. The old program's own folder had the usual place's name (<c>%LOCALAPPDATA%\Vitomir</c>): one there without a user
/// folder's mark is never taken for the user folder (<see cref="UserFolderChoice.OlderVitomir"/>). The folder is <see cref="UserFolderChoice"/>'s: <c>VITOMIR_USERDATA</c> (tests use a temporary one so they
/// never touch the player's), else <c>vitomir.ini</c>'s <c>[folders] user</c> beside the launcher, else
/// <c>%LOCALAPPDATA%\Vitomir</c>. The program, the launcher and the setup's steps read it through here; a start checks
/// the choice first (<see cref="Readiness"/>), so a bad ini refuses the start rather than falling back.
/// </summary>
public static class UserData
{
    public const string Variable = UserFolderPlace.VariableName, FolderName = UserFolderPlace.UsualName;

    /// <summary>The choice as it stands now (the variable, the ini and the usual place read again: the launcher may have written the ini).</summary>
    public static UserFolderChoice Choice => UserFolderChoice.ThisProgram();

    /// <summary>The user folder (with a bad ini, the usual place's path, which a start refuses to use: <see cref="Choice"/>'s problem).</summary>
    public static string Folder => Choice.Folder;

    public static string SettingsPath => Path.Combine(Folder, "settings.json");
    public static string Logs => Path.Combine(Folder, "Logs");
    public static string Recordings => Path.Combine(Folder, "Recordings");
    public static string Screenshots => Path.Combine(Folder, "Screenshots");

    public static string Ensure(string folder)
    {
        Directory.CreateDirectory(folder);
        return folder;
    }
}
