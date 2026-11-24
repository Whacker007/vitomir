// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Render.Modern;

namespace Vitomir.Platform.Render.Characters;

/// <summary>The textures a <see cref="CharacterBatch"/>'s draws name, as the GPU holds them (<see cref="CharacterPass"/>; a test's stand-in).</summary>
internal interface ICharacterTextures
{
    /// <summary>
    /// The index of the texture named (an index for <see cref="ModernExtra"/>), uploaded in the colour space of the slot that
    /// names it — <paramref name="srgb"/> for a base colour, linear for a normal or occlusion-roughness-metal map — or −1 for
    /// none (or not there yet: asked again next frame). A person's cut-out base colour gives its <paramref name="cutout"/>:
    /// its mips then keep its coverage at that alpha (<see cref="CharacterTextureSize.CoverageMips"/>).
    /// </summary>
    int Texture(string? name, bool srgb, float cutout = 0f);
}

/// <summary>
/// The frame's characters on the CPU (<see cref="CharacterPass"/>'s work before the upload): each skinned into world space in
/// the renderer's units, their triangles' indices, and a draw a part. 1.1's models go as they always went
/// (<see cref="CharacterPass.Skin"/>, every part two-sided, one texture, the old program's constants); the
/// models repository's people (a model whose parts carry a <see cref="PartMaterial"/>) go by their materials, skinned with their
/// tangents and colours, and only the vertices their drawn triangles use (<see cref="PeopleMesh"/>); a large one on several
/// threads (<see cref="SkinWorkers"/>); in first person once whole for the shadow and again only where the picture's skin
/// differs (<see cref="FirstPersonMesh"/>). Allocates nothing per frame once its buffers have grown and each model (and each
/// first-person collapse) has been seen once.
/// </summary>
internal sealed class CharacterBatch : IDisposable
{
    /// <summary>
    /// The dielectric reflectance factor of the models repository's parts: glTF's own without <c>KHR_materials_specular</c>
    /// (4 % at normal incidence; the files carry no other).
    /// </summary>
    public const float PeopleSpecular = 1f;

    readonly ICharacterTextures textures;
    readonly List<uint> indices = [];
    // Each model seen, prepared once (1.1's as the stand-in below); kept while the model lives and no longer.
    readonly ConditionalWeakTable<SkinnedModel, PeopleMesh> meshes = [];
    static readonly PeopleMesh OneOne = new() { Vertices = [], Indices = [], Parts = [], Bones = 0 };
    readonly List<FirstPersonMesh?> views = [];
    ModernVertex[] vertices = [];
    Matrix4x4[] placed = new Matrix4x4[256], placedView = new Matrix4x4[256];
    int count;
    SkinWorkers? workers;
    bool noWorkers;

    /// <summary>
    /// Whether a large person may be skinned on several threads (<see cref="SkinWorkers"/>; on by default where the machine has
    /// a second core). Off for measuring one thread's cost. When on, the first <see cref="Trials"/> large skinnings alternate
    /// with and without the helpers and the faster way is kept: on this project's two-core cloud machine one thread is the
    /// faster (2.5 against 3.3 ms for the player), which a machine with cores to spare should turn round.
    /// </summary>
    public bool Parallel { get; set; } = true;

    /// <summary>How many large skinnings are timed, half each way, before the faster is kept.</summary>
    public const int Trials = 40;
    int trials;
    long withHelpers, without;
    bool? helpersFaster;

    /// <summary>Whether the helpers were found the faster (null while still timing, or before any large person).</summary>
    public bool? HelpersFaster => helpersFaster;

    /// <summary>
    /// In first person, the share of a vertex's weight on the collapsed bones above which its triangles are
    /// left out of the picture (<see cref="FirstPersonMesh"/>; the shadow is whole).
    /// </summary>
    public float FirstPersonCut { get; set; } = FirstPersonMesh.DefaultCut;

    public CharacterBatch(ICharacterTextures textures, List<ModernExtra.Draw> draws)
    {
        this.textures = textures;
        Draws = draws;
    }

    /// <summary>The frame's draws, into <see cref="Vertices"/> and <see cref="Indices"/>.</summary>
    public List<ModernExtra.Draw> Draws { get; }
    /// <summary>The frame's vertices, world space, the renderer's units.</summary>
    public ReadOnlySpan<ModernVertex> Vertices => vertices.AsSpan(0, count);
    /// <summary>The frame's indices into <see cref="Vertices"/>.</summary>
    public ReadOnlySpan<uint> Indices => CollectionsMarshal.AsSpan(indices);

    /// <summary>
    /// How the people are skinned when there are several (<see cref="PeopleSkinning"/>: only those in view, the distant less
    /// often); <see cref="PeopleSkinning.EveryFrame"/> is the earlier build's.
    /// </summary>
    public PeopleSkinning Skinning { get; set; } = PeopleSkinning.Default;

    /// <summary>The last frame's people: skinned, drawn again from an earlier skinning, and left out (out of view).</summary>
    public (int Skinned, int Reused, int Culled) LastPeople { get; private set; }

    // Each person's last skinning (kept while the drawn character lives), the frames counted, each character's lot this frame.
    readonly ConditionalWeakTable<DrawnCharacter, SkinCache> caches = [];
    readonly List<int> every = [];
    long frame;
    int skinned, reused, culled;

