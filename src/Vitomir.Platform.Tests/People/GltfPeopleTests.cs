// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text.Json;
using Vitomir.Formats.Gltf;
using Vitomir.Formats.Npy;
using Vitomir.Formats.Png;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.People;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Tests.People;

/// <summary>
/// The glTF reader, the PNG decoder, the <c>.npz</c> reader and the people loader on files made in the test
/// (<see cref="TestPeople"/>): read back exactly, and converted to the platform's model space as the brief says (glTF
/// (x, y, z) to (−x, z, y), quaternions (−qx, qz, qy, qw)), merged, hidden and sampled as the task says. No model files needed.
/// </summary>
public sealed class GltfPeopleTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "vitomir-people-" + Guid.NewGuid().ToString("N"));

    public GltfPeopleTests() => TestPeople.Write(folder);

    public void Dispose()
    {
        try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
    }

    static void Near(Vector3 expected, Vector3 actual, float within = 1e-5f) =>
        Assert.True(Vector3.Distance(expected, actual) <= within, $"expected {expected}, got {actual}");

    static void Near(Quaternion expected, Quaternion actual, float within = 1e-5f) =>
        Assert.True(MathF.Abs(MathF.Abs(Quaternion.Dot(expected, actual)) - 1f) <= within, $"expected {expected}, got {actual}");

    [Fact]
    public void A_glb_made_here_reads_back_exactly()
    {
        var file = GltfFile.Read(TestPeople.Base());
        Assert.Equal("Vitomir tests", file.Generator);
        Assert.Equal("2.0", file.Version);
        Assert.Equal("""{"made":"test"}""", file.Extras);
        Assert.Equal(["KHR_materials_ior", "KHR_materials_transmission"], file.ExtensionsUsed);
        Assert.Equal(0, file.Scene);

        // Nodes: names, transforms (a matrix kept as given), children and parents, extras raw.
        Assert.Equal(["root", "pelvis", "hand_r", "thigh_l", "socket_hand_r", "skeleton_human", "body", "eyes"], file.Nodes.Select(n => n.Name));
        var pelvis = file.Nodes[1];
        Assert.Equal(TestPeople.PelvisAt, pelvis.Translation);
        Assert.Equal(TestPeople.PelvisTurn, pelvis.Rotation);
        Assert.Equal(Vector3.One, pelvis.Scale);
        Assert.Equal([2, 3], pelvis.Children);
        Assert.Equal("""{"vitomir":{"note":1}}""", pelvis.Extras);
        Assert.Equal(1, file.Parents[2]);
        Assert.Equal(5, file.Parents[0]);
        Assert.Equal(-1, file.Parents[5]);
        Assert.Equal(new Vector3(0, 0.05f, 0), file.Nodes[4].Matrix!.Value.Translation);
        Assert.Equal(new Vector3(0, 0.05f, 0), file.Nodes[4].LocalMatrix.Translation);
        Assert.Equal([5, 0, 1, 2, 4, 3, 6, 7], file.SceneOrder());
        var world = TestPeople.World();
        Assert.True(Vector3.Distance(world[2].Translation, file.WorldMatrix(2).Translation) < 1e-6f);

        // The skin and its inverse binds.
        var skin = Assert.Single(file.Skins);
        Assert.Equal([0, 1, 2, 3], skin.Joints);
        var ibm = file.ReadMatrices(skin.InverseBindMatrices);
        Matrix4x4.Invert(world[2], out var handInverse);
        Assert.Equal(handInverse, ibm[2]);

        // The body: every attribute as stored; normalised bytes to 0..1; joints raw; strided UVs; a sparse target with no view.
        var body = file.Meshes[0];
        Assert.Equal("body", body.Name);
        Assert.Equal(["jawOpen"], body.TargetNames);
        Assert.Equal([0f], body.Weights);
        var prim = Assert.Single(body.Primitives);
        Assert.Equal(4, prim.Mode);
        Assert.Equal(0, prim.Material);
        Assert.Equal(new Vector3(0, 1.2f, 0.05f), file.ReadVector3(prim.Attribute("POSITION"))[2]);
        Assert.Equal(new Vector4(1, 0, 0, -1), file.ReadVector4(prim.Attribute("TANGENT"))[2]);
        Assert.Equal([new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 1), new Vector2(0.5f, 0)], file.ReadVector2(prim.Attribute("TEXCOORD_0")));
        Assert.Equal(new Vector4(1f, 128 / 255f, 0f, 1f), file.ReadVector4(prim.Attribute("COLOR_0"))[0]);
        Assert.Equal(new Vector4(51 / 255f, 102 / 255f, 153 / 255f, 204 / 255f), file.ReadVector4(prim.Attribute("COLOR_0"))[3]);
        Assert.Equal([1u, 0, 0, 0, 2, 1, 0, 0, 1, 0, 0, 0, 3, 0, 0, 0], file.ReadUInts(prim.Attribute("JOINTS_0")));
        Assert.Equal(128 / 255f, file.ReadFloats(prim.Attribute("WEIGHTS_0"))[4]);
        Assert.Equal([0u, 1, 2, 1, 0, 3], file.ReadUInts(prim.Indices));
        var target = file.ReadVector3(prim.Targets[0]["POSITION"]);
        Assert.Equal([Vector3.Zero, Vector3.Zero, new Vector3(0, 0.01f, 0), Vector3.Zero], target);
        Assert.Equal([0u, 1, 2], file.ReadUInts(file.Meshes[1].Primitives[0].Indices));   // unsigned bytes
        Assert.Equal([0u, 1, 2], file.ReadUInts(file.Meshes[1].Primitives[1].Indices));   // unsigned ints

        // Materials: factors, every slot, alpha, sides, extensions by name and as written, extras.
        var m = file.Materials[0];
        Assert.Equal("skin", m.Name);
        Assert.Equal(new Vector4(1, 0.9f, 0.8f, 1), m.BaseColorFactor);
        Assert.Equal(0, m.BaseColorTexture!.Index);
        Assert.Equal(0.5f, m.NormalTexture!.Scale);
        Assert.Equal(0.8f, m.OcclusionTexture!.Scale);
        Assert.Equal(2, m.MetallicRoughnessTexture!.Index);
        Assert.Equal((0f, 0.7f), (m.MetallicFactor, m.RoughnessFactor));
        Assert.Equal((GltfAlphaMode.Mask, 0.4f, true), (m.AlphaMode, m.AlphaCutoff, m.DoubleSided));
        Assert.Equal(["KHR_materials_ior"], m.Extensions);
        Assert.True(m.Uses("KHR_materials_ior"));
        Assert.Contains("\"ior\"", m.ExtensionsJson);
        Assert.Equal("""{"note":"skin"}""", m.Extras);
        Assert.Equal((GltfAlphaMode.Blend, 0.5f, 1f, 1f), (file.Materials[2].AlphaMode, file.Materials[2].AlphaCutoff, file.Materials[2].MetallicFactor, file.Materials[2].RoughnessFactor));
        Assert.Equal(33071, file.Samplers[0].WrapS);
        Assert.Equal(10497, file.Samplers[0].WrapT);
        Assert.Equal(-1, file.Textures[2].Sampler);

        // Images: the PNG bytes as written, decoded to the pixels they were made from.
        Assert.Equal(["skin_basecolor", "skin_normal", "skin_orm"], file.Images.Select(i => i.Name));
        Assert.Equal(TestPeople.SkinPng, file.ImageBytes(0).ToArray());
        Assert.Equal(TestPeople.Pixels, PngDecoder.Decode(file.ImageBytes(0).AsMemory(), out int w, out int h));
        Assert.Equal((2, 2), (w, h));
    }

    [Fact]
    public void The_animations_read_back_with_every_sampler_and_path()
    {
        var file = GltfFile.Read(TestPeople.Motions());
        Assert.Equal(["walk", "turn_left_90", "bare"], file.Animations.Select(a => a.Name));
        var walk = file.Animations[0];
        Assert.Equal([GltfInterpolation.Linear, GltfInterpolation.Step, GltfInterpolation.CubicSpline, GltfInterpolation.Linear, GltfInterpolation.Linear, GltfInterpolation.Linear],
                     walk.Samplers.Select(s => s.Interpolation));
        Assert.Equal([GltfPath.Rotation, GltfPath.Translation, GltfPath.Rotation, GltfPath.Scale, GltfPath.Weights, GltfPath.Translation], walk.Channels.Select(c => c.Path));
        Assert.Equal([0f, 0.5f, 1f], file.ReadFloats(walk.Samplers[0].Input));
        Assert.Equal(6, file.ReadQuaternions(walk.Samplers[2].Output).Length);
        Assert.Equal(new float[] { 0, 0, 1, 0.5f, 0, 0 }, file.ReadFloats(walk.Samplers[4].Output));
        using var notes = JsonDocument.Parse(walk.Extras!);
        Assert.Equal(1.25, notes.RootElement.GetProperty("vitomir").GetProperty("ground_speed_m_s").GetDouble());
        Assert.Null(file.Animations[2].Extras);
    }

    [Fact]
    public void Accessors_of_every_component_type_normalised_or_not_read_as_glTF_says()
    {
        var g = new GlbWriter();
        int shorts = g.Shorts("SCALAR", true, -32768, -32767, 0, 16384, 32767);
        int raw = g.Shorts("SCALAR", false, -5, 7);
        int sbytes = g.Accessor(g.View([0x80, 0x81, 0, 0x7F]), 5120, "SCALAR", 4, normalized: true);
        int ushorts = g.UShorts("VEC2", true, 0, 65535);
        int uints = g.Accessor(g.View(BitConverter.GetBytes(123456789u)), 5125, "SCALAR", 1);
        // Interleaved: a VEC3 of floats and a VEC2 of unsigned shorts in one view of 16-byte vertices.
        var inter = new byte[32];
        for (int v = 0; v < 2; v++)
        {
            for (int c = 0; c < 3; c++) BitConverter.TryWriteBytes(inter.AsSpan(16 * v + 4 * c), v * 10f + c);
            BitConverter.TryWriteBytes(inter.AsSpan(16 * v + 12), (ushort)(v + 1));
            BitConverter.TryWriteBytes(inter.AsSpan(16 * v + 14), (ushort)(v + 2));
        }
        int view = g.View(inter, stride: 16);
        int positions = g.Accessor(view, 5126, "VEC3", 2);
        int pairs = g.Accessor(view, 5123, "VEC2", 2, byteOffset: 12);
        // A sparse accessor over a base: two of four elements replaced.
        var sparse = new System.Text.Json.Nodes.JsonObject
        {
            ["count"] = 2,
            ["indices"] = new System.Text.Json.Nodes.JsonObject { ["bufferView"] = g.View([3, 0, 1, 0]), ["componentType"] = 5123 },
            ["values"] = new System.Text.Json.Nodes.JsonObject { ["bufferView"] = g.View(new float[] { 30, 10 }.SelectMany(BitConverter.GetBytes).ToArray()) },
        };
        int based = g.Accessor(g.View(new float[] { 1, 2, 3, 4 }.SelectMany(BitConverter.GetBytes).ToArray()), 5126, "SCALAR", 4, sparse: sparse);
        g.Root["nodes"] = new System.Text.Json.Nodes.JsonArray();
        var file = GltfFile.Read(g.Build());
        Assert.Equal([-1f, -1f, 0f, 16384 / 32767f, 1f], file.ReadFloats(shorts));
        Assert.Equal([-5f, 7f], file.ReadFloats(raw));
        Assert.Equal([-1f, -1f, 0f, 1f], file.ReadFloats(sbytes));
        Assert.Equal([0f, 1f], file.ReadFloats(ushorts));
        Assert.Equal([123456789u], file.ReadUInts(uints));
        Assert.Equal([new Vector3(0, 1, 2), new Vector3(10, 11, 12)], file.ReadVector3(positions));
        Assert.Equal([1u, 2, 2, 3], file.ReadUInts(pairs));
        Assert.Equal([1f, 10f, 3f, 30f], file.ReadFloats(based));
        Assert.Equal([1u, 10, 3, 30], file.ReadUInts(based));
    }

    [Fact]
    public void A_person_made_here_is_converted_merged_and_hidden_as_the_task_says()
    {
        var lib = PeopleLibrary.TryOpen(folder, out string why);
        Assert.True(lib is not null, why);
        Assert.Null(lib.Commit);
        var person = lib.Person(new PersonRecipe("tester", "male", "test_base", ["test_outfit", "hair_test"]));
        var model = person.Model;
        var s = model.Skeleton;

        // One skeleton: the base's four joints, then the outfit's swing bone with its parent by name; the hair's left out with it.
        Assert.Equal(["root", "pelvis", "hand_r", "thigh_l", "swing_test_0"], s.Bones.Select(b => b.Name));
        Assert.Equal([-1, 0, 1, 1, 3], s.Bones.Select(b => b.Parent));
        Assert.DoesNotContain(model.Parts, p => p.Material!.Name == "hair");
        Assert.Contains(lib.Log, l => l.Contains("left out under what covers the hair: hair_test"));

        // Rests: quaternions (−qx, qz, qy, qw), translations (−x, z, y).
        var q = TestPeople.PelvisTurn;
        Assert.Equal(new Quaternion(-q.X, q.Z, q.Y, q.W), s.Bones[1].Rest.Rotation);
        Assert.Equal(new Vector3(-0f, 0.02f, 0.9f), s.Bones[1].Rest.Translation);
        Assert.Equal(new Vector3(0.3f, 0.1f, 0.4f), s.Bones[2].Rest.Translation);
        Assert.Equal(new Quaternion(-0f, 0f, TestPeople.HalfRoot2, TestPeople.HalfRoot2), s.Bones[2].Rest.Rotation);
        Assert.Equal(new Vector3(-0f, 0.1f, -0.2f), s.Bones[4].Rest.Translation);

        // Inverse binds converted with them: at rest every skin matrix is the identity, and model space is glTF's turned.
        var model0 = PeopleLibraryTests.RestModel(s);
        var world = TestPeople.World();
        for (int b = 0; b < s.Count; b++)
        {
            Near(PeopleSpace.Point(world[b].Translation), model0[b].Translation);
            var skin = s.InverseBind[b] * model0[b];
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    Assert.True(MathF.Abs(skin[r, c] - (r == c ? 1f : 0f)) < 1e-5f, $"bone {s.Bones[b].Name}: skin [{r},{c}] = {skin[r, c]}");
        }
        Assert.Equal(PeopleSpace.Matrix(PeopleSpace.Matrix(world[2])), world[2]);   // the change is its own inverse

        // The body: positions and normals turned, tangents' w kept, UVs as stored, colours, bones by the merged index.
        var v = model.Vertices;
        Near(new Vector3(0.1f, 0.05f, 1.0f), v[0].Position);
        Near(new Vector3(0f, 0.05f, 1.2f), v[2].Position);
        Near(new Vector3(0, 1, 0), v[0].Normal);
        Assert.Equal(new Vector4(-1, 0, 0, -1), model.Tangents![2]);
        Assert.Equal(new Vector2(0.5f, 1), v[2].Uv);
        Assert.Equal(new Vector4(1f, 128 / 255f, 0f, 1f), model.Colors![0]);
        Assert.Equal((2, 1), (v[1].Bone(0), v[1].Bone(1)));
        Assert.Equal(new Vector4(128 / 255f, 127 / 255f, 0, 0), v[1].Weights);
        // The tunic's vertex on the swing bone (its own joint 4) and thigh_l (its 3): the merged 4 and 3.
        var tunic = model.Parts.Single(p => p.Material!.Name == "wool");
        var tv = v[model.Indices[tunic.FirstIndex]];
        Assert.Equal((4, 3), (tv.Bone(0), tv.Bone(1)));
        Assert.Equal(new Vector4(0.82f, 0.82f, 0.82f, 1f), model.Colors[model.Indices[tunic.FirstIndex]]);
        Assert.True(tunic.Material!.VertexColor);

        // The body less its triangle 1, which the outfit hides (listed twice: counted once); still facing out (+Y here).
        Assert.Equal(1, person.HiddenTriangles);
        Assert.Equal(3, model.Parts[0].IndexCount);
        Assert.Equal([0u, 1, 2], model.Indices[..3]);
        var face = Vector3.Cross(v[1].Position - v[0].Position, v[2].Position - v[0].Position);
        Assert.True(Vector3.Dot(face, v[0].Normal) > 0f, "the change of axes keeps the winding");

        // Parts and materials: the cornea hidden, the lashes a cut-out at 0.5, the skin's cut-off, factors and maps by name.
        Assert.Equal(["skin", "cornea", "eyelashes", "wool"], model.Parts.Select(p => p.Material!.Name));
        var skinPart = model.Parts[0];
        var pm = skinPart.Material!;
        Assert.Equal(("people:test_base/skin_basecolor", "people:test_base/skin_normal", "people:test_base/skin_orm"), (pm.BaseColor, pm.Normal, pm.Orm));
        Assert.Equal("people:test_base/skin_basecolor", skinPart.Texture);
        Assert.Equal((0.4f, true, true), (pm.AlphaCutoff, pm.DoubleSided, skinPart.AlphaTest));
        Assert.Equal((new Vector4(1, 0.9f, 0.8f, 1), 0.7f, 0f), (pm.BaseColorFactor, pm.Roughness, pm.Metallic));
        Assert.False(pm.VertexColor && false);
        Assert.True(model.Parts[1].Material!.Hidden);
        Assert.Equal(0.5f, model.Parts[2].Material!.AlphaCutoff);
        Assert.False(model.Parts[2].Material!.Hidden);
        // The outfit's picture is the same bytes as the skin's: one texture, the first file's name.
        Assert.Equal("people:test_base/skin_basecolor", tunic.Material.BaseColor);
        Assert.Equal(["people:test_base/skin_basecolor", "people:test_base/skin_normal", "people:test_base/skin_orm"], person.Textures.Select(t => t.Name));
        Assert.Equal(TestPeople.Pixels, person.Textures[0].Rgba);
        Assert.Equal((2, 1), (person.Textures[1].Width, person.Textures[1].Height));

        // The socket under the hand, its offset turned; the bounds round the rest pose.
        var socket = person.Rig.Sockets["socket_hand_r"];
        Assert.Equal(2, socket.Bone);
        Near(new Vector3(0, 0, 0.05f), socket.Offset.Translation);
        Assert.Equal(Quaternion.Identity, socket.Offset.Rotation);
        Assert.Equal(model.Vertices.Aggregate(new Vector3(float.MaxValue), (a, x) => Vector3.Min(a, x.Position)), model.BoundsMin);
        Assert.False(person.Rig.Complete);   // four bones are not a person
        Assert.Same(person, lib.Person(new PersonRecipe("another name", "male", "test_base", ["test_outfit", "hair_test"])));
        Assert.Throws<FileNotFoundException>(() => lib.Person(new PersonRecipe("x", "male", "test_base", ["no_such_outfit"])));
    }

    [Fact]
    public void A_clip_made_here_samples_by_bone_name_in_the_platforms_space()
    {
        var lib = PeopleLibrary.TryOpen(folder, out _)!;
        var person = lib.Person(new PersonRecipe("tester", "male", "test_base", ["test_outfit"]));
        var s = person.Model.Skeleton;
        var clips = lib.Motions("male");
        Assert.Equal(["walk", "turn_left_90", "bare"], clips.Select(c => c.Name));
        var walk = (GltfClip)clips[0];
        Assert.Equal((1f, true), (walk.Duration, walk.Loops));
        Assert.Equal(("walk", "plain", "none", 1.25f, 0f, 1, 1.375f, 109.1f, true), (walk.Notes.Kind, walk.Notes.Style, walk.Notes.Carry, walk.Notes.GroundSpeed,
                     walk.Notes.HeadingDeg, walk.Notes.Strides, walk.Notes.StrideM, walk.Notes.StepsPerMinute, walk.Notes.Quickens));
        Assert.Equal([new ClipFootfall("Lwalk", 0f), new ClipFootfall("Rwalk", 0.5f)], walk.Notes.Footfalls);
        Assert.Contains("\"made_by\"", walk.Notes.Json);
        Assert.Equal(["root", "hand_r"], walk.RotatedBones);
        Assert.Equal(["pelvis", "prop"], walk.MovedBones);

        var local = new BoneTransform[s.Count];
        // LINEAR, the short way: a quarter of the way to a half turn about glTF's up is 45° about +Z here.
        walk.Sample(0.25f, s, local);
        float h = MathF.PI / 8f;
        Near(new Quaternion(0, 0, MathF.Sin(h), MathF.Cos(h)), local[0].Rotation);
        // STEP holds its key: the pelvis at its first, then at its second.
        Assert.Equal(new Vector3(-0f, 0.02f, 0.9f), local[1].Translation);
        walk.Sample(0.5f, s, local);
        Assert.Equal(new Vector3(-0f, 0.02f, 0.95f), local[1].Translation);
        // CUBICSPLINE with no tangents: half way is the normalised mean of its keys (both converted).
        var a = PeopleSpace.Rotation(TestPeople.HandTurn);
        var b = PeopleSpace.Rotation(TestPeople.Quarter);
        var mean = Quaternion.Normalize(new Quaternion((a.X + b.X) / 2, (a.Y + b.Y) / 2, (a.Z + b.Z) / 2, (a.W + b.W) / 2));
        Near(mean, local[2].Rotation);
        // No track: the skeleton's own rest (thigh_l's length, the swing bone); the scale track ignored; the prop on no bone.
        Assert.Equal(s.Bones[3].Rest, local[3]);
        Assert.Equal(s.Bones[4].Rest, local[4]);
        Assert.Equal(s.Bones[1].Rest.Rotation, local[1].Rotation);
        // A loop wraps by whole durations; its last key is its first.
        var again = new BoneTransform[s.Count];
        walk.Sample(2.25f, s, again);
        walk.Sample(0.25f, s, local);
        Assert.Equal(local, again);
        walk.Sample(-0.75f, s, again);
        Assert.Equal(local, again);
        walk.Sample(1f, s, again);
        walk.Sample(0f, s, local);
        Assert.Equal(local, again);
        // The face's weights are kept, not applied.
        Assert.Equal(["jawOpen", "eyeBlinkLeft"], walk.FaceTargets);
        Span<float> weights = stackalloc float[3];
        walk.SampleFace(0.25f, weights);
        Assert.Equal([0.5f, 0.25f, 0f], weights.ToArray());

        // A clip that does not loop holds its ends; its duration is the notes', past its last key.
        var turn = clips[1];
        Assert.Equal((2f, false, 90f, true, "stand", "socket_hand_r"), (turn.Duration, turn.Loops, turn.Notes.TurnDeg, turn.Notes.RootTurns, turn.Notes.After, turn.Notes.Held));
        turn.Sample(1.5f, s, local);
        Near(new Quaternion(0, 0, TestPeople.HalfRoot2, TestPeople.HalfRoot2), local[0].Rotation);
        turn.Sample(-1f, s, local);
        Assert.Equal(Quaternion.Identity, local[0].Rotation);
        turn.Sample(99f, s, again);
        turn.Sample(turn.Duration, s, local);
        Assert.Equal(local, again);
        // Without notes: the last key's time, not looping.
        Assert.Equal((0.8f, false), (clips[2].Duration, clips[2].Loops));
        Assert.True(float.IsNaN(clips[2].Notes.HeadingDeg));

        // The same clip on another skeleton (the base alone, without the swing bone): mapped again by name.
        var bare = lib.Person(new PersonRecipe("bare", "male", "test_base", [])).Model.Skeleton;
        var four = new BoneTransform[bare.Count];
        walk.Sample(0.25f, bare, four);
        walk.Sample(0.25f, s, local);
        Assert.Equal(local[..4], four);
    }

    [Fact]
    public void Sampling_is_the_same_on_every_call_and_never_uses_the_C_runtimes_trigonometry()
    {
        var lib = PeopleLibrary.TryOpen(folder, out _)!;
        var s = lib.Person(new PersonRecipe("tester", "male", "test_base", ["test_outfit"])).Model.Skeleton;
        var walk = lib.Motions("male")[0];
        var a = new BoneTransform[s.Count];
        var b = new BoneTransform[s.Count];
        for (float t = -2f; t < 3f; t += 0.0173f)
        {
            walk.Sample(t, s, a);
            walk.Sample(t, s, b);
            Assert.Equal(a, b);
        }
        // DetMath's slerp, step for step.
        walk.Sample(0.3f, s, a);
        var key = PeopleSpace.Rotation(TestPeople.HandTurn);
        Assert.Equal(DetMath.Slerp(Quaternion.Identity, key, 0.3f / 0.5f), a[0].Rotation);
    }

    [Fact]
    public void The_commit_comes_from_the_repositorys_git_folder_or_a_packages_source_json()
    {
        Assert.Null(PeopleLibrary.TryOpen(folder, out _)!.Commit);
        const string hash = "0123456789abcdef0123456789abcdef01234567", other = "89abcdef0123456789abcdef0123456789abcdef";
        File.WriteAllText(Path.Combine(folder, "source.json"), $$"""{ "commit": "{{other}}" }""");
        Assert.Equal(other, PeopleLibrary.TryOpen(folder, out _)!.Commit);
        string git = Path.Combine(folder, ".git");
        Directory.CreateDirectory(git);
        File.WriteAllText(Path.Combine(git, "HEAD"), "ref: refs/heads/main\n");
        File.WriteAllText(Path.Combine(git, "packed-refs"), $"# pack-refs with: peeled fully-peeled sorted\n{other} refs/heads/other\n{hash} refs/heads/main\n");
        Assert.Equal(hash, PeopleLibrary.TryOpen(folder, out _)!.Commit);
        Directory.CreateDirectory(Path.Combine(git, "refs", "heads"));
        File.WriteAllText(Path.Combine(git, "refs", "heads", "main"), other + "\n");
        Assert.Equal(other, PeopleLibrary.TryOpen(folder, out _)!.Commit);
        File.WriteAllText(Path.Combine(git, "HEAD"), hash + "\n");   // detached
        Assert.Equal(hash, PeopleLibrary.TryOpen(folder, out _)!.Commit);
    }

    [Fact]
    public void The_png_decoder_reads_every_colour_type_depth_filter_and_interlacing()
    {
        var random = new Random(14);
        foreach (bool interlace in new[] { false, true })
            foreach (var (colour, depth) in new[] { (0, 1), (0, 2), (0, 4), (0, 8), (0, 16), (2, 8), (2, 16), (3, 1), (3, 2), (3, 4), (3, 8), (4, 8), (4, 16), (6, 8), (6, 16) })
                foreach (var (w, h) in new[] { (1, 1), (5, 3), (13, 11) })
                {
                    int channels = colour switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
                    int top = colour == 3 ? Math.Min(255, (1 << depth) - 1) : (1 << depth) - 1;
                    var samples = Enumerable.Range(0, w * h * channels).Select(_ => random.Next(top + 1)).ToArray();
                    byte[]? palette = colour == 3 ? Enumerable.Range(0, (top + 1) * 3).Select(i => (byte)(i * 7)).ToArray() : null;
                    byte[]? trns = colour == 3 ? [10, 20] : colour == 2 && depth == 8 ? [0, (byte)samples[0], 0, (byte)samples[1], 0, (byte)samples[2]] : null;
                    var png = PngWriter.Write(w, h, colour, depth, samples, interlace, y => (y * 3 + w) % 5, palette, trns);
                    var rgba = PngDecoder.Decode(png.AsMemory(), out int dw, out int dh);
                    Assert.True(rgba is not null, $"colour {colour}, depth {depth}, {w}×{h}, interlaced {interlace}");
                    Assert.Equal((w, h), (dw, dh));
                    Assert.True(PngDecoder.TryReadHeader(png, out int hw, out int hh, out int hd, out int hc) && (hw, hh, hd, hc) == (w, h, depth, colour));
                    for (int p = 0; p < w * h; p++)
                    {
                        byte S(int c) => depth == 16 ? (byte)(samples[p * channels + c] >> 8) : depth == 8 ? (byte)samples[p * channels + c] : (byte)(samples[p * channels + c] * 255 / ((1 << depth) - 1));
                        byte[] expected = colour switch
                        {
                            0 => [S(0), S(0), S(0), 255],
                            2 => [S(0), S(1), S(2), (byte)(trns is not null && samples[p * 3] == trns[1] && samples[p * 3 + 1] == trns[3] && samples[p * 3 + 2] == trns[5] ? 0 : 255)],
                            3 => [palette![samples[p] * 3], palette[samples[p] * 3 + 1], palette[samples[p] * 3 + 2], samples[p] < trns!.Length ? trns[samples[p]] : (byte)255],
                            4 => [S(0), S(0), S(0), S(1)],
                            _ => [S(0), S(1), S(2), S(3)],
                        };
                        Assert.True(expected.AsSpan().SequenceEqual(rgba.AsSpan(4 * p, 4)),
                                    $"colour {colour}, depth {depth}, {w}×{h}, interlaced {interlace}, pixel {p}: expected {string.Join(",", expected)}, got {string.Join(",", rgba.AsSpan(4 * p, 4).ToArray())}");
                    }
                }
        Assert.Null(PngDecoder.Decode([1, 2, 3], out _, out _));
        var broken = PngWriter.Rgba(2, 2, TestPeople.Pixels);
        Assert.Null(PngDecoder.Decode(broken.AsMemory(0, broken.Length - 30), out _, out _));
        Assert.Equal(PngDecoder.ContentKey(TestPeople.SkinPng), PngDecoder.ContentKey(TestPeople.SkinPng.ToArray()));
        Assert.NotEqual(PngDecoder.ContentKey(TestPeople.SkinPng), PngDecoder.ContentKey(TestPeople.NormalPng));
    }

    [Fact]
    public void The_npz_reader_reads_numpys_arrays()
    {
        string path = Path.Combine(folder, "arrays.npz");
        var floats = new float[] { 1.5f, -2f }.SelectMany(BitConverter.GetBytes).ToArray();
        var longs = new long[] { 1, -2, 3, long.MaxValue }.SelectMany(BitConverter.GetBytes).ToArray();
        var text = "ab\0\0\0cdefg".SelectMany(c => BitConverter.GetBytes((int)c)).ToArray();
        NpyWriter.Npz(path,
                      ("hidden", NpyWriter.Npy("<i4", [3], NpyWriter.Int32s(7, 8, 9)), true),
                      ("pairs", NpyWriter.Npy("<i8", [2, 2], longs, version: 2), false),
                      ("floats", NpyWriter.Npy("<f4", [2], floats), true),
                      ("flags", NpyWriter.Npy("|b1", [3], [1, 0, 1]), false),
                      ("names", NpyWriter.Npy("<U5", [2], text), true),
                      ("big", NpyWriter.Npy(">i2", [2], [0x01, 0x02, 0xFF, 0xFE]), true),
                      ("scalar", NpyWriter.Npy("<f8", [], BitConverter.GetBytes(0.25)), true));
        using var npz = NpzArchive.Open(path);
        Assert.Equal(["hidden", "pairs", "floats", "flags", "names", "big", "scalar"], npz.Names);
        Assert.True(npz.Contains("hidden.npy") && npz.Contains("hidden") && !npz.Contains("absent"));
        var hidden = npz.Read("hidden");
        Assert.Equal(("<i4", false, 'i'), (hidden.Descr, hidden.FortranOrder, hidden.Kind));
        Assert.Equal([3], hidden.Shape);
        Assert.Equal([7, 8, 9], hidden.ToInt32());
        var pairs = npz.Read("pairs");
        Assert.Equal([2, 2], pairs.Shape);
        Assert.Equal([1L, -2, 3, long.MaxValue], pairs.ToInt64());
        Assert.Throws<OverflowException>(() => pairs.ToInt32());
        Assert.Equal([1.5f, -2f], npz.Read("floats").ToSingle());
        Assert.Equal([1, 0, 1], npz.Read("flags").ToInt32());
        Assert.Equal(["ab", "cdefg"], npz.Read("names").ToStrings());
        Assert.Equal([0x0102, -2], npz.Read("big").ToInt32());
        var scalar = npz.Read("scalar");
        Assert.Empty(scalar.Shape);
        Assert.Equal(1, scalar.Count);
        Assert.Equal([0.25], scalar.ToDouble());
        Assert.Throws<KeyNotFoundException>(() => npz.Read("absent"));
        Assert.Throws<InvalidDataException>(() => NpyArray.Read([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]));
    }
}
