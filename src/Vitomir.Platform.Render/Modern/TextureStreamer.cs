// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Modern/TextureStreamer.cs at b4185ef, reworked for the platform.
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using SDL;
using Vitomir.Formats.L2;
using TextureKind = Vitomir.Platform.Render.Level.TextureKind;
using static SDL.SDL3;

namespace Vitomir.Platform.Render.Modern;

/// <summary>
/// **Ours (Phase 3): textures streamed by mip.** A cooked level's maps are KTX2 files,
/// BC7 or BC5 with every mip (<see cref="Ktx2"/>). When the level opens, the small mips of every map — those
/// no bigger than <see cref="SmallEdge"/> texels — are read and put on the GPU, and stay. Each frame the
/// drawn surfaces ask for the mip their distance and texture density call for (<see cref="Request"/>:
/// the level whose texels a metre are nearest the screen's pixels a metre there); larger mips are read on
/// a background thread with the rest of the chain below them, and, when in, the texture is made again from
/// them. Everything stays under <see cref="BudgetBytes"/>: when the wanted mips would pass it, the maps
/// longest unused, then the biggest, give up their top mip first, and are made again smaller the same way.
/// A map asked for coarser than it has keeps what it has while the budget allows.
/// </summary>
internal sealed unsafe class TextureStreamer : IDisposable
{
    /// <summary>Mips no bigger than this (texels on the long side) are always in.</summary>
    public const int SmallEdge = 64;

    sealed class Tex
    {
        public required string File;
        public required Ktx2.Header Header;
        public required SDL_GPUTextureFormat Format;
        public SDL_GPUTexture* Gpu;
        /// <summary>The file's level that is the GPU texture's level 0.</summary>
        public int Top;
        public int Small;
        public int Requested = int.MaxValue;
        public int LastRequested = -1000;
        public bool Loading;
        public int LoadingTop;
        /// <summary>
        /// The chain the worker read, levels <see cref="LoadingTop"/> to the last, each at its place in the
        /// transfer buffer (<see cref="Align"/>): a block of native memory, 0 for none (ours — a
        /// managed array a mip was megabytes on the large object heap every time a map changed its top
        /// mip, and a walk through the town changes several a second: a full collection a second).
        /// </summary>
        public nint Read;
        /// <summary>The bytes of each level, for the budget's plan: made once.</summary>
        public long[] LevelBytes = [];
        public int LevelCount => Header.Levels.Length;
        public long Bytes(int top) { long b = 0; for (int l = Math.Max(0, top); l < LevelCount; l++) b += Header.Levels[l].Length; return b; }
    }

    readonly Gpu gpu;
    readonly List<Tex> textures = [];
    readonly Queue<Tex> toRead = new();
    readonly Queue<Tex> read = new();
    readonly object gate = new();
    Thread? worker;
    bool stopping;
    int frame;

    public long BudgetBytes { get; set; }
    /// <summary>Mips a frame may copy in at most, in bytes.</summary>
    public long UploadBytesPerFrame { get; set; } = 16L << 20;
    public long ResidentBytes => textures.Sum(t => t.Bytes(t.Top));
    public int Count => textures.Count;
    public int Loads { get; private set; }
    public int Shrinks { get; private set; }
    /// <summary>Bytes of mips sent to the GPU since the streamer began (ours: the frame log's).</summary>
    public long UploadedBytes { get; private set; }
    public double ReadMilliseconds { get; private set; }
    /// <summary>How many maps wanted a finer mip than they had at the last frame, or were reading one.</summary>
    public int Wanting { get; private set; }
    /// <summary>How many maps have their full-size mip in.</summary>
    public int AtFullSize => textures.Count(t => t.Top == 0);
    /// <summary>The mean of the maps' top mips (0: all at full size).</summary>
    public double MeanTop => textures.Count == 0 ? 0 : textures.Average(t => t.Top);

