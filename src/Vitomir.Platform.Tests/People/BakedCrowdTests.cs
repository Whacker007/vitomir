// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.People;
using Vitomir.Platform.Core;
using static Vitomir.Platform.Tests.PeopleMotion.PeopleWorlds;

namespace Vitomir.Platform.Tests.People;

/// <summary>
/// The models repository's crowd as it was baked ( each person in a folder of its own, its shaped body with its own skin
/// and eyes, its outfit, hair and beard fitted to it). The file's new fields read (and a crowd not baked); a baked person
/// loaded on its own skeleton — the base's 86 bones in the base's order at its own proportions, its own skin map, its clothes hiding what the
/// base's outfit hides, no look; the base's motions played on it with the pelvis's keyed place scaled by its hips' height over its base's
/// (<see cref="ProportionedClip"/>), which keeps its feet on the ground where the motions as made leave them in the air; its kind drawn at
/// its own size, striding by its proportion, its planted feet still. The tests that read the files skip without <c>VITOMIR_MODELS_DIR</c>
/// or a crowd.
/// </summary>
public sealed class BakedCrowdTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.000") => v.ToString(format, CultureInfo.InvariantCulture);

    const string Round35 = """
        [
          {"name": "person_04", "sex": "male", "ok": true, "role": "field_hand", "kind": "kindred", "hair": "light_brown",
           "groom": {"hair": "crop", "beard": null, "stubble": "none", "grey": 0.0, "covered": false},
           "age": 22.2, "height_m": 1.575, "eye": "brown", "skin": {"melanin": 0.03},
           "folder": "person_04", "outfit": "townsman",
           "files": {"body": "person_04.glb", "outfit": "person_04_townsman.glb", "hair": "person_04_hair.glb", "record": "person_04.json"},
           "hip_height_m": 0.8251, "base_hip_height_m": 0.9023},
          {"name": "person_02", "sex": "male", "role": "guard",
           "groom": {"hair": "ear", "beard": "beard_short", "covered": false},
           "folder": "person_02", "outfit": "guard",
           "files": {"body": "person_02.glb", "outfit": "person_02_guard.glb", "hair": "person_02_hair.glb", "beard": "person_02_beard.glb", "record": "person_02.json"},
           "hip_height_m": 0.9113, "base_hip_height_m": 0.9023},
          {"name": "person_x", "sex": "female", "role": "townswoman", "groom": {"covered": true}, "height_m": 1.6}
        ]
        """;

    [Fact]
    public void A_round_35_crowd_names_each_persons_folder_files_and_hips_and_an_older_one_is_not_baked()
    {
        var people = Crowd.Read(Round35);
        var p04 = people[0];
        Assert.True(p04.IsBaked);
        Assert.Equal(("person_04", "townsman", "assets/characters/crowd/person_04"), (p04.Folder, p04.Outfit, p04.BakedFolder));
        Assert.Equal(new CrowdFiles("person_04.glb", "person_04_townsman.glb", "person_04_hair.glb", null, "person_04.json"), p04.Files);
        Assert.Equal(["person_04_townsman.glb", "person_04_hair.glb"], p04.Files!.Worn());
        Assert.Equal(0.8251f / 0.9023f, p04.Proportion, 6);
        var recipe = p04.BakedRecipe("person_04 as Merchant");
        Assert.Equal(("person_04 as Merchant", "male", "person_04", "assets/characters/crowd/person_04"), (recipe.Name, recipe.Sex, recipe.Base, recipe.Folder));
        Assert.Equal(["person_04_townsman", "person_04_hair"], recipe.Wear);
        Assert.Equal(["person_04_hair=hair_crop_male", "person_04_townsman=townsman_male"], recipe.FittedFrom!.Select(kv => kv.Key + "=" + kv.Value).Order(StringComparer.Ordinal));
        Assert.Null(recipe.Look);                                                // its own skin and hair: no tint
        Assert.Contains("in assets/characters/crowd/person_04", recipe.Key);
        // A guard's beard is fitted from his own beard.
        Assert.Equal("beard_short_male", people[1].BakedRecipe().FittedFrom!["person_02_beard"]);
        Assert.Equal(["person_02_guard", "person_02_hair", "person_02_beard"], people[1].BakedRecipe().Wear);
        // A person of a crowd: not baked, of its base's proportions.
        Assert.False(people[2].IsBaked);
        Assert.Null(people[2].BakedFolder);
        Assert.Equal(1f, people[2].Proportion);
        Assert.Throws<InvalidOperationException>(() => people[2].BakedRecipe());
    }

    /// <summary>
    /// A person with <c>hooded</c> and <c>files.hood</c> wears its hood after its outfit, fitted from the
    /// base's hood (which covers the hair: the hair is left out when it is built); <c>hooded</c> without a hood file is not hooded; a person of
    /// an older round has none.
    /// </summary>
    [Fact]
    public void A_round_40_person_with_a_hood_wears_it_over_the_outfit_fitted_from_the_bases_hood()
    {
        var people = Crowd.Read("""
            [
              {"name": "person_04", "sex": "male", "role": "field_hand", "groom": {"hair": "crop"}, "hooded": true, "tunic_dye": "undyed",
               "folder": "person_04", "outfit": "townsman",
               "files": {"body": "person_04.glb", "outfit": "person_04_townsman.glb", "hood": "person_04_hood.glb", "hair": "person_04_hair.glb", "record": "person_04.json"}},
              {"name": "person_11", "sex": "male", "role": "merchant", "groom": {"hair": "crop"}, "hooded": true,
               "folder": "person_11", "outfit": "townsman",
               "files": {"body": "person_11.glb", "outfit": "person_11_townsman.glb", "hair": "person_11_hair.glb", "record": "person_11.json"}}
            ]
            """);
        var hooded = people[0];
        Assert.True(hooded.Hooded);
        Assert.Equal("person_04_hood.glb", hooded.Files!.Hood);
        Assert.Equal(["person_04_townsman.glb", "person_04_hood.glb", "person_04_hair.glb"], hooded.Files.Worn());
        var recipe = hooded.BakedRecipe();
        Assert.Equal(["person_04_townsman", "person_04_hood", "person_04_hair"], recipe.Wear);
        Assert.Equal("hood_male", recipe.FittedFrom!["person_04_hood"]);
        Assert.False(people[1].Hooded);   // hooded, but no hood file
        Assert.Null(people[1].Files!.Hood);
        Assert.False(Crowd.Read(Round35)[0].Hooded);
    }

    /// <summary>
    /// (on its files: skipped before it.) the crowd's baked files refer to the maps they share by a relative <c>uri</c>
    /// (<c>../shared/&lt;file&gt;</c>, no buffer view), followed from the file's folder; each such map is read and named once for everyone who
    /// wears it (by its file); a hooded person is built with its hood and without its hair.
    /// </summary>
    [Fact]
    public void Round_40_s_shared_maps_are_followed_and_named_once_and_a_hooded_person_has_no_hair()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(PeopleLibrary.FolderVariable) is { Length: > 0 } dir && Directory.Exists(dir), "the models repository's files are not here (VITOMIR_MODELS_DIR)");
        var lib = PeopleLibrary.TryOpen(Environment.GetEnvironmentVariable(PeopleLibrary.FolderVariable)!, out _, decodeImages: false)!;
        var crowd = lib.Crowd();
        var hooded = crowd.FirstOrDefault(p => p.Hooded);
        Assert.SkipUnless(hooded is not null, "the models repository's crowd has no hoods here (too old)");
        // The files refer to their shared maps by uri.
        string folder = Path.Combine(lib.Folder, hooded!.BakedFolder!.Replace('/', Path.DirectorySeparatorChar));
        var tunic = Vitomir.Formats.Gltf.GltfFile.Load(Path.Combine(folder, hooded.Files!.Outfit!));
        var shared = Enumerable.Range(0, tunic.Images.Count).Select(tunic.ImageFile).OfType<string>().ToArray();
        output.WriteLine($"{hooded.Files.Outfit}: {tunic.Images.Count} images, {shared.Length} in shared/: {string.Join(", ", shared.Select(Path.GetFileName))}");
        Assert.NotEmpty(shared);
        Assert.All(shared, f => { Assert.True(File.Exists(f), f); Assert.Equal("shared", Path.GetFileName(Path.GetDirectoryName(f))); });
        // Two people in the townsman's tunic: the shared maps of their tunics the same texture, named once by the file.
        var other = crowd.First(p => p != hooded && p.IsBaked && p.Outfit == hooded.Outfit && Directory.Exists(Path.Combine(lib.Folder, p.BakedFolder!.Replace('/', Path.DirectorySeparatorChar))));
        var a = lib.Person(hooded.BakedRecipe());
        var b = lib.Person(other.BakedRecipe());
        static IEnumerable<string> Maps(PersonModel m) => m.Model.Parts.Select(p => p.Material).OfType<PartMaterial>()
            .SelectMany(x => new[] { x.BaseColor, x.Normal, x.Orm }).OfType<string>();
        var common = Maps(a).Intersect(Maps(b)).ToArray();
        output.WriteLine($"{hooded.Name} and {other.Name} share {common.Length} maps: {string.Join(", ", common.Take(8))}");
        Assert.NotEmpty(common);
        foreach (var file in shared) Assert.NotNull(lib.TextureOfFile(file));
        // The hood worn, the hair left out (the hood covers it).
        Assert.Equal(0, a.Model.Parts.Count(p => p.Material is { } m && PersonLooks.IsHair(m)));
        Assert.Contains(Path.GetFileNameWithoutExtension(hooded.Files.Hood!), a.Recipe.Wear);
    }

    static readonly Lazy<PeopleLibrary?> library = new(() =>
        Environment.GetEnvironmentVariable(PeopleLibrary.FolderVariable) is { Length: > 0 } dir ? PeopleLibrary.TryOpen(dir, out _, decodeImages: false) : null);

    /// <summary>The library (its own, no images) and the baked man furthest from his base's proportions whose folder is here (person_04, ×0.914).</summary>
    static (PeopleLibrary Lib, CrowdPerson Man) Baked()
    {
        Assert.SkipUnless(library.Value is not null, "the models repository's files are not here (VITOMIR_MODELS_DIR)");
        var lib = library.Value!;
        var man = lib.Crowd().Where(p => p.Sex == "male" && p.IsBaked && p.Role != "guard"
                                         && Directory.Exists(Path.Combine(lib.Folder, p.BakedFolder!.Replace('/', Path.DirectorySeparatorChar))))
                     .OrderBy(p => MathF.Abs(1f - p.Proportion)).LastOrDefault();
        Assert.SkipUnless(man is not null, "the models repository's crowd is not baked here (too old)");
        return (lib, man!);
    }

    /// <summary>Each bone's joint in model space at a clip's first frame on a skeleton.</summary>
    static Vector3[] Joints(Skeleton skeleton, IAnimationClip clip, float seconds = 0f)
    {
        var local = new BoneTransform[skeleton.Count];
        var model = new Matrix4x4[skeleton.Count];
        clip.Sample(seconds, skeleton, local);
        PoseEvaluator.ToModel(skeleton, local, model);
        return [.. model.Select(m => m.Translation)];
    }

    static Vector3[] RestJoints(Skeleton skeleton)
    {
        var local = new BoneTransform[skeleton.Count];
        var model = new Matrix4x4[skeleton.Count];
        PoseEvaluator.Rest(skeleton, local);
        PoseEvaluator.ToModel(skeleton, local, model);
        return [.. model.Select(m => m.Translation)];
    }

    [Fact]
    public void A_baked_person_loads_on_the_bases_skeleton_at_its_own_proportions_in_its_own_skin_and_clothes()
    {
        var (lib, man) = Baked();
        var person = lib.Person(man.BakedRecipe());
        var bases = lib.Person(new PersonRecipe("the base", man.Sex, man.Base, []));
        var skeleton = person.Model.Skeleton;
        var baseSkeleton = bases.Model.Skeleton;
        output.WriteLine($"{man.Name}: {person.Recipe.Key}: {skeleton.Count} bones, {person.Model.Vertices.Length:N0} vertices, {person.HiddenTriangles:N0} of the body hidden; hips {F(man.HipHeight)} m of {F(man.BaseHipHeight)}");

        // The base's bones in the base's order (the outfit's swing bones after them), the rig complete.
        Assert.True(baseSkeleton.Count >= 86);
        for (int b = 0; b < baseSkeleton.Count; b++)
        {
            Assert.Equal(baseSkeleton.Bones[b].Name, skeleton.Bones[b].Name);
            Assert.Equal(baseSkeleton.Bones[b].Parent, skeleton.Bones[b].Parent);
        }
        Assert.True(person.Rig.Complete);

        // Its own proportions: its hips and its legs by its proportion, give or take a few per cent (the record's hips are the skin's, the
        // skeleton's joints the rig's).
        var rest = RestJoints(skeleton);
        var baseRest = RestJoints(baseSkeleton);
        int pelvis = skeleton.Find("pelvis"), thigh = skeleton.Find("thigh_l"), calf = skeleton.Find("calf_l"), foot = skeleton.Find("foot_l");
        float leg = Vector3.Distance(rest[thigh], rest[calf]) + Vector3.Distance(rest[calf], rest[foot]);
        float baseLeg = Vector3.Distance(baseRest[thigh], baseRest[calf]) + Vector3.Distance(baseRest[calf], baseRest[foot]);
        output.WriteLine($"pelvis at rest {F(rest[pelvis].Z)} m against the base's {F(baseRest[pelvis].Z)} (×{F(rest[pelvis].Z / baseRest[pelvis].Z)}); the leg ×{F(leg / baseLeg)}; the crowd's ×{F(man.Proportion)}");
        Assert.InRange(rest[pelvis].Z / baseRest[pelvis].Z, man.Proportion - 0.02f, man.Proportion + 0.02f);
        Assert.InRange(leg / baseLeg, man.Proportion - 0.03f, man.Proportion + 0.03f);

        // Its own skin map; its clothes hide what the base's outfit hides (its body keeps the base's triangles in order).
        var skin = Assert.Single(person.Model.Parts, p => p.Material is { } m && PersonLooks.IsSkin(m));
        Assert.Contains($"{man.Name}_skin", skin.Material!.BaseColor);
        var townsman = lib.Person(new PersonRecipe("the base dressed", man.Sex, man.Base, man.Hooded ? [$"{man.Outfit}_{man.Sex}", $"hood_{man.Sex}"] : [$"{man.Outfit}_{man.Sex}"]));   // and its hood
        Assert.Equal(townsman.HiddenTriangles, person.HiddenTriangles);
        Assert.True(person.HiddenTriangles > 1000, $"{person.HiddenTriangles}");
        Assert.Equal(man.Hair is null || man.Hooded ? 0 : 1, person.Model.Parts.Count(p => p.Material is { } m && PersonLooks.IsHair(m)));   // a hood leaves the hair out
        Assert.Null(person.HairGrey);                     // a baked hair carries its own colour and grey
        Assert.True(person.BaseHeight is > 1.4f and < 1.9f); // its record's height
        Assert.Same(person, lib.Person(man.BakedRecipe()));
    }

    [Fact]
    public void The_bases_motions_on_a_baked_person_stand_on_the_ground_with_the_pelvis_scaled_and_float_without()
    {
        var (lib, man) = Baked();
        var person = lib.Person(man.BakedRecipe());
        var bases = lib.Person(new PersonRecipe("the base", man.Sex, man.Base, []));
        var motions = lib.Motions(man.Sex);
        var scaled = ProportionedClip.Set(motions, man.Proportion);
        Assert.Equal(motions.Count, scaled.Count);
        Assert.Same(motions, ProportionedClip.Set(motions, 1f));
        var skeleton = person.Model.Skeleton;
        int ball = skeleton.Find("ball_l"), foot = skeleton.Find("foot_l"), pelvis = skeleton.Find("pelvis");
        foreach (var name in new[] { "stand", "crouch" })
        {
            if (motions.FirstOrDefault(c => c.Name == name) is not { } asMade) continue;
            var proportioned = scaled.Single(c => c.Name == name);
            var onBase = Joints(bases.Model.Skeleton, asMade);
            var onPerson = Joints(skeleton, proportioned);
            var unscaled = Joints(skeleton, asMade);
            output.WriteLine($"{name}: the base's ankle {F(onBase[foot].Z)} m, ball {F(onBase[ball].Z)}; {man.Name}'s scaled {F(onPerson[foot].Z)}, {F(onPerson[ball].Z)}; as made {F(unscaled[foot].Z)}, {F(unscaled[ball].Z)} (hips {F(onPerson[pelvis].Z)} against {F(unscaled[pelvis].Z)})");
            Assert.InRange(onPerson[ball].Z - onBase[ball].Z, -0.015f, 0.015f);   // on the ground as the base is
            Assert.InRange(onPerson[foot].Z - onBase[foot].Z, -0.02f, 0.015f);   // its ankle a little lower: a smaller foot
            Assert.True(unscaled[ball].Z - onBase[ball].Z > 0.04f, "as made, the hips at the base's height leave a shorter person's feet in the air");
        }
        // The notes' stride by the proportion; the rotations as made.
        var walk = scaled.Single(c => c.Name == "walk");
        var made = motions.Single(c => c.Name == "walk");
        Assert.Equal(made.Notes.GroundSpeed * man.Proportion, walk.Notes.GroundSpeed, 5);
        Assert.Equal((made.Duration, made.Loops, made.Notes.Footfalls.Count), (walk.Duration, walk.Loops, walk.Notes.Footfalls.Count));
        var a = new BoneTransform[skeleton.Count];
        var b = new BoneTransform[skeleton.Count];
        made.Sample(0.4f, skeleton, a);
        walk.Sample(0.4f, skeleton, b);
        for (int i = 0; i < a.Length; i++)
        {
            Assert.Equal(a[i].Rotation, b[i].Rotation);
            if (i != pelvis) Assert.Equal(a[i].Translation, b[i].Translation);
            else Assert.Equal(a[i].Translation * man.Proportion, b[i].Translation);
        }
    }

    [Fact]
    public void A_baked_persons_kind_is_drawn_at_its_size_strides_by_its_proportion_and_its_planted_feet_keep_still()
    {
        var (lib, man) = Baked();
        var person = lib.Person(man.BakedRecipe());
        var assets = new CharacterAssets();
        int k = PeopleKinds.Register(assets, person, ProportionedClip.Set(lib.Motions(man.Sex), man.Proportion), "baked", new PersonStyle { FidgetAfter = 8f });
        var kind = assets.Kind(k);
        Assert.Equal(1f, kind.Scale);                                                   // its figure carries its height
        Assert.Equal(1.25f * man.Proportion, kind.Motion(MotionRole.Walk).NaturalSpeed, 3);
        Assert.Equal(3.0f * man.Proportion, kind.Motion(MotionRole.Run).NaturalSpeed, 3);

        float speed = kind.Motion(MotionRole.Walk).NaturalSpeed;
        float yaw = DetMath.HalfPi;
        var v = new Vector3(DetMath.Cos(yaw), DetMath.Sin(yaw), 0f) * speed;
        var (world, e) = Make(assets, k, Floor(), Vector3.Zero, yaw);
        var skeleton = assets.ModelOf(k).Skeleton;
        int[] balls = [skeleton.Find("ball_l"), skeleton.Find("ball_r")];
        int settle = World.TicksPerSecond, ticks = 3 * World.TicksPerSecond;
        var path = new Vector3[2][];
        for (int f = 0; f < 2; f++) path[f] = new Vector3[ticks];
        var feet = Vector3.Zero;
        for (int t = 0; t < settle + ticks; t++)
        {
            feet += v * Tick;
            Step(world, e, feet, yaw, true, velocity: v);
            if (t >= settle) for (int f = 0; f < 2; f++) path[f][t - settle] = Bone(world, e, assets, balls[f]);
        }
        Assert.True(Finite(world, e));
        float sum = 0f, lowest = float.MaxValue;
        int n = 0;
        for (int f = 0; f < 2; f++)
        {
            float low = path[f].Min(p => p.Z);
            lowest = MathF.Min(lowest, low);
            for (int t = 1; t < ticks; t++)
            {
                if (path[f][t].Z > low + 0.01f || path[f][t - 1].Z > low + 0.01f) continue;
                var d = path[f][t] - path[f][t - 1];
                sum += MathF.Sqrt(d.X * d.X + d.Y * d.Y) / Tick;
                n++;
            }
        }
        float mean = sum / n * 100f;
        output.WriteLine($"{man.Name} walking at {F(speed, "0.00")} m/s: planted slide mean {F(mean, "0.0")} cm/s, the balls' lowest {F(lowest)} m");
        Assert.InRange(mean, 0f, 8f);
        Assert.InRange(lowest, -0.02f, 0.04f);   // on the ground, neither floating nor sunk
    }
}
