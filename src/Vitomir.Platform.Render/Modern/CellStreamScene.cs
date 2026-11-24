// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Modern/CellStreamScene.cs at b4185ef, reworked for the platform.
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using SDL;
using Vitomir.Platform.Render.Level;
using Vitomir.Formats.L2;
using static SDL.SDL3;

namespace Vitomir.Platform.Render.Modern;

/// <summary>
/// How far a streamed level is drawn and what it may hold (ours; graphics options). There is no far
/// clip and no fog wall: <see cref="ViewDistanceMetres"/> is how far the far versions are drawn, and
/// "whole map" (<see cref="WholeMap"/>) draws every one of them, the town to its last roof.
/// </summary>
internal sealed class ModernStreamSettings
{
    /// <summary>How far (metres) the blocks' far versions are drawn. Ignored with <see cref="WholeMap"/>.</summary>
    public float ViewDistanceMetres { get; set; } = 800f;
    /// <summary>Draw every far version however far (a big machine's setting).</summary>
    public bool WholeMap { get; set; }
    /// <summary>Blocks whose box is within this (metres) have their cells loaded and drawn instead of their far version.</summary>
    public float NearMetres { get; set; } = 120f;
    /// <summary>A cell's second level of detail is drawn from this far (metres) from its box.</summary>
    public float Lod1Metres { get; set; } = 90f;
    /// <summary>The margin (metres) a block or a level of detail keeps before it changes back.</summary>
    public float HysteresisMetres { get; set; } = 24f;
    /// <summary>The geometry arena's size (MB): every loaded cell's and far version's vertices and indices.</summary>
    public int GeometryBudgetMB { get; set; } = 512;
    /// <summary>The textures' budget (MB): the streamed maps' mips on the GPU (<see cref="TextureStreamer"/>).</summary>
    public int TextureBudgetMB { get; set; } = 256;
    /// <summary>How much geometry (MB) is copied to the GPU in one frame at most, so a load does not stall a frame.</summary>
    public int UploadMBPerFrame { get; set; } = 64;
    /// <summary>Whether a level's skyline is drawn (ours, <see cref="CellSkylineDraws"/>); off for measuring what it costs.</summary>
    public bool Skyline { get; set; } = true;
    /// <summary>How near the camera (metres) a skyline draw's box must come for it to cast the sun's shadow (ours; 0: never).</summary>
    public float SkylineShadowMetres { get; set; } = 150f;
    /// <summary>
    /// The skyline's level of detail (ours): a scale on the distance at which its parts change to their far
    /// version — 1 as built, 0.5 far versions twice as near (a laptop's low quality), 2.5 near versions two and
    /// a half times as far (ultra). Set by the quality presets (<see cref="ModernQualityPreset.SkylineDetail"/>).
    /// </summary>
    public float SkylineDetail { get; set; } = 1f;
    /// <summary>How far off (metres) a skyline part's far version is drawn; null: the level's own (ours; 0: never).</summary>
    public float? SkylineFarMetres { get; set; }

    /// <summary>
    /// The settings from the environment, for harnesses: <c>VITOMIR_VIEW_DISTANCE</c> (metres or
    /// <c>whole</c>), <c>VITOMIR_STREAM_NEAR</c>, <c>VITOMIR_LOD1</c>, <c>VITOMIR_GEOMETRY_MB</c>, <c>VITOMIR_SKYLINE=0</c>.
    /// </summary>
    public static ModernStreamSettings FromEnvironment() => FromEnvironment(null);

