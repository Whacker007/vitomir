// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Text.Json;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Modules;

namespace Vitomir.Modules.Enhanced.Configuration;

/// <summary>
/// The enhanced module's contact data (ours): the files of <c>Data/combat/</c> that are
/// contact's, told apart by their schema — one <c>VITOMIR_COMBAT_MATTER</c> file (<c>enhanced:combat/weapons</c>: the
/// active window's rule, the weapons' matter by carry class and by template, the shields' materials, the body's, the
/// level's surfaces by name, the parry rule, the bare hand's ball, the reach) and any number of <c>VITOMIR_PROTECTED</c>
/// files (<c>enhanced:combat/protected</c>: the people weapons never harm, by the kind of character they are) — read
/// <b>strictly</b>, as the movement and perception files are: a field missing, unknown, of the wrong kind or out of its
/// range is named, by file and field, and the module refuses. Files of other schemas in the folder are left to others.
/// <code>
/// { "schema": "VITOMIR_COMBAT_MATTER", "status": "SANDBOX", "title": "...", "note": "...",
///   "activeWindow": { "shareOfPeak": 0.4, "sampleSeconds": 0.0041666667, "bridgeSeconds": 0.05, "leastSeconds": 0.1 },
///   "parry": "whileBlocking", "handRadius": 0.06, "reachAtMost": 3.5, "body": "flesh",
///   "classes": { "0": { "name": "bare hands", "material": "flesh", "massKg": 0.6 }, "1": { ... }, ... },
///   "weapons": { "BroadSword": { "material": "steel", "massKg": 1.4 }, ... },
///   "shields": { "default": "wood", "byTemplate": { "weapRapier_shield": "wood" } },
///   "level": { "default": "stone", "surfaces": [ { "contains": "wood", "material": "wood" }, ... ] } }
/// { "schema": "VITOMIR_PROTECTED", "status": "SANDBOX", "title": "...", "note": "...", "people": [ "Merchant" ] }
/// </code>
/// </summary>
internal static class CombatFiles
{
    public const string MatterSchema = "VITOMIR_COMBAT_MATTER", ProtectedSchema = "VITOMIR_PROTECTED";
    const string Sandbox = "SANDBOX";

    static readonly string[] MatterKeys = ["schema", "status", "title", "note", "activeWindow", "parry", "handRadius", "reachAtMost", "body", "classes", "weapons", "shields", "level", "stop"];
    // Where a blade stops (the platform's BladeStopRule).
    static readonly string[] StopKeys = ["enabled", "biteSeconds", "reboundSeconds", "reboundMetres", "reboundRate", "readySecondsLeast", "readySecondsMost", "bringBack", "stopFreesOwnerAtOnce", "blockedStaggerSeconds",
        "scaled"];   // the rebound scaled by the blow, the recoil, the stun
    static readonly string[] ScaledKeys = ["note", "byImpulse", "metresPerImpulse", "leastMetres", "mostMetres", "seconds", "levelByMaterial", "recoil", "stun"];
    static readonly string[] RecoilKeys = ["note", "metresPerImpulse", "mostMetres", "peakSeconds", "settleSeconds", "leanPerMetre", "wristPerMetre"];
    static readonly string[] StunKeys = ["note", "heldSeconds", "parriedSeconds", "guardAfterRebound", "fromSwingEnd"];
    static readonly string[] ProtectedKeys = ["schema", "status", "title", "note", "people"];
    static readonly string[] WindowKeys = ["shareOfPeak", "sampleSeconds", "bridgeSeconds", "leastSeconds", "strikeStarts"];
    static readonly string[] StrikeStartKeys = ["clips", "rule", "metres", "source"];
    static readonly string[] ClassKeys = ["name", "material", "massKg"];
    static readonly string[] WeaponKeys = ["material", "massKg"];
    static readonly string[] ShieldKeys = ["default", "byTemplate"];
    static readonly string[] LevelKeys = ["default", "surfaces"];
    static readonly string[] SurfaceKeys = ["contains", "material"];

