// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The template table is taken from src/Vitomir.Engine/World/Templates.cs (TemplateSet), the motion sets from
// src/Vitomir.Engine/Anim/MotionSets.cs, and the model cache from src/Vitomir.Engine/Scene/SceneMesh.cs (Model)
// at b4185ef, reworked for the platform.
using Vitomir.Formats;
using Vitomir.Platform.Animation;

namespace Vitomir.Platform.Assets.OneOne;

/// <summary>One of 1.1's characters, loaded: its model (in metres), its textures, its motion set, and the clip it plays for each role.</summary>
public sealed class OneOneCharacter
{
    /// <summary>The name it was asked for by (a catalogue name or a template).</summary>
    public required string Name { get; init; }
    /// <summary>The 1.1 template it was made from.</summary>
    public required string Template { get; init; }
    public required SkinnedModel Model { get; init; }
    /// <summary>The textures its parts name (<see cref="ModelPart.Texture"/>), each once, in part order.</summary>
    public required IReadOnlyList<TextureImage> Textures { get; init; }
    public required NmsMotionSet MotionSet { get; init; }
    /// <summary>For each <see cref="MotionRole"/>, the mode played and its clip (null when the character has none).</summary>
    public required OneOneRole[] Roles { get; init; }
    /// <summary>The head bone (the motion set's <c>Head</c> tag), or −1.</summary>
    public int HeadBone { get; init; } = -1;
    /// <summary>The template's <c>MODELSCALE</c> (tag 235): how much bigger 1.1 draws it than its model (1 for none). The program puts it in the world matrix.</summary>
    public float Scale { get; init; } = 1f;

    /// <summary>The hips, legs and arms for IK, from the motion set's foot and finger attach points, or null.</summary>
    public LimbMap? Limbs { get; init; }

    /// <summary>The stand-ins composed for the slots 1.1 has no motion for (<see cref="OneOneCompositions"/>).</summary>
    public IReadOnlyList<(MotionRole Role, ComposedClip Clip)> Compositions { get; init; } = [];

    /// <summary>The template's <c>SKELETONID</c> (tag 118): which copy of a held model fits it.</summary>
    public int SkeletonId { get; init; }

    /// <summary>The weapon it carries: the one asked for, else the template's <c>ACTOR_WEAPON</c> (tag 93); null for none.</summary>
    public OneOneWeapon? Weapon { get; init; }

    /// <summary>What its action channel plays for that weapon (<see cref="OneOneActions"/>), or null.</summary>
    public OneOneActionRoles? Actions { get; init; }

    /// <summary>The models it holds: the weapon's <c>WEAPON_MODEL_SKELETONID</c>, fitted to its things; empty for bare hands.</summary>
    public IReadOnlyList<HeldModel> Held { get; init; } = [];

    /// <summary>The overhead and the thrust stitched for this kind from 1.1's clips (<see cref="OneOneStitched"/>), or none.</summary>
    public IReadOnlyList<OneOneStitchedBlow> Stitched { get; init; } = [];

    /// <summary>This kind's side blows flattened (<see cref="OneOneStitched.Flat"/>: by the 1.1 clip each stands in for), or none; played in place of 1.1's when registered so.</summary>
    public IReadOnlyList<(string From, StitchedClip Clip)> Flat { get; init; } = [];

    /// <summary>
    /// This kind's blows with their wind-ups into the four ready poses (<see cref="OneOneWindUps"/>: its flattened side blows and its
    /// stitched overhead and thrust, each cocked before it strikes), or none; registered in place of <see cref="Stitched"/> with the
    /// flattened side blows (not with <c>swings-oneone</c>).
    /// </summary>
    public IReadOnlyList<OneOneStitchedBlow> WindUps { get; init; } = [];

    /// <summary>
    /// The left lent to this kind when its wind-ups have none — Christof's flattened backhand with its wind-up
    /// (<see cref="OneOneWindUps.LentLeft"/>): 1.1's guards and watchmen swing only the forehand. Registered first among <see cref="WindUps"/>
    /// unless the switch <c>guards-one-blow</c> (or <c>swings-oneone</c>) keeps the earlier build's. Null for a kind that has its own left or no wind-ups.
    /// </summary>
    public OneOneStitchedBlow? LentLeft { get; init; }

