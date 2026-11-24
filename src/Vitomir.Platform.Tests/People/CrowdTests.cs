// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.People;

namespace Vitomir.Platform.Tests.People;

/// <summary>
/// The models repository's crowd (<c>assets/characters/crowd/crowd.json</c>) as people the platform builds: the file read (and a
/// text of it, without the files); a look's tint; a crowd person built from its settings on its base — its role's outfit, its own hair
/// and beard, its hair's colour and its greying lock by lock, its skin's tint — sharing the plain build's mesh; its maps capped as they
/// are decoded. The tests that read the files skip without <c>VITOMIR_MODELS_DIR</c>.
/// </summary>
public sealed class CrowdTests(ITestOutputHelper output)
{
    const string Two = """
        [
          {"name": "person_a", "sex": "male", "ok": true, "role": "cleric", "kind": "living",
           "groom": {"hair": "monk", "beard": null, "grey": 0.5, "covered": false},
           "age": 60, "height_m": 1.66, "eye": "blue",
           "skin": {"melanin": 0.04, "sun": 0.6, "hair": [0.1, 0.07, 0.04], "grey": 0.5, "vampire": 0.0}},
          {"name": "person_b", "sex": "female", "ok": false, "role": "nun"},
          {"name": "person_c", "sex": "female", "role": "townswoman", "kind": "kindred",
           "groom": {"hair": null, "beard": null, "grey": 0.0, "covered": true},
           "height_m": 1.55, "skin": {"vampire": 0.6, "deathly": 0.5}}
        ]
        """;

    [Fact]
    public void A_crowd_file_reads_its_people_leaving_out_those_not_ok()
    {
        var people = Crowd.Read(Two);
        Assert.Equal(["person_a", "person_c"], people.Select(p => p.Name));
        var monk = people[0];
        Assert.Equal(("male", "cleric", "living", "monk", (string?)null, false), (monk.Sex, monk.Role, monk.Nature, monk.Hair, monk.Beard, monk.Covered));
        Assert.Equal(["hair_monk_male"], monk.Groom());
        Assert.Equal(new Vector3(0.1f, 0.07f, 0.04f), monk.Skin.HairColour);
        Assert.Equal(0.5f, monk.Skin.Grey);
        Assert.Equal(1.66f, monk.HeightMetres);
        var woman = people[1];
        Assert.True(woman.Covered);
        Assert.Empty(woman.Groom());
        Assert.Equal(0.6f, woman.Skin.Vampire);
        var recipe = monk.Recipe(["monk_male"], "the monk");
        Assert.Equal(("the monk", "male", "base_male"), (recipe.Name, recipe.Sex, recipe.Base));
        Assert.Equal(["monk_male", "hair_monk_male"], recipe.Wear);
        Assert.Equal(monk.Look, recipe.Look);
        Assert.NotEqual(recipe with { Look = null }, recipe);
        Assert.Contains("grey 0.5", recipe.Key);   // the look is part of the key
        Assert.Throws<InvalidDataException>(() => Crowd.Read("{}"));
    }

    [Fact]
    public void A_look_darkens_the_skin_with_melanin_and_sun_and_pales_the_undead()
    {
        CrowdPerson Person(float melanin, float sun, float vampire) =>
            new("p", "male", "field_hand", "living", null, null, false, 30f, 1.7f, "", new CrowdSkin(melanin, sun, Vector3.One * 0.1f, 0f, vampire, 0.5f));
        var asBase = PersonLook.Of(Person(PersonLook.BaseMelanin, PersonLook.BaseSun, 0f)).SkinTint;
        Assert.Equal(Vector3.One, asBase);   // the base's own skin: no tint
        var darker = PersonLook.Of(Person(0.05f, 0.9f, 0f)).SkinTint;
        Assert.True(darker.X < 1f && darker.Z < darker.Y && darker.Y < darker.X, $"{darker}");   // darker, the blue the most
        var pale = PersonLook.Of(Person(0.05f, 0.9f, 0.7f)).SkinTint;
        Assert.True(MathF.Abs(pale.X - pale.Z) < MathF.Abs(darker.X - darker.Z), $"{pale}: greyer than {darker}");
        Assert.True(pale.Z > darker.Z, $"{pale}: paler than {darker}");
        output.WriteLine($"base {asBase}, darker {darker}, undead {pale}");
    }

    /// <summary>
    /// A library of the crowd's own (no images): not <see cref="PeopleLibraryTests"/>' shared one, whose images are named after the first
    /// file that holds them (its tests expect the player's names, and the crowd shares his hair's maps).
    /// </summary>
    static readonly Lazy<PeopleLibrary?> library = new(() =>
        Environment.GetEnvironmentVariable(PeopleLibrary.FolderVariable) is { Length: > 0 } dir ? PeopleLibrary.TryOpen(dir, out _, decodeImages: false) : null);

    internal static PeopleLibrary Lib()
    {
        Assert.SkipUnless(library.Value is not null, "the models repository's files are not here (VITOMIR_MODELS_DIR)");
        return library.Value!;
    }

    [Fact]
    public void The_models_streams_crowd_is_read_from_its_files()
    {
        var crowd = Lib().Crowd();
        Assert.NotEmpty(crowd);
        Assert.Same(crowd, Lib().Crowd());   // read once
        foreach (var p in crowd)
        {
            output.WriteLine($"{p.Name}: {p.Sex} {p.Role} {p.Nature}, {p.HeightMetres:0.000} m, hair {p.Hair ?? "-"}{(p.Covered ? " (covered)" : "")}, beard {p.Beard ?? "-"}, grey {p.Skin.Grey:0.00}");
            Assert.True(p.Sex is "male" or "female");
            Assert.InRange(p.HeightMetres, 1.3f, 2.0f);
            foreach (var worn in p.Groom()) Assert.True(Lib().FindWorn(worn) is not null, $"{p.Name}'s {worn} is in the files");
        }
    }

