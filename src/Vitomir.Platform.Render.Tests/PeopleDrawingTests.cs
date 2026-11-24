// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Render.Characters;
using Vitomir.Platform.Render.Modern;

namespace Vitomir.Platform.Render.Tests;

/// <summary>
/// Made-up people for the drawing tests: a board of upright quads, each its own part with its own
/// <see cref="PartMaterial"/>, on one bone; and the small textures they name.
/// </summary>
internal static class TestPeople
{
    public const float Size = 0.5f, Gap = 0.62f, Bottom = 0.6f;

    /// <summary>A quad of the board: its material, and whether it faces away (+Y, toward the eye as the board is placed, when not).</summary>
    public readonly record struct Quad(PartMaterial Material, bool Away = false, Vector4? Colour = null);

    /// <summary>The quads side by side along X, the first at the left of the picture (+X), each in the XZ plane facing ±Y.</summary>
    public static SkinnedModel Board(params Quad[] quads)
    {
        var vertices = new List<SkinVertex>();
        var tangents = new List<Vector4>();
        var colours = new List<Vector4>();
        var indices = new List<uint>();
        var parts = new List<ModelPart>();
        for (int q = 0; q < quads.Length; q++)
        {
            float cx = Centre(q, quads.Length);
            var normal = quads[q].Away ? -Vector3.UnitY : Vector3.UnitY;
            uint first = (uint)vertices.Count;
            for (int k = 0; k < 4; k++)
            {
                float u = k is 1 or 2 ? 1f : 0f, v = k >= 2 ? 0f : 1f;   // v = 0 at the top (the image's first row)
                vertices.Add(new SkinVertex
                {
                    Position = new Vector3(cx + Size / 2 - u * Size, 0f, Bottom + (1f - v) * Size),   // u runs toward −X (the picture's right)
                    Normal = normal, Uv = new Vector2(u, v), Bones = SkinVertex.PackBones(0), Weights = new Vector4(1, 0, 0, 0),
                });
                tangents.Add(new Vector4(-1, 0, 0, 1));
                colours.Add(quads[q].Colour ?? Vector4.One);
            }
            int at = indices.Count;
            Triangle(vertices, indices, normal, first, first + 1, first + 2);
            Triangle(vertices, indices, normal, first, first + 2, first + 3);
            parts.Add(new ModelPart(at, 6, quads[q].Material.BaseColor ?? "", AlphaTest: false) { Material = quads[q].Material });
        }
        return new SkinnedModel
        {
            Name = "test board",
            Vertices = [.. vertices], Indices = [.. indices], Parts = [.. parts],
            Tangents = [.. tangents], Colors = [.. colours],
            Skeleton = new Skeleton { Bones = [new BoneInfo("root", -1, BoneTransform.Identity)], InverseBind = [Matrix4x4.Identity] },
            BoundsMin = new Vector3(-2, 0, 0), BoundsMax = new Vector3(2, 0, 2),
        };
    }

    /// <summary>A quad's centre along X.</summary>
    public static float Centre(int q, int count) => ((count - 1) / 2f - q) * Gap;

    /// <summary>A triangle wound counter-clockwise seen from where <paramref name="normal"/> points (the renderer's front face).</summary>
    static void Triangle(List<SkinVertex> v, List<uint> into, Vector3 normal, uint a, uint b, uint c)
    {
        var face = Vector3.Cross(v[(int)b].Position - v[(int)a].Position, v[(int)c].Position - v[(int)a].Position);
        if (Vector3.Dot(face, normal) < 0) (b, c) = (c, b);
        into.AddRange([a, b, c]);
    }

    /// <summary>A material: white unless given otherwise, roughness 0.6, dielectric, opaque, one-sided, no vertex colour.</summary>
    public static PartMaterial Plain(string name, string? baseColor = White) =>
        new(name, baseColor, null, null, Vector4.One, 0.6f, 0f, 0f, DoubleSided: false, VertexColor: false);

    public const string White = "people:test/white", Tilted = "people:test/tilted", Metal = "people:test/orm-metal",
                        Holes = "people:test/holes", Grey = "people:test/grey", Occluded = "people:test/orm-occluded";

    /// <summary>The test textures by name: white; a normal map leaning hard along the bitangent; ORM maps of a smooth metal and of a deep crevice; a white checker with holes; mid grey.</summary>
    public static TextureImage? Texture(string name) => name switch
    {
        White => Solid(name, 255, 255, 255, 255),
        Grey => Solid(name, 128, 128, 128, 255),
        Tilted => Solid(name, 128, 236, 166, 255),   // (0, 0.85, 0.30) tangent space: leaning along the bitangent (glTF's green)
        Metal => Solid(name, 255, 40, 255, 255),     // occlusion 1, roughness 0.16, metal 1
        Occluded => Solid(name, 15, 200, 0, 255),    // occlusion 0.06, roughness 0.78, metal 0
        Holes => Checker(name),
        _ => null,
    };

