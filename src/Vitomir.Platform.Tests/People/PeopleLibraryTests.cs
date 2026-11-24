// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Formats.Npy;
using Vitomir.Formats.Png;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.People;

namespace Vitomir.Platform.Tests.People;

/// <summary>
/// The models repository's people from its own files, found by <c>VITOMIR_MODELS_DIR</c> (every test skips
/// without it): the library opens the folder; the player and the woman are put together on one skeleton each, upright and
/// facing +Y, with what their clothes hide left out and every map decoded; the motions load with their notes, turn the
/// right way, loop seamlessly and sample the same every time.
/// </summary>
public sealed class PeopleLibraryTests(ITestOutputHelper output)
{
    /// <summary>
    /// The library the tests share, without images: the maps (about 300 MB a person) are decoded only by the tests that
    /// look at them, each with a library of its own that is let go when it ends (the test process shares this machine's
    /// memory with three other processes' and was killed holding them).
    /// </summary>
    internal static readonly Lazy<PeopleLibrary?> Library = new(() =>
        Environment.GetEnvironmentVariable(PeopleLibrary.FolderVariable) is { Length: > 0 } dir ? PeopleLibrary.TryOpen(dir, out _, decodeImages: false) : null);

    /// <summary>A library of its own that decodes images, for a test that looks at the maps.</summary>
    static PeopleLibrary WithImages() => PeopleLibrary.TryOpen(Lib().Folder, out _)!;

    internal static PeopleLibrary Lib()
    {
        Assert.SkipUnless(Library.Value is not null, "the models repository's files are not here (VITOMIR_MODELS_DIR)");
        return Library.Value!;
    }

    static string Asset(params string[] parts) => Path.Combine([Lib().Folder, "assets", .. parts]);

    /// <summary>
    /// The models repository's bend helpers, after the 78 in this order: two at
    /// each knee (children of the thigh) and two at each hip (children of the pelvis).
    /// </summary>
    internal static readonly (string Name, string Parent)[] BendHelpers =
    [
        ("knee_bend_front_l", "thigh_l"), ("knee_bend_back_l", "thigh_l"), ("knee_bend_front_r", "thigh_r"), ("knee_bend_back_r", "thigh_r"),
        ("hip_bend_front_l", "pelvis"), ("hip_bend_back_l", "pelvis"), ("hip_bend_front_r", "pelvis"), ("hip_bend_back_r", "pelvis"),
    ];

    /// <summary>Whether a bone is one of the bend helpers (by the models repository's names).</summary>
    internal static bool IsBendHelper(string bone) => bone.StartsWith("knee_bend_", StringComparison.Ordinal) || bone.StartsWith("hip_bend_", StringComparison.Ordinal);

    /// <summary>
    /// The base's bones of whichever round of the models repository is here, on: the 56 and the 22 twist bones
    /// (78), and the 8 bend helpers after them (86) — all eight or none, each named, placed and parented as above.
    /// </summary>
    internal static int BaseBones(Skeleton skeleton)
    {
        int found = BendHelpers.Count(h => skeleton.Find(h.Name) >= 0);
        Assert.True(found is 0 or 8, $"{found} of the 8 bend helpers");
        for (int i = 0; i < found; i++)
        {
            var (name, parent) = BendHelpers[i];
            Assert.Equal(name, skeleton.Bones[78 + i].Name);
            Assert.Equal(parent, skeleton.Bones[skeleton.Bones[78 + i].Parent].Name);
        }
        return 78 + found;
    }

    static int[] Hidden(string npz)
    {
        using var a = NpzArchive.Open(npz);
        return a.Read("hidden").ToInt32();
    }

