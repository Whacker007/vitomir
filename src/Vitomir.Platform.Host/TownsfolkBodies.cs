// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using System.Globalization;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.People;

namespace Vitomir.Platform.Host;

/// <summary>
/// (<i>"get the new 3d models animations and movement across the board working … general non combat
/// movement for now"</i>): the townsfolk who do not fight drawn as the models repository's crowd (<see cref="CrowdPerson"/>) in place of
/// 1.1's stand-ins, kind by kind; anyone who fights (the guards, the watch, <c>--foe</c>'s fighters, the arena's) stays on 1.1's bodies,
/// the only ones with combat for now. <c>--townsfolk-oneone</c> (or the setting <c>townsfolkBodies</c> <c>oneone</c>) puts them all back
/// on 1.1's.
/// <para>
/// <b>Who is who</b> (<see cref="Specs"/>): each 1.1 townsfolk template is a sex, the crowd's roles it is drawn from, what it wears over
/// the outfit its person's role wears, and a walk of the models repository's styles. <b>Which person</b>: the townsfolk a session makes are a
/// list fixed by its options (<see cref="Townsfolk.ForSlots"/>: by day or night, how many); each place in it is given a person of its
/// template's pool, starting at a fixed point of the pool for the template and taking the next not yet given, so the same options draw
/// the same people (a replay, a fresh program) and two townsfolk of one template differ while the pool lasts. The person's outfit is its
/// role's (<see cref="OutfitOf"/>), its hair and beard its own, its look (hair colour and grey, skin tone) and height its own.
/// </para>
/// <para>
/// The crowd <b>baked</b>, each person in a folder of its own beside
/// <c>crowd.json</c> (its shaped body with its own face, figure, skin and eyes; its role's outfit, hair and beard fitted to it): a
/// townsperson is drawn as its baked person (<see cref="BakedRecipeOf"/>: no tint, no height scale, its own proportions; the base's motions
/// with the pelvis's keyed place scaled to its hips, <see cref="ProportionedClip"/>), from the baked roles (<see cref="Spec.Baked"/>: the
/// laymen the field hands, the women the townswomen, the young lady the craftswoman, the monks the monk and the priest). An older
/// crowd (no person names a folder) is built at run time as before, said once; a person whose folder is missing likewise, said
/// once for it. <c>--crowd-built</c> (or the setting <c>townsfolkBodies</c> <c>models-built</c>) is the earlier build's run-time crowd throughout.
/// </para>
/// </summary>
public static class TownsfolkBodies
{
    /// <summary>The words <c>townsfolkBodies</c> takes, and the tape's note says (<c>models</c>: on the crowd; the note's the earlier build's run-time crowd).</summary>
    public const string Models = "models", OneOne = "oneone";

    /// <summary>
    /// The setting <c>townsfolkBodies</c>'s word for the earlier build's run-time crowd (<see cref="CrowdBuiltSwitch"/> for one run), and the
    /// tape's note <c>townsfolk</c> when any townsperson was drawn as its baked person (<see cref="Baked"/>; a tape noting <c>models</c>
    /// had the earlier build's run-time crowd and replays with it).
    /// </summary>
    public const string ModelsBuilt = "models-built", Baked = "baked";

    /// <summary>The switch that draws the crowd as before at run time, on the bases, rather than as the models repository baked it.</summary>
    public const string CrowdBuiltSwitch = "crowd-built";

    /// <summary>The switch that keeps the townsfolk on 1.1's bodies.</summary>
    public const string OneOneSwitch = "townsfolk-oneone";

    /// <summary>The tape head's note: <c>models</c> when the townsfolk were drawn on the models repository's crowd; none: 1.1's.</summary>
    public const string TapeNote = "townsfolk";

    /// <summary>
    /// SANDBOX (ours): a townsperson standing plays one loop of the stand (8 s) and then the set's fidget (<c>idle</c>, 4.5 s), round
    /// again (<see cref="FidgetClip"/>).
    /// </summary>
    public const float FidgetAfterSeconds = 8f;

