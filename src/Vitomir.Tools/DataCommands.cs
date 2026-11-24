// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Part of vitomir-tools.
using Vitomir.Engine.Vfs;

namespace Vitomir;

/// <summary>
/// `--install-data` and `--verify-data` (ours): making Vitomir's own game folder from
/// the 1.1 install, and checking it.
/// </summary>
public static class DataCommands
{
    /// <summary>
    /// `vitomir --install-data [<1.1 folder>] [--install-to <folder>]`: the install named, or the one
    /// Vitomir finds (VTMR_GAME_DIR, C:\Games\VTMR), unpacked and laid out into `Vitomir\game` (or the
    /// folder given). Run again, it writes only what changed and checks every file again.
    /// </summary>
    public static int Install(string? from, string? to)
    {
        string? source = GameFiles.LocateInstall(from);
        if (source is null)
        {
            Console.Error.WriteLine(from is null
                ? "install-data: no VTMR install found — name its folder: vitomir --install-data <folder holding Resource.nob>"
                : $"install-data: {from} is not a VTMR install (it has no Resource.nob, or it is a game folder of Vitomir's own)");
            return 2;
        }
        string target = to ?? GameData.DefaultGame;
        GameData.Report report;
        try { report = GameData.Make(source, target, Console.WriteLine); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Console.Error.WriteLine($"install-data: stopped: {e.Message}");
            Console.Error.WriteLine("install-data: nothing in the install was touched; run the same command again to carry on");
            return 1;
        }
        foreach (var p in report.Problems) Console.Error.WriteLine($"install-data: {p}");
        if (!report.Ok) return 1;
        var m = report.Manifest;
        Console.WriteLine($"install-data: {m.Files.Count} files, {m.Bytes / (1024.0 * 1024.0):0} MB, in {target} — " +
                          $"{report.Written} written, {report.Kept} already there, {report.SetAside} set aside; every file checked against the install");
        Console.WriteLine($"install-data: unpacked {string.Join(", ", m.Archives)}; left out {string.Join(", ", GameData.LeftOutFolders)} " +
                          $"and the install's programs and installer files ({m.LeftOut.Count} files)");
        if (to is not null && !Path.GetFullPath(to).Equals(Path.GetFullPath(GameData.DefaultGame), StringComparison.OrdinalIgnoreCase))
            Console.WriteLine($"install-data: run Vitomir with --game \"{target}\" to use it");
        return 0;
    }

    /// <summary>`vitomir --verify-data [--install-to <folder>]`: every file of the game folder against its manifest's hash.</summary>
    public static int Verify(string gameDir)
    {
        if (GameData.Check(gameDir, hashes: true) is not { } c)
        {
            Console.Error.WriteLine($"verify-data: {gameDir} is not a finished game folder (it has no {GameData.ManifestName})");
            return 2;
        }
        foreach (var f in c.Missing) Console.WriteLine($"  missing  {f}");
        foreach (var f in c.Changed) Console.WriteLine($"  changed  {f}");
        foreach (var f in c.Extra) Console.WriteLine($"  added    {f}");
        Console.WriteLine($"verify-data: {c.Manifest.Files.Count} files, made {c.Manifest.MadeUtc:yyyy-MM-dd HH:mm} UTC from {c.Manifest.Source} by {c.Manifest.MadeBy}: " +
                          (c.Whole ? "every one as it was made" : $"{c.Missing.Count} missing, {c.Changed.Count} changed") +
                          (c.Extra.Count > 0 ? $"; {c.Extra.Count} not in the manifest (not read — mods go in a --data folder)" : ""));
        return c.Whole ? 0 : 1;
    }