    /// <summary>The frame's characters skinned (<see cref="Vertices"/>, <see cref="Indices"/>, <see cref="Draws"/>).</summary>
    public void Build(IReadOnlyList<DrawnCharacter> characters) => Build(characters, default);

    /// <summary>
    /// The frame's characters skinned (<see cref="Vertices"/>, <see cref="Indices"/>, <see cref="Draws"/>), the people as
    /// <see cref="Skinning"/> says for <paramref name="eye"/> (a default view: every one, every frame).
    /// </summary>
    internal void Build(IReadOnlyList<DrawnCharacter> characters, in SkinView eye)
    {
        Draws.Clear();
        indices.Clear();
        views.Clear();
        every.Clear();
        count = 0;
        frame++;
        (skinned, reused, culled) = (0, 0, 0);
        int total = 0;
        for (int i = 0; i < characters.Count; i++)
        {
            var c = characters[i];
            var mesh = Mesh(c.Model);
            // A person in first person: whole for the shadow, and again only the vertices the picture's skin moves otherwise.
            var view = mesh is not null && c.ShadowSkin is { } whole ? FirstPersonMesh.For(mesh, c.Skin, whole, FirstPersonCut) : null;
            views.Add(view);
            // A person (not the viewer's own body) out of view is left out; a distant one skinned every few frames.
            int often = 1;
            if (mesh is not null && view is null && eye.Has)
            {
                var (centre, radius) = Sphere(c);
                if (Skinning.Cull && !eye.Sees(centre, radius + Skinning.ShadowReach)) { every.Add(0); culled++; continue; }
                often = Skinning.Every(eye.Distance(centre));
            }
            every.Add(often);
            total += mesh is null ? c.Model.Vertices.Length * (c.ShadowSkin is null ? 1 : 2)
                   : mesh.Vertices.Length + (c.ShadowSkin is null ? 0 : view!.Extra.Length);
        }
        LastPeople = (skinned, reused, culled);
        if (total == 0) return;
        if (vertices.Length < total) vertices = new ModernVertex[total];
        int first = 0;
        for (int i = 0; i < characters.Count; i++)
        {
            var c = characters[i];
            if (every[i] == 0) continue;   // out of view
            if (Mesh(c.Model) is { } mesh)
            {
                first = views[i] is { } view ? AddFirstPerson(c.Model, mesh, view, c.Skin, c.ShadowSkin!, c.World, first)
                      : every[i] > 1 ? AddDistant(c, mesh, every[i], first)
                      : AddPerson(c.Model, mesh, c.Skin, c.World, first);
                continue;
            }
            // A character with a skin of its own for the shadow (the viewer's body in first person) is skinned twice:
            // as seen, for the picture, and whole, for the shadow.
            // The viewer's own body in first person is drawn one-sided, each face turned to its vertices'
            // normals, so the inside of the chest, a sleeve or the collar is never drawn whatever the eye comes close to.
            first = Add(c.Model, c.Skin, c.World, first, c.ShadowSkin is null ? ModernExtra.DrawPasses.Both : ModernExtra.DrawPasses.View, outsideOnly: c.ShadowSkin is not null);
            if (c.ShadowSkin is { } whole) first = Add(c.Model, whole, c.World, first, ModernExtra.DrawPasses.Shadow);
        }
        count = first;
        LastPeople = (skinned, reused, culled);
    }

    /// <summary>The sphere round a character's model at rest, placed (metres): its box's middle and half its diagonal, grown by a quarter for its poses.</summary>
    static (Vector3 Centre, float Radius) Sphere(DrawnCharacter c)
    {
        var m = c.Model;
        var centre = Vector3.Transform((m.BoundsMin + m.BoundsMax) * 0.5f, c.World);
        float scale = new Vector3(c.World.M11, c.World.M12, c.World.M13).Length();
        return (centre, (m.BoundsMax - m.BoundsMin).Length() * 0.5f * 1.25f * scale);
    }

    /// <summary>
    /// A distant person (<see cref="PeopleSkinning"/>): skinned when its last skinning is <paramref name="often"/> frames old (each
    /// person's turn staggered from the first), else that skinning drawn again, moved by how far the person has moved since.
    /// </summary>
    int AddDistant(DrawnCharacter c, PeopleMesh mesh, int often, int first)
    {
        var cache = caches.GetValue(c, static _ => new SkinCache());
        int n = mesh.Vertices.Length;
        var origin = c.World.Translation * ModernMath.UnitsPerMetre;
        Vector3 min, max;
        if (cache.Mesh == mesh && cache.Vertices.Length == n && frame - cache.Frame < often)
        {
            var delta = origin - cache.Origin;
            var from = cache.Vertices.AsSpan(0, n);
            var into = vertices.AsSpan(first, n);
            for (int i = 0; i < n; i++)
            {
                var v = from[i];
                v.Position += delta;
                into[i] = v;
            }
            (min, max) = (cache.Min + delta, cache.Max + delta);
            reused++;
        }
        else
        {
            (min, max) = Skin(mesh, c.Skin, c.World, placed, null, first);
            if (cache.Vertices.Length != n) cache.Vertices = new ModernVertex[n];
            vertices.AsSpan(first, n).CopyTo(cache.Vertices);
            // The first time, its turns staggered by its entity: the people's skinnings spread over the frames.
            cache.Frame = cache.Mesh == mesh ? frame : frame - (long)(c.Entity % (ulong)often);
            (cache.Mesh, cache.Origin, cache.Min, cache.Max) = (mesh, origin, min, max);
            skinned++;
        }
        AddParts(c.Model, mesh.Parts, mesh.Indices, first, min, max, ModernExtra.DrawPasses.Both, outsideOnly: false);
        return first + n;
    }