    /// <summary>The contact data as read: applied to the platform's <see cref="ContactConfig"/> by <see cref="Apply"/>.</summary>
    public sealed record Matter(bool Sandbox, ActiveWindowRule Window, ParryRule Parry, float HandRadius, float ReachAtMost, ContactMaterial Body,
        WeaponMatter[] Classes, IReadOnlyDictionary<string, WeaponMatter> Weapons, ContactMaterial ShieldDefault,
        IReadOnlyDictionary<string, ContactMaterial> Shields, ContactMaterial LevelDefault, IReadOnlyList<SurfaceRule> Surfaces,
        BladeStopRule? Stop = null)
    {
        /// <summary>Gives these numbers to the platform's contact.</summary>
        public void Apply(ContactConfig c)
        {
            c.Sandbox = Sandbox;
            c.Window = Window;
            c.Parry = Parry;
            c.HandRadius = HandRadius;
            c.ReachAtMost = ReachAtMost;
            c.Body = Body;
            c.Classes = Classes;
            foreach (var (k, v) in Weapons) c.Weapons[k] = v;
            c.ShieldDefault = ShieldDefault;
            foreach (var (k, v) in Shields) c.Shields[k] = v;
            c.LevelDefault = LevelDefault;
            c.Surfaces.AddRange(Surfaces);
            if (Stop is not null) c.Stop = Stop;
        }
    }