    [Fact]
    public void The_library_opens_the_models_streams_folder_and_refuses_others()
    {
        var lib = Lib();
        Assert.True(Directory.Exists(Path.Combine(lib.Folder, "assets", "characters")));
        Assert.Null(PeopleLibrary.TryOpen(Path.Combine(lib.Folder, "assets"), out string why));
        Assert.Contains("assets/characters", why);
        Assert.Null(PeopleLibrary.TryOpen(Path.Combine(lib.Folder, "no such folder"), out why));
        Assert.Contains("does not exist", why);
        Assert.NotNull(PeopleLibrary.TryOpen(lib.Folder, out why));
        Assert.Equal("", why);
        output.WriteLine($"commit: {lib.Commit ?? "(none: no .git and no source.json)"}");
    }

    [Fact]
    public void The_player_is_the_base_man_dressed_on_one_skeleton()
    {
        var player = Lib().Person(PersonRecipe.Player);
        foreach (var line in Lib().Log) output.WriteLine(line);
        var model = player.Model;
        var skeleton = model.Skeleton;

        // The base's 78 joints first in their order (86, its bend helpers after them), then the townsman's 80 swing bones
        // (the hair and beard add none).
        int based = BaseBones(skeleton);
        output.WriteLine($"{based} bones of the base");
        Assert.Equal(based + 80, skeleton.Count);
        Assert.Equal("root", skeleton.Bones[0].Name);
        Assert.Equal("pelvis", skeleton.Bones[1].Name);
        Assert.Equal("thigh_twist_02_r", skeleton.Bones[77].Name);
        Assert.All(skeleton.Bones.Skip(based), b => Assert.StartsWith("swing_", b.Name));
        for (int b = 0; b < skeleton.Count; b++) Assert.True(skeleton.Bones[b].Parent < b, $"{skeleton.Bones[b].Name} comes before its parent");
        Assert.Equal("thigh_l", skeleton.Bones[skeleton.Bones[skeleton.Find("swing_tunic_00_0")].Parent].Name);
        Assert.True(player.Rig.Complete);
        Assert.Equal(skeleton.Find("pelvis"), player.Rig.Pelvis);
        Assert.Contains("socket_hand_l", player.Rig.Sockets.Keys);
        Assert.Contains("socket_hand_r", player.Rig.Sockets.Keys);
        Assert.Equal(skeleton.Find("hand_r"), player.Rig.Sockets["socket_hand_r"].Bone);

        // The body less what the townsman's clothes hide (hair and beard hide nothing).
        var union = new HashSet<int>(Hidden(Asset("outfits", "townsman", "townsman_male_fit.npz")));
        union.UnionWith(Hidden(Asset("hair", "hair_collar_male_fit.npz")));
        union.UnionWith(Hidden(Asset("hair", "beard_short_male_fit.npz")));
        Assert.Equal(41178, union.Count);
        Assert.Equal(union.Count, player.HiddenTriangles);
        var body = model.Parts[0];
        Assert.Equal("skin_male", body.Material!.Name);
        Assert.Equal((107024 - union.Count) * 3, body.IndexCount);

        // One part a primitive: the body, eyeball, cornea, lashes, tooth, gum, tongue; the townsman's eight; hair; beard.
        Assert.Equal(7 + 8 + 1 + 1, model.Parts.Length);
        Assert.All(model.Parts, p => Assert.NotNull(p.Material));
        var byName = model.Parts.GroupBy(p => p.Material!.Name).ToDictionary(g => g.Key, g => g.First());
        Assert.True(byName["cornea"].Material!.Hidden);
        Assert.Equal(1, model.Parts.Count(p => p.Material!.Hidden));
        Assert.Equal(0.5f, byName["eyelashes"].Material!.AlphaCutoff);
        Assert.True(byName["eyelashes"].AlphaTest && byName["eyelashes"].TwoSided);
        Assert.Equal(0.35f, byName["hair"].Material!.AlphaCutoff, 5);
        Assert.Equal(0.35f, byName["beard"].Material!.AlphaCutoff, 5);
        Assert.True(byName["hair"].Material!.VertexColor && byName["wool_tunic"].Material!.VertexColor);
        Assert.False(byName["skin_male"].Material!.VertexColor);
        Assert.Equal(0f, byName["skin_male"].Material!.AlphaCutoff);
        Assert.Null(byName["tooth"].Material!.BaseColor);
        Assert.Equal("", byName["tooth"].Texture);
        // An earlier build: the player's own skin map in place of the base's colour map (the scalp and the beard's area shaded); the base's
        // normal and ORM maps. An older folder: the base's colour map.
        Assert.Equal(PlayerSkin() ? "people:base_male/skin_male_player_basecolor" : "people:base_male/base_male_skin_male_basecolor", byName["skin_male"].Texture);
        Assert.Equal("people:base_male/skin_male_normal", byName["skin_male"].Material!.Normal);
        Assert.Equal("people:base_male/base_male_skin_male_orm", byName["skin_male"].Material!.Orm);
        // The hair's strand maps are the same bytes in the beard's file: kept once, under the name of the first file the library read
        // them from. The tests share one library, so that is the player's hair or a crowd person's (person_05's hair has the same
        // strands), by which test ran first (the full run met person_05's first).
        Assert.Equal(byName["hair"].Material!.BaseColor, byName["beard"].Material!.BaseColor);
        Assert.Matches("^people:[a-z0-9_]+/hair_basecolor$", byName["beard"].Material!.BaseColor);

        Assert.NotNull(model.Tangents);
        Assert.NotNull(model.Colors);
        Assert.Equal(model.Vertices.Length, model.Tangents!.Length);
        Assert.Equal(model.Vertices.Length, model.Colors!.Length);
        Assert.All(model.Vertices, v => Assert.True(v.Bone(0) < skeleton.Count && v.Bone(1) < skeleton.Count && v.Bone(2) < skeleton.Count && v.Bone(3) < skeleton.Count));
        Assert.All(model.Indices, i => Assert.True(i < model.Vertices.Length));

        Assert.Empty(player.Textures);   // this library decodes no images (The_maps_of_*)
        Assert.Same(player, Lib().Person(PersonRecipe.Player));
        output.WriteLine($"{model.Vertices.Length} vertices, {model.Indices.Length / 3} triangles, {model.Parts.Length} parts");
    }

