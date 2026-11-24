// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// One readiness check, shared by the launcher's Play page and the game started on its own: each thing a start needs, its state,
// one plain line and the page that fixes it.
using System.Globalization;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Modules;
using Vitomir.Platform.PlayerModules;

namespace Vitomir.Platform.Setup;

/// <summary>An item's state: ✓ ready, ✗ not (a start is refused), ! a risk or a lack the player may play with, … not checked yet.</summary>
public enum ReadyState { Ready, Failed, Warning, Waiting }

/// <summary>
/// One thing a start needs: its key (<c>userfolder</c>, <c>gamefiles</c>, <c>modules</c>, <c>program</c>, <c>ffmpeg</c>), its name, its
/// state, one plain line, the launcher's page that fixes it (<c>setup</c>, <c>modules</c>, <c>folders</c>, <c>choices</c> for the
/// first start's choices; empty when no page does) and whether a start needs it (FFmpeg does not).
/// </summary>
public sealed record ReadyItem(string Key, string Name, ReadyState State, string Line, string Fix, bool Required = true)
{
    /// <summary>Whether it stops a start: required and not ready (a warning does not stop one).</summary>
    public bool Stops => Required && State is ReadyState.Failed or ReadyState.Waiting;

    /// <summary>Its mark as text (the log's, the box's): <c>ok</c>, <c>NOT READY</c>, <c>warning</c>, <c>waiting</c>.</summary>
    public string Word => State switch { ReadyState.Ready => "ok", ReadyState.Failed => "NOT READY", ReadyState.Warning => "warning", _ => "waiting" };
}

/// <summary>The items in order; ready when none stops a start.</summary>
public sealed record ReadinessReport(IReadOnlyList<ReadyItem> Items)
{
    /// <summary>Whether a start may go on: no required item failed or waiting.</summary>
    public bool Ready => !Items.Any(i => i.Stops);

    /// <summary>The first item that stops a start (Play's reason, the box's line), or null.</summary>
    public ReadyItem? FirstStopping => Items.FirstOrDefault(i => i.Stops);

    /// <summary>The item of <paramref name="key"/>, or null.</summary>
    public ReadyItem? this[string key] => Items.FirstOrDefault(i => i.Key == key);
}

/// <summary>
/// The machine's places a user folder must not be in (Program Files, by the known folders' paths, as the launcher's own folder is
/// checked) and the synced folders it should not be in (OneDrive's folders as Windows names them in <c>%OneDrive%</c>,
/// <c>%OneDriveConsumer%</c>, <c>%OneDriveCommercial%</c>; and a folder of the path named as OneDrive's, Dropbox's, Google Drive's or
/// iCloud Drive's are), and whether a drive is there. A test gives its own.
/// </summary>
public sealed record FolderPlaces(IReadOnlyList<string> ProgramFiles, IReadOnlyList<string> Synced, Func<string, bool> DriveThere, Func<string, string?> WriteTest)
{
    /// <summary>This machine's.</summary>
    public static FolderPlaces ThisMachine()
    {
        static IEnumerable<string> Some(params string?[] all) => all.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!);
        var programs = Some(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                            Environment.GetEnvironmentVariable("ProgramW6432"));
        var synced = Some(Environment.GetEnvironmentVariable("OneDrive"), Environment.GetEnvironmentVariable("OneDriveConsumer"), Environment.GetEnvironmentVariable("OneDriveCommercial"));
        return new([.. programs.Distinct(StringComparer.OrdinalIgnoreCase)], [.. synced.Distinct(StringComparer.OrdinalIgnoreCase)], Directory.Exists, FolderWrite.Test);
    }

    /// <summary>The Program Files folder <paramref name="folder"/> is in, or null.</summary>
    public string? ProgramFilesOf(string folder) => ProgramFiles.FirstOrDefault(p => p.Length > 0 && UserFolderMarks.Under(folder, p));

    /// <summary>The synced folder's service <paramref name="folder"/> is in (<c>OneDrive</c>, <c>Dropbox</c>, …), or null.</summary>
    public string? SyncedBy(string folder)
    {
        if (Synced.FirstOrDefault(s => UserFolderMarks.Under(folder, s)) is { } root) return Path.GetFileName(Path.TrimEndingDirectorySeparator(root)).StartsWith("OneDrive", StringComparison.OrdinalIgnoreCase) ? "OneDrive" : Path.GetFileName(root);
        foreach (var part in Path.GetFullPath(folder).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.StartsWith("OneDrive", StringComparison.OrdinalIgnoreCase)) return "OneDrive";
            if (part.Equals("Dropbox", StringComparison.OrdinalIgnoreCase)) return "Dropbox";
            if (part.Equals("Google Drive", StringComparison.OrdinalIgnoreCase) || part.Equals("My Drive", StringComparison.OrdinalIgnoreCase)) return "Google Drive";
            if (part.Equals("iCloudDrive", StringComparison.OrdinalIgnoreCase) || part.Equals("iCloud Drive", StringComparison.OrdinalIgnoreCase)) return "iCloud Drive";
        }
        return null;
    }
}