    [Fact]
    public void A_crowd_person_builds_on_its_base_in_its_clothes_with_its_hair_greying_lock_by_lock_and_shares_the_plain_mesh()
    {
        // The crowd's men with a beard and some grey (person_02), else any man with hair.
        var crowd = Lib().Crowd();
        var man = crowd.FirstOrDefault(p => p.Sex == "male" && p.Beard is not null && p.Skin.Grey > 0.05f && p.Hair is not null)
                  ?? crowd.First(p => p.Sex == "male" && p.Hair is not null);
        var recipe = man.Recipe(["townsman_male"]);
        var person = Lib().Person(recipe);
        var plain = Lib().Person(recipe with { Look = null });
        var model = person.Model;
        output.WriteLine($"{man.Name}: {recipe.Key}; {model.Vertices.Length:N0} vertices, {model.Parts.Length} parts, base {person.BaseHeight} m");
        Assert.Same(person, Lib().Person(recipe));                      // built once
        Assert.Same(plain.Model.Vertices, model.Vertices);              // the mesh shared with the plain build
        Assert.Same(plain.Model.Indices, model.Indices);
        Assert.NotSame(plain.Model, model);
        Assert.Equal(person.Recipe, recipe);
        Assert.True(person.BaseHeight is > 1.5f and < 1.9f, $"{person.BaseHeight}");

        var look = recipe.Look!;
        var hair = PersonLooks.HairFactor(look);
        var hairParts = model.Parts.Where(p => p.Material is { } m && PersonLooks.IsHair(m)).ToList();
        Assert.Equal(man.Beard is null ? 1 : 2, hairParts.Count);
        foreach (var part in hairParts) Assert.Equal(new Vector4(hair, 1f), part.Material!.BaseColorFactor);
        var skin = Assert.Single(model.Parts, p => p.Material is { } m && PersonLooks.IsSkin(m));
        var plainSkin = Assert.Single(plain.Model.Parts, p => p.Material is { } m && PersonLooks.IsSkin(m));
        Assert.Equal(plainSkin.Material!.BaseColorFactor.X * look.SkinTint.X, skin.Material!.BaseColorFactor.X, 5);
        // Every other part as the files have it.
        for (int p = 0; p < model.Parts.Length; p++)
            if (model.Parts[p].Material is { } m && !PersonLooks.IsHair(m) && !PersonLooks.IsSkin(m)) Assert.Same(plain.Model.Parts[p], model.Parts[p]);

        // The greying: every hair vertex knows its place in the order; about the share of them grey, each darker lock carried down to the hair's colour.
        var order = person.HairGrey!;
        Assert.Equal(model.Vertices.Length, order.Length);
        int hairVertices = order.Count(o => !float.IsNaN(o));
        Assert.True(hairVertices > 1000, $"{hairVertices} hair vertices");
        foreach (var part in hairParts)
            for (int i = part.FirstIndex; i < part.FirstIndex + part.IndexCount; i++) Assert.False(float.IsNaN(order[model.Indices[i]]), "a hair part's vertex with no order");
        if (look.Grey > 0f)
        {
            int grey = 0;
            var light = PersonLooks.LockColour(look, float.NegativeInfinity);
            var dark = PersonLooks.LockColour(look, float.PositiveInfinity);
            for (int i = 0; i < order.Length; i++)
            {
                if (float.IsNaN(order[i])) { Assert.Equal(plain.Model.Colors![i], model.Colors![i]); continue; }
                bool isGrey = order[i] < look.Grey;
                var k = isGrey ? light : dark;
                if (isGrey) grey++;
                Assert.Equal(plain.Model.Colors![i].X * k.X, model.Colors![i].X, 5);
            }
            float share = grey / (float)hairVertices;
            output.WriteLine($"grey {look.Grey:0.000}: {grey:N0} of {hairVertices:N0} hair vertices ({share:0.000})");
            Assert.InRange(share, look.Grey * 0.5f, look.Grey * 1.5f + 0.02f);
            Assert.True(light.X > dark.X && light.X <= 1f && dark.X <= 1f);
        }
        else Assert.Same(plain.Model.Colors, model.Colors);
    }

    [Fact]
    public void Maps_over_the_cap_are_halved_as_they_are_decoded_and_the_same_pixels_as_the_renderers_halving()
    {
        var capped = PeopleLibrary.TryOpen(Lib().Folder, out _, decodeImages: true, maxTextureSide: 1024)!;
        var person = capped.Person(new PersonRecipe("cap test", "male", "base_male", ["hair_crop_male"]));
        Assert.All(person.Textures, t => Assert.True(t.Width <= 1024 && t.Height <= 1024, $"{t.Name} {t.Width}x{t.Height}"));
        output.WriteLine($"{person.Textures.Count} maps at most 1024: {capped.TextureBytes / 1048576.0:0} MB");
        // The halving is the platform's own filter (the renderer calls it): a colour in linear light.
        var image = new TextureImage("t", 2, 2, [255, 255, 255, 255, 0, 0, 0, 255, 255, 255, 255, 255, 0, 0, 0, 255]);
        var half = TextureHalving.Fit(image, 1, srgb: true);
        Assert.Equal((1, 1), (half.Width, half.Height));
        Assert.InRange(half.Rgba[0], 186, 189);   // half of white in linear light is sRGB 188, not 128
        Assert.Equal(128, TextureHalving.Fit(image, 1, srgb: false).Rgba[0]);
    }
}
