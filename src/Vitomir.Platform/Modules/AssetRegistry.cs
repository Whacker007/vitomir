// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text;
using Vitomir.Platform.Assets;

namespace Vitomir.Platform.Modules;

/// <summary>An asset id that cannot be read: not there, not visible to the module asking, or its source not open.</summary>
public sealed class AssetException(string message) : Exception(message);

/// <summary>Where an id resolved to: the module whose file it is (the overrider's, when overridden) and the file, or 1.1's install.</summary>
public sealed record AssetSource(AssetId Id, string Module, string? File)
{
    /// <summary>The source for a person to read: the file's path, or the id in 1.1's install.</summary>
    public string Describe() => File ?? $"{Id.Path} in the 1.1 install";
}

/// <summary>An override the loader applied (design 10.1: "the loader logs every one").</summary>
public sealed record AppliedOverride(AssetId Id, string By, string File, string Replaces)
{
    public override string ToString() => $"override: {Id} from module {By} ({File}) in place of {Replaces}";
}

/// <summary>
/// The files of one module by id (ours): every file under its asset folders (<see cref="AssetFolders"/>)
/// under its own namespace, and every file under <c>Overrides/&lt;module&gt;/</c> as the other module's id it
/// overrides. Indexing names every file whose name makes no id, every id two files claim, every override file
/// the manifest does not declare and every declared override without its file.
/// </summary>
public sealed class ModuleFiles
{
    /// <summary>The folders of a module whose files are its assets, each file's id its path under the folder (design 10.1).</summary>
    public static readonly string[] AssetFolders = ["Data", "Content", "Interface"];

    /// <summary>The folder of a module that holds its overrides of other modules' assets: <c>Overrides/people/motions/attack_sword.glb</c>.</summary>
    public const string OverridesFolder = "Overrides";

    readonly SortedDictionary<AssetId, string> own = [];
    readonly SortedDictionary<AssetId, string> overrides = [];

    ModuleFiles(ModuleManifest manifest) { Manifest = manifest; }

    public ModuleManifest Manifest { get; }

    /// <summary>The module's own ids and their files, in id order.</summary>
    public IReadOnlyDictionary<AssetId, string> Own => own;

    /// <summary>Other modules' ids it overrides and its files for them, in id order.</summary>
    public IReadOnlyDictionary<AssetId, string> Overrides => overrides;

    /// <summary>Indexes a module's folder; problems are added and the files that make good ids kept.</summary>
    public static ModuleFiles Index(ModuleManifest m, List<string> problems)
    {
        var files = new ModuleFiles(m);
        foreach (var folderName in AssetFolders)
        {
            string root = Path.Combine(m.Folder, folderName);
            if (!Directory.Exists(root)) continue;
            foreach (var (rel, full) in FilesUnder(root))
            {
                AssetId id;
                try { id = AssetId.FromFile(m.Id, rel); }
                catch (FormatException e) { problems.Add($"module {m.Id}: {folderName}/{rel} makes no asset id ({e.Message})"); continue; }
                if (files.own.TryGetValue(id, out var other))
                    problems.Add($"module {m.Id}: the id {id} is claimed twice, by {Relative(m, other)} and {folderName}/{rel}");
                else files.own[id] = full;
            }
        }

        string overridesRoot = Path.Combine(m.Folder, OverridesFolder);
        if (Directory.Exists(overridesRoot))
        {
            foreach (var (rel, full) in FilesUnder(overridesRoot))
            {
                int slash = rel.IndexOf('/');
                if (slash <= 0) { problems.Add($"module {m.Id}: {OverridesFolder}/{rel} is not in a module's folder ({OverridesFolder}/<module>/<path>)"); continue; }
                AssetId id;
                try { id = AssetId.FromFile(rel[..slash], rel[(slash + 1)..]); }
                catch (FormatException e) { problems.Add($"module {m.Id}: {OverridesFolder}/{rel} makes no asset id ({e.Message})"); continue; }
                if (files.overrides.TryGetValue(id, out var other))
                    problems.Add($"module {m.Id}: the override of {id} is claimed twice, by {Relative(m, other)} and {OverridesFolder}/{rel}");
                else if (!m.Overrides.Contains(id))
                    problems.Add($"module {m.Id}: {OverridesFolder}/{rel} overrides {id} without declaring it (add it to 'overrides' in its {ModuleManifest.FileName})");
                else files.overrides[id] = full;
            }
        }
        foreach (var id in m.Overrides)
        {
            if (id.Module == m.Id) problems.Add($"module {m.Id}: it declares an override of its own {id} (change its file instead)");
            else if (id.IsOneOne) problems.Add($"module {m.Id}: it declares an override of {id}; 1.1's own files are not overridden by modules (not built)");
            else if (!files.overrides.ContainsKey(id))
                problems.Add($"module {m.Id}: it declares an override of {id} but has no file for it under {OverridesFolder}/{id.Module}/");
        }
        return files;
    }