    /// <summary>Opens the maps (paths relative to <paramref name="folder"/>) with their small mips in.</summary>
    public TextureStreamer(Gpu gpu, string folder, IReadOnlyList<string> files, long budgetBytes)
    {
        this.gpu = gpu;
        BudgetBytes = budgetBytes;
        foreach (var f in files)
        {
            var path = Path.Combine(folder, f);
            using var fs = File.OpenRead(path);
            var head = new byte[Math.Min(fs.Length, 4096)];
            fs.ReadExactly(head);
            var h = Ktx2.ReadHeader(head, fs.Length, path);
            int small = 0;
            while (small < h.Levels.Length - 1 && Math.Max(h.Width >> small, h.Height >> small) > SmallEdge) small++;
            var tex = new Tex { File = path, Header = h, Format = FormatOf(h.Format), Top = small, Small = small };
            tex.LevelBytes = new long[h.Levels.Length];
            for (int l = 0; l < h.Levels.Length; l++) tex.LevelBytes[l] = h.Levels[l].Length;
            tex.Read = ReadLevels(tex, small, fs);
            textures.Add(tex);
        }
        // The small mips of every map, in one go.
        var cmd = SDL_AcquireGPUCommandBuffer(gpu.Device);
        foreach (var t in textures) Swap(cmd, t, t.Small, t.Read, null);
        var fence = SDL_SubmitGPUCommandBufferAndAcquireFence(cmd);
        SDL_WaitForGPUFences(gpu.Device, true, &fence, 1);
        SDL_ReleaseGPUFence(gpu.Device, fence);
        foreach (var t in textures) Free(t);
        ReleasePending(all: true);
    }

    static SDL_GPUTextureFormat FormatOf(Ktx2Format f) => f switch
    {
        Ktx2Format.Bc7Srgb => SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_BC7_RGBA_UNORM_SRGB,
        Ktx2Format.Bc7Unorm => SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_BC7_RGBA_UNORM,
        Ktx2Format.Bc5Unorm => SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_BC5_RG_UNORM,
        Ktx2Format.Bc4Unorm => SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_BC4_R_UNORM,
        Ktx2Format.Rgba8Srgb => SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM_SRGB,
        _ => SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM,
    };

    /// <summary>Whether this GPU can sample the block-compressed formats a cooked level's maps use.</summary>
    public static bool Supported(Gpu gpu) =>
        new[] { SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_BC7_RGBA_UNORM_SRGB, SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_BC7_RGBA_UNORM,
                SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_BC5_RG_UNORM, SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_BC4_R_UNORM }
            .All(f => SDL_GPUTextureSupportsFormat(gpu.Device, f, SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D, SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER));

    /// <summary>The map's texture now (0 while it has none).</summary>
    public nint Texture(int index) => index >= 0 && index < textures.Count ? (nint)textures[index].Gpu : 0;

    /// <summary>The map's size (texels on its long side).</summary>
    public int Size(int index) => Math.Max(textures[index].Header.Width, textures[index].Header.Height);

    /// <summary>
    /// A surface drawn this frame asks for map <paramref name="index"/> at a place where the screen
    /// shows <paramref name="pixelsPerMetre"/> pixels a metre and the surface holds
    /// <paramref name="uvPerMetre"/> texture repeats a metre: the mip whose texels a metre are nearest the
    /// pixels, never coarser than the small mips.
    /// </summary>
    public void Request(int index, float pixelsPerMetre, float uvPerMetre)
    {
        if (index < 0 || index >= textures.Count) return;
        var t = textures[index];
        int level = MipFor(t.Header.Width, t.Header.Height, pixelsPerMetre, uvPerMetre, t.LevelCount);
        t.Requested = Math.Min(t.Requested, Math.Min(level, t.Small));
        t.LastRequested = frame;
    }

    /// <summary>
    /// The mip for a surface: level L holds (size ≫ L) × density texels a metre; the finest level whose
    /// texels a metre are no more than the pixels a metre (so a texel covers at least a pixel). Pure.
    /// </summary>
    public static int MipFor(int width, int height, float pixelsPerMetre, float uvPerMetre, int levels)
    {
        if (uvPerMetre <= 0 || pixelsPerMetre <= 0) return levels - 1;
        float texelsPerMetre = Math.Max(width, height) * uvPerMetre;
        int level = (int)MathF.Floor(MathF.Log2(MathF.Max(texelsPerMetre / pixelsPerMetre, 1f)));
        return Math.Clamp(level, 0, levels - 1);
    }