    /// <summary>The model prepared for the people's path, once (null for a 1.1 model, which keeps its own path).</summary>
    PeopleMesh? Mesh(SkinnedModel model)
    {
        if (!meshes.TryGetValue(model, out var mesh))
        {
            mesh = PeopleMesh.For(model) ?? OneOne;
            meshes.AddOrUpdate(model, mesh);
        }
        return ReferenceEquals(mesh, OneOne) ? null : mesh;
    }

    /// <summary>
    /// One 1.1 character skinned at <paramref name="first"/> of the frame's vertices, its parts' draws in <paramref name="passes"/>;
    /// answers the next free vertex. <paramref name="outsideOnly"/>: one-sided, each triangle wound so its face is the
    /// side its vertices' normals point to (<see cref="CharacterPass.Outward"/>), so it is seen from outside the body only.
    /// </summary>
    int Add(SkinnedModel model, Matrix4x4[] skin, Matrix4x4 world, int first, ModernExtra.DrawPasses passes, bool outsideOnly = false)
    {
        var skinned = vertices.AsSpan(first, model.Vertices.Length);
        var (min, max) = CharacterPass.Skin(model, skin, world, skinned);
        foreach (var part in model.Parts)
        {
            if (part.IndexCount <= 0 || part.FirstIndex + part.IndexCount > model.Indices.Length) continue;
            uint start = (uint)indices.Count;
            for (int i = 0; i + 2 < part.IndexCount; i += 3)
            {
                uint a = model.Indices[part.FirstIndex + i], b = model.Indices[part.FirstIndex + i + 1], c = model.Indices[part.FirstIndex + i + 2];
                if (outsideOnly && !CharacterPass.Outward(skinned, a, b, c)) (b, c) = (c, b);
                indices.Add((uint)first + a); indices.Add((uint)first + b); indices.Add((uint)first + c);
            }
            if ((uint)indices.Count == start) continue;
            Draws.Add(new ModernExtra.Draw(start, (uint)indices.Count - start, min, max, textures.Texture(part.Texture, srgb: true),
                                           part.AlphaTest ? CharacterPass.AlphaCutoff : -1f, DoubleSided: !outsideOnly,
                                           CharacterPass.Roughness, 0f, CharacterPass.Specular, passes));
        }
        return first + model.Vertices.Length;
    }

    /// <summary>
    /// A person of the models repository's, seen and casting: its used vertices skinned with their tangents and
    /// colours (<see cref="Skin"/>), then a draw for each part by its material (<see cref="AddParts"/>).
    /// </summary>
    int AddPerson(SkinnedModel model, PeopleMesh mesh, Matrix4x4[] skin, Matrix4x4 world, int first)
    {
        var (min, max) = Skin(mesh, skin, world, placed, null, first);
        skinned++;
        AddParts(model, mesh.Parts, mesh.Indices, first, min, max, ModernExtra.DrawPasses.Both, outsideOnly: false);
        return first + mesh.Vertices.Length;
    }

    /// <summary>
    /// A person in first person: skinned whole by <paramref name="shadow"/> for the shadow, and
    /// only the vertices <paramref name="view"/> names again by <paramref name="skin"/>, just after; the picture's draws read
    /// the whole skinning for every other vertex (its skin is the same there), leave out the triangles the picture's skin
    /// shrinks to nothing, and are one-sided, wound outward, as 1.1's; then the shadow's draws, as the materials say.
    /// </summary>
    int AddFirstPerson(SkinnedModel model, PeopleMesh mesh, FirstPersonMesh view, Matrix4x4[] skin, Matrix4x4[] shadow, Matrix4x4 world, int first)
    {
        var (min, max) = Skin(mesh, shadow, world, placed, null, first);
        skinned++;
        int extra = first + mesh.Vertices.Length;
        if (view.Extra.Length > 0)
        {
            var (lo, hi) = Skin(mesh, skin, world, placedView, view.Extra, extra);
            (lo, hi) = (Vector3.Min(lo, min), Vector3.Max(hi, max));
            AddParts(model, view.Parts, view.Indices, first, lo, hi, ModernExtra.DrawPasses.View, outsideOnly: true);
        }
        else AddParts(model, view.Parts, view.Indices, first, min, max, ModernExtra.DrawPasses.View, outsideOnly: true);
        AddParts(model, mesh.Parts, mesh.Indices, first, min, max, ModernExtra.DrawPasses.Shadow, outsideOnly: false);
        return extra + view.Extra.Length;
    }

