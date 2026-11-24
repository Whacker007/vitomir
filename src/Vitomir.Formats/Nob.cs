// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.IO.Compression;

namespace Vitomir.Formats;

/// <summary>
/// A <c>.nob</c> archive: a plain ZIP with every entry stored uncompressed. Entry names use
/// forward slashes and are looked up case-insensitively, as the engine does. The game resolves
/// loose files (the <c>Codex\</c> tree, <c>-user</c> directories, <c>_</c>-prefixed globals) before
/// archive contents; that layering belongs to the engine's file system, not here.
/// </summary>
public sealed class NobArchive : IDisposable
{
    readonly ZipArchive zip;
    readonly Dictionary<string, ZipArchiveEntry> byName = new(StringComparer.OrdinalIgnoreCase);
    readonly object gate = new();

    public string Path { get; }

    public NobArchive(string path)
    {
        Path = path;
        zip = ZipFile.OpenRead(path);
        foreach (var e in zip.Entries)
            if (!string.IsNullOrEmpty(e.Name)) byName[Normalize(e.FullName)] = e;
    }

    static string Normalize(string name) => name.Replace('\\', '/').TrimStart('/');

    /// <summary>Every file entry's name and length, in archive order (the data's fingerprint, <c>GameFiles.Fingerprint</c>).</summary>
    public IEnumerable<(string Name, long Length)> Entries =>
        zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).Select(e => (Normalize(e.FullName), e.Length));

    /// <summary>Every file entry's name, as stored.</summary>
    public IEnumerable<string> Names => byName.Values.Select(e => e.FullName);

    public bool Contains(string name) => byName.ContainsKey(Normalize(name));

    /// <summary>Entry names ending in <paramref name="extension"/> (e.g. ".nil"), in archive order.</summary>
    public IEnumerable<string> NamesWithExtension(string extension) =>
        zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name) && e.FullName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                   .Select(e => e.FullName);

    public bool TryRead(string name, out byte[] data)
    {
        if (!byName.TryGetValue(Normalize(name), out var e)) { data = []; return false; }
        lock (gate)                               // ZipArchive is not safe for concurrent reads
        {
            data = new byte[e.Length];
            using var s = e.Open();
            s.ReadExactly(data);
        }
        return true;
    }

    public byte[] Read(string name) =>
        TryRead(name, out var data) ? data : throw new FileNotFoundException($"{name} not in {System.IO.Path.GetFileName(Path)}");

    public void Dispose() => zip.Dispose();
}
