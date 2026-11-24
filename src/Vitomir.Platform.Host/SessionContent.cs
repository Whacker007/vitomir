// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Host.Arena;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Perception;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Host;

/// <summary>
/// What a session loads before it builds its world (moved out of <see cref="Session.Create"/>): the 1.1 install, the
/// game's modules loaded and configured, the level's space and areas, 1.1's characters registered (the catalogue's — Christof
/// with the player's weapon —, the townsfolk's, then the arena's cast's, each template with its weapon), their textures and
/// 1.1's cursors. Nothing in it changes while a world runs, so the arena loads it once and builds a fresh world on it for
/// each bout (<see cref="SessionOptions.Content"/>); the order of the characters, and so every kind's index, depends only on
/// the options, so a tape replayed in a fresh program builds the same world.
/// </summary>
public sealed class SessionContent : IDisposable
{
    public required GameInstall? Install { get; init; }
    public required LoadedGame Modules { get; init; }
    public required LevelSpace Level { get; init; }
    public required LevelAreas Areas { get; init; }
    public required CharacterAssets Assets { get; init; }
    public required IReadOnlyDictionary<string, TextureImage> Textures { get; init; }
    /// <summary>Each character's kind by name: the catalogue's and the townsfolk's by their own, the arena's cast's by <see cref="ArenaCast.KindKey"/>.</summary>
    public required IReadOnlyDictionary<string, int> Kinds { get; init; }
    public required OneOneCursors Cursors { get; init; }
    /// <summary>1.1's creatures the cast and the foes may ask for (their templates' speeds and AI files, read from the install), by template.</summary>
    public IReadOnlyDictionary<string, OneOneCreature?> Creatures { get; init; } = new Dictionary<string, OneOneCreature?>();

    /// <summary>1.1's arrow model (read from the install when a bow is carried), or null.</summary>
    public SkinnedModel? ArrowModel { get; init; }

    /// <summary>Each bow kind's bone that 1.1's held bow model's own arrow rides (hidden while nothing is on the string).</summary>
    public IReadOnlyDictionary<int, int> BowArrowBones { get; init; } = new Dictionary<int, int>();

    // ---- ----
    /// <summary>
    /// The player's body built from the models repository's files (<see cref="SessionOptions.Body"/>), or null
    /// for 1.1's Christof (none asked, or it could not be built: <see cref="PlayerBodyWhy"/>).
    /// </summary>
    public PeopleBodies.Built? PlayerBody { get; init; }

    /// <summary>Why the body asked for was not built (said once at the start), or null.</summary>
    public string? PlayerBodyWhy { get; init; }
    // ---- end

    /// <summary>
    /// The townsfolk who do not fight built on the models repository's crowd (<see cref="SessionOptions.TownsfolkBody"/>), each
    /// place's kind; null when they are 1.1's (none asked, the arena, no townsfolk).
    /// </summary>
    public TownsfolkBodies.Built? Townsfolk { get; init; }

    /// <summary>The level, the game, the switches, the weapon and the cast it was loaded for (a session asking for others loads its own).</summary>
    public required string LoadedFor { get; init; }

    /// <summary>What decides the content: the options it is loaded from (not the seed, the place or the sheets).</summary>
    public static string KeyOf(SessionOptions o) =>
        string.Join("\n", o.Level, o.Game ?? "", o.GameList ?? "", o.ModulesFolder ?? "", string.Join(",", o.Switches.Order(StringComparer.Ordinal)),
                    o.Weapon ?? "", ArenaCast.KindsKey(o.Arena), o.StitchedAsQuick ?? "",   // the stitched blows as the quick
                    string.Join(";", o.Foes),   // the foes' characters
                    o.DataOverrides ?? "",      // the modules' data overrides
                    PeopleBodies.RecipeOf(o.Body) is null || o.Arena is not null ? "" : o.Body + "@" + (o.ModelsFolder ?? "") + (o.DecodePeopleImages ? "+maps" : "") + (o.PeopleTextureLimit > 0 ? $"<={o.PeopleTextureLimit}" : ""),   // the player's body; its maps' cap
                    CrowdWanted(o) ? $"townsfolk {o.People} {(o.Night ? "night" : "day")}@{o.ModelsFolder ?? ""}{(o.DecodePeopleImages ? "+maps" : "")}<={o.PeopleTextureLimit}{(o.CrowdBuilt ? " built" : "")}" : "");   // --crowd-built