    static TextureImage Solid(string name, byte r, byte g, byte b, byte a)
    {
        var rgba = new byte[8 * 8 * 4];
        for (int i = 0; i < rgba.Length; i += 4) (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = (r, g, b, a);
        return new TextureImage(name, 8, 8, rgba);
    }

    static TextureImage Checker(string name)
    {
        var rgba = new byte[8 * 8 * 4];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                int i = (y * 8 + x) * 4;
                (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = (255, 255, 255, (byte)(((x / 2) + (y / 2)) % 2 == 0 ? 255 : 0));
            }
        return new TextureImage(name, 8, 8, rgba);
    }
}

/// <summary>A stand-in for the GPU's textures: every name a number of its own, its colour space remembered.</summary>
internal sealed class CountingTextures : ICharacterTextures
{
    public readonly Dictionary<(string, bool), int> Seen = [];
    public readonly Dictionary<string, float> Cutouts = [];

    public int Texture(string? name, bool srgb, float cutout = 0f)
    {
        if (string.IsNullOrEmpty(name)) return -1;
        if (cutout > 0f) Cutouts[name] = cutout;
        if (!Seen.TryGetValue((name, srgb), out int n)) Seen[(name, srgb)] = n = Seen.Count;
        return n;
    }
}

/// <summary>The people's path on the CPU: what is skinned, what is drawn and how, and that 1.1's is as it was.</summary>
public sealed class PeopleBatchTests
{
    [Fact]
    public void A_part_draws_by_its_material_and_a_hidden_part_not_at_all()
    {
        var textures = new CountingTextures();
        var batch = new CharacterBatch(textures, []);
        var hair = new PartMaterial("hair", TestPeople.White, TestPeople.Tilted, TestPeople.Metal, new Vector4(0.1f, 0.05f, 0.03f, 0.9f),
                                    0.85f, 0f, 0.35f, DoubleSided: true, VertexColor: true) { NormalScale = 0.6f };
        var cornea = TestPeople.Plain("cornea", null) with { Hidden = true };
        var model = TestPeople.Board(new(hair), new(cornea), new(TestPeople.Plain("skin")));
        batch.Build([new DrawnCharacter { Model = model, World = Matrix4x4.Identity, Skin = [Matrix4x4.Identity] }]);
        Assert.Equal(2, batch.Draws.Count);
        var d = batch.Draws[0];
        Assert.Equal(textures.Seen[(TestPeople.White, true)], d.Texture);
        Assert.Equal(0.35f, textures.Cutouts[TestPeople.White]);      // its mips keep its coverage at its cut-off
        Assert.Equal(textures.Seen[(TestPeople.Tilted, false)], d.Normal);      // the maps linear
        Assert.Equal(textures.Seen[(TestPeople.Metal, false)], d.Orm);
        Assert.Equal(new Vector4(0.1f, 0.05f, 0.03f, 0.9f), d.BaseColorFactor);
        Assert.Equal((0.35f, true, 0.85f, 0f, 0.6f, CharacterBatch.PeopleSpecular), (d.AlphaCutoff, d.DoubleSided, d.Roughness, d.Metallic, d.NormalScale, d.Specular));
        var skin = batch.Draws[1];
        Assert.Equal((-1f, false, -1, -1, Vector4.One), (skin.AlphaCutoff, skin.DoubleSided, skin.Normal, skin.Orm, skin.BaseColorFactor));
        // The cornea's four vertices are not skinned at all: two quads' worth.
        Assert.Equal(8, batch.Vertices.Length);
        Assert.Equal(12, batch.Indices.Length);
        Assert.All(batch.Indices.ToArray(), i => Assert.InRange(i, 0u, 7u));
    }

