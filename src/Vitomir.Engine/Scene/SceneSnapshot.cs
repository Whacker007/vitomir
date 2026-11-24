// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The Classic snapshot's scene, filled from the engine.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Scene;

/// <summary>
/// **The producer of a <see cref="ClassicScene"/>**: a mesh of things (and a level), read after the frame's game side has
/// posed and lit it (<see cref="ScenePoses"/>, <c>WorldFrame.Frame</c>), written into the snapshot the old world renderer
/// draws from — so the renderer reads no <see cref="SceneMesh"/>, <see cref="MaterialCache"/>, <see cref="LightStyles"/> or
/// <see cref="WorldThing"/> of its own (earlier, it did, <c>WorldRenderer</c>). It **reads the game and writes nothing of
/// it**, but for the two things the old renderer did there, at the same points: a thing's shell material is loaded into
/// the place's cache the first time a thing names it (1.1 at <c>SetShell</c>), and a film's repainted pictures
/// are taken off the cache's list (<see cref="MaterialCache.Repainted"/>).
/// <para>Resources go by id, once: the mesh at each revision, each material as the cache takes it on with its pictures.
/// The rest is the frame's: each material's frame, the 32 styles, the fog, each thing's placement, alpha, fade, flags, shell
/// and glow, each thing batch's named-shape bit, and the posed and relit vertices.</para>
/// </summary>
public sealed class SceneSnapshot
{
    readonly SceneMesh mesh;
    readonly MaterialCache materials;
    readonly ScenePoses poses;
    readonly List<ClassicMaterial> sent = [];
    int drawable;
    ClassicMesh? meshSent;
    int[] thingBatches = [];
    readonly Dictionary<string, int?> shellMaterials = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<WorldThing, Vector3> lit = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<WorldThing, int> first = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<(int Sector, int Surface), (int Batch, int Run)> runs = [];

    public SceneSnapshot(SceneMesh mesh, MaterialCache materials, ScenePoses poses, LightStyles styles)
    {
        this.mesh = mesh;
        this.materials = materials;
        this.poses = poses;
        Styles = styles;
    }

    public SceneMesh Mesh => mesh;
    public MaterialCache Materials => materials;
    /// <summary>The scene's poses (whose <see cref="ScenePoses.Things"/> the batches name, and whose glow tints them).</summary>
    public ScenePoses Poses => poses;
    /// <summary>The light styles the level's layers and the glow maps follow: the place's, or the spare a pass was handed.</summary>
    public LightStyles Styles { get; set; }
    public FogSettings Fog { get; set; }
    /// <summary>The stealth fade (<c>StealthFade.Fade</c>), or null for none.</summary>
    public Func<WorldThing, float>? Fade { get; set; }
    /// <summary>What the frame draws see-through for being in the camera's way (ours; the place's), or null for nothing.</summary>
    public SeeThrough? SeeThrough { get; set; }
    /// <summary>Whether <see cref="ClassicThing.Identity"/> names the first thing in the list that is the same (a pass drawing one thing alone).</summary>
    public bool Identities { get; set; }
    /// <summary>How long the last <see cref="Fill"/> took, in milliseconds, and how many bytes of vertices and things it copied.</summary>
    public double LastFillMs { get; private set; }
    public long LastBytes { get; private set; }

    /// <summary>
    /// The shell materials the things have been given, loaded before the frame (the old renderer's <c>ResolveShells</c>): 1.1
    /// loads one when <c>SetShell</c> names it; the script side has already asked the same cache by
    /// then, so this is normally a lookup.
    /// </summary>
    public void ResolveShells()
    {
        var things = poses.Things;
        for (int i = 0; i < things.Count; i++)      // by index: the list is an interface, and a frame allocates nothing
            if (things[i]?.Fx?.Shell is { } name && !shellMaterials.ContainsKey(name))
                shellMaterials[name] = materials.Named(name);
    }