    /// <summary>
    /// The budget's decision (pure): each map's top mip given what it asks for — the finer of that and what
    /// it has (what it has, for one not asked for this frame) — and when the total passes the budget, the
    /// maps longest unused, then the one that saves most, giving up a mip at a time, never below its small mips.
    /// </summary>
    public static int[] Plan(IReadOnlyList<(long[] LevelBytes, int Top, int Small, int Requested, int Age)> maps, long budget)
    {
        var want = new int[maps.Count];
        Plan(maps, budget, want);
        return want;
    }

    /// <summary>The same, into <paramref name="want"/> (at least as long as <paramref name="maps"/>): the frame's own call, which makes nothing.</summary>
    public static void Plan(IReadOnlyList<(long[] LevelBytes, int Top, int Small, int Requested, int Age)> maps, long budget, int[] want)
    {
        // A map keeps the mips it has while the budget allows, even when asked for coarser: making maps again
        // smaller as the camera draws back and larger as it comes near costs uploads for nothing.
        for (int i = 0; i < maps.Count; i++)
        {
            var m = maps[i];
            want[i] = m.Requested == int.MaxValue ? m.Top : Math.Min(Math.Min(m.Requested, m.Small), m.Top);
        }
        long total = 0;
        for (int i = 0; i < maps.Count; i++)
        {
            var levels = maps[i].LevelBytes;
            for (int l = Math.Max(0, want[i]); l < levels.Length; l++) total += levels[l];
        }
        while (total > budget)
        {
            int pick = -1; long bestSaving = -1; int bestAge = -1;
            for (int i = 0; i < maps.Count; i++)
            {
                if (want[i] >= maps[i].Small) continue;
                long saving = maps[i].LevelBytes[want[i]];
                if (maps[i].Age > bestAge || (maps[i].Age == bestAge && saving > bestSaving)) { pick = i; bestSaving = saving; bestAge = maps[i].Age; }
            }
            if (pick < 0) break;
            total -= maps[pick].LevelBytes[want[pick]];
            want[pick]++;
        }
    }

    /// <summary>The plan's maps and its answer, kept from frame to frame.</summary>
    readonly List<(long[] LevelBytes, int Top, int Small, int Requested, int Age)> planMaps = [];
    int[] plan = [];

    /// <summary>The frame's streaming, on <paramref name="cmd"/> before its render passes.</summary>
    public void Update(SDL_GPUCommandBuffer* cmd)
    {
        frame++;
        planMaps.Clear();
        foreach (var t in textures)
            planMaps.Add((t.LevelBytes, t.Top, t.Small, t.LastRequested == frame - 1 ? t.Requested : int.MaxValue, frame - 1 - t.LastRequested));
        if (plan.Length < textures.Count) plan = new int[textures.Count];
        Plan(planMaps, BudgetBytes, plan);
        long uploaded = 0;
        for (int i = 0; i < textures.Count; i++)
        {
            var t = textures[i];
            int want = plan[i];
            t.Requested = int.MaxValue;
            if (want != t.Top && !t.Loading)
            {
                // Finer or coarser, the new chain is read from the file whole, from its new top down.
                t.Loading = true; t.LoadingTop = want;
                lock (gate) { toRead.Enqueue(t); Monitor.PulseAll(gate); }
            }
        }
        // Mips read: the texture made again with them on top.
        while (uploaded < UploadBytesPerFrame)
        {
            Tex? t;
            lock (gate) t = read.Count > 0 ? read.Dequeue() : null;
            if (t is null) break;
            t.Loading = false;
            if (t.Read == 0) continue;
            uploaded += t.Bytes(t.LoadingTop);
            if (t.LoadingTop < t.Top) Loads++; else if (t.LoadingTop > t.Top) Shrinks++;
            Swap(cmd, t, t.LoadingTop, t.Read, t.Gpu);
            Free(t);
        }
        UploadedBytes += uploaded;
        Wanting = 0;
        for (int i = 0; i < textures.Count; i++) if (textures[i].Loading || plan[i] < textures[i].Top) Wanting++;
        ReleasePending();
        if (worker is null)
        {
            worker = new Thread(Work) { IsBackground = true, Name = "texture stream", Priority = ThreadPriority.BelowNormal };
            worker.Start();
        }
    }

