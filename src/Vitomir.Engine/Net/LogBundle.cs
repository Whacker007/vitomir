// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// **The tester's log bundle** (ours; item 6): testers play the network game from their own homes, and what
// went wrong on a tester's machine is in files nobody else can see. One command — `/bundle` on the chat line or the dedicated
// host's console, or the menu-free switch `--log-bundle` — puts them in one zip in the game's own folder for the tester
// to send: the game's logs (and the platform's, under it), the network trace of the last session, and this machine's
// build and data fingerprints.
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace Vitomir.Engine.Net;

public static class LogBundle
{
    /// <summary>
    /// **Makes the bundle**: <c>Vitomir-logs-&lt;when&gt;.zip</c> in <paramref name="into"/> (the game's own folder),
    /// holding every log in <paramref name="logFolders"/> (the game's, and the platform's when it runs under it) — the
    /// network trace among them — and <c>identity.txt</c>: the program, its build, the data's fingerprint, the system and
    /// the simulator if one is in. Answers the zip's path and how many files it holds; a file that cannot be read is
    /// left out and said so in <c>identity.txt</c>. The two newest sessions archived (<c>Archive\*.zip</c>) are taken as they are.
    /// </summary>
    public static (string Path, int Files) Make(string into, IEnumerable<string> logFolders, NetIdentity? identity, DateTime when)
    {
        Directory.CreateDirectory(into);
        string path = Path.Combine(into, $"Vitomir-logs-{when.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.zip");
        var skipped = new List<string>();
        int files = 0;
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var folder in logFolders.Where(Directory.Exists).Distinct())
            {
                string name = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar)) ?? "logs");
                // The folder's own files, and its two newest archived sessions (Archive\*.zip: a launch archives the one before it, so a
                // --log-bundle run, or a game started again, finds the session to send there).
                string archive = Path.Combine(folder, "Archive");
                var archived = Directory.Exists(archive)
                    ? Directory.EnumerateFiles(archive, "*.zip").OrderByDescending(f => File.GetLastWriteTimeUtc(f)).Take(2)
                    : [];
                foreach (var file in Directory.EnumerateFiles(folder).OrderBy(f => f, StringComparer.Ordinal).Concat(archived))
                {
                    if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(file).StartsWith("Vitomir-logs-", StringComparison.Ordinal)) continue;
                    try
                    {
                        // Shared reading: a log still being written (this session's) is read as it stands.
                        using var from = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        bool old = Path.GetDirectoryName(file) == archive;
                        var entry = zip.CreateEntry(old ? $"{name}/Archive/{Path.GetFileName(file)}" : $"{name}/{Path.GetFileName(file)}", old ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                        using var to = entry.Open();
                        from.CopyTo(to);
                        files++;
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped.Add($"{file}: {e.Message}"); }
                }
            }
            var about = new StringBuilder();
            about.AppendLine($"Vitomir's log bundle, made {when.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
            about.AppendLine($"program: {NetIdentity.ProgramName}");
            about.AppendLine($"build: {NetIdentity.ThisBuild}");
            if (identity is not null) about.AppendLine($"this machine: {identity}");
            about.AppendLine($"system: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), .NET {Environment.Version}");
            if (NetSim.Default is { } sim) about.AppendLine($"the wire simulated: {sim}");
            // Each joined player's round trip as last said this session (a bundle made in a game, `/bundle`); the logs have every line.
            var trips = RoundTrips.Last();
            if (trips.Count > 0)
            {
                about.AppendLine("round trips (the last said of each, this session; the logs have every line):");
                foreach (var t in trips) about.AppendLine("  " + t);
            }
            foreach (var s in skipped) about.AppendLine($"not read: {s}");
            var info = zip.CreateEntry("identity.txt");
            using (var w = new StreamWriter(info.Open(), new UTF8Encoding(false))) w.Write(about.ToString());
            files++;
        }
        return (path, files);
    }

    /// <summary>
    /// The logs folders of this game: its own (<see cref="Diagnostics.Diag.Folder"/>, else its user folder's
    /// <c>Logs</c>) and, under the platform's program, the platform's (the <c>Logs</c> two folders up from the module's own).
    /// </summary>
    public static IReadOnlyList<string> Folders()
    {
        var found = new List<string>();
        string own = Diagnostics.Diag.Folder?.Root ?? Path.Combine(Diagnostics.Diag.UserDataFolder, "Logs");
        found.Add(own);
        if (Diagnostics.HostFolders.User is { } module && Directory.GetParent(module)?.Parent is { } user)
            found.Add(Path.Combine(user.FullName, "Logs"));
        return found;
    }

    /// <summary>
    /// The bundle made for this game, said in one line (the chat's, the console's and <c>--log-bundle</c>'s): where it is
    /// and what is in it, or why it could not be made.
    /// </summary>
    public static string MakeHere(NetIdentity? identity)
    {
        try
        {
            var (path, files) = Make(Diagnostics.Diag.UserDataFolder, Folders(), identity, DateTime.Now);
            return $"bundle: {files} files (the logs, the network trace and this machine's build and data) in {path} - send that file";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return $"bundle: not made ({e.Message})"; }
    }
}
