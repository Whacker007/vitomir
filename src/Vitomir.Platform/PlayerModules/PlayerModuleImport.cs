// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Vitomir.Formats;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Setup;

namespace Vitomir.Platform.PlayerModules;

/// <summary>What an import of a pack is asked to do.</summary>
public sealed record ModuleImportOptions
{
    /// <summary>The id to give a bare pack (else made from its name); an authored module's id is its manifest's.</summary>
    public string? Id { get; init; }
    /// <summary>Replace a module of that id that came from another source.</summary>
    public bool Replace { get; init; }
    /// <summary>1.1's own spelling of a path (lower case in, 1.1's out), from the base game's files, when they are at hand.</summary>
    public IReadOnlyDictionary<string, string>? BaseSpelling { get; init; }
    /// <summary>The program's version line, for the record.</summary>
    public string By { get; init; } = "";
    public IProgress<SetupProgress>? Progress { get; init; }
    /// <summary>The recognised files whose bindings a bare pack's manifest gets (<see cref="RecognisedPacks.All"/>; a test's own).</summary>
    public IReadOnlyList<RecognisedFile>? Recognised { get; init; }
}

/// <summary>Why an import was refused, for a caller that answers one refusal in its own way (the launcher's Replace question).</summary>
public enum ModuleRefusal
{
    /// <summary>Not refused.</summary>
    None,
    /// <summary>Any other reason: <see cref="ModuleImportReport.Refusal"/> says it.</summary>
    Other,
    /// <summary>
    /// A module of the same id is in the user folder already, imported from another source, and <see cref="ModuleImportOptions.Replace"/>
    /// was not asked (<see cref="ModuleImportReport.There"/> is that module).
    /// </summary>
    AlreadyThere,
}

/// <summary>What an import did: the module (null when refused, with <see cref="Refusal"/>), what was left out and why, what was copied.</summary>
public sealed record ModuleImportReport(
    PlayerModule? Module, string? Refusal, IReadOnlyList<ModuleLeftOut> LeftOut, int Copied, int Kept, long Bytes, IReadOnlyList<string> Notes)
{
    public bool Accepted => Module is not null;

    /// <summary>Why it was refused (<see cref="ModuleRefusal.None"/> when it was not); <see cref="Refusal"/> has the words.</summary>
    public ModuleRefusal Reason { get; init; } = Module is null ? ModuleRefusal.Other : ModuleRefusal.None;

    /// <summary>The module of that id the user folder had before this import (a refusal's, or the one an update or a replace took over), or null.</summary>
    public PlayerModule? There { get; init; }

    /// <summary>
    /// The data files the import took out of the module's copy (by their paths under its <c>Data\</c>): an earlier import's that this pack
    /// no longer has, and any put there by hand. Its <c>Documents\</c> is made the pack's too. The pack itself is never touched.
    /// </summary>
    public IReadOnlyList<string> Removed { get; init; } = [];
}

/// <summary>
/// **The import of a pack into a player module** (<c>vitomir-platform --setup module-add &lt;path&gt;</c>): from a folder, a zip or a
/// <c>.nob</c>, its files read by their 1.1 paths (without regard to case; a pack's top folders matched to 1.1's own, <c>materials</c>,
/// <c>3d</c>, <c>UI</c>, <c>Strings</c>, <c>Video</c>, …, wherever in the pack they start: a JSGME <c>MODS\&lt;name&gt;\</c> layout or a
/// zip's wrapping folder is found), copied into the module's folder in the user data and hashed, and the manifest written — the
/// pack's own (<c>module.json</c> beside a <c>Data\</c> folder: an authored module) or, for a bare pack, one made from its name
/// (version 0.0.0, made for 1.1's models). Script code and program files are refused and listed, each with what it was for when
/// that is known; files that are neither (readmes, settings) are kept aside in <c>Documents\</c>, never read by the game.
/// <para>As the 1.1 import: each file copied to a <c>.partial</c> beside its place with its SHA-256 taken as it is read, moved in once
/// whole, read back and hashed again; a journal of the files done, so a stopped import carries on where it stopped; the record
/// (<see cref="ModuleRecord"/>) written last. Each data file of a kind Vitomir reads is decoded as it is recorded, and what does not
/// decode is kept in the record as the module's error.</para>
/// </summary>
public static class PlayerModuleImport
{
    /// <summary>The journal of the files copied so far, in a module's folder while its import runs.</summary>
    public const string JournalName = "import-journal.tsv";

