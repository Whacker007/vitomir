// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text.Json.Nodes;

namespace Vitomir.Platform.Tests.People;

/// <summary>
/// A small models-stream folder made in the test, laid out as the real one: a base body (four bones, a body of two
/// triangles with every attribute, a sparse morph target, a textured material; eyes with a clear cornea and blended
/// lashes; a socket under the hand), an outfit that adds a swing bone, hides one body triangle and covers the hair, hair
/// it leaves out, and a motions file (LINEAR, STEP and CUBICSPLINE samplers, a scale track, a face weights track, notes).
/// Everything in glTF's axes; the tests check what the platform makes of it.
/// </summary>
internal static class TestPeople
{
    public static readonly float HalfRoot2 = MathF.Sqrt(0.5f);
    public static readonly Quaternion PelvisTurn = new(MathF.Sin(10f * MathF.PI / 180f), 0, 0, MathF.Cos(10f * MathF.PI / 180f));
    public static readonly Quaternion HandTurn = new(0, HalfRoot2, 0, HalfRoot2);   // 90° about glTF's up
    public static readonly Quaternion Quarter = new(HalfRoot2, 0, 0, HalfRoot2);    // 90° about X
    public static readonly Vector3 PelvisAt = new(0, 0.9f, 0.02f), HandAt = new(-0.3f, 0.4f, 0.1f), ThighAt = new(0.1f, -0.05f, 0), SwingAt = new(0, -0.2f, 0.1f);

    public static readonly byte[] Pixels = [255, 0, 0, 255, 0, 255, 0, 128, 0, 0, 255, 0, 10, 20, 30, 40];
    public static readonly byte[] SkinPng = PngWriter.Rgba(2, 2, Pixels);
    public static readonly byte[] NormalPng = PngWriter.Write(2, 1, 2, 8, [128, 128, 255, 120, 130, 250], false, _ => 4);
    public static readonly byte[] OrmPng = PngWriter.Write(1, 1, 2, 8, [255, 200, 0], false, _ => 0);

    static Matrix4x4 Trs(Quaternion r, Vector3 t) => Matrix4x4.CreateFromQuaternion(r) * Matrix4x4.CreateTranslation(t);

    /// <summary>The bones' rest in the file's world (row vectors): root, pelvis, hand_r, thigh_l, swing_test_0.</summary>
    public static Matrix4x4[] World()
    {
        var pelvis = Trs(PelvisTurn, PelvisAt);
        var hand = Trs(HandTurn, HandAt) * pelvis;
        var thigh = Trs(Quaternion.Identity, ThighAt) * pelvis;
        var swing = Trs(Quaternion.Identity, SwingAt) * thigh;
        return [Matrix4x4.Identity, pelvis, hand, thigh, swing];
    }

    static Matrix4x4 Inverse(Matrix4x4 m) { Matrix4x4.Invert(m, out var i); return i; }

    static JsonArray A(params float[] v) => new(v.Select(x => (JsonNode)x).ToArray());
    static JsonArray I(params int[] v) => new(v.Select(x => (JsonNode)x).ToArray());
    static JsonArray Q(Quaternion q) => A(q.X, q.Y, q.Z, q.W);
    static JsonArray V(Vector3 v) => A(v.X, v.Y, v.Z);

    /// <summary>The skeleton's nodes from index <paramref name="first"/>: root, pelvis, hand_r, thigh_l (and swing_test_0 when asked), and the armature node above them.</summary>
    static List<JsonObject> Skeleton(int first, bool swing, bool socket)
    {
        var nodes = new List<JsonObject>
        {
            new() { ["name"] = "root", ["children"] = I(first + 1) },
            new() { ["name"] = "pelvis", ["translation"] = V(PelvisAt), ["rotation"] = Q(PelvisTurn), ["children"] = I(first + 2, first + 3), ["extras"] = new JsonObject { ["vitomir"] = new JsonObject { ["note"] = 1 } } },
            new() { ["name"] = "hand_r", ["translation"] = V(HandAt), ["rotation"] = Q(HandTurn) },
            new() { ["name"] = "thigh_l", ["translation"] = V(ThighAt) },
        };
        if (swing)
        {
            nodes[3]["children"] = I(first + nodes.Count);
            nodes.Add(new JsonObject { ["name"] = "swing_test_0", ["translation"] = V(SwingAt) });
        }
        if (socket)
        {
            nodes[2]["children"] = I(first + nodes.Count);
            // A node given by its matrix (column-major): a translation of 5 cm along the hand's own +Y.
            nodes.Add(new JsonObject { ["name"] = "socket_hand_r", ["matrix"] = A(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0.05f, 0, 1) });
        }
        nodes.Add(new JsonObject { ["name"] = "skeleton_human", ["children"] = I(first) });
        return nodes;
    }