    /// <summary>A quality preset's settings (<see cref="ModernQualityPreset"/>; null: the defaults), then the environment's over them.</summary>
    public static ModernStreamSettings FromEnvironment(ModernQualityPreset? quality)
    {
        var s = new ModernStreamSettings();
        quality?.Apply(s);
        static float? F(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v
            && float.TryParse(v, CultureInfo.InvariantCulture, out float f) ? f : null;
        if (Environment.GetEnvironmentVariable("VITOMIR_VIEW_DISTANCE") is "whole" or "all") s.WholeMap = true;
        else if (F("VITOMIR_VIEW_DISTANCE") is { } metres) (s.ViewDistanceMetres, s.WholeMap) = (metres, false);
        s.NearMetres = F("VITOMIR_STREAM_NEAR") ?? s.NearMetres;
        s.Lod1Metres = F("VITOMIR_LOD1") ?? s.Lod1Metres;
        s.GeometryBudgetMB = (int)(F("VITOMIR_GEOMETRY_MB") ?? s.GeometryBudgetMB);
        s.TextureBudgetMB = (int)(F("VITOMIR_TEXTURE_MB") ?? s.TextureBudgetMB);
        s.Skyline = Environment.GetEnvironmentVariable("VITOMIR_SKYLINE") != "0";
        s.SkylineShadowMetres = F("VITOMIR_SKYLINE_SHADOW") ?? s.SkylineShadowMetres;
        s.SkylineFarMetres = F("VITOMIR_SKYLINE_FAR");
        return s;
    }
}

/// <summary>
/// **Ours (Phase 3): a streamed cell level's drawing.** The cells' <c>MESH</c>
/// buffers (two levels of detail each) and the blocks' far versions are read and decoded on a background
/// thread, copied into one geometry arena on the GPU a few megabytes a frame (<see cref="ArenaAllocator"/>),
/// and given back when they fall out of reach. Each frame (<see cref="Update"/>) the draws are chosen:
/// a block near the camera draws its cells, each at the level of detail its distance calls for; a block
/// further off, or one whose cells are not all in yet, draws its far version, out to the view distance
/// or, with "whole map", everywhere; the resident cell always. Whole cells and far versions outside the
/// view are left out before their draws are (the renderer then culls each draw to the view). The
/// modern renderer draws the result as any scene (<see cref="Scene"/>).
/// </summary>
internal sealed unsafe class CellStreamScene : IDisposable
{
    readonly Gpu gpu;
    readonly RenderLevel level;
    readonly float scale;
    readonly SDL_GPUBuffer* vertexBuffer, indexBuffer;
    readonly ArenaAllocator vertexArena, indexArena;
    readonly SDL_GPUTransferBuffer* transfer;
    readonly uint transferBytes;

    public ModernStreamSettings Settings { get; }
    public ModernScene Scene { get; }

    /// <summary>A drawable thing: a cell (two levels of detail) or a block's far version (one).</summary>
    sealed class Item
    {
        public required string Key;
        public required string File;
        public required Vector2 Min2, Max2;
        public Vector3 Min, Max;
        public bool IsFar;
        public int Block = -1;
        // Loaded state.
        public L2MeshBuffers? Mesh;            // decoded, waiting to be copied
        public (long Vertex, long VertexCount, long Index, long IndexCount)[] Ranges = [];
        public bool Resident;
        public bool Wanted;
        public bool TooBig;
        public int Lod = -1;
        /// <summary>What the last frame drew it as: 0 or 1 its own level of detail, 2 its block's far version, −1 nothing.</summary>
        public int DrawnAs = -1;
        public long Bytes;
        public long WaitingBytes;
        public double LoadMilliseconds;
    }

    readonly List<Item> cells = [];
    readonly List<Item> fars = [];
    readonly Dictionary<int, List<Item>> blockCells = [];
    readonly Dictionary<int, Item> blockFar = [];
    readonly List<(int Block, Vector2 Min, Vector2 Max)> blocks = [];
    Item? resident;
    /// <summary>The skyline's parts: always wanted, drawn at any distance (ours, <see cref="CellSkylineDraws"/>).</summary>
    readonly List<Item> skyline = [];
    HashSet<int> nearBlocks = [];
    readonly List<(long VStart, long VLen, long IStart, long ILen, int Frame)> quarantine = [];
    int frame;

    // The loader.
    readonly object gate = new();
    readonly Queue<Item> toLoad = new();
    readonly Queue<Item> loaded = new();
    readonly HashSet<Item> queued = [];
    Thread? worker;
    bool stopping;

    /// <summary>What the last frames did, for the streaming table.</summary>
    public int ResidentCells => cells.Count(c => c.Resident);
    public int ResidentFar => fars.Count(f => f.Resident);
    public long ResidentBytes => cells.Where(c => c.Resident).Sum(c => c.Bytes) + fars.Where(f => f.Resident).Sum(f => f.Bytes) + (resident?.Resident == true ? resident.Bytes : 0);
    public int DrawnCellsLod0 { get; private set; }
    public int DrawnCellsLod1 { get; private set; }
    public int DrawnFar { get; private set; }
    /// <summary>The skyline's parts and draws in the last frame's draw list (ours).</summary>
    public int DrawnSkylineParts { get; private set; }
    public int DrawnSkylineDraws { get; private set; }
    /// <summary>Of those parts, how many were drawn as their far version (ours).</summary>
    public int DrawnSkylineFar { get; private set; }
    /// <summary>The skyline's parts, and how many are in.</summary>
    public int SkylineParts => skyline.Count;
    public int ResidentSkyline => skyline.Count(s => s.Resident);
    public long DrawnTriangles { get; private set; }
    public int Loads { get; private set; }
    public int Unloads { get; private set; }
    /// <summary>Bytes of cells' geometry sent to the GPU since the stream began (ours: the frame log's).</summary>
    public long UploadedBytes { get; private set; }
    public double LoadMilliseconds { get; private set; }
    public double LastUploadMilliseconds { get; private set; }
    public int Pending { get { lock (gate) return toLoad.Count + loaded.Count; } }
    public int CellCount => cells.Count;
    public int FarCount => fars.Count;

