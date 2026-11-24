// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Render.Modern;

namespace Vitomir.Platform.Render.Characters;

/// <summary>
/// Threads that skin a large person beside the frame's own thread: the person's vertices cut into chunks of
/// <see cref="Chunk"/>, taken in turn by whichever thread is free (<see cref="CharacterBatch.SkinRange"/>), each chunk's box
/// kept and joined at the end. Made once, idle between frames, nothing allocated a frame. Presentation only: each vertex is
/// computed the same whichever thread takes it, so the picture does not depend on the threads.
/// </summary>
internal sealed class SkinWorkers : IDisposable
{
    /// <summary>The vertices a thread takes at once.</summary>
    public const int Chunk = 4096;

    /// <summary>The helpers a machine gets: its cores less the frame's own, at most three.</summary>
    public static int Helpers => Math.Clamp(Environment.ProcessorCount - 1, 0, 3);

    readonly Thread[] threads;
    readonly ManualResetEventSlim[] go;
    readonly CountdownEvent done;
    volatile bool stopping;

    // The job in hand, set before the helpers are woken (the wake is a full fence) and read only while they work.
    PeopleMesh.Vertex[] source = [];
    int[]? only;
    Matrix4x4[] placed = [];
    Matrix4x4 place;
    ModernVertex[] into = [];
    int at, count, chunks, next;
    Vector3[] mins = new Vector3[32], maxs = new Vector3[32];

    public SkinWorkers(int helpers)
    {
        threads = new Thread[helpers];
        go = new ManualResetEventSlim[helpers];
        done = new CountdownEvent(1);
        for (int i = 0; i < helpers; i++)
        {
            go[i] = new ManualResetEventSlim(false, spinCount: 50);
            threads[i] = new Thread(Loop) { IsBackground = true, Name = "vitomir-skin-" + i };
            threads[i].Start(i);
        }
    }

    /// <summary>
    /// <paramref name="mesh"/>'s vertices (all, or those <paramref name="list"/> names) skinned by <paramref name="placed"/>
    /// (<see cref="CharacterBatch.Place"/>'s) into <paramref name="vertices"/> from <paramref name="first"/> on, on this thread
    /// and the helpers; answers the box round them.
    /// </summary>
    public (Vector3 Min, Vector3 Max) Skin(PeopleMesh mesh, Matrix4x4[] placed, Matrix4x4 place, int[]? list, ModernVertex[] vertices, int first)
    {
        source = mesh.Vertices;
        only = list;
        this.placed = placed;
        this.place = place;
        into = vertices;
        at = first;
        count = list?.Length ?? mesh.Vertices.Length;
        chunks = (count + Chunk - 1) / Chunk;
        if (mins.Length < chunks) { mins = new Vector3[chunks]; maxs = new Vector3[chunks]; }
        next = 0;
        done.Reset(threads.Length);
        for (int i = 0; i < go.Length; i++) go[i].Set();
        Work();
        done.Wait();
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        for (int c = 0; c < chunks; c++) { min = Vector3.Min(min, mins[c]); max = Vector3.Max(max, maxs[c]); }
        return (min, max);
    }

    void Work()
    {
        int c;
        var output = into.AsSpan(at, count);
        while ((c = Interlocked.Increment(ref next) - 1) < chunks)
        {
            int from = c * Chunk, to = Math.Min(count, from + Chunk);
            if (only is null) CharacterBatch.SkinRange(source, placed, place, from, to, output, out mins[c], out maxs[c]);
            else CharacterBatch.SkinList(source, only, placed, place, from, to, output, out mins[c], out maxs[c]);
        }
    }

    void Loop(object? index)
    {
        var wake = go[(int)index!];
        while (true)
        {
            wake.Wait();
            wake.Reset();
            if (stopping) return;
            Work();
            done.Signal();
        }
    }

    public void Dispose()
    {
        stopping = true;
        foreach (var g in go) g.Set();
        foreach (var t in threads) t.Join();
        foreach (var g in go) g.Dispose();
        done.Dispose();
    }
}