    [Fact]
    public void The_woman_wears_the_townswomans_clothes_and_her_head_cloth_leaves_her_braids_out()
    {
        var woman = Lib().Person(PersonRecipe.Woman);
        foreach (var line in Lib().Log) output.WriteLine(line);
        var model = woman.Model;
        Assert.Equal(BaseBones(model.Skeleton) + 96, model.Skeleton.Count);   // the townswoman's swing bones; no braids' (the hair is left out)
        Assert.Equal(-1, model.Skeleton.Find("swing_braid_l_0"));
        Assert.True(woman.Rig.Complete);
        Assert.DoesNotContain(model.Parts, p => p.Material!.Name == "hair");
        Assert.Contains(model.Parts, p => p.Material!.Name == "linen_veil");
        Assert.Equal(Hidden(Asset("outfits", "townswoman", "townswoman_female_fit.npz")).Distinct().Count(), woman.HiddenTriangles);
        Assert.Equal(51659, woman.HiddenTriangles);
        Assert.Equal((107024 - 51659) * 3, model.Parts[0].IndexCount);
        // The tiles are the same bytes as the townsman's: named for whichever of the two this library met first.
        Assert.Matches("^people:(townsman_male|townswoman_female)/wool_basecolor$", model.Parts.First(p => p.Material!.Name == "wool_tunic").Material!.BaseColor);
    }

