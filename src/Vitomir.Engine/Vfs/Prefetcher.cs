// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The thread that reads game files ahead of the game. Vitomir's own.
namespace Vitomir.Engine.Vfs;

/// <summary>
/// **Ours**: reads game files into <see cref="GameFiles.Cache"/> on a background thread of the lowest
/// priority, job by job in the order they were asked for. A job is code that runs on that thread and
/// asks for files through a <see cref="PrefetchReader"/>; it is handed only what was gathered for it
/// on the game's thread, and it never touches the simulation. The thread starts when there is work and
/// ends when the queue is empty, so an idle game has none.
///
/// A job that fails (the files closed under it as the game quits) is dropped: a file not read ahead is
/// only read by the game when it asks, as it always was.
/// </summary>
public sealed class Prefetcher(GameFiles files)
{
    readonly object gate = new();
    readonly Queue<(object? Scope, string What, Action<PrefetchReader> Job)> queue = new();
    bool running;
    int pending;

    /// <summary>What each finished job read ahead, for the game's thread to log (<see cref="Drain"/>).</summary>
    readonly Queue<PrefetchReport> reports = new();

    public GameFiles Files => files;

    /// <summary>Queues <paramref name="job"/>, its files to belong to <paramref name="scope"/>.</summary>
    public void Enqueue(object? scope, string what, Action<PrefetchReader> job)
    {
        lock (gate)
        {
            queue.Enqueue((scope, what, job));
            pending++;
            if (running) return;
            running = true;
        }
        new Thread(Run) { IsBackground = true, Priority = ThreadPriority.Lowest, Name = "Vitomir read-ahead" }.Start();
    }

    void Run()
    {
        while (true)
        {
            (object? Scope, string What, Action<PrefetchReader> Job) next;
            lock (gate)
            {
                if (!queue.TryDequeue(out next)) { running = false; Monitor.PulseAll(gate); return; }
            }
            var reader = new PrefetchReader(files, next.Scope);
            long began = System.Diagnostics.Stopwatch.GetTimestamp();
            string? failed = null;
            try { next.Job(reader); }
            catch (Exception e) { failed = e.Message; }
            var report = new PrefetchReport(next.What, reader.Files, reader.Bytes,
                System.Diagnostics.Stopwatch.GetElapsedTime(began).TotalMilliseconds, files.Cache.Bytes, failed);
            lock (gate)
            {
                reports.Enqueue(report);
                while (reports.Count > 64) reports.Dequeue();
                pending--;
                Monitor.PulseAll(gate);
            }
        }
    }

    /// <summary>Whether every job queued so far has finished; waits up to <paramref name="timeoutMs"/> for it.</summary>
    public bool WaitIdle(int timeoutMs)
    {
        var until = Environment.TickCount64 + timeoutMs;
        lock (gate)
        {
            while (pending > 0)
            {
                long left = until - Environment.TickCount64;
                if (left <= 0 || !Monitor.Wait(gate, (int)left)) return pending == 0;
            }
            return true;
        }
    }

    /// <summary>The reports of the jobs finished since the last call, oldest first.</summary>
    public List<PrefetchReport> Drain()
    {
        lock (gate)
        {
            var list = reports.ToList();
            reports.Clear();
            return list;
        }
    }
}

/// <summary>What one job read ahead: the files and bytes it put in the cache, its time, and the cache's size after it.</summary>
public sealed record PrefetchReport(string What, int Files, long Bytes, double Ms, long CacheBytes, string? Failed);

/// <summary>
/// A job's way to the files: each read answered from the cache when it is already there (and the entry
/// moved to this job's scope), otherwise read from the files and put in the cache — unless the game
/// has read it meanwhile.
/// </summary>
public sealed class PrefetchReader(GameFiles files, object? scope)
{
    /// <summary>How many files, and bytes, this job read from the files into the cache.</summary>
    public int Files { get; private set; }
    public long Bytes { get; private set; }

    /// <summary>The bytes at <paramref name="path"/>, read ahead; false when there is no such file.</summary>
    public bool Fetch(string path, out byte[] data)
    {
        path = FileCache.Normal(path);
        var cache = files.Cache;
        if (cache.TryPeek(path, scope, out data)) return true;
        if (!cache.Claim(path)) { data = []; return false; }        // being read already: nothing to do
        bool found;
        try { found = files.TryReadAhead(path, out data); }
        catch { cache.Unclaim(path); throw; }
        if (!found) { cache.Unclaim(path); return false; }
        if (cache.Put(path, data, scope)) { Files++; Bytes += data.Length; }
        return true;
    }

    /// <summary>The first of <paramref name="paths"/> that exists, read ahead, in the order given — a resource kind's search.</summary>
    public string? First(IEnumerable<string> paths, out byte[] data)
    {
        foreach (var p in paths)
            if (Fetch(p, out data)) return p;
        data = [];
        return null;
    }
}