    /// <summary>
    /// A 1.1 townsfolk template as a crowd person: its sex, the crowd roles it is drawn from (the first that has anyone), what it wears over
    /// its role's outfit, its walk; <paramref name="Eldest"/>: the pool's eldest first (an old man), not a point of it fixed by the name.
    /// </summary>
    public sealed record Spec(string Sex, string[][] Roles, string[] Over, string? Walk, bool Eldest = false)
    {
        /// <summary>
        /// The roles of the baked crowd it is drawn from (the first that has anyone), each baked person in its own outfit and
        /// nothing over it (a hood fitted to the base is not fitted to a baked person: the hooded variants are to come); null: <see cref="Roles"/>.
        /// </summary>
        public string[][]? Baked { get; init; }
    }

    static readonly string[] Laymen = ["field_hand", "guard"], Clerics = ["cleric"], Laywomen = ["townswoman", "craftswoman"], Maidens = ["craftswoman"];
    static readonly string[] FieldHands = ["field_hand"], Townswomen = ["townswoman"];   // the baked crowd's
    // Four more laymen in the townsman's tunic — a merchant, two craftsmen (one of them old) and a
    // labourer. The laymen's templates draw from them all (the field hands, with the hoods, among them); a crowd without them (round
    // 39 and before) leaves each pool the field hands it was, in the same order, so the same people.
    static readonly string[] BakedLaymen = ["field_hand", "merchant", "craftsman", "labourer"], Merchants = ["merchant"], Craftsmen = ["craftsman", "labourer"],
                             Labourers = ["labourer"];

    /// <summary>
    /// The non-fighting townsfolk by their 1.1 template (the catalogue's and <see cref="Townsfolk.Templates"/>'), and how each is drawn.
    /// The crowd's men are field hands, guards (as laymen here, in the townsman's tunic: the crowd has no townsman of its own) and clerics;
    /// its women townswomen (covered), a craftswoman (in braids) and a nun. The walks are the models repository's styles (its README).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Spec> Specs = new Dictionary<string, Spec>(StringComparer.Ordinal)
    {
        // A man of means: the proud walk (bare-headed: the felt cap, fitted on its own, is not kept off the hair under it, which comes
        // through it, CONVENTIONS, Outfits: "a separate hood is not yet kept off the tunic under it").
        // Baked, the laymen are the field hands in the townsman's tunic (the old man and the beggar too, bare-headed, until
        // the hooded variants come; their walks stay theirs); the monks the monk and the priest; the young lady the craftswoman in the
        // maiden's dress; the townswomen and the milkmaid the townswomen. The guards (00, 02) fight, and the nun has no 1.1 template here.
        // The merchants the crowd's merchant first, the townsman a craftsman or the labourer, the drunk the labourer, the old
        // man the eldest layman (the old craftsman), each spilling into the laymen; the beggar a field hand (hooded).
        ["Merchant"] = new("male", [Laymen], [], "walk_proud") { Baked = [Merchants, BakedLaymen] },
        ["Civ03merchant1"] = new("male", [Laymen], [], "walk_proud") { Baked = [Merchants, BakedLaymen] },
        ["Civ12male"] = new("male", [Laymen], [], null) { Baked = [Craftsmen, BakedLaymen] },                          // the townsman: the plain walk's eight ways
        ["OldMan"] = new("male", [Laymen], ["hood_male"], "walk_old", Eldest: true) { Baked = [BakedLaymen] },   // the old walk, hooded; the crowd's eldest layman
        ["Civ07drunk"] = new("male", [Laymen], [], "walk_drunk") { Baked = [Labourers, BakedLaymen] },
        ["Civ02beggar"] = new("male", [Laymen], ["hood_male"], "walk_shuffle") { Baked = [FieldHands, BakedLaymen] },   // the beggar's shuffle
        ["Monk"] = new("male", [Clerics], [], "walk_procession") { Baked = [Clerics] },                     // the procession's pace, hands folded
        ["Civ10monk2"] = new("male", [Clerics], [], "walk_procession") { Baked = [Clerics] },
        ["YoungLady"] = new("female", [Maidens, Laywomen], [], "walk_proud") { Baked = [Maidens, Townswomen] },   // a girl in braids first
        ["Civ06Milkmaid"] = new("female", [Laywomen], [], null) { Baked = [Townswomen] },
        ["Civ11female"] = new("female", [Laywomen], [], null) { Baked = [Townswomen] },
    };