    [Fact]
    public void A_persons_tangents_turn_with_the_normals_its_colours_are_carried_and_only_used_vertices_are_skinned()
    {
        var tint = new PartMaterial("dyed", TestPeople.White, null, null, Vector4.One, 0.6f, 0f, 0f, false, VertexColor: true);
        var plain = TestPeople.Plain("plain");
        var model = TestPeople.Board(new(tint, Colour: new Vector4(1f, 0.5f, 0.25f, 1f)), new(plain, Colour: new Vector4(0.2f, 0.2f, 0.2f, 1f)));
        // A vertex no triangle names (the body under the clothes): never skinned.
        model = new SkinnedModel
        {
            Name = model.Name, Vertices = [.. model.Vertices, model.Vertices[0]], Indices = model.Indices, Parts = model.Parts,
            Tangents = [.. model.Tangents!, new Vector4(1, 0, 0, 1)], Colors = [.. model.Colors!, Vector4.One], Skeleton = model.Skeleton,
        };
        var mesh = PeopleMesh.For(model)!;
        Assert.Equal(8, mesh.Vertices.Length);
        Assert.Equal(1, mesh.Unused);
        // A quarter turn about Z in the skin, a half turn in the world: the tangent (−1, 0, 0) goes to (0, −1, 0) then (0, 1, 0).
        var turn = Matrix4x4.CreateRotationZ(MathF.PI / 2);
        var world = Matrix4x4.CreateRotationZ(MathF.PI) * Matrix4x4.CreateTranslation(1, 2, 3);
        var into = new ModernVertex[mesh.Vertices.Length];
        var (min, max) = CharacterBatch.SkinPerson(mesh, [turn], world, new Matrix4x4[mesh.Bones], into);
        for (int i = 0; i < into.Length; i++)
        {
            var src = mesh.Vertices[i];
            var expected = Vector3.Transform(Vector3.Transform(src.Position, turn), world) * ModernMath.UnitsPerMetre;
            Assert.True(Vector3.Distance(expected, into[i].Position) < 1e-3f, $"{expected} vs {into[i].Position}");
            var n = Vector3.TransformNormal(Vector3.TransformNormal(src.Normal, turn), world);
            Assert.True(Vector3.Distance(n, into[i].Normal) < 1e-5f);
            Assert.True(Vector3.Distance(new Vector3(0, 1, 0), new Vector3(into[i].Tangent.X, into[i].Tangent.Y, into[i].Tangent.Z)) < 1e-5f);
            Assert.Equal(1f, into[i].Tangent.W);
            Assert.True(min.X <= into[i].Position.X && max.X >= into[i].Position.X);
        }
        // The dyed quad's colour as the GPU takes it (linear RGBA8, R lowest); the plain quad's material asks for none: white.
        Assert.Equal(PeopleMesh.Pack(new Vector4(1f, 0.5f, 0.25f, 1f)), into[0].Color);
        Assert.Equal(0xff4080ffu, into[0].Color);
        Assert.Equal(0xffffffffu, into[7].Color);
    }

    [Fact]
    public void Four_bones_blend_as_the_sum_of_four_transforms_and_a_bone_past_the_skin_is_the_identity()
    {
        var model = TestPeople.Board(new TestPeople.Quad(TestPeople.Plain("p")));
        var vs = model.Vertices;
        vs[0].Bones = SkinVertex.PackBones(0, 1, 2, 200);
        vs[0].Weights = new Vector4(0.4f, 0.3f, 0.2f, 0.1f);
        Matrix4x4[] skin = [Matrix4x4.CreateTranslation(1, 0, 0), Matrix4x4.CreateRotationX(0.7f), Matrix4x4.CreateScale(1.5f) * Matrix4x4.CreateTranslation(0, 0, 2)];
        var mesh = PeopleMesh.For(model)!;
        Assert.Equal(201, mesh.Bones);
        var into = new ModernVertex[mesh.Vertices.Length];
        CharacterBatch.SkinPerson(mesh, skin, Matrix4x4.Identity, new Matrix4x4[mesh.Bones], into);
        var p = vs[0].Position;
        var expected = (Vector3.Transform(p, skin[0]) * 0.4f + Vector3.Transform(p, skin[1]) * 0.3f + Vector3.Transform(p, skin[2]) * 0.2f + p * 0.1f) * ModernMath.UnitsPerMetre;
        Assert.True(Vector3.Distance(expected, into[0].Position) < 1e-3f, $"{expected} vs {into[0].Position}");
    }

    [Fact]
    public void A_one_one_character_draws_as_it_did_before_the_people()
    {
        var textures = new CountingTextures();
        var batch = new CharacterBatch(textures, []);
        var model = TestColumn.Make();
        var skin = TestColumn.Bent(0.6f);
        var world = Matrix4x4.CreateRotationZ(0.3f) * Matrix4x4.CreateTranslation(5, 6, 0);
        batch.Build([new DrawnCharacter { Model = model, World = world, Skin = skin }]);
        Assert.Null(PeopleMesh.For(model));
        var expected = new ModernVertex[model.Vertices.Length];
        CharacterPass.Skin(model, skin, world, expected);
        Assert.Equal(expected, batch.Vertices.ToArray());     // every vertex as 1.1's path makes it, bit for bit
        Assert.Equal(model.Indices, batch.Indices.ToArray());
        var d = Assert.Single(batch.Draws);
        Assert.Equal(new ModernExtra.Draw(0, (uint)model.Indices.Length, d.Min, d.Max, textures.Seen[(TestColumn.TextureName, true)], -1f, true,
                                          CharacterPass.Roughness, 0f, CharacterPass.Specular), d);
        Assert.Equal((-1, -1, Vector4.One, 1f), (d.Normal, d.Orm, d.BaseColorFactor, d.NormalScale));
    }

