// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.People;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The townsfolk who do not fight drawn as the models repository's crowd (<see cref="TownsfolkBodies"/>). Who is drawn as whom
/// (a person of the template's sex and role, the same for the same list, two of a template different, the old man the eldest); the
/// outfits by role; the switch and the setting. In a session (skipped without the proving ground, the 1.1 install and the models
/// stream's files): the townsfolk on the crowd and the guards and the watch on 1.1's bodies; the merchant still protected; the tape
/// notes it and a replay draws the same person for each and agrees digest for digest; a tape without the note has 1.1's; a tape
/// with it that cannot be built here is refused; <c>--townsfolk-oneone</c>'s session is the earlier build's. The crowd as the models
/// stream baked it: who is drawn as whom from the baked roles (never a guard or the nun); the package carries each baked
/// person's folder; in a session each townsperson its baked person, the tape noting <c>baked</c>; a person whose folder is missing built
/// at run time (said once), the rest baked; <c>--crowd-built</c>'s session the earlier build's run-time crowd, its tape noting <c>models</c> and
/// replaying so.
/// </summary>
public sealed class TownsfolkBodiesTests(ITestOutputHelper output)
{
    static CrowdPerson P(string name, string sex, string role, float age = 30f, string? hair = "crop", bool covered = false) =>
        new(name, sex, role, "living", hair, null, covered, age, 1.7f, "", new CrowdSkin(0.03f, 0.4f, Vector3.One * 0.1f, 0f, 0f, 0f));

    static readonly CrowdPerson[] Crowd =
    [
        P("m1", "male", "guard", 30), P("m2", "male", "field_hand", 22), P("m3", "male", "guard", 44), P("m4", "male", "field_hand", 38),
        P("c1", "male", "cleric", 62, "monk"), P("c2", "male", "cleric", 20, "priest"),
        P("w1", "female", "townswoman", 33, null, covered: true), P("w2", "female", "craftswoman", 27, "braids"), P("w3", "female", "townswoman", 50, null, covered: true),
        P("n1", "female", "nun", 41, null, covered: true),
    ];

    [Fact]
    public void Each_townsperson_is_a_crowd_person_of_its_sex_and_role_the_same_for_the_same_list()
    {
        var night = TownsfolkBodies.Slots(night: true, 6);
        var day = TownsfolkBodies.Slots(night: false, 8);
        Assert.Equal(["Civ11female", "Civ07drunk", "Civ02beggar", "Monk", "Civ11female", "Merchant"], night);
        Assert.Equal(8, day.Length);
        foreach (var slots in new[] { night, day })
        {
            var given = TownsfolkBodies.Assign(slots, Crowd);
            Assert.Equal(given, TownsfolkBodies.Assign(slots, Crowd));   // the same list, the same people
            for (int i = 0; i < slots.Length; i++)
            {
                var spec = TownsfolkBodies.Specs[slots[i]];
                var p = Assert.IsType<CrowdPerson>(given[i]);
                output.WriteLine($"{slots[i]} -> {p.Name} ({p.Sex} {p.Role}) in {TownsfolkBodies.OutfitOf(p)}");
                Assert.Equal(spec.Sex, p.Sex);
                Assert.Contains(p.Role, spec.Roles.SelectMany(r => r));
            }
        }
        // Two townswomen at night are two women; the monk a cleric; the young lady the girl in braids; the old man the eldest layman.
        var n = TownsfolkBodies.Assign(night, Crowd);
        Assert.NotEqual(n[0]!.Name, n[4]!.Name);
        Assert.Equal("cleric", n[3]!.Role);
        var d = TownsfolkBodies.Assign(TownsfolkBodies.Slots(false, 6), Crowd);
        Assert.Equal("w2", d[1]!.Name);
        Assert.Equal("m3", d[3]!.Name);
        // Men are distinct while the laymen last (four of them here, five laymen at night with the drunk, the beggar and the merchant).
        Assert.Equal(3, new[] { n[1], n[2], n[5] }.Select(p => p!.Name).Distinct().Count());
        // The keepers of the peace are not here, and an empty pool draws nobody.
        Assert.False(TownsfolkBodies.Drawn("rudolfGuard"));
        Assert.False(TownsfolkBodies.Drawn("KnightOfStJohn_torch"));
        Assert.All(TownsfolkBodies.Assign(["rudolfGuard", "Monk"], [P("w", "female", "townswoman")]), Assert.Null);
    }