    /// <summary>Imports <paramref name="source"/> into a module folder under <paramref name="store"/>.</summary>
    public static ModuleImportReport Run(string source, string store, ModuleImportOptions options, CancellationToken cancel = default)
    {
        var notes = new List<string>();
        PackSource pack;
        try { pack = PackSource.From(source); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            return Refused($"{source} cannot be read as a folder, a zip or a .nob: {e.Message}");
        }
        using (pack)
        {
            // ---- where the pack's data starts, and whether it is an authored module --------------------------------------------------
            var plan = new List<Item>();
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var leftOut = new List<ModuleLeftOut>();
            PlayerModuleManifest? authored = null;
            byte[]? authoredBytes = null;
            string root, layout = "";
            string? rootName;
            var manifests = pack.Files.Where(f => Path.GetFileName(f.Path).Equals(PlayerModuleManifest.FileName, StringComparison.OrdinalIgnoreCase)
                                                  && f.Path.Count(c => c == '/') <= 1).ToList();
            if (manifests.Count == 1 && (manifests[0].Path.IndexOf('/') is var slash and >= 0
                    ? pack.Files.All(f => f.Path.StartsWith(manifests[0].Path[..(slash + 1)], StringComparison.Ordinal))
                    : true))
            {
                root = manifests[0].Path[..(manifests[0].Path.LastIndexOf('/') + 1)];
                using (var s = pack.Open(manifests[0].Path)) { var m = new MemoryStream(); s.CopyTo(m); authoredBytes = m.ToArray(); }
                var problems = new List<string>();
                authored = PlayerModuleManifest.Parse(Encoding.UTF8.GetString(authoredBytes), PlayerModuleManifest.FileName, problems);
                if (authored is null) return Refused($"its {PlayerModuleManifest.FileName} is not a player module's: {string.Join("; ", problems)}");
                if (options.Id is { } asked && asked != authored.Id)
                    return Refused($"it is the module {authored.Id} (its {PlayerModuleManifest.FileName} says so); an authored module keeps its own id");
                rootName = authored.Id;
                string data = root + PlayerModule.DataFolderName + "/";
                foreach (var (path, size) in pack.Files)
                {
                    if (path == manifests[0].Path) continue;
                    if (path.StartsWith(data, StringComparison.OrdinalIgnoreCase)) AddData(path, path[data.Length..], size);
                    else Document(path, size, "beside the module's Data folder");
                }
            }
            else
            {
                var roots = Roots(pack.Files.Select(f => f.Path));
                if (roots.Count == 0)
                    return Refused($"no file of it is in one of 1.1's folders ({string.Join(", ", PackFiles.TopFolders.Where(t => t != "Codex"))}) "
                                   + "or a .nob at its top: nothing to import");
                if (roots.Count > 1)
                    return Refused($"it holds {roots.Count} packs ({string.Join(", ", roots.Select(r => r.TrimEnd('/')))}): add the folder of the one you want");
                root = roots[0];
                var parts = root.TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[^2].Equals("MODS", StringComparison.OrdinalIgnoreCase)) layout = "jsgme";
                rootName = parts.Length > 0 ? parts[^1] : null;
                foreach (var (path, size) in pack.Files)
                {
                    if (!path.StartsWith(root, StringComparison.Ordinal)) { Document(path, size, "outside the pack's data"); continue; }
                    AddData(path, path[root.Length..], size);
                }
            }

            void AddData(string path, string rel, long size)
            {
                var c = PackFiles.Classify(rel);
                switch (c.Class)
                {
                    case PackFileClass.Code: leftOut.Add(new() { Path = path, Class = "code", Why = c.Why }); return;
                    case PackFileClass.Program: leftOut.Add(new() { Path = path, Class = "program", Why = c.Why }); return;
                    case PackFileClass.Document: Document(path, size, c.Why); return;
                }
                string target = Spell(rel, options.BaseSpelling);
                if (!targets.Add(target))
                {
                    leftOut.Add(new() { Path = path, Class = "twice", Why = $"a second file of the path {target} (the first is kept)" });
                    return;
                }
                plan.Add(new Item(path, target, size, Data: true, c.Class == PackFileClass.Archive, c.Kind));
            }
            void Document(string path, long size, string why)
            {
                plan.Add(new Item(path, path, size, Data: false, Archive: false, 0));
                leftOut.Add(new() { Path = path, Class = "document", Why = why });
            }

            if (!plan.Any(p => p.Data)) return Refused("it has no data of 1.1's kinds to import" + (leftOut.Count > 0 ? $" ({leftOut.Count} files left out)" : ""));

            // ---- the module's id and folder ------------------------------------------------------------------------------------------
            var known = authored is null ? KnownPacks.Of(rootName ?? pack.Name) ?? KnownPacks.Of(pack.Name) : null;
            if (known is not null) notes.Add(known.Note);
            string id = authored?.Id ?? options.Id ?? known?.Id ?? IdFrom(rootName ?? pack.Name);
            if (PlayerModuleManifest.IdProblem(id) is { } idProblem) return Refused($"the id {id}: {idProblem} (give another with --id <id>)");
            string folder = Path.Combine(store, id);
            string fullSource = Path.GetFullPath(source);
            PlayerModule? had = File.Exists(Path.Combine(folder, PlayerModule.RecordName)) ? PlayerModule.Open(folder, []) : null;
            if (had is not null && !string.Equals(had.Record.Source, fullSource, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) && !options.Replace)
                return Refused($"a module {id} is there already (from {had.Record.Source}): remove it first (module-remove {id}), give this one another id (--id <id>), or --replace",
                               ModuleRefusal.AlreadyThere) with { There = had };
            Directory.CreateDirectory(folder);
            long needed = plan.Sum(p => p.Size);
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!);
                if (drive.AvailableFreeSpace < needed + (64L << 20))
                    return Refused($"not enough room: it needs {SetupWords.Size(needed)} and {drive.Name} has {SetupWords.Size(drive.AvailableFreeSpace)} free");
            }
            catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException) { }

            // ---- the copy, carried on from the journal ---------------------------------------------------------------------------------
            string journalPath = Path.Combine(folder, JournalName);
            var journal = ReadJournal(journalPath);
            File.Delete(Path.Combine(folder, PlayerModule.RecordName));               // the module is not usable until the record is written again
            int copied = 0, kept = 0;
            long done = 0;
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            using (var journalWriter = new StreamWriter(journalPath, append: true))
            {
                foreach (var item in plan)
                {
                    cancel.ThrowIfCancellationRequested();
                    string place = Place(folder, item);
                    if (journal.TryGetValue(item.Target, out var j) && j.Size == item.Size && File.Exists(place) && new FileInfo(place).Length == item.Size)
                    {
                        hashes[item.Target] = j.Sha;
                        kept++;
                    }
                    else
                    {
                        hashes[item.Target] = Copy(pack, item, place);
                        journalWriter.WriteLine($"{item.Target}\t{item.Size.ToString(CultureInfo.InvariantCulture)}\t{hashes[item.Target]}");
                        journalWriter.Flush();
                        copied++;
                    }
                    done += item.Size;
                    options.Progress?.Report(new SetupProgress("copy and hash", item.Target, done, needed));
                }
            }

            // ---- archives: kept whole, unless they carry code (then their data is taken out of them, the code refused) ----------------
            var files = new List<ModuleFileRecord>();
            var archives = new List<ModuleArchiveRecord>();
            var dataFolder = Path.Combine(folder, PlayerModule.DataFolderName);
            foreach (var item in plan.Where(p => p.Data && p.Archive))
            {
                string place = Place(folder, item);
                NobArchive nob;
                try { nob = new NobArchive(place); }
                catch (InvalidDataException e) { return Refused($"{item.Source} is not an archive of 1.1's kind (a zip): {e.Message}"); }
                var entries = new List<(string Name, PackFile Class)>();
                using (nob)
                {
                    foreach (var (name, _) in nob.Entries) entries.Add((name, PackFiles.Classify(name)));
                    var refused = entries.Where(e => e.Class.Class is PackFileClass.Code or PackFileClass.Program).ToList();
                    if (refused.Count == 0)
                    {
                        archives.Add(new() { Path = item.Target, Size = item.Size, Sha256 = hashes[item.Target] });
                        foreach (var (name, c) in entries.Where(e => e.Class.Class == PackFileClass.Data))
                        {
                            var bytes = nob.Read(name);
                            files.Add(Decode(Spell(name, options.BaseSpelling), item.Target, bytes, PlayerModule.Hash(bytes), c.Kind));
                        }
                        continue;
                    }
                    foreach (var (name, c) in refused)
                        leftOut.Add(new() { Path = $"{item.Source}/{name}", Class = c.Class == PackFileClass.Code ? "code" : "program", Why = c.Why });
                    notes.Add($"{item.Source} carries script code, so its data is taken out of it as loose files and the archive itself is not kept");
                    foreach (var (name, c) in entries.Where(e => e.Class.Class == PackFileClass.Data))
                    {
                        string rel = Spell(name, options.BaseSpelling);
                        if (!targets.Add(rel)) continue;                                // a loose file of the pack's of that path wins
                        var bytes = nob.Read(name);
                        string to = Path.Combine(dataFolder, rel.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                        File.WriteAllBytes(to, bytes);
                        files.Add(Decode(rel, null, bytes, PlayerModule.Hash(bytes), c.Kind));
                    }
                }
                File.Delete(place);
            }

            // ---- every loose data file decoded and recorded ------------------------------------------------------------------------------
            foreach (var item in plan.Where(p => p.Data && !p.Archive))
            {
                cancel.ThrowIfCancellationRequested();
                files.Add(Decode(item.Target, null, File.ReadAllBytes(Place(folder, item)), hashes[item.Target], item.Kind));
            }

            // ---- what the import did not put there goes (the module's folder is the import's own) -----------------------------------
            var wanted = new HashSet<string>(files.Where(f => f.In is null).Select(f => Path.Combine(dataFolder, f.Path.Replace('/', Path.DirectorySeparatorChar))), PathComparer);
            foreach (var a in archives) wanted.Add(Path.Combine(dataFolder, a.Path));
            foreach (var d in plan.Where(p => !p.Data)) wanted.Add(Place(folder, d));
            var removed = new List<string>();
            foreach (var sub in new[] { dataFolder, Path.Combine(folder, PlayerModule.DocumentsFolderName) })
                if (Directory.Exists(sub))
                {
                    foreach (var file in Directory.EnumerateFiles(sub, "*", SearchOption.AllDirectories).ToList())
                        if (!wanted.Contains(file))
                        {
                            File.Delete(file);
                            if (sub == dataFolder) removed.Add(Path.GetRelativePath(sub, file).Replace(Path.DirectorySeparatorChar, '/'));
                        }
                    // and the folders that held only those (ours: the copy is left as the import made it)
                    foreach (var dir in Directory.EnumerateDirectories(sub, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).ToList())
                        if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
                }

            // ---- a bare pack's recognised files: the bindings their buttons were made for -----------------------------------------------
            var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (authored is null)
                foreach (var r in options.Recognised ?? RecognisedPacks.All)
                    if (files.FirstOrDefault(f => f.Sha256.Equals(r.Sha256, StringComparison.OrdinalIgnoreCase)) is { } known1)
                    {
                        foreach (var (element, action) in r.Actions) bindings[element] = action;
                        notes.Add($"{known1.Path} is {r.Pack}'s ({r.Path}): its {string.Join(" and ", r.Actions.Select(a => $"{a.Key} button {InterfaceActions.Find(a.Value)?.Words ?? a.Value}"))}"
                                  + " (Vitomir's own actions, bound in its module.json)"
                                  + (r.Actions.Values.Any(a => InterfaceActions.Find(a)?.Rules == true) ? "; that makes it a rules module: every player of a network game needs it, and saves name it" : ""));
                    }

            // ---- the manifest and the record, last -------------------------------------------------------------------------------------
            var kinds = files.Select(f => PackKindWords.Parse(f.Kind) ?? 0).Aggregate((PackKinds)0, (a, k) => a | k);
            var manifest = authored ?? new PlayerModuleManifest
            {
                Id = id,
                Name = known?.Name ?? rootName ?? pack.Name,
                Version = KnownPacks.VersionIn(VersionNames(root, rootName, pack, fullSource)) ?? new ModuleVersion(0, 0, 0),
                Description = (known is not null ? known.Description + "; " : "") + $"imported from {Path.GetFileName(Path.TrimEndingDirectorySeparator(fullSource))}",
                Game = PlayerModuleManifest.ClassicGame,
                Kind = kinds,
                MadeFor = [AssetId.OneOne],
                Actions = bindings,
            };
            byte[] manifestBytes = authoredBytes ?? Encoding.UTF8.GetBytes(manifest.ToJson());
            File.WriteAllBytes(Path.Combine(folder, PlayerModuleManifest.FileName), manifestBytes);
            if (authored?.Kind is { } said && said != kinds)
                notes.Add($"its {PlayerModuleManifest.FileName} says it is {PackKindWords.Words(said)}; its files are {PackKindWords.Words(kinds)}, which decide");
            var record = new ModuleRecord
            {
                Id = id, Version = manifest.Version.ToString(), Source = fullSource, SourceKind = pack.Kind, Layout = layout,
                ImportedUtc = DateTime.UtcNow, ImportedBy = options.By,
                ContentHash = PlayerModule.ContentHashOf(files, archives), ManifestHash = PlayerModule.Hash(manifestBytes),
                Kinds = [.. PackKindWords.List(kinds)],
                Files = [.. files.OrderBy(f => f.In ?? "", StringComparer.Ordinal).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)],
                Archives = archives, LeftOut = leftOut,
            };
            PlayerModule.WriteRecord(folder, record);
            File.Delete(Path.Combine(folder, PlayerModule.DamagedName));      // added again: a verification's mark goes with what it found
            File.Delete(journalPath);
            var module = PlayerModule.Open(folder, notes);
            return new ModuleImportReport(module, module is null ? "the module's record cannot be read back" : null, leftOut, copied, kept, needed, notes)
            {
                There = had, Removed = [.. removed.Order(StringComparer.Ordinal)],
            };
        }

        ModuleImportReport Refused(string why, ModuleRefusal reason = ModuleRefusal.Other) => new(null, why, [], 0, 0, 0, notes) { Reason = reason };
    }

    static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// The names a bare pack's version is looked for in, nearest first: its data's own folder, the folders above it in the pack (a
    /// JSGME <c>MODS\</c> and the download's folder around it), the pack's own name, and for a folder added from inside a download
    /// (<c>…\VTMR Crimson Edition-12-1-0-1726673430\MODS\VTMR Crimson Edition</c>) the folder around its <c>MODS\</c>.
    /// </summary>
    static IEnumerable<string?> VersionNames(string? root, string? rootName, PackSource pack, string fullSource)
    {
        yield return rootName;
        var parts = (root ?? "").TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = parts.Length - 2; i >= 0; i--) yield return parts[i];
        yield return pack.Name;
        if (pack.Kind != "folder") yield break;
        var mods = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(fullSource));
        if (mods is null || !Path.GetFileName(mods).Equals("MODS", StringComparison.OrdinalIgnoreCase)) yield break;   // only a JSGME folder's
        yield return Path.GetFileName(Path.GetDirectoryName(mods));
    }

    /// <summary>A planned file: its path in the pack, its target (a 1.1 path under <c>Data\</c>, or a path under <c>Documents\</c>), its size and kind.</summary>
    sealed record Item(string Source, string Target, long Size, bool Data, bool Archive, PackKinds Kind);

    static string Place(string folder, Item item) =>
        Path.Combine(folder, item.Data ? PlayerModule.DataFolderName : PlayerModule.DocumentsFolderName, item.Target.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// **Where a pack's data starts**: the shallowest folders (the pack's top among them) holding one of 1.1's top folders or a <c>.nob</c>
    /// at their own top. One is the pack; several at one depth are several packs (a JSGME <c>MODS\</c> folder of two), which the
    /// import does not choose between.
    /// </summary>
    public static IReadOnlyList<string> Roots(IEnumerable<string> paths)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in paths)
        {
            var segments = p.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                bool top = i < segments.Length - 1 ? PackFiles.TopFolder(segments[i]) is not null
                                                   : segments[i].EndsWith(".nob", StringComparison.OrdinalIgnoreCase);
                if (!top) continue;
                found.Add(string.Concat(segments.Take(i).Select(s => s + "/")));
                break;
            }
        }
        if (found.Count == 0) return [];
        int least = found.Min(r => r.Count(c => c == '/'));
        return [.. found.Where(r => r.Count(c => c == '/') == least).Order(StringComparer.Ordinal)];
    }

    /// <summary>A 1.1 path as 1.1 spells it: its top folder always, the whole path when the base game's files have it.</summary>
    public static string Spell(string rel, IReadOnlyDictionary<string, string>? baseSpelling)
    {
        rel = rel.Replace('\\', '/').TrimStart('/');
        if (baseSpelling is not null && baseSpelling.TryGetValue(rel.ToLowerInvariant(), out var spelt)) return spelt;
        int slash = rel.IndexOf('/');
        if (slash > 0 && PackFiles.TopFolder(rel[..slash]) is { } top) return top + rel[slash..];
        return rel;
    }

    /// <summary>1.1's paths in lower case to their own spelling, from the base game's files (for <see cref="ModuleImportOptions.BaseSpelling"/>).</summary>
    public static IReadOnlyDictionary<string, string> SpellingOf(Assets.GameInstall install)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in install.List("")) map.TryAdd(p.ToLowerInvariant(), p);
        return map;
    }

    /// <summary>A module id from a pack's name: lower case, every run of other characters (<c>_</c> among them) one <c>-</c>, starting with a letter, at most 64.</summary>
    public static string IdFrom(string name)
    {
        var b = new StringBuilder();
        foreach (char c in name.ToLowerInvariant())
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') b.Append(c);
            else if (b.Length > 0 && b[^1] != '-') b.Append('-');
        string id = b.ToString().Trim('-');
        if (id.Length == 0 || id[0] is < 'a' or > 'z') id = "pack-" + id;
        if (id.Length > AssetId.MaxModuleLength) id = id[..AssetId.MaxModuleLength].TrimEnd('-');
        if (PlayerModuleManifest.Reserved.Contains(id)) id = "pack-" + id;
        return id;
    }

    /// <summary>The journal's lines: a file's target, size and SHA-256, as each was copied.</summary>
    static Dictionary<string, (long Size, string Sha)> ReadJournal(string path)
    {
        var done = new Dictionary<string, (long, string)>(StringComparer.Ordinal);
        if (!File.Exists(path)) return done;
        foreach (var line in File.ReadAllLines(path))
            if (line.Split('\t') is [var target, var size, var sha] && long.TryParse(size, NumberStyles.None, CultureInfo.InvariantCulture, out long n))
                done[target] = (n, sha);
        return done;
    }

    /// <summary>One file copied to a <c>.partial</c> with its hash taken as it is read, moved into place, read back and hashed again.</summary>
    static string Copy(PackSource pack, Item item, string place)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(place)!);
        string partial = place + ".partial";
        string sha;
        using (var from = pack.Open(item.Source))
        using (var to = File.Create(partial))
        using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            var buffer = new byte[1 << 16];
            int n;
            while ((n = from.Read(buffer)) > 0) { hash.AppendData(buffer, 0, n); to.Write(buffer, 0, n); }
            sha = Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        File.Move(partial, place, overwrite: true);
        string again = PlayerModule.HashFile(place);
        if (again != sha) throw new IOException($"{item.Target} did not read back as it was written");
        return sha;
    }

    /// <summary>
    /// **A data file decoded and recorded**: a texture (its size and alpha), a model (its materials), a motion, a material, an interface
    /// layout, a string table, templates, a scene, a chronicle, a sound or a film's header — each read as the game reads it, and what does
    /// not read kept as <see cref="ModuleFileRecord.Problem"/>. Other kinds are recorded without decoding.
    /// </summary>
    public static ModuleFileRecord Decode(string path, string? archive, byte[] data, string sha, PackKinds kind)
    {
        var record = new ModuleFileRecord { Path = path, In = archive, Size = data.Length, Sha256 = sha, Kind = PackKindWords.List(kind).FirstOrDefault() ?? "" };
        string name = Path.GetFileName(path);
        try
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".tga":
                    {
                        var image = TgaImage.Read(data, name);
                        bool used = false;
                        if (image.HasAlpha)
                            for (int i = 3; i < image.Rgba.Length && !used; i += 4) used = image.Rgba[i] != 255;
                        return record with { Width = image.Width, Height = image.Height, Alpha = image.HasAlpha, AlphaUsed = used };
                    }
                case ".nod": return record with { Materials = [.. NodModel.Read(data, name).Materials] };
                case ".nad": NadAnimation.Read(data, name); break;
                case ".nam": NamMaterial.Read(data, name); break;
                case ".nui": NuiPane.Read(data, name); break;
                case ".nls": NlsFile.Read(data, name); break;
                case ".not": NotFile.Read(data, name); break;
                case ".nsd": NsdScene.Read(data, name); break;
                case ".nsc": NscChronicle.Read(data, name); break;
                // The recooked text (Formats.Recooked): a template's, a scene's (its geometry is its own file) or a camera path's.
                case ".txt" when PackFiles.RecookedText(path) is { } form:
                    {
                        var text = RecookText.Parse(data, name);
                        if (form == Recooked.Form.Templates) NotText.Read(text, NotText.NameOf(name));
                        else if (form == Recooked.Form.Path) NcpText.Read(text);
                        break;
                    }
                case ".wav":
                    if (Wav.Read(data) is null) return record with { Problem = "cannot be read as a sound (no WAV header)" };
                    break;
                case ".bik":
                    if (data.Length == 0) return record with { Empty = true };
                    if (!PackFiles.IsBinkFilm(data)) return record with { NotFilm = true };     // a blanked film: it takes 1.1's out
                    break;
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return record with { Problem = $"cannot be read as {PackFiles.What(path)}: {e.Message}" };
        }
        return record;
    }
}

