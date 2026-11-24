// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The Classic snapshot of a place's frame, filled from the engine.
using System.Numerics;
using Vitomir.Engine.Explosions;
using Vitomir.Engine.Scene;
using Vitomir.Engine.Ui;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>
/// **The producer of a place's <see cref="ClassicFrame"/>**: run after the frame's game side
/// (<see cref="WorldFrame.Frame"/>, which steps, walks, finds the mirrors and poses), it reads everything the old 3D renderer
/// read of the game until then — the picture's layout and camera, the scene (<see cref="SceneSnapshot"/>), the sky, the
/// shadows worked out for the eye and for the mirror, the particles, the light blobs on their surfaces, the decals, the
/// halos (stepped here, as the old renderer stepped them), the click ring, the mirror and the walk — into one of two
/// frames, and publishes it. The renderer then reads that frame and nothing of the game.
/// <para>It writes nothing of the game but what the old renderer wrote at the same points, in the same order: materials
/// loaded into the place's cache when first needed (a shell's, a light's blob, a halo's, a decal's; the ring's and the
/// shadows' when it is made) and a film's repainted pictures taken. The old renderer's <c>FrameRenderer</c> fills it
/// before each draw; the Classic module can fill it after its own <see cref="WorldFrame.Frame"/>.</para>
/// </summary>
public sealed class WorldSnapshot
{
    readonly Location location;
    readonly WorldFrame game;
    readonly ClassicFrames frames = new();
    readonly Shadows shadows = new(), mirrorShadows = new();
    readonly int blob, silhouette;
    readonly Dictionary<string, int> blobMaterial = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<(int Sector, int Group), int> surfaceMaterial = [];
    readonly Dictionary<string, int> haloMaterial = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, int> decalMaterial = new(StringComparer.OrdinalIgnoreCase);
    readonly ClassicLevel level;
    Sky? skySent;
    ClassicSky? sky;
    int skies;
    int eyeHint = -1;

    /// <param name="poses">The scene's poses (the frame's game side poses them, <see cref="WorldFrame.Poses"/>).</param>
    public WorldSnapshot(Location location, WorldFrame game, ScenePoses poses)
    {
        this.location = location;
        this.game = game;
        // In the old renderer's order of making (FrameRenderer's constructor): the blobs' `lightblob`, the halos' clock, the
        // shadows' two pictures. (Its world renderer's first catch-up ran before it was told its things: no shell then.)
        Scene = new SceneSnapshot(location.Mesh, location.Materials, poses, location.Styles)
        { Fog = location.Fog, Fade = t => StealthFade.Fade(t, location),                     // the stealth fade
          SeeThrough = location.SeeThrough };                                                // what is in the camera's way (ours)
        // The engine loads `lightblob` at start-up.
        BlobMaterial(new DynamicLight());
        Halos.Begin(location.Milliseconds);
        blob = location.Materials.Named("shadowblob") ?? -1;         // `pDropShadowMat`
        silhouette = location.Materials.Named("silshadow") ?? -1;    // `pSilhouetteMat`
        level = Level(location.Level, location.Mesh);
        Lightmaps = [.. location.Level.Lightmaps.Select(Lightmap)];
        TakeSky();
    }

    /// <summary>The place's sky as last sent (a resource; a new one when the place's sky changes), or null for none.</summary>
    public ClassicSky? Sky => sky;

    /// <summary>The sky the place has now (a chapter swap's at once; SkySwap), sent when it changes.</summary>
    void TakeSky()
    {
        if (ReferenceEquals(skySent, location.Sky) && (skySent is not null || skies > 0)) return;
        skySent = location.Sky;
        sky = skySent is null ? null : SkyOf(skySent, ++skies);
    }