    readonly List<(nint Texture, int Frame)> retired = [];
    readonly List<(nint Buffer, int Frame)> transfers = [];

    /// <summary>
    /// Old textures and the transfer buffers their mips came through, let go a few frames on, when no frame
    /// in flight can use them (a transfer buffer let go before its frame was submitted crashed lavapipe).
    /// </summary>
    void ReleasePending(bool all = false)
    {
        for (int i = transfers.Count - 1; i >= 0; i--)
            if (all || frame - transfers[i].Frame >= 4) { SDL_ReleaseGPUTransferBuffer(gpu.Device, (SDL_GPUTransferBuffer*)transfers[i].Buffer); transfers.RemoveAt(i); }
        for (int i = retired.Count - 1; i >= 0; i--)
            if (all || frame - retired[i].Frame >= 4) { SDL_ReleaseGPUTexture(gpu.Device, (SDL_GPUTexture*)retired[i].Texture); retired.RemoveAt(i); }
    }

    /// <summary>
    /// Makes <paramref name="t"/>'s texture again with file levels <paramref name="top"/> on, every level
    /// uploaded from <paramref name="fresh"/> (file levels top, top + 1, … to the last, as <see cref="ReadLevels"/>
    /// lays them); <paramref name="old"/>
    /// is let go a few frames on. (Copying the levels it already had across on the GPU instead was tried
    /// and crashed the software GPU, lavapipe, when maps shrank; the small levels cost little to send again.)
    /// </summary>
    void Swap(SDL_GPUCommandBuffer* cmd, Tex t, int top, nint fresh, SDL_GPUTexture* old)
    {
        var (w, h) = Ktx2.LevelSize(t.Header.Width, t.Header.Height, top);
        var info = new SDL_GPUTextureCreateInfo
        {
            type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D, format = t.Format, usage = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER,
            width = (uint)w, height = (uint)h, layer_count_or_depth = 1, num_levels = (uint)(t.LevelCount - top),
        };
        var tex = SDL_CreateGPUTexture(gpu.Device, &info);
        if (tex == null) throw new SdlException("SDL_CreateGPUTexture (streamed map)");
        long total = ChainBytes(t, top);
        var pass = SDL_BeginGPUCopyPass(cmd);
        if (total > 0)
        {
            var tinfo = new SDL_GPUTransferBufferCreateInfo { usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD, size = (uint)total };
            var transfer = SDL_CreateGPUTransferBuffer(gpu.Device, &tinfo);
            var map = (byte*)SDL_MapGPUTransferBuffer(gpu.Device, transfer, false);
            // The block is laid out as the transfer buffer is: each level's bytes at its aligned place.
            long o = 0;
            for (int l = top; l < t.LevelCount; l++)
            {
                long len = t.Header.Levels[l].Length;
                Buffer.MemoryCopy((byte*)fresh + o, map + o, len, len);
                o += Align(len);
            }
            SDL_UnmapGPUTransferBuffer(gpu.Device, transfer);
            o = 0;
            for (int k = 0; k < t.LevelCount - top; k++)
            {
                var (lw, lh) = Ktx2.LevelSize(t.Header.Width, t.Header.Height, top + k);
                var src = new SDL_GPUTextureTransferInfo { transfer_buffer = transfer, offset = (uint)o };
                var dst = new SDL_GPUTextureRegion { texture = tex, mip_level = (uint)k, w = (uint)lw, h = (uint)lh, d = 1 };
                SDL_UploadToGPUTexture(pass, &src, &dst, false);
                o += Align(t.Header.Levels[top + k].Length);
            }
            transfers.Add(((nint)transfer, frame));
        }
        SDL_EndGPUCopyPass(pass);
        if (old != null) retired.Add(((nint)old, frame));
        t.Gpu = tex;
        t.Top = top;
    }