    /// <summary>The base body's file.</summary>
    public static byte[] Base()
    {
        var g = new GlbWriter();
        var world = World();
        var nodes = Skeleton(0, swing: false, socket: true);   // 0 root, 1 pelvis, 2 hand_r, 3 thigh_l, 4 socket, 5 skeleton_human
        int armature = nodes.Count - 1;
        nodes.Add(new JsonObject { ["name"] = "body", ["mesh"] = 0, ["skin"] = 0 });
        nodes.Add(new JsonObject { ["name"] = "eyes", ["mesh"] = 1, ["skin"] = 0 });
        int ibm = g.Matrices(Inverse(world[0]), Inverse(world[1]), Inverse(world[2]), Inverse(world[3]));
        // UVs in a strided view: two floats and four bytes of padding a vertex.
        float[] uv = [0, 0, 1, 0, 0.5f, 1, 0.5f, 0];
        var uvBytes = new byte[4 * 12];
        for (int i = 0; i < 4; i++)
        {
            BitConverter.TryWriteBytes(uvBytes.AsSpan(12 * i), uv[2 * i]);
            BitConverter.TryWriteBytes(uvBytes.AsSpan(12 * i + 4), uv[2 * i + 1]);
            uvBytes[12 * i + 8] = 0xEE;
        }
        int uvAcc = g.Accessor(g.View(uvBytes, stride: 12), 5126, "VEC2", 4);
        // A sparse morph target: no buffer view, one vertex moved.
        var sparse = new JsonObject
        {
            ["count"] = 1,
            ["indices"] = new JsonObject { ["bufferView"] = g.View([2, 0, 0, 0]), ["componentType"] = 5121 },
            ["values"] = new JsonObject { ["bufferView"] = g.View(new float[] { 0, 0.01f, 0 }.SelectMany(BitConverter.GetBytes).ToArray()) },
        };
        var body = new JsonObject
        {
            ["attributes"] = new JsonObject
            {
                ["POSITION"] = g.Floats("VEC3", -0.1f, 1.0f, 0.05f, 0.1f, 1.0f, 0.05f, 0, 1.2f, 0.05f, 0, 0.8f, 0.05f),
                ["NORMAL"] = g.Floats("VEC3", 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1),
                ["TANGENT"] = g.Floats("VEC4", 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, -1, 1, 0, 0, 1),
                ["TEXCOORD_0"] = uvAcc,
                ["COLOR_0"] = g.Bytes("VEC4", true, 255, 128, 0, 255, 0, 0, 0, 255, 255, 255, 255, 255, 51, 102, 153, 204),
                ["JOINTS_0"] = g.Bytes("VEC4", false, 1, 0, 0, 0, 2, 1, 0, 0, 1, 0, 0, 0, 3, 0, 0, 0),
                ["WEIGHTS_0"] = g.Bytes("VEC4", true, 255, 0, 0, 0, 128, 127, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0),
            },
            ["indices"] = g.UShorts("SCALAR", false, 0, 1, 2, 1, 0, 3),
            ["material"] = 0,
            ["targets"] = new JsonArray(new JsonObject { ["POSITION"] = g.Accessor(-1, 5126, "VEC3", 4, sparse: sparse) }),
        };
        JsonObject Eye(int material, int indices) => new()
        {
            ["attributes"] = new JsonObject
            {
                ["POSITION"] = g.Floats("VEC3", -0.02f, 1.5f, 0.1f, 0.02f, 1.5f, 0.1f, 0, 1.52f, 0.1f),
                ["NORMAL"] = g.Floats("VEC3", 0, 0, 1, 0, 0, 1, 0, 0, 1),
                ["JOINTS_0"] = g.UShorts("VEC4", false, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0),
                ["WEIGHTS_0"] = g.Floats("VEC4", 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0),
            },
            ["indices"] = indices,
            ["material"] = material,
        };
        var eyes = new JsonArray(Eye(1, g.Bytes("SCALAR", false, 0, 1, 2)),
                                 Eye(2, g.Accessor(g.View(new uint[] { 0, 1, 2 }.SelectMany(BitConverter.GetBytes).ToArray()), 5125, "SCALAR", 3)));
        g.Root["scene"] = 0;
        g.Root["scenes"] = new JsonArray(new JsonObject { ["nodes"] = I(armature, armature + 1, armature + 2) });
        g.Root["nodes"] = new JsonArray(nodes.ToArray<JsonNode>());
        g.Root["skins"] = new JsonArray(new JsonObject { ["name"] = "skeleton_human", ["joints"] = I(0, 1, 2, 3), ["inverseBindMatrices"] = ibm });
        g.Root["meshes"] = new JsonArray(
            new JsonObject { ["name"] = "body", ["primitives"] = new JsonArray(body), ["weights"] = A(0), ["extras"] = new JsonObject { ["targetNames"] = new JsonArray("jawOpen") } },
            new JsonObject { ["name"] = "eyes", ["primitives"] = eyes });
        g.Root["materials"] = new JsonArray(
            new JsonObject
            {
                ["name"] = "skin",
                ["pbrMetallicRoughness"] = new JsonObject
                {
                    ["baseColorFactor"] = A(1, 0.9f, 0.8f, 1), ["baseColorTexture"] = new JsonObject { ["index"] = 0 },
                    ["metallicFactor"] = 0, ["roughnessFactor"] = 0.7f, ["metallicRoughnessTexture"] = new JsonObject { ["index"] = 2 },
                },
                ["normalTexture"] = new JsonObject { ["index"] = 1, ["scale"] = 0.5f },
                ["occlusionTexture"] = new JsonObject { ["index"] = 2, ["strength"] = 0.8f },
                ["alphaMode"] = "MASK", ["alphaCutoff"] = 0.4f, ["doubleSided"] = true,
                ["extensions"] = new JsonObject { ["KHR_materials_ior"] = new JsonObject { ["ior"] = 1.4f } },
                ["extras"] = new JsonObject { ["note"] = "skin" },
            },
            new JsonObject
            {
                ["name"] = "cornea", ["doubleSided"] = true,
                ["extensions"] = new JsonObject { ["KHR_materials_transmission"] = new JsonObject { ["transmissionFactor"] = 1 } },
                ["pbrMetallicRoughness"] = new JsonObject { ["metallicFactor"] = 0, ["roughnessFactor"] = 0 },
            },
            new JsonObject { ["name"] = "eyelashes", ["alphaMode"] = "BLEND", ["doubleSided"] = true, ["pbrMetallicRoughness"] = new JsonObject { ["baseColorTexture"] = new JsonObject { ["index"] = 0 } } });
        g.Root["samplers"] = new JsonArray(new JsonObject { ["magFilter"] = 9729, ["minFilter"] = 9987, ["wrapS"] = 33071 });
        g.Root["textures"] = new JsonArray(new JsonObject { ["sampler"] = 0, ["source"] = 0 }, new JsonObject { ["sampler"] = 0, ["source"] = 1 }, new JsonObject { ["source"] = 2 });
        g.Root["images"] = new JsonArray(
            new JsonObject { ["name"] = "skin_basecolor", ["mimeType"] = "image/png", ["bufferView"] = g.View(SkinPng) },
            new JsonObject { ["name"] = "skin_normal", ["mimeType"] = "image/png", ["bufferView"] = g.View(NormalPng) },
            new JsonObject { ["name"] = "skin_orm", ["mimeType"] = "image/png", ["bufferView"] = g.View(OrmPng) });
        g.Root["extensionsUsed"] = new JsonArray("KHR_materials_ior", "KHR_materials_transmission");
        g.Root["extras"] = new JsonObject { ["made"] = "test" };
        return g.Build();
    }