    public OneOneRole Role(MotionRole role) => Roles[(int)role];
}

/// <summary>What a 1.1 character plays for one role: the motion set's mode, its clip, and the stride-matched natural speed (m/s, 0 when not measured).</summary>
public sealed record OneOneRole(string? Mode, NadClip? Clip, float NaturalSpeed, float StartSeconds, bool Variable, bool NoFadeIn)
{
    public static readonly OneOneRole None = new(null, null, 0f, 0f, false, false);
}

/// <summary>A named character the catalogue loads: the 1.1 template it is, and what it plays beyond idle, walk and run.</summary>
/// <param name="Land">The mode for a hard landing, or null.</param>
/// <param name="LandFromTag">Where in the landing to start: the first of its marks of this type (a <c>.nad</c> tag), or null for its start.</param>
public sealed record OneOneCatalogueEntry(string Name, string Template, string About, string? Land = null, uint? LandFromTag = null);

/// <summary>
/// A small catalogue of 1.1's people for the new levels (ours): Christof, and four ordinary townsfolk of 1.1's
/// Prague. Each is a template of 1.1's <c>.not</c> files, whose <c>MODEL</c> (tag 4) and <c>MOTIONSET</c>
/// (tag 9) say what it is drawn with and how it moves.
/// </summary>
public static class OneOneCatalogue
{
    /// <summary>NAD tag type 7, <c>Fall</c>: in Christof's rough landing, the moment he meets the ground (frame 13).</summary>
    public const uint FallTag = 7;

    public static readonly IReadOnlyList<OneOneCatalogueEntry> Characters =
    [
        // Christof's `special22` is his rough landing (Christof_RoughLanding.nad, noloop endpause nofadein), the one
        // motion of 1.1's that is a landing; it begins with its own fall from six metres, so it is played from its
        // Fall mark, where he meets the ground.
        new("Christof", "Christof", "Christof Romuald, the player of the Dark Ages", Land: "special22", LandFromTag: FallTag),
        new("OldMan", "Civ01oldman", "an old man of Prague"),
        new("Merchant", "Civ04merchant2", "a merchant of Prague"),
        new("YoungLady", "Civ05YoungLady", "a young lady of Prague"),
        new("Monk", "Civ09monk1", "a monk of Prague"),
    ];

    public static OneOneCatalogueEntry? Find(string name) =>
        Characters.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// 1.1's templates (<c>misc/templates/*.not</c>), as the old engine's <c>TemplateSet</c> and 1.1 load them:
/// <c>global</c>, <c>darkAges</c>, <c>modernDay</c> and
/// <c>multiplayer</c>, then every <c>_</c>-prefixed <c>.not</c> of the folder, into one namespace in which the
/// first loaded wins; a template's tags are its chain of base templates' (tag 0, <c>PARENT</c>), a later one
/// overriding.
/// </summary>
public sealed class OneOneTemplates
{
    public const uint Model = 4, MotionSet = 9, ModelScale = 235;

    /// <summary>An actor's <c>ACTOR_WEAPON</c> (tag 93) and <c>SKELETONID</c> (tag 118).</summary>
    public const uint ActorWeapon = 93, SkeletonId = 118;

    readonly List<NotFile> files = [];

    public OneOneTemplates(GameInstall install)
    {
        foreach (var name in new[] { "global", "darkAges", "modernDay", "multiplayer" })
            if (install.TryRead($"misc/templates/{name}.not", out var data)) files.Add(NotFile.Read(data, name + ".not"));
        foreach (var path in install.List("misc/templates/"))
            if (Path.GetFileName(path).StartsWith('_') && path.EndsWith(".not", StringComparison.OrdinalIgnoreCase))
                files.Add(NotFile.Read(install.Read(path), path));
    }

    public NotTemplate? Find(string name)
    {
        foreach (var f in files)
            if (f.ByName.TryGetValue(name, out var t)) return t;
        return null;
    }