    static long Align(long n) => (n + 511) & ~511L;

    /// <summary>What file levels <paramref name="from"/> to the last take, each on its 512-byte boundary: the chain's block and its transfer buffer.</summary>
    static long ChainBytes(Tex t, int from)
    {
        long total = 0;
        for (int l = from; l < t.LevelCount; l++) total += Align(t.Header.Levels[l].Length);
        return total;
    }

    /// <summary>
    /// File levels <paramref name="from"/> to the last, read into one block of native memory, each level at
    /// its aligned place (<see cref="ChainBytes"/>); the caller frees it (<see cref="Free"/>). Native, not
    /// an array: nothing here is the garbage collector's.
    /// </summary>
    static nint ReadLevels(Tex t, int from, FileStream? open = null)
    {
        int to = t.LevelCount;
        long total = ChainBytes(t, from);
        if (total == 0) return 0;
        var block = (byte*)NativeMemory.Alloc((nuint)total);
        var fs = open ?? new FileStream(t.File, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 0);
        try
        {
            long o = 0;
            for (int l = from; l < to; l++)
            {
                var (off, len) = t.Header.Levels[l];
                fs.Seek(off, SeekOrigin.Begin);
                fs.ReadExactly(new Span<byte>(block + o, checked((int)len)));
                o += Align(len);
            }
            return (nint)block;
        }
        catch { NativeMemory.Free(block); throw; }
        finally { if (open is null) fs.Dispose(); }
    }

    /// <summary>Lets a map's read chain go.</summary>
    static void Free(Tex t)
    {
        if (t.Read != 0) NativeMemory.Free((void*)t.Read);
        t.Read = 0;
    }

    void Work()
    {
        while (true)
        {
            Tex t;
            lock (gate)
            {
                while (toRead.Count == 0 && !stopping) Monitor.Wait(gate);
                if (stopping) return;
                t = toRead.Dequeue();
            }
            var watch = Stopwatch.StartNew();
            try { t.Read = ReadLevels(t, t.LoadingTop); }
            catch (IOException e) { Console.Error.WriteLine($"texture stream: {t.File}: {e.Message}"); t.Read = 0; }
            ReadMilliseconds += watch.Elapsed.TotalMilliseconds;
            lock (gate) read.Enqueue(t);
        }
    }

    public void Dispose()
    {
        lock (gate) { stopping = true; Monitor.PulseAll(gate); }
        bool stopped = worker?.Join(2000) ?? true;
        if (stopped) foreach (var t in textures) Free(t);      // chains read and never taken up (a worker still reading keeps its own)
        foreach (var t in textures) if (t.Gpu != null) SDL_ReleaseGPUTexture(gpu.Device, t.Gpu);
        ReleasePending(all: true);
    }
}

/// <summary>
/// **Ours (Phase 3): a cooked level's materials** (<c>materials/*.json</c>, <see cref="L2Material"/>) as the
/// modern renderer's material table: one image per KTX2 map (its size from the file's header, no pixels —
/// the <see cref="TextureStreamer"/> gives the textures) and the files in that order.
/// </summary>
internal static class CellMaterials
{
    /// <summary>
    /// The cook's definition of a glTF material (<c>vitomir --cook-l2</c>): its factors, its maps as
    /// <paramref name="map"/> cooks them (by what each holds), alpha, sidedness, what the cook made of the
    /// surface, and — ours — its height map (BC4) and span, and its foliage, and — how its maps tile
    /// (the inverse of <see cref="Load"/>).
    /// </summary>
    public static L2Material Define(GltfMaterial m, string name, Func<GltfTextureSlot, TextureKind, L2MaterialMap?> map,
                                    bool ground, bool passable)
    {
        return new L2Material
        {
            Name = name, BaseColorFactor = [m.BaseColorFactor.X, m.BaseColorFactor.Y, m.BaseColorFactor.Z, m.BaseColorFactor.W],
            BaseColorTexture = map(m.BaseColorTexture, TextureKind.Colour), MetallicFactor = m.MetallicFactor, RoughnessFactor = m.RoughnessFactor,
            MetallicRoughnessTexture = map(m.MetallicRoughnessTexture, TextureKind.Data), NormalTexture = map(m.NormalTexture, TextureKind.Normal),
            NormalScale = m.NormalScale, OcclusionTexture = map(m.OcclusionTexture, TextureKind.Data), OcclusionStrength = m.OcclusionStrength,
            EmissiveFactor = [m.EmissiveFactor.X, m.EmissiveFactor.Y, m.EmissiveFactor.Z], EmissiveTexture = map(m.EmissiveTexture, TextureKind.Colour),
            EmissiveStrength = m.EmissiveStrength, SpecularFactor = m.SpecularFactor, AlphaMode = m.AlphaMode.ToString().ToUpperInvariant(),
            AlphaCutoff = m.AlphaCutoff, DoubleSided = m.DoubleSided, Ground = ground, Passable = passable,
            HeightTexture = m.HeightScale > 0 ? map(m.HeightTexture, TextureKind.Height) : null, HeightScale = m.HeightTexture.Present ? m.HeightScale : 0f,
            Foliage = m.Foliage ? new L2Foliage { Translucency = m.Translucency, Wind = m.Wind } : null,
            Tiling = GltfTilings.Define(m.Tiling, map),   // ours: passed through as the glTF gives it
        };
    }