    /// <summary>A baked person of the crowd (the fields, as its <c>crowd.json</c> names them).</summary>
    static CrowdPerson B(string name, string sex, string role, string outfit, float age = 30f, string? hair = "crop", bool covered = false) =>
        P(name, sex, role, age, hair, covered) with
        {
            Folder = name, Outfit = outfit, HipHeight = sex == "male" ? 0.85f : 0.79f, BaseHipHeight = sex == "male" ? 0.9023f : 0.8119f,
            Files = new CrowdFiles(name + ".glb", $"{name}_{outfit}.glb", hair is null ? null : name + "_hair.glb", null, name + ".json"),
        };

    /// <summary>The models repository's crowd as it was baked, in its order and roles (person_00 … person_10).</summary>
    static readonly CrowdPerson[] BakedCrowd =
    [
        B("person_00", "male", "guard", "guard", 30, "ear"), B("person_01", "female", "townswoman", "townswoman", 33, null, true),
        B("person_02", "male", "guard", "guard", 42, "ear"), B("person_03", "female", "townswoman", "townswoman", 53, null, true),
        B("person_04", "male", "field_hand", "townsman", 22), B("person_05", "female", "craftswoman", "maiden", 27, "braids"),
        B("person_06", "male", "field_hand", "townsman", 38), B("person_07", "female", "townswoman", "townswoman", 49, null, true),
        B("person_08", "male", "cleric", "monk", 62, "monk"), B("person_09", "male", "cleric", "priest", 20, "priest"),
        B("person_10", "female", "nun", "nun", 42, null, true),
    ];

    /// <summary>The crowd — the field hands hooded, four more laymen (a merchant, two craftsmen, one of them old, a labourer).</summary>
    static readonly CrowdPerson[] BakedCrowd40 =
    [
        .. BakedCrowd.Select(p => p.Role == "field_hand" ? p with { Hooded = true, Files = p.Files! with { Hood = p.Name + "_hood.glb" } } : p),
        B("person_11", "male", "merchant", "townsman", 39), B("person_12", "male", "craftsman", "townsman", 26),
        B("person_13", "male", "labourer", "townsman", 43), B("person_14", "male", "craftsman", "townsman", 67),
    ];

    /// <summary>
    /// The laymen drawn from the four added — the merchants the merchant, the townsman a craftsman or the labourer, the drunk the
    /// labourer, the old man the eldest layman (the old craftsman), the beggar a field hand (hooded: his hood worn, over his tunic) — the same
    /// for the same list, the men different while they last; the crowd before it draws as before (the test above).
    /// </summary>
    [Fact]
    public void Round_40_s_laymen_are_drawn_the_merchant_a_merchant_and_the_beggar_a_hooded_field_hand()
    {
        foreach (var (night, people) in new[] { (true, 6), (false, 8), (true, 12), (false, 16) })
        {
            var slots = TownsfolkBodies.Slots(night, people);
            var given = TownsfolkBodies.Assign(slots, BakedCrowd40, baked: true);
            Assert.Equal(given, TownsfolkBodies.Assign(slots, BakedCrowd40, baked: true));
            for (int i = 0; i < slots.Length; i++)
            {
                var p = given[i]!;
                if (night && people == 6 || !night && people == 8) output.WriteLine($"{(night ? "night" : "day")}: {slots[i]} -> {p.Name} ({p.Role}{(p.Hooded ? ", hooded" : "")})");
                Assert.DoesNotContain(p.Role, new[] { "guard", "nun" });
                if (TownsfolkBodies.Specs[slots[i]].Sex == "male" && slots[i] is not ("Monk" or "Civ10monk2"))
                    Assert.Contains(p.Role, new[] { "field_hand", "merchant", "craftsman", "labourer" });
            }
            string First(string template) => given[Array.IndexOf(slots, template)]!.Name;
            // The first of the merchants has the crowd's merchant (in a long list a second old man, choosing first, may take him: the eldest left).
            int merchant = Array.FindIndex(slots, t => t is "Merchant" or "Civ03merchant1");
            if (merchant >= 0 && slots.Count(t => t == "OldMan") < 2) Assert.Equal("person_11", given[merchant]!.Name);
            if (slots.Contains("Civ07drunk")) Assert.Equal("person_13", First("Civ07drunk"));
            if (slots.Contains("OldMan")) Assert.Equal("person_14", First("OldMan"));
            if (slots.Contains("Civ02beggar"))
            {
                var beggar = given[Array.IndexOf(slots, "Civ02beggar")]!;
                Assert.True(beggar.Hooded);
                Assert.Contains($"{beggar.Name}_hood", TownsfolkBodies.BakedRecipeOf("Civ02beggar", beggar).Wear);
            }
            // The men differ while they last (six laymen and two clerics).
            var men = Enumerable.Range(0, slots.Length).Where(i => given[i]!.Sex == "male").Select(i => given[i]!.Name).ToList();
            Assert.Equal(Math.Min(men.Count, 8), men.Distinct().Count());
        }
    }