    [Fact]
    public void The_maps_of_the_player_are_decoded_each_at_its_size_once()
    {
        var lib = WithImages();
        var player = lib.Person(PersonRecipe.Player);
        foreach (var line in lib.Log) output.WriteLine(line);
        var model = player.Model;
        // Every map a part names is there, decoded at the size its PNG says, once.
        var named = model.Parts.SelectMany(p => new[] { p.Material!.BaseColor, p.Material.Normal, p.Material.Orm }).OfType<string>().Distinct().ToArray();
        Assert.Equal(named.Order(), player.Textures.Select(t => t.Name).Order());
        Assert.Equal(player.Textures.Count, player.Textures.Select(t => t.Name).Distinct().Count());
        foreach (var t in player.Textures)
        {
            Assert.Equal(t.Width * t.Height * 4, t.Rgba.Length);
            Assert.StartsWith("people:", t.Name);
        }
        var skin = player.Textures.Single(t => t.Name == (PlayerSkin() ? "people:base_male/skin_male_player_basecolor" : "people:base_male/base_male_skin_male_basecolor"));
        Assert.Equal((4096, 4096), (skin.Width, skin.Height));
        output.WriteLine($"{player.Textures.Count} textures, {player.Textures.Sum(t => (long)t.Rgba.Length) / (1024 * 1024)} MB of maps");
        // The woman's tiles are the same bytes as the townsman's: decoded once, kept under his names.
        var woman = lib.Person(PersonRecipe.Woman);
        foreach (var t in woman.Textures) Assert.Equal(t.Width * t.Height * 4, t.Rgba.Length);
        var shared = woman.Textures.Where(t => t.Name.StartsWith("people:townsman_male/", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(shared);
        Assert.All(shared, t => Assert.Same(player.Textures.Single(p => p.Name == t.Name), t));
        Assert.Equal(player.Textures.Concat(woman.Textures).Distinct().Sum(t => (long)t.Rgba.Length), lib.TextureBytes);
    }

    /// <summary>Whether the folder has the player's own skin map.</summary>
    static bool PlayerSkin() => File.Exists(Path.Combine(Lib().Folder, PersonRecipe.PlayerSkinMap));

    [Fact]
    public void The_player_s_skin_is_his_own_map_and_without_it_the_base_s_said_once()
    {
        // An earlier build: only the skin's base colour is the player's; the base's normal and ORM maps, and the base man without the
        // recipe's map (a crowd man, a folder) as made.
        Assert.SkipUnless(PlayerSkin(), "the models repository's files are too old (no player skin map)");
        var lib = PeopleLibrary.TryOpen(Lib().Folder, out _, decodeImages: false)!;
        PartMaterial Skin(PersonRecipe recipe) => lib.Person(recipe).Model.Parts.First(p => p.Material!.Name == "skin_male").Material!;
        var own = Skin(PersonRecipe.Player);
        var plain = Skin(PersonRecipe.Player with { SkinMap = null });
        Assert.Equal("people:base_male/skin_male_player_basecolor", own.BaseColor);
        Assert.Equal("people:base_male/base_male_skin_male_basecolor", plain.BaseColor);
        Assert.Equal((plain.Normal, plain.Orm), (own.Normal, own.Orm));
        Assert.NotEqual(PersonRecipe.Player.Key, (PersonRecipe.Player with { SkinMap = null }).Key);
        // The eyes and lashes keep the base's maps.
        Assert.Equal(lib.Person(PersonRecipe.Player with { SkinMap = null }).Model.Parts.First(p => p.Material!.Name == "eyelashes").Material!.BaseColor,
                     lib.Person(PersonRecipe.Player).Model.Parts.First(p => p.Material!.Name == "eyelashes").Material!.BaseColor);
        // A map the folder lacks: the base's, said once however many people are built so.
        var missing = PersonRecipe.Player with { SkinMap = "assets/characters/base_male/player/none.png" };
        Assert.Equal("people:base_male/base_male_skin_male_basecolor", Skin(missing).BaseColor);
        Assert.Equal("people:base_male/base_male_skin_male_basecolor", Skin(missing with { Name = "another", Wear = ["townsman_male", "hair_collar_male"] }).BaseColor);
        foreach (var line in lib.Log) output.WriteLine(line);
        Assert.Single(lib.Log, l => l.Contains("none.png", StringComparison.Ordinal));
    }

    /// <summary>Each bone's model matrix in the rest pose.</summary>
    internal static Matrix4x4[] RestModel(Skeleton skeleton)
    {
        var local = new BoneTransform[skeleton.Count];
        var model = new Matrix4x4[skeleton.Count];
        PoseEvaluator.Rest(skeleton, local);
        PoseEvaluator.ToModel(skeleton, local, model);
        return model;
    }

    [Theory]
    [InlineData("player")]
    [InlineData("woman")]
    public void A_person_stands_upright_on_the_ground_and_faces_plus_Y(string who)
    {
        var person = Lib().Person(who == "player" ? PersonRecipe.Player : PersonRecipe.Woman);
        var s = person.Model.Skeleton;
        var m = RestModel(s);
        Vector3 At(string bone) => m[s.Find(bone)].Translation;
        var head = At("head");
        output.WriteLine($"head {head}, eyes {At("eye_l")} {At("eye_r")}, hands {At("hand_l")} {At("hand_r")}, feet {At("foot_l")} {At("ball_l")}, "
                         + $"bounds {person.Model.BoundsMin} {person.Model.BoundsMax}");
        Assert.InRange(head.Z, 1.4f, 1.7f);
        Assert.True(At("eye_l").Y > head.Y + 0.05f && At("eye_r").Y > head.Y + 0.05f, "the eyes are ahead (+Y) of the head");
        Assert.True(At("eye_l").X < 0f && At("eye_r").X > 0f, "the left eye at −X, the right at +X");
        Assert.True(At("hand_l").X < -0.2f && At("hand_r").X > 0.2f, "the left hand at −X, the right at +X");
        Assert.True(At("thigh_l").X < 0f && At("thigh_r").X > 0f);
        foreach (var foot in new[] { "foot_l", "foot_r", "ball_l", "ball_r" }) Assert.InRange(At(foot).Z, 0f, 0.15f);
        Assert.True(At("ball_l").Y > At("foot_l").Y, "the toes are ahead of the ankle");
        Assert.InRange(person.Model.BoundsMin.Z, -0.03f, 0.03f);
        Assert.InRange(person.Model.BoundsMax.Z, 1.55f, 1.85f);
        // The sockets are at the fists.
        var hand = person.Rig.Sockets["socket_hand_r"];
        var socket = PoseMath.Multiply(PoseMath.ToMatrix(hand.Offset), m[hand.Bone]).Translation;
        Assert.InRange(Vector3.Distance(socket, At("hand_r")), 0.03f, 0.15f);
        Assert.True(socket.X > 0.2f);
    }

    [Theory]
    [InlineData("player")]
    [InlineData("woman")]
    public void The_inverse_binds_undo_the_rest_pose_and_a_vertex_skins_back_to_itself(string who)
    {
        var person = Lib().Person(who == "player" ? PersonRecipe.Player : PersonRecipe.Woman);
        var model = person.Model;
        var s = model.Skeleton;
        var m = RestModel(s);
        var skin = new Matrix4x4[s.Count];
        PoseEvaluator.ToSkin(s, m, skin);
        float worst = 0f;
        for (int b = 0; b < s.Count; b++)
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    worst = MathF.Max(worst, MathF.Abs(skin[b][r, c] - (r == c ? 1f : 0f)));
        output.WriteLine($"worst skin matrix entry off the identity at rest: {worst:g3}");
        Assert.True(worst < 2e-4f, $"a bone's inverse bind is off its rest by {worst}");
        float far = 0f;
        foreach (var v in model.Vertices) far = MathF.Max(far, Vector3.Distance(PoseEvaluator.SkinPosition(v, skin), v.Position));
        output.WriteLine($"farthest vertex skinned at rest from itself: {far * 1000:0.000} mm");
        Assert.True(far < 0.5e-3f);
    }

    [Fact]
    public void The_bodys_triangles_face_out_after_the_change_of_axes()
    {
        var model = Lib().Person(PersonRecipe.Player).Model;
        var body = model.Parts[0];
        int agree = 0, n = 0;
        for (int i = body.FirstIndex; i < body.FirstIndex + body.IndexCount; i += 3)
        {
            var a = model.Vertices[model.Indices[i]]; var b = model.Vertices[model.Indices[i + 1]]; var c = model.Vertices[model.Indices[i + 2]];
            var face = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
            if (Vector3.Dot(face, a.Normal + b.Normal + c.Normal) > 0f) agree++;
            n++;
        }
        output.WriteLine($"{agree} of {n} body triangles wound counter-clockwise seen from where their normals point");
        Assert.True(agree > 0.995 * n);
        // The tangents are across the normals, and their handedness survives (the change is a rotation).
        var t = model.Tangents!;
        for (int i = 0; i < model.Vertices.Length; i += 97)
            Assert.True(MathF.Abs(Vector3.Dot(new Vector3(t[i].X, t[i].Y, t[i].Z), model.Vertices[i].Normal)) < 0.05f);
    }

    [Fact]
    public void The_texture_coordinates_are_glTFs_as_stored_v_down_the_image()
    {
        // A hair lock's root is at the top of the strand map: with glTF's v kept as stored
        // (0 at the image's top row, which a TextureImage holds first), the collar hair's higher vertices have the smaller v.
        // 1.1's loader flips v because 1.1's images are bottom-up; flipped here, the roots would hang at the tips.
        var model = Lib().Person(PersonRecipe.Player).Model;
        var hair = model.Parts.Single(p => p.Material!.Name == "hair");
        var used = model.Indices.Skip(hair.FirstIndex).Take(hair.IndexCount).Distinct().Select(i => model.Vertices[i]).ToArray();
        double mz = used.Average(v => v.Position.Z), mv = used.Average(v => v.Uv.Y);
        double cov = used.Sum(v => (v.Position.Z - mz) * (v.Uv.Y - mv));
        double corr = cov / Math.Sqrt(used.Sum(v => Math.Pow(v.Position.Z - mz, 2)) * used.Sum(v => Math.Pow(v.Uv.Y - mv, 2)));
        output.WriteLine($"the collar hair: correlation of height and v {corr:0.00} over {used.Length} vertices");
        Assert.True(corr < -0.3, $"correlation {corr}");
    }

    [Fact]
    public void The_male_motions_load_with_their_notes()
    {
        var clips = Lib().Motions("male");
        foreach (var line in Lib().Log) output.WriteLine(line);
        // 153 clips at the models repository's (its crouch): the tests take whichever is there.
        Assert.True(clips.Count >= 153, $"{clips.Count} clips");
        Assert.Equal("stand", clips[0].Name);
        Assert.Contains(clips, c => c.Name == "walk_frail");
        Assert.Same(clips, Lib().Motions("male"));
        var walk = clips.Single(c => c.Name == "walk");
        Assert.True(walk.Loops);
        Assert.Equal(1.1f, walk.Duration, 5);
        Assert.Equal("walk", walk.Notes.Kind);
        Assert.Equal(1.25f, walk.Notes.GroundSpeed, 5);
        Assert.Equal(0f, walk.Notes.HeadingDeg);
        Assert.Equal(1, walk.Notes.Strides);
        Assert.Equal(1.375f, walk.Notes.StrideM, 5);
        Assert.Equal([new ClipFootfall("Lwalk", 0f), new ClipFootfall("Rwalk", 0.55f)], walk.Notes.Footfalls);
        Assert.True(walk.Notes.Footfalls[0].Left);
        Assert.Contains("\"on_ground_share\"", walk.Notes.Json);
        var turn = clips.Single(c => c.Name == "turn_left_90");
        Assert.False(turn.Loops);
        Assert.Equal("turn", turn.Notes.Kind);
        Assert.Equal(90f, turn.Notes.TurnDeg);
        Assert.True(turn.Notes.RootTurns);
        Assert.True(float.IsNaN(walk.Notes.TurnDeg));
        Assert.Equal("kneel_down", clips.Single(c => c.Name == "kneel").Notes.After);
        Assert.Equal("socket_hand_r", clips.Single(c => c.Name == "stand_onehand").Notes.Held);
        Assert.All(clips, c => Assert.True(c.Duration > 0f));
        var gltf = (GltfClip)walk;
        // Every bone of the base turned (78; 86); moved, the pelvis — and the bend helpers, turned and moved.
        int based = BaseBones(Lib().Person(PersonRecipe.Player).Model.Skeleton);
        Assert.Equal(based, gltf.RotatedBones.Count());
        Assert.Equal(["pelvis"], gltf.MovedBones.Where(b => !IsBendHelper(b)));
        if (based > 78)
        {
            Assert.Equal(BendHelpers.Select(h => h.Name).Order(), gltf.RotatedBones.Where(IsBendHelper).Order());
            Assert.Contains(gltf.MovedBones, IsBendHelper);
        }
        else Assert.DoesNotContain(gltf.MovedBones, IsBendHelper);
        Assert.Empty(gltf.FaceTargets);
        Assert.Equal(52, ((GltfClip)clips.Single(c => c.Name == "talk")).FaceTargets.Count);
    }

    [Fact]
    public void A_turn_on_the_spot_ends_turned_to_the_left_about_plus_Z()
    {
        var player = Lib().Person(PersonRecipe.Player);
        var s = player.Model.Skeleton;
        var local = new BoneTransform[s.Count];
        foreach (var (name, degrees) in new[] { ("turn_left_90", 90f), ("turn_left_45", 45f), ("turn_right_90", -90f) })
        {
            var turn = Lib().Motions("male").Single(c => c.Name == name);
            turn.Sample(0f, s, local);
            var q0 = local[s.Find("root")].Rotation;
            Assert.True(MathF.Abs(q0.W) > 0.9999f, $"{name} starts unturned");
            turn.Sample(turn.Duration, s, local);
            var q = local[s.Find("root")].Rotation;
            // A turn about +Z alone, by the notes' angle, counter-clockwise seen from above (to the person's left) for a positive one.
            Assert.True(MathF.Abs(q.X) < 1e-4f && MathF.Abs(q.Y) < 1e-4f, $"{name}: {q}");
            float half = degrees * MathF.PI / 360f;
            Assert.Equal((float)Math.Sin(half), q.Z * MathF.Sign(q.W), 4);
            Assert.Equal((float)Math.Cos(half), MathF.Abs(q.W), 4);
            // The +Y the person faced is turned to −X for a left turn (their left is −X).
            var ahead = Vector3.Transform(Vector3.UnitY, q);
            Assert.Equal(-(float)Math.Sin(degrees * Math.PI / 180), ahead.X, 4);
        }
    }

    [Fact]
    public void A_loop_ends_where_it_starts_and_sampling_is_repeatable()
    {
        var player = Lib().Person(PersonRecipe.Player);
        var s = player.Model.Skeleton;
        var a = new BoneTransform[s.Count];
        var b = new BoneTransform[s.Count];
        foreach (var clip in Lib().Motions("male").Where(c => c.Loops))
        {
            clip.Sample(0f, s, a);
            clip.Sample(clip.Duration, s, b);   // wraps to the start
            Assert.Equal(a, b);
            clip.Sample(clip.Duration - 1e-4f, s, b);   // the last key is the first again
            for (int i = 0; i < s.Count; i++)
            {
                Assert.True(MathF.Abs(Quaternion.Dot(a[i].Rotation, b[i].Rotation)) > 0.99995f, $"{clip.Name}: {s.Bones[i].Name} jumps across the seam");
                Assert.True(Vector3.Distance(a[i].Translation, b[i].Translation) < 1e-3f, $"{clip.Name}: {s.Bones[i].Name} moves across the seam");
            }
        }
        var walk = Lib().Motions("male").Single(c => c.Name == "walk");
        for (float t = -1f; t < 3f; t += 0.0371f)
        {
            walk.Sample(t, s, a);
            walk.Sample(t, s, b);
            Assert.Equal(a, b);
        }
        // Bones the clip does not move keep the skeleton's own rest: the swing bones, and every bone's length. The bend helpers
        // are moved by their rule along their own directions, keyed by design: not here.
        walk.Sample(0.3f, s, a);
        for (int i = 0; i < s.Count; i++)
            if (s.Bones[i].Name != "pelvis" && !IsBendHelper(s.Bones[i].Name)) Assert.Equal(s.Bones[i].Rest.Translation, a[i].Translation);
        Assert.Equal(s.Bones[s.Find("swing_tunic_03_2")].Rest, a[s.Find("swing_tunic_03_2")]);
    }

    [Fact]
    public void The_walk_steps_ahead_along_plus_Y()
    {
        // A walk on the spot carries the planted foot back: between the two footfalls the left foot moves towards −Y.
        var player = Lib().Person(PersonRecipe.Player);
        var s = player.Model.Skeleton;
        var walk = Lib().Motions("male").Single(c => c.Name == "walk");
        var local = new BoneTransform[s.Count];
        var model = new Matrix4x4[s.Count];
        Vector3 Foot(float t)
        {
            walk.Sample(t, s, local);
            PoseEvaluator.ToModel(s, local, model);
            return model[s.Find("foot_l")].Translation;
        }
        var down = Foot(0.05f);
        var later = Foot(0.45f);
        output.WriteLine($"left foot at 0.05 s {down}, at 0.45 s {later}");
        Assert.True(later.Y < down.Y - 0.2f, "the planted left foot goes back (−Y) under a body walking ahead (+Y)");
        Assert.True(down.X < 0f);
        Assert.InRange(model[s.Find("head")].Translation.Z, 1.4f, 1.7f);
    }

    [Fact]
    public void The_female_motions_play_on_the_woman_by_bone_name()
    {
        var woman = Lib().Person(PersonRecipe.Woman);
        var clips = Lib().Motions("female");
        Assert.True(clips.Count >= 153, $"{clips.Count} clips");   // 153
        var s = woman.Model.Skeleton;
        var local = new BoneTransform[s.Count];
        var model = new Matrix4x4[s.Count];
        clips.Single(c => c.Name == "stand").Sample(1f, s, local);
        PoseEvaluator.ToModel(s, local, model);
        Assert.InRange(model[s.Find("head")].Translation.Z, 1.35f, 1.65f);
        Assert.InRange(model[s.Find("ball_l")].Translation.Z, -0.01f, 0.1f);
        Assert.Equal(s.Bones[s.Count - 1].Rest, local[s.Count - 1]);   // a swing bone at its rest
    }

    [Fact]
    public void The_png_decoder_reads_the_bodys_maps_quickly()
    {
        var lib = Lib();
        var file = Formats.Gltf.GltfFile.Load(Asset("characters", "base_male", "base_male.glb"));
        for (int i = 0; i < file.Images.Count; i++)
        {
            var png = file.ImageBytes(i);
            Assert.True(PngDecoder.TryReadHeader(png, out int hw, out int hh, out int depth, out int colour));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var rgba = PngDecoder.Decode(png.AsMemory(), out int w, out int h);
            watch.Stop();
            Assert.NotNull(rgba);
            Assert.Equal((hw, hh), (w, h));
            output.WriteLine($"{file.Images[i].Name}: {w}×{h}, depth {depth}, colour type {colour}, {png.Count / 1024} KB, decoded in {watch.ElapsedMilliseconds} ms");
        }
        Assert.NotNull(lib);
    }
}
