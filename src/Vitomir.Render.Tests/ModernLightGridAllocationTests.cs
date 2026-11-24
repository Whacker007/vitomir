// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Render.Modern;

namespace Vitomir.Render.Tests;

/// <summary>
/// **The clustered lights' grid allocates nothing once it has grown, and holds what it held**
/// (ours). An allocation ledger of the pilot town at
/// <c>high</c> (12,439 frames) showed two arrays on the large object heap every frame, a <c>UInt32[]</c> of
/// up to 185 KB and a <c>List&lt;int&gt;[]</c> of 92 KB, and 43 full collections, every one for the large
/// heap's budget: <see cref="ModernLights.Assign"/> made the grid anew each frame. The sizes say which grid:
/// 92 KB of references is 11,776 clusters (32 × 23 tiles of 64 pixels, 16 slices), and 185 KB is the result's
/// list doubled once from the room it began with (twice 2 × 11,776 + 64 numbers of four bytes). Now the
/// renderer keeps a <see cref="ModernLights.ClusterSorter"/>.
///
/// Two things are pinned, neither needing a GPU. The sorter's buffer is, number for number, what the code
/// before made (<see cref="Before"/>, that code kept here as it was) for the same lights and view: over
/// pictures of many sizes with one sorter used for all of them in turn, with more lights than a frame
/// takes, and with more light indices than the buffer takes. And on a grid of that size with the Old
/// Town's count of lights, each reaching two to seven metres, walked through with the head turning — a
/// walk on which the code before made exactly the ledger's arrays in every frame — once one lap of the
/// walk has grown the arrays no frame's sorting is charged more than the runtime's own block (<see cref="Quantum"/>) on
/// its thread, at most two frames in a hundred with no collection in them are charged anything, and the frames together
/// under <see cref="BytesAFrame"/> a frame (0 bytes in every frame measured here alone), where the code before allocated
/// over <see cref="BytesAFrameBefore"/> a frame (about 770,000 measured here).
/// </summary>
public class ModernLightGridAllocationTests
{
    const float M = ModernMath.UnitsPerMetre;

    /// <summary>The picture whose grid is 32 × 23 × 16 clusters, the one that ledger showed.</summary>
    const int W = 2048, H = 1472;

    /// <summary>The smallest object that goes to the large object heap (bytes): a frame that allocates less has put nothing there.</summary>
    const long LargeObject = 85_000;

    /// <summary>
    /// What the frames' sorting may allocate at the most, a frame on average, once the arrays have grown
    /// (bytes, on the thread that sorts: <c>GC.GetAllocatedBytesForCurrentThread</c> before and after each
    /// call). Measured here: 0 in every frame of 12,439 alone, and in every frame of 3,000 with another
    /// thread forcing collections throughout.
    /// </summary>
    const long BytesAFrame = 256;

    /// <summary>
    /// How many frames in a hundred may be charged anything at all with no collection in them (was one). The sorter has
    /// nothing on its path that allocates once its arrays have grown, and alone every frame measures 0; but in runs of the
    /// whole suite (its classes run in parallel) frames are charged one block of the runtime's that the sorter did not ask
    /// for: 8,088 bytes in one frame of 720 (October), 8 to 11 frames of 720 charged 8,144 bytes each in all three
    /// attempts of a full run, never alone. In the cloud, with three other threads allocating
    /// without pause and collections in nearly every frame, 120 frames of 4,800 were charged exactly 8,144 bytes, 111 of
    /// them with a collection inside the frame; with gentler load (collections in 38 frames of 4,800) none was. So a
    /// charge is the runtime's own book-keeping under load, not the sorter's: two such frames in a hundred are allowed, and
    /// those a collection fell in are counted apart (<see cref="CollectedFramesInAHundred"/>). An allocation the sorter
    /// made itself would be in every frame, or most, and fail.
    /// </summary>
    const int StrayFramesInAHundred = 2;

    /// <summary>
    /// How many frames in a hundred may be charged (no more than <see cref="Quantum"/> each) with a collection inside them
    /// (any generation's count moved): the runtime's own work on this thread while the other classes allocate. Under the
    /// heaviest load measured here, 2.3 in a hundred; a sorter that allocated in every frame under that load would be over it.
    /// </summary>
    const int CollectedFramesInAHundred = 10;

    /// <summary>
    /// The most a stray frame may be charged: one of the runtime's allocation blocks (8 KiB; every stray seen was 8,088 or
    /// 8,144 bytes). A frame charged more is the sorter's, whatever else happened in it.
    /// </summary>
    const long Quantum = 8_192;