    /// <summary>
    /// The townsfolk's bodies for a run (<see cref="SessionOptions.TownsfolkBody"/>): 1.1's (null) with <c>--townsfolk-oneone</c>
    /// (<paramref name="oneOneSwitch"/>) or the setting <c>townsfolkBodies</c> <c>oneone</c>, else the crowd (<see cref="Models"/>; any other
    /// word in the setting is taken as models).
    /// </summary>
    public static string? Choose(bool oneOneSwitch, string? setting) =>
        oneOneSwitch || string.Equals(setting, OneOne, StringComparison.OrdinalIgnoreCase) ? null : Models;

    /// <summary>
    /// Whether the crowd is drawn as before at run time (<see cref="SessionOptions.CrowdBuilt"/>): <c>--crowd-built</c>
    /// (<paramref name="crowdBuiltSwitch"/>) or the setting <c>townsfolkBodies</c> <c>models-built</c>.
    /// </summary>
    public static bool ChooseBuilt(bool crowdBuiltSwitch, string? setting) =>
        crowdBuiltSwitch || string.Equals(setting, ModelsBuilt, StringComparison.OrdinalIgnoreCase);

    /// <summary>The townsfolk a session of <paramref name="people"/> makes by night or by day, in its order (the keepers after them not in it).</summary>
    public static string[] Slots(bool night, int people) => Townsfolk.ForSlots(night, people);

    /// <summary>Whether a 1.1 template is a townsperson drawn on the crowd (not a fighter: the guards and the watch are not here).</summary>
    public static bool Drawn(string? template) => template is not null && Specs.ContainsKey(template);

    /// <summary>
    /// The outfit a crowd person's role wears (by stem): a cleric the monk's habit (the monk's ring of hair) or the priest's tunic (the
    /// priest's tonsure); a nun the nun's; a woman whose hair is covered the townswoman's dress and head-cloth, one in braids the maiden's
    /// dress; any other man the townsman's tunic.
    /// </summary>
    public static string OutfitOf(CrowdPerson p) => (p.Sex, p.Role) switch
    {
        ("male", "cleric") => p.Hair == "priest" ? "priest_male" : "monk_male",
        ("female", "nun") => "nun_female",
        ("female", _) => p.Covered || p.Hair is null ? "townswoman_female" : "maiden_female",
        _ => "townsman_male",
    };

    /// <summary>
    /// The crowd person each place of <paramref name="slots"/> (1.1 templates) is drawn as, or null for a template that is not drawn on
    /// The crowd (or a pool that is empty): deterministic in the list and the crowd alone. <paramref name="baked"/>, from the
    /// baked roles (<see cref="Spec.Baked"/>), a used-up pool spilling into the template's later roles. Two of a pool share a person only
    /// when every person it can draw is given: the template's <i>n</i>th then takes the person <i>n</i> past its starting point.
    /// </summary>
    public static CrowdPerson?[] Assign(IReadOnlyList<string> slots, IReadOnlyList<CrowdPerson> crowd, bool baked = false)
    {
        var given = new CrowdPerson?[slots.Count];
        var used = new HashSet<string>(StringComparer.Ordinal);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        // Those who want the eldest choose first (an old man before a merchant takes him), then the rest, each in the list's order.
        var order = Enumerable.Range(0, slots.Count).OrderBy(i => Specs.TryGetValue(slots[i], out var s) && s.Eldest ? 0 : 1).ThenBy(i => i);
        foreach (int i in order)
        {
            if (!Specs.TryGetValue(slots[i], out var spec)) continue;
            var pool = Pool(spec, crowd, baked);
            if (pool.Count == 0) continue;
            int occurrence = seen.TryGetValue(slots[i], out int n) ? n : 0;
            seen[slots[i]] = occurrence + 1;
            int start = spec.Eldest ? 0 : (int)(StableHash(slots[i]) % (uint)pool.Count);
            CrowdPerson? pick = null;
            for (int k = 0; k < pool.Count && pick is null; k++)
                if (!used.Contains(pool[(start + k) % pool.Count].Name)) pick = pool[(start + k) % pool.Count];
            // Baked, a pool used up spills into its template's later roles before anyone is drawn twice (a second young
            // lady a townswoman not yet given, rather than the one craftswoman again).
            if (pick is null && baked)
                foreach (var spill in Spill(spec, crowd))
                    if (!used.Contains(spill.Name)) { pick = spill; break; }
            pick ??= pool[(start + occurrence) % pool.Count];
            used.Add(pick.Name);
            given[i] = pick;
        }
        return given;
    }

