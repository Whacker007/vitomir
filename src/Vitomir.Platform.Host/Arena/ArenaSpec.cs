// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Text.Json;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;

namespace Vitomir.Platform.Host.Arena;

/// <summary>What is wrong with an arena spec, by name.</summary>
public enum ArenaSpecError
{
    /// <summary>The spec file is missing or unreadable.</summary>
    FileMissing,
    /// <summary>The spec is not JSON, or not a JSON object.</summary>
    NotJson,
    /// <summary>A field the spec does not know (a typo is not passed over).</summary>
    UnknownField,
    /// <summary>A field of the wrong kind (a number where text is wanted, …).</summary>
    WrongType,
    /// <summary>A number outside what it may be.</summary>
    OutOfRange,
    /// <summary>No level: none in the spec, no <c>--level</c>, no <c>VITOMIR_COURSE_LEVEL</c>.</summary>
    NoLevel,
    /// <summary>The level named is not a level package (no <c>level.json</c>).</summary>
    LevelNotFound,
    /// <summary>Fewer than two fighters.</summary>
    TooFewFighters,
    /// <summary>A fighter's name is empty, repeated, or holds one of <c>, : @ ^ +</c>.</summary>
    BadName,
    /// <summary>A place that is not two or three numbers.</summary>
    BadPlace,
    /// <summary>A brain that is not <c>duel</c>, <c>dummy</c> (standing or blocking) or <c>script</c>, or numbers it does not take.</summary>
    BadBrain,
    /// <summary>A script that <c>--npc-actions</c>'s grammar does not read.</summary>
    BadScript,
    /// <summary>A sheet that is not a template, a file and values.</summary>
    BadSheet,
    /// <summary>A sheet file that is not there.</summary>
    SheetFileMissing,
    /// <summary>A foe that is no other fighter.</summary>
    UnknownFoe,
    /// <summary>A sweep without a fighter, without exactly one of trait or power, or with no values.</summary>
    BadSweep,
    /// <summary>A sweep's fighter is no fighter of the spec.</summary>
    SweepUnknownFighter,
    /// <summary>More than two sweeps (a grid of two is the most).</summary>
    TooManySweeps,
    /// <summary>A bout's cast (a tape's head) that is not readable.</summary>
    CastUnreadable,
    /// <summary>
    /// A power that is not a rank or <c>{ "rank": n, "options": [ids] }</c>, an option that is not an id, or a power
    /// at a nonzero rank given no option (a rank learns none by itself, so it would do nothing: refused by name).
    /// </summary>
    BadPower,
    /// <summary><c>slots</c> that are not a list of quick slots, 1 to 8, each once.</summary>
    BadSlots,
    /// <summary>A <c>side</c> that is not a name, given to some fighters and not others, all on one side, or a foe named on the fighter's own side.</summary>
    BadSide,
}

/// <summary>An arena spec refused: the error by name and the reason in words.</summary>
public sealed class ArenaSpecException(ArenaSpecError error, string message) : Exception(message)
{
    public ArenaSpecError Error { get; } = error;
}

/// <summary>A fighter as the spec gives it, before a bout's swept values.</summary>
public sealed record SpecFighter(string Name, string Template, string? Weapon, float X, float Y, float Z, float Facing, CastBrain Brain,
    DuelBrainSettings? Numbers, string? Script, int Foe, CastSheet Sheet, int? Blood = null, List<int>? Slots = null, bool? Shield = null,
    List<string>? Armor = null,   // the armour it wears (the module's pieces), null for none
    string? Side = null,          // its side (one against several), null for none
    string? Movement = null);     // how it moves (human, neonate, elder, template), null for the human's

/// <summary>
/// One sweep: a fighter's trait (an Attribute or an Ability) or a power's rank over a list of values; a power's
/// options learned with each nonzero rank (null: the rank alone, keeping the options the sheet has).
/// </summary>
public sealed record ArenaSweep(int Fighter, string Id, bool Power, IReadOnlyList<int> Values, List<string>? Options = null)
{
    /// <summary>The column's name: <c>A.strength</c>, <c>B.power.potence</c>.</summary>
    public string Label(ArenaSpec spec) => $"{spec.Fighters[Fighter].Name}.{(Power ? "power." : "")}{Id}";
}

/// <summary>
/// An arena spec: the level, two or more fighters (each a 1.1 character with a weapon, a
/// sheet, a place, a facing, a brain and a foe), the bouts per swept value, the seed base, the most seconds a bout runs,
/// the tapes kept per value, and up to two sweeps (one a line, two a grid). Read from JSON with every error named
/// (<see cref="ArenaSpecError"/>).
/// </summary>
public sealed record ArenaSpec
{
    public required string Level { get; init; }
    public string Title { get; init; } = "";
    public bool Night { get; init; } = true;
    public required IReadOnlyList<SpecFighter> Fighters { get; init; }
    public int Bouts { get; init; } = 10;
    public ulong Seed { get; init; } = 1;
    public float Seconds { get; init; } = 60f;
    public int Tapes { get; init; } = 2;
    public IReadOnlyList<ArenaSweep> Sweeps { get; init; } = [];