/// <summary>Whether a folder can be written: a file of its own made, a byte written, and deleted again.</summary>
public static class FolderWrite
{
    /// <summary>Null when a test file (<c>.vitomir-write-test-&lt;guid&gt;.tmp</c>, new) could be made and deleted in <paramref name="folder"/>, else why not.</summary>
    public static string? Test(string folder)
    {
        string path = Path.Combine(folder, $".vitomir-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) f.WriteByte(0);
            File.Delete(path);
            return File.Exists(path) ? "a test file could be made there but not deleted" : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception again) when (again is IOException or UnauthorizedAccessException) { }
            return e is UnauthorizedAccessException ? "it is not allowed to" : e.Message;
        }
    }
}

/// <summary>
/// **The readiness check**, one for the launcher's Play page and the game started on its own (<c>vitomir-platform.exe</c> with no
/// command line): each item a start needs, with its state, one plain line and the page that fixes it.
/// <list type="number">
/// <item>**the user folder** (<see cref="UserFolder"/>): chosen (a first start asks: <see cref="UserFolderSetup"/>), its ini read
/// without a problem, there (a missing drive said as one), not in Program Files, writable; in a synced folder (OneDrive, …) a warning;</item>
/// <item>**the game's files imported** (<see cref="GameFiles"/>): the import's marker, its manifest this program's, every file at its
/// size (<see cref="ImportedData.Open"/>: no hashing; Verify files stays on demand), or a developer's <c>VTMR_GAME_DIR</c>;</item>
/// <item>**the modules on** (<see cref="Modules"/>): no error, warnings accepted for the combination (the player modules' checks, cached);</item>
/// <item>**Vitomir's own files** (<see cref="Program"/>): the game's program and the Classic module and its game list beside it (a start
/// without them is refused: a package unzipped in part); a warning when Vitomir sits inside the original game's own folder;</item>
/// <item>**FFmpeg** (<see cref="FFmpeg"/>): optional, a warning only (the films are skipped).</item>
/// </list>
/// Each item is pure over what it is given, so the tests set each failing and passing.
/// </summary>
public static class Readiness
{
    static string N(long n) => n.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>**The user folder**: chosen, its ini without a problem, there, not in Program Files, writable; synced a warning.</summary>
    public static ReadyItem UserFolder(UserFolderChoice choice, FolderPlaces places)
    {
        const string name = "Your user folder";
        if (choice.Problems.Count > 0)
        {
            var bad = choice.Problems[0];
            return new("userfolder", name, ReadyState.Failed, (bad.Line > 0
                ? $"{VitomirIni.FileName} line {bad.Line} is not understood (\"{bad.Text.Trim()}\")."
                : $"{VitomirIni.FileName} cannot be read ({bad.Why}).") + " Correct it, or choose your user folder again on Folders.", "folders");
        }
        if (!choice.Chosen)
            return new("userfolder", name, ReadyState.Failed, "Not chosen yet.", "choices");
        string folder = choice.Folder;
        bool there = Directory.Exists(folder);
        // A relative (portable) folder that is not there yet is made on first use, when the Vitomir folder can be written (a fresh
        // unzip with user = UserData); an absolute one that is missing was moved, or its drive is unplugged (below).
        if (!there && choice.Relative)
        {
            if (places.WriteTest(choice.Place.TopFolder) is { } cannot)
                return new("userfolder", name, ReadyState.Failed,
                    $"{folder} cannot be created: the Vitomir folder {choice.Place.TopFolder} cannot be written to ({cannot}). Choose your user folder again on Folders.", "folders");
            try
            {
                Directory.CreateDirectory(folder);
                there = true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return new("userfolder", name, ReadyState.Failed, $"{folder} cannot be created ({e.Message}). Choose your user folder again on Folders.", "folders");
            }
        }
        if (!there)
        {
            string root = Path.GetPathRoot(Path.GetFullPath(folder)) ?? "";
            if (root.Length > 0 && !places.DriveThere(root))
                return new("userfolder", name, ReadyState.Failed, $"{folder} is not there: is its drive plugged in?", "folders");
            if (choice.Source == UserFolderSource.Ini)
                return new("userfolder", name, ReadyState.Failed,
                    $"{folder} is not there ({VitomirIni.FileName} names it, line {choice.Ini.UserLine}): was it moved, renamed or deleted? Put it back, or choose the folder again on the Folders page.", "folders");
        }
        if (places.ProgramFilesOf(folder) is { } programs)
            return new("userfolder", name, ReadyState.Failed,
                $"{folder} is in Program Files ({programs}), where Vitomir cannot write. Move it on Folders.", "folders");
        if (there && places.WriteTest(folder) is { } why)
            return new("userfolder", name, ReadyState.Failed, $"{folder} cannot be written to ({why}). Move it on Folders.", "folders");
        if (places.SyncedBy(folder) is { } service)
            return new("userfolder", name, ReadyState.Warning,
                $"{folder} is in {service}: syncing can make a save fail. Move it out on Folders.", "folders");
        return new("userfolder", name, ReadyState.Ready, $"{folder}: {choice.How}{(there ? "" : " (made when it is first needed)")}.", "folders");
    }