    [Fact]
    public void In_first_person_a_persons_parts_are_one_sided_and_wound_outward_and_the_shadow_whole_and_as_the_material_says()
    {
        var batch = new CharacterBatch(new CountingTextures(), []);
        var cards = new PartMaterial("hair", TestPeople.White, null, null, Vector4.One, 0.8f, 0f, 0.35f, DoubleSided: true, VertexColor: false);
        var model = TestPeople.Board(new(cards), new(TestPeople.Plain("skin")));
        batch.Build([new DrawnCharacter { Model = model, World = Matrix4x4.Identity, Skin = [Matrix4x4.Identity], ShadowSkin = [Matrix4x4.Identity] }]);
        Assert.Equal(4, batch.Draws.Count);
        Assert.All(batch.Draws.Take(2), d => Assert.Equal((ModernExtra.DrawPasses.View, false), (d.Passes, d.DoubleSided)));
        Assert.Equal((ModernExtra.DrawPasses.Shadow, true), (batch.Draws[2].Passes, batch.Draws[2].DoubleSided));
        Assert.Equal((ModernExtra.DrawPasses.Shadow, false), (batch.Draws[3].Passes, batch.Draws[3].DoubleSided));
        var v = batch.Vertices;
        var ix = batch.Indices;
        for (int i = 0; i < 12; i += 3) Assert.True(CharacterPass.Outward(v, ix[i], ix[i + 1], ix[i + 2]));
    }

    [Fact]
    public void In_first_person_only_what_the_collapse_moves_is_skinned_twice_and_what_it_takes_is_left_out()
    {
        // Three quads: the first on the head alone, the second the neck (its top 70 % on the head, 30 % on the body), the third the body's.
        var plain = TestPeople.Plain("p");
        var board = TestPeople.Board(new(plain), new(plain), new(plain));
        var vs = board.Vertices;
        for (int i = 0; i < 4; i++) { vs[i].Bones = SkinVertex.PackBones(1); }
        foreach (int i in new[] { 4, 5, 6, 7 })
            if (vs[i].Position.Z > TestPeople.Bottom + 0.1f) { vs[i].Bones = SkinVertex.PackBones(0, 1); vs[i].Weights = new Vector4(0.3f, 0.7f, 0, 0); }
        var model = new SkinnedModel
        {
            Name = "headed", Vertices = vs, Indices = board.Indices, Parts = board.Parts, Tangents = board.Tangents, Colors = board.Colors,
            Skeleton = new Skeleton
            {
                Bones = [new BoneInfo("root", -1, BoneTransform.Identity), new BoneInfo("head", 0, BoneTransform.Identity)],
                InverseBind = [Matrix4x4.Identity, Matrix4x4.Identity],
            },
        };
        var whole = new[] { Matrix4x4.CreateRotationZ(0.2f), Matrix4x4.CreateRotationX(0.3f) * Matrix4x4.CreateTranslation(0, 0.1f, 0) };
        var seen = (Matrix4x4[])whole.Clone();
        var pivot = new Vector3(0, 0, 1);
        seen[1] = whole[1] * Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateScale(1e-3f) * Matrix4x4.CreateTranslation(pivot);   // as HeadCollapse
        var world = Matrix4x4.CreateTranslation(3, 2, 0);
        // By default a vertex with more than half its weight on the collapsed bones takes its triangles out of the picture:
        // the head's quad and the neck's (its top half on the head); the body's alone is seen, and all three cast.
        using (var cut = new CharacterBatch(new CountingTextures(), []))
        {
            cut.Build([new DrawnCharacter { Model = model, World = world, Skin = seen, ShadowSkin = whole }]);
            Assert.Equal(12, cut.Vertices.Length);
            Assert.Equal(4, cut.Draws.Count);
            Assert.Equal((ModernExtra.DrawPasses.View, 6u), (cut.Draws[0].Passes, cut.Draws[0].IndexCount));
            Assert.All(cut.Draws.Skip(1), d => Assert.Equal(ModernExtra.DrawPasses.Shadow, d.Passes));
        }
        // With a cut of 0.8 the neck stays: twelve vertices skinned whole, then the neck's two top ones again for the picture.
        using var batch = new CharacterBatch(new CountingTextures(), []) { FirstPersonCut = 0.8f };
        batch.Build([new DrawnCharacter { Model = model, World = world, Skin = seen, ShadowSkin = whole }]);
        Assert.Equal(14, batch.Vertices.Length);
        Assert.Equal(5, batch.Draws.Count);
        Assert.All(batch.Draws.Take(2), d => Assert.Equal((ModernExtra.DrawPasses.View, 6u), (d.Passes, d.IndexCount)));
        Assert.All(batch.Draws.Skip(2), d => Assert.Equal(ModernExtra.DrawPasses.Shadow, d.Passes));
        // Every vertex the picture reads is where skinning everything by the picture's skin puts it; the shadow's by the whole.
        var mesh = PeopleMesh.For(model)!;
        var view = FirstPersonMesh.For(mesh, seen, whole, 0.8f);
        Assert.Equal(2, view.Extra.Length);
        var bySeen = new ModernVertex[12];
        var byWhole = new ModernVertex[12];
        CharacterBatch.SkinPerson(mesh, seen, world, new Matrix4x4[256], bySeen);
        CharacterBatch.SkinPerson(mesh, whole, world, new Matrix4x4[256], byWhole);
        var v = batch.Vertices;
        var ix = batch.Indices;
        for (int i = 0; i < 12; i++)
        {
            var at = ix[(int)batch.Draws[i / 6].FirstIndex + i % 6];
            int source = at < 12 ? (int)at : view.Extra[at - 12];
            Assert.True(Vector3.Distance(bySeen[source].Position, v[(int)at].Position) < 1e-3f);
        }
        for (int i = 0; i < 12; i++) Assert.True(Vector3.Distance(byWhole[i].Position, v[i].Position) < 1e-3f);
        // The same collapse next frame: the same plan, kept; another cut, another plan.
        Assert.Same(view, FirstPersonMesh.For(mesh, seen, whole, 0.8f));
        Assert.NotSame(view, FirstPersonMesh.For(mesh, seen, whole));
    }