    /// <summary>The file's schema, or null when it names none.</summary>
    public static string? SchemaOf(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("schema", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;

    /// <summary>The contact matter of <paramref name="id"/>, or null with every problem added.</summary>
    public static Matter? ReadMatter(AssetId id, JsonElement root, List<string> problems)
    {
        int before = problems.Count;
        var c = new JsonChecks(id.ToString(), problems);
        if (!Top(c, root, MatterKeys, MatterSchema, out bool sandbox)) return null;

        var window = new ActiveWindowRule();
        if (c.Obj(root, "activeWindow") is { } w)
        {
            Known(c, w, WindowKeys, "activeWindow");
            float share = Number(c, w, "shareOfPeak", "activeWindow", 0.01, 1), step = Number(c, w, "sampleSeconds", "activeWindow", 1e-4, 0.05),
                bridge = Number(c, w, "bridgeSeconds", "activeWindow", 0, 0.5), least = Number(c, w, "leastSeconds", "activeWindow", 0, 1);
            window = new ActiveWindowRule { ShareOfPeak = share, SampleSeconds = step, BridgeSeconds = bridge, LeastSeconds = least };
            if (c.Has(w, "strikeStarts")) window = window with { StrikeStarts = StrikeStarts(c, w) };   // absent: the platform's
        }
        var parry = c.Str(root, "parry") switch
        {
            "never" => ParryRule.Never,
            "whileBlocking" => ParryRule.WhileBlocking,
            "always" => ParryRule.Always,
            null => ParryRule.WhileBlocking,
            var p => BadParry(c, p),
        };
        float hand = Number(c, root, "handRadius", "", 0.005, 0.5), reach = Number(c, root, "reachAtMost", "", 0.5, 10);
        var body = Material(c, c.Str(root, "body"), "body");

        var classes = new List<WeaponMatter>();
        if (c.Obj(root, "classes") is { } table)
        {
            var given = new SortedDictionary<int, WeaponMatter>();
            foreach (var p in table.EnumerateObject())
            {
                string what = $"classes: '{p.Name}'";
                if (!int.TryParse(p.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int carry) || carry > 15) { c.Fail($"{what} is not a carry class (0 to 15)"); continue; }
                if (p.Value.ValueKind != JsonValueKind.Object) { c.Fail($"{what} is not an object of {string.Join(", ", ClassKeys)}"); continue; }
                Known(c, p.Value, ClassKeys, what);
                c.Str(p.Value, "name");
                given[carry] = Weapon(c, p.Value, what);
            }
            for (int k = 0; k < (given.Count == 0 ? 0 : given.Keys.Max() + 1); k++)
            {
                if (!given.TryGetValue(k, out var m)) { c.Fail($"classes: carry class '{k}' is missing (every class from 0 to the highest given)"); continue; }
                classes.Add(m);
            }
            if (given.Count == 0) c.Fail("classes: none given (at least carry class 0, bare hands)");
        }

        var weapons = new Dictionary<string, WeaponMatter>(StringComparer.OrdinalIgnoreCase);
        if (c.Obj(root, "weapons") is { } wt)
            foreach (var p in wt.EnumerateObject())
            {
                string what = $"weapons: '{p.Name}'";
                if (p.Value.ValueKind != JsonValueKind.Object) { c.Fail($"{what} is not an object of {string.Join(", ", WeaponKeys)}"); continue; }
                Known(c, p.Value, WeaponKeys, what);
                if (!weapons.TryAdd(p.Name, Weapon(c, p.Value, what))) c.Fail($"{what} is given twice");
            }

        var shieldDefault = ContactMaterial.Wood;
        var shields = new Dictionary<string, ContactMaterial>(StringComparer.OrdinalIgnoreCase);
        if (c.Obj(root, "shields") is { } st)
        {
            Known(c, st, ShieldKeys, "shields");
            shieldDefault = Material(c, c.Str(st, "default"), "shields: default");
            if (c.Obj(st, "byTemplate", required: false) is { } bt)
                foreach (var p in bt.EnumerateObject())
                    shields[p.Name] = Material(c, p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null, $"shields: byTemplate '{p.Name}'");
        }

        var levelDefault = ContactMaterial.Stone;
        var surfaces = new List<SurfaceRule>();
        if (c.Obj(root, "level") is { } lt)
        {
            Known(c, lt, LevelKeys, "level");
            levelDefault = Material(c, c.Str(lt, "default"), "level: default");
            if (c.Arr(lt, "surfaces", required: false) is { } list)
            {
                int i = 0;
                foreach (var s in list.EnumerateArray())
                {
                    string what = $"level: surfaces[{i++}]";
                    if (s.ValueKind != JsonValueKind.Object) { c.Fail($"{what} is not an object of {string.Join(", ", SurfaceKeys)}"); continue; }
                    Known(c, s, SurfaceKeys, what);
                    var contains = c.Str(s, "contains");
                    if (contains is { Length: 0 }) c.Fail($"{what}: 'contains' is empty");
                    var m = Material(c, c.Str(s, "material"), what);
                    if (contains is { Length: > 0 }) surfaces.Add(new SurfaceRule(contains, m));
                }
            }
        }
        // Where a blade stops; absent, the platform's defaults.
        BladeStopRule? stop = null;
        if (c.Obj(root, "stop", required: false) is { } so)
        {
            Known(c, so, StopKeys, "stop");
            var kind = c.Str(so, "bringBack", required: false) switch
            {
                "reverse" => BringBackKind.Reverse,
                "blend" => BringBackKind.Blend,
                null => BringBackKind.Blend,
                var k => BadBringBack(c, k),
            };
            stop = new BladeStopRule
            {
                Enabled = c.Bool(so, "enabled") ?? true,
                BiteSeconds = Number(c, so, "biteSeconds", "stop", 0, 0.5),
                ReboundSeconds = Number(c, so, "reboundSeconds", "stop", 0, 0.5),
                ReboundMetres = Number(c, so, "reboundMetres", "stop", 0, 2),
                ReboundRate = Number(c, so, "reboundRate", "stop", 0, 10),
                ReadySecondsLeast = Number(c, so, "readySecondsLeast", "stop", 0, 2),
                ReadySecondsMost = Number(c, so, "readySecondsMost", "stop", 0, 5),
                BringBack = kind,
                StopFreesOwnerAtOnce = c.Bool(so, "stopFreesOwnerAtOnce") ?? false,
                BlockedStaggerSeconds = Number(c, so, "blockedStaggerSeconds", "stop", 0, 5),
            };
            if (stop.ReadySecondsMost < stop.ReadySecondsLeast) c.Fail("stop: 'readySecondsMost' is less than 'readySecondsLeast'");
            if (c.Obj(so, "scaled", required: false) is { } sc) stop = Scaled(c, sc, stop);
        }
        return problems.Count == before
            ? new Matter(sandbox, window, parry, hand, reach, body, [.. classes], weapons, shieldDefault, shields, levelDefault, surfaces, stop)
            : null;
    }

    /// <summary>The protected people of <paramref name="id"/>, or null with every problem added.</summary>
    public static IReadOnlyList<string>? ReadProtected(AssetId id, JsonElement root, List<string> problems)
    {
        int before = problems.Count;
        var c = new JsonChecks(id.ToString(), problems);
        if (!Top(c, root, ProtectedKeys, ProtectedSchema, out _)) return null;
        var people = new List<string>();
        if (c.Arr(root, "people") is { } list)
            foreach (var p in list.EnumerateArray())
            {
                if (p.ValueKind == JsonValueKind.String && p.GetString() is { Length: > 0 } name) people.Add(name);
                else c.Fail("people: each is the name of a kind of character (a 1.1 template or a catalogue name, a string)");
            }
        return problems.Count == before ? people : null;
    }

    static WeaponMatter Weapon(JsonChecks c, JsonElement o, string what) =>
        new(Material(c, c.Str(o, "material"), what), Number(c, o, "massKg", what, 0.01, 50));

    static ContactMaterial Material(JsonChecks c, string? word, string what)
    {
        if (word is null) return ContactMaterial.Stone;
        if (ContactConfig.Parse(word) is { } m) return m;
        c.Fail($"{what}: material '{word}' is none of {string.Join(", ", Enum.GetValues<ContactMaterial>().Select(ContactConfig.Word))}");
        return ContactMaterial.Stone;
    }

    // ---- stop.scaled — the rebound scaled by the blow, the arm's recoil, the attacker's stun ----
    static BladeStopRule Scaled(JsonChecks c, JsonElement o, BladeStopRule stop)
    {
        Known(c, o, ScaledKeys, "stop.scaled");
        var byMaterial = MaterialFactors.Default;
        if (c.Obj(o, "levelByMaterial") is { } m)
            foreach (var p in m.EnumerateObject())
            {
                if (p.Name == "note") continue;
                if (ContactConfig.Parse(p.Name) is not { } mat) { c.Fail($"stop.scaled.levelByMaterial: '{p.Name}' is no material"); continue; }
                byMaterial = byMaterial.With(mat, Number(c, m, p.Name, "stop.scaled.levelByMaterial", 0, 3));
            }
        stop = stop with
        {
            ReboundByImpulse = c.Bool(o, "byImpulse") ?? true,
            ReboundMetresPerImpulse = Number(c, o, "metresPerImpulse", "stop.scaled", 0, 1),
            ReboundLeastMetres = Number(c, o, "leastMetres", "stop.scaled", 0, 2),
            ReboundMostMetres = Number(c, o, "mostMetres", "stop.scaled", 0, 2),
            ReboundScaledSeconds = Number(c, o, "seconds", "stop.scaled", 0.01, 0.5),
            ReboundByMaterial = byMaterial,
        };
        if (stop.ReboundMostMetres < stop.ReboundLeastMetres) c.Fail("stop.scaled: 'mostMetres' is less than 'leastMetres'");
        if (c.Obj(o, "recoil") is { } r)
        {
            Known(c, r, RecoilKeys, "stop.scaled.recoil");
            stop = stop with
            {
                RecoilMetresPerImpulse = Number(c, r, "metresPerImpulse", "stop.scaled.recoil", 0, 0.1),
                RecoilMostMetres = Number(c, r, "mostMetres", "stop.scaled.recoil", 0, 0.5),
                RecoilPeakSeconds = Number(c, r, "peakSeconds", "stop.scaled.recoil", 0.005, 0.5),
                RecoilSettleSeconds = Number(c, r, "settleSeconds", "stop.scaled.recoil", 0.01, 2),
                RecoilLeanPerMetre = Number(c, r, "leanPerMetre", "stop.scaled.recoil", 0, 10),
                RecoilWristPerMetre = Number(c, r, "wristPerMetre", "stop.scaled.recoil", 0, 20),
            };
        }
        if (c.Obj(o, "stun") is { } t)
        {
            Known(c, t, StunKeys, "stop.scaled.stun");
            stop = stop with
            {
                StunHeldSeconds = Number(c, t, "heldSeconds", "stop.scaled.stun", 0, 2),
                StunParriedSeconds = Number(c, t, "parriedSeconds", "stop.scaled.stun", 0, 2),
                GuardAfterRebound = c.Bool(t, "guardAfterRebound") ?? true,
                StunFromSwingEnd = c.Bool(t, "fromSwingEnd") ?? false,
            };
        }
        return stop;
    }
    // ---- end

    static BringBackKind BadBringBack(JsonChecks c, string k)
    {
        c.Fail($"stop: bringBack is '{k}'; it is reverse or blend");
        return BringBackKind.Blend;
    }

    static ParryRule BadParry(JsonChecks c, string p)
    {
        c.Fail($"parry is '{p}'; it is never, whileBlocking or always");
        return ParryRule.WhileBlocking;
    }

    /// <summary>
    /// <c>activeWindow.strikeStarts</c>, where a family of blows' strike proper begins (<see cref="StrikeStart"/>):
    /// each row its <c>clips</c> (names), its <c>rule</c> (<c>level</c>, <c>top</c>, <c>forward</c>), for <c>level</c> its
    /// <c>metres</c> (0 to 0.5), and its <c>source</c> (words, not read). An empty list is the earlier build's windows.
    /// </summary>
    static IReadOnlyList<StrikeStart> StrikeStarts(JsonChecks c, JsonElement w)
    {
        var rows = new List<StrikeStart>();
        if (c.Arr(w, "strikeStarts") is not { } list) return rows;
        int n = 0;
        foreach (var e in list.EnumerateArray())
        {
            string where = $"activeWindow: strikeStarts[{n++}]";
            if (e.ValueKind != JsonValueKind.Object) { c.Fail($"{where} must be an object"); continue; }
            Known(c, e, StrikeStartKeys, where);
            var clips = new List<string>();
            if (c.Arr(e, "clips") is { } names)
                foreach (var x in names.EnumerateArray())
                    if (x.ValueKind == JsonValueKind.String && x.GetString() is { Length: > 0 } name) clips.Add(name);
                    else c.Fail($"{where}: clips holds a name that is not a word");
            if (clips.Count == 0) c.Fail($"{where}: no clips named");
            var word = c.Str(e, "rule");
            int rule = word is null ? -1 : StrikeStart.RuleNames.ToList().IndexOf(word);
            if (word is not null && rule < 0) c.Fail($"{where}: rule is '{word}'; it is {string.Join(", ", StrikeStart.RuleNames)}");
            float metres = c.Has(e, "metres") ? Number(c, e, "metres", where, 0, 0.5) : 0.1f;
            if (rule >= 0 && clips.Count > 0) rows.Add(new StrikeStart(clips, (StrikeStartRule)rule, metres));
        }
        return rows;
    }

    static float Number(JsonChecks c, JsonElement o, string key, string where, double least, double most)
    {
        string what = where.Length == 0 ? $"'{key}'" : $"{where}: '{key}'";
        if (!o.TryGetProperty(key, out var v)) { c.Fail($"{what} is missing"); return (float)least; }
        if (c.Num(v, what) is not { } d) return (float)least;
        if (d < least || d > most) { c.Fail($"{what} is {d.ToString(CultureInfo.InvariantCulture)}; it must be {least.ToString(CultureInfo.InvariantCulture)} to {most.ToString(CultureInfo.InvariantCulture)}"); return (float)least; }
        return (float)d;
    }

    static void Known(JsonChecks c, JsonElement o, string[] keys, string what)
    {
        foreach (var p in o.EnumerateObject())
            if (Array.IndexOf(keys, p.Name) < 0) c.Fail($"{what}: unknown field '{p.Name}' (the fields are {string.Join(", ", keys)})");
    }

    static bool Top(JsonChecks c, JsonElement root, string[] keys, string schema, out bool sandbox)
    {
        sandbox = false;
        if (root.ValueKind != JsonValueKind.Object) { c.Fail("must be an object"); return false; }
        foreach (var p in root.EnumerateObject())
            if (Array.IndexOf(keys, p.Name) < 0) c.Fail($"unknown field '{p.Name}' (the fields are {string.Join(", ", keys)})");
        c.Schema(root, schema);
        var status = c.Str(root, "status");
        if (status is not null && status != Sandbox) c.Fail($"status is '{status}'; the only status known is '{Sandbox}' (the engine's research numbers) until the rules package's");
        sandbox = status == Sandbox;
        c.Str(root, "title");
        c.Str(root, "note", required: false);
        return true;
    }
}