    /// <summary>
    /// **The game's files**: imported and whole by size against this program's manifest (with <paramref name="developer"/>, a developer's
    /// manifest taken), else the install a developer's <paramref name="gameDir"/> (<c>VTMR_GAME_DIR</c>) names; waiting while the user folder
    /// is not ready (<paramref name="userFolderReady"/>).
    /// </summary>
    public static ReadyItem GameFiles(string userData, bool developer, string? gameDir, bool userFolderReady = true) =>
        userFolderReady ? GameFiles(ImportedData.Open(userData, DataManifest.Builtin, developer), gameDir)
            : new("gamefiles", "The original game's files", ReadyState.Waiting, "After your user folder.", "setup");

    /// <summary>The game's files from the imported data's state as a start finds it (<see cref="ImportedData.Open"/>) and a developer's <paramref name="gameDir"/>.</summary>
    public static ReadyItem GameFiles(ImportedState state, string? gameDir)
    {
        const string name = "The original game's files";
        if (state.Ready)
        {
            var m = state.Marker!;
            string store = m.Store is "GOG" or "Steam" ? $" from your {m.Store} copy" : "";
            return new("gamefiles", name, ReadyState.Ready, $"Imported{store}.", "setup");
        }
        if (!string.IsNullOrEmpty(gameDir) && Assets.GameInstall.Locate(gameDir) is { } dev)
            return new("gamefiles", name, ReadyState.Ready, $"{Path.GetFullPath(dev)}, VTMR_GAME_DIR's (a developer's: {(state.None ? "nothing is imported" : "the imported files are not usable")}).", "setup");
        if (state.None)
            return new("gamefiles", name, ReadyState.Failed, "Not imported yet.", "setup");
        return new("gamefiles", name, ReadyState.Failed, "Some imported files are missing or changed: on Setup, select Import again.", "setup");
    }

    /// <summary>
    /// **The modules on**, from the player modules' check of the combination (<paramref name="start"/>, cached; null when the game has no
    /// list), <paramref name="checking"/> while the launcher checks them, <paramref name="listProblem"/> when the list does not read.
    /// </summary>
    public static ReadyItem Modules(ModuleStart? start, bool checking = false, string? listProblem = null, bool ready = true)
    {
        const string name = "The enabled modules";
        if (!ready) return new("modules", name, ReadyState.Waiting, "After the original game's files.", "modules");
        if (checking) return new("modules", name, ReadyState.Waiting, "Checking the enabled modules…", "modules");
        if (listProblem is not null) return new("modules", name, ReadyState.Failed, $"The modules' list cannot be read ({SetupWords.Plain(listProblem)}).", "modules");
        if (start is null || start.Order.Count == 0 && !start.Problems.Any(p => p.Level != ProblemLevel.Note))
            return new("modules", name, ReadyState.Ready, "None on.", "modules");
        int errors = start.Errors.Count(), warnings = start.Warnings.Count(), on = start.Order.Count;
        string count = $"{N(on)} {(on == 1 ? "module" : "modules")} on";
        if (errors > 0)
            return new("modules", name, ReadyState.Failed, $"{N(errors)} {(errors == 1 ? "error" : "errors")}: see Modules.", "modules");
        if (warnings > 0 && !start.Accepted)
            return new("modules", name, ReadyState.Failed, $"{N(warnings)} {(warnings == 1 ? "warning" : "warnings")} to accept or fix.", "modules");
        if (warnings > 0) return new("modules", name, ReadyState.Ready, $"{N(on)} on; {(warnings == 1 ? "its warning" : "warnings")} accepted.", "modules");
        return new("modules", name, ReadyState.Ready, $"{count}.", "modules");
    }