    /// <summary>
    /// **The frame's scene into <paramref name="into"/>**, in the old renderer's order (its <c>Sync</c>, then its
    /// <c>UploadAnimated</c>): the shells resolved, the materials the cache took on since the last fill sent with their
    /// pictures, a film's repainted pictures, the mesh at a new revision, then this frame's state and its posed and relit
    /// vertices. <paramref name="seconds"/> is what the slides run on.
    /// </summary>
    public void Fill(ClassicScene into, float seconds)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        into.NewPictures.Clear();
        into.Repainted.Clear();
        Sync(into);
        if (meshSent is null || meshSent.Revision != mesh.Revision)
        {
            // SceneMesh hands out copies of its lists; the batches are the same records (a renderer may key on them).
            meshSent = new ClassicMesh { Revision = mesh.Revision, Vertices = mesh.Vertices, Indices = mesh.Indices, Batches = [.. mesh.Batches] };
            var things = new List<int>();
            for (int i = 0; i < meshSent.Batches.Length; i++) if (meshSent.Batches[i].Thing >= 0) things.Add(i);
            thingBatches = [.. things];
            runs.Clear();
            for (int i = 0; i < meshSent.Batches.Length; i++)
            {
                var b = meshSent.Batches[i];
                for (int r = 0; r < b.Runs.Length; r++) runs[(b.Sector, b.Runs[r].Surface)] = (i, r);
            }
        }
        into.Mesh = meshSent;
        into.Seconds = seconds;
        into.Fog = Fog;
        for (int i = 0; i < ClassicStyles.Count; i++) into.Styles.Values[i] = Styles.Value(i);
        long bytes = Things(into);
        bytes += Vertices(into);
        Faded(into);
        Frames(into);
        LastBytes = bytes;
        LastFillMs = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
    }

    /// <summary>
    /// The old renderer's <c>Sync</c> without the mesh: the shells resolved, every material the cache has taken on made
    /// drawable (its pictures sent), the films' repainted pictures. Run again mid-frame where the old renderer did (a halo's
    /// material met for the first time), so the same materials can be drawn in the same frame.
    /// </summary>
    public void Sync(ClassicScene into)
    {
        ResolveShells();
        var items = materials.Items;
        for (int i = drawable; i < items.Count; i++) into.NewPictures.Add((i, Pictures(items[i])));
        drawable = items.Count;
        Send(into);
        into.Drawable = drawable;
        var repainted = materials.Repainted;
        if (repainted.Count == 0) return;
        foreach (var (m, image) in repainted)
        {
            if (m >= drawable || image >= Math.Max(1, items[m].Images.Length)) continue;
            if (items[m].Images[image] is not { } picture) continue;
            into.Repainted.Add((m, image, new ClassicPicture(picture.Width, picture.Height, TextureLayout.BottomRowFirst(picture))));
        }
        repainted.Clear();
    }

    /// <summary>The level surfaces drawn see-through this frame (ours), by batch and run, the batches in order.</summary>
    void Faded(ClassicScene into)
    {
        into.Faded.Clear();
        into.Holes.Clear();
        into.Windowed = false;
        if (SeeThrough is not { AnySurfaces: true } seen) return;
        into.HoleEye = seen.Eye;
        into.Windowed = seen.Mode == SeeThroughMode.On;
        foreach (var h in seen.Holes) into.Holes.Add(new ClassicHole(h.Axis, h.CosInner, h.CosOuter, h.Far));
        foreach (var (sector, surface, level) in seen.Surfaces())
            if (runs.TryGetValue((sector, surface), out var at))
                into.Faded.Add(new ClassicFaded(at.Batch, at.Run, level, World.SeeThrough.Opacity(level)));
        into.Faded.Sort((a, b) => a.Batch != b.Batch ? a.Batch.CompareTo(b.Batch) : a.Run.CompareTo(b.Run));
    }

    /// <summary>The materials the cache holds now, sent (their descriptions only; the pictures go when they are made drawable).</summary>
    void Send(ClassicScene into)
    {
        var items = materials.Items;
        for (int i = sent.Count; i < items.Count; i++) sent.Add(Describe(items[i]));
        into.Materials = sent;
    }

    /// <summary>The frame's last word on the materials: those loaded since (a blob's, a decal's) described, and each one's frame now.</summary>
    void Frames(ClassicScene into)
    {
        Send(into);
        var items = materials.Items;
        if (into.MaterialFrames.Length < items.Count) into.MaterialFrames = new ClassicMaterialFrame[Math.Max(items.Count, into.MaterialFrames.Length * 2)];
        for (int i = 0; i < items.Count; i++) into.MaterialFrames[i] = new ClassicMaterialFrame(items[i].CurrentImage, items[i].DrawPasses);
    }

    /// <summary>
    /// The end of the frame's fill: the materials loaded after the scene was filled (a light's blob, a decal's) described,
    /// and every material's frame as it stands. The world's producer calls it last.
    /// </summary>
    public void Finish(ClassicScene into) => Frames(into);

    static ClassicMaterial Describe(LoadedMaterial m) => new()
    {
        Name = m.Name, Blend = m.Blend, Traits = m.Traits, Passes = m.Passes, Images = m.Images.Length,
        GlowImage = m.GlowImage, DetailImage = m.DetailImage, DetailScale = m.DetailScale,
        TwoSided = m.Nam.TwoSided, FullyLit = m.Nam.FullyLit, GlowRamp = m.Nam.GlowRamp ?? 0, Slide0 = ShellDraws.SlideOf(m),
    };

    /// <summary>A material's pictures, one a slot (at least one), bottom row first; null where a picture is missing.</summary>
    static ClassicPicture?[] Pictures(LoadedMaterial m)
    {
        var images = m.Images;
        var pictures = new ClassicPicture?[Math.Max(1, images.Length)];
        for (int j = 0; j < pictures.Length; j++)
            if (j < images.Length && images[j] is { } img) pictures[j] = new ClassicPicture(img.Width, img.Height, TextureLayout.BottomRowFirst(img));
        return pictures;
    }

    /// <summary>Each thing as this frame draws it, and each thing batch's named-shape bit.</summary>
    long Things(ClassicScene into)
    {
        var things = poses.Things;
        int n = things.Count;
        if (into.Things.Length < n) into.Things = new ClassicThing[Math.Max(n, into.Things.Length * 2)];
        into.ThingCount = n;
        var fade = Fade;
        into.Fades = fade is not null;
        into.AnyShells = shellMaterials.Count > 0;
        var glow = poses.Glow;
        into.GlowOn = glow is not null;
        lit.Clear();
        if (glow is not null) foreach (var (thing, add) in glow.Lit()) lit[thing] = add;
        into.GlowAny = lit.Count > 0;
        if (Identities) first.Clear();
        var passed = poses.Passed;
        var items = materials.Items;
        for (int i = 0; i < n; i++)
        {
            var t = things[i];
            ref var d = ref into.Things[i];
            if (t is null) { d = new ClassicThing { Fade = fade is null ? 1f : 0f, Shell = -1, Identity = i }; continue; }
            d.ModelAlpha = t.ModelAlpha;
            if (t.SeeThroughLevel > 0f) d.ModelAlpha *= World.SeeThrough.Opacity(t.SeeThroughLevel);   // in the camera's way (ours)
            d.Fade = fade?.Invoke(t) ?? 1f;
            d.InWorld = t.InWorld;
            d.ModelHidden = t.ModelHidden;
            d.Reflected = (t.RenderFlags & 0x2u) == 0 && (t.Flags & WorldThing.Flag.NoReflection) == 0;
            d.Passed = passed.Count > 0 && passed.Contains(i);
            // The model's own scale (`SetScale`, MODELSCALE) is about its own origin, before it is placed — so it goes
            // between taking the placement back out and putting the pose in.
            bool scaled = t.Fx is { Scale: var s } && s != Vector3.One;
            d.Placed = false;
            if ((t.Moved || scaled) && Matrix4x4.Invert(t.Base, out var inverse))
            {
                var world = scaled ? Matrix4x4.CreateScale(t.Fx!.Scale) * t.World : t.World;
                world.M43 += t.ShownLift;       // ours: a walker eased over a cell level's steps (CellStepEase); nothing in a 1.1 level
                d.Placement = inverse * world;
                d.Placed = true;
            }
            var fx = t.Fx;
            d.HidesModel = ShellDraw.HidesModel(fx);
            d.Shell = -1;
            d.ShellDraw = null;
            if (fx?.Shell is { } name && shellMaterials.TryGetValue(name, out var index) && index is int shell)
            {
                d.Shell = shell;
                d.ShellDraw = ShellDraw.For(fx, items[shell], null);
            }
            d.Glowing = lit.TryGetValue(t, out d.Glow);
            if (Identities)
            {
                if (!first.TryGetValue(t, out int at)) first[t] = at = i;
                d.Identity = at;
            }
            else d.Identity = i;
        }
        var batches = meshSent!.Batches;
        if (into.BatchHidden.Length < batches.Length) into.BatchHidden = new bool[Math.Max(batches.Length, into.BatchHidden.Length * 2)];
        foreach (int b in thingBatches)
        {
            var batch = batches[b];
            into.BatchHidden[b] = batch.Thing >= 0 && batch.Thing < n && NamedShapes.IsHidden(things[batch.Thing], batch.Slot, batch.Mesh);
        }
        return (long)n * System.Runtime.CompilerServices.Unsafe.SizeOf<ClassicThing>();
    }

    /// <summary>The vertices posed this frame (each animating thing's, then what it carries) and the standing-still runs relit.</summary>
    long Vertices(ClassicScene into)
    {
        into.Posed.Clear();
        into.Relit.Clear();
        int need = 0;
        var posed = poses.Posed;
        for (int i = 0; i < posed.Count; i++)
        {
            need += posed[i].Vertices.Length;
            foreach (var part in posed[i].Carried) need += part.Vertices.Length;
        }
        var changed = poses.Relighting.Changed;
        foreach (var slice in changed) need += slice.Run!.Length;
        if (into.Vertices.Length < need) into.Vertices = new SceneVertex[Math.Max(need, into.Vertices.Length * 2)];
        int at = 0;
        void Put(List<ClassicSpan> spans, uint firstVertex, SceneVertex[] run)
        {
            run.AsSpan().CopyTo(into.Vertices.AsSpan(at));
            spans.Add(new ClassicSpan(firstVertex, at, run.Length));
            at += run.Length;
        }
        for (int i = 0; i < posed.Count; i++)
        {
            var a = posed[i];
            Put(into.Posed, a.FirstVertex, a.Vertices);
            foreach (var part in a.Carried) Put(into.Posed, part.FirstVertex, part.Vertices);
        }
        foreach (var slice in changed) Put(into.Relit, slice.First, slice.Run!);
        into.VertexCount = at;
        into.Animated = poses.LastAnimated;
        return (long)at * System.Runtime.CompilerServices.Unsafe.SizeOf<SceneVertex>();
    }

    /// <summary>The first index in the scene's things that is <paramref name="thing"/>, or −1 (what a pass drawing one thing alone names).</summary>
    public int IdentityOf(WorldThing? thing)
    {
        if (thing is null) return -1;
        var things = poses.Things;
        for (int i = 0; i < things.Count; i++) if (ReferenceEquals(things[i], thing)) return i;
        return -1;
    }
}