    /// <summary>The level's cooked maps streamed by mip, or null for a level whose images come whole from its glTF.</summary>
    public TextureStreamer? Textures { get; }
    readonly int[][] materialImages;

    public CellStreamScene(Gpu gpu, RenderLevel level, float anisotropy, ModernStreamSettings? settings = null)
    {
        this.gpu = gpu;
        this.level = level;
        Settings = settings ?? ModernStreamSettings.FromEnvironment();
        scale = level.Manifest.Units.EngineUnitsPerMetre;
        long budget = (long)Settings.GeometryBudgetMB << 20;
        long vbytes = budget * 4 / 5, ibytes = budget / 5;
        vertexArena = new ArenaAllocator(vbytes / L2Vertex.Size);
        indexArena = new ArenaAllocator(ibytes / 4);
        var vinfo = new SDL_GPUBufferCreateInfo { usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX, size = (uint)(vertexArena.Capacity * L2Vertex.Size) };
        vertexBuffer = SDL_CreateGPUBuffer(gpu.Device, &vinfo);
        var iinfo = new SDL_GPUBufferCreateInfo { usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_INDEX, size = (uint)(indexArena.Capacity * 4) };
        indexBuffer = SDL_CreateGPUBuffer(gpu.Device, &iinfo);
        if (vertexBuffer == null || indexBuffer == null) throw new SdlException("SDL_CreateGPUBuffer (geometry arena)");
        transferBytes = (uint)Settings.UploadMBPerFrame << 20;
        var tinfo = new SDL_GPUTransferBufferCreateInfo { usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD, size = transferBytes };
        transfer = SDL_CreateGPUTransferBuffer(gpu.Device, &tinfo);
        // The materials: the cooked definitions and their maps streamed by mip when the level has them and the GPU
        // samples BC7 and BC5; else the glTF the level was cooked from, its images whole.
        GltfScene materials;
        if (level.Manifest.MaterialFiles is { Count: > 0 } defs && TextureStreamer.Supported(gpu)
            && Environment.GetEnvironmentVariable("VITOMIR_STREAM_TEXTURES") != "0")
        {
            (materials, var files) = CellMaterials.Load(level.Package, defs);
            Textures = new TextureStreamer(gpu, level.Package, files, (long)Settings.TextureBudgetMB << 20);
            Scene = new ModernScene(gpu, materials, vertexBuffer, indexBuffer, anisotropy, uploadImages: false);
            var textures = Textures;
            Scene.TextureSource = (image, _) => textures.Texture(image);
        }
        else
        {
            materials = level.AssetFile is { } asset && File.Exists(asset) ? GltfReader.Load(asset) : new GltfScene();
            if (level.Manifest.Skyline is { } sky && File.Exists(Path.Combine(level.Package, sky.Asset)))
            {
                while (materials.Materials.Count < sky.FirstMaterial) materials.Materials.Add(GltfMaterial.Default());
                CellSkylineDraws.AppendMaterials(materials, GltfReader.Load(Path.Combine(level.Package, sky.Asset)));
            }
            Scene = new ModernScene(gpu, materials, vertexBuffer, indexBuffer, anisotropy, uploadImages: true);
            // The glTF is kept for its materials only: its images are on the GPU now and its geometry is the cells'.
            foreach (var image in materials.Images) image.Rgba = null;
            materials.Primitives.Clear();
        }
        materialImages = [.. materials.Materials.Select(m => new[] { m.BaseColorTexture, m.MetallicRoughnessTexture, m.NormalTexture, m.OcclusionTexture, m.EmissiveTexture, m.HeightTexture }
            .Where(slot => slot.Present && slot.Texture < materials.Textures.Count).Select(slot => materials.Textures[slot.Texture].Image).Distinct().ToArray())];
        var b = level.Manifest.Bounds;
        Scene.SetBounds(new Vector3(b.Min[0], b.Min[1], b.Min[2]) * scale, new Vector3(b.Max[0], b.Max[1], b.Max[2]) * scale);
        // The level's baked light probes, when its package has them (ours): always resident, a few MB.
        if (L2ProbeGrid.ReadPackage(level.Package, level.Manifest) is { } probeGrid)
        {
            Probes = new ModernProbes(probeGrid) { Rooms = ModernProbeRoom.From(level.Manifest.Areas) };
            Scene.Probes = Probes;
        }
        Scene.Lights.AddRange(level.Lights.Select(l => new ModernPointLight(l.Position * scale, l.Colour * l.Intensity, l.Range * scale)));

        var grid = level.Manifest.Grid;
        int bc = grid?.BlockCells ?? 1;
        foreach (var e in level.Manifest.Cells.Where(c => c.Region == RenderLevel.CellRegion))
        {
            var item = new Item
            {
                Key = e.Id, File = e.File, Min2 = new(e.Bounds.Min[0], e.Bounds.Min[1]), Max2 = new(e.Bounds.Max[0], e.Bounds.Max[1]),
                Min = new Vector3(e.Bounds.Min[0], e.Bounds.Min[1], e.Bounds.Min[2]) * scale,
                Max = new Vector3(e.Bounds.Max[0], e.Bounds.Max[1], e.Bounds.Max[2]) * scale,
            };
            if (e.Resident == true) { resident = item; continue; }
            if (e.At is { Length: 2 } at && grid is not null)
            {
                item.Block = BlockKey(at[0] / bc, at[1] / bc);
                if (!blockCells.TryGetValue(item.Block, out var list)) blockCells[item.Block] = list = [];
                list.Add(item);
            }
            cells.Add(item);
        }
        foreach (var f in level.Manifest.Far ?? [])
        {
            var item = new Item
            {
                Key = f.Id, File = f.File, IsFar = true, Block = BlockKey(f.At[0], f.At[1]),
                Min2 = new(f.Bounds.Min[0], f.Bounds.Min[1]), Max2 = new(f.Bounds.Max[0], f.Bounds.Max[1]),
                Min = new Vector3(f.Bounds.Min[0], f.Bounds.Min[1], f.Bounds.Min[2]) * scale,
                Max = new Vector3(f.Bounds.Max[0], f.Bounds.Max[1], f.Bounds.Max[2]) * scale,
            };
            fars.Add(item);
            blockFar[item.Block] = item;
        }
        foreach (var p in Settings.Skyline ? level.Manifest.Skyline?.Parts ?? [] : [])
            skyline.Add(new Item
            {
                Key = p.Id, File = p.File, Min2 = new(p.Bounds.Min[0], p.Bounds.Min[1]), Max2 = new(p.Bounds.Max[0], p.Bounds.Max[1]),
                Min = new Vector3(p.Bounds.Min[0], p.Bounds.Min[1], p.Bounds.Min[2]) * scale,
                Max = new Vector3(p.Bounds.Max[0], p.Bounds.Max[1], p.Bounds.Max[2]) * scale,
            });
        foreach (var (key, list) in blockCells)
        {
            var min = list.Select(c => c.Min2).Aggregate(Vector2.Min);
            var max = list.Select(c => c.Max2).Aggregate(Vector2.Max);
            blocks.Add((key, min, max));
        }
        // Cells in no block (a level cooked as one cell) are their own.
        foreach (var c in cells.Where(c => c.Block < 0)) { c.Block = BlockKey(100000 + cells.IndexOf(c), 0); blockCells[c.Block] = [c]; blocks.Add((c.Block, c.Min2, c.Max2)); }
    }

