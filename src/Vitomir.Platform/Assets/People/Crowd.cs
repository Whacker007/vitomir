// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The models repository's crowd (assets/characters/crowd/crowd.json) as people the platform builds; as the people
// The models repository baked.
using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace Vitomir.Platform.Assets.People;

/// <summary>
/// One of the models repository's generated people (<c>assets/characters/crowd/crowd.json</c>):
/// what the platform reads of its settings. <i>"People are made on demand from the bases"</i>: the platform builds one from the base
/// of its sex, the outfit its role wears, its own hair and beard (<see cref="Groom"/>), and its look (<see cref="Look"/>: its hair's
/// colour and grey, its skin's tone, its height).
/// <para>
/// <b>What is not applied (yet)</b>: the body's and the face's sliders (<c>muscle</c>, <c>weight</c>, <c>proportions</c>, <c>age</c>,
/// the 168 local <c>face</c> sliders) are MakeHuman's shape targets, which the models repository does not ship (its README: they are read
/// from MakeHuman's own files at build time); the skin's layers (melanin, blood, sun, grime, scars, the scalp's shadow, a tonsure) are
/// composed by its skin tool from layer maps that are not in its repository either; the eye colours other than the
/// base's own. So every crowd person here has its base's shape, face and skin map, tinted (<see cref="PersonLook.SkinTint"/>), and
/// scaled to its height. Either the models repository bakes its crowd into files, or ships the targets and layers:
/// 4 (people at cook time or at run time), still open.
/// </para>
/// <para>
/// The models repository baked it: a person whose <see cref="Folder"/> is there is drawn as baked
/// (<see cref="BakedRecipe"/>), all of the above applied; the build from the base (<see cref="Recipe"/>, <see cref="Look"/>) stays as the
/// fallback and as before crowd.
/// </para>
/// </summary>
/// <param name="Name">Its name in the crowd (<c>person_00</c> …).</param>
/// <param name="Sex"><c>male</c> or <c>female</c>.</param>
/// <param name="Role">Its role (<c>guard</c>, <c>townswoman</c>, <c>field_hand</c>, <c>craftswoman</c>, <c>cleric</c>, <c>nun</c>).</param>
/// <param name="Nature"><c>living</c>, <c>kindred</c> or <c>cappadocian</c> (the file's <c>kind</c>).</param>
/// <param name="Hair">Its cut (<c>crop</c>, <c>ear</c>, <c>collar</c>, <c>priest</c>, <c>monk</c>, <c>braids</c>), or null (none, or covered).</param>
/// <param name="Beard">Its beard (<c>beard_short</c>, <c>beard_full</c>), or null.</param>
/// <param name="Covered">Its hair is covered (a married woman's head-cloth, a nun's veil).</param>
public sealed record CrowdPerson(string Name, string Sex, string Role, string Nature, string? Hair, string? Beard, bool Covered,
                                 float Age, float HeightMetres, string Eye, CrowdSkin Skin)
{
    /// <summary>The base body it is made on (<c>base_male</c>, <c>base_female</c>).</summary>
    public string Base => "base_" + Sex;

    /// <summary>The worn files of its own grooming (its hair, then its beard), by stem.</summary>
    public IEnumerable<string> Groom()
    {
        if (Hair is { Length: > 0 } hair) yield return $"hair_{hair}_{Sex}";
        if (Beard is { Length: > 0 } beard) yield return $"{beard}_{Sex}";
    }

    /// <summary>
    /// The person as a recipe: its base, <paramref name="outfit"/> and anything else worn over it (<paramref name="over"/>: a cap,
    /// a hood), then its hair and beard, with its look. Named <paramref name="name"/> in logs and kinds (its own name when null).
    /// </summary>
    public PersonRecipe Recipe(IReadOnlyList<string> wear, string? name = null) =>
        new(name ?? Name, Sex, Base, [.. wear, .. Groom()]) { Look = Look };

    /// <summary>Its look: the hair's colour and its share of grey, the skin's tint (<see cref="PersonLook.Of"/>).</summary>
    public PersonLook Look => PersonLook.Of(this);

    // ---- the person baked by the models repository ----
    /// <summary>
    /// Its folder beside <c>crowd.json</c> (<c>person_NN</c>), where it is baked: its own
    /// shaped body, skin and eyes, its role's outfit, hair and beard fitted to it (<see cref="Files"/>); null in a crowd.
    /// </summary>
    public string? Folder { get; init; }

    /// <summary>The outfit it is baked in (<c>townsman</c>, <c>townswoman</c>, <c>maiden</c>, <c>monk</c>, <c>priest</c>, <c>nun</c>, <c>guard</c>), or null.</summary>
    public string? Outfit { get; init; }

    /// <summary>Its baked files' names in <see cref="Folder"/>, or null in a crowd.</summary>
    public CrowdFiles? Files { get; init; }

    /// <summary>Baked with a hood over its head (<c>hooded</c>, <c>files.hood</c>), drawn in place of its hair.</summary>
    public bool Hooded { get; init; }

    /// <summary>Its hips' height at rest (<c>hip_height_m</c>), 0 when the crowd does not say.</summary>
    public float HipHeight { get; init; }

    /// <summary>Its base's hips' height (<c>base_hip_height_m</c>: 0.90 the man, 0.81 the woman), 0 when the crowd does not say.</summary>
    public float BaseHipHeight { get; init; }

    /// <summary>Whether the crowd names its baked files (a newer set).</summary>
    public bool IsBaked => Folder is { Length: > 0 } && Files is { Body.Length: > 0 };

    /// <summary>
    /// How much its motions' keyed hips (and so its strides) are scaled to its own proportions: its hips' height over its
    /// base's (the models repository: <i>"scale the pelvis's keyed height by hip_height_m / base_hip_height_m"</i>); 1 when the crowd does not say.
    /// </summary>
    public float Proportion => HipHeight > 0f && BaseHipHeight > 0f ? HipHeight / BaseHipHeight : 1f;

    /// <summary>Its folder under the models folder (<c>assets/characters/crowd/person_NN</c>), or null when it is not baked.</summary>
    public string? BakedFolder => IsBaked ? "assets/characters/crowd/" + Folder : null;

    /// <summary>
    /// The person as the models repository baked it, as a recipe: its own body (<c>person_NN</c>) and what it is baked in (its
    /// outfit, hair, beard: <see cref="CrowdFiles.Worn"/>), from <see cref="BakedFolder"/>, each worn file with the file it was fitted from
    /// (its outfit's on its base, <c>&lt;outfit&gt;_&lt;sex&gt;</c>; its cut, <c>hair_&lt;cut&gt;_&lt;sex&gt;</c>; its beard): what it covers and
    /// hides (<see cref="PersonRecipe.FittedFrom"/>). No look: its skin, eyes and hair carry their own colours. Named <paramref name="name"/>
    /// (its own name when null). Throws <see cref="InvalidOperationException"/> when it is not baked.
    /// </summary>
    public PersonRecipe BakedRecipe(string? name = null)
    {
        if (!IsBaked) throw new InvalidOperationException($"{Name} is not baked (the crowd names no folder and files for it)");
        var files = Files!;
        static string Stem(string file) => Path.GetFileNameWithoutExtension(file);
        var from = new Dictionary<string, string>(StringComparer.Ordinal);
        if (files.Outfit is { Length: > 0 } outfit && Outfit is { Length: > 0 } made) from[Stem(outfit)] = $"{made}_{Sex}";
        if (files.Hair is { Length: > 0 } hair && Hair is { Length: > 0 } cut) from[Stem(hair)] = $"hair_{cut}_{Sex}";
        if (files.Beard is { Length: > 0 } beard && Beard is { Length: > 0 } style) from[Stem(beard)] = $"{style}_{Sex}";
        if (files.Hood is { Length: > 0 } hood) from[Stem(hood)] = $"hood_{Sex}";   // Fitted from the hood (its covers_hair, its hidden)
        return new PersonRecipe(name ?? Name, Sex, Stem(files.Body), [.. files.Worn().Select(Stem)]) { Folder = BakedFolder, FittedFrom = from };
    }
    // ---- end
}

