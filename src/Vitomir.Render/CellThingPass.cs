// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using SDL;
using Vitomir.Engine.Scene;
using Vitomir.Render.Modern;

namespace Vitomir.Render;

/// <summary>
/// **Ours.** 1.1's characters and things in a cell level, drawn by the modern path (the levels design,
/// sections 2 and 6: "1.1's characters and effects draw inside the new path with their own art: the
/// same models, lit by the new lights"). Each frame, after the game has posed its models
/// (<see cref="WorldRenderer.UploadAnimated"/>), the thing batches <see cref="WorldRenderer.ModernDrawable"/>
/// allows — the opaque and cut-out single-texture models: bodies, heads, held weapons, props — are
/// copied into <see cref="Extra"/> in world space with their normals (a skinned model's posed by its
/// skeleton, <c>ThingAnimation.PoseNormals</c>, as 1.1's own lighting poses them), and the modern
/// renderer draws them with its world: the sun through the cascaded shadow maps, which they receive and
/// cast, the ambient light with ambient occlusion, the clustered torches, the haze, the tone mapping and
/// the grade. The game's thing pass then draws those batches into its depth buffer only
/// (<see cref="WorldRenderer.DrawnElsewhere"/>), so the shells, the hover glow, the translucent batches,
/// the particles and the rest of 1.1's effects are drawn over them as before.
/// <para>Materials: the model's texture is the base colour (sRGB, so its painted colour is the
/// surface's albedo), with no normal, roughness or occlusion map — 1.1's models have none. Their
/// surfaces are given a constant roughness and reflectance (<see cref="Roughness"/>,
/// <see cref="Specular"/>); nothing is metal.</para>
/// </summary>
public sealed class CellThingPass : IDisposable
{
    /// <summary>The roughness of every surface drawn here: cloth, skin and leather (ours).</summary>
    public const float Roughness = 0.7f;
    /// <summary>What held things (weapons, the off hand) are given: smoother, so a blade catches the sky (ours).</summary>
    public const float HeldRoughness = 0.45f;
    /// <summary>The dielectric reflectance (KHR_materials_specular's factor on glTF's 4%): a matte 2% (ours).</summary>
    public const float Specular = 0.5f;

    readonly WorldRenderer world;
    readonly SceneMesh mesh;
    readonly MaterialCache materials;
    /// <summary>The batches (by number) drawn here this frame.</summary>
    readonly HashSet<int> drawn = [];
    readonly Dictionary<(int Material, int Image), int> textures = [];
    readonly Dictionary<uint, SceneVertex[]> still = [];
    readonly Dictionary<int, (Vector3 Min, Vector3 Max, Matrix4x4 Placement)> things = [];
    readonly List<uint> indices = [];
    ModernVertex[] vertices = [];
    Vector3[] normals = [];
    uint[] meshIndices = [];
    int indexRevision = -1;

    /// <summary>What the modern renderer draws with the world this frame.</summary>
    public ModernExtra Extra { get; }

    /// <summary>How many thing batches the last frame drew here.</summary>
    public int LastBatches => drawn.Count;

    /// <param name="mesh">The cell level's mesh and <paramref name="materials"/> its cache: this pass, the cell levels', reads
    /// the engine's posed and lit slices itself (the 1.1 look's <paramref name="world"/> reads only the snapshot).</param>
    public CellThingPass(Gpu gpu, WorldRenderer world, SceneMesh mesh, MaterialCache materials)
    {
        this.world = world;
        this.mesh = mesh;
        this.materials = materials;
        Extra = new ModernExtra(gpu);
        world.DrawnElsewhere = b => drawn.Contains(b);
    }

    /// <summary>
    /// Whether a cell level's things are drawn this way (ours). <c>VITOMIR_CELL_THINGS=1.1</c> keeps
    /// 1.1's thing pass and model lighting, for comparing the two.
    /// </summary>
    public static bool Wanted => Environment.GetEnvironmentVariable("VITOMIR_CELL_THINGS") is not "1.1";

