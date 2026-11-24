// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using System.Text.Json.Nodes;
using Vitomir.Platform.Control;
using Vitomir.Platform.Host.Arena;

namespace Vitomir.Platform.Host.Scenario;

internal sealed partial class ScenarioReader
{
    /// <summary>The characters an actor's name may not hold (the dot is the paths' separator; the rest the selectors' and the arena's).</summary>
    static readonly char[] NameForbidden = ['.', ',', ':', ';', '@', '^', '+', '/', '~', '"'];

    /// <summary>The actors whose foe the file names (a named foe on its own side is refused; a default one moves to the first of another side).</summary>
    readonly HashSet<int> foeNamed = [];

    JsonArray ActorsOf(JsonElement root)
    {
        var list = new JsonArray();
        if (!root.TryGetProperty("actors", out var actors) || actors.ValueKind != JsonValueKind.Array || actors.GetArrayLength() < 2)
        {
            Error(ScenarioErrorCode.TooFewActors, "/actors", "must list two or more actors");
            if (actors.ValueKind != JsonValueKind.Array) return list;
        }
        // The names first: foes, places and steps name each other.
        int k = 0;
        foreach (var a in actors.EnumerateArray())
        {
            string at = ScenarioPointer.Of("/actors", k);
            string name = ((char)('A' + k % 26)).ToString();
            if (a.ValueKind == JsonValueKind.Object && a.TryGetProperty("name", out var n))
            {
                if (n.ValueKind != JsonValueKind.String) Error(ScenarioErrorCode.WrongType, at + "/name", $"must be text, got {Kind(n)}");
                else name = n.GetString()!;
            }
            if (name.Length == 0 || name.IndexOfAny(NameForbidden) >= 0 || name.Trim() != name || actorNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                Error(ScenarioErrorCode.BadName, at + "/name", $"\"{name}\" is empty, repeated (case aside), starts or ends with a space, or holds one of . , : ; @ ^ + / ~ \" (letters, digits, spaces, _ and - only)");
            actorNames.Add(name);
            k++;
        }
        k = 0;
        foreach (var a in actors.EnumerateArray())
        {
            string at = ScenarioPointer.Of("/actors", k);
            list.Add(IsObject(a, at, "an actor") ? Actor(a, k, at) : new JsonObject { ["name"] = actorNames[k] });
            k++;
        }
        Sides(list);
        PlaceCycles(list);
        return list;
    }

    JsonObject Actor(JsonElement a, int index, string at)
    {
        Known(a, at, ActorFields, $"an actor ({actorNames[index]})");
        var o = new JsonObject { ["name"] = actorNames[index] };

        // The preset expands to its template, weapon, shield and sheet; the fields given beside it win.
        FoePreset? preset = null;
        if (Text(a, "preset", at, what: "a preset's name") is { } presetName)
        {
            if (game is null) o["preset"] = presetName;
            else if (Name(presetName, at + "/preset", game.Presets.Keys, "preset") is { } p)
            {
                preset = game.Presets[p];
                o["preset"] = p;
            }
        }
        string template = Text(a, "template", at, what: "a 1.1 character") ?? preset?.Template ?? "Christof";
        if (game is not null && Has(a, "template")) template = Name(template, at + "/template", game.Characters, "character") ?? template;
        o["template"] = template;

        o["sheet"] = SheetOf(a, at, preset);
        if (Whole(a, "health", at, 1, 1000, "Health, 1 to 1000; 100 a person") is { } health) o["health"] = health;
        else o["health"] = "full";
        if (Whole(a, "blood", at, 0, 1_000_000, "Blood, 1.1's scale") is { } blood) o["blood"] = blood;
        else o["blood"] = "full";
        o["movement"] = MovementOf(a, at, preset, actorNames[index]);   // human, neonate, elder or template (its 1.1 template's walk and run)

        o["gear"] = GearOf(a, at, template, preset);
        o["place"] = PlaceOf(a, at, index);
        o["facing"] = FacingOf(a, at, index);

        // The foe: named, else the next actor round (the arena's).
        int foe = (index + 1) % Math.Max(1, actorNames.Count);
        if (a.TryGetProperty("foe", out var fv) && fv.ValueKind != JsonValueKind.Null)
        {
            if (fv.ValueKind != JsonValueKind.String) Error(ScenarioErrorCode.WrongType, at + "/foe", $"must be another actor's name, got {Kind(fv)}");
            else
            {
                int f = actorNames.FindIndex(n => n.Equals(fv.GetString(), StringComparison.OrdinalIgnoreCase));
                if (f < 0 || f == index) Error(ScenarioErrorCode.UnknownFoe, at + "/foe", $"\"{fv.GetString()}\" is no other actor (the actors: {string.Join(", ", actorNames)})", ScenarioPointer.Nearest(fv.GetString()!, actorNames.Where((_, i) => i != index)));
                else { foe = f; foeNamed.Add(index); }
            }
        }
        o["foe"] = actorNames.Count > 1 ? actorNames[foe] : null;
        if (Text(a, "side", at, what: "a side's name") is { } side)
        {
            if (side.Length == 0 || side.IndexOfAny([',', ':', '@', '^', '+', ';']) >= 0) Error(ScenarioErrorCode.BadSide, at + "/side", $"\"{side}\" is empty or holds one of , : @ ^ + ;");
            o["side"] = side;
        }
        if (a.TryGetProperty("slots", out var sl) && sl.ValueKind != JsonValueKind.Null)
        {
            var slots = new JsonArray();
            if (sl.ValueKind != JsonValueKind.Array) Error(ScenarioErrorCode.BadSlots, at + "/slots", $"must be a list of the quick slots its AI may press (1 to {Powers.QuickSlots.Count}), got {Kind(sl)}");
            else
            {
                int i = 0;
                var seen = new HashSet<int>();
                foreach (var v in sl.EnumerateArray())
                {
                    if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out int s) || s < 1 || s > Powers.QuickSlots.Count || !seen.Add(s))
                        Error(ScenarioErrorCode.BadSlots, ScenarioPointer.Of(at + "/slots", i), $"{v.GetRawText()} is not a quick slot (1 to {Powers.QuickSlots.Count}, each once)");
                    else slots.Add(s);
                    i++;
                }
            }
            o["slots"] = slots;
        }
        else o["slots"] = "all";
        o["control"] = ControlOf(a, at, index);
        // The archer brain shoots a bow: the actor must carry one.
        if (o["control"]?["ai"]?["kind"]?.GetValue<string>() == "archer" && o["gear"]?["ranged"] is null)
            Error(ScenarioErrorCode.BadBrain, ScenarioPointer.Of(at, "control") + "/ai", "the archer brain needs a bow: give the actor \"gear\": { \"ranged\": { \"weapon\": \"war_bow\", \"arrows\": 12 } }");
        ApproachDefault(o["control"]!.AsObject(), preset, actorNames[index]);   // a duelling brain's approach, its preset's unless given
        return o;
    }

    // ---------------------------------------------------------------- the sheet

    JsonObject SheetOf(JsonElement a, string at, FoePreset? preset)
    {
        var sheet = new JsonObject { ["template"] = preset?.Sheet, ["values"] = new JsonObject(), ["powers"] = new JsonObject() };
        if (!a.TryGetProperty("sheet", out var sh) || sh.ValueKind == JsonValueKind.Null) return Checked(sheet, at + "/sheet");
        string sp = at + "/sheet";
        if (sh.ValueKind == JsonValueKind.String) { sheet["template"] = sh.GetString(); return Checked(sheet, sp); }
        if (sh.ValueKind != JsonValueKind.Object) { Error(ScenarioErrorCode.BadSheet, sp, $"must be a sheet template's id or {{ \"template\" | \"file\", \"values\", \"powers\" }}, got {Kind(sh)}"); return sheet; }
        Known(sh, sp, SheetFields, "a sheet");
        if (Text(sh, "template", sp, what: "a sheet template's id") is { } t) sheet["template"] = t;
        if (Text(sh, "file", sp, what: "a sheet file, from the scenario's folder") is { } file)
        {
            if (Has(sh, "template")) Error(ScenarioErrorCode.BadSheet, sp, "a sheet is a template or a file, not both");
            string path = Path.GetFullPath(Path.Combine(folder, file));
            if (!File.Exists(path)) Error(ScenarioErrorCode.SheetFileMissing, sp + "/file", $"{path} is not there");
            sheet.Remove("template");
            sheet["file"] = path;
        }
        if (sh.TryGetProperty("values", out var vs) && vs.ValueKind != JsonValueKind.Null)
        {
            if (vs.ValueKind != JsonValueKind.Object) Error(ScenarioErrorCode.BadSheet, sp + "/values", $"must be an object of trait id: whole number, got {Kind(vs)}");
            else foreach (var v in vs.EnumerateObject())
                {
                    string vp = ScenarioPointer.Of(sp + "/values", v.Name);
                    string id = v.Name;
                    if (game is not null && v.Name == "archery" && !game.SheetValueIds.Contains("archery"))
                        Error(ScenarioErrorCode.UnknownName, vp, "there is no trait \"archery\": the bow reads the registry's one ranged Skill, \"firearms\" (the enhanced module's working name for it is \"Range\"); write \"firearms\"", ["firearms"]);
                    else if (game is not null) id = Name(v.Name, vp, game.SheetValueIds, "trait") ?? v.Name;
                    if (Whole(v.Value, vp, int.MinValue, int.MaxValue) is { } n) ((JsonObject)sheet["values"]!)[id] = n;
                }
        }
        if (sh.TryGetProperty("powers", out var ps) && ps.ValueKind != JsonValueKind.Null)
        {
            if (ps.ValueKind != JsonValueKind.Object) Error(ScenarioErrorCode.BadSheet, sp + "/powers", $"must be an object of power id: rank (or {{ \"rank\", \"options\" }}), got {Kind(ps)}");
            else foreach (var p in ps.EnumerateObject())
                {
                    string pp = ScenarioPointer.Of(sp + "/powers", p.Name);
                    string id = game is null ? p.Name : Name(p.Name, pp, game.Powers.Keys, "power") ?? p.Name;
                    if (Power(p.Value, pp, id) is { } power) ((JsonObject)sheet["powers"]!)[id] = power;
                }
        }
        return Checked(sheet, sp);
    }

    JsonObject Checked(JsonObject sheet, string sp)
    {
        if (game is not null && sheet["template"]?.GetValue<string>() is { } t && Name(t, sp + (sheet.Count > 0 ? "/template" : ""), game.SheetTemplates, "sheet template") is { } known)
            sheet["template"] = known;
        return sheet;
    }

    JsonObject? Power(JsonElement v, string pp, string id)
    {
        int? rank = null;
        JsonArray? options = null;
        if (v.ValueKind == JsonValueKind.Number) rank = (int?)Whole(v, pp, 0, 10, "rank");
        else if (v.ValueKind == JsonValueKind.Object)
        {
            Known(v, pp, ["rank", "options"], $"the power {id}");
            if (!v.TryGetProperty("rank", out var r)) Error(ScenarioErrorCode.BadPower, pp + "/rank", "is required: the power's rank, a whole number 0 to 10");
            else rank = (int?)Whole(r, pp + "/rank", 0, 10, "rank");
            if (v.TryGetProperty("options", out var os) && os.ValueKind != JsonValueKind.Null)
            {
                options = [];
                if (os.ValueKind != JsonValueKind.Array) Error(ScenarioErrorCode.BadPower, pp + "/options", $"must be a list of the power's option ids (\"celerity.active\"), got {Kind(os)}");
                else
                {
                    int i = 0;
                    foreach (var x in os.EnumerateArray())
                    {
                        string op = ScenarioPointer.Of(pp + "/options", i++);
                        if (x.ValueKind != JsonValueKind.String) { Error(ScenarioErrorCode.BadPower, op, $"must be an option's id, got {Kind(x)}"); continue; }
                        string opt = x.GetString()!;
                        if (game is not null && game.Powers.TryGetValue(id, out var known)) opt = Name(opt, op, known.Options, $"option of {id}") ?? opt;
                        if (options.Any(o => o!.GetValue<string>() == opt)) { Error(ScenarioErrorCode.BadPower, op, $"\"{opt}\" is given twice"); continue; }
                        options.Add(opt);
                    }
                    if (options.Count == 0 && rank is not 0)
                        Error(ScenarioErrorCode.BadPower, pp + "/options", $"is empty, and a rank learns no option by itself, so the power would do nothing; name its options (\"{id}.…\") or leave \"options\" out to keep the sheet's");
                }
            }
        }
        else { Error(ScenarioErrorCode.BadPower, pp, $"must be a rank or {{ \"rank\": n, \"options\": [ids] }}, got {Kind(v)}"); return null; }
        if (rank is null) return null;
        return new JsonObject { ["rank"] = rank, ["options"] = options ?? (JsonNode?)"the sheet's" };
    }

    // ---------------------------------------------------------------- gear

    JsonObject GearOf(JsonElement a, string at, string template, FoePreset? preset)
    {
        string? weapon = preset?.Weapon;
        bool? shield = preset?.Shield;
        var armor = new JsonArray();
        string gp = at + "/gear";
        // The arena's top-level weapon, shield and armor are accepted as well (not both ways).
        foreach (var f in new[] { "weapon", "shield", "armor" })
            if (Has(a, f) && a.TryGetProperty("gear", out var g0) && g0.ValueKind == JsonValueKind.Object && Has(g0, f))
                Error(ScenarioErrorCode.WrongType, ScenarioPointer.Of(at, f), $"\"{f}\" is given both here and in \"gear\"; give it once");
        JsonElement src = a;
        string sp = at;
        JsonObject? ranged = null;
        // The bow — { "weapon": a bow the module knows (Data/combat/ranged.json: "war_bow"), "arrows": 0 to 1000 (12) }.
        JsonObject? ReadRanged(JsonElement r, string rp)
        {
            if (!IsObject(r, rp, "{ \"weapon\", \"arrows\" }")) return null;
            Known(r, rp, ["weapon", "arrows"], "gear.ranged");
            string? w = Text(r, "weapon", rp, required: true, what: "a bow's name (\"war_bow\": --describe weapons)");
            if (w is not null && game is not null) w = Name(w, ScenarioPointer.Of(rp, "weapon"), game.Weapons, "bow") ?? w;
            int arrows = (int)Math.Round(Number(r, "arrows", rp, 0, 1000, "arrows") ?? 12);
            return w is null ? null : new JsonObject { ["weapon"] = w, ["arrows"] = arrows };
        }
        if (a.TryGetProperty("gear", out var gear) && gear.ValueKind != JsonValueKind.Null)
        {
            if (IsObject(gear, gp, "{ \"weapon\", \"shield\", \"armor\", \"ranged\" }"))
            {
                Known(gear, gp, GearFields, "gear");
                ReadGear(gear, gp);
            }
        }
        ReadGear(src, sp);
        void ReadGear(JsonElement g, string p)
        {
            if (Text(g, "weapon", p, what: "a 1.1 weapon's name") is { } w)
                weapon = game is null ? w : Name(w, ScenarioPointer.Of(p, "weapon"), game.Weapons, "1.1 weapon") ?? w;
            if (Flag(g, "shield", p) is { } s) shield = s;
            if (g.TryGetProperty("armor", out var av) && av.ValueKind != JsonValueKind.Null)
            {
                if (av.ValueKind != JsonValueKind.Array) Error(ScenarioErrorCode.WrongType, ScenarioPointer.Of(p, "armor"), $"must be a list of armour pieces (\"mail_shirt\"), got {Kind(av)}");
                else
                {
                    int i = 0;
                    foreach (var x in av.EnumerateArray())
                    {
                        string xp = ScenarioPointer.Of(ScenarioPointer.Of(p, "armor"), i++);
                        if (x.ValueKind != JsonValueKind.String || x.GetString()!.Length == 0) { Error(ScenarioErrorCode.WrongType, xp, $"must be an armour piece's id, got {Kind(x)}"); continue; }
                        string piece = game is null ? x.GetString()! : Name(x.GetString()!, xp, game.Armor, "armour piece") ?? x.GetString()!;
                        armor.Add(piece);
                    }
                }
            }
            if (Has(g, "ranged") && p == gp)
            {
                if (!ScenarioGame.BowBuilt) NotBuilt(gp + "/ranged", "\"gear.ranged\" (the bow)", "a later build");
                else ranged = ReadRanged(g.GetProperty("ranged"), ScenarioPointer.Of(gp, "ranged"));
            }
        }
        // Christof carries his starting sword (the arena's), else the character's own weapon.
        weapon ??= template == "Christof" ? SessionOptions.DefaultWeapon : null;
        var o = new JsonObject { ["weapon"] = weapon ?? "its own", ["shield"] = shield is null ? "as its weapon holds" : shield.Value, ["armor"] = armor };
        // A preset's bow (the archer's) unless the actor's gear gives one.
        if (ranged is null && preset?.Ranged is { } presetBow) ranged = new JsonObject { ["weapon"] = presetBow, ["arrows"] = preset.Arrows };
        if (ranged is not null) o["ranged"] = ranged;   // { "weapon", "arrows" }, resolved
        return o;
    }

    // ---------------------------------------------------------------- place and facing

    JsonNode PlaceOf(JsonElement a, string at, int index)
    {
        string pp = at + "/place";
        if (!a.TryGetProperty("place", out var p) || p.ValueKind == JsonValueKind.Null) return "default";
        if (p.ValueKind == JsonValueKind.Array)
        {
            if (p.GetArrayLength() is < 2 or > 3 || p.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.Number))
            {
                Error(ScenarioErrorCode.BadPlace, pp, "must be [x, y] or [x, y, z] (metres, the level's frame), or { \"from\": \"<actor>\", \"distanceMetres\": d, \"bearingDegrees\": b }");
                return "default";
            }
            var xs = p.EnumerateArray().Select(v => v.GetDouble()).ToArray();
            return new JsonArray([.. xs.Select(x => (JsonNode?)JsonValue.Create(x))]);
        }
        if (p.ValueKind != JsonValueKind.Object)
        {
            Error(ScenarioErrorCode.BadPlace, pp, $"must be [x, y], [x, y, z] or {{ \"from\", \"distanceMetres\", \"bearingDegrees\" }}, got {Kind(p)}");
            return "default";
        }
        Known(p, pp, RelativePlaceFields, "a relative place");
        var o = new JsonObject();
        if (!p.TryGetProperty("from", out var from)) Error(ScenarioErrorCode.BadPlace, pp + "/from", "is required: the actor this place is measured from");
        else if (Actor(from, pp + "/from") is { } other)
        {
            if (other == actorNames[index]) Error(ScenarioErrorCode.BadPlace, pp + "/from", "an actor's place cannot be measured from itself");
            o["from"] = other;
        }
        if (!Has(p, "distanceMetres")) Error(ScenarioErrorCode.BadPlace, pp + "/distanceMetres", "is required: the distance from that actor (m, feet to feet)");
        o["distanceMetres"] = Number(p, "distanceMetres", pp, 0, 1000, "m") ?? 0;
        o["bearingDegrees"] = Number(p, "bearingDegrees", pp, -3600, 3600, "degrees counter-clockwise from +X") ?? 0;
        return o;
    }

    JsonNode FacingOf(JsonElement a, string at, int index)
    {
        string fp = at + "/facing";
        if (!a.TryGetProperty("facing", out var f) || f.ValueKind == JsonValueKind.Null) return new JsonObject { ["toward"] = "its foe" };
        if (f.ValueKind == JsonValueKind.Number) return Number(f, fp, -3600, 3600, "degrees counter-clockwise from +X") ?? 0;
        if (f.ValueKind != JsonValueKind.Object) { Error(ScenarioErrorCode.WrongType, fp, $"must be degrees, or {{ \"toward\": \"<actor>\" }} or {{ \"toward\": [x, y] }}, got {Kind(f)}"); return 0; }
        Known(f, fp, ["toward"], "a facing");
        if (!f.TryGetProperty("toward", out var t)) { Error(ScenarioErrorCode.WrongType, fp + "/toward", "is required: an actor's name or [x, y]"); return 0; }
        if (t.ValueKind == JsonValueKind.String)
        {
            if (Actor(t, fp + "/toward") is { } other)
            {
                if (other == actorNames[index]) Error(ScenarioErrorCode.WrongType, fp + "/toward", "an actor cannot face itself");
                return new JsonObject { ["toward"] = other };
            }
            return 0;
        }
        if (t.ValueKind == JsonValueKind.Array && t.GetArrayLength() is 2 or 3 && t.EnumerateArray().All(v => v.ValueKind == JsonValueKind.Number))
            return new JsonObject { ["toward"] = new JsonArray([.. t.EnumerateArray().Take(2).Select(v => (JsonNode?)JsonValue.Create(v.GetDouble()))]) };
        Error(ScenarioErrorCode.WrongType, fp + "/toward", $"must be an actor's name or [x, y], got {Kind(t)}");
        return 0;
    }

    /// <summary>Relative places that go round in a circle (A from B, B from A) are refused.</summary>
    void PlaceCycles(JsonArray actors)
    {
        for (int i = 0; i < actors.Count; i++)
        {
            var seen = new List<int> { i };
            int at = i;
            while (actors[at]?["place"] is JsonObject rel && rel["from"]?.GetValue<string>() is { } from)
            {
                int next = actorNames.IndexOf(from);
                if (next < 0) break;
                if (seen.Contains(next))
                {
                    if (next == i) Error(ScenarioErrorCode.BadPlace, $"/actors/{i}/place/from", $"the relative places go round in a circle ({string.Join(" from ", seen.Select(s => actorNames[s]))} from {actorNames[next]}); one of them must be [x, y]");
                    break;
                }
                seen.Add(next);
                at = next;
            }
        }
    }

    /// <summary>The sides together (the arena's rule): none, or every actor on one of two or more; a named foe on another side.</summary>
    void Sides(JsonArray actors)
    {
        int given = actors.Count(a => a?["side"] is not null);
        if (given == 0) return;
        if (given < actors.Count)
        {
            Error(ScenarioErrorCode.BadSide, "/actors", $"\"side\" is given to {given} of the {actors.Count} actors; give every actor a side, or none (without: {string.Join(", ", actors.Where(a => a?["side"] is null).Select(a => a!["name"]!.GetValue<string>()))})");
            return;
        }
        if (actors.Select(a => a!["side"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count() < 2)
        {
            Error(ScenarioErrorCode.BadSide, "/actors", "every actor is on one side; one against several needs two sides at least");
            return;
        }
        for (int i = 0; i < actors.Count; i++)
        {
            var me = actors[i]!.AsObject();
            string mySide = me["side"]!.GetValue<string>();
            string? foe = me["foe"]?.GetValue<string>();
            int f = foe is null ? -1 : actorNames.IndexOf(foe);
            if (f < 0 || actors[f]!["side"]!.GetValue<string>() != mySide) continue;
            if (foeNamed.Contains(i)) Error(ScenarioErrorCode.BadSide, $"/actors/{i}/foe", $"its foe \"{foe}\" is on its own side \"{mySide}\"");
            else me["foe"] = actors.First(o => o!["side"]!.GetValue<string>() != mySide)!["name"]!.GetValue<string>();
        }
    }
}