    [Fact]
    public void Drawing_a_frame_of_people_and_one_one_characters_allocates_nothing_once_warm()
    {
        var batch = new CharacterBatch(new CountingTextures(), []);
        var person = TestPeople.Board(new(TestPeople.Plain("a")), new(TestPeople.Plain("b") with { DoubleSided = true }));
        var frame = new List<DrawnCharacter>
        {
            new() { Model = TestColumn.Make(), World = Matrix4x4.Identity, Skin = TestColumn.Bent(0.3f) },
            new() { Model = person, World = Matrix4x4.CreateTranslation(2, 0, 0), Skin = [Matrix4x4.Identity] },
            new() { Model = person, World = Matrix4x4.CreateTranslation(4, 0, 0), Skin = [Matrix4x4.CreateScale(1e-3f)], ShadowSkin = [Matrix4x4.Identity] },
        };
        IReadOnlyList<DrawnCharacter> characters = frame;
        batch.Build(characters);
        batch.Build(characters);
        for (int round = 0; round < 2; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            batch.Build(characters);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }

    [Fact]
    public void A_texture_over_the_limit_is_halved_by_a_box_filter_a_colour_in_linear_light()
    {
        // Black and white texels side by side: their mean in linear light is 0.5, sRGB 188, not 128 (and a data map's 128).
        var rgba = new byte[2048 * 2048 * 4];
        for (int i = 0; i < rgba.Length; i += 8) { (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = (255, 255, 255, 255); rgba[i + 7] = 0; }
        var image = new TextureImage("t", 2048, 2048, rgba);
        var colour = CharacterTextureSize.Fit(image, 1024, srgb: true);
        Assert.Equal((1024, 1024), (colour.Width, colour.Height));
        Assert.Equal(188, colour.Rgba[0]);
        Assert.Equal(128, colour.Rgba[3]);      // alpha linearly
        var data = CharacterTextureSize.Fit(image, 1024, srgb: false);
        Assert.Equal(128, data.Rgba[0]);
        Assert.Same(image, CharacterTextureSize.Fit(image, 2048, srgb: true));
        var quarter = CharacterTextureSize.Fit(new TextureImage("t", 4096, 1024, new byte[4096 * 1024 * 4]), 1024, srgb: false);
        Assert.Equal((1024, 256), (quarter.Width, quarter.Height));
        // The limits a player can ask for, and the memory a texture takes with its mips.
        Assert.Equal([1024, 1024, 2048, 2048, 4096, 4096, 2048], new[] { 100, 1024, 2048, 3000, 4096, 9000, 0 }.Select(CharacterTextureSize.Limit));
        Assert.Equal(89_478_484L, CharacterTextureSize.GpuBytes(4096, 4096));
        Assert.Equal(4L * (16 + 4 + 1), CharacterTextureSize.GpuBytes(4, 4));
    }

    [Fact]
    public void A_cut_outs_mips_do_not_thin_and_a_dense_ones_are_the_box_filters()
    {
        // A quarter of the texels opaque, scattered (strands seen end on): a quarter of the image passes 0.35. The box filter's own
        // mips average toward a quarter's alpha (64) and soon pass almost nothing; the coverage-keeping mips pass about a quarter at
        // every level with texels enough to say, and something down to 4 × 4.
        var rgba = new byte[64 * 64 * 4];
        uint seed = 12345;
        for (int i = 0; i < rgba.Length; i += 4)
        {
            seed = seed * 1664525 + 1013904223;
            rgba[i] = rgba[i + 1] = rgba[i + 2] = 200;
            rgba[i + 3] = (byte)((seed >> 24) < 64 ? 255 : 0);
        }
        var image = new TextureImage("strands", 64, 64, rgba);
        float full = CharacterTextureSize.Coverage(rgba, 0.35f, 1f);
        Assert.InRange(full, 0.22f, 0.28f);
        var levels = CharacterTextureSize.CoverageMips(image, 0.35f, srgb: true);
        Assert.Equal(7, levels.Length);
        Assert.Same(rgba, levels[0]);
        var plain = CharacterTextureSize.Halve(CharacterTextureSize.Halve(CharacterTextureSize.Halve(image, true), true), true);
        Assert.True(CharacterTextureSize.Coverage(plain.Rgba, 0.35f, 1f) < 0.05f, "the box filter alone thins it to nearly nothing at 8 × 8");
        for (int l = 1; l < levels.Length; l++)
        {
            Assert.Equal((64 >> l) * (64 >> l) * 4, levels[l].Length);
            float share = CharacterTextureSize.Coverage(levels[l], 0.35f, 1f);
            // As near the image's quarter as the level's steps allow (a texel of level l is the mean of 4^l of the image's).
            if (l <= 3) Assert.True(MathF.Abs(share - full) < 0.08f, $"level {l}: {share:P0} passes, the image {full:P0}");
            if (l <= 4) Assert.True(share > 0f, $"level {l} keeps something");   // (a single texel's level is all or nothing)
        }
        // Three quarters opaque: the box filter thickens it (more passes far off); its mips are the box filter's own.
        var dense = (byte[])rgba.Clone();
        for (int i = 3; i < dense.Length; i += 4) dense[i] = (byte)(255 - dense[i]);
        var thick = new TextureImage("dense", 64, 64, dense);
        var kept = CharacterTextureSize.CoverageMips(thick, 0.35f, srgb: true);
        var box = thick;
        for (int l = 1; l < kept.Length; l++)
        {
            box = CharacterTextureSize.Halve(box, true);
            Assert.Equal(box.Rgba, kept[l]);
        }
    }

    /// <summary>
    /// The CPU's cost a frame of skinning a person the size of the player (about 70,000 vertices drawn, four bones each, on a
    /// skeleton of 158), as the people's path does it; written out, and held to a generous bound (the machine is shared). A timing
    /// test: left out of the plain run and run alone by the timing command (the README's Build, test and run).
    /// </summary>
    [Fact, Trait("Category", "Timing")]
    public void Skinning_a_person_the_size_of_the_player_costs_a_few_milliseconds()
    {
        const int count = 70_000, bones = 158;
        var random = new Random(7);
        var vertices = new SkinVertex[count];
        var tangents = new Vector4[count];
        var indices = new uint[count / 3 * 3];
        for (int i = 0; i < count; i++)
        {
            var w = new Vector4(random.NextSingle(), random.NextSingle(), random.NextSingle(), random.NextSingle());
            w /= w.X + w.Y + w.Z + w.W;
            vertices[i] = new SkinVertex
            {
                Position = new Vector3(random.NextSingle(), random.NextSingle(), random.NextSingle() * 1.8f), Normal = Vector3.UnitY,
                Bones = SkinVertex.PackBones(random.Next(bones), random.Next(bones), random.Next(bones), random.Next(bones)), Weights = w,
            };
            tangents[i] = new Vector4(1, 0, 0, 1);
        }
        for (int i = 0; i < indices.Length; i++) indices[i] = (uint)i;
        var model = new SkinnedModel
        {
            Name = "player-sized", Vertices = vertices, Indices = indices, Tangents = tangents,
            Parts = [new ModelPart(0, indices.Length, "", false) { Material = TestPeople.Plain("p") }],
            Skeleton = new Skeleton { Bones = [.. Enumerable.Range(0, bones).Select(b => new BoneInfo("b" + b, b - 1, BoneTransform.Identity))], InverseBind = new Matrix4x4[bones] },
        };
        var skin = Enumerable.Range(0, bones).Select(b => Matrix4x4.CreateRotationZ(b * 0.01f) * Matrix4x4.CreateTranslation(0, 0, b * 0.001f)).ToArray();
        using var batch = new CharacterBatch(new CountingTextures(), []);
        var frame = new List<DrawnCharacter> { new() { Model = model, World = Matrix4x4.CreateTranslation(3, 4, 0), Skin = skin } };
        // The fastest of many frames (the machine is shared: the slower ones measure the others' work), and the middle one.
        (double Fastest, double Middle) Cost(bool parallel)
        {
            batch.Parallel = parallel;
            for (int i = 0; i < 60; i++) batch.Build(frame);   // past the JIT's first tiers
            var times = new double[60];
            for (int i = 0; i < times.Length; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                batch.Build(frame);
                times[i] = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            }
            Array.Sort(times);
            return (times[0], times[times.Length / 2]);
        }
        var one = Cost(parallel: false);
        var many = Cost(parallel: true);
        Assert.NotNull(batch.HelpersFaster);     // timed both ways and kept the faster
        double ms = many.Fastest;
        var line = $"a person of {count:N0} vertices: allowed {1 + SkinWorkers.Helpers} threads {many.Fastest:0.00} ms a frame at best ({many.Middle:0.00} the middle; "
                   + $"the helpers {(batch.HelpersFaster == true ? "kept" : "dropped")}), on one {one.Fastest:0.00} ms ({one.Middle:0.00})";
        TestContext.Current.TestOutputHelper?.WriteLine(line);
        Assert.True(ms < 40, $"{ms:0.00} ms a frame");
    }
}

/// <summary>
/// Each kind of material drawn on the GPU (headless; skipped without a device or the East Gate): a normal map
/// lights a quad otherwise than the flat one, the ORM map's metal and smoothness change it, the vertex colour and the factor
/// tint it, a cut-out leaves holes, a hidden part is not there, a two-sided quad is seen from behind and a one-sided one not.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class PeopleDrawingTests
{
    sealed record Scene(PlatformRenderer Renderer, FrameView Frame, Matrix4x4 Place, int Width, int Height, byte[] Bare);

    /// <summary>
    /// The board's scene: the board 2.6 m ahead of the first start, the eye 2.6 m from it on one side or the other, looking a
    /// little down at it — the side where a plain quad is the brighter, so its face is toward the sun (what a normal map and
    /// metal show is the sun's light on them).
    /// </summary>
    static Scene Open(int width = 480, int height = 200)
    {
        var level = Stills.Level();
        var (w, h) = Stills.Size(width, height);
        var (start, yaw) = Stills.FirstStart(level);
        var ahead = new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0);
        var board = start + ahead * 2.6f;
        // One renderer for both sides (the first made owns SDL's video: it must outlive any other).
        var r = Stills.Headless(w, h, Environment.GetEnvironmentVariable("VITOMIR_RENDER_QUALITY") ?? "low");
        r.LoadLevel(level);
        r.CharacterTextures = TestPeople.Texture;
        Scene At(bool back)
        {
            float look = back ? yaw + MathF.PI : yaw;
            var eye = board - new Vector3(MathF.Cos(look), MathF.Sin(look), 0) * 2.6f + new Vector3(0, 0, 1.3f);
            var view = new ViewPoint(eye, look, -8f * MathF.PI / 180f, 50f);
            var place = Matrix4x4.CreateRotationZ(look + MathF.PI / 2) * Matrix4x4.CreateTranslation(board);   // its +Y toward the eye
            var frame = new FrameView { View = view, ShowReticle = false };
            Stills.Still(r, frame);
            return new Scene(r, frame, place, w, h, r.Pixels(out _, out _));
        }
        float Brightness(Scene scene)
        {
            var lit = Draw(scene, TestPeople.Board(new TestPeople.Quad(TestPeople.Plain("plain", TestPeople.Grey))), "people-sun");
            var b = Box(scene, 0, 1);
            scene.Frame.Characters.Clear();
            return Stills.Mean(lit, scene.Width, b.X0, b.Y0, b.X1, b.Y1).Length();
        }
        return Brightness(At(back: false)) >= Brightness(At(back: true)) ? At(back: false) : At(back: true);
    }

    /// <summary>The middle of quad <paramref name="q"/> of <paramref name="count"/> in the picture: a box a third of its size.</summary>
    static (int X0, int Y0, int X1, int Y1) Box(Scene s, int q, int count)
    {
        var vp = ModernRenderer.ViewProjection(FreeCamera.From(s.Frame.View), s.Width / (float)s.Height);
        (float X, float Y) At(Vector3 local)
        {
            var world = Vector3.Transform(local, s.Place) * ModernMath.UnitsPerMetre;
            var c = Vector4.Transform(new Vector4(world, 1), vp);
            return ((c.X / c.W * 0.5f + 0.5f) * s.Width, (0.5f - c.Y / c.W * 0.5f) * s.Height);
        }
        float cx = TestPeople.Centre(q, count), third = TestPeople.Size / 6f, mid = TestPeople.Bottom + TestPeople.Size / 2;
        var a = At(new Vector3(cx + third, 0, mid + third));
        var b = At(new Vector3(cx - third, 0, mid - third));
        return ((int)MathF.Min(a.X, b.X), (int)MathF.Min(a.Y, b.Y), (int)MathF.Ceiling(MathF.Max(a.X, b.X)), (int)MathF.Ceiling(MathF.Max(a.Y, b.Y)));
    }

    static byte[] Draw(Scene s, SkinnedModel model, string name)
    {
        s.Frame.Characters.Clear();
        s.Frame.Characters.Add(new DrawnCharacter { Model = model, World = s.Place, Skin = [Matrix4x4.Identity], Entity = 1 });
        Stills.Still(s.Renderer, s.Frame);
        Stills.Save(s.Renderer, name);
        return s.Renderer.Pixels(out _, out _);
    }

    static float Changed(Scene s, byte[] px, (int X0, int Y0, int X1, int Y1) box, int tolerance = 10)
    {
        int yes = 0, n = 0;
        for (int y = box.Y0; y < box.Y1; y++)
            for (int x = box.X0; x < box.X1; x++)
            {
                int i = (y * s.Width + x) * 4;
                if (Math.Abs(px[i] - s.Bare[i]) > tolerance || Math.Abs(px[i + 1] - s.Bare[i + 1]) > tolerance || Math.Abs(px[i + 2] - s.Bare[i + 2]) > tolerance) yes++;
                n++;
            }
        return n == 0 ? 0f : (float)yes / n;
    }

    [Fact]
    public void Each_kind_of_material_is_drawn_as_it_says()
    {
        var s = Open(560, 200);
        using var r = s.Renderer;
        var plain = TestPeople.Plain("plain", TestPeople.Grey);
        var quads = new TestPeople.Quad[]
        {
            new(plain),                                                                                    // 0: the reference
            new(plain with { Name = "bumped", Normal = TestPeople.Tilted }),                               // 1: a normal map
            new(plain with { Name = "metal", Orm = TestPeople.Metal, Metallic = 1f, Roughness = 1f }),     // 2: an ORM map: smooth metal (the factors 1, as the files')
            new(plain with { Name = "dyed", VertexColor = true }, Colour: new Vector4(1f, 0.15f, 0.1f, 1f)),   // 3: vertex colour, red
            new(plain with { Name = "blue", BaseColorFactor = new Vector4(0.15f, 0.25f, 1f, 1f) }),         // 4: the factor, blue
            new(plain with { Name = "holes", BaseColor = TestPeople.Holes, AlphaCutoff = 0.5f }),          // 5: a cut-out
            new(plain with { Name = "cornea", Hidden = true }),                                            // 6: hidden
            new(plain with { Name = "crevice", Orm = TestPeople.Occluded, Roughness = 1f }),               // 7: an ORM map: occluded
        };
        var px = Draw(s, TestPeople.Board(quads), "people-materials");
        Vector3 M(int q) { var b = Box(s, q, quads.Length); return Stills.Mean(px, s.Width, b.X0, b.Y0, b.X1, b.Y1); }
        var reference = M(0);
        Assert.True(Changed(s, px, Box(s, 0, quads.Length)) > 0.9f, $"the plain quad is drawn ({reference})");
        Assert.True((M(1) - reference).Length() > 12f, $"the normal map lights it otherwise ({M(1)} vs {reference})");
        Assert.True((M(2) - reference).Length() > 12f, $"the ORM map's smooth metal looks otherwise ({M(2)} vs {reference})");
        var red = M(3);
        Assert.True(red.X > red.Y + 25 && red.X > red.Z + 25, $"the vertex colour tints it red ({red})");
        var blue = M(4);
        Assert.True(blue.Z > blue.X + 20, $"the factor tints it blue ({blue})");
        float holes = Changed(s, px, Box(s, 5, quads.Length));
        Assert.InRange(holes, 0.2f, 0.8f);                        // the checker's holes show what is behind
        Assert.True(Changed(s, px, Box(s, 6, quads.Length)) < 0.05f, "the hidden part is not drawn");
        Assert.True(M(7).Length() < reference.Length() - 5f, $"the ORM map's occlusion darkens it ({M(7)} vs {reference})");
        Assert.True(r.CharacterTextureBytes > 0);
    }

    [Fact]
    public void A_two_sided_part_is_seen_from_behind_and_a_one_sided_one_is_not()
    {
        var s = Open();
        using var r = s.Renderer;
        var plain = TestPeople.Plain("plain");
        var quads = new TestPeople.Quad[] { new(plain with { DoubleSided = true }, Away: true), new(plain, Away: true), new(plain) };
        var px = Draw(s, TestPeople.Board(quads), "people-sides");
        Assert.True(Changed(s, px, Box(s, 0, 3)) > 0.9f, "the two-sided quad facing away is drawn");
        Assert.True(Changed(s, px, Box(s, 1, 3)) < 0.05f, "the one-sided quad facing away is not");
        Assert.True(Changed(s, px, Box(s, 2, 3)) > 0.9f, "the one-sided quad facing the eye is");
    }

    [Fact]
    public void The_same_pixels_under_two_names_are_uploaded_once_and_a_texture_over_the_limit_smaller()
    {
        var level = Stills.Level();
        // The board 2.5 m ahead of the level's start, in view: a person out of view is not skinned, and its
        // textures not uploaded (at the world's origin the board was out of the start's view: nothing uploaded on the East Gate).
        var (start, yaw) = Stills.FirstStart(level);
        var ahead = Matrix4x4.CreateTranslation(start + new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0f) * 2.5f);
        long Bytes(int limit, bool twice)
        {
            using var r = PlatformRenderer.CreateHeadless(64, 36, new RendererOptions(Quality: "low", CharacterTextureLimit: limit));
            r.LoadLevel(level);
            var big = new TextureImage("people:big", 4096, 2048, new byte[4096 * 2048 * 4]);
            var again = big with { Name = "people:big-again" };
            r.CharacterTextures = n => n == big.Name ? big : n == again.Name ? again : null;
            var m = TestPeople.Plain("a", big.Name);
            var quads = twice ? new TestPeople.Quad[] { new(m), new(m with { BaseColor = again.Name }) } : [new(m)];
            var frame = new FrameView { View = Stills.StartView(level), ShowReticle = false };
            frame.Characters.Add(new DrawnCharacter { Model = TestPeople.Board(quads), World = ahead, Skin = [Matrix4x4.Identity] });
            r.Draw(frame);
            Assert.Equal(0, r.CharacterCost.Culled);   // in view: skinned, its textures asked for
            return r.CharacterTextureBytes;
        }
        Assert.Equal(CharacterTextureSize.GpuBytes(4096, 2048), Bytes(4096, twice: false));
        Assert.Equal(CharacterTextureSize.GpuBytes(4096, 2048), Bytes(4096, twice: true));
        Assert.Equal(CharacterTextureSize.GpuBytes(2048, 1024), Bytes(2048, twice: false));
    }
}