    /// <summary>Whether these options draw the townsfolk on the models repository's crowd (asked, not an arena, some townsfolk).</summary>
    public static bool CrowdWanted(SessionOptions o) => o.TownsfolkBody == TownsfolkBodies.Models && o.Arena is null && o.People > 0;

    public void Dispose() => Install?.Dispose();
}

public sealed partial class Session
{
    /// <summary>
    /// Ce-motions: the four directions' wind-ups and ready poses from the modules' data (<see cref="OneOneWindUps.Asset"/>, the last
    /// module in load order that has it), or null for the platform's own (<see cref="OneOneWindUps.Defaults"/>); what is wrong in it is said.
    /// </summary>
    static IReadOnlyList<WindUpShape>? WindUpShapes(LoadedGame modules, Action<string> say)
    {
        IReadOnlyList<WindUpShape>? shapes = null;
        foreach (var m in modules.Modules)
        {
            var id = AssetId.Of(m.Manifest.Id, OneOneWindUps.Asset);
            if (!modules.Assets.TryResolve(id, out _)) continue;
            var problems = new List<string>();
            shapes = OneOneWindUps.Read(System.Text.Encoding.UTF8.GetString(modules.Assets.Read(id)).TrimStart('\uFEFF'), id.ToString(), problems);
            foreach (var p in problems) say($"wind-ups: {p}");
        }
        return shapes;
    }

    /// <summary>
    /// Loads what a session needs before its world (was the first half of <see cref="Create"/>): the modules first — a
    /// refusal stops everything before a window opens —, the level, 1.1's characters from the player's own install (never
    /// shipped). Says what it loaded.
    /// </summary>
    public static SessionContent LoadContent(SessionOptions options)
    {
        var say = options.Say;
        var install = GameInstall.TryOpen(options.Game);
        LoadedGame modules;
        try { modules = ModulesCommand.Load(options, install); }
        catch { install?.Dispose(); throw; }
        foreach (var code in modules.Code)
        {
            var stamp = code.Stamp;
            say($"rules: {stamp.ModuleId} profile {stamp.ProfileId}, formulae {stamp.FormulaVersion}, config {stamp.ConfigHash[..12]}{(stamp.NonshippingResearch ? ", research sandbox (not shipping numbers)" : "")}");
            if (code.Banner is { Length: > 0 } banner) say($"rules: {banner}");   // the module's warnings (the double count)
        }

        LevelSpace level;
        try { level = BuiltInGrounds.Is(options.Level) ? BuiltInGrounds.Open(options.Level) : LevelSpace.Open(options.Level); }   // a ground built in code
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            install?.Dispose();
            throw new SessionException($"The level {options.Level} could not be read: {e.Message}");
        }
        say($"level: {level.Name}, {level.Collision.Triangles:N0} triangles, {level.Navigation?.Polygons ?? 0:N0} navigation polygons, {level.Starts.Count()} starts");
        say(level.Exits.Count == 0 ? "exits: none in the package"
            : $"exits: {level.Exits.Count}, {level.Exits.WalkIn} with a volume the player is stopped at ({string.Join(", ", Enumerable.Range(0, level.Exits.Count).Select(i => level.Exits[i]).Select(e => $"{e.Number}{(e.Volume is null ? "" : " walk-in")}"))})");