    /// <summary>The crowd's people a template draws from: those of its sex in its first role list that has anyone, in the crowd's order (the eldest first for <see cref="Spec.Eldest"/>).</summary>
    public static IReadOnlyList<CrowdPerson> Pool(Spec spec, IReadOnlyList<CrowdPerson> crowd, bool baked = false)
    {
        foreach (var roles in baked ? spec.Baked ?? spec.Roles : spec.Roles)
        {
            var pool = crowd.Where(p => p.Sex == spec.Sex && roles.Contains(p.Role)).ToList();
            if (spec.Eldest) pool = [.. pool.OrderByDescending(p => p.Age).ThenBy(p => p.Name, StringComparer.Ordinal)];
            if (pool.Count > 0) return pool;
        }
        return [];
    }

    /// <summary>
    /// The baked people of a template's later roles than its pool's (<see cref="Spec.Baked"/>), of its sex, in the crowd's
    /// order: whom a used-up pool spills into.
    /// </summary>
    static IEnumerable<CrowdPerson> Spill(Spec spec, IReadOnlyList<CrowdPerson> crowd)
    {
        var lists = spec.Baked ?? spec.Roles;
        int first = Array.FindIndex(lists, roles => crowd.Any(p => p.Sex == spec.Sex && roles.Contains(p.Role)));
        for (int l = first + 1; first >= 0 && l < lists.Length; l++)
            foreach (var p in crowd)
                if (p.Sex == spec.Sex && lists[l].Contains(p.Role)) yield return p;
    }

    /// <summary>FNV-1a of a name's characters: the same on every machine and run (not <see cref="string.GetHashCode()"/>).</summary>
    static uint StableHash(string s)
    {
        uint h = 2166136261;
        foreach (char c in s) { h ^= c; h *= 16777619; }
        return h;
    }

    /// <summary>A townsperson's recipe: its person in its role's outfit and what its template wears over it, named for both.</summary>
    public static PersonRecipe RecipeOf(string template, CrowdPerson person) =>
        person.Recipe([OutfitOf(person), .. Specs[template].Over], $"{person.Name} as {template}");

    /// <summary>A townsperson's style: its template's walk, its height over its base's, the stand broken by the fidget.</summary>
    public static PersonStyle StyleOf(string template, CrowdPerson person, PersonModel model) => new()
    {
        Walk = Specs[template].Walk,
        Scale = person.HeightMetres > 0f && model.BaseHeight > 0f ? person.HeightMetres / model.BaseHeight : 1f,
        FidgetAfter = FidgetAfterSeconds,
    };