    /// <summary>
    /// `vitomir-tools --make-setup-manifest [<known-good install>] [--lists <sha256sum list> <sizes list>]... [--same-as <sha> <sizes>]...
    /// --out <file> [--name vtmr-1.1-data] [--version 1] [--made <text>]`: the setup's manifest, the files of 1.1
    /// that Vitomir reads by size and SHA-256 (`Vitomir.Platform.Setup.DataManifest`), from an install hashed here or from a survey's
    /// lists (a file taken from the first list that has it); each `--same-as` pair must agree with its required part, file for file (the
    /// other store's copy). A new version for any change of 1.1's data: the file is never edited.
    /// </summary>
    public static int MakeSetupManifest(string[] args)
    {
        static string? After(string[] a, int i) => i + 1 < a.Length && !a[i + 1].StartsWith("--", StringComparison.Ordinal) ? a[i + 1] : null;
        string? install = null, outFile = null, name = "vtmr-1.1-data", made = null;
        int version = 1;
        var lists = new List<(string Sha, string Sizes)>();
        var same = new List<(string Sha, string Sizes)>();
        for (int i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--make-setup-manifest": install = After(args, i); break;
                case "--out": outFile = After(args, i); break;
                case "--name": name = After(args, i) ?? name; break;
                case "--made": made = After(args, i); break;
                case "--version" when After(args, i) is { } v && int.TryParse(v, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n) && n > 0: version = n; break;
                case "--lists" or "--same-as" when i + 2 < args.Length:
                    (args[i] == "--lists" ? lists : same).Add((args[i + 1], args[i + 2]));
                    i += 2;
                    break;
            }
        if (outFile is null || (install is null) == (lists.Count == 0))
        {
            Console.Error.WriteLine("make-setup-manifest: name a known-good install or --lists <sha256sum list> <sizes list> (one or the other), and --out <file>");
            return 2;
        }
        Vitomir.Platform.Setup.DataManifest manifest;
        try
        {
            if (install is not null)
            {
                if (!Directory.Exists(install)) { Console.Error.WriteLine($"make-setup-manifest: no folder {install}"); return 2; }
                Console.WriteLine($"make-setup-manifest: hashing what Vitomir reads of {install}");
                manifest = Vitomir.Platform.Setup.DataManifest.Make(install, name, version, made ?? $"hashed from {install} on {DateTime.UtcNow:yyyy-MM-dd}", Console.WriteLine);
            }
            else
            {
                var files = new List<Vitomir.Platform.Setup.ManifestFile>();
                var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (sha, sizes) in lists)
                    foreach (var f in Vitomir.Platform.Setup.DataManifest.FromLists(sha, sizes, name, version, "").Files)
                        if (taken.Add(f.Path)) files.Add(f);
                var first = Vitomir.Platform.Setup.DataManifest.FromLists(lists[0].Sha, lists[0].Sizes, name, version, "");
                manifest = new Vitomir.Platform.Setup.DataManifest
                {
                    Name = name, Version = version, Made = made ?? "from the lists " + string.Join(", ", lists.Select(l => Path.GetFileName(l.Sha))),
                    Parts = first.Parts, OverrideFolders = first.OverrideFolders, Files = files,
                };
            }
            foreach (var (sha, sizes) in same)
            {
                var other = Vitomir.Platform.Setup.DataManifest.FromLists(sha, sizes, name, version, "")
                    .Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
                var differ = manifest.Of(Vitomir.Platform.Setup.DataManifest.Game)
                    .Where(f => !other.TryGetValue(f.Path, out var o) || o.Size != f.Size || o.Sha256 != f.Sha256).Select(f => f.Path).ToList();
                var extra = other.Values.Where(o => o.Part == Vitomir.Platform.Setup.DataManifest.Game && manifest.Files.All(f => !f.Path.Equals(o.Path, StringComparison.OrdinalIgnoreCase))).Select(o => o.Path).ToList();
                if (differ.Count + extra.Count > 0)
                {
                    foreach (var d in differ.Take(20)) Console.Error.WriteLine($"make-setup-manifest: {Path.GetFileName(sha)}: not the same: {d}");
                    foreach (var d in extra.Take(20)) Console.Error.WriteLine($"make-setup-manifest: {Path.GetFileName(sha)}: not in the manifest: {d}");
                    Console.Error.WriteLine($"make-setup-manifest: {Path.GetFileName(sha)} differs in {differ.Count + extra.Count} files of the required part: no manifest written");
                    return 1;
                }
                Console.WriteLine($"make-setup-manifest: {Path.GetFileName(sha)}: its required part is the same, file for file ({other.Values.Count(o => o.Part == Vitomir.Platform.Setup.DataManifest.Game)} files)");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Console.Error.WriteLine($"make-setup-manifest: {e.Message}");
            return 1;
        }
        string dir = Path.GetDirectoryName(Path.GetFullPath(outFile))!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(outFile, manifest.Text());
        foreach (var part in manifest.Parts)
            Console.WriteLine($"make-setup-manifest: {part.Name} ({(part.Required ? "required" : "optional")}): {manifest.Of(part.Name).Count()} files, " +
                              $"{manifest.Bytes(part.Name):N0} bytes — {part.What}");
        Console.WriteLine($"make-setup-manifest: {manifest} written to {outFile}");
        return 0;
    }
}