/// <summary>
/// A baked crowd person's files (<c>crowd.json</c>'s <c>files</c>), each a name in its folder: the shaped body, the outfit fitted
/// to it, its hair and beard (null where it has none), the build's record.
/// </summary>
public sealed record CrowdFiles(string Body, string? Outfit, string? Hair, string? Beard, string? Record)
{
    /// <summary>
    /// The hood fitted to the person (<c>files.hood</c>, a person whose entry says <c>hooded</c>: the two
    /// field hands), or null. Its outfit's record says <c>covers_hair</c>, so the hair is left out under it (the beard stays).
    /// </summary>
    public string? Hood { get; init; }

    /// <summary>The worn files in the order they are put on: the outfit, the hood, the hair, the beard.</summary>
    public IEnumerable<string> Worn()
    {
        if (Outfit is { Length: > 0 } o) yield return o;
        if (Hood is { Length: > 0 } hd) yield return hd;
        if (Hair is { Length: > 0 } h) yield return h;
        if (Beard is { Length: > 0 } b) yield return b;
    }
}

/// <summary>A crowd person's skin parameters as <c>crowd.json</c> gives them (its skin tool's): the ones the platform reads.</summary>
/// <param name="HairColour">The hair's colour, linear RGB (the hair's material factor, CONVENTIONS, Hair).</param>
/// <param name="Grey">The hair's share of grey, 0 to 1.</param>
/// <param name="Vampire">The undead's pallor, 0 for the living.</param>
/// <param name="Deathly">How deathly the undead look, 0 for the living.</param>
public sealed record CrowdSkin(float Melanin, float Sun, Vector3 HairColour, float Grey, float Vampire, float Deathly);