    static int BlockKey(int column, int row) => column * 65536 + row;

    /// <summary>
    /// Loads everything wanted round <paramref name="camera"/> (engine units) before the first frame and
    /// copies it in, however long it takes — a level's opening, a still's harness.
    /// </summary>
    public void LoadNow(Vector3 camera, Matrix4x4 viewProj, int timeoutMs = 120_000)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            var cmd = SDL_AcquireGPUCommandBuffer(gpu.Device);
            Update(cmd, camera, viewProj);
            var fence = SDL_SubmitGPUCommandBufferAndAcquireFence(cmd);
            SDL_WaitForGPUFences(gpu.Device, true, &fence, 1);
            SDL_ReleaseGPUFence(gpu.Device, fence);
            if (Settled) return;
            Thread.Sleep(2);
        }
    }

    /// <summary>Whether a frame has been streamed yet (<see cref="LoadNow"/> is for before the first).</summary>
    public bool Started => frame > 0;

    /// <summary>
    /// Whether the last frame found the geometry arena too full for the next cell wanted: the view wants more
    /// than the budget holds, and the blocks whose cells cannot all come in go on drawing their far versions.
    /// </summary>
    public bool ArenaFull { get; private set; }

    /// <summary>Whether everything wanted is in and nothing is waiting — or all that can be, the arena being full.</summary>
    public bool Settled
    {
        get
        {
            lock (gate)
            {
                if (ArenaFull && loaded.Count > 0 && (toLoad.Count == 0 || waitingBytes > ReadAheadBytes)) return Textures is null || Textures.Wanting == 0;
                if (toLoad.Count > 0 || loaded.Count > 0 || queued.Count > 0) return false;
            }
            return cells.Concat(fars).Concat(skyline).All(i => i.Wanted == i.Resident || i.TooBig) && (resident is null || resident.Resident)
                   && (Textures is null || Textures.Wanting == 0);
        }
    }

    /// <summary>
    /// A frame's streaming, on <paramref name="cmd"/> before its render passes: decides what is wanted,
    /// copies in what the loader has finished (a few megabytes at most), gives back what is no longer
    /// wanted, and sets <see cref="Scene"/>'s draws for the camera at <paramref name="camera"/> (engine units).
    /// </summary>
    public void Update(SDL_GPUCommandBuffer* cmd, Vector3 camera, Matrix4x4 viewProj, float pixelsPerRadian = 800f)
    {
        frame++;
        var focus = new Vector2(camera.X, camera.Y) / scale;
        float hyst = Settings.HysteresisMetres;

        // 1. What is wanted: every cell of a near block, every far version in view distance, the resident cell.
        // A block is near within the near distance, and stays near a hysteresis further once it is (what
        // CellStreamPlan.Wanted answers, without its lists: nothing here is made anew each frame).
        var near = nearNext;
        near.Clear();
        foreach (var b in blocks)
        {
            float d = CellStreamPlan.Distance(focus, b.Min, b.Max);
            if (d <= Settings.NearMetres || (nearBlocks.Contains(b.Block) && d <= Settings.NearMetres + hyst)) near.Add(b.Block);
        }
        (nearBlocks, nearNext) = (near, nearBlocks);
        foreach (var c in cells) c.Wanted = near.Contains(c.Block);
        foreach (var f in fars)
        {
            float d = CellStreamPlan.Distance(focus, f.Min2, f.Max2);
            // With a skyline round the level its far versions go as far as the skyline does: no hole where the level stands (ours).
            f.Wanted = Settings.WholeMap || skyline.Count > 0 || d <= Settings.ViewDistanceMetres || (f.Resident && d <= Settings.ViewDistanceMetres + hyst);
        }
        if (resident is not null) resident.Wanted = true;
        foreach (var s in skyline) s.Wanted = true;
        var all = Everything();
        toQueue.Clear();
        foreach (var item in all) if (item.Wanted && !item.Resident && !item.TooBig) toQueue.Add(item);
        if (toQueue.Count > 1)
        {
            // Nearest first, those as near as each other in their own order (a frame with something to load may allocate).
            var ordered = toQueue.OrderBy(i => CellStreamPlan.Distance(focus, i.Min2, i.Max2)).ToList();
            toQueue.Clear(); toQueue.AddRange(ordered);
        }
        foreach (var item in toQueue) Queue(item);

        // 2. Give back what is no longer wanted (its ranges wait a few frames: frames in flight may still read them).
        foreach (var item in all) if (!item.Wanted && item.Resident) Release(item);
        for (int q = quarantine.Count - 1; q >= 0; q--)
        {
            if (frame - quarantine[q].Frame < 4) continue;
            vertexArena.Free(quarantine[q].VStart, quarantine[q].VLen);
            indexArena.Free(quarantine[q].IStart, quarantine[q].ILen);
            quarantine.RemoveAt(q);
        }

        // 3. Copy in what the loader has finished, within the frame's upload budget.
        Upload(cmd);

        // 4. The draws.
        var planes = ModernMath.FrustumPlanes(viewProj);
        // The frame's two lists are the same two every frame, emptied and filled again: a town's
        // casters are thousands of draws, and a list of them made anew each frame is a large array a frame.
        var draws = frameDraws;
        draws.Clear();
        // The sun's casters are chosen apart from the camera's frustum: a house behind the camera still
        // shades the street ahead, and each cascade keeps only what its own frustum meets (ours).
        var casters = frameCasters;
        casters.Clear();
        int lod0 = 0, lod1 = 0, far = 0;
        long triangles = 0;
        void Add(Item item, int lod)
        {
            var r = item.Ranges[lod];
            bool seen = ModernMath.BoxVisible(planes, item.Min, item.Max);
            var meshDraws = item.Mesh!.Lods[lod].Draws;
            for (int k = 0; k < meshDraws.Count; k++)
            {
                var d = meshDraws[k];
                var min = d.Min * scale; var max = d.Max * scale;
                var draw = new ModernScene.Draw((uint)(r.Index + d.FirstIndex), (uint)d.IndexCount, d.Material, min, max, (int)r.Vertex);
                casters.Add(draw);
                if (!seen) continue;
                draws.Add(draw);
                triangles += d.IndexCount / 3;
                // The maps this draw's surface wants at its distance: the screen's pixels a metre there.
                if (Textures is null || d.Material >= materialImages.Length || !ModernMath.BoxVisible(planes, min, max)) continue;
                float metres = Vector3.Distance(Vector3.Clamp(camera, min, max), camera) / scale;
                float ppm = pixelsPerRadian / MathF.Max(metres, 0.25f);
                foreach (int image in materialImages[d.Material]) Textures.Request(image, ppm, d.UvDensity);
            }
        }
        foreach (var c in cells) c.DrawnAs = -1;
        if (resident is { Resident: true }) Add(resident, 0);
        // The skyline, at any distance: each part the frustum meets, each of its draws it meets (ours).
        int skyParts = 0, skyDraws = draws.Count, skyFar = 0;
        float farFrom = (Settings.SkylineFarMetres ?? level.Manifest.Skyline?.FarFrom ?? 0f) * Settings.SkylineDetail * scale;
        float shadowReach = Settings.SkylineShadowMetres * scale;
        // What a skyline draw in view asks of the texture streamer: one delegate a frame, not one a part.
        Action<Vector3, Vector3, L2MeshDraw>? skySeen = Textures is null ? null : (min, max, d) =>
        {
            if (d.Material >= materialImages.Length) return;
            float metres = Vector3.Distance(Vector3.Clamp(camera, min, max), camera) / scale;
            foreach (int image in materialImages[d.Material]) Textures.Request(image, pixelsPerRadian / MathF.Max(metres, 0.25f), d.UvDensity);
        };
        foreach (var s in skyline)
        {
            if (!s.Resident) continue;
            int v = CellSkylineDraws.Version(camera, s.Min, s.Max, farFrom, s.Ranges.Length);
            // The skyline casts only near the camera, in view or not (CellSkylineDraws.Casters).
            CellSkylineDraws.Casters(s.Mesh!.Lods[v].Draws, scale, s.Ranges[v].Index, s.Ranges[v].Vertex, camera, shadowReach, casters);
            if (!ModernMath.BoxVisible(planes, s.Min, s.Max)) continue;
            skyParts++;
            skyFar += v;
            triangles += CellSkylineDraws.Add(s.Mesh!.Lods[v].Draws, scale, planes, s.Ranges[v].Index, s.Ranges[v].Vertex, draws, skySeen);
        }
        DrawnSkylineParts = skyParts; DrawnSkylineDraws = draws.Count - skyDraws; DrawnSkylineFar = skyFar;
        lodFrom[0] = 0f; lodFrom[1] = Settings.Lod1Metres;
        foreach (var (block, _, _) in blocks)
        {
            var list = blockCells[block];
            bool ownCells = near.Contains(block);
            for (int k = 0; ownCells && k < list.Count; k++) ownCells = list[k].Resident;
            if (ownCells)
            {
                foreach (var c in list)
                {
                    int lod = Math.Min(CellStreamPlan.Lod(CellStreamPlan.Distance(focus, c.Min2, c.Max2), c.Lod, lodFrom, hyst), c.Ranges.Length - 1);
                    c.Lod = lod;
                    c.DrawnAs = lod;
                    Add(c, lod);
                    if (lod == 0) lod0++; else lod1++;
                }
            }
            else if (blockFar.TryGetValue(block, out var f) && f.Resident && f.Wanted)
            {
                Add(f, 0); far++;
                foreach (var c in list) c.DrawnAs = 2;
            }
            else foreach (var c in list) { if (!c.Resident) continue; int lod = Math.Min(1, c.Ranges.Length - 1); Add(c, lod); c.DrawnAs = lod; lod1++; }
        }
        Scene.Draws = draws;
        // The sun's casters: every draw of the level's cells and far versions drawn this frame, in view or
        // not, and the skyline's near the camera; set only once the level's cells are in.
        Scene.Casters = casters;
        Textures?.Update(cmd);
        DrawnCellsLod0 = lod0; DrawnCellsLod1 = lod1; DrawnFar = far; DrawnTriangles = triangles;
        EnsureWorker();
    }

    /// <summary>What the cell under a point (engine units) was drawn as in the last frame: <c>lod0</c>, <c>lod1</c>, <c>far</c> or <c>none</c>.</summary>
    public string Describe(Vector3 point)
    {
        var p = new Vector2(point.X, point.Y) / scale;
        var grid = level.Manifest.Grid;
        Item? cell = null;
        if (grid is not null)
        {
            var (c, r) = grid.CellAt(p.X, p.Y);
            cell = cells.FirstOrDefault(i => i.Key == Formats.L2.L2Grid.CellId(c, r));
        }
        cell ??= cells.FirstOrDefault(i => p.X >= i.Min2.X && p.X <= i.Max2.X && p.Y >= i.Min2.Y && p.Y <= i.Max2.Y);
        return cell?.DrawnAs switch { 0 => "lod0", 1 => "lod1", 2 => "far", _ => "none" };
    }

    /// <summary>Every item there is, in <see cref="AllItems"/>'s order: made once (the level's items do not change), read each frame.</summary>
    Item[] Everything() => everything ??= [.. AllItems()];
    Item[]? everything;
    /// <summary>The frame's own lists, emptied and filled again each frame rather than made anew.</summary>
    readonly List<ModernScene.Draw> frameDraws = [], frameCasters = [];
    readonly List<Item> toQueue = [];
    readonly float[] lodFrom = new float[2];
    HashSet<int> nearNext = [];

    IEnumerable<Item> AllItems()
    {
        foreach (var c in cells) yield return c;
        foreach (var f in fars) yield return f;
        if (resident is not null) yield return resident;
        foreach (var s in skyline) yield return s;
    }

    void Queue(Item item)
    {
        lock (gate)
        {
            if (!queued.Add(item)) return;
            toLoad.Enqueue(item);
            Monitor.PulseAll(gate);
        }
    }

    void Release(Item item)
    {
        foreach (var r in item.Ranges) quarantine.Add((r.Vertex, r.VertexCount, r.Index, r.IndexCount, frame));
        item.Ranges = [];
        item.Resident = false;
        item.Mesh = null;
        item.Lod = -1;
        Unloads++;
    }

    void Upload(SDL_GPUCommandBuffer* cmd)
    {
        var watch = Stopwatch.StartNew();
        ArenaFull = false;
        long used = 0;
        byte* map = null;
        while (true)
        {
            Item? item;
            lock (gate) item = loaded.Count > 0 ? loaded.Peek() : null;
            if (item is null) break;
            if (!item.Wanted) { lock (gate) { loaded.Dequeue(); queued.Remove(item); waitingBytes -= item.WaitingBytes; Monitor.PulseAll(gate); } item.Mesh = null; continue; }
            var mesh = item.Mesh!;
            long bytes = mesh.Lods.Sum(l => l.Bytes);
            // What it takes in the transfer buffer, each part on its 16-byte boundary.
            long need = mesh.Lods.Sum(l => Align((long)l.Vertices.Length * L2Vertex.Size) + Align(l.Indices.Length * 4L));
            if (used > 0 && used + need > transferBytes) break;
            if (need > transferBytes) { lock (gate) { loaded.Dequeue(); queued.Remove(item); waitingBytes -= item.WaitingBytes; Monitor.PulseAll(gate); } item.TooBig = true; continue; }   // too big to ever copy in
            // Room in the arena, letting go of far-off things this frame does not draw if need be.
            var ranges = new (long, long, long, long)[mesh.Lods.Count];
            bool fits = true;
            for (int l = 0; l < mesh.Lods.Count; l++)
            {
                long v = vertexArena.Allocate(mesh.Lods[l].Vertices.Length), i = v < 0 ? -1 : indexArena.Allocate(mesh.Lods[l].Indices.Length);
                if (v < 0 || i < 0)
                {
                    if (v >= 0) vertexArena.Free(v, mesh.Lods[l].Vertices.Length);
                    for (int k = 0; k < l; k++) { vertexArena.Free(ranges[k].Item1, ranges[k].Item2); indexArena.Free(ranges[k].Item3, ranges[k].Item4); }
                    fits = false; break;
                }
                ranges[l] = (v, mesh.Lods[l].Vertices.Length, i, mesh.Lods[l].Indices.Length);
            }
            if (!fits) { ArenaFull = true; break; }   // the arena is full; what is wanted waits for what is let go
            lock (gate) { loaded.Dequeue(); queued.Remove(item); waitingBytes -= item.WaitingBytes; Monitor.PulseAll(gate); }
            if (map == null) map = (byte*)SDL_MapGPUTransferBuffer(gpu.Device, transfer, true);
            for (int l = 0; l < mesh.Lods.Count; l++)
            {
                var vb = MemoryMarshal.AsBytes(mesh.Lods[l].Vertices.AsSpan());
                if (vb.Length == 0) continue;
                vb.CopyTo(new Span<byte>(map + used, vb.Length));
                pendingCopies.Add((used, (uint)vb.Length, true, ranges[l].Item1 * L2Vertex.Size));
                used += Align(vb.Length);
                var ib = MemoryMarshal.AsBytes(mesh.Lods[l].Indices.AsSpan());
                ib.CopyTo(new Span<byte>(map + used, ib.Length));
                pendingCopies.Add((used, (uint)ib.Length, false, ranges[l].Item3 * 4));
                used += Align(ib.Length);
            }
            item.Ranges = ranges;
            item.Resident = true;
            item.Bytes = bytes;
            // The decoded vertices are not kept: only the draws are needed from now on.
            item.Mesh = new L2MeshBuffers { Lods = [.. mesh.Lods.Select(l => new L2MeshLod { Error = l.Error, Draws = l.Draws })] };
            Loads++;
            LoadMilliseconds += item.LoadMilliseconds;
        }
        if (map != null)
        {
            UploadedBytes += used;
            SDL_UnmapGPUTransferBuffer(gpu.Device, transfer);
            var pass = SDL_BeginGPUCopyPass(cmd);
            foreach (var (src, length, vertex, dst) in pendingCopies)
            {
                var from = new SDL_GPUTransferBufferLocation { transfer_buffer = transfer, offset = (uint)src };
                var to = new SDL_GPUBufferRegion { buffer = vertex ? vertexBuffer : indexBuffer, offset = (uint)dst, size = length };
                SDL_UploadToGPUBuffer(pass, &from, &to, false);
            }
            SDL_EndGPUCopyPass(pass);
            pendingCopies.Clear();
        }
        LastUploadMilliseconds = watch.Elapsed.TotalMilliseconds;
    }

    readonly List<(long Src, uint Length, bool Vertex, long Dst)> pendingCopies = [];

    /// <summary>How much decoded geometry may wait to be copied in before the reader stops (bytes).</summary>
    const long ReadAheadBytes = 96L << 20;
    long waitingBytes;

    static long Align(long n) => (n + 15) & ~15L;

    void EnsureWorker()
    {
        if (worker is not null) return;
        worker = new Thread(Work) { IsBackground = true, Name = "cell stream (drawing)", Priority = ThreadPriority.BelowNormal };
        worker.Start();
    }

    void Work()
    {
        while (true)
        {
            Item item;
            lock (gate)
            {
                // Read ahead of the copying by a bounded amount: a full arena must not fill memory with cells waiting.
                while ((toLoad.Count == 0 || waitingBytes > ReadAheadBytes) && !stopping) Monitor.Wait(gate);
                if (stopping) return;
                item = toLoad.Dequeue();
            }
            var watch = Stopwatch.StartNew();
            try
            {
                var cell = L2Cell.ReadFile(Path.Combine(level.Package, item.File), t => t == L2Mesh.Tag);
                var mesh = cell.Find(L2Mesh.Tag) is { Version: L2MeshBuffers.Version2 } m ? L2MeshBuffers.Decode(m) : new L2MeshBuffers();
                foreach (var lod in mesh.Lods)
                {
                    var v = lod.Vertices;
                    for (int i = 0; i < v.Length; i++) v[i].Position *= scale;
                }
                if (mesh.Lods.Count == 0) mesh.Lods.Add(new L2MeshLod());
                item.Mesh = mesh;
            }
            catch (Exception e) when (e is IOException or Formats.NodFormatException)
            {
                Console.Error.WriteLine($"stream: {item.File}: {e.Message}");
                item.Mesh = new L2MeshBuffers { Lods = [new L2MeshLod()] };
            }
            item.LoadMilliseconds = watch.Elapsed.TotalMilliseconds;
            item.WaitingBytes = item.Mesh.Lods.Sum(l => l.Bytes);
            lock (gate) { loaded.Enqueue(item); waitingBytes += item.WaitingBytes; }
        }
    }

    /// <summary>The level's light probes (ours), or null when its package has none.</summary>
    public ModernProbes? Probes { get; }

    public void Dispose()
    {
        lock (gate) { stopping = true; Monitor.PulseAll(gate); }
        worker?.Join(2000);
        Scene.Dispose();
        Probes?.Dispose();
        Textures?.Dispose();
        SDL_ReleaseGPUTransferBuffer(gpu.Device, transfer);
        SDL_ReleaseGPUBuffer(gpu.Device, vertexBuffer);
        SDL_ReleaseGPUBuffer(gpu.Device, indexBuffer);
    }
}