    /// <summary>
    /// A person's vertices (all its used ones, or those <paramref name="only"/> names) skinned by <paramref name="skin"/> into the
    /// frame's vertices from <paramref name="first"/>, on the helper threads when there are many; the box round them.
    /// <paramref name="into"/> holds a matrix a bone (256: a byte's worth of bones).
    /// </summary>
    (Vector3 Min, Vector3 Max) Skin(PeopleMesh mesh, Matrix4x4[] skin, Matrix4x4 world, Matrix4x4[] into, int[]? only, int first)
    {
        var place = Place(mesh, skin, world, into);
        int n = only?.Length ?? mesh.Vertices.Length;
        if (!Parallel || n < 2 * SkinWorkers.Chunk || Workers() is not { } helpers) return SkinHere(mesh, into, place, only, first, n);
        if (helpersFaster is { } faster) return faster ? helpers.Skin(mesh, into, place, only, vertices, first) : SkinHere(mesh, into, place, only, first, n);
        // Still timing: every other large skinning on the helpers.
        bool use = (trials & 1) == 0;
        long t0 = Stopwatch.GetTimestamp();
        var box = use ? helpers.Skin(mesh, into, place, only, vertices, first) : SkinHere(mesh, into, place, only, first, n);
        long spent = Stopwatch.GetTimestamp() - t0;
        if (use) withHelpers += spent; else without += spent;
        if (++trials >= Trials) helpersFaster = withHelpers < without;
        return box;
    }

    (Vector3 Min, Vector3 Max) SkinHere(PeopleMesh mesh, Matrix4x4[] into, in Matrix4x4 place, int[]? only, int first, int n)
    {
        var output = vertices.AsSpan(first, n);
        Vector3 min, max;
        if (only is null) SkinRange(mesh.Vertices, into, place, 0, n, output, out min, out max);
        else SkinList(mesh.Vertices, only, into, place, 0, n, output, out min, out max);
        return (min, max);
    }

    /// <summary>
    /// A draw a part (<paramref name="parts"/>' runs of <paramref name="source"/>, indices into the vertices from
    /// <paramref name="first"/>) by its material: the base colour (sRGB) × its factor × the vertex colour, the normal map, the
    /// occlusion-roughness-metal map, a cut-out at the material's alpha, one or both faces as the material says (one when
    /// <paramref name="outsideOnly"/>, as 1.1's in first person: a two-sided part's triangles wound outward, a one-sided part's
    /// as the file has them); a hidden part (the cornea) has no run.
    /// </summary>
    void AddParts(SkinnedModel model, (int First, int Count)[] parts, uint[] source, int first, Vector3 min, Vector3 max,
                  ModernExtra.DrawPasses passes, bool outsideOnly)
    {
        var skinned = vertices.AsSpan(first);
        for (int p = 0; p < model.Parts.Length && p < parts.Length; p++)
        {
            var (from, n) = parts[p];
            if (n == 0) continue;
            uint start = (uint)indices.Count;
            int at = indices.Count;
            CollectionsMarshal.SetCount(indices, at + n);
            var into = CollectionsMarshal.AsSpan(indices).Slice(at, n);
            var run = source.AsSpan(from, n);
            var part = model.Parts[p];
            // A one-sided material's own winding is its outside (glTF's front faces, kept by the conversion's turn), so only a
            // two-sided one's triangles are turned to their normals.
            if (outsideOnly && part.Material is not { DoubleSided: false })
                for (int i = 0; i + 2 < n; i += 3)
                {
                    uint a = run[i], b = run[i + 1], c = run[i + 2];
                    if (!CharacterPass.Outward(skinned, a, b, c)) (b, c) = (c, b);
                    into[i] = (uint)first + a; into[i + 1] = (uint)first + b; into[i + 2] = (uint)first + c;
                }
            else
                for (int i = 0; i < n; i++) into[i] = (uint)first + run[i];
            if (part.Material is not { } m)
            {
                Draws.Add(new ModernExtra.Draw(start, (uint)n, min, max, textures.Texture(part.Texture, srgb: true),
                                               part.AlphaTest ? CharacterPass.AlphaCutoff : -1f, DoubleSided: !outsideOnly,
                                               CharacterPass.Roughness, 0f, CharacterPass.Specular, passes));
                continue;
            }
            Draws.Add(new ModernExtra.Draw(start, (uint)n, min, max, textures.Texture(m.BaseColor, srgb: true, cutout: m.AlphaCutoff),
                                           m.AlphaCutoff > 0f ? m.AlphaCutoff : -1f, DoubleSided: m.DoubleSided && !outsideOnly,
                                           m.Roughness, m.Metallic, PeopleSpecular, passes)
            {
                Normal = textures.Texture(m.Normal, srgb: false),
                Orm = textures.Texture(m.Orm, srgb: false),
                BaseColorFactor = m.BaseColorFactor,
                NormalScale = m.NormalScale,
            });
        }
    }

    /// <summary>The helper threads, made the first time a large person is drawn (none on a machine of one core).</summary>
    SkinWorkers? Workers()
    {
        if (workers is not null || noWorkers) return workers;
        if (SkinWorkers.Helpers == 0) { noWorkers = true; return null; }
        return workers = new SkinWorkers(SkinWorkers.Helpers);
    }

    public void Dispose()
    {
        workers?.Dispose();
        workers = null;
    }