    /// <summary>
    /// This frame's geometry into <see cref="Extra"/> and up to the GPU, on <paramref name="cmd"/> outside
    /// any render pass. Call after the models are posed and before the modern renderer draws.
    /// </summary>
    public unsafe void Prepare(SDL_GPUCommandBuffer* cmd)
    {
        drawn.Clear();
        things.Clear();
        Extra.Draws.Clear();
        var batches = world.Batches;
        for (int bi = 0; bi < batches.Count; bi++)
            if (world.ModernDrawable(bi))
            {
                var b = batches[bi];
                drawn.Add(bi);
                if (!things.ContainsKey(b.Thing)) things[b.Thing] = (new Vector3(float.MaxValue), new Vector3(float.MinValue), world.Placement(b));
            }
        if (drawn.Count == 0) return;
        if (mesh.Revision != indexRevision) { meshIndices = mesh.Indices; indexRevision = mesh.Revision; }
        if (vertices.Length < mesh.VertexCount) vertices = new ModernVertex[mesh.VertexCount];

        // What is posed this frame: each animating model and what it carries, by where its vertices start.
        var posed = new Dictionary<uint, (SceneVertex[] Vertices, Engine.Anim.ThingAnimation Animation, Formats.NodModel Model, Matrix4x4 Built)>();
        int count = mesh.Animated.Count;
        for (int i = 0; i < count; i++)
        {
            var a = mesh.Animated[i];
            posed[a.FirstVertex] = (a.Vertices, a.Animation, a.Animation.Model, a.World);
            foreach (var part in a.Carried) posed[part.FirstVertex] = (part.Vertices, a.Animation, part.Model, a.World);
        }

        foreach (var slice in mesh.Lit)
        {
            if (!things.TryGetValue(slice.Thing, out var t)) continue;
            int n = slice.Normals.Length;
            SceneVertex[] source;
            ReadOnlySpan<Vector3> sliceNormals;
            Matrix4x4 rotation = Matrix4x4.Identity;
            if (posed.TryGetValue(slice.First, out var p))
            {
                source = p.Vertices;
                if (normals.Length < p.Model.Vertices.Length) normals = new Vector3[p.Model.Vertices.Length];
                p.Animation.PoseNormals(p.Model, normals);
                sliceNormals = normals;
                rotation = p.Built with { M41 = 0, M42 = 0, M43 = 0 };
            }
            else
            {
                if (!still.TryGetValue(slice.First, out var run))
                {
                    run = new SceneVertex[n];
                    mesh.CopyVertices(slice.First, run);
                    still[slice.First] = run;
                }
                source = run;
                sliceNormals = slice.Normals;
            }
            n = Math.Min(n, Math.Min(source.Length, sliceNormals.Length));
            var place = t.Placement;
            bool moved = !place.IsIdentity;
            for (int i = 0; i < n; i++)
            {
                var v = source[i];
                var normal = rotation.IsIdentity ? sliceNormals[i] : Vector3.TransformNormal(sliceNormals[i], rotation);
                var at = v.Position;
                if (moved) { at = Vector3.Transform(at, place); normal = Vector3.TransformNormal(normal, place); }
                float len = normal.Length();
                normal = len > 1e-6f ? normal / len : Vector3.UnitZ;
                vertices[slice.First + i] = new ModernVertex
                {
                    Position = at, Normal = normal, Tangent = new Vector4(1, 0, 0, 1), Uv0 = v.Uv, Uv1 = v.LightmapUv, Color = 0xffffffff,
                };
                t.Min = Vector3.Min(t.Min, at);
                t.Max = Vector3.Max(t.Max, at);
            }
            things[slice.Thing] = t;
        }

        // Each batch's indices, gathered into one short list.
        indices.Clear();
        for (int bi = 0; bi < batches.Count; bi++)
        {
            var b = batches[bi];
            if (!drawn.Contains(bi) || b.FirstIndex + b.IndexCount > meshIndices.Length) continue;
            var box = things[b.Thing];
            if (box.Min.X > box.Max.X) continue;
            uint first = (uint)indices.Count;
            for (uint i = 0; i < b.IndexCount; i++) indices.Add(meshIndices[b.FirstIndex + i]);
            var mat = materials.Items[b.Material];
            bool held = b.Slot != Engine.World.NamedShapes.Body;
            Extra.Draws.Add(new ModernExtra.Draw(first, b.IndexCount, box.Min, box.Max, Texture(b.Material, mat),
                                                 mat.Traits.Masked ? WorldRenderer.AlphaTestThreshold : -1f, world.TwoSidedBatch(b),
                                                 held ? HeldRoughness : Roughness, 0f, Specular));
        }
        Extra.Upload(cmd, vertices.AsSpan(0, mesh.VertexCount), System.Runtime.InteropServices.CollectionsMarshal.AsSpan(indices));
    }

    /// <summary>A material's current image as a modern texture, made the first time it is drawn; −1 for a missing image (white).</summary>
    int Texture(int material, LoadedMaterial mat)
    {
        int image = mat.CurrentImage;
        if (textures.TryGetValue((material, image), out int have)) return have;
        int made = mat.Image is { } img ? Extra.AddTexture(img.Width, img.Height, TextureLayout.BottomRowFirst(img)) : -1;
        textures[(material, image)] = made;
        return made;
    }

    public void Dispose()
    {
        world.DrawnElsewhere = null;
        Extra.Dispose();
    }
}