        // 1.1's characters, read from the player's own install (never shipped).
        var assets = new CharacterAssets { CarryActionHips = !options.Switches.Contains(ActionHips.Switch),   // On by default: blows-on-moving-hips turns it off
                                           PersonWaistEven = options.PersonTurn.WaistEven,
                                           PersonSkirtSplit = options.SkirtSplit,   // the skirt hung from the thighs (--skirt-split) // an earlier build: the people's waist (--turn-waist-even, a tape's note)
                                           PersonSwing = options.PersonSwing };   // the swing bones' simulator (--swing-off, --swing-near, --swing-far)
        var textures = new Dictionary<string, TextureImage>(StringComparer.OrdinalIgnoreCase);
        var kinds = new Dictionary<string, int>(StringComparer.Ordinal);
        Dictionary<string, OneOneCreature?> creatures = [];
        SkinnedModel? arrow = null;
        var arrowBones = new Dictionary<int, int>();
        if (install is null) say("characters: no 1.1 install found (--game, VTMR_GAME_DIR or C:\\Games\\VTMR); bodies are not drawn");
        else
        {
            var library = new OneOneLibrary(install) { WindUpShapes = WindUpShapes(modules, say),   // the wind-ups' data
                HipsUpright = options.Switches.Contains(StitchHips.Switch) };               // the blows' hips held upright with blow-hips-upright
            // The player's weapon, asked for or Christof's starting sword; a name that is no weapon stops the start.
            string weapon = options.Weapon ?? SessionOptions.DefaultWeapon;
            if (OneOneWeapon.From(library.Templates, weapon) is null)
            {
                install.Dispose();
                throw new SessionException($"There is no 1.1 weapon template '{weapon}' (try BroadSword, Rapier, Halberd, GreatSword, Punch).");
            }
            foreach (var entry in OneOneCatalogue.Characters)
            {
                var c = library.Character(entry.Name, entry.Name == "Christof" ? weapon : null);
                if (c is null) { say($"characters: {entry.Name} could not be read from {install.Root}"); continue; }
                kinds[entry.Name] = OneOneLibrary.Register(assets, c, options.StitchedAsQuick, !options.Switches.Contains(OneOneStitched.SwingsOneOneSwitch),
                    !options.Switches.Contains(OneOneWindUps.GuardsOneBlowSwitch));   // the side blows flattened unless swings-oneone; the guards' left unless guards-one-blow
                foreach (var t in c.Textures) textures.TryAdd(t.Name, t);
                if (entry.Name == "Christof") say(ActionsLine(c));
            }
            // The townsfolk of the day and the night and the keepers of the peace, 1.1's templates by their own names.
            foreach (var name in Townsfolk.Templates)
            {
                if (kinds.ContainsKey(name)) continue;
                var c = library.Character(name);
                if (c is null) { say($"characters: {name} could not be read from {install.Root}"); continue; }
                kinds[name] = OneOneLibrary.Register(assets, c, options.StitchedAsQuick, !options.Switches.Contains(OneOneStitched.SwingsOneOneSwitch),
                    !options.Switches.Contains(OneOneWindUps.GuardsOneBlowSwitch));   // the side blows flattened unless swings-oneone; the guards' left unless guards-one-blow
                foreach (var t in c.Textures) textures.TryAdd(t.Name, t);
                if (c.Held.Count > 0) say(ActionsLine(c));
            }
            // The arena's cast, each template with its weapon, in the cast's order.
            if (options.Arena is { } castText)
            {
                ArenaCast cast;
                try { cast = ArenaCast.Read(castText); }
                catch (ArenaSpecException e) { install.Dispose(); throw new SessionException(e.Message); }
                foreach (var f in cast.Fighters)
                {
                    string key = ArenaCast.KindKey(f.Template, f.Weapon);
                    if (kinds.ContainsKey(key)) continue;
                    if (f.Weapon is { } w && OneOneWeapon.From(library.Templates, w) is null)
                    {
                        install.Dispose();
                        throw new SessionException($"arena: fighter {f.Name}: there is no 1.1 weapon template '{w}' (try BroadSword, Rapier, Halberd, GreatSword, Punch).");
                    }
                    var c = library.Character(f.Template, f.Weapon);
                    if (c is null)
                    {
                        install.Dispose();
                        throw new SessionException($"arena: fighter {f.Name}: there is no 1.1 character '{f.Template}' (try Christof, rudolfGuard, KnightOfStJohn_torch).");
                    }
                    kinds[key] = OneOneLibrary.Register(assets, c, options.StitchedAsQuick, !options.Switches.Contains(OneOneStitched.SwingsOneOneSwitch),
                    !options.Switches.Contains(OneOneWindUps.GuardsOneBlowSwitch));   // the side blows flattened unless swings-oneone; the guards' left unless guards-one-blow
                    foreach (var t in c.Textures) textures.TryAdd(t.Name, t);
                    say(ActionsLine(c));
                }
                RegisterClipRates(assets, kinds, cast, say);   // a fighter's clips at their own rates, a kind of its own
            }
            RegisterFoes(options, modules, library, assets, kinds, textures, install, say);   // --foe's characters
            creatures = ReadCreatures(options, modules, library, install);                   // 1.1's creatures' speeds and AI files
            arrow = BowLooks.Load(options, library, assets, kinds, textures, say, Session.FoeArcherTemplates(options, modules), arrowBones);   // the bow's 1.1 look and the arrow's model; the archer foes' too
            say($"characters: {kinds.Count} of 1.1's read from {install.Root}");
        }
        // The player's body from the models repository's files, after every 1.1 kind (theirs keep their
        // indices); not in the arena (its fighters are 1.1's). Missing files or loader: 1.1's Christof, and one line says why.
        PeopleBodies.Built? body = null;
        string? bodyWhy = null;
        if (PeopleBodies.RecipeOf(options.Body) is not null && options.Arena is null)
        {
            string folder = options.ModelsFolder ?? PeopleBodies.FindFolder(null, null) ?? "";
            body = PeopleBodies.TryBuild(options.Body!, folder.Length > 0 ? folder : null, assets, textures, out string why, options.DecodePeopleImages, options.PeopleTextureLimit);
            if (body is null)
            {
                bodyWhy = why;
                if (options.BodyRequired)
                {
                    install?.Dispose();
                    throw new SessionException($"people: the recording's player is the models repository's {options.Body}, which cannot be built here: {why}");
                }
                say($"people: the player is 1.1's Christof: {why}");
            }
            else say(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"people: the models folder {body.Folder} (commit {body.Commit ?? "unknown"}); the player is the models repository's {body.Person.Recipe.Name} ({body.Person.Recipe.Key}), loaded in {body.Milliseconds:0} ms; the new body has no combat yet (--body oneone for combat)"));
        }
        // The townsfolk who do not fight on the models repository's crowd, after the player's body (sharing its maps).
        TownsfolkBodies.Built? crowd = null;
        if (SessionContent.CrowdWanted(options))
        {
            string? folder = body?.Folder ?? options.ModelsFolder ?? PeopleBodies.FindFolder(null, null);
            crowd = TownsfolkBodies.Build(Townsfolk.ForSlots(options.Night, options.People), body?.Library, folder, assets, textures, options.DecodePeopleImages, options.PeopleTextureLimit,
                                          crowdBuilt: options.CrowdBuilt);
            if (!crowd.Any)
            {
                if (options.TownsfolkRequired)
                {
                    install?.Dispose();
                    throw new SessionException($"people: the recording's townsfolk are the models repository's crowd, which cannot be built here: {crowd.Why}");
                }
                say($"people: the townsfolk are 1.1's: {crowd.Why}");
                crowd = null;
            }
            else
            {
                say(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"people: {crowd.Kinds.Count(k => k >= 0)} of {crowd.Kinds.Length} townsfolk drawn as the models repository's crowd, {crowd.BakedPlaces} as baked ({crowd.Kinds.Where(k => k >= 0).Distinct().Count()} kinds, in {crowd.Milliseconds:0} ms; the guards and the watch stay 1.1's; --townsfolk-oneone for 1.1's, --crowd-built for the run-time crowd)"));
                foreach (var line in crowd.Lines) say("people:   " + line);
            }
        }
        var cursors = OneOneCursors.From(install);
        return new SessionContent
        {
            Townsfolk = crowd,
            PlayerBody = body, PlayerBodyWhy = bodyWhy,
            Install = install,
            Modules = modules,
            Level = level,
            Areas = LevelAreas.Open(options.Level),
            Assets = assets,
            Textures = textures,
            Kinds = kinds,
            Cursors = cursors,
            Creatures = creatures,
            ArrowModel = arrow,
            BowArrowBones = arrowBones,
            LoadedFor = SessionContent.KeyOf(options),
        };
    }
}