    public Location Location => location;
    /// <summary>The scene's half (the mesh, its materials, its things).</summary>
    public SceneSnapshot Scene { get; }
    /// <summary>The halos (lanterns, lamps, the flashlight; stepped as each frame is filled, by the interface's clock).</summary>
    public Halos Halos { get; } = new();
    /// <summary>The level's lightmaps, RGBA (a resource, sent once).</summary>
    public IReadOnlyList<ClassicPicture> Lightmaps { get; }
    /// <summary>The last frame published.</summary>
    public ClassicFrame Front => frames.Front;
    /// <summary>How long the last <see cref="Fill"/> took in all (milliseconds), and the bytes it copied (vertices, things, lists).</summary>
    public double LastFillMs { get; private set; }
    public long LastBytes { get; private set; }
    /// <summary>Of <see cref="LastFillMs"/>, what working the shadows out took (the world's and the mirror's: the old renderer's work, not a copy).</summary>
    public double LastShadowsMs { get; private set; }

    /// <summary>
    /// Whose fade and flash the frame carries (<see cref="ClassicFrame.Screen"/>): the game camera's (<c>WorldCamera.Effects</c>),
    /// handed in by whoever drives the frame; null carries none.
    /// </summary>
    public ScreenEffects? Effects { get; set; }

    /// <summary>The click ring's material, loaded now (the engine loads <c>pWalkTargetMat</c> as the world comes up).</summary>
    public int RingMaterial() => location.Materials.Get(WalkTarget.Material);