    /// <summary>
    /// A person's used vertices skinned into <paramref name="into"/> in world space, the renderer's units, and
    /// the box round them, on this thread (<see cref="Place"/>, then <see cref="SkinRange"/> over them all).
    /// </summary>
    internal static (Vector3 Min, Vector3 Max) SkinPerson(PeopleMesh mesh, Matrix4x4[] skin, Matrix4x4 world, Matrix4x4[] placed, Span<ModernVertex> into)
    {
        var place = Place(mesh, skin, world, placed);
        SkinRange(mesh.Vertices, placed, place, 0, mesh.Vertices.Length, into, out var min, out var max);
        return (min, max);
    }

    /// <summary>
    /// Each bone's skin matrix put into the world once (<paramref name="placed"/>: skin × world × the units), a bone past the
    /// skin's matrices as the identity (as on 1.1's path); answers the world × the units, which places an unweighted vertex.
    /// </summary>
    internal static Matrix4x4 Place(PeopleMesh mesh, Matrix4x4[] skin, Matrix4x4 world, Matrix4x4[] placed)
    {
        var place = world * Matrix4x4.CreateScale(ModernMath.UnitsPerMetre);
        int bones = Math.Min(skin.Length, mesh.Bones);
        for (int b = 0; b < bones; b++) placed[b] = skin[b] * place;
        for (int b = bones; b < mesh.Bones; b++) placed[b] = place;
        return place;
    }

    /// <summary>
    /// Vertices <paramref name="from"/> to <paramref name="to"/> of a person skinned into the same places of
    /// <paramref name="into"/>, and the box round them (<see cref="SkinOne"/>).
    /// </summary>
    internal static void SkinRange(PeopleMesh.Vertex[] source, Matrix4x4[] placed, in Matrix4x4 place, int from, int to, Span<ModernVertex> into,
                                   out Vector3 min, out Vector3 max)
    {
        if (from < 0 || (uint)to > (uint)source.Length || (uint)to > (uint)into.Length) throw new ArgumentOutOfRangeException(nameof(to));
        min = new Vector3(float.MaxValue);
        max = new Vector3(float.MinValue);
        ref Vector4 rows = ref Unsafe.As<Matrix4x4, Vector4>(ref MemoryMarshal.GetArrayDataReference(placed));
        ref Vector4 own = ref Unsafe.As<Matrix4x4, Vector4>(ref Unsafe.AsRef(in place));
        ref ModernVertex output = ref MemoryMarshal.GetReference(into);
        ref PeopleMesh.Vertex input = ref MemoryMarshal.GetArrayDataReference(source);
        for (int i = from; i < to; i++)
        {
            ref var o = ref Unsafe.Add(ref output, i);
            SkinOne(in Unsafe.Add(ref input, i), ref rows, ref own, ref o);
            min = Vector3.Min(min, o.Position);
            max = Vector3.Max(max, o.Position);
        }
    }

    /// <summary>
    /// The vertices <paramref name="list"/>[<paramref name="from"/>..<paramref name="to"/>] names skinned into
    /// <paramref name="into"/> at their places in the list (first person's second skinning), and the box round them.
    /// </summary>
    internal static void SkinList(PeopleMesh.Vertex[] source, int[] list, Matrix4x4[] placed, in Matrix4x4 place, int from, int to, Span<ModernVertex> into,
                                  out Vector3 min, out Vector3 max)
    {
        if (from < 0 || (uint)to > (uint)list.Length || (uint)to > (uint)into.Length) throw new ArgumentOutOfRangeException(nameof(to));
        min = new Vector3(float.MaxValue);
        max = new Vector3(float.MinValue);
        ref Vector4 rows = ref Unsafe.As<Matrix4x4, Vector4>(ref MemoryMarshal.GetArrayDataReference(placed));
        ref Vector4 own = ref Unsafe.As<Matrix4x4, Vector4>(ref Unsafe.AsRef(in place));
        ref ModernVertex output = ref MemoryMarshal.GetReference(into);
        for (int i = from; i < to; i++)
        {
            ref var o = ref Unsafe.Add(ref output, i);
            SkinOne(in source[list[i]], ref rows, ref own, ref o);
            min = Vector3.Min(min, o.Position);
            max = Vector3.Max(max, o.Position);
        }
    }