    /// <summary>Every file under <paramref name="root"/>, its path relative to it with <c>/</c>, in ordinal order.</summary>
    static List<(string Rel, string Full)> FilesUnder(string root)
    {
        var list = new List<(string, string)>();
        foreach (var full in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            list.Add((Path.GetRelativePath(root, full).Replace('\\', '/'), full));
        list.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
        return list;
    }

    static string Relative(ModuleManifest m, string full) => Path.GetRelativePath(m.Folder, full).Replace('\\', '/');
}

/// <summary>
/// Resolves a namespaced asset id to its source (ours): a
/// file of the module that owns the id — or of the module whose declared override the load order put last —
/// or, for <c>oneone:</c>, a file of the player's own 1.1 install through <see cref="GameInstall"/>. It only
/// reads: a module's folder is read only to everything else. Built by the <see cref="ModuleChecker"/> from a
/// game's modules in load order; each module reads through its own view (<see cref="For"/>), which sees its own
/// ids, its dependencies' (and theirs) and 1.1's, and refuses the rest by name.
/// </summary>
public sealed class AssetRegistry
{
    readonly Dictionary<string, ModuleFiles> modules = new(StringComparer.Ordinal);
    readonly List<ModuleManifest> order = [];
    readonly Dictionary<AssetId, AssetSource> overridden = [];
    readonly List<AppliedOverride> applied = [];
    readonly Dictionary<string, HashSet<string>> reads = new(StringComparer.Ordinal);

    internal AssetRegistry(IReadOnlyList<ModuleFiles> inOrder, IReadOnlyDictionary<string, HashSet<string>> dependsOn, GameInstall? oneOne)
    {
        OneOneInstall = oneOne;
        foreach (var f in inOrder)
        {
            modules[f.Manifest.Id] = f;
            order.Add(f.Manifest);
            var r = new HashSet<string>(StringComparer.Ordinal) { f.Manifest.Id, AssetId.OneOne };
            if (dependsOn.TryGetValue(f.Manifest.Id, out var deps)) r.UnionWith(deps);
            reads[f.Manifest.Id] = r;
        }
        // In load order, so a later (dependant) module's override wins; the checker has refused two overriders the order does not decide.
        foreach (var f in inOrder)
            foreach (var (id, file) in f.Overrides)
            {
                string replaces = overridden.TryGetValue(id, out var was) ? $"module {was.Module}'s override" : $"module {id.Module}'s own";
                overridden[id] = new AssetSource(id, f.Manifest.Id, file);
                applied.Add(new AppliedOverride(id, f.Manifest.Id, Path.GetRelativePath(f.Manifest.Folder, file).Replace('\\', '/'), replaces));
            }
    }

    /// <summary>A registry of one module alone, checked as the loader checks it (for a module's own tests and tools); throws <see cref="ModuleCheckException"/>.</summary>
    public static AssetRegistry ForModule(string moduleFolder, GameInstall? oneOne = null)
    {
        var problems = new List<string>();
        var m = ModuleManifest.Read(moduleFolder, problems);
        if (m is null) throw new ModuleCheckException(problems);
        var check = ModuleChecker.Check([m], new GameList("module-" + m.Id, m.Name, [m.Id]), oneOne);
        return check.Assets ?? throw new ModuleCheckException(check.Problems);
    }

    /// <summary>The modules in load order.</summary>
    public IReadOnlyList<ModuleManifest> Modules => order;

    /// <summary>The overrides applied, in load order.</summary>
    public IReadOnlyList<AppliedOverride> Overrides => applied;

    /// <summary>The 1.1 install behind <c>oneone:</c>, or null.</summary>
    public GameInstall? OneOneInstall { get; }

    /// <summary>Whether <paramref name="id"/> resolves.</summary>
    public bool Exists(AssetId id) => TryResolve(id, out _);

    /// <summary>Resolves an id; false when nothing has it.</summary>
    public bool TryResolve(AssetId id, out AssetSource source)
    {
        if (id.IsOneOne)
        {
            source = new AssetSource(id, AssetId.OneOne, null);
            return OneOneInstall is not null && OneOneInstall.Exists(id.Path);
        }
        if (overridden.TryGetValue(id, out var over)) { source = over; return true; }
        if (modules.TryGetValue(id.Module, out var files) && files.Own.TryGetValue(id, out var file))
        {
            source = new AssetSource(id, id.Module, file);
            return true;
        }
        source = new AssetSource(id, id.Module, null);
        return false;
    }

    /// <summary>Resolves an id; throws <see cref="AssetException"/> naming it when nothing has it.</summary>
    public AssetSource Resolve(AssetId id)
    {
        if (TryResolve(id, out var s)) return s;
        if (id.IsOneOne)
            throw new AssetException(OneOneInstall is null ? $"{id}: no 1.1 install is open" : $"{id}: not in the 1.1 install at {OneOneInstall.Root}");
        if (!modules.ContainsKey(id.Module)) throw new AssetException($"{id}: no module {id.Module} is loaded");
        throw new AssetException($"{id}: module {id.Module} has no such asset");
    }