    /// <summary>A worn file: the skeleton (with a swing bone under <c>thigh_l</c> when <paramref name="swing"/>) and one mesh named <paramref name="mesh"/> skinned to it.</summary>
    public static byte[] Worn(string mesh, string swingName, bool swing)
    {
        var g = new GlbWriter();
        var world = World();
        var nodes = Skeleton(0, swing, socket: false);   // 0 root, 1 pelvis, 2 hand_r, 3 thigh_l, (4 swing), armature
        if (swing) nodes[4]["name"] = swingName;
        int armature = nodes.Count - 1;
        nodes.Add(new JsonObject { ["name"] = mesh, ["mesh"] = 0, ["skin"] = 0 });
        int[] joints = swing ? [0, 1, 2, 3, 4] : [0, 1, 2, 3];
        int ibm = g.Matrices(joints.Select(j => Inverse(world[j])).ToArray());
        var prim = new JsonObject
        {
            ["attributes"] = new JsonObject
            {
                ["POSITION"] = g.Floats("VEC3", 0.1f, 0.7f, 0.1f, 0.15f, 0.7f, 0.1f, 0.12f, 0.75f, 0.1f),
                ["NORMAL"] = g.Floats("VEC3", 0, 0, 1, 0, 0, 1, 0, 0, 1),
                ["TEXCOORD_0"] = g.Floats("VEC2", 0, 0, 1, 0, 0, 1),
                ["COLOR_0"] = g.Floats("VEC3", 0.82f, 0.82f, 0.82f, 0.5f, 0.5f, 0.5f, 1, 1, 1),
                ["JOINTS_0"] = g.Bytes("VEC4", false, (byte)(swing ? 4 : 3), 3, 0, 0, 3, 0, 0, 0, 3, 0, 0, 0),
                ["WEIGHTS_0"] = g.Floats("VEC4", 0.5f, 0.5f, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0),
            },
            ["material"] = 0,
        };
        g.Root["scene"] = 0;
        g.Root["scenes"] = new JsonArray(new JsonObject { ["nodes"] = I(armature, armature + 1) });
        g.Root["nodes"] = new JsonArray(nodes.ToArray<JsonNode>());
        g.Root["skins"] = new JsonArray(new JsonObject { ["joints"] = I(joints), ["inverseBindMatrices"] = ibm });
        g.Root["meshes"] = new JsonArray(new JsonObject { ["name"] = mesh, ["primitives"] = new JsonArray(prim) });
        // The same picture as the base's skin: kept once, under the base's name.
        g.Root["materials"] = new JsonArray(new JsonObject
        {
            ["name"] = mesh == "hair" ? "hair" : "wool", ["alphaMode"] = mesh == "hair" ? "MASK" : "OPAQUE", ["alphaCutoff"] = 0.35f,
            ["pbrMetallicRoughness"] = new JsonObject { ["baseColorFactor"] = A(0.3f, 0.25f, 0.18f, 1), ["baseColorTexture"] = new JsonObject { ["index"] = 0 } },
        });
        g.Root["textures"] = new JsonArray(new JsonObject { ["source"] = 0 });
        g.Root["images"] = new JsonArray(new JsonObject { ["name"] = "wool_basecolor", ["mimeType"] = "image/png", ["bufferView"] = g.View(SkinPng) });
        return g.Build();
    }