    /// <summary>
    /// The game's switches every bout runs with (<c>"switches": ["skill-contest", "skill-tempo-off"]</c>), as if given on
    /// the command line (without the dashes), joined to the command line's own; carried in each tape's head with the rest.
    /// </summary>
    public IReadOnlyList<string> Switches { get; init; } = [];

    /// <summary>
    /// The global combat tempo every bout plays (<c>"combat_tempo": 0.6</c>; the program's
    /// <see cref="CombatTempoOverride"/>, as <c>--combat-tempo</c>, which is given over it), or null for the rules module's own.
    /// </summary>
    public float? CombatTempo { get; init; }
    /// <summary>The sides' names in the order the fighters first name them (empty without sides): <see cref="BoutResult.SideWon"/> indexes it.</summary>
    public IReadOnlyList<string> SideNames => [.. Fighters.Where(f => f.Side is not null).Select(f => f.Side!).Distinct(StringComparer.Ordinal)];

    /// <summary>The spec's text as given (for the summary's head).</summary>
    public string Text { get; init; } = "";

    /// <summary>The proving ground's free yard (x 14 to 56, y −15 to 21; a free place): fighters with no place stand round (40, 3) on its open floor, 2 m out, facing in.</summary>
    public static readonly (float X, float Y, float Z, float Radius) Yard = (40f, 3f, 0.02f, 2f);

    /// <summary>
    /// The swept points: every value of the first sweep (with every value of the second, a grid), as the values in sweep
    /// order; one point with no values when nothing is swept.
    /// </summary>
    public IReadOnlyList<int[]> Points
    {
        get
        {
            if (Sweeps.Count == 0) return [[]];
            var list = new List<int[]>();
            foreach (int a in Sweeps[0].Values)
            {
                if (Sweeps.Count == 1) list.Add([a]);
                else foreach (int b in Sweeps[1].Values) list.Add([a, b]);
            }
            return list;
        }
    }

    /// <summary>The cast of a bout at a swept point: each fighter as given, the swept values set last on its sheet.</summary>
    public ArenaCast Cast(int[] point)
    {
        var fighters = new List<CastFighter>(Fighters.Count);
        for (int i = 0; i < Fighters.Count; i++)
        {
            var f = Fighters[i];
            var values = new List<CastValue>(f.Sheet.Values);
            for (int s = 0; s < Sweeps.Count; s++)
                if (Sweeps[s].Fighter == i) values.Add(new CastValue(Sweeps[s].Id, point[s], Sweeps[s].Power, Sweeps[s].Options));
            fighters.Add(new CastFighter
            {
                Name = f.Name, Template = f.Template, Weapon = f.Weapon, X = f.X, Y = f.Y, Z = f.Z, Facing = f.Facing,
                Brain = f.Brain, Numbers = f.Numbers, Script = f.Script, Foe = f.Foe,
                Sheet = f.Sheet with { Values = values },
                Blood = f.Blood, Slots = f.Slots, Shield = f.Shield,
                Armor = f.Armor,
                Side = f.Side,
                Movement = f.Movement,
            });
        }
        return new ArenaCast { Fighters = fighters };
    }

    // ---- reading ----