/// <summary>A pack's files, by their paths in it (<c>/</c> separated), from a folder, a zip or one <c>.nob</c>.</summary>
abstract class PackSource : IDisposable
{
    /// <summary><c>folder</c>, <c>zip</c> or <c>nob</c>.</summary>
    public abstract string Kind { get; }
    /// <summary>The pack's own name: its folder's or its file's, without the extension.</summary>
    public abstract string Name { get; }
    public abstract IReadOnlyList<(string Path, long Size)> Files { get; }
    public abstract Stream Open(string path);
    public virtual void Dispose() { }

    public static PackSource From(string source)
    {
        if (Directory.Exists(source)) return new FolderPack(source);
        if (!File.Exists(source)) throw new FileNotFoundException($"no folder or file {source}");
        if (source.EndsWith(".nob", StringComparison.OrdinalIgnoreCase)) return new NobPack(source);
        return new ZipPack(source);
    }

    sealed class FolderPack(string root) : PackSource
    {
        public override string Kind => "folder";
        public override string Name => Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)));
        public override IReadOnlyList<(string Path, long Size)> Files { get; } =
            [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
                .Select(f => (Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'), new FileInfo(f).Length))];
        public override Stream Open(string path) => File.OpenRead(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
    }

    sealed class ZipPack : PackSource
    {
        readonly ZipArchive zip;
        readonly Dictionary<string, ZipArchiveEntry> entries = new(StringComparer.Ordinal);
        readonly string path;

        public ZipPack(string path)
        {
            this.path = path;
            zip = ZipFile.OpenRead(path);
            foreach (var e in zip.Entries)
                if (!string.IsNullOrEmpty(e.Name)) entries.TryAdd(e.FullName.Replace('\\', '/').TrimStart('/'), e);
            Files = [.. entries.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => (e.Key, e.Value.Length))];
        }

        public override string Kind => "zip";
        public override string Name => Path.GetFileNameWithoutExtension(path);
        public override IReadOnlyList<(string Path, long Size)> Files { get; }
        public override Stream Open(string p) => entries[p].Open();
        public override void Dispose() => zip.Dispose();
    }

    sealed class NobPack(string file) : PackSource
    {
        public override string Kind => "nob";
        public override string Name => Path.GetFileNameWithoutExtension(file);
        public override IReadOnlyList<(string Path, long Size)> Files { get; } = [(Path.GetFileName(file), new FileInfo(file).Length)];
        public override Stream Open(string path) => File.OpenRead(file);
    }
}