    /// <summary>The bytes behind an id (an asset a data override changed reads as changed).</summary>
    public byte[] Read(AssetId id)
    {
        var s = Resolve(id);
        lock (readIds) readIds.Add(id);
        if (patched.TryGetValue(id, out var bytes)) return (byte[])bytes.Clone();
        return s.File is null ? OneOneInstall!.Read(id.Path) : File.ReadAllBytes(s.File);
    }

    // ---- data overrides, applied to this registry's reads only (never to the files)
    readonly Dictionary<AssetId, byte[]> patched = [];
    readonly HashSet<AssetId> readIds = [];

    /// <summary>The ids read so far (by the modules as they were configured, and by whatever read them since), in id order.</summary>
    public IReadOnlyList<AssetId> ReadSoFar { get { lock (readIds) return [.. readIds.Order()]; } }
    readonly List<AppliedDataOverride> dataApplied = [];

    /// <summary>The data overrides applied to this registry (<see cref="ApplyData"/>), in order.</summary>
    public IReadOnlyList<AppliedDataOverride> DataOverrides => dataApplied;

    /// <summary>
    /// Applies <paramref name="overrides"/> to the assets they name, in order, so every later <see cref="Read"/> of them returns
    /// the changed bytes (the loader does it before any module is configured). Each problem — an asset not there, not JSON, a
    /// pointer that names nothing — is added to <paramref name="problems"/>, and that asset is left as it is.
    /// </summary>
    public void ApplyData(IReadOnlyList<DataOverride> overrides, List<string> problems)
    {
        foreach (var group in overrides.GroupBy(o => o.Asset))
        {
            if (!TryResolve(group.Key, out _)) { problems.Add($"data override: {group.Key}: no such asset (the modules' Data/ files are listed by --describe data)"); continue; }
            var done = new List<AppliedDataOverride>();
            try
            {
                patched[group.Key] = global::Vitomir.Platform.Modules.DataOverrides.Apply(group.Key, Read(group.Key), [.. group], done);
                dataApplied.AddRange(done);
            }
            catch (DataOverrideException e) { problems.Add($"data override: {e.Message}"); }
        }
    }
    // ---- end

    /// <summary>The ids of <paramref name="module"/> in <paramref name="folder"/> (and below; "" for all), in id order.</summary>
    public IReadOnlyList<AssetId> List(string module, string folder)
    {
        if (!modules.TryGetValue(module, out var files)) return [];
        var list = new List<AssetId>();
        foreach (var id in files.Own.Keys) if (id.IsIn(folder)) list.Add(id);
        return list;
    }

    /// <summary>What <paramref name="module"/> may read: its own ids, its dependencies' and 1.1's.</summary>
    public ModuleAssets For(string module) =>
        reads.TryGetValue(module, out var r) ? new ModuleAssets(this, module, r) : throw new ArgumentException($"no module {module} is loaded", nameof(module));
}

/// <summary>
/// What one module reads its data and assets through (<see cref="ModuleConfigContext.Assets"/>): ids, never
/// paths; its own, its dependencies' and 1.1's, and nothing else (another module's id is refused by name).
/// </summary>
public sealed class ModuleAssets
{
    readonly AssetRegistry registry;
    readonly HashSet<string> visible;

    internal ModuleAssets(AssetRegistry registry, string module, HashSet<string> visible)
    {
        this.registry = registry;
        this.visible = visible;
        Module = module;
    }

    /// <summary>The module's own id (its namespace).</summary>
    public string Module { get; }

    /// <summary>The id of one of the module's own assets.</summary>
    public AssetId Own(string path) => AssetId.Of(Module, path);

    /// <summary>Whether the id resolves and this module may read it.</summary>
    public bool Exists(AssetId id) => visible.Contains(id.Module) && registry.Exists(id);

    /// <summary>The bytes behind an id; throws <see cref="AssetException"/> naming it.</summary>
    public byte[] Read(AssetId id)
    {
        Check(id);
        return registry.Read(id);
    }

    /// <summary>The text (UTF-8) behind an id.</summary>
    public string ReadText(AssetId id) => Encoding.UTF8.GetString(Read(id)).TrimStart('﻿');

    /// <summary>Its own ids in <paramref name="folder"/>, in id order.</summary>
    public IReadOnlyList<AssetId> List(string folder) => registry.List(Module, folder);

    /// <summary>Another visible module's ids in <paramref name="folder"/>, in id order.</summary>
    public IReadOnlyList<AssetId> List(string module, string folder)
    {
        if (!visible.Contains(module)) throw new AssetException($"module {Module} may not read module {module}'s assets: it does not depend on it");
        return registry.List(module, folder);
    }

    /// <summary>The data overrides applied to this module's own assets (none when nothing overrode them), in order.</summary>
    public IReadOnlyList<AppliedDataOverride> DataOverrides => [.. registry.DataOverrides.Where(o => o.Asset.Module == Module)];

    /// <summary>Where an id resolves, for a person to read (a file's path or the 1.1 install).</summary>
    public string Where(AssetId id)
    {
        Check(id);
        return registry.Resolve(id).Describe();
    }

    void Check(AssetId id)
    {
        if (!visible.Contains(id.Module))
            throw new AssetException($"module {Module} may not read {id}: it does not depend on module {id.Module}");
    }
}