    public static (GltfScene Scene, List<string> Files) Load(string package, IReadOnlyList<string> materialFiles)
    {
        var scene = new GltfScene();
        var files = new List<string>();
        var textureOf = new Dictionary<(string, bool, bool, bool, bool), int>();
        GltfTextureSlot Slot(L2MaterialMap? map)
        {
            if (map is null) return GltfTextureSlot.None;
            int image = files.IndexOf(map.File);
            if (image < 0) { image = files.Count; files.Add(map.File); scene.Images.Add(new GltfImage { Name = map.File, MimeType = "image/ktx2" }); }
            var key = (map.File, map.RepeatU, map.RepeatV, map.MirrorU, map.MirrorV);
            if (!textureOf.TryGetValue(key, out int t))
            {
                t = scene.Textures.Count; textureOf[key] = t;
                scene.Textures.Add(new GltfTexture(image, map.RepeatU, map.RepeatV, map.MirrorU, map.MirrorV));
            }
            return new GltfTextureSlot(t, map.TexCoord);
        }
        foreach (var f in materialFiles)
        {
            var m = L2Material.Read(File.ReadAllText(Path.Combine(package, f)), f);
            scene.Materials.Add(new GltfMaterial
            {
                Name = m.Name, BaseColorFactor = new Vector4(m.BaseColorFactor[0], m.BaseColorFactor[1], m.BaseColorFactor[2], m.BaseColorFactor[3]),
                BaseColorTexture = Slot(m.BaseColorTexture), MetallicFactor = m.MetallicFactor, RoughnessFactor = m.RoughnessFactor,
                MetallicRoughnessTexture = Slot(m.MetallicRoughnessTexture), NormalTexture = Slot(m.NormalTexture), NormalScale = m.NormalScale,
                OcclusionTexture = Slot(m.OcclusionTexture), OcclusionStrength = m.OcclusionStrength,
                EmissiveFactor = new Vector3(m.EmissiveFactor[0], m.EmissiveFactor[1], m.EmissiveFactor[2]), EmissiveTexture = Slot(m.EmissiveTexture),
                EmissiveStrength = m.EmissiveStrength, SpecularFactor = m.SpecularFactor,
                AlphaMode = m.AlphaMode switch { "MASK" => GltfAlphaMode.Mask, "BLEND" => GltfAlphaMode.Blend, _ => GltfAlphaMode.Opaque },
                AlphaCutoff = m.AlphaCutoff, DoubleSided = m.DoubleSided,
                HeightTexture = Slot(m.HeightTexture), HeightScale = m.HeightTexture is null ? 0f : m.HeightScale,
                Foliage = m.Foliage is not null, Translucency = m.Foliage?.Translucency ?? 0f, Wind = m.Foliage?.Wind ?? 0f,
                Tiling = GltfTilings.Load(m.Tiling, Slot),
            });
        }
        return (scene, files);
    }
}