    /// <summary>A template's tags with its base chain flattened (a later template's tag overriding), or empty when there is none.</summary>
    public Dictionary<uint, NodTag> Resolve(string name)
    {
        var chain = new List<NotTemplate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var cur = name; !string.IsNullOrEmpty(cur) && seen.Add(cur);)
        {
            var t = Find(cur);
            if (t is null) break;
            chain.Add(t);
            cur = t.Parent;
        }
        var result = new Dictionary<uint, NodTag>();
        for (int i = chain.Count - 1; i >= 0; i--)
            foreach (var tag in chain[i].Tags) result[tag.Id] = tag;
        return result;
    }
}

/// <summary>
/// 1.1's characters read from the player's install (the install as a source behind the
/// asset registry): models, textures, motions and motion sets, each read once by name and kept, and
/// characters made from 1.1's templates. Safe to use from one loader thread at a time; what it gives is
/// handed to the simulation between ticks (<see cref="Register"/>).
/// </summary>
public sealed class OneOneLibrary(GameInstall install)
{
    /// <summary>The wind-ups' times and ready poses the characters are made with (the module's <c>windups.json</c>, read by the session), or null for <see cref="OneOneWindUps.Defaults"/>.</summary>
    public IReadOnlyList<WindUpShape>? WindUpShapes { get; init; }

    /// <summary>
    /// Whether the composed blows (the flattened side blows and the wind-ups) hold their hips upright
    /// (<see cref="StitchHips"/>: a blow struck while the body moves keeps its turn and bow). Off by default (the switch
    /// <c>blow-hips-upright</c> turns it on): the control's <c>ActionHips</c> (in the pose evaluator, every action, the switch
    /// on by default; <c>blows-on-moving-hips</c> off) does the same for every blow and more, and measured together this adds nothing.
    /// </summary>
    public bool HipsUpright { get; init; }

    readonly Dictionary<string, SkinnedModel?> models = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, NodModel?> nods = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, TgaImage?> images = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, TextureImage?> textures = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, NadAnimation?> anims = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<(string, bool), NadClip?> clips = [];
    readonly Dictionary<string, NmsMotionSet?> sets = new(StringComparer.OrdinalIgnoreCase);
    OneOneTemplates? templates;

    public GameInstall Install => install;

    /// <summary>1.1's template table, read the first time it is wanted.</summary>
    public OneOneTemplates Templates => templates ??= new OneOneTemplates(install);

    /// <summary>A <c>.nod</c> from <c>3d/models/</c> as read (the extension may be left off), or null when it is not there.</summary>
    public NodModel? Nod(string name)
    {
        string file = name.Contains('.') ? name : name + ".nod";
        if (nods.TryGetValue(file, out var n)) return n;
        return nods[file] = install.TryRead("3d/models/" + file, out var data) ? NodModel.Read(data, file) : null;
    }

    /// <summary>A model in metres with its materials resolved (<see cref="OneOneModels"/>), or null when it is not there.</summary>
    public SkinnedModel? Model(string name)
    {
        string file = name.Contains('.') ? name : name + ".nod";
        if (models.TryGetValue(file, out var m)) return m;
        var nod = Nod(file);
        if (nod is null) return models[file] = null;
        var materials = nod.Materials.Select(mat => OneOneModels.ResolveMaterial(install, mat, Image)).ToArray();
        return models[file] = OneOneModels.ToSkinnedModel(nod, Path.GetFileNameWithoutExtension(file), materials);
    }

    /// <summary>A texture by the path a <see cref="ModelPart"/> names, decoded (RGBA, rows top first), or null.</summary>
    public TextureImage? Texture(string path)
    {
        if (path.Length == 0) return null;
        if (textures.TryGetValue(path, out var t)) return t;
        return textures[path] = Image(path) is { } image ? OneOneModels.ToTextureImage(path, image) : null;
    }