    [Fact]
    public void Baked_each_townsperson_is_a_baked_person_of_its_sex_and_role_the_same_for_the_same_list_never_a_guard_or_the_nun()
    {
        foreach (var (night, people) in new[] { (true, 6), (false, 8), (true, 12), (false, 16) })
        {
            var slots = TownsfolkBodies.Slots(night, people);
            var given = TownsfolkBodies.Assign(slots, BakedCrowd, baked: true);
            Assert.Equal(given, TownsfolkBodies.Assign(slots, BakedCrowd, baked: true));   // the same list, the same people
            for (int i = 0; i < slots.Length; i++)
            {
                var spec = TownsfolkBodies.Specs[slots[i]];
                var p = Assert.IsType<CrowdPerson>(given[i]);
                if (night && people == 6) output.WriteLine($"{slots[i]} -> {p.Name} ({p.Sex} {p.Role}) baked in {p.Outfit}");
                Assert.Equal(spec.Sex, p.Sex);
                Assert.True(p.IsBaked);
                Assert.DoesNotContain(p.Role, new[] { "guard", "nun" });
                string expected = slots[i] switch
                {
                    "Monk" or "Civ10monk2" => "cleric",
                    "YoungLady" when Array.IndexOf(slots, "YoungLady") == i => "craftswoman",   // the first; a later one spills (below)
                    "YoungLady" => p.Role,
                    "Civ06Milkmaid" or "Civ11female" => "townswoman",
                    _ => "field_hand",   // the merchants, the townsman, the old man, the drunk, the beggar
                };
                Assert.Equal(expected, p.Role);
                if (slots[i] == "YoungLady") Assert.True(p.Role is "craftswoman" or "townswoman", p.Role);
                var recipe = TownsfolkBodies.BakedRecipeOf(slots[i], p);
                Assert.Equal((p.Name, $"assets/characters/crowd/{p.Name}", $"{p.Name} as {slots[i]}"), (recipe.Base, recipe.Folder, recipe.Name));
                Assert.Null(recipe.Look);
                Assert.DoesNotContain("hood_male", recipe.Wear);   // nothing over a baked person's own outfit
            }
            // The women differ while they last (the three townswomen, and the craftswoman when a young lady is about).
            var women = Enumerable.Range(0, slots.Length).Where(i => given[i]!.Sex == "female").Select(i => given[i]!.Name).ToList();
            Assert.Equal(Math.Min(women.Count, slots.Contains("YoungLady") ? 4 : 3), women.Distinct().Count());
        }
        // The old man is the eldest field hand (person_06, 38), the young lady the craftswoman; the style draws them at their own size.
        var day = TownsfolkBodies.Slots(false, 8);
        var d = TownsfolkBodies.Assign(day, BakedCrowd, baked: true);
        for (int i = 0; i < day.Length; i++)
        {
            if (day[i] == "OldMan") Assert.Equal("person_06", d[i]!.Name);
        }
        // The first young lady is the craftswoman; the second, the craftswoman given, a townswoman not yet given (the pool spills).
        var ladies = Enumerable.Range(0, day.Length).Where(i => day[i] == "YoungLady").Select(i => d[i]!).ToList();
        Assert.Equal(2, ladies.Count);
        Assert.Equal("person_05", ladies[0].Name);
        Assert.Equal("townswoman", ladies[1].Role);
        Assert.Equal(day.Count(t => t is "YoungLady" or "Civ06Milkmaid" or "Civ11female"), Enumerable.Range(0, day.Length)
            .Where(i => day[i] is "YoungLady" or "Civ06Milkmaid" or "Civ11female").Select(i => d[i]!.Name).Distinct().Count());
        var style = TownsfolkBodies.BakedStyleOf("OldMan");
        Assert.Equal(("walk_old", 1f, TownsfolkBodies.FidgetAfterSeconds), (style.Walk, style.Scale, style.FidgetAfter));
        // Not baked, the same crowd is drawn as before (its pools: the guards laymen).
        var nBuilt = TownsfolkBodies.Assign(TownsfolkBodies.Slots(true, 6), BakedCrowd);
        Assert.Contains(nBuilt, p => p!.Role == "guard");

        // --crowd-built and the setting's word for it; models and oneone as before.
        Assert.True(TownsfolkBodies.ChooseBuilt(true, null));
        Assert.True(TownsfolkBodies.ChooseBuilt(false, "models-built"));
        Assert.False(TownsfolkBodies.ChooseBuilt(false, "models"));
        Assert.Equal(TownsfolkBodies.Models, TownsfolkBodies.Choose(false, "models-built"));
    }

