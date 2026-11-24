// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Files read ahead of the game. Vitomir's own; 1.1 has nothing like it.
namespace Vitomir.Engine.Vfs;

/// <summary>
/// **Ours**: game files read ahead of the game by a background thread (<see cref="Prefetcher"/>) and
/// held here until the game asks for them, so that the read on the game's own frame is a lookup in
/// memory instead of an open on the disk. 1.1 read the same files at the same moments from archives it
/// kept open; Vitomir's laid-out game folder opens a loose file for each, and on Windows that open can
/// cost milliseconds a file (the disk, the antivirus scan), which is what made the convent's opening
/// fight stutter in play.
///
/// <para>The rules, each of which keeps what a read answers exactly what it was:</para>
/// <list type="bullet">
/// <item>An entry holds the very bytes <see cref="GameFiles"/> read for its path, looked up the same
/// way, so a read answered from here is the read it would have been.</item>
/// <item>An entry is <b>taken</b> by the read that finds it: the reader gets the array and the cache
/// forgets it. Nothing is ever shared between two readers, and what the game keeps (a sound, a model)
/// is the game's own copy, not held twice. A second read of the same path goes to the files, as it
/// always did.</item>
/// <item>A read that arrives while its file is still being read ahead does not wait: it reads the
/// file itself, and the late copy is thrown away (<see cref="Put"/>).</item>
/// <item>Each entry belongs to a <b>scope</b> — the place, or the conversation, it was read ahead
/// for — and <see cref="Release"/> drops a scope's entries (a place let go, a conversation over).</item>
/// <item>The whole is held under <see cref="Cap"/> bytes: past it the entries read ahead longest ago
/// go first.</item>
/// </list>
/// Safe to use from any thread; everything is under one lock, and a lookup is a dictionary probe.
/// </summary>
public sealed class FileCache
{
    /// <summary>
    /// The most the cache holds, 128 MB (ours). A place's sounds read ahead come to about 9 MB (the
    /// convent 8.9, the Old Town 8.8, Silver Mines 1 8.0, at most P1_ARC3's 21.4), most of it taken within
    /// minutes of play, so this is never reached; it is the bound, not a budget.
    /// </summary>
    public const long DefaultCap = 128L << 20;

    readonly object gate = new();
    readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The entries oldest first, which is the order they go in past the cap.</summary>
    readonly LinkedList<string> order = [];
    /// <summary>Paths being read ahead right now; a read of one by the game unclaims it.</summary>
    readonly HashSet<string> claimed = new(StringComparer.OrdinalIgnoreCase);
    long bytes, peak;

    sealed class Entry(byte[] data, object? scope, LinkedListNode<string> node)
    {
        public byte[] Data { get; } = data;
        public object? Scope { get; set; } = scope;
        public LinkedListNode<string> Node { get; } = node;
    }

    /// <summary>The most the cache holds before the oldest entries go; <see cref="DefaultCap"/> unless set.</summary>
    public long Cap { get; set; } = DefaultCap;

    /// <summary>The bytes held now, the most ever held, and the number of files.</summary>
    public long Bytes { get { lock (gate) return bytes; } }
    public long PeakBytes { get { lock (gate) return peak; } }
    public int Count { get { lock (gate) return entries.Count; } }

    /// <summary>Entries dropped past the cap, and read ahead too late to be used (the game had read the file itself).</summary>
    public long Evicted, Late;

    /// <summary>A path as <see cref="GameFiles"/> looks it up: forward slashes, no leading slash.</summary>
    public static string Normal(string path) => path.Replace('\\', '/').TrimStart('/');

    /// <summary>The paths held now, oldest first.</summary>
    public List<string> Paths() { lock (gate) return [.. order]; }

    /// <summary>Whether the cache holds <paramref name="path"/> (normalised).</summary>
    public bool Holds(string path) { lock (gate) return entries.ContainsKey(path); }

    /// <summary>
    /// The game's read: the entry's bytes, taken out of the cache, when it holds the path. On a miss a
    /// read ahead of that path in flight is unclaimed, so its copy will not be kept.
    /// </summary>
    public bool TryTake(string path, out byte[] data)
    {
        lock (gate)
        {
            if (entries.Remove(path, out var e))
            {
                order.Remove(e.Node);
                bytes -= e.Data.Length;
                data = e.Data;
                return true;
            }
            claimed.Remove(path);
        }
        data = [];
        return false;
    }

    /// <summary>
    /// The read-ahead's look at an entry already held: its bytes, left in place, and the entry moved to
    /// <paramref name="scope"/> and to the young end — whoever asked last is who needs it next.
    /// </summary>
    public bool TryPeek(string path, object? scope, out byte[] data)
    {
        lock (gate)
        {
            if (entries.TryGetValue(path, out var e))
            {
                e.Scope = scope;
                order.Remove(e.Node);
                order.AddLast(e.Node);
                data = e.Data;
                return true;
            }
        }
        data = [];
        return false;
    }

    /// <summary>The read-ahead is about to read <paramref name="path"/>: false when it is held or already being read.</summary>
    public bool Claim(string path)
    {
        lock (gate) return !entries.ContainsKey(path) && claimed.Add(path);
    }

    /// <summary>The read-ahead found nothing at <paramref name="path"/>.</summary>
    public void Unclaim(string path) { lock (gate) claimed.Remove(path); }

    /// <summary>
    /// The read-ahead's bytes for a path it claimed. Kept unless the game read the path itself in the
    /// meantime (then it has its own copy, and this one would only take room), or the file alone is
    /// bigger than the cap. The oldest entries go while the cache is over its cap.
    /// </summary>
    public bool Put(string path, byte[] data, object? scope)
    {
        lock (gate)
        {
            if (!claimed.Remove(path)) { Late++; return false; }
            if (data.Length > Cap) return false;
            var node = order.AddLast(path);
            entries[path] = new Entry(data, scope, node);
            bytes += data.Length;
            while (bytes > Cap && order.First is { } oldest)
            {
                var e = entries[oldest.Value];
                entries.Remove(oldest.Value);
                order.RemoveFirst();
                bytes -= e.Data.Length;
                Evicted++;
            }
            peak = Math.Max(peak, bytes);
            return entries.ContainsKey(path);
        }
    }

    /// <summary>Drops every entry read ahead for <paramref name="scope"/> and answers how many bytes went.</summary>
    public long Release(object scope)
    {
        lock (gate)
        {
            long freed = 0;
            for (var node = order.First; node is not null;)
            {
                var next = node.Next;
                var e = entries[node.Value];
                if (ReferenceEquals(e.Scope, scope))
                {
                    entries.Remove(node.Value);
                    order.Remove(node);
                    bytes -= e.Data.Length;
                    freed += e.Data.Length;
                }
                node = next;
            }
            return freed;
        }
    }

    /// <summary>Drops everything.</summary>
    public void Clear()
    {
        lock (gate) { entries.Clear(); order.Clear(); claimed.Clear(); bytes = 0; }
    }
}