    /// <summary>
    /// **The frame's snapshot**, for a target <paramref name="width"/> by <paramref name="height"/> drawn from
    /// <paramref name="camera"/>, after <see cref="WorldFrame.Frame"/>. <paramref name="cellWorld"/>: a cell level's world is
    /// drawn under the things (its rows stay 1.1's); <paramref name="castShadows"/> false: the cell level's things cast the sun's
    /// own; <paramref name="ring"/>: the frame draws a click ring. Returns the frame published.
    /// </summary>
    public ClassicFrame Fill(FreeCamera camera, int width, int height, bool cellWorld = false, bool castShadows = true, bool ring = true)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var f = frames.Next();
        long bytes = 0;
        var lay = WorldFrame.Lay(camera, width, height, game.ViewHeight, cellWorld);
        f.Layout = new ClassicLayout(lay.TargetWidth, lay.TargetHeight, lay.Width, lay.Height, lay.Stretch, lay.Rows, lay.Drawn,
                                     lay.Columns.Drawn, lay.Columns.Magnify, lay.X0, lay.Cols, lay.Projection, lay.ViewProjection);
        f.Camera = new ClassicCamera(camera.View, camera.RotationView, camera.Position,
                                     camera.RotationView * WorldFrame.Viewport(camera, lay.Cols, lay.Height, lay.Rows, ClassicSky.FarPlane,
                                                                               lay.Columns.Magnify, lay.Stretch, lay.Drawn));
        f.Seconds = game.Sim.Seconds;
        f.InterfaceSeconds = game.InterfaceSeconds;
        // The camera's fade and flash as they stand after the frame's game side (stepped with the camera).
        f.Screen = Effects is { } fx ? new ClassicScreen(fx.FadeLevel, fx.FlashIntensity, fx.FlashColor) : ClassicScreen.None;
        f.Level = level;
        // The sky the place has now (a chapter swap's at once; SkySwap), posed while it shows.
        TakeSky();
        f.Sky = sky;
        f.SkyOn = game.SkyOn && location.Sky is not null;
        f.SkyWindow = game.SkyWindow;
        if (f.SkyOn)
        {
            int n = location.Sky!.Model.Vertices.Length;
            if (f.SkyPositions.Length != n) f.SkyPositions = new Vector3[n];
            location.Sky.Pose(f.SkyPositions);
            bytes += n * 12L;
        }
        // The scene: the materials' clock (the slides on the interface's clock, DrawClocks), the catch-up, the poses.
        var scene = f.Scene;
        Scene.Fill(scene, (float)(location.Materials.ClockMs / 1000.0));
        bytes += Scene.LastBytes;
        Ring(f.Ring, ring && game.RingShown);
        bytes += Particles(f.Particles, location.Particles);
        bytes += Blobs(f, location.Lights, location.Level);
        bytes += HalosOf(f, camera);
        bytes += Decals(f, location.Decals);
        long s0 = System.Diagnostics.Stopwatch.GetTimestamp();
        f.Shadows.Cast = castShadows;
        if (castShadows) bytes += ShadowsOf(f.Shadows, shadows, camera.Position, game.SeesAll ? null : game.Walked, mirror: false);
        else Clear(f.Shadows);
        // The mirrors in view, found with the walk (a frame that drew no world keeps the last search's, unread).
        var m = game.Mirrored ? game.Mirror : null;
        f.Mirror.Seen = m is not null;
        f.MirrorShadows.Cast = m is not null;
        if (m is not null)
        {
            var r = m.Rect.Intersect(ScreenRect.Full);
            (f.Mirror.X0, f.Mirror.Y0, f.Mirror.X1, f.Mirror.Y1) = (r.X0, r.Y0, r.X1, r.Y1);
            f.Mirror.View = m.View;
            f.Mirror.ViewProjection = m.ViewProjection;
            f.Mirror.Eye = Vector3.Transform(Vector3.Zero, Matrix4x4.Invert(m.View, out var inverse) ? inverse : Matrix4x4.Identity);
            // The shadows are laid on every surface (the mirror's world is not cut to a walk here).
            bytes += ShadowsOf(f.MirrorShadows, mirrorShadows, f.Mirror.Eye, null, mirror: true);
        }
        else Clear(f.MirrorShadows);
        LastShadowsMs = System.Diagnostics.Stopwatch.GetElapsedTime(s0).TotalMilliseconds;
        f.Reflections = game.Reflections;
        f.Walked = game.Walked is not null && !game.SeesAll;   // seeing through: no sector left out (ours, WorldFrame.SeesAll)
        int sectors = location.Level.Sectors.Length;
        if (f.SectorSeen.Length != sectors) f.SectorSeen = new bool[sectors];
        for (int i = 0; i < sectors; i++) f.SectorSeen[i] = game.View.Sees(i);
        Scene.Finish(scene);
        LastBytes = bytes;
        LastFillMs = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        return frames.Publish();
    }

    static ClassicSky SkyOf(Sky sky, int id)
    {
        var model = sky.Model;
        var vertices = new SceneVertex[model.Vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
            vertices[i] = new SceneVertex { Position = model.Vertices[i].Position, Uv = model.Vertices[i].Uv, Color = 0xffffffff };
        var indices = new List<uint>();
        var groups = new List<(int, uint, uint)>();
        foreach (var g in model.Groups)
        {
            if (g.Material < 0 || g.Material >= sky.Materials.Length || g.NumFaces == 0) continue;
            uint first = (uint)indices.Count;
            for (int f = 0; f < g.NumFaces * 3; f++) indices.Add((uint)(g.FirstVertex + model.Indices[g.FirstFace * 3 + f]));
            groups.Add((sky.Materials[g.Material], first, (uint)indices.Count - first));
        }
        return new ClassicSky { Id = id, Vertices = vertices, Indices = [.. indices], Groups = [.. groups] };
    }

    static ClassicPicture Lightmap(NilLightmap lm)
    {
        var rgba = new byte[lm.Width * lm.Height * 4];
        for (int p = 0; p < lm.Pixels.Length; p++)
        {
            var (r, g, b) = lm.Rgb(p);
            rgba[p * 4] = r; rgba[p * 4 + 1] = g; rgba[p * 4 + 2] = b; rgba[p * 4 + 3] = 255;
        }
        return new ClassicPicture(lm.Width, lm.Height, rgba);
    }

    /// <summary>
    /// The reflection pass's view of the level (ReflectionPass's constructor until then): each level batch's sector by the source
    /// the mesh builder gives it (<c>sector {si} group {gi}</c>), whether its group is a mirror (Reflect, 0x8000), and the
    /// Nodraw mirrors' glass, which has no batch, as triangles.
    /// </summary>
    static ClassicLevel Level(NilLevel level, SceneMesh mesh)
    {
        var batches = mesh.Batches;
        var sector = new int[batches.Count];
        var reflective = new bool[batches.Count];
        for (int i = 0; i < batches.Count; i++)
        {
            sector[i] = -1;
            var b = batches[i];
            if (b.Layer != BatchLayer.Level || Parse(b.Source) is not var (si, gi) || si >= level.Sectors.Length
                || gi >= level.Sectors[si].Groups.Length) continue;
            sector[i] = si;
            reflective[i] = (level.Sectors[si].Groups[gi].SurfaceFlags & NilGroup.FlagReflect) != 0;
        }
        var tris = new List<SceneVertex>();
        foreach (var s in level.Sectors)
            foreach (var g in s.Groups)
            {
                if ((g.SurfaceFlags & NilGroup.FlagReflect) == 0 || (g.SurfaceFlags & NilGroup.FlagNodraw) == 0) continue;
                for (int k = 0; k < g.NumSurfaces; k++)
                {
                    var sf = s.Surfaces[g.FirstSurface + k];
                    for (int i = 1; i + 1 < sf.NumVerts; i++)
                        foreach (int j in new[] { 0, i, i + 1 })
                            tris.Add(new SceneVertex { Position = s.Vertices[s.SectorVertices[sf.FirstVertex + j].Vertex], Color = 0xffffffff });
                }
            }
        return new ClassicLevel { BatchSector = sector, BatchReflective = reflective, Glass = [.. tris] };
    }

    static (int, int)? Parse(string source)
    {
        // "sector {si} group {gi}" (SceneMesh.AddLevel).
        var w = source.Split(' ');
        return w.Length == 4 && w[0] == "sector" && w[2] == "group" && int.TryParse(w[1], out int s) && int.TryParse(w[3], out int g)
            ? (s, g) : null;
    }

    void Ring(ClassicRing into, bool shown)
    {
        var marker = game.Sim.WalkTarget;
        into.Shown = shown && marker is { Active: true };
        if (!into.Shown) return;
        into.Colour = marker.Colour;
        var corners = marker.Corners();
        for (int i = 0; i < into.Corners.Length; i++) into.Corners[i] = new SceneVertex { Position = corners[i].Position, Uv = corners[i].Uv };
    }

    static long Particles(ClassicParticles into, Particles.ParticleSystem? from)
    {
        into.Sprites.Clear(); into.Quads.Clear(); into.Streaks.Clear(); into.Batches.Clear();
        if (from is null) return 0;
        into.Sprites.AddRange(from.Sprites);
        into.Quads.AddRange(from.Quads);
        into.Streaks.AddRange(from.Streaks);
        foreach (var b in from.Batches) into.Batches.Add(new ClassicParticleBatch(b.Material, b.First, b.Count, b.Run));
        return from.Sprites.Count * 20L + from.Quads.Count * 96L + from.Streaks.Count * 32L + from.Batches.Count * 16L;
    }

    /// <summary>
    /// The light blobs gathered with the walk (WorldFrame.Frame), each laid on its surface: the surface's own fan of
    /// points in the order the level mesh has them, with the blob's coordinates from the foot of the light
    /// (<see cref="DynamicLights.BlobUv"/>) and the wall's own; the blob's material (its light's, or `lightblob`) and the
    /// wall's (−1 where its plan has no slot for a blob, LightBlobRenderer's summary).
    /// </summary>
    long Blobs(ClassicFrame f, DynamicLights? lights, NilLevel level)
    {
        f.Blobs.Clear();
        f.BlobPoints.Clear();
        if (lights is null || lights.Blobs.Count == 0) return 0;
        foreach (var b in lights.Blobs)
        {
            int blobMat = BlobMaterial(b.Light), surfMat = SurfaceMaterial(level, b.Sector, b.Surface);
            var sector = level.Sectors[b.Sector];
            var surface = sector.Surfaces[b.Surface];
            var normal = sector.SurfacePlanes[surface.Plane].Normal;
            int first = f.BlobPoints.Count;
            for (int i = 0; i < surface.NumVerts; i++)
            {
                var sv = sector.SectorVertices[surface.FirstVertex + i];
                var p = sector.Vertices[sv.Vertex];
                f.BlobPoints.Add(new SceneVertex { Position = p, Uv = DynamicLights.BlobUv(b, normal, p), LightmapUv = sv.Uv });
            }
            f.Blobs.Add(new ClassicBlob(blobMat, surfMat, lights.SurfaceSeen(b.Sector), SceneVertex.FromBgra(DynamicLights.BlobColor(b)),
                                        first, surface.NumVerts));
        }
        return f.Blobs.Count * 24L + f.BlobPoints.Count * 32L;
    }

    /// <summary>The blob's texture: the light's material, or `lightblob`; −1 when it does not load.</summary>
    int BlobMaterial(DynamicLight light)
    {
        string name = light.Material.Length > 0 ? light.Material : DynamicLight.Blob;
        if (blobMaterial.TryGetValue(name, out int have)) return have;
        int index = location.Materials.Named(name) ?? -1;
        blobMaterial[name] = index;
        return index;
    }

    /// <summary>
    /// The wall's material, or −1 for a surface whose plan has no slot for a blob (masked, alpha-textured, fully lit, Trans
    /// 33/66, a material with <c>pass</c> lines) or that is not drawn at all.
    /// </summary>
    int SurfaceMaterial(NilLevel level, int sectorIndex, int surfaceIndex)
    {
        var sector = level.Sectors[sectorIndex];
        int group = sector.Surfaces[surfaceIndex].Group;
        if (surfaceMaterial.TryGetValue((sectorIndex, group), out int have)) return have;
        var g = sector.Groups[group];
        int index = -1;
        const uint none = NilGroup.FlagSky | NilGroup.FlagNodraw | NilGroup.FlagTrans33 | NilGroup.FlagTrans66;
        var materials = location.Materials;
        if ((g.SurfaceFlags & none) == 0 && g.Material >= 0 && g.Material < level.Materials.Length)
        {
            int m = materials.Get("materials/" + level.Materials[g.Material]);
            var mat = materials.Items[m];
            var t = mat.Traits;
            if (!mat.HasPasses && !t.Masked && !t.AlphaTexture && !t.FullyLit) index = m;
        }
        surfaceMaterial[(sectorIndex, group)] = index;
        return index;
    }

    /// <summary>
    /// The halos, stepped by the interface's clock from the eye's sector (as the old renderer stepped them), their materials
    /// found (one met for the first time is made drawable at once, as the old renderer's catch-up did), and each halo's place,
    /// size and colour; the twinkles first, as the world draw draws them.
    /// </summary>
    long HalosOf(ClassicFrame f, FreeCamera camera)
    {
        var into = f.Halos;
        into.Twinkles.Clear();
        into.Halos.Clear();
        into.TwinkleMaterial = -1;
        int eyeSector = eyeHint = location.Solid is { } solid ? solid.SectorAt(camera.Position, eyeHint) : -1;
        Halos.Step(location, camera.Position, eyeSector, f.InterfaceSeconds);
        if (Halos.Drawn.Count == 0 && Halos.Twinkles.Count == 0) return 0;
        bool fresh = false;
        foreach (var halo in Halos.Drawn)
            if (HaloMaterial(halo.Material) is int m and >= 0 && m >= f.Scene.Drawable) fresh = true;
        if (Halos.Twinkles.Count > 0 && HaloMaterial(Halos.TwinkleMaterial) is int tm and >= 0 && tm >= f.Scene.Drawable) fresh = true;
        if (fresh) Scene.Sync(f.Scene);
        if (Halos.Twinkles.Count > 0 && HaloMaterial(Halos.TwinkleMaterial) is int twinkle and >= 0)
        {
            into.TwinkleMaterial = twinkle;
            into.TwinkleColour = Halos.TwinkleColour();
            into.TwinkleHalf = Halos.TwinkleSize * Halos.Twinkle;
            into.Twinkles.AddRange(Halos.Twinkles);
        }
        foreach (var halo in Halos.Drawn)
        {
            int m = HaloMaterial(halo.Material);
            if (m < 0) continue;
            into.Halos.Add(new ClassicHalo(m, halo.Thing.Position, halo.Size, (halo.Thing.RenderFlags & Halos.FluoroFlag) != 0, Halos.Shown(halo)));
        }
        return into.Twinkles.Count * 12L + into.Halos.Count * 28L;
    }

    int HaloMaterial(string name)
    {
        if (haloMaterial.TryGetValue(name, out int have)) return have;
        return haloMaterial[name] = location.Materials.Named(name) ?? -1;
    }

    /// <summary>The decals on their surfaces' lists (<see cref="Explosions.Decals.Live"/>), each with its material (−1: none), colour and polygon.</summary>
    long Decals(ClassicFrame f, Decals? decals)
    {
        f.Decals.Clear();
        f.DecalPoints.Clear();
        if (decals is null) return 0;
        foreach (var d in decals.Live)
        {
            int material = DecalMaterial(d.Material);
            int first = f.DecalPoints.Count;
            for (int i = 0; i < d.Points.Length; i++) f.DecalPoints.Add(new SceneVertex { Position = d.Points[i], Uv = d.Uvs[i] });
            f.Decals.Add(new ClassicDecal(material, d.Color, first, d.Points.Length));
        }
        return f.Decals.Count * 28L + f.DecalPoints.Count * 32L;
    }

    int DecalMaterial(string name)
    {
        if (decalMaterial.TryGetValue(name, out int have)) return have;
        int index = location.Materials.Named(name) ?? -1;
        decalMaterial[name] = index;
        return index;
    }

    /// <summary>
    /// This frame's shadows (ShadowRenderer.Upload until then): the maxed-out preset whatever the screens say
    /// and the console's `shadow`, cut to the walk's sectors when there is a walk, the unseen left out (the stealth
    /// fade); for the mirror, its rule for which things cast and the reflected eye.
    /// </summary>
    long ShadowsOf(ClassicShadows into, Shadows from, Vector3 eye, PortalView? view, bool mirror)
    {
        Clear(into);
        into.BlobMaterial = blob >= 0 ? blob : -2;
        into.SilhouetteMaterial = silhouette >= 0 ? silhouette : -2;
        from.Settings = ShadowConsole.Live();
        from.MaxLights = DetailPresets.Maxed.MaxThingLights;
        from.SectorDrawn = view is null ? null : view.Sees;
        from.Unseen = StealthFade.Hides(location);
        from.Frame(location, location.Mesh, location.Drawn, eye, mirror);
        foreach (var p in from.Polys)
        {
            int first = into.Points.Count;
            for (int i = 0; i < p.Points.Length; i++) into.Points.Add(new ClassicShadowPoint(p.Points[i], p.Uvs[i], p.Shade[i]));
            into.Pieces.Add(new ClassicShadowPiece(p.Texture, p.Bitmap, first, p.Points.Length));
        }
        int n = from.BitmapCount, size = Shadows.BitmapSize * Shadows.BitmapSize;
        while (into.Bitmaps.Count < n) into.Bitmaps.Add(new byte[size]);
        for (int b = 0; b < n; b++) Buffer.BlockCopy(from.Bitmaps[b], 0, into.Bitmaps[b], 0, size);
        into.BitmapCount = n;
        return into.Pieces.Count * 16L + into.Points.Count * 24L + (long)n * size;
    }

    static void Clear(ClassicShadows s)
    {
        s.Pieces.Clear();
        s.Points.Clear();
        s.BitmapCount = 0;
    }
}