    /// <summary>
    /// What the code before allocated a frame at the least on the same walk: its four large arrays alone
    /// (the lists' array, the result's list as it began and doubled, and the copy returned) are more.
    /// </summary>
    const long BytesAFrameBefore = 4 * 85_000;

    /// <summary>The Old Town's count of exported lights.</summary>
    const int TownLights = 419;

    // ---- the code before, as it was (ModernLights.Assign and what it called, at the levels branch's bdb8cc57) ----

    static float SliceStart(int s) => s == 0 ? 0f : ModernLights.ClusterNear * MathF.Exp(s / ModernLights.SliceScale);
    static float SliceEnd(int s) => s == ModernLights.Slices - 1 ? float.MaxValue : ModernLights.ClusterNear * MathF.Exp((s + 1) / ModernLights.SliceScale);

    static bool Touches(Vector3 c, float r, int tx, int ty, int s, int width, int height, float xScale, float yScale)
    {
        const int TileSize = ModernLights.TileSize;
        float z0 = SliceStart(s), z1 = MathF.Min(SliceEnd(s), -c.Z + r + 1f);
        z0 = MathF.Max(z0, -c.Z - r - 1f);
        if (z1 < z0) return false;
        float u0 = (float)(tx * TileSize) / width * 2f - 1f, u1 = (float)((tx + 1) * TileSize) / width * 2f - 1f;
        float v1 = 1f - (float)(ty * TileSize) / height * 2f, v0 = 1f - (float)((ty + 1) * TileSize) / height * 2f;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (float z in (ReadOnlySpan<float>)[z0, z1])
            foreach (float u in (ReadOnlySpan<float>)[u0, u1])
                foreach (float v in (ReadOnlySpan<float>)[v0, v1])
                {
                    var p = new Vector3(u / xScale * z, v / yScale * z, -z);
                    min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                }
        var q = Vector3.Clamp(c, min, max);
        return Vector3.DistanceSquared(q, c) <= r * r;
    }