    /// <summary>
    /// **Vitomir's own files**: the game's program (<paramref name="gameProgram"/>, when the launcher asks) and the Classic module (a folder of
    /// <paramref name="modulesFolder"/> whose <c>module.json</c> reads with the id <c>classic</c>) and its game list in <paramref name="gamesFolder"/>.
    /// </summary>
    public static ReadyItem Program(string modulesFolder, string gamesFolder, string? gameProgram = null)
    {
        const string name = "Vitomir's own files";
        var missing = new List<string>();
        if (gameProgram is not null && !File.Exists(gameProgram)) missing.Add(Path.GetFileName(gameProgram));
        bool classic = false;
        try
        {
            if (Directory.Exists(modulesFolder))
                foreach (var dir in Directory.EnumerateDirectories(modulesFolder))
                    if (ModuleManifest.Read(dir, []) is { Id: "classic" }) { classic = true; break; }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        if (!classic) missing.Add($"the Classic module ({Path.Combine(modulesFolder, "Classic", ModuleManifest.FileName)})");
        if (!File.Exists(Path.Combine(gamesFolder, "classic.json"))) missing.Add(Path.Combine(gamesFolder, "classic.json"));
        if (missing.Count > 0)
            return new("program", name, ReadyState.Failed, $"Vitomir's own files are not whole ({string.Join(", ", missing)} not there): unzip Vitomir's whole zip again into its folder.", "");
        if (InsideGameInstall(Path.GetDirectoryName(Path.GetFullPath(modulesFolder.TrimEnd('\\', '/')))) is { } install)
            return new("program", name, ReadyState.Warning,
                $"Vitomir is inside the original game's folder ({install}). A store (Steam, GOG) can change or delete files there when it updates, checks or uninstalls the game, taking Vitomir with it. Unzip Vitomir into a folder of its own, outside the game's.", "");
        return new("program", name, ReadyState.Ready, "All there.", "");
    }

    /// <summary>
    /// The original game's install that <paramref name="folder"/> is, or is inside (it or a folder above it has <c>Resource.nob</c>), or
    /// null: Vitomir unzipped into the game's own folder (Steam's <c>steamapps\\common</c>, GOG's, a CD install's).
    /// </summary>
    public static string? InsideGameInstall(string? folder)
    {
        for (var dir = folder is null ? null : new DirectoryInfo(folder); dir is not null; dir = dir.Parent)
        {
            try { if (GameInstall.IsInstall(dir.FullName)) return dir.FullName; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return null;
    }

    /// <summary>**FFmpeg**: optional, a warning only when it is not found (the films are skipped).</summary>
    public static ReadyItem FFmpeg(FFmpegFound? found) =>
        found is not null
            ? new("ffmpeg", "FFmpeg, for the movies", ReadyState.Ready, "Found.", "setup", Required: false)
            : new("ffmpeg", "FFmpeg, for the movies", ReadyState.Warning, "Not found: the movies will be skipped.", "setup", Required: false);

    /// <summary>
    /// **The game's own check at a start with no command line** (the launcher not run): the user folder, the game's files, the modules on
    /// (the player modules' cached check over the base game's files) and Vitomir's own files beside the program; FFmpeg is not looked for
    /// (it is optional). <paramref name="gameDir"/> is a developer's <c>VTMR_GAME_DIR</c>.
    /// </summary>
    public static ReadinessReport ForGame(UserFolderChoice choice, FolderPlaces places, string? gameDir, string modulesFolder, string gamesFolder)
    {
        var folder = UserFolder(choice, places);
        bool folderReady = !folder.Stops;
        var files = GameFiles(choice.Folder, developer: false, gameDir, folderReady);
        ReadyItem modules;
        if (folder.Stops || files.Stops) modules = Modules(null, ready: false);
        else
        {
            var store = new PlayerModuleStore(choice.Folder, PlayerModuleManifest.ClassicGame);
            if (!File.Exists(store.ListPath)) modules = Modules(null);
            else
            {
                store.List(out string? listProblem);
                var state = ImportedData.Open(choice.Folder, DataManifest.Builtin);
                string? baseFolder = state.Ready ? state.Folder : gameDir is { Length: > 0 } && Assets.GameInstall.Locate(gameDir) is { } dev ? Path.GetFullPath(dev) : null;
                using var install = baseFolder is null ? null : Assets.GameInstall.TryOpen(baseFolder);
                modules = Modules(listProblem is null ? store.Prepare(install) : null, listProblem: listProblem);
            }
        }
        return new([folder, files, modules, Program(modulesFolder, gamesFolder)]);
    }
}
