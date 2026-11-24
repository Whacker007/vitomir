// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The readable documents, as the engine holds them: several `.ntx` files stacked into one lookup,
// exactly the way `Strings` stacks the `.nls` tables beside them in the same folder.
//
// **First wins here too**, and for the same reason it is worth saying twice: the engine's own
// string map refuses a key it already holds, and a document loader that
// behaved differently would put the two halves of the same chronicle's text out of step. Whether
// the original's `.ntx` loader shares that map is **not read**; this matches `Strings` because
// consistency between the two is the property that matters until it is.
using Vitomir.Engine.Vfs;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>Every loaded `.ntx`, stacked. Ask it for an `ITEM_TEXT` id.</summary>
public sealed class Documents
{
    readonly Dictionary<string, NtxEntry> byId = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> loaded = [];
    readonly GameFiles files;

    /// <summary>Files that were asked for and are not in the install.</summary>
    public IReadOnlyList<string> Missing => missing;
    readonly List<string> missing = [];

    public IReadOnlyList<string> Loaded => loaded;
    public int Count => byId.Count;

    /// <summary>
    /// Nothing is loaded at start-up. Unlike the string tables — which the engine loads four of
    /// before anything else — a document belongs to a chronicle, so the caller adds the one it is
    /// in. Loading all seven would be wrong rather than merely wasteful: two chronicles could
    /// name the same id.
    /// </summary>
    public Documents(GameFiles files) => this.files = files;

    public bool Add(string name)
    {
        if (loaded.Contains(name, StringComparer.OrdinalIgnoreCase)) return true;
        string path = NtxFile.PathFor(name);
        if (!files.TryRead(path, out var data)) { missing.Add(path); return false; }

        var file = NtxFile.Read(data, path);
        foreach (var entry in file.Entries) byId.TryAdd(entry.Id, entry);
        loaded.Add(name);
        return true;
    }

    /// <summary>
    /// Everything a chronicle's **metafile** declares, in the order it declares it. That is where
    /// the list actually lives — `Chronicles/<name>.nmf`, one `NTX` line per table — which settles
    /// a question that had been on the deferred list since the format was decoded.
    ///
    /// **And it is not one table per chronicle.** `vienna.nmf` names `prague.ntx` *and*
    /// `vienna.ntx`, so a letter picked up in Prague is still readable in Vienna. Guessing the file
    /// from the chronicle's own name — which this class did until then — loses the first one.
    /// </summary>
    public int AddFrom(NmfFile? meta)
    {
        int added = 0;
        foreach (var name in meta?.Ntx ?? [])
            if (Add(name)) added++;
        return added;
    }

    /// <summary>
    /// A chronicle's documents begun from none: **every** document let go and the lookup made anew
    /// (the same freeing as the shut-down, then the name table again),
    /// then the tables the new metafile names read in its order (the metafile loader, which calls that reset
    /// before its `NTX` lines). 1.1 does this at every chronicle's start, so a chronicle changed in
    /// play (`ChangeChronicle`, Prague to Vienna) reads Vienna's list — and a table the new metafile
    /// does not name is gone. Vitomir read the first chronicle's list once a run until then, so
    /// Vienna's own letters and books could not be read after playing through from Prague.
    /// </summary>
    public int Restart(NmfFile? meta)
    {
        byId.Clear();
        loaded.Clear();
        missing.Clear();
        return AddFrom(meta);
    }

    /// <summary>The document an item's `ITEM_TEXT` names, or null.</summary>
    public NtxEntry? Find(string id) =>
        id.Length > 0 && byId.TryGetValue(id, out var entry) ? entry : null;
}