/// <summary>
/// What makes a person built from a base and its worn files look like one of the crowd rather than another: applied to
/// the plain build (<see cref="PeopleLibrary.Person"/>), sharing its vertices, indices and skeleton.
/// <list type="bullet">
/// <item><b>The hair's colour</b> (<see cref="HairColour"/>): the hair's and the beard's material factor, as the models repository sets it
/// (CONVENTIONS, Hair: <i>"baseColorFactor is the person's hair colour"</i>).</item>
/// <item><b>Grey</b> (<see cref="Grey"/>): lock by lock, in the order the hair's fit data gives (<c>&lt;mesh&gt;.grey</c>, 0 first): a lock
/// whose order is under the share is greyed (<see cref="GreyLock"/>: its own colour <see cref="GreyMix"/> of the way to <see cref="GreyColour"/>,
/// so a few grey locks in dark hair read as salt and pepper, not as specks of white). The factor is then the lighter of the two a channel, and each
/// vertex's colour carries its lock's colour over it, so neither goes past 1.</item>
/// <item><b>The skin's tint</b> (<see cref="SkinTint"/>): the skin's base colour factor, a stand-in for the skin the models repository composes
/// from layers the platform does not have (<see cref="CrowdPerson"/>): darker with more melanin and sun than the base was made with,
/// greyer and paler for the undead.</item>
/// </list>
/// </summary>
public sealed record PersonLook(Vector3 HairColour, float Grey, Vector3 SkinTint)
{
    /// <summary>The colour a grey lock is (linear): an old man's grey (sRGB about 150), lighter than any hair colour of the crowd.</summary>
    public static readonly Vector3 GreyColour = new(0.30f, 0.29f, 0.28f);

    /// <summary>How far a grey lock's colour goes from the hair's toward <see cref="GreyColour"/> (SANDBOX, ours: chosen on the stills).</summary>
    public const float GreyMix = 0.6f;

    /// <summary>A grey lock's colour for this look (linear).</summary>
    public Vector3 GreyLock => Vector3.Lerp(HairColour, GreyColour, GreyMix);

    /// <summary>The base bodies' own skin parameters (their <c>.json</c>: melanin 0.032, sun 0.45), against which the tint is taken.</summary>
    public const float BaseMelanin = 0.032f, BaseSun = 0.45f;

    /// <summary>A key for caches and logs (the numbers to three places).</summary>
    public string Key => string.Create(CultureInfo.InvariantCulture,
        $"hair {HairColour.X:0.###},{HairColour.Y:0.###},{HairColour.Z:0.###} grey {Grey:0.###} skin {SkinTint.X:0.###},{SkinTint.Y:0.###},{SkinTint.Z:0.###}");