    /// <summary>
    /// One vertex skinned: its bones' placed matrices (<paramref name="rows"/>, four rows a bone) blended by its weights (linear
    /// blend skinning: the old path's sum of the four transforms, done once on the matrix), and the blend moves its position,
    /// normal and tangent (xyz; w, the bitangent's side, kept), the two directions normalised. Presentation only: the
    /// multiply-adds may be fused where the processor can.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void SkinOne(in PeopleMesh.Vertex v, ref Vector4 rows, ref Vector4 own, ref ModernVertex o)
    {
        Vector4 a0, a1, a2, a3;
        if (v.Kind == PeopleMesh.VertexKind.Single)
        {
            ref Vector4 m = ref Unsafe.Add(ref rows, v.Row0);
            a0 = m; a1 = Unsafe.Add(ref m, 1); a2 = Unsafe.Add(ref m, 2); a3 = Unsafe.Add(ref m, 3);
        }
        else if (v.Kind == PeopleMesh.VertexKind.Blend)
        {
            ref Vector4 m0 = ref Unsafe.Add(ref rows, v.Row0), m1 = ref Unsafe.Add(ref rows, v.Row1);
            ref Vector4 m2 = ref Unsafe.Add(ref rows, v.Row2), m3 = ref Unsafe.Add(ref rows, v.Row3);
            Vector4 x = new(v.Weights.X), y = new(v.Weights.Y), z = new(v.Weights.Z), w = new(v.Weights.W);
            a0 = Vector4.MultiplyAddEstimate(m3, w, Vector4.MultiplyAddEstimate(m2, z, Vector4.MultiplyAddEstimate(m1, y, m0 * x)));
            a1 = Vector4.MultiplyAddEstimate(Unsafe.Add(ref m3, 1), w, Vector4.MultiplyAddEstimate(Unsafe.Add(ref m2, 1), z,
                 Vector4.MultiplyAddEstimate(Unsafe.Add(ref m1, 1), y, Unsafe.Add(ref m0, 1) * x)));
            a2 = Vector4.MultiplyAddEstimate(Unsafe.Add(ref m3, 2), w, Vector4.MultiplyAddEstimate(Unsafe.Add(ref m2, 2), z,
                 Vector4.MultiplyAddEstimate(Unsafe.Add(ref m1, 2), y, Unsafe.Add(ref m0, 2) * x)));
            a3 = Vector4.MultiplyAddEstimate(Unsafe.Add(ref m3, 3), w, Vector4.MultiplyAddEstimate(Unsafe.Add(ref m2, 3), z,
                 Vector4.MultiplyAddEstimate(Unsafe.Add(ref m1, 3), y, Unsafe.Add(ref m0, 3) * x)));
        }
        else { a0 = own; a1 = Unsafe.Add(ref own, 1); a2 = Unsafe.Add(ref own, 2); a3 = Unsafe.Add(ref own, 3); }
        var pos = Vector4.MultiplyAddEstimate(a0, new Vector4(v.Position.X), Vector4.MultiplyAddEstimate(a1, new Vector4(v.Position.Y),
                  Vector4.MultiplyAddEstimate(a2, new Vector4(v.Position.Z), a3)));
        var nrm = Vector4.MultiplyAddEstimate(a0, new Vector4(v.Normal.X), Vector4.MultiplyAddEstimate(a1, new Vector4(v.Normal.Y), a2 * v.Normal.Z));
        var tan = Vector4.MultiplyAddEstimate(a0, new Vector4(v.Tangent.X), Vector4.MultiplyAddEstimate(a1, new Vector4(v.Tangent.Y), a2 * v.Tangent.Z));
        var n = new Vector3(nrm.X, nrm.Y, nrm.Z);
        var t = new Vector3(tan.X, tan.Y, tan.Z);
        float nn = Vector3.Dot(n, n), tt = Vector3.Dot(t, t);
        o.Position = new Vector3(pos.X, pos.Y, pos.Z);
        o.Normal = nn > 1e-12f ? n * (1f / MathF.Sqrt(nn)) : Vector3.UnitZ;
        o.Tangent = tt > 1e-12f ? new Vector4(t * (1f / MathF.Sqrt(tt)), v.Tangent.W) : new Vector4(1, 0, 0, 1);
        o.Uv0 = v.Uv;
        o.Uv1 = v.Uv;
        o.Color = v.Color;
    }
}

/// <summary>
/// What first person draws of a person (the picture's skin hides the head, the shadow's is whole), made
/// once for a set of bones that differ between the two skins and kept while it holds. A bone the picture shrinks to nothing (a
/// matrix whose turn's determinant is under a millionth: <c>HeadCollapse</c>'s thousandth) is collapsed. A triangle is left out
/// of the picture when any of its vertices has more than a share (<see cref="DefaultCut"/>) of its weight on collapsed bones: the head and all on
/// it, and what is weighted partly to the neck — the throat's skin, the beard under the chin, the top of the collar — which the
/// collapse would otherwise draw toward the neck's joint (a stump at the collar, and a cone of it before the chest when he looks
/// down). A used vertex weighted to no differing bone is the shadow's own skinning (the same skin there); every other vertex a
/// kept triangle uses is skinned again by the picture's skin (<see cref="Extra"/>), after the whole. So the head's thousands of
/// vertices cost nothing twice. 1.1's characters are not drawn this way (their path is as it was).
/// </summary>
internal sealed class FirstPersonMesh
{
    /// <summary>The used vertices skinned again by the picture's skin, in their order after the whole skinning.</summary>
    public required int[] Extra { get; init; }
    /// <summary>The picture's triangles: an index below the person's used vertices' count is the whole skinning's, at or above it the extra one's.</summary>
    public required uint[] Indices { get; init; }
    /// <summary>Each of the model's parts: where its picture's indices start in <see cref="Indices"/> and how many.</summary>
    public required (int First, int Count)[] Parts { get; init; }
    /// <summary>Each bone: 0 the same in both skins, 1 different, 2 different and shrunk to nothing in the picture.</summary>
    public required byte[] Bones { get; init; }
    /// <summary>The share it was made with (<see cref="For"/>'s <c>cut</c>).</summary>
    public float MadeWith { get; init; }