    /// <summary>The grid as it was made: a list a cluster, an array of the lists, the buffer grown and copied.</summary>
    static uint[] Before(IReadOnlyList<ModernPointLight> lights, Matrix4x4 view, float xScale, float yScale, int width, int height)
    {
        const int TileSize = ModernLights.TileSize, MaxLights = ModernLights.MaxLights, MaxIndices = ModernLights.MaxIndices;
        var (gx, gy, gz) = ModernLights.Grid(width, height);
        int clusters = gx * gy * gz;
        var lists = new List<int>?[clusters];
        int n = Math.Min(lights.Count, MaxLights);
        for (int i = 0; i < n; i++)
        {
            var l = lights[i];
            var c = Vector3.Transform(l.Position, view);
            float depth = -c.Z, r = l.Range;
            if (depth + r < 0f) continue;   // wholly behind the eye
            int s0 = ModernLights.SliceOf(depth - r), s1 = ModernLights.SliceOf(depth + r);
            int x0 = 0, x1 = gx - 1, y0 = 0, y1 = gy - 1;
            if (depth - r > 1f)
            {
                float near = depth - r, far = depth + r;
                float sx0 = MathF.Min((c.X - r) / near, (c.X - r) / far) * xScale, sx1 = MathF.Max((c.X + r) / near, (c.X + r) / far) * xScale;
                float sy0 = MathF.Min((c.Y - r) / near, (c.Y - r) / far) * yScale, sy1 = MathF.Max((c.Y + r) / near, (c.Y + r) / far) * yScale;
                if (sx1 < -1f || sx0 > 1f || sy1 < -1f || sy0 > 1f) continue;
                x0 = Math.Clamp((int)MathF.Floor((sx0 * 0.5f + 0.5f) * width / TileSize), 0, gx - 1);
                x1 = Math.Clamp((int)MathF.Floor((sx1 * 0.5f + 0.5f) * width / TileSize), 0, gx - 1);
                y0 = Math.Clamp((int)MathF.Floor((0.5f - sy1 * 0.5f) * height / TileSize), 0, gy - 1);
                y1 = Math.Clamp((int)MathF.Floor((0.5f - sy0 * 0.5f) * height / TileSize), 0, gy - 1);
            }
            for (int s = s0; s <= s1; s++)
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        if (!Touches(c, r, x, y, s, width, height, xScale, yScale)) continue;
                        int k = (s * gy + y) * gx + x;
                        (lists[k] ??= []).Add(i);
                    }
        }
        var result = new List<uint>(clusters * 2 + 64);
        for (int k = 0; k < clusters * 2; k++) result.Add(0);
        for (int k = 0; k < clusters; k++)
        {
            var list = lists[k];
            if (list is null) continue;
            int room = MaxIndices - (result.Count - clusters * 2);
            int take = Math.Min(list.Count, Math.Max(room, 0));
            result[k * 2] = (uint)result.Count;
            result[k * 2 + 1] = (uint)take;
            for (int j = 0; j < take; j++) result.Add((uint)list[j]);
        }
        return [.. result];
    }

    // ---- the scenes ----

    /// <summary>The town as the allocation tests walk it: the Old Town's count of lights over 200 m square, each reaching 2 to 7 m.</summary>
    static List<ModernPointLight> Town() => Lights(1141, TownLights, 200, 2, 7);

    /// <summary>
    /// <paramref name="count"/> lights over a square of <paramref name="side"/> metres about the origin, one
    /// to six metres up, reaching <paramref name="reachFrom"/> to <paramref name="reachTo"/> metres; every
    /// seventh a spot (the grid sorts a spot by its sphere of reach, as it does a point light).
    /// </summary>
    static List<ModernPointLight> Lights(int seed, int count, float side, float reachFrom, float reachTo)
    {
        var rng = new Random(seed);
        var lights = new List<ModernPointLight>(count);
        for (int i = 0; i < count; i++)
        {
            var at = new Vector3(((float)rng.NextDouble() - 0.5f) * side * M, ((float)rng.NextDouble() - 0.5f) * side * M, (1 + (float)rng.NextDouble() * 5) * M);
            float reach = (reachFrom + (float)rng.NextDouble() * (reachTo - reachFrom)) * M;
            var colour = new Vector3(1f, 0.7f, 0.4f) * (1 + (float)rng.NextDouble() * 8);
            lights.Add(i % 7 == 3 ? new ModernPointLight(at, colour, reach, -Vector3.UnitZ, 0.9f, 0.7f) : new ModernPointLight(at, colour, reach));
        }
        return lights;
    }

    /// <summary>The view of step <paramref name="step"/> of a walk round the square at eye height, the head turning and nodding as it goes.</summary>
    static (Matrix4x4 View, float Xs, float Ys) Walk(int step, int steps, float aspect)
    {
        float t = (float)step / steps * MathF.Tau;
        var camera = new FreeCamera
        {
            Position = new Vector3(MathF.Cos(t) * 60 * M, MathF.Sin(t * 2) * 40 * M, 1.7f * M),
            Yaw = step * 360f * 3 / steps,
            Pitch = MathF.Sin(t * 5) * 25f,
        };
        float ys = 1f / MathF.Tan(camera.VerticalFov / 2f);
        return (camera.View, ys / aspect, ys);
    }

    static void Same(uint[] before, ReadOnlySpan<uint> now, string what)
    {
        Assert.True(before.Length == now.Length, $"{what}: the buffer is {now.Length} numbers long, before {before.Length}");
        int at = before.AsSpan().CommonPrefixLength(now);
        Assert.True(at == before.Length, $"{what}: number {at} is {(at < now.Length ? now[at] : 0)}, before {(at < before.Length ? before[at] : 0)}");
    }

    // ---- the buffer is what it was ----

    [Fact]
    public void The_grid_is_number_for_number_what_the_code_before_made_with_one_sorter_kept_through_every_view()
    {
        // One sorter through pictures large and small in turn, each walked through, so that whatever a
        // larger or fuller frame left in the kept arrays would show in the next: the town's lights, a few
        // with a wide reach (clusters with long lists), a light the eye stands inside, and no light at all.
        var sorter = new ModernLights.ClusterSorter();
        var scenes = new (string Name, List<ModernPointLight> Lights)[]
        {
            ("the town's 419", Town()),
            ("419 reaching further", Lights(1141, TownLights, 200, 3, 15)),
            ("40 with a wide reach", Lights(7, 40, 120, 30, 90)),
            ("one the eye stands inside", [new ModernPointLight(new Vector3(60 * M, 0, 1.7f * M), Vector3.One, 500 * M)]),
            ("none", []),
            ("1,500 small ones", Lights(26, 1500, 300, 1, 6)),
        };
        (int W, int H)[] pictures = [(W, H), (1280, 720), (2560, 1440), (160, 90), (1920, 1080), (3840, 2160), (1001, 777), (64, 64)];
        int compared = 0; long listed = 0;
        for (int round = 0; round < 2; round++)
            foreach (var (w, h) in pictures)
                foreach (var (name, lights) in scenes)
                    for (int step = round; step < 12; step += 4)
                    {
                        var (view, xs, ys) = Walk(step, 12, (float)w / h);
                        var before = Before(lights, view, xs, ys, w, h);
                        Same(before, sorter.Assign(lights, view, xs, ys, w, h), $"{name}, {w}x{h}, step {step}");
                        Same(before, ModernLights.Assign(lights, view, xs, ys, w, h), $"{name}, {w}x{h}, step {step}, Assign");
                        var (gx, gy, gz) = ModernLights.Grid(w, h);
                        compared++; listed += before.Length - gx * gy * gz * 2;
                    }
        Assert.Equal(2 * 8 * 6 * 3, compared);
        Assert.True(listed > 500_000, $"the comparisons listed {listed} light indices in all: the lights were not in view");
    }

    [Fact]
    public void More_lights_than_a_frame_takes_and_more_indices_than_the_buffer_takes_are_cut_as_before()
    {
        var sorter = new ModernLights.ClusterSorter();
        // More lights than MaxLights: those past the limit are not sorted.
        var many = Lights(3, ModernLights.MaxLights + 500, 400, 2, 10);
        var (view, xs, ys) = Walk(1, 12, 16f / 9f);
        var before = Before(many, view, xs, ys, 1920, 1080);
        Same(before, sorter.Assign(many, view, xs, ys, 1920, 1080), "over MaxLights");
        Assert.True(before.Skip(ModernLights.Grid(1920, 1080).X * ModernLights.Grid(1920, 1080).Y * ModernLights.Slices * 2).All(i => i < ModernLights.MaxLights));

        // More indices than MaxIndices: 60 lights that each reach the whole view are 60 in every one of
        // 14,720 clusters, 883,200 indices; the buffer holds 262,144, and the clusters past them get none.
        var wide = Enumerable.Range(0, 60).Select(i => new ModernPointLight(new Vector3(i * M, 0, 2 * M), Vector3.One, 5000 * M)).ToList();
        var (gx, gy, gz) = ModernLights.Grid(2560, 1440);
        int clusters = gx * gy * gz;
        before = Before(wide, view, xs, ys, 2560, 1440);
        Assert.Equal(clusters * 2 + ModernLights.MaxIndices, before.Length);
        Assert.Equal(0u, before[(clusters - 1) * 2 + 1]);   // the last cluster got none
        Same(before, sorter.Assign(wide, view, xs, ys, 2560, 1440), "over MaxIndices");
        // And the frame after such a one is not marked by it.
        var town = Town();
        Same(Before(town, view, xs, ys, 2560, 1440), sorter.Assign(town, view, xs, ys, 2560, 1440), "the town after the full buffer");
    }

    // ---- and it allocates nothing once grown ----

    [Fact]
    public void A_frames_sorting_at_the_high_presets_size_allocates_nothing_once_the_arrays_have_grown()
    {
        const int Steps = 240, Laps = 3;
        var lights = Town();
        var (gx, gy, gz) = ModernLights.Grid(W, H);
        int clusters = gx * gy * gz;
        Assert.Equal(11_776, clusters);
        // The arrays the code before made each frame for this grid were large ones: the lists' array, the
        // result's list as it began, and (in a frame listing more than 64 indices) that list doubled.
        Assert.True(clusters * IntPtr.Size >= 85_000 && (clusters * 2 + 64) * 4 >= 85_000);
        Assert.Equal(188_928, (clusters * 2 + 64) * 2 * 4);   // the ledger's "up to 185 KB" (184.5 KiB)

        // The code before, one lap: what it allocated a frame, and that every frame of the walk is the
        // ledger's case — more numbers than the result's list began with room for and no more than twice
        // that, so the list doubled once, to the 188,928 bytes, and the copy returned was a large array too.
        int room = clusters * 2 + 64, doubledOnce = 0;
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int step = 0; step < Steps; step++)
        {
            var (view, xs, ys) = Walk(step, Steps, (float)W / H);
            int length = Before(lights, view, xs, ys, W, H).Length;
            if (length > room && length <= room * 2) doubledOnce++;
        }
        long before = (GC.GetAllocatedBytesForCurrentThread() - start) / Steps;
        Assert.True(before > BytesAFrameBefore, $"the code before allocated {before} bytes a frame on this walk: the walk is not the case meant");
        Assert.Equal(Steps, doubledOnce);

        // The sorter: one lap grows its arrays (the first frame most of it, a fuller frame later a little more).
        var sorter = new ModernLights.ClusterSorter();
        for (int step = 0; step < Steps; step++)
        {
            var (view, xs, ys) = Walk(step, Steps, (float)W / H);
            sorter.Assign(lights, view, xs, ys, W, H);
        }
        // Then the same walk again and again, each frame measured by itself. An earlier build (for cell levels):
        // in full parallel runs on a Windows machine this failed three times in four with 8 to 10 frames of 720 allocating one 8 KB
        // block each, and passed alone — the runtime's own warm-up landing in the measured frames under load, not the sorter. So the
        // laps are measured up to five times (was three), the first that keeps within the bounds wins; each frame says whether
        // a collection fell in it, and a charge of more than one of the runtime's blocks is the sorter's. An allocation the sorter
        // makes every frame (or most) still fails all five, and the message gives every attempt.
        const int Frames = Laps * Steps, Attempts = 5;
        long worst = 0, total = 0, numbers = 0;
        int strays = 0, collected = 0, over = 0;
        var charges = new SortedSet<long>();
        var attempts = new List<string>();
        static int Collections() => GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
        bool Within() => over == 0 && strays <= Frames * StrayFramesInAHundred / 100 && collected <= Frames * CollectedFramesInAHundred / 100
                         && total / Frames < BytesAFrame;
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            worst = 0; total = 0; numbers = 0; strays = 0; collected = 0; over = 0;
            charges.Clear();
            for (int lap = 0; lap < Laps; lap++)
                for (int step = 0; step < Steps; step++)
                {
                    var (view, xs, ys) = Walk(step, Steps, (float)W / H);
                    int gcs = Collections();
                    long mark = GC.GetAllocatedBytesForCurrentThread();
                    int length = sorter.Assign(lights, view, xs, ys, W, H).Length;
                    long made = GC.GetAllocatedBytesForCurrentThread() - mark;
                    bool collection = Collections() != gcs;
                    worst = Math.Max(worst, made); numbers += length;
                    if (made == 0) continue;
                    charges.Add(made);
                    if (made > Quantum) { over++; total += made; }
                    else if (collection) collected++;
                    else { strays++; total += made; }
                }
            attempts.Add($"{strays} stray, {collected} with a collection, {over} over a block; the worst {worst} bytes, {total / Frames} a frame; charges {string.Join("/", charges)}");
            if (Within()) break;
        }
        string said = $"of {Frames} frames {strays} were charged with no collection in them, {collected} with one, {over} more than {Quantum} bytes; the worst {worst} bytes, " +
                      $"{total / Frames} a frame on average (those with a collection left out); before, {before} a frame (attempts: {string.Join("; ", attempts)})";
        Assert.True(worst < LargeObject, $"a frame's sorting allocated enough for a large object: {said}");
        Assert.True(over == 0, $"a frame was charged more than the runtime's block: {said}");
        Assert.True(strays <= Frames * StrayFramesInAHundred / 100, $"more than the stray frames allocated: {said}");
        Assert.True(collected <= Frames * CollectedFramesInAHundred / 100, $"too many frames were charged while collections ran: {said}");
        Assert.True(total / Frames < BytesAFrame, $"the frames' sorting allocated over {BytesAFrame} bytes a frame: {said}");
        Assert.True(numbers / Frames * 4 >= LargeObject, "the buffers sorted were not of the large heap's size");
    }

    [Fact]
    public void Standing_still_the_first_frame_makes_the_arrays_and_the_frames_after_it_make_none()
    {
        // "After the first frame": the first sorting of a view makes the arrays, large ones; of the fifty
        // sortings of the same view after it none allocates a large object's worth and (a stray frame
        // aside) none allocates at all.
        const int Frames = 50;
        var lights = Town();
        var (view, xs, ys) = Walk(17, 240, (float)W / H);
        var sorter = new ModernLights.ClusterSorter();
        long mark = GC.GetAllocatedBytesForCurrentThread();
        int length = sorter.Assign(lights, view, xs, ys, W, H).Length;
        long first = GC.GetAllocatedBytesForCurrentThread() - mark;
        Assert.True(first >= LargeObject && length * 4 >= LargeObject, $"the first frame made {first} bytes for {length} numbers: not the large case");
        long worst = 0; int allocating = 0;
        for (int frame = 1; frame <= Frames; frame++)
        {
            mark = GC.GetAllocatedBytesForCurrentThread();
            sorter.Assign(lights, view, xs, ys, W, H);
            long made = GC.GetAllocatedBytesForCurrentThread() - mark;
            worst = Math.Max(worst, made);
            if (made != 0) allocating++;
        }
        Assert.True(worst < LargeObject && allocating <= 1, $"of {Frames} frames of the same view after the first, {allocating} allocated, the worst {worst} bytes");
    }
}