    /// <summary>
    /// A crowd person's look. The tint (ours, a stand-in, SANDBOX): the melanin a person's skin shows, taken as its melanin × (1 + sun/2),
    /// darkens each channel by e^(−k·Δ) for its difference from the base's, k (6, 9, 13) a channel (red least, as melanin absorbs);
    /// the undead's pallor (vampire × (0.6 + 0.4 deathly)) then draws it that far toward its own grey, a little cool and lighter.
    /// </summary>
    public static PersonLook Of(CrowdPerson p)
    {
        float shown = p.Skin.Melanin * (1f + 0.5f * p.Skin.Sun) - BaseMelanin * (1f + 0.5f * BaseSun);
        var tint = new Vector3(Core.DetMath.Exp(-6f * shown), Core.DetMath.Exp(-9f * shown), Core.DetMath.Exp(-13f * shown));
        float pallor = Math.Clamp(p.Skin.Vampire * (0.6f + 0.4f * p.Skin.Deathly), 0f, 1f);
        if (pallor > 0f)
        {
            float grey = 0.3f * tint.X + 0.55f * tint.Y + 0.15f * tint.Z;
            var pale = new Vector3(0.92f, 0.95f, 1.0f) * MathF.Min(1f, grey * 1.08f);
            tint = Vector3.Lerp(tint, pale, 0.75f * pallor);
        }
        return new PersonLook(p.Skin.HairColour, Math.Clamp(p.Skin.Grey, 0f, 1f), Vector3.Clamp(tint, Vector3.Zero, Vector3.One));
    }
}

/// <summary>The models repository's crowd file read.</summary>
public static class Crowd
{
    /// <summary>The crowd's file under a models folder.</summary>
    public static string PathIn(string folder) => Path.Combine(folder, "assets", "characters", "crowd", "crowd.json");

    /// <summary>
    /// The people in <paramref name="json"/> (a list of objects, as <c>crowd.py</c> writes it), in its order; one marked
    /// <c>"ok": false</c> is left out. Throws <see cref="InvalidDataException"/> when it is not that.
    /// </summary>
    public static IReadOnlyList<CrowdPerson> Read(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("crowd.json: not a list of people");
        var people = new List<CrowdPerson>();
        foreach (var p in doc.RootElement.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object) continue;
            if (p.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False) continue;
            string name = Text(p, "name") ?? throw new InvalidDataException("crowd.json: a person without a name");
            string sex = Text(p, "sex") is "male" or "female" ? Text(p, "sex")! : throw new InvalidDataException($"crowd.json: {name}'s sex is not male or female");
            string? hair = null, beard = null;
            bool covered = false;
            float grey = 0f;
            if (p.TryGetProperty("groom", out var g) && g.ValueKind == JsonValueKind.Object)
            {
                hair = Text(g, "hair");
                beard = Text(g, "beard");
                covered = g.TryGetProperty("covered", out var c) && c.ValueKind == JsonValueKind.True;
                grey = Number(g, "grey", 0f);
            }
            var skin = p.TryGetProperty("skin", out var s) && s.ValueKind == JsonValueKind.Object ? s : default;
            var hairColour = skin.ValueKind == JsonValueKind.Object && skin.TryGetProperty("hair", out var hc) && hc.ValueKind == JsonValueKind.Array && hc.GetArrayLength() == 3
                ? new Vector3(hc[0].GetSingle(), hc[1].GetSingle(), hc[2].GetSingle()) : new Vector3(0.085f, 0.055f, 0.032f);
            float Skin(string key, float fallback) => skin.ValueKind == JsonValueKind.Object ? Number(skin, key, fallback) : fallback;
            // the baked person, when the crowd names it.
            CrowdFiles? files = p.TryGetProperty("files", out var f) && f.ValueKind == JsonValueKind.Object && Text(f, "body") is { } bodyFile
                ? new CrowdFiles(bodyFile, Text(f, "outfit"), Text(f, "hair"), Text(f, "beard"), Text(f, "record")) { Hood = Text(f, "hood") } : null;   // the hood
            people.Add(new CrowdPerson(name, sex, Text(p, "role") ?? "", Text(p, "kind") ?? "living", hair, beard, covered,
                                       Number(p, "age", 30f), Number(p, "height_m", 0f), Text(p, "eye") ?? "",
                                       new CrowdSkin(Skin("melanin", PersonLook.BaseMelanin), Skin("sun", PersonLook.BaseSun), hairColour,
                                                     MathF.Max(grey, Skin("grey", 0f)), Skin("vampire", 0f), Skin("deathly", 0f)))
            {
                Folder = Text(p, "folder"), Outfit = Text(p, "outfit"), Files = files,
                Hooded = p.TryGetProperty("hooded", out var hd) && hd.ValueKind == JsonValueKind.True && files?.Hood is not null,
                HipHeight = Number(p, "hip_height_m", 0f), BaseHipHeight = Number(p, "base_hip_height_m", 0f),
            });
        }
        return people;
    }

    static string? Text(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

    static float Number(JsonElement e, string key, float fallback) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;
}