    /// <summary>
    /// The share of a vertex's weight on collapsed bones above which its triangles are left out of the first-person picture
    /// (<see cref="CharacterBatch.FirstPersonCut"/>), chosen on the player's stills:
    /// at a half the beard under the chin and the most-collapsed throat go (no stump), and the view
    /// looking down is the same as cutting nothing but the head (at 0.4 the first holes show at the throat, at 0.25 a ragged
    /// edge).
    /// </summary>
    public const float DefaultCut = 0.5f;

    /// <summary>A shrunk bone's turn has a determinant under this (<c>HeadCollapse.Scale</c>³ is 10⁻⁹).</summary>
    public const float Shrunk = 1e-6f;

    /// <summary>What first person draws of <paramref name="mesh"/> for these two skins: the one kept on the mesh when its bones still hold, else made.</summary>
    public static FirstPersonMesh For(PeopleMesh mesh, Matrix4x4[] skin, Matrix4x4[] shadow, float cut = DefaultCut)
    {
        var kept = mesh.FirstPerson;
        if (kept is not null && kept.MadeWith == cut && Holds(kept.Bones, skin, shadow)) return kept;
        var bones = new byte[mesh.Bones];
        for (int b = 0; b < bones.Length; b++) bones[b] = State(b, skin, shadow);
        return mesh.FirstPerson = Make(mesh, bones, cut);
    }

    static byte State(int b, Matrix4x4[] skin, Matrix4x4[] shadow)
    {
        if (b >= skin.Length || b >= shadow.Length) return (byte)(skin.Length == shadow.Length ? 0 : 1);
        if (skin[b] == shadow[b]) return 0;
        ref readonly var m = ref skin[b];
        float det = m.M11 * (m.M22 * m.M33 - m.M23 * m.M32) - m.M12 * (m.M21 * m.M33 - m.M23 * m.M31) + m.M13 * (m.M21 * m.M32 - m.M22 * m.M31);
        return (byte)(MathF.Abs(det) < Shrunk ? 2 : 1);
    }

    static bool Holds(byte[] bones, Matrix4x4[] skin, Matrix4x4[] shadow)
    {
        for (int b = 0; b < bones.Length; b++) if (State(b, skin, shadow) != bones[b]) return false;
        return true;
    }

    static FirstPersonMesh Make(PeopleMesh mesh, byte[] bones, float cut)
    {
        var source = mesh.Vertices;
        // Each used vertex: 0 the whole skinning's, 1 skinned again, 2 cut (more than `cut` of its weight on collapsed bones).
        var kind = new byte[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            ref readonly var v = ref source[i];
            if (v.Kind == PeopleMesh.VertexKind.Unweighted) continue;
            float changed = 0f, shrunk = 0f, total = 0f;
            for (int k = 0; k < 4; k++)
            {
                float w = v.Kind == PeopleMesh.VertexKind.Single ? (k == 0 ? 1f : 0f) : v.Weights[k];
                if (w == 0f) continue;
                int b = (k switch { 0 => v.Row0, 1 => v.Row1, 2 => v.Row2, _ => v.Row3 }) / 4;
                total += w;
                if (bones[b] != 0) changed += w;
                if (bones[b] == 2) shrunk += w;
            }
            kind[i] = changed == 0f ? (byte)0 : shrunk > total * MathF.Min(cut, 1f - 1e-5f) ? (byte)2 : (byte)1;
        }
        var again = new int[source.Length];   // 0 none, else 1 + the place among the extra
        var extra = new List<int>();
        var indices = new List<uint>(mesh.Indices.Length);
        var parts = new (int, int)[mesh.Parts.Length];
        for (int p = 0; p < parts.Length; p++)
        {
            var (from, n) = mesh.Parts[p];
            int start = indices.Count;
            for (int t = from; t + 2 < from + n; t += 3)
            {
                uint a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
                if (kind[a] == 2 || kind[b] == 2 || kind[c] == 2) continue;
                indices.Add(Map(a)); indices.Add(Map(b)); indices.Add(Map(c));
            }
            parts[p] = (start, indices.Count - start);
        }
        return new FirstPersonMesh { Extra = [.. extra], Indices = [.. indices], Parts = parts, Bones = bones, MadeWith = cut };

        uint Map(uint v)
        {
            if (kind[v] == 0) return v;
            if (again[v] == 0) { extra.Add((int)v); again[v] = extra.Count; }
            return (uint)(source.Length + again[v] - 1);
        }
    }
}

/// <summary>
/// A model of the models repository's prepared once for drawing: the vertices its drawn parts use (the body under
/// the clothes is in the index list no more, and the cornea is not drawn, so a good share of the file's vertices are never
/// looked at), packed for skinning in their own order (tangent and colour beside each, the colour as the GPU takes it), and
/// each part's triangles as indices into them (a triangle naming a vertex the model does not have is left out).
/// </summary>
internal sealed class PeopleMesh
{
    /// <summary>How a vertex is skinned: by blending its four bones, by one bone alone (at weight 1), or by the world alone (no weight).</summary>
    public enum VertexKind : byte { Blend, Single, Unweighted }

