// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Platform.Modules;

/// <summary>
/// A namespaced asset id: the module
/// that owns an asset or data file and its path in that module, written <c>module:path</c> —
/// <c>enhanced:profiles/prototype_sandbox</c>, <c>people:motions/run</c>, <c>oneone:3d/models/christof.nod</c> —
/// never a bare file path, so two modules can never mean the same file by accident and nothing reads into a
/// module it does not know it reads.
/// <para>Ours. The rules: a module id is a lower-case letter followed by lower-case letters, digits, <c>_</c> or
/// <c>-</c>, at most 64; a path is segments joined by <c>/</c>, each of lower-case letters, digits, <c>_</c>,
/// <c>-</c> and <c>.</c>, starting with a letter, digit or <c>_</c>, never <c>.</c> or <c>..</c>, at most 240 in all.
/// Ids are lower-case because the files behind them live on Windows too, where <c>Run.json</c> and
/// <c>run.json</c> are one file. A module file's id is its path under the module's <c>Data/</c>,
/// <c>Content/</c> or <c>Interface/</c> lower-cased and without its extension (<see cref="FromFile"/>);
/// an <c>oneone:</c> id is 1.1's own path for the file, lower-cased, extension and all (1.1 has
/// <c>christof.nod</c> and <c>christof.nad</c> side by side).</para>
/// </summary>
public readonly record struct AssetId : IComparable<AssetId>
{
    /// <summary>The source that is 1.1's own install, read through <see cref="Assets.GameInstall"/>, never shipped.</summary>
    public const string OneOne = "oneone";

    /// <summary>The longest module id.</summary>
    public const int MaxModuleLength = 64;

    /// <summary>The longest path.</summary>
    public const int MaxPathLength = 240;

    AssetId(string module, string path)
    {
        Module = module;
        Path = path;
    }

    /// <summary>The module (or source) that owns the asset.</summary>
    public string Module { get; }

    /// <summary>The asset's path in its module.</summary>
    public string Path { get; }

    /// <summary>Whether this is one of 1.1's own files.</summary>
    public bool IsOneOne => Module == OneOne;

    /// <summary>An id from its two parts; throws <see cref="FormatException"/> naming what is wrong.</summary>
    public static AssetId Of(string module, string path)
    {
        if (ModuleProblem(module) is { } m) throw new FormatException(m);
        if (PathProblem(path) is { } p) throw new FormatException($"'{module}:{path}': {p}");
        return new AssetId(module, path);
    }

    /// <summary>Reads <c>module:path</c>; throws <see cref="FormatException"/> naming what is wrong.</summary>
    public static AssetId Parse(string text) =>
        TryParse(text, out var id, out var problem) ? id : throw new FormatException(problem);

    /// <summary>Reads <c>module:path</c>, or says why it is not an id.</summary>
    public static bool TryParse(string? text, out AssetId id, out string problem)
    {
        id = default;
        if (string.IsNullOrEmpty(text)) { problem = "an asset id is empty"; return false; }
        int colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0) { problem = $"'{text}' is not an asset id (module:path); a bare path is not one"; return false; }
        string module = text[..colon], path = text[(colon + 1)..];
        if (ModuleProblem(module) is { } m) { problem = $"'{text}': {m}"; return false; }
        if (PathProblem(path) is { } p) { problem = $"'{text}': {p}"; return false; }
        id = new AssetId(module, path);
        problem = "";
        return true;
    }

    /// <summary>
    /// The id of a module's file from its path under one of the module's asset folders (<c>/</c> or <c>\</c>
    /// separated): lower-cased, its extension dropped. Throws <see cref="FormatException"/> when the name cannot
    /// make an id (a space, a letter outside a–z).
    /// </summary>
    public static AssetId FromFile(string module, string relativePath)
    {
        string p = relativePath.Replace('\\', '/');
        int slash = p.LastIndexOf('/');
        int dot = p.LastIndexOf('.');
        if (dot > slash + 1) p = p[..dot];
        return Of(module, p.ToLowerInvariant());
    }

    /// <summary>Why <paramref name="module"/> is not a well-formed module id, or null when it is.</summary>
    public static string? ModuleProblem(string? module)
    {
        if (string.IsNullOrEmpty(module)) return "the module id is empty";
        if (module.Length > MaxModuleLength) return $"the module id '{module}' is longer than {MaxModuleLength}";
        if (module[0] is < 'a' or > 'z') return $"the module id '{module}' must start with a lower-case letter";
        foreach (char c in module)
            if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-'))
                return $"the module id '{module}' may hold only lower-case letters, digits, '_' and '-'";
        return null;
    }

    /// <summary>Why <paramref name="path"/> is not a well-formed asset path, or null when it is.</summary>
    public static string? PathProblem(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "the path is empty";
        if (path.Length > MaxPathLength) return $"the path is longer than {MaxPathLength}";
        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0) return "the path has an empty segment (a leading, trailing or doubled '/')";
            if (segment is "." or "..") return $"the path may not hold '{segment}'";
            if (segment[0] == '.' || segment[0] == '-') return $"the path's segment '{segment}' must start with a letter, a digit or '_'";
            if (segment[^1] == '.') return $"the path's segment '{segment}' may not end with '.'";
            foreach (char c in segment)
                if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.'))
                    return $"the path's segment '{segment}' may hold only lower-case letters, digits, '_', '-' and '.'";
        }
        return null;
    }

    /// <summary>Whether this id lies in <paramref name="folder"/> of its module (<c>profiles</c> holds <c>profiles/x</c>; the empty folder holds every id).</summary>
    public bool IsIn(string folder) =>
        folder.Length == 0 || Path.Length > folder.Length && Path.StartsWith(folder, StringComparison.Ordinal) && Path[folder.Length] == '/';

    public int CompareTo(AssetId other)
    {
        int c = string.CompareOrdinal(Module, other.Module);
        return c != 0 ? c : string.CompareOrdinal(Path, other.Path);
    }

    public override string ToString() => $"{Module}:{Path}";
}