    /// <summary>The motions file: the skeleton's nodes, a face mesh to carry the weights track, a prop node, two animations.</summary>
    public static byte[] Motions()
    {
        var g = new GlbWriter();
        var nodes = Skeleton(0, swing: false, socket: false);   // 0 root, 1 pelvis, 2 hand_r, 3 thigh_l, 4 armature
        nodes.Add(new JsonObject { ["name"] = "face", ["mesh"] = 0 });   // 5
        nodes.Add(new JsonObject { ["name"] = "prop" });                 // 6: not a bone of any body
        int t3 = g.Floats("SCALAR", 0, 0.5f, 1), t2 = g.Floats("SCALAR", 0, 1);
        Quaternion R(Quaternion q) => q;
        float[] Qs(params Quaternion[] q) => q.SelectMany(x => new[] { x.X, x.Y, x.Z, x.W }).ToArray();
        var zero = new Quaternion(0, 0, 0, 0);
        var walk = new JsonObject
        {
            ["name"] = "walk",
            ["samplers"] = new JsonArray(
                new JsonObject { ["input"] = t3, ["output"] = g.Floats("VEC4", Qs(Quaternion.Identity, R(HandTurn), Quaternion.Identity)), ["interpolation"] = "LINEAR" },
                new JsonObject { ["input"] = t3, ["output"] = g.Floats("VEC3", 0, 0.9f, 0.02f, 0, 0.95f, 0.02f, 0, 0.9f, 0.02f), ["interpolation"] = "STEP" },
                new JsonObject { ["input"] = t2, ["output"] = g.Floats("VEC4", Qs(zero, HandTurn, zero, zero, Quarter, zero)), ["interpolation"] = "CUBICSPLINE" },
                new JsonObject { ["input"] = t2, ["output"] = g.Floats("VEC3", 1, 1, 1, 2, 2, 2) },
                new JsonObject { ["input"] = t3, ["output"] = g.Floats("SCALAR", 0, 0, 1, 0.5f, 0, 0) },
                new JsonObject { ["input"] = t2, ["output"] = g.Floats("VEC3", 1, 2, 3, 4, 5, 6) }),
            ["channels"] = new JsonArray(
                new JsonObject { ["sampler"] = 0, ["target"] = new JsonObject { ["node"] = 0, ["path"] = "rotation" } },
                new JsonObject { ["sampler"] = 1, ["target"] = new JsonObject { ["node"] = 1, ["path"] = "translation" } },
                new JsonObject { ["sampler"] = 2, ["target"] = new JsonObject { ["node"] = 2, ["path"] = "rotation" } },
                new JsonObject { ["sampler"] = 3, ["target"] = new JsonObject { ["node"] = 1, ["path"] = "scale" } },
                new JsonObject { ["sampler"] = 4, ["target"] = new JsonObject { ["node"] = 5, ["path"] = "weights" } },
                new JsonObject { ["sampler"] = 5, ["target"] = new JsonObject { ["node"] = 6, ["path"] = "translation" } }),
            ["extras"] = new JsonObject
            {
                ["vitomir"] = new JsonObject
                {
                    ["duration_s"] = 1.0, ["loops"] = true, ["kind"] = "walk", ["style"] = "plain", ["carry"] = "none", ["ground_speed_m_s"] = 1.25,
                    ["heading_deg"] = 0.0, ["strides"] = 1, ["stride_m"] = 1.375, ["steps_per_minute"] = 109.1, ["quickens"] = true,
                    ["footfalls"] = new JsonArray(new JsonObject { ["foot"] = "Lwalk", ["at_s"] = 0.0 }, new JsonObject { ["foot"] = "Rwalk", ["at_s"] = 0.5 }),
                    ["made_by"] = "a test",
                },
            },
        };
        var turn = new JsonObject
        {
            ["name"] = "turn_left_90",
            ["samplers"] = new JsonArray(new JsonObject { ["input"] = t2, ["output"] = g.Floats("VEC4", Qs(Quaternion.Identity, HandTurn)) }),
            ["channels"] = new JsonArray(new JsonObject { ["sampler"] = 0, ["target"] = new JsonObject { ["node"] = 0, ["path"] = "rotation" } }),
            ["extras"] = new JsonObject { ["vitomir"] = new JsonObject { ["duration_s"] = 2.0, ["loops"] = false, ["kind"] = "turn", ["turn_deg"] = 90.0, ["root_turns"] = true, ["after"] = "stand", ["held"] = "socket_hand_r" } },
        };
        var bare = new JsonObject
        {
            ["name"] = "bare",
            ["samplers"] = new JsonArray(new JsonObject { ["input"] = g.Floats("SCALAR", 0, 0.8f), ["output"] = g.Floats("VEC4", Qs(Quaternion.Identity, Quarter)) }),
            ["channels"] = new JsonArray(new JsonObject { ["sampler"] = 0, ["target"] = new JsonObject { ["node"] = 2, ["path"] = "rotation" } }),
        };
        g.Root["scene"] = 0;
        g.Root["scenes"] = new JsonArray(new JsonObject { ["nodes"] = I(4, 5, 6) });
        g.Root["nodes"] = new JsonArray(nodes.ToArray<JsonNode>());
        g.Root["meshes"] = new JsonArray(new JsonObject
        {
            ["name"] = "face",
            ["primitives"] = new JsonArray(new JsonObject { ["attributes"] = new JsonObject { ["POSITION"] = g.Floats("VEC3", 0, 0, 0, 1, 0, 0, 0, 1, 0) } }),
            ["extras"] = new JsonObject { ["targetNames"] = new JsonArray("jawOpen", "eyeBlinkLeft") },
        });
        g.Root["animations"] = new JsonArray(walk, turn, bare);
        return g.Build();
    }