    [Fact]
    public void A_crowd_person_wears_its_role_s_outfit_and_the_switch_and_the_setting_put_the_townsfolk_back_on_1_1_s_bodies()
    {
        Assert.Equal("monk_male", TownsfolkBodies.OutfitOf(Crowd[4]));
        Assert.Equal("priest_male", TownsfolkBodies.OutfitOf(Crowd[5]));
        Assert.Equal("townswoman_female", TownsfolkBodies.OutfitOf(Crowd[6]));
        Assert.Equal("maiden_female", TownsfolkBodies.OutfitOf(Crowd[7]));
        Assert.Equal("nun_female", TownsfolkBodies.OutfitOf(Crowd[9]));
        Assert.Equal("townsman_male", TownsfolkBodies.OutfitOf(Crowd[0]));
        var oldMan = TownsfolkBodies.RecipeOf("OldMan", Crowd[2]);
        Assert.Equal(["townsman_male", "hood_male", "hair_crop_male"], oldMan.Wear);
        Assert.NotNull(oldMan.Look);

        Assert.Equal(TownsfolkBodies.Models, TownsfolkBodies.Choose(false, null));
        Assert.Equal(TownsfolkBodies.Models, TownsfolkBodies.Choose(false, "models"));
        Assert.Equal(TownsfolkBodies.Models, TownsfolkBodies.Choose(false, "nonsense"));
        Assert.Null(TownsfolkBodies.Choose(true, "models"));          // --townsfolk-oneone
        Assert.Null(TownsfolkBodies.Choose(false, "OneOne"));         // the setting townsfolkBodies
        Assert.Equal("models", new PlatformSettings().TownsfolkBodies);
        string dir = Path.Combine(Path.GetTempPath(), "vitomir-townsfolk-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new PlatformSettings { TownsfolkBodies = "oneone", CharacterSkinNear = 20f, CharacterSkinFarEvery = 3 }.Save(path);
            var back = PlatformSettings.Load(path);
            Assert.Equal(("oneone", 20f, 3), (back.TownsfolkBodies, back.CharacterSkinNear, back.CharacterSkinFarEvery));
            var skinning = back.PeopleSkinningFor(everyFrame: false);
            Assert.Equal((true, 20f, 30f, 2, 3), (skinning.Cull, skinning.NearMetres, skinning.FarMetres, skinning.MidEvery, skinning.FarEvery));
            Assert.Same(Render.Characters.PeopleSkinning.EveryFrame, back.PeopleSkinningFor(everyFrame: true));   // --skin-every-frame
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void The_package_carries_every_file_a_townsperson_of_the_crowd_can_wear()
    {
        var models = Environment.GetEnvironmentVariable(PeopleBodies.EnvironmentVariable);
        Assert.SkipUnless(models is not null && File.Exists(Assets.People.Crowd.PathIn(models)), "the models repository's crowd is not here (VITOMIR_MODELS_DIR)");
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "tools", "platform", "people-files.txt"))) dir = Path.GetDirectoryName(dir);
        Assert.SkipUnless(dir is not null, "the repository's tools/platform/people-files.txt is not above the tests");
        var listed = File.ReadAllLines(Path.Combine(dir!, "tools", "platform", "people-files.txt"))
            .Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Trim()).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("assets/characters/crowd/crowd.json", listed);
        var crowd = Assets.People.Crowd.Read(File.ReadAllText(Assets.People.Crowd.PathIn(models!)));
        var library = PeopleLibrary.TryOpen(models!, out _, decodeImages: false)!;
        foreach (var (template, spec) in TownsfolkBodies.Specs)
            foreach (var person in TownsfolkBodies.Pool(spec, crowd))
            {
                var recipe = TownsfolkBodies.RecipeOf(template, person);
                foreach (var stem in recipe.Wear)
                {
                    string path = Path.GetRelativePath(models!, library.FindWornPath(stem) ?? Path.Combine(models!, "missing", stem + ".glb")).Replace('\\', '/');
                    foreach (var file in new[] { path, Path.ChangeExtension(path, ".json"), path[..^4] + "_fit.npz" })
                        Assert.True(listed.Contains(file), $"{template} as {person.Name} wears {stem}: {file} is not in people-files.txt");
                }
                Assert.Contains($"assets/characters/{recipe.Base}/{recipe.Base}.glb", listed);
            }
        // Each baked person a townsperson can be: its folder's files, and the files its own were fitted from.
        foreach (var (template, spec) in TownsfolkBodies.Specs)
            foreach (var person in TownsfolkBodies.Pool(spec, crowd, baked: true).Where(p => p.IsBaked))
            {
                var recipe = TownsfolkBodies.BakedRecipeOf(template, person);
                foreach (var file in new[] { person.Files!.Body, person.Files.Record }.Concat(person.Files.Worn()).OfType<string>())
                    Assert.True(listed.Contains($"{recipe.Folder}/{file}"), $"{template} as {person.Name}: {recipe.Folder}/{file} is not in people-files.txt");
                // The maps its files refer to beside them (../shared/<file>).
                foreach (var file in new[] { person.Files.Body }.Concat(person.Files.Worn()))
                {
                    var glb = Vitomir.Formats.Gltf.GltfFile.Load(Path.Combine(models!, recipe.Folder!, file));
                    for (int i = 0; i < glb.Images.Count; i++)
                        if (glb.ImageFile(i) is { } image)
                        {
                            string rel = Path.GetRelativePath(models!, image).Replace('\\', '/');
                            Assert.True(listed.Contains(rel), $"{template} as {person.Name}: {file} refers to {rel}, which is not in people-files.txt");
                        }
                }
                foreach (var from in recipe.FittedFrom!.Values)
                {
                    string path = Path.GetRelativePath(models!, library.FindWornPath(from) ?? Path.Combine(models!, "missing", from + ".glb")).Replace('\\', '/');
                    foreach (var file in new[] { Path.ChangeExtension(path, ".json"), path[..^4] + "_fit.npz" })
                        Assert.True(listed.Contains(file), $"{template} as {person.Name} is fitted from {from}: {file} is not in people-files.txt");
                }
            }
        // Not the guards' folders: they fight, on 1.1's bodies.
        Assert.DoesNotContain(listed, l => l.StartsWith("assets/characters/crowd/person_00/", StringComparison.Ordinal) || l.StartsWith("assets/characters/crowd/person_02/", StringComparison.Ordinal));
    }

    static (string Course, string Game, string Models) Needs()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        var models = Environment.GetEnvironmentVariable(PeopleBodies.EnvironmentVariable);
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Assert.SkipUnless(models is not null && Directory.Exists(Path.Combine(models, "assets", "characters", "crowd")), "the models repository's crowd is not here (VITOMIR_MODELS_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-townsfolk-tests-userdata"));
        return (course!, game!, models!);
    }

    static SessionOptions Night(string course, string game, string models, List<string> log, string? townsfolk = TownsfolkBodies.Models) => new()
    {
        Level = course, Game = game, Switches = new HashSet<string>(["sandbox"], StringComparer.Ordinal), Seed = 3, People = 6,
        TownsfolkBody = townsfolk, ModelsFolder = models, Say = log.Add,
    };

    /// <summary>Each character but the player: its shown name and its kind's name.</summary>
    static List<(string Name, string Kind)> Kinds(Session session) =>
        [.. session.PeoplePlaces().Select(p => (p.Name, p.Kind))];

    static Tape Record(SessionOptions options, int seconds)
    {
        using var session = Session.Create(options);
        var tape = new Tape { Header = session.TapeHeader("test") };
        for (int t = 0; t < seconds * World.TicksPerSecond; t++)
        {
            var frame = new ActionFrame(t < 60 ? new Vector2(0f, 1f) : Vector2.Zero, Vector2.Zero, Buttons.None, new Vector2(0.5f, 0.5f), 16f / 9f);
            session.Step(frame);
            tape.Frames.Add(frame);
            if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
        }
        return tape;
    }

    [Fact]
    public void In_a_session_the_townsfolk_are_the_crowd_the_keepers_1_1_s_and_a_replay_draws_the_same_people_digest_for_digest()
    {
        var (course, game, models) = Needs();
        var log = new List<string>();
        var options = Night(course, game, models, log);
        List<(string Name, string Kind)> drawn;
        using (var session = Session.Create(options))
        {
            Assert.True(session.TownsfolkOnCrowd);
            drawn = Kinds(session);
            foreach (var (name, kind) in drawn) output.WriteLine($"{name}: {kind}");
            foreach (var line in log.Where(l => l.StartsWith("people:", StringComparison.Ordinal))) output.WriteLine(line);
            var townsfolk = drawn.Where(d => d.Name is not ("Guard" or "Night Watchman")).ToList();
            Assert.Equal(6, townsfolk.Count);
            Assert.All(townsfolk, d => Assert.StartsWith("people:person_", d.Kind));
            Assert.Contains(drawn, d => d.Name == "Guard" && d.Kind == "rudolfGuard");                       // the keepers fight: 1.1's
            Assert.Contains(drawn, d => d.Name == "Night Watchman" && d.Kind == "KnightOfStJohn_torch");
            // Each on a body of the models repository's (a person's motions), the keepers not.
            var assets = session.World.Service<CharacterAssets>();
            foreach (var e in session.World.Store<CharacterAnimation>().Entities)
            {
                var kind = assets.Kind(session.World.Get<CharacterAnimation>(e).Kind);
                Assert.Equal(kind.Name.StartsWith("people:", StringComparison.Ordinal), kind.Person is not null);
            }
            // The merchant is protected as 1.1's kind of him was (the data protects kinds by name).
            int merchants = 0;
            foreach (var e in session.World.Store<CharacterAnimation>().Entities)
                if (session.NameOf(e.Packed) == "Merchant") { Assert.True(ProtectedPeople.Is(session.World, e)); merchants++; }
            Assert.Equal(1, merchants);
            // Each drawn as its baked person (its own body, from its folder), noted so.
            foreach (var e in session.World.Store<CharacterAnimation>().Entities)
            {
                var kind = assets.Kind(session.World.Get<CharacterAnimation>(e).Kind);
                if (kind.Person is null) continue;
                if (e == session.Player) continue;
                Assert.Contains(" in assets/characters/crowd/person_", assets.Model(kind.Model).Name);
                Assert.Equal(1f, kind.Scale);
            }
            Assert.Contains(log, l => l.Contains("6 as baked", StringComparison.Ordinal));
            Assert.Equal(TownsfolkBodies.Baked, session.TapeHeader("test").Notes[TownsfolkBodies.TapeNote]);
        }

        var tape = Record(options, seconds: 3);
        var again = new SessionOptions { Level = course, Game = game, ModelsFolder = models, Say = _ => { } }.From(tape.Header);
        Assert.Equal((TownsfolkBodies.Models, true, false), (again.TownsfolkBody, again.TownsfolkRequired, again.CrowdBuilt));
        using (var replay = Session.Create(again))
        {
            Assert.Equal(drawn, Kinds(replay));   // the same person for each
            int compared = 0;
            foreach (var frame in tape.Frames)
            {
                replay.Step(frame);
                if (tape.Digests.TryGetValue(replay.Tick, out ulong want)) { Assert.Equal(want, replay.Digest()); compared++; }
            }
            Assert.True(compared >= 5);
        }

        // The earlier build's tape (no note): 1.1's townsfolk; and a tape with the note that cannot be built here is refused.
        var notes = new Dictionary<string, string>(tape.Header.Notes);
        notes.Remove(TownsfolkBodies.TapeNote);
        var fourteenCl = new SessionOptions { Level = course, Game = game, ModelsFolder = models, Say = _ => { } }.From(tape.Header with { Notes = notes });
        Assert.Null(fourteenCl.TownsfolkBody);
        string empty = Path.Combine(Path.GetTempPath(), "vitomir-no-crowd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            var x = Assert.Throws<SessionException>(() => Session.Create(again with { ModelsFolder = empty }));
            Assert.Contains("cannot be built here", x.Message);
        }
        finally { Directory.Delete(empty, recursive: true); }
    }

    [Fact]
    public void A_baked_person_whose_folder_is_missing_is_built_at_run_time_said_once_and_the_rest_are_baked()
    {
        var (_, _, models) = Needs();
        var library = PeopleLibrary.TryOpen(models, out _, decodeImages: false)!;
        var crowd = library.Crowd();
        Assert.SkipUnless(crowd.Any(p => p.IsBaked), "the models repository's crowd is not baked here (too old)");
        var slots = TownsfolkBodies.Slots(night: true, 6);
        var given = TownsfolkBodies.Assign(slots, crowd, baked: true);
        // The first townswoman's folder gone (an older or partial repository).
        string gone = given.First(p => p is { Sex: "female" })!.Name;
        var partial = crowd.Select(p => p.Name == gone ? p with { Folder = p.Folder + "_not_here" } : p).ToList();
        var assets = new CharacterAssets();
        var built = TownsfolkBodies.Build(slots, library, models, assets, new Dictionary<string, TextureImage>(), decodeImages: false, crowdGiven: partial);
        foreach (var line in built.Lines) output.WriteLine(line);
        Assert.Null(built.Why);
        Assert.Single(built.Lines, l => l.Contains("is not in the models folder", StringComparison.Ordinal));   // said once, though two townswomen may be her
        Assert.Equal(TownsfolkBodies.Baked, built.Note);
        int runTime = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            var model = assets.ModelOf(built.Kinds[i]);
            bool baked = model.Name.Contains(" in assets/characters/crowd/person_", StringComparison.Ordinal);
            Assert.Equal(given[i]!.Name != gone, baked);
            if (!baked)
            {
                Assert.StartsWith("people:base_female+", model.Name);   // her base in the earlier build's way: her role's outfit, her look
                Assert.NotEqual(1f, assets.Kind(built.Kinds[i]).Scale);
                runTime++;
            }
        }
        Assert.Equal(slots.Length - runTime, built.BakedPlaces);
        Assert.True(runTime > 0);

        // --crowd-built: every person built at run time, as before assigned and built them; the note the earlier build's.
        var allBuilt = TownsfolkBodies.Build(slots, library, models, new CharacterAssets(), new Dictionary<string, TextureImage>(), decodeImages: false, crowdBuilt: true);
        Assert.Equal((0, TownsfolkBodies.Models), (allBuilt.BakedPlaces, allBuilt.Note));
        Assert.Contains(allBuilt.Lines, l => l.Contains("--crowd-built", StringComparison.Ordinal));
    }

    /// <summary>
    /// (A run of the earlier tests on the files, which that build could not read: every baked person fell back to the
    /// run-time build): the people were given as the baked crowd gives them, but the tape was noted the earlier build's (<c>models</c>), so its replay
    /// gave the places the earlier build's people — the merchant another man, his walk speed another, a digest apart at tick 0 — which
    /// <c>PeopleSwingTests</c>' replay showed as if the swing's doing. The note now says how the people were given, and a replay gives the
    /// same people however many of them could be drawn baked.
    /// </summary>
    [Fact]
    public void A_crowd_whose_every_baked_person_falls_back_is_noted_baked_and_its_replay_gives_the_same_people()
    {
        var (course, game, models) = Needs();
        var library = PeopleLibrary.TryOpen(models, out _, decodeImages: false)!;
        var crowd = library.Crowd();
        Assert.SkipUnless(crowd.Any(p => p.IsBaked), "the models repository's crowd is not baked here (too old)");
        var slots = TownsfolkBodies.Slots(night: true, 6);
        // Every baked folder gone (as when a person's files cannot be read): each place built at run time.
        var none = crowd.Select(p => p.IsBaked ? p with { Folder = p.Folder + "_not_here" } : p).ToList();
        var first = new CharacterAssets();
        var built = TownsfolkBodies.Build(slots, library, models, first, new Dictionary<string, TextureImage>(), decodeImages: false, crowdGiven: none);
        Assert.Null(built.Why);
        Assert.Equal(0, built.BakedPlaces);
        Assert.True(built.AssignedBaked);
        Assert.Equal(TownsfolkBodies.Baked, built.Note);   // models, and the replay was the earlier build's crowd
        var header = new TapeHeader { Level = course, Seed = 5, Notes = { [TownsfolkBodies.TapeNote] = built.Note } };
        var again = new SessionOptions { Level = course, Game = game, ModelsFolder = models, Say = _ => { } }.From(header);
        Assert.False(again.CrowdBuilt);
        var assets = new CharacterAssets();
        var replayed = TownsfolkBodies.Build(slots, library, models, assets, new Dictionary<string, TextureImage>(), decodeImages: false, crowdBuilt: again.CrowdBuilt, crowdGiven: none);
        for (int i = 0; i < slots.Length; i++)
        {
            output.WriteLine($"{slots[i]}: {first.Kind(built.Kinds[i]).Name} / {assets.Kind(replayed.Kinds[i]).Name}");
            Assert.Equal(first.Kind(built.Kinds[i]).Name, assets.Kind(replayed.Kinds[i]).Name);   // the same person in each place
        }
        // Where it shows (these files): the baked crowd's people and the earlier build's differ in some place.
        var given = TownsfolkBodies.Assign(slots, crowd, baked: true);
        var fourteenCm = TownsfolkBodies.Assign(slots, crowd, baked: false);
        output.WriteLine($"the people given as baked and as built differ in {Enumerable.Range(0, slots.Length).Count(i => given[i]?.Name != fourteenCm[i]?.Name)} of {slots.Length} places");
    }

    [Fact]
    public void With_crowd_built_the_session_draws_the_run_time_crowd_and_its_tape_replays_so()
    {
        var (course, game, models) = Needs();
        var log = new List<string>();
        var options = Night(course, game, models, log) with { CrowdBuilt = true };
        using var session = Session.Create(options);
        Assert.True(session.TownsfolkOnCrowd);
        var assets = session.World.Service<CharacterAssets>();
        foreach (var e in session.World.Store<CharacterAnimation>().Entities)
        {
            var kind = assets.Kind(session.World.Get<CharacterAnimation>(e).Kind);
            if (e == session.Player || kind.Person is null) continue;
            Assert.StartsWith("people:base_", assets.Model(kind.Model).Name);   // on the bases, tinted and scaled
        }
        var header = session.TapeHeader("test");
        Assert.Equal(TownsfolkBodies.Models, header.Notes[TownsfolkBodies.TapeNote]);
        var again = new SessionOptions { Level = course, Game = game, ModelsFolder = models, Say = _ => { } }.From(header);
        Assert.Equal((TownsfolkBodies.Models, true, true), (again.TownsfolkBody, again.TownsfolkRequired, again.CrowdBuilt));
        Assert.NotEqual(SessionContent.KeyOf(again), SessionContent.KeyOf(again with { CrowdBuilt = false }));   // not the baked crowd's content
    }

    [Fact]
    public void With_the_townsfolk_on_1_1_s_bodies_the_session_is_as_before_the_crowd()
    {
        var (course, game, models) = Needs();
        var log = new List<string>();
        using var session = Session.Create(Night(course, game, models, log, townsfolk: null));
        Assert.False(session.TownsfolkOnCrowd);
        Assert.DoesNotContain(Kinds(session), d => d.Kind.StartsWith("people:", StringComparison.Ordinal));
        Assert.Contains(Kinds(session), d => d.Name == "Townswoman" && d.Kind == "Civ11female");
        Assert.False(session.TapeHeader("test").Notes.ContainsKey(TownsfolkBodies.TapeNote));
        Assert.DoesNotContain(log, l => l.Contains("models repository's crowd", StringComparison.Ordinal));
        // The crowd asked for with a folder that has no crowd: 1.1's, said in one line.
        string empty = Path.Combine(Path.GetTempPath(), "vitomir-no-crowd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            log.Clear();
            using var fallback = Session.Create(Night(course, game, empty, log));
            Assert.False(fallback.TownsfolkOnCrowd);
            Assert.Single(log, l => l.StartsWith("people: the townsfolk are 1.1's: ", StringComparison.Ordinal));
        }
        finally { Directory.Delete(empty, recursive: true); }
    }
}