    /// <summary>Reads a spec file; relative paths in it (the level, sheet files) are from its folder. <paramref name="level"/> is <c>--level</c>, if given.</summary>
    public static ArenaSpec ReadFile(string path, string? level = null)
    {
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ArenaSpecException(ArenaSpecError.FileMissing, $"arena: the spec {path} could not be read ({e.Message})");
        }
        return Read(text, Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".", level);
    }

    static readonly string[] TopFields = ["title", "level", "time", "fighters", "bouts", "seed", "seconds", "tapes", "sweep", "sweeps", "switches", "combat_tempo"];
    static readonly string[] FighterFields = ["name", "template", "weapon", "sheet", "place", "facing", "brain", "foe", "blood", "slots", "shield", "armor", "side", "movement"];   // armor; side; movement
    static readonly string[] SheetFields = ["template", "file", "values", "powers"];
    static readonly string[] SweepFields = ["fighter", "trait", "power", "options", "values", "from", "to", "step"];

    /// <summary>Reads a spec from its text; <paramref name="folder"/> is where relative paths start.</summary>
    public static ArenaSpec Read(string text, string folder, string? level = null)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException e) { throw new ArenaSpecException(ArenaSpecError.NotJson, $"arena: the spec is not JSON ({e.Message})"); }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new ArenaSpecException(ArenaSpecError.NotJson, "arena: the spec is not a JSON object");
            Known(root, TopFields, "the spec");

            // The level: the spec's, else --level, else VITOMIR_COURSE_LEVEL.
            string? lv = Str(root, "level", "the spec") is { } given ? Path.GetFullPath(Path.Combine(folder, given)) : level ?? Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
            if (string.IsNullOrWhiteSpace(lv))
                throw new ArenaSpecException(ArenaSpecError.NoLevel, "arena: no level (give \"level\" in the spec, --level, or VITOMIR_COURSE_LEVEL; the proving ground's free yard is the default ground)");
            lv = Path.GetFullPath(lv.TrimEnd('/', '\\'));
            if (!File.Exists(Path.Combine(lv, "level.json")))
                throw new ArenaSpecException(ArenaSpecError.LevelNotFound, $"arena: {lv} is not a level package (it has no level.json)");

            bool night = Str(root, "time", "the spec") switch
            {
                null or "night" => true,
                "day" => false,
                var t => throw new ArenaSpecException(ArenaSpecError.OutOfRange, $"arena: \"time\" is day or night, not '{t}'"),
            };

            if (!root.TryGetProperty("fighters", out var fs) || fs.ValueKind != JsonValueKind.Array || fs.GetArrayLength() < 2)
                throw new ArenaSpecException(ArenaSpecError.TooFewFighters, "arena: \"fighters\" must list two or more");
            int n = fs.GetArrayLength();
            var names = new List<string>(n);
            int k = 0;
            foreach (var f in fs.EnumerateArray())
            {
                if (f.ValueKind != JsonValueKind.Object) throw new ArenaSpecException(ArenaSpecError.WrongType, $"arena: fighter {k + 1} is not an object");
                string name = Str(f, "name", $"fighter {k + 1}") ?? ((char)('A' + k)).ToString();
                if (name.Length == 0 || name.IndexOfAny([',', ':', '@', '^', '+']) >= 0 || names.Contains(name, StringComparer.OrdinalIgnoreCase))
                    throw new ArenaSpecException(ArenaSpecError.BadName, $"arena: fighter {k + 1}'s name '{name}' is empty, repeated, or holds one of , : @ ^ +");
                names.Add(name);
                k++;
            }
            var fighters = new List<SpecFighter>(n);
            k = 0;
            foreach (var f in fs.EnumerateArray())
            {
                fighters.Add(Fighter(f, k, n, names, folder));
                k++;
            }
            Sides(fs, fighters);

            int bouts = Int(root, "bouts", "the spec", 10, 1, 1_000_000);
            long seed = Long(root, "seed", "the spec", 1, 0, long.MaxValue);
            float seconds = Float(root, "seconds", "the spec", 60f, 0.1f, 3600f);
            int tapes = Int(root, "tapes", "the spec", 2, 0, 1_000_000);
            var switches = new List<string>();
            if (root.TryGetProperty("switches", out var swx))
            {
                if (swx.ValueKind != JsonValueKind.Array) throw new ArenaSpecException(ArenaSpecError.WrongType, "arena: \"switches\" is a list of the game's switches (\"skill-contest\", …)");
                foreach (var x in swx.EnumerateArray())
                {
                    string? name = x.ValueKind == JsonValueKind.String ? x.GetString()?.TrimStart('-') : null;
                    if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsWhiteSpace))
                        throw new ArenaSpecException(ArenaSpecError.WrongType, $"arena: switch {x.GetRawText()} is not a switch's name (\"skill-contest\")");
                    if (!switches.Contains(name)) switches.Add(name);
                }
            }

            // The global combat tempo.
            float? tempo = root.TryGetProperty("combat_tempo", out _) ? Float(root, "combat_tempo", "the spec", 1f, CombatTempoOverride.Least, CombatTempoOverride.Most) : null;

            var sweeps = new List<ArenaSweep>();
            if (root.TryGetProperty("sweep", out var sw) && root.TryGetProperty("sweeps", out _))
                throw new ArenaSpecException(ArenaSpecError.BadSweep, "arena: give \"sweep\" or \"sweeps\", not both");
            if (root.TryGetProperty("sweep", out sw) || root.TryGetProperty("sweeps", out sw))
            {
                if (sw.ValueKind == JsonValueKind.Object) sweeps.Add(Sweep(sw, names));
                else if (sw.ValueKind == JsonValueKind.Array)
                {
                    if (sw.GetArrayLength() > 2) throw new ArenaSpecException(ArenaSpecError.TooManySweeps, $"arena: {sw.GetArrayLength()} sweeps; two (a grid) are the most");
                    foreach (var s in sw.EnumerateArray()) sweeps.Add(Sweep(s, names));
                }
                else throw new ArenaSpecException(ArenaSpecError.WrongType, "arena: \"sweep\" is an object (or a list of two for a grid)");
                if (sweeps.Count == 2 && sweeps[0].Fighter == sweeps[1].Fighter && sweeps[0].Id == sweeps[1].Id && sweeps[0].Power == sweeps[1].Power)
                    throw new ArenaSpecException(ArenaSpecError.BadSweep, "arena: the two sweeps sweep the same value");
            }

            return new ArenaSpec
            {
                Level = lv,
                Title = Str(root, "title", "the spec") ?? "",
                Night = night,
                Fighters = fighters,
                Bouts = bouts,
                Seed = (ulong)seed,
                Seconds = seconds,
                Tapes = tapes,
                Sweeps = sweeps,
                Switches = switches,
                CombatTempo = tempo,
                Text = text,
            };
        }
    }

    static SpecFighter Fighter(JsonElement f, int index, int count, List<string> names, string folder)
    {
        string who = $"fighter {names[index]}";
        Known(f, FighterFields, who);
        string template = Str(f, "template", who) ?? "Christof";
        if (template.Length == 0) throw new ArenaSpecException(ArenaSpecError.WrongType, $"arena: {who}: \"template\" is empty");
        // The weapon: given, else the character's own — but Christof carries his starting sword, as the player does (his
        // template's own is his fists, 1.1's Punch: give "weapon": "Punch" for those).
        string? weapon = Str(f, "weapon", who) ?? (template == "Christof" ? SessionOptions.DefaultWeapon : null);

        // The place: given, else round the free yard's middle, facing in.
        var yard = Yard;
        float angle = DetMathPi * (1f + 2f * index / count);
        float x = yard.X + yard.Radius * Round3((float)Math.Cos(angle));
        float y = yard.Y + yard.Radius * Round3((float)Math.Sin(angle));
        float z = yard.Z;
        if (f.TryGetProperty("place", out var p))
        {
            if (p.ValueKind != JsonValueKind.Array || p.GetArrayLength() is < 2 or > 3 || p.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.Number))
                throw new ArenaSpecException(ArenaSpecError.BadPlace, $"arena: {who}: \"place\" is [x, y] or [x, y, z] (metres)");
            var v = p.EnumerateArray().Select(e => e.GetSingle()).ToArray();
            (x, y, z) = (v[0], v[1], v.Length > 2 ? v[2] : yard.Z);
        }
        // The facing: given (degrees), else toward the middle of the fighters' default ring.
        float facing = Round3((float)(Math.Atan2(yard.Y - y, yard.X - x) * 180.0 / Math.PI));
        if (f.TryGetProperty("facing", out var fa))
        {
            if (fa.ValueKind != JsonValueKind.Number) throw new ArenaSpecException(ArenaSpecError.WrongType, $"arena: {who}: \"facing\" is degrees");
            facing = fa.GetSingle();
        }

        // The brain.
        var brain = CastBrain.Duel;
        DuelBrainSettings? numbers = null;
        string? script = null;
        if (f.TryGetProperty("brain", out var b))
        {
            if (b.ValueKind == JsonValueKind.String) (brain, numbers, script) = (BrainKindOf(b.GetString()!, null, who), null, null);
            else if (b.ValueKind == JsonValueKind.Object) (brain, numbers, script) = Brain(b, who);
            else throw new ArenaSpecException(ArenaSpecError.BadBrain, $"arena: {who}: \"brain\" is duel, dummy or script (or an object with \"kind\")");
        }

        // The foe: named, else the next fighter.
        int foe = (index + 1) % count;
        if (Str(f, "foe", who) is { } foeName)
        {
            foe = names.FindIndex(nm => nm.Equals(foeName, StringComparison.OrdinalIgnoreCase));
            if (foe < 0 || foe == index) throw new ArenaSpecException(ArenaSpecError.UnknownFoe, $"arena: {who}: the foe '{foeName}' is no other fighter (the fighters: {string.Join(", ", names)})");
        }

        // The sheet.
        var sheet = new CastSheet();
        if (f.TryGetProperty("sheet", out var sh))
        {
            if (sh.ValueKind == JsonValueKind.String) sheet = new CastSheet { Template = sh.GetString() };
            else if (sh.ValueKind == JsonValueKind.Object) sheet = Sheet(sh, who, folder);
            else throw new ArenaSpecException(ArenaSpecError.BadSheet, $"arena: {who}: \"sheet\" is a template's id or an object (template, file, values, powers)");
        }
        // Blood at the start, the quick slots the brain may use, and whether it carries a shield.
        int? blood = f.TryGetProperty("blood", out _) ? Int(f, "blood", who, 0, 0, 1_000_000) : null;
        List<int>? slots = null;
        if (f.TryGetProperty("slots", out var sl))
        {
            if (sl.ValueKind != JsonValueKind.Array)
                throw new ArenaSpecException(ArenaSpecError.BadSlots, $"arena: {who}: \"slots\" is a list of the quick slots its brain may use (1 to {Powers.QuickSlots.Count})");
            slots = [];
            foreach (var v in sl.EnumerateArray())
            {
                if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out int slot) || slot < 1 || slot > Powers.QuickSlots.Count || slots.Contains(slot))
                    throw new ArenaSpecException(ArenaSpecError.BadSlots, $"arena: {who}: the slot {v.GetRawText()} is not a quick slot (1 to {Powers.QuickSlots.Count}, each once)");
                slots.Add(slot);
            }
        }
        bool? shield = null;
        if (f.TryGetProperty("shield", out var shv))
        {
            if (shv.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new ArenaSpecException(ArenaSpecError.WrongType, $"arena: {who}: \"shield\" is true or false (whether it carries a shield: in 1.1 a shield is part of the weapon, as rudolfGuard's weapRapier_shield)");
            shield = shv.GetBoolean();
        }
        // ---- the armour it wears, the module's pieces by id (the module refuses one it has not when the bout is made).
        List<string>? armor = null;
        if (f.TryGetProperty("armor", out var av))
        {
            if (av.ValueKind != JsonValueKind.Array || av.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.String || v.GetString()!.Length == 0))
                throw new ArenaSpecException(ArenaSpecError.WrongType, $"arena: {who}: \"armor\" is a list of the module's pieces of armour (\"mail_shirt\", \"light_helm\")");
            armor = [.. av.EnumerateArray().Select(v => v.GetString()!)];
        }
        // ---- end
        // ---- its side (one against several; Sides checks them together).
        string? side = Str(f, "side", who);
        if (side is { Length: 0 } || side is not null && side.IndexOfAny([',', ':', '@', '^', '+', ';']) >= 0)
            throw new ArenaSpecException(ArenaSpecError.BadSide, $"arena: {who}: \"side\" '{side}' is empty or holds one of , : @ ^ + ;");
        // ---- end
        // ---- how it moves — a movement profile's name (human, neonate, elder) or "template" (its 1.1 template's walk and
        // run, read from the install when the bout is made; the session refuses a name it has not).
        string? movement = Str(f, "movement", who);
        if (movement is { Length: 0 }) throw new ArenaSpecException(ArenaSpecError.WrongType, $"arena: {who}: \"movement\" is empty (human, neonate, elder or template)");
        // ---- end
        return new SpecFighter(names[index], template, weapon, x, y, z, facing, brain, numbers, script, foe, sheet, blood, slots, shield, armor, side, movement);
    }

    /// <summary>
    /// The sides checked together — none, or every fighter given one, on two sides at least — and each fighter's first
    /// foe: a foe it names must be on another side; one it does not name is the first fighter of another side (the duelling brain's
    /// chooser, <see cref="DuelSideSystem"/>, then takes the nearest of the other sides from the first tick).
    /// </summary>
    static void Sides(JsonElement fs, List<SpecFighter> fighters)
    {
        int given = fighters.Count(f => f.Side is not null);
        if (given == 0) return;
        if (given < fighters.Count)
            throw new ArenaSpecException(ArenaSpecError.BadSide, $"arena: \"side\" is given to {given} of the {fighters.Count} fighters; give every fighter a side, or none (the fighters without: {string.Join(", ", fighters.Where(f => f.Side is null).Select(f => f.Name))})");
        if (fighters.Select(f => f.Side).Distinct(StringComparer.Ordinal).Count() < 2)
            throw new ArenaSpecException(ArenaSpecError.BadSide, $"arena: every fighter is on the side '{fighters[0].Side}'; one against several needs two sides at least");
        int k = 0;
        foreach (var f in fs.EnumerateArray())
        {
            var me = fighters[k];
            bool named = f.TryGetProperty("foe", out _);
            if (fighters[me.Foe].Side == me.Side)
            {
                if (named) throw new ArenaSpecException(ArenaSpecError.BadSide, $"arena: fighter {me.Name}: its foe '{fighters[me.Foe].Name}' is on its own side '{me.Side}'");
                fighters[k] = me with { Foe = fighters.FindIndex(o => o.Side != me.Side) };
            }
            k++;
        }
    }

    const float DetMathPi = MathF.PI;

    /// <summary>A number kept to a thousandth (the default places and facings, so they read the same on any machine).</summary>
    static float Round3(float v) => MathF.Round(v * 1000f) / 1000f;

    static CastBrain BrainKindOf(string kind, string? mode, string who) => kind switch
    {
        "duel" => CastBrain.Duel,
        "dummy" => mode switch
        {
            null or "stand" or "standing" => CastBrain.Stand,
            "block" or "blocking" => CastBrain.Block,
            _ => throw new ArenaSpecException(ArenaSpecError.BadBrain, $"arena: {who}: a dummy's \"mode\" is stand or block, not '{mode}'"),
        },
        "script" => CastBrain.Script,
        _ => throw new ArenaSpecException(ArenaSpecError.BadBrain, $"arena: {who}: the brain '{kind}' is not duel, dummy or script"),
    };

    static (CastBrain, DuelBrainSettings?, string?) Brain(JsonElement b, string who)
    {
        string kind = Str(b, "kind", who) ?? throw new ArenaSpecException(ArenaSpecError.BadBrain, $"arena: {who}: the brain has no \"kind\" (duel, dummy or script)");
        var brain = BrainKindOf(kind, Str(b, "mode", who), who);
        string? script = null;
        DuelBrainSettings? numbers = null;
        if (brain == CastBrain.Script)
        {
            Known(b, ["kind", "actions"], $"{who}'s brain");
            script = Str(b, "actions", who) ?? throw new ArenaSpecException(ArenaSpecError.BadScript, $"arena: {who}: a script brain needs \"actions\" (attack@1,strong@3,block@5+1)");
            if (!ActionScript.TryParse(string.Join(",", script.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(a => "x:" + a)),
                    new CombatSettings(), out _, out string error))
                throw new ArenaSpecException(ArenaSpecError.BadScript, $"arena: {who}: {error.Replace("--npc-actions: 'x:", "'")}");
        }
        else if (brain == CastBrain.Duel)
        {
            Known(b, ["kind", .. BrainNumbers.Names, ApproachField], $"{who}'s brain");   // the numbers' table, shared with the scenario; its approach
            var d = new DuelBrainSettings();
            // Read through the numbers' table (Arena/BrainNumbers.cs), in its order, with the same ranges and messages.
            numbers = d;
            foreach (var n in BrainNumbers.All)
            {
                if (n.Kind == BrainNumberKind.Number)
                    numbers = n.Set(numbers, Float(b, n.Name, $"{who}'s brain", n.Get(d), n.Least, n.Most));
                else if (n.Kind == BrainNumberKind.Flag && b.TryGetProperty(n.Name, out var flag))
                    numbers = n.Set(numbers, flag.ValueKind switch
                    {
                        JsonValueKind.True => 1f,
                        JsonValueKind.False => 0f,
                        _ => throw new ArenaSpecException(ArenaSpecError.WrongType, $"arena: {who}'s brain: \"{n.Name}\" is true or false"),
                    });
            }
            // The pace in place of the fighter's kind's — "none", or [least, most] seconds of pause after a run of
            // 1 to paceRun blows (2 by default) — so an old spec can state none.
            if (b.TryGetProperty("pace", out var pv))
            {
                bool none = pv.ValueKind == JsonValueKind.String && pv.GetString() == "none";
                bool pair = pv.ValueKind == JsonValueKind.Array && pv.GetArrayLength() == 2 && pv[0].ValueKind == JsonValueKind.Number && pv[1].ValueKind == JsonValueKind.Number;
                float lo = pair ? (float)pv[0].GetDouble() : 0f, hi = pair ? (float)pv[1].GetDouble() : 0f;
                if (!(none || pair) || !(lo >= 0f && hi >= lo && hi <= 60f))
                    throw new ArenaSpecException(ArenaSpecError.BadBrain, $"arena: {who}'s brain: \"pace\" is \"none\" or [least, most] seconds (0 to 60, rising)");
                numbers = numbers with { PaceLeastSeconds = lo, PaceMostSeconds = hi };
            }
            numbers = numbers with { PaceRunMost = (int)Float(b, "paceRun", $"{who}'s brain", numbers.PaceRunMost, 0f, 10f) };
            // Its approach from beyond its striking range — "duel" (the duelling brain's own) or "oneone" (1.1's manoeuvre).
            if (Str(b, ApproachField, $"{who}'s brain") is { } approach)
                numbers = numbers with { Approach = ApproachOf(approach) ?? throw new ArenaSpecException(ArenaSpecError.BadBrain, $"arena: {who}'s brain: \"approach\" is duel or oneone, not '{approach}'") };
            if (numbers == d) numbers = null;
        }
        else Known(b, ["kind", "mode"], $"{who}'s brain");
        return (brain, numbers, script);
    }

    /// <summary>The brain's field for its approach.</summary>
    public const string ApproachField = "approach";

    /// <summary>An approach by its name in a spec (<c>duel</c>, <c>oneone</c>), or null for another word.</summary>
    public static DuelApproach? ApproachOf(string name) => name switch
    {
        "duel" => DuelApproach.Duel,
        "oneone" => DuelApproach.OneOne,
        _ => null,
    };

    /// <summary>An approach's name in a spec.</summary>
    public static string NameOf(DuelApproach approach) => approach == DuelApproach.OneOne ? "oneone" : "duel";

    static CastSheet Sheet(JsonElement sh, string who, string folder)
    {
        Known(sh, SheetFields, $"{who}'s sheet");
        string? template = Str(sh, "template", who);
        string? text = null;
        if (Str(sh, "file", who) is { } file)
        {
            if (template is not null) throw new ArenaSpecException(ArenaSpecError.BadSheet, $"arena: {who}: a sheet is a template or a file, not both");
            string path = Path.GetFullPath(Path.Combine(folder, file));
            try { text = File.ReadAllText(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new ArenaSpecException(ArenaSpecError.SheetFileMissing, $"arena: {who}: the sheet file {path} could not be read ({e.Message})");
            }
        }
        var values = new List<CastValue>();
        if (sh.TryGetProperty("values", out var vs))
        {
            if (vs.ValueKind != JsonValueKind.Object) throw new ArenaSpecException(ArenaSpecError.BadSheet, $"arena: {who}: the sheet's \"values\" is an object of id: number");
            foreach (var v in vs.EnumerateObject())
            {
                if (v.Value.ValueKind != JsonValueKind.Number || !v.Value.TryGetInt32(out int value))
                    throw new ArenaSpecException(ArenaSpecError.BadSheet, $"arena: {who}: the sheet's values {v.Name} is not a whole number");
                values.Add(new CastValue(v.Name, value));
            }
        }
        // A power is its rank ("potence": 2, the options the sheet has kept) or { "rank": 2, "options": ["potence.passive"] }.
        if (sh.TryGetProperty("powers", out var ps))
        {
            if (ps.ValueKind != JsonValueKind.Object) throw new ArenaSpecException(ArenaSpecError.BadSheet, $"arena: {who}: the sheet's \"powers\" is an object of id: rank (or id: {{ \"rank\": n, \"options\": [ids] }})");
            foreach (var v in ps.EnumerateObject())
            {
                string what = $"{who}: the power {v.Name}";
                if (v.Value.ValueKind == JsonValueKind.Number)
                {
                    if (!v.Value.TryGetInt32(out int rank)) throw new ArenaSpecException(ArenaSpecError.BadPower, $"arena: {what}: its rank is a whole number");
                    values.Add(new CastValue(v.Name, rank, true));
                }
                else if (v.Value.ValueKind == JsonValueKind.Object)
                {
                    Known(v.Value, ["rank", "options"], what);
                    if (!v.Value.TryGetProperty("rank", out var r) || r.ValueKind != JsonValueKind.Number || !r.TryGetInt32(out int rank))
                        throw new ArenaSpecException(ArenaSpecError.BadPower, $"arena: {what}: \"rank\" is a whole number");
                    values.Add(new CastValue(v.Name, rank, true, Options(v.Value, what, rank)));
                }
                else throw new ArenaSpecException(ArenaSpecError.BadPower, $"arena: {what} is a rank or {{ \"rank\": n, \"options\": [ids] }}");
            }
        }
        return new CastSheet { Template = template, Text = text, Values = values };
    }

    /// <summary>
    /// A power's <c>options</c>, or null when it gives none: ids (lower case, digits, <c>_. -</c>), each once. An empty
    /// list with a nonzero rank is refused by name: a rank learns no option by itself, so the power would do nothing.
    /// </summary>
    static List<string>? Options(JsonElement o, string what, int? rank)
    {
        if (!o.TryGetProperty("options", out var os)) return null;
        if (os.ValueKind != JsonValueKind.Array) throw new ArenaSpecException(ArenaSpecError.BadPower, $"arena: {what}: \"options\" is a list of the power's option ids (\"celerity.active\")");
        var list = new List<string>();
        foreach (var x in os.EnumerateArray())
        {
            string? id = x.ValueKind == JsonValueKind.String ? x.GetString() : null;
            if (id is null || id.Length == 0 || !id.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '.' or '-') || list.Contains(id))
                throw new ArenaSpecException(ArenaSpecError.BadPower, $"arena: {what}: the option {x.GetRawText()} is not an option's id (lower case: \"celerity.active\"), or is given twice");
            list.Add(id);
        }
        if (list.Count == 0 && rank is not 0)
            throw new ArenaSpecException(ArenaSpecError.BadPower, $"arena: {what}: \"options\" is empty, and a rank learns no option by itself, so the power would do nothing; name its options (\"celerity.active\") or leave \"options\" out to keep the sheet's");
        return list;
    }

    static ArenaSweep Sweep(JsonElement s, List<string> names)
    {
        if (s.ValueKind != JsonValueKind.Object) throw new ArenaSpecException(ArenaSpecError.WrongType, "arena: a sweep is an object");
        Known(s, SweepFields, "the sweep");
        string fighter = Str(s, "fighter", "the sweep") ?? throw new ArenaSpecException(ArenaSpecError.BadSweep, "arena: the sweep names no \"fighter\"");
        int index = names.FindIndex(n => n.Equals(fighter, StringComparison.OrdinalIgnoreCase));
        if (index < 0) throw new ArenaSpecException(ArenaSpecError.SweepUnknownFighter, $"arena: the sweep's fighter '{fighter}' is not one of {string.Join(", ", names)}");
        string? trait = Str(s, "trait", "the sweep"), power = Str(s, "power", "the sweep");
        if ((trait is null) == (power is null)) throw new ArenaSpecException(ArenaSpecError.BadSweep, "arena: a sweep sweeps a \"trait\" (an Attribute or Ability by id) or a \"power\" (its rank), one of them");
        var values = new List<int>();
        if (s.TryGetProperty("values", out var vs))
        {
            if (vs.ValueKind != JsonValueKind.Array || vs.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out _)))
                throw new ArenaSpecException(ArenaSpecError.BadSweep, "arena: the sweep's \"values\" are whole numbers");
            values.AddRange(vs.EnumerateArray().Select(v => v.GetInt32()));
            if (s.TryGetProperty("from", out _) || s.TryGetProperty("to", out _))
                throw new ArenaSpecException(ArenaSpecError.BadSweep, "arena: a sweep gives \"values\" or \"from\"/\"to\"/\"step\", not both");
        }
        else if (s.TryGetProperty("from", out _))
        {
            int from = Int(s, "from", "the sweep", 0, int.MinValue, int.MaxValue), to = Int(s, "to", "the sweep", from, int.MinValue, int.MaxValue);
            int step = Int(s, "step", "the sweep", 1, 1, int.MaxValue);
            for (long v = from; from <= to ? v <= to : v >= to; v += from <= to ? step : -step)
            {
                values.Add((int)v);
                if (values.Count > 10_000) throw new ArenaSpecException(ArenaSpecError.BadSweep, "arena: the sweep has more than 10,000 values");
            }
        }
        if (values.Count == 0) throw new ArenaSpecException(ArenaSpecError.BadSweep, "arena: the sweep has no values (\"values\": [...], or \"from\", \"to\", \"step\")");
        // A power's sweep may name the options learned with each nonzero rank.
        List<string>? options = null;
        if (s.TryGetProperty("options", out _))
        {
            if (power is null) throw new ArenaSpecException(ArenaSpecError.BadSweep, "arena: a trait's sweep takes no \"options\" (a power's does)");
            options = Options(s, $"the sweep of {power}", values.Any(v => v != 0) ? 1 : 0);
        }
        return new ArenaSweep(index, trait ?? power!, power is not null, values, options);
    }

    // ---- fields ----

    static void Known(JsonElement o, string[] fields, string what)
    {
        foreach (var p in o.EnumerateObject())
            if (Array.IndexOf(fields, p.Name) < 0)
                throw new ArenaSpecException(ArenaSpecError.UnknownField, $"arena: {what}: the field \"{p.Name}\" is not one it takes ({string.Join(", ", fields)})");
    }

    static string? Str(JsonElement o, string name, string what)
    {
        if (!o.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind != JsonValueKind.String) throw new ArenaSpecException(ArenaSpecError.WrongType, $"arena: {what}: \"{name}\" is text");
        return v.GetString();
    }

    static int Int(JsonElement o, string name, string what, int fallback, int lo, int hi) => (int)Long(o, name, what, fallback, lo, hi);

    static long Long(JsonElement o, string name, string what, long fallback, long lo, long hi)
    {
        if (!o.TryGetProperty(name, out var v)) return fallback;
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt64(out long x)) throw new ArenaSpecException(ArenaSpecError.WrongType, $"arena: {what}: \"{name}\" is a whole number");
        if (x < lo || x > hi) throw new ArenaSpecException(ArenaSpecError.OutOfRange, string.Create(CultureInfo.InvariantCulture, $"arena: {what}: \"{name}\" is {x}; it may be {lo} to {hi}"));
        return x;
    }

    static float Float(JsonElement o, string name, string what, float fallback, float lo, float hi)
    {
        if (!o.TryGetProperty(name, out var v)) return fallback;
        if (v.ValueKind != JsonValueKind.Number) throw new ArenaSpecException(ArenaSpecError.WrongType, $"arena: {what}: \"{name}\" is a number");
        float x = v.GetSingle();
        if (!(x >= lo && x <= hi)) throw new ArenaSpecException(ArenaSpecError.OutOfRange, string.Create(CultureInfo.InvariantCulture, $"arena: {what}: \"{name}\" is {x}; it may be {lo} to {hi}"));
        return x;
    }
}