    /// <summary>A used vertex as the skinning reads it (80 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Vertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        /// <summary>xyz along the texture's u, w the bitangent's side (glTF's); (1, 0, 0, 1) when the model has none.</summary>
        public Vector4 Tangent;
        public Vector2 Uv;
        public Vector4 Weights;
        /// <summary>Each bone's first row among the placed matrices' rows (the bone × 4); 0 for a bone of no weight.</summary>
        public ushort Row0, Row1, Row2, Row3;
        /// <summary>The vertex colour, linear RGBA8, R in the lowest byte (white when the model has none or its material does not use it).</summary>
        public uint Color;
        public VertexKind Kind;
    }

    public required Vertex[] Vertices { get; init; }
    /// <summary>The parts' triangles, three indices each into <see cref="Vertices"/>.</summary>
    public required uint[] Indices { get; init; }
    /// <summary>Each of the model's parts: where its indices start in <see cref="Indices"/> and how many (0 for a hidden part).</summary>
    public required (int First, int Count)[] Parts { get; init; }
    /// <summary>How many skin matrices the vertices name: one past the highest bone index used.</summary>
    public required int Bones { get; init; }
    /// <summary>How many of the model's vertices are not used (skinned never).</summary>
    public int Unused { get; init; }

    /// <summary>What first person last drew of it (<see cref="FirstPersonMesh.For"/>'s keeping; the collapse is the same from frame to frame).</summary>
    public FirstPersonMesh? FirstPerson { get; set; }

    /// <summary>Whether the model is one of the models repository's people (a part carries a material, or it has tangents or colours).</summary>
    public static bool IsPerson(SkinnedModel model)
    {
        if (model.Tangents is not null || model.Colors is not null) return true;
        foreach (var part in model.Parts) if (part.Material is not null) return true;
        return false;
    }

    /// <summary>The model prepared, or null when it is a 1.1 model (drawn by the old path).</summary>
    public static PeopleMesh? For(SkinnedModel model)
    {
        if (!IsPerson(model)) return null;
        var source = model.Vertices;
        var used = new int[source.Length];   // 0 unused, else 1 + the compact index
        var parts = new (int, int)[model.Parts.Length];
        var kept = new List<uint>(model.Indices.Length);
        var colours = new bool[source.Length];
        for (int p = 0; p < model.Parts.Length; p++)
        {
            var part = model.Parts[p];
            if (part.Material is { Hidden: true } || part.IndexCount <= 0 || part.FirstIndex < 0
                || part.FirstIndex + part.IndexCount > model.Indices.Length) { parts[p] = (kept.Count, 0); continue; }
            int from = kept.Count;
            bool colour = part.Material is { VertexColor: true };
            for (int i = 0; i + 2 < part.IndexCount; i += 3)
            {
                uint a = model.Indices[part.FirstIndex + i], b = model.Indices[part.FirstIndex + i + 1], c = model.Indices[part.FirstIndex + i + 2];
                if (a >= source.Length || b >= source.Length || c >= source.Length) continue;
                kept.Add(a); kept.Add(b); kept.Add(c);
                used[a] = used[b] = used[c] = 1;
                if (colour) colours[a] = colours[b] = colours[c] = true;
            }
            parts[p] = (from, kept.Count - from);
        }
        int n = 0;
        for (int i = 0; i < source.Length; i++) if (used[i] != 0) used[i] = ++n;
        var vertices = new Vertex[n];
        int bones = 1;
        Span<ushort> row = stackalloc ushort[4];
        for (int i = 0; i < source.Length; i++)
        {
            if (used[i] == 0) continue;
            ref readonly var v = ref source[i];
            var tangent = model.Tangents is { } t && i < t.Length ? t[i] : new Vector4(1, 0, 0, 1);
            var colour = colours[i] && model.Colors is { } c && i < c.Length ? Pack(c[i]) : 0xffffffffu;
            float sum = 0f;
            int nonzero = 0;
            for (int k = 0; k < 4; k++)
            {
                float w = v.Weights[k];
                row[k] = 0;
                if (w == 0f) continue;
                sum += w;
                nonzero++;
                int bone = v.Bone(k);
                row[k] = (ushort)(bone * 4);
                bones = Math.Max(bones, bone + 1);
            }
            var weights = v.Weights;
            var kind = sum == 0f ? VertexKind.Unweighted : nonzero == 1 && weights.X == 1f ? VertexKind.Single : VertexKind.Blend;
            vertices[used[i] - 1] = new Vertex
            {
                Position = v.Position, Normal = v.Normal, Tangent = tangent, Uv = v.Uv, Weights = weights, Color = colour,
                Row0 = row[0], Row1 = row[1], Row2 = row[2], Row3 = row[3], Kind = kind,
            };
        }
        var indices = kept.ToArray();
        for (int i = 0; i < indices.Length; i++) indices[i] = (uint)(used[indices[i]] - 1);
        return new PeopleMesh { Vertices = vertices, Indices = indices, Parts = parts, Bones = bones, Unused = source.Length - n };
    }

    /// <summary>A linear colour (0 to 1 a channel) as the GPU's RGBA8 vertex colour, R in the lowest byte, rounded.</summary>
    public static uint Pack(Vector4 c)
    {
        static uint B(float x) => (uint)(Math.Clamp(x, 0f, 1f) * 255f + 0.5f);
        return B(c.X) | B(c.Y) << 8 | B(c.Z) << 16 | B(c.W) << 24;
    }
}