    // ---- the baked crowd ----
    /// <summary>Whether a baked person's folder is in <paramref name="modelsFolder"/>.</summary>
    public static bool BakedHere(CrowdPerson person, string modelsFolder) =>
        person.BakedFolder is { } folder && Directory.Exists(Path.Combine(modelsFolder, folder.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>
    /// A townsperson's recipe as its baked person (<see cref="CrowdPerson.BakedRecipe"/>): its own body and what it is baked in,
    /// from its folder; no look, nothing over it (<see cref="Spec.Baked"/>); named for the person and the template.
    /// </summary>
    public static PersonRecipe BakedRecipeOf(string template, CrowdPerson person) => person.BakedRecipe($"{person.Name} as {template}");

    /// <summary>
    /// A baked townsperson's style: its template's walk, drawn at its own size (its figure carries its height), the stand broken
    /// by the fidget; its strides are its motions' (<see cref="ProportionedClip"/>: the notes' ground speed by its proportion).
    /// </summary>
    public static PersonStyle BakedStyleOf(string template) => new() { Walk = Specs[template].Walk, Scale = 1f, FidgetAfter = FidgetAfterSeconds };
    // ---- end

    /// <summary>The townsfolk built: each place's kind (−1: 1.1's, as before), what was built, why not when nothing was, the time it took.</summary>
    public sealed record Built(int[] Kinds, IReadOnlyList<string> Lines, string? Why, double Milliseconds)
    {
        /// <summary>Whether any townsperson is drawn on the crowd.</summary>
        public bool Any => Kinds.Any(k => k >= 0);

        /// <summary>How many places are drawn as their baked person (the rest of those drawn: built at run time, as before).</summary>
        public int BakedPlaces { get; init; }

        /// <summary>
        /// Whether the places were given their people as the baked crowd gives them (<see cref="Assign"/> with <c>baked</c>: the baked
        /// templates' roles, a used-up pool spilling into its later roles) — so even when no baked person's files could be read and every
        /// place was built at run time.
        /// </summary>
        public bool AssignedBaked { get; init; }

        /// <summary>
        /// The tape's note <c>townsfolk</c>: <see cref="Baked"/> when the people were given as the baked crowd gives them, else
        /// <see cref="Models"/> (the earlier build's run-time crowd: <c>--crowd-built</c>, or a crowd). by how the people were given, not
        /// by how many were drawn baked — a run whose every baked person fell back to the run-time build (its files unreadable: an older set's
        /// shared maps where they are not) was noted the earlier build's, and its replay gave the places the earlier build's people (the day's merchant another man,
        /// another walk speed: a digest apart at tick 0).
        /// </summary>
        public string Note => AssignedBaked ? TownsfolkBodies.Baked : Models;
    }

    /// <summary>
    /// The townsfolk of <paramref name="slots"/> built from the models repository's crowd into <paramref name="assets"/> (one kind for each
    /// different person, outfit and style, named <c>people:&lt;person&gt; as &lt;template&gt;</c>, after every kind already there) and their
    /// textures into <paramref name="textures"/>, with <paramref name="library"/> (the player's, so the maps they share are decoded once) or
    /// one opened on <paramref name="folder"/>. Nothing built (every place −1) with the reason when the folder, its crowd or a file is
    /// missing; never throws for those. Call before the world's first tick. each person as the models repository baked it, unless
    /// <paramref name="crowdBuilt"/> (the earlier build's run-time build throughout) or its folder is missing (that person built at run time, said once);
    /// <paramref name="crowdGiven"/> in place of the files' crowd (tests).
    /// </summary>
    public static Built Build(IReadOnlyList<string> slots, PeopleLibrary? library, string? folder, CharacterAssets assets,
                              IDictionary<string, TextureImage> textures, bool decodeImages, int maxTextureSide = 0, bool crowdBuilt = false,
                              IReadOnlyList<CrowdPerson>? crowdGiven = null)
    {
        var clock = Stopwatch.StartNew();
        var kinds = new int[slots.Count];
        Array.Fill(kinds, -1);
        var lines = new List<string>();
        Built None(string why) => new(kinds.Select(_ => -1).ToArray(), lines, why, clock.Elapsed.TotalMilliseconds);
        if (!slots.Any(Drawn)) return None("no townsfolk who do not fight");
        if (library is null)
        {
            if (folder is null) return None($"no models folder found (--models, the setting modelsFolder, {PeopleBodies.EnvironmentVariable}, {PeopleBodies.PackageFolder} beside the program, {PeopleBodies.DefaultRepository})");
            library = PeopleLibrary.Locate(folder, out string notOpen, decodeImages, maxTextureSide);
            if (library is null) return None($"{folder} is not the models repository's ({notOpen})");
        }
        IReadOnlyList<CrowdPerson> crowd;
        try { crowd = crowdGiven ?? library.Crowd(); }   // a crowd given in place of the files' (the tests' missing folder)
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            return None($"the models repository's crowd could not be read: {e.Message}");
        }
        if (crowd.Count == 0) return None($"the models repository's files at {library.Folder} have no crowd (assets/characters/crowd/crowd.json)");
        // The crowd as the models repository baked it, unless the earlier build's run-time crowd is asked or the crowd is older.
        bool baked = !crowdBuilt && crowd.Any(p => p.IsBaked);
        if (crowdBuilt) lines.Add("the crowd built at run time on the bases (--crowd-built)");
        else if (!baked) lines.Add("the crowd is not baked in these files (too old): each person built at run time on the bases");
        var people = Assign(slots, crowd, baked);
        var made = new Dictionary<string, int>(StringComparer.Ordinal);
        var motions = new Dictionary<string, IReadOnlyList<INotedClip>>(StringComparer.Ordinal);
        var missing = new HashSet<string>(StringComparer.Ordinal);
        int bakedPlaces = 0;
        try
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (people[i] is not { } person) continue;
                PersonModel? model = null;
                PersonRecipe recipe;
                PersonStyle style;
                IReadOnlyList<INotedClip> set;
                if (baked && BakedHere(person, library.Folder))
                {
                    try { model = library.Person(BakedRecipeOf(slots[i], person)); }   // built once a person (its key has no template), kept
                    catch (Exception e) when (e is IOException or InvalidDataException or FormatException or System.Text.Json.JsonException)
                    {
                        if (missing.Add(person.Name)) lines.Add($"{person.Name}'s baked files could not be read ({e.Message}): built at run time on the bases");
                    }
                }
                else if (baked && missing.Add(person.Name))
                    lines.Add($"{person.Name}'s folder ({person.BakedFolder ?? "none named"}) is not in the models folder: built at run time on the bases");
                if (model is not null)
                {
                    recipe = BakedRecipeOf(slots[i], person);   // named for the template too
                    style = BakedStyleOf(slots[i]);
                    string at = person.Sex + " " + person.Proportion.ToString("R", CultureInfo.InvariantCulture);
                    if (!motions.TryGetValue(at, out set!)) motions[at] = set = ProportionedClip.Set(library.Motions(person.Sex), person.Proportion);
                    bakedPlaces++;
                }
                else
                {
                    recipe = RecipeOf(slots[i], person);
                    model = library.Person(recipe);
                    style = StyleOf(slots[i], person, model);
                    set = library.Motions(person.Sex);
                }
                string key = recipe.Key + " / " + style.Key;
                if (!made.TryGetValue(key, out int kind))
                {
                    kind = PeopleKinds.Register(assets, model, set, "people:" + recipe.Name, style);
                    made[key] = kind;
                    foreach (var t in model.Textures) textures.TryAdd(t.Name, t);
                    lines.Add(recipe.Folder is not null
                        ? string.Create(CultureInfo.InvariantCulture,
                            $"{slots[i]} is the crowd's {person.Name} baked ({person.Sex} {person.Role}, {person.Nature}, {person.HeightMetres:0.00} m, hips {person.HipHeight:0.000} m): {string.Join(", ", recipe.Wear)}; walks {style.Walk ?? "the plain walk"}, its motions' hips ×{person.Proportion:0.000}")
                        : string.Create(CultureInfo.InvariantCulture,
                            $"{slots[i]} is the crowd's {person.Name} ({person.Sex} {person.Role}, {person.Nature}, {person.HeightMetres:0.00} m): {string.Join(", ", recipe.Wear)}; walks {style.Walk ?? "the plain walk"}, ×{style.Scale:0.000}"));
                }
                kinds[i] = kind;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or FormatException or System.Text.Json.JsonException
                                      or KeyNotFoundException or ArgumentException or InvalidOperationException)
        {
            // Kinds already added stay in the assets (indices are never taken back); none is used.
            return None($"the crowd could not be built from {library.Folder}: {e.Message}");
        }
        lines.Add(library.DecodeImages
            ? string.Create(CultureInfo.InvariantCulture, $"the people's maps decoded and kept: {library.TextureBytes / 1048576.0:0} MB (the player's with them{(library.MaxTextureSide > 0 ? $", each at most {library.MaxTextureSide}" : "")})")
            : "the people's maps not decoded (a run without pictures)");
        return new Built(kinds, lines, null, clock.Elapsed.TotalMilliseconds) { BakedPlaces = bakedPlaces, AssignedBaked = baked };   // the note by how the people were given
    }
}