    /// <summary>Writes the folder at <paramref name="root"/>: <c>assets/characters/test_base</c>, <c>assets/outfits/test</c>, <c>assets/hair</c>, <c>assets/motions</c>.</summary>
    public static void Write(string root)
    {
        string Dir(params string[] p) { var d = Path.Combine([root, "assets", .. p]); Directory.CreateDirectory(d); return d; }
        File.WriteAllBytes(Path.Combine(Dir("characters", "test_base"), "test_base.glb"), Base());
        string outfit = Dir("outfits", "test");
        File.WriteAllBytes(Path.Combine(outfit, "test_outfit.glb"), Worn("tunic", "swing_test_0", swing: true));
        File.WriteAllText(Path.Combine(outfit, "test_outfit.json"), """{ "base": "test_base.glb", "covers_hair": true }""");
        NpyWriter.Npz(Path.Combine(outfit, "test_outfit_fit.npz"),
                      ("tunic.tri", NpyWriter.Npy("<i8", [1], new byte[8]), true),
                      ("hidden", NpyWriter.Npy("<i4", [2], NpyWriter.Int32s(1, 1)), true));
        string hair = Dir("hair");
        File.WriteAllBytes(Path.Combine(hair, "hair_test.glb"), Worn("hair", "swing_hair_0", swing: true));
        File.WriteAllText(Path.Combine(hair, "hair_test.json"), """{ "kind": "hair" }""");
        File.WriteAllBytes(Path.Combine(Dir("motions"), "motions_male.glb"), Motions());
    }
}