    /// <summary>The textures a model's parts name, each once, in part order.</summary>
    public IReadOnlyList<TextureImage> TexturesOf(SkinnedModel model)
    {
        var list = new List<TextureImage>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in model.Parts)
            if (seen.Add(p.Texture) && Texture(p.Texture) is { } t) list.Add(t);
        return list;
    }

    TgaImage? Image(string path)
    {
        if (images.TryGetValue(path, out var i)) return i;
        return images[path] = install.TryRead(path, out var data) ? TgaImage.Read(data, path) : null;
    }

    /// <summary>A motion from <c>3d/anims/</c> (the extension may be left off), or null when it is not there or does not read — as 1.1, which drops a mode whose motion will not load.</summary>
    public NadAnimation? Animation(string name)
    {
        string file = name.Contains('.') ? name : name + ".nad";
        if (anims.TryGetValue(file, out var a)) return a;
        try { a = install.TryRead("3d/anims/" + file, out var data) ? NadAnimation.Read(data, file) : null; }
        catch (NodFormatException) { a = null; }
        return anims[file] = a;
    }

    /// <summary>A motion as a clip that loops or not (one clip object per motion and looping).</summary>
    public NadClip? Clip(string name, bool loops)
    {
        string file = name.Contains('.') ? name : name + ".nad";
        if (clips.TryGetValue((file.ToLowerInvariant(), loops), out var c)) return c;
        var a = Animation(file);
        return clips[(file.ToLowerInvariant(), loops)] = a is null ? null : new NadClip(Path.GetFileNameWithoutExtension(file), a, loops);
    }

    /// <summary>A motion set from <c>3d/Motions/</c> (the extension may be left off), or null.</summary>
    public NmsMotionSet? MotionSet(string name)
    {
        if (name.Length == 0) return null;
        string file = name.Contains('.') ? name : name + ".nms";
        if (sets.TryGetValue(file, out var s)) return s;
        return sets[file] = install.TryRead("3d/Motions/" + file, out var data) ? NmsMotionSet.Read(data, file) : null;
    }

    /// <summary>
    /// A motion set's mode and its clip: the first entry of that mode whose motion loads (1.1 drops the
    /// others; when a set lists a mode more than once 1.1 rolls among them — none of the
    /// catalogue's idles, walks or runs has more than one), looping unless the mode says <c>noloop</c>.
    /// </summary>
    public (NmsMode Mode, NadClip Clip)? Motion(NmsMotionSet set, string mode)
    {
        foreach (var m in set.Modes)
            if (m.Name.Equals(mode, StringComparison.OrdinalIgnoreCase) && Clip(m.File, !m.Has(NmsFlags.NoLoop)) is { } clip)
                return (m, clip);
        return null;
    }

    /// <summary>
    /// A catalogue character by its name (<see cref="OneOneCatalogue"/>), or any 1.1 template by its own; null when there is
    /// no such template or its model is not there. It carries <paramref name="weapon"/> (a 1.1 weapon template) or,
    /// when none is given, its template's own <c>ACTOR_WEAPON</c>: the weapon's carry class picks its stand, walk and run
    /// (<c>stand_onehand</c>, …, where the set has them), its attacks, block, hurt and deaths fill the action
    /// channel (<see cref="OneOneActions"/>), and its model is held.
    /// </summary>
    public OneOneCharacter? Character(string name, string? weapon = null)
    {
        var entry = OneOneCatalogue.Find(name) ?? new OneOneCatalogueEntry(name, name, name);
        var tags = Templates.Resolve(entry.Template);
        if (!tags.TryGetValue(OneOneTemplates.Model, out var modelTag)) return null;
        var model = Model(modelTag.AsString());
        if (model is null) return null;
        var set = tags.TryGetValue(OneOneTemplates.MotionSet, out var setTag) ? MotionSet(setTag.AsString()) : null;
        set ??= new NmsMotionSet { Name = "" };
        float scale = tags.TryGetValue(OneOneTemplates.ModelScale, out var sc) && sc.AsFloat() > 0f ? sc.AsFloat() : 1f;
        int skeletonId = tags.TryGetValue(OneOneTemplates.SkeletonId, out var sk) ? (int)sk.AsUInt() : 0;
        string weaponName = weapon ?? (tags.TryGetValue(OneOneTemplates.ActorWeapon, out var aw) ? aw.AsString() : "");
        var carried = OneOneWeapon.From(Templates, weaponName);

        int Tag(int index) => set.Tags.FirstOrDefault(t => t.Index == index)?.Bone ?? -1;
        int left = Tag(5), right = Tag(9), head = Tag(19);   // LFoot, RFoot, Head (Vitomir.Formats NmsMotionSet.TagNames)
        int leftHand = Tag(30), rightHand = Tag(25);          // LFingers, RFingers

        var roles = new OneOneRole[Enum.GetValues<MotionRole>().Length];
        Array.Fill(roles, OneOneRole.None);
        var (stands, walks, runs) = OneOneActions.Locomotion(carried?.Carry ?? 0);    // the carry class's first
        roles[(int)MotionRole.Idle] = RoleOf(set, stands, model, left, right, null, measure: false);
        roles[(int)MotionRole.Walk] = RoleOf(set, walks, model, left, right, null, measure: true);
        roles[(int)MotionRole.Run] = RoleOf(set, runs, model, left, right, null, measure: true);
        if (entry.Land is { } land) roles[(int)MotionRole.Land] = RoleOf(set, [land], model, left, right, entry.LandFromTag, measure: false);
        // The dodge's slot — the set's own dodge for the carry class (Christof's: dodge_onehand armed, dodge bare-handed;
        // no other of 1.1's 214 sets has either), else, for a person on 1.1's skeleton 0 (its people share its clips: 1.1's
        // own ST_rudolfGuard_DA plays the player's set), Christof's own motion when every bone it moves is the person's.
        bool bare = (carried?.Carry ?? 0) == 0;
        roles[(int)MotionRole.Dodge] = RoleOf(set, bare ? ["dodge", "dodge_onehand"] : ["dodge_onehand", "dodge"], model, left, right, null, measure: false);
        if (roles[(int)MotionRole.Dodge].Clip is null && skeletonId == 0 && Clip(bare ? ChrisDodgeFists : ChrisDodgeSword, loops: false) is { } dodge
            && dodge.Animation.Tracks.All(t => t.Bone < (uint)model.Skeleton.Count))
            roles[(int)MotionRole.Dodge] = new OneOneRole(bare ? "dodge" : "dodge_onehand", dodge, 0f, 0f, false, false);

        // The limbs for IK, and the moves' stand-ins made of this character's own poses (from the plain stand).
        var stand = roles[(int)MotionRole.Idle].Clip;
        var plainStand = Motion(set, NmsMotionSet.Idle)?.Clip ?? stand;
        var limbs = LimbMap.FromTips(model.Skeleton, left, right, leftHand, rightHand, plainStand);
        IReadOnlyList<(MotionRole, ComposedClip)> compositions = plainStand is null || limbs is null ? []
            : [.. OneOneCompositions.For(entry.Name, set, model.Skeleton, limbs, head, (s, mode) => Motion(s, mode)?.Clip, plainStand)];

        // The held model (its things fitted from its vertices), and the action channel's roles for the weapon.
        var held = new List<HeldModel>();
        if (carried?.HeldName(skeletonId, modelTag.AsString()) is { Length: > 0 } heldName && Model(heldName) is { } heldModel)
            held.Add(HeldModel.Fit(carried.Template, heldModel));
        var blade = held.SelectMany(h => h.Things).FirstOrDefault(t => t.Kind == HeldKind.Blade);
        var actions = OneOneActions.For(set, model.Skeleton, head, limbs, carried, m => Clip(m.File, !m.Has(NmsFlags.NoLoop)), weaponTag: Tag(26), blade: blade);
        // The overhead and the thrust, stitched from Christof's own clips for a one-handed blade, the off hand from this kind's stand.
        // The side blows flattened (played in place of 1.1's unless the session keeps them: swings-oneone).
        // The composed blows' hips held upright, so a blow struck while the body moves keeps its turn and bow (StitchHips).
        var hips = HipsUpright && stand is not null ? StitchHips.Of(model.Skeleton, stand) : null;
        var flatSwings = actions.Quick.Count > 0 ? OneOneStitched.Flat(model.Skeleton, limbs, carried, stand, blade, actions.Quick.Select(q => q.Clip), hips) : [];
        var stitched = actions.Quick.Count > 0 ? OneOneStitched.For(model.Skeleton, limbs, carried, stand, n => Clip(n, loops: false), actions.Spine.Length > 0 ? actions.Spine[0] : -1, blade, head) : [];
        // The four directions' blows with their wind-ups into the ready poses (the shapes the session read from the module's data).
        var windUps = actions.Quick.Count > 0 ? OneOneWindUps.For(model.Skeleton, limbs, carried, stand, blade, actions.Spine.Length > 0 ? actions.Spine[0] : -1, actions.Quick, flatSwings, stitched, WindUpShapes, hips) : [];
        // A kind with wind-ups but no left (1.1's guards and watchmen: the forehand alone) is lent Christof's backhand.
        var lentLeft = windUps.Count > 0 && !windUps.Any(w => w.Direction == "left") && Clip(OneOneStitched.LentBackhand, loops: false) is { } backhand
            ? OneOneWindUps.LentLeft(model.Skeleton, limbs, carried, stand, blade, actions.Spine.Length > 0 ? actions.Spine[0] : -1, backhand, WindUpShapes, hips) : null;
        var textures = TexturesOf(model).ToList();
        foreach (var h in held)
            foreach (var t in TexturesOf(h.Model))
                if (!textures.Exists(x => x.Name == t.Name)) textures.Add(t);

        return new OneOneCharacter
        {
            Name = entry.Name, Template = entry.Template, Model = model, Textures = textures, MotionSet = set,
            Roles = roles, HeadBone = head, Scale = scale, Limbs = limbs, Compositions = compositions,
            SkeletonId = skeletonId, Weapon = carried, Actions = actions, Held = held, Stitched = stitched, Flat = flatSwings, WindUps = windUps, LentLeft = lentLeft,
        };
    }

    /// <summary>Christof's dodges (<c>Christof.nms</c>'s modes 169 and 170), lent to people on skeleton 0 whose sets have none.</summary>
    public const string ChrisDodgeFists = "ChrisDodgeFists", ChrisDodgeSword = "ChrisDodgeSword";

    OneOneRole RoleOf(NmsMotionSet set, string[] modes, SkinnedModel model, int left, int right, uint? fromTag, bool measure)
    {
        foreach (var name in modes)
        {
            if (Motion(set, name) is not { } found) continue;
            var clip = found.Clip;
            float start = 0f;
            if (fromTag is { } tag && clip.Tags.FirstOrDefault(t => t.Type == tag) is { } mark && mark.Type == tag)
                start = mark.Frame / NadClip.FramesPerSecond;
            // The stride from the footfall marks, as the old engine measures it; without them (two of the
            // catalogue's walks), from the feet's sweep (ours, Stride.Sweep). Only walks and runs use it.
            float natural = 0f;
            if (measure && clip.Loops && left >= 0 && right >= 0
                && ((Footfalls(clip) is { Length: >= 2 } plants ? Stride.Measure(clip, model.Skeleton, left, right, plants) : null)
                    ?? Stride.Sweep(clip, model.Skeleton, left, right)) is { } stride)
                natural = Stride.NaturalSpeed(stride, clip.Duration);
            return new OneOneRole(found.Mode.Name, clip, natural, start, found.Mode.Has(NmsFlags.Variable), found.Mode.Has(NmsFlags.NoFadeIn));
        }
        return OneOneRole.None;
    }

    /// <summary>A clip's footfalls (NAD tags 0 to 3: Lwalk, Rwalk, Lrun, Rrun; 0 and 2 the left foot), in time order.</summary>
    static Footfall[] Footfalls(NadClip clip) =>
        [.. clip.Tags.Where(t => t.Type <= 3).OrderBy(t => t.Frame).Select(t => new Footfall(t.Frame / NadClip.FramesPerSecond, t.Type is 0 or 2))];

    /// <summary>
    /// Hands a loaded character to the simulation's <see cref="CharacterAssets"/>: its model, its clips and a
    /// kind that names them, with its head for first person. Answers the kind's index. Call between ticks.
    /// </summary>
    public static int Register(CharacterAssets assets, OneOneCharacter character) => Register(assets, character, null);

    /// <summary>
    /// <see cref="Register(CharacterAssets, OneOneCharacter, string?)"/> with the kind's side blows flattened
    /// (<paramref name="flatSwings"/>; the game's and the arena's default, the switch <c>swings-oneone</c> keeps 1.1's) — each flattened
    /// clip registered after every other of the kind's and played in 1.1's blow's place (its rate, mark and busy time). Without it
    /// nothing of the flattened is registered: 1.1's side blows and 1.1's clip numbers exactly.
    /// </summary>
    /// With them the kind's blows with their wind-ups (<see cref="OneOneCharacter.WindUps"/>) in place of the earlier build's stitched blows: the
    /// directions' table finds them as <c>stitched:left</c>, <c>stitched:right</c>, <c>stitched:overhead</c>, <c>stitched:thrust</c>.
    public static int Register(CharacterAssets assets, OneOneCharacter character, string? stitchedAsQuick, bool flatSwings) =>
        Register(assets, character, stitchedAsQuick, flatSwings, lentLeft: true);

    /// <summary>
    /// <see cref="Register(CharacterAssets, OneOneCharacter, string?, bool)"/> with the kind's lent left
    /// (<see cref="OneOneCharacter.LentLeft"/>: the guards' and the watchmen's blow from the left, Christof's flattened backhand with its
    /// wind-up) first among its wind-ups when <paramref name="lentLeft"/> (the default; the switch <c>guards-one-blow</c> keeps the earlier build's three).
    /// </summary>
    public static int Register(CharacterAssets assets, OneOneCharacter character, string? stitchedAsQuick, bool flatSwings, bool lentLeft) =>
        Register(assets, character, stitchedAsQuick, flatSwings ? character.Flat : (IReadOnlyList<(string, StitchedClip)>)[],
                 !flatSwings || character.WindUps.Count == 0 ? character.Stitched
                 : lentLeft && character.LentLeft is { } left ? [left, .. character.WindUps] : character.WindUps);

    /// <summary>
    /// <see cref="Register(CharacterAssets, OneOneCharacter)"/>: with <paramref name="stitchedAsQuick"/> the kind's stitched
    /// blows played as its quick blows (<c>--stitched-as-quick</c>: <c>overhead</c>, <c>thrust</c> or <c>both</c>; for measuring and
    /// filming them).
    /// </summary>
    public static int Register(CharacterAssets assets, OneOneCharacter character, string? stitchedAsQuick) =>
        Register(assets, character, stitchedAsQuick, (IReadOnlyList<(string, StitchedClip)>)[], character.Stitched);

    static int Register(CharacterAssets assets, OneOneCharacter character, string? stitchedAsQuick, IReadOnlyList<(string From, StitchedClip Clip)> flat,
                        IReadOnlyList<OneOneStitchedBlow> stitched)
    {
        int model = assets.AddModel(character.Model);
        var motions = new RoleMotion[character.Roles.Length];
        var added = new Dictionary<IAnimationClip, int>(ReferenceEqualityComparer.Instance);
        for (int r = 0; r < motions.Length; r++)
        {
            var role = character.Roles[r];
            if (role.Clip is null) { motions[r] = RoleMotion.None; continue; }
            if (!added.TryGetValue(role.Clip, out int clip)) added[role.Clip] = clip = assets.AddClip(role.Clip);
            motions[r] = new RoleMotion(clip, role.NaturalSpeed, role.StartSeconds, role.Variable, role.NoFadeIn);
        }
        // An empty slot plays its composition; a crouched walk is the walk, lowered by the rig.
        foreach (var (role, composed) in character.Compositions)
            if (!motions[(int)role].Has) motions[(int)role] = new RoleMotion(assets.AddClip(composed), 0f, 0f, false, Composed: true);
        if (!motions[(int)MotionRole.CrouchWalk].Has && motions[(int)MotionRole.Walk] is { Has: true } walk)
            motions[(int)MotionRole.CrouchWalk] = walk with { Variable = false, Composed = true };
        return assets.AddKind(new CharacterKind
        {
            Name = character.Name, Model = model, Motions = motions, FacingYaw = OneOneModels.FacingYaw, Scale = character.Scale,
            Head = character.HeadBone >= 0 ? HeadCollapse.For(character.Model.Skeleton, character.HeadBone, character.Model.Vertices, OneOneModels.FacingYaw) : null,   // the neck and the eyes
            Limbs = character.Limbs,
            Actions = character.Actions is { } actions ? OneOneActions.Register(assets, actions, added, stitched, stitchedAsQuick, flat) : null,     // the stitched blows, and with their wind-ups
            Held = character.Held,
        });
    }
}
