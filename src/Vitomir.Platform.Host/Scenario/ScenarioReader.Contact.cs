// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the fixed-contact kind's fields.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vitomir.Platform.Host.Scenario;

/// <summary>
/// The fixed-contact kind (<c>"kind": "contact"</c>, execution mode <c>FIXED_CONTACT</c>): a list of <c>contacts</c>, each a
/// <c>ContactMade</c> placed at a time on a region of the defender with the closing speed, squareness and place on the blade given,
/// resolved by the same harm as a swept contact; the kind's rules for the actors (no native blow: no attack step, no bow, no player's
/// input; a duelling brain is muted on attack); a contact's fields as sweep paths (<c>contacts.0.squarenessDegrees</c>,
/// <c>contacts.all.closingSpeedMetresPerSecond</c>).
/// </summary>
internal sealed partial class ScenarioReader
{
    internal static readonly string[] ContactFields = ["label", "atSeconds", "attacker", "defender", "weapon", "meets", "region", "part", "alongBladeShare",
        "closingSpeedMetresPerSecond", "squarenessDegrees", "swing", "motionRate", "peakSpeedMetresPerSecond"];

    /// <summary>The fields a sweep may set on a contact (<c>contacts.&lt;index&gt;.&lt;field&gt;</c> or <c>contacts.all.&lt;field&gt;</c>).</summary>
    internal static readonly string[] ContactSettable = ["atSeconds", "meets", "region", "part", "alongBladeShare", "closingSpeedMetresPerSecond", "squarenessDegrees",
        "swing", "motionRate", "peakSpeedMetresPerSecond"];

    /// <summary>The named places on the blade, and the share of the blade from the guard (0) to the tip (1) each stands for.</summary>
    internal static readonly string[] ContactParts = ["edge", "point", "haft"];

    /// <summary>What a contact may meet: the defender's body (a region), its shield, its blade held in guard, its blade in its parry window.</summary>
    internal static readonly string[] ContactMeets = ["body", "shield", "heldBlade", "parryingBlade"];

    /// <summary>The resolved region of a contact that meets the guard and was given none.</summary>
    internal const string NoRegion = "none (it meets the guard)";

    /// <summary>The sweep paths over <c>meets</c> whose contacts have no region (a sweep to <c>body</c> would have none to meet).</summary>
    readonly HashSet<string> meetsWithoutRegion = [];

    /// <summary>The blows a contact may stand for: a quick blow by its direction, or the strong blow.</summary>
    internal static readonly string[] ContactSwings = ["right", "left", "overhead", "thrust", "strong"];

    /// <summary>
    /// The typical contact (ENGINE_SANDBOX, ours: the earlier build's medians over Christof's quick forehand contacts with 1.1's Rapier, the
    /// fixed-contact harness's typical geometry, <c>ContactSweepTests</c>): the point closing at 25.8 m/s, at 56° to the surface, 0.4
    /// of the way along the blade.
    /// </summary>
    internal const double TypicalClosing = 25.8, TypicalSquareness = 56, EdgeShare = 0.4, PointShare = 1.0, HaftShare = 0.05;

    /// <summary>A named place's share of the blade (ENGINE_SANDBOX, ours): the edge 0.4 (the typical contact's), the point 1 (the tip), the haft 0.05 (inside the rules' hilt, 0.1: bashing).</summary>
    internal static double PartShare(string part) => part switch { "point" => PointShare, "haft" => HaftShare, _ => EdgeShare };

    /// <summary>The <c>contacts</c> list (a contact scenario's, required; refused in a fight), resolved: every default explicit.</summary>
    JsonArray? ContactsOf(JsonElement root, string kind, JsonArray actors)
    {
        bool has = root.TryGetProperty("contacts", out var list) && list.ValueKind != JsonValueKind.Null;
        if (kind != "contact")
        {
            if (has) Error(ScenarioErrorCode.BadContact, "/contacts", "only a scenario of \"kind\": \"contact\" places fixed contacts; a fight's contacts are its blows' (leave \"contacts\" out, or give \"kind\": \"contact\")");
            return null;
        }
        var o = new JsonArray();
        if (!has) { Error(ScenarioErrorCode.BadContact, "/contacts", "a contact scenario needs \"contacts\": a list of { \"atSeconds\", \"attacker\", \"defender\", \"region\", … } (section 18)"); return o; }
        if (!IsList(list, "/contacts", "a list of fixed contacts")) return o;
        int i = 0;
        foreach (var c in list.EnumerateArray())
        {
            string at = ScenarioPointer.Of("/contacts", i++);
            if (!IsObject(c, at, "a fixed contact: { \"atSeconds\", \"attacker\", \"defender\", \"region\", \"part\", \"closingSpeedMetresPerSecond\", \"squarenessDegrees\", \"swing\" }")) continue;
            o.Add(Contact(c, at, actors));
        }
        if (o.Count == 0) Error(ScenarioErrorCode.BadContact, "/contacts", "is empty: a contact scenario places at least one contact");
        return o;
    }

    JsonObject Contact(JsonElement c, string at, JsonArray actors)
    {
        Known(c, at, ContactFields, "a fixed contact");
        var o = new JsonObject();
        if (Text(c, "label", at) is { } label) o["label"] = label;
        o["atSeconds"] = c.TryGetProperty("atSeconds", out var t) && t.ValueKind != JsonValueKind.Null ? ContactField("atSeconds", t, ScenarioPointer.Of(at, "atSeconds")) : Required(at, "atSeconds", "the second of the run it is placed at (0 to 3600; rounded to the tick, 1/120 s)");
        string? attacker = c.TryGetProperty("attacker", out var av) && av.ValueKind != JsonValueKind.Null ? Actor(av, ScenarioPointer.Of(at, "attacker")) : actorNames.FirstOrDefault();
        o["attacker"] = attacker;
        int ai = attacker is null ? -1 : actorNames.IndexOf(attacker);
        string? defender = c.TryGetProperty("defender", out var dv) && dv.ValueKind != JsonValueKind.Null ? Actor(dv, ScenarioPointer.Of(at, "defender"))
            : ai >= 0 && actors[ai]?["foe"] is JsonValue fv && fv.TryGetValue(out string? foe) ? foe : null;
        if (defender is null && attacker is not null && !(c.TryGetProperty("defender", out var given) && given.ValueKind != JsonValueKind.Null))
            Error(ScenarioErrorCode.BadContact, ScenarioPointer.Of(at, "defender"), $"is required here: {attacker} has no foe to default to (give \"defender\": an actor's name)");
        if (defender is not null && defender == attacker) Error(ScenarioErrorCode.BadContact, ScenarioPointer.Of(at, "defender"), $"{defender} is the attacker too: a contact is one actor's blow on another");
        o["defender"] = defender;
        // The weapon is the one the attacker holds (the rules read the attacker's): given, it must be that one.
        string held = ai >= 0 && actors[ai]?["gear"]?["weapon"] is JsonValue hw && hw.TryGetValue(out string? hs) ? hs! : "its own";
        if (Text(c, "weapon", at) is { } weapon && held != "its own" && !weapon.Equals(held, StringComparison.OrdinalIgnoreCase))
            Error(ScenarioErrorCode.BadContact, ScenarioPointer.Of(at, "weapon"), $"\"{weapon}\" is not the weapon {attacker} holds (\"{held}\"): a fixed contact's weapon is its attacker's (the rules read what the attacker holds); set /actors/{ai}/gear/weapon, or leave \"weapon\" out", [held]);
        o["weapon"] = held == "its own" ? "its attacker's own" : held;
        var meets = Has(c, "meets") ? ContactField("meets", c.GetProperty("meets"), ScenarioPointer.Of(at, "meets")) : "body";
        o["meets"] = meets;
        bool body = meets is null || meets.GetValue<string>() == "body";
        if (!body && !Has(c, "region")) o["region"] = NoRegion;   // a guard meeting needs none (one given is kept, for a sweep of meets to body)
        else o["region"] = c.TryGetProperty("region", out var rv) && rv.ValueKind != JsonValueKind.Null ? ContactField("region", rv, ScenarioPointer.Of(at, "region"))
            : Required(at, "region", $"a region of the defender ({ScenarioPointer.Words(ScenarioWords.AimRegions.Take(11))}; with a side: \"leftUpperArm\", \"rightShin\"; without one, the side nearer the attacker)");
        bool part = Has(c, "part"), share = Has(c, "alongBladeShare");
        if (part && share) Error(ScenarioErrorCode.BadContact, ScenarioPointer.Of(at, "part"), "give \"part\" (a named place) or \"alongBladeShare\" (0 at the guard to 1 at the tip), not both");
        if (share)
        {
            o["part"] = "by share";
            o["alongBladeShare"] = ContactField("alongBladeShare", c.GetProperty("alongBladeShare"), ScenarioPointer.Of(at, "alongBladeShare"));
        }
        else
        {
            var p = part ? ContactField("part", c.GetProperty("part"), ScenarioPointer.Of(at, "part")) : "edge";
            o["part"] = p;
            o["alongBladeShare"] = PartShare(p?.GetValue<string>() ?? "edge");
        }
        o["closingSpeedMetresPerSecond"] = Has(c, "closingSpeedMetresPerSecond") ? ContactField("closingSpeedMetresPerSecond", c.GetProperty("closingSpeedMetresPerSecond"), ScenarioPointer.Of(at, "closingSpeedMetresPerSecond")) : TypicalClosing;
        o["squarenessDegrees"] = Has(c, "squarenessDegrees") ? ContactField("squarenessDegrees", c.GetProperty("squarenessDegrees"), ScenarioPointer.Of(at, "squarenessDegrees")) : TypicalSquareness;
        o["swing"] = Has(c, "swing") ? ContactField("swing", c.GetProperty("swing"), ScenarioPointer.Of(at, "swing")) : "right";
        o["motionRate"] = Has(c, "motionRate") ? ContactField("motionRate", c.GetProperty("motionRate"), ScenarioPointer.Of(at, "motionRate")) : "its own";
        o["peakSpeedMetresPerSecond"] = Has(c, "peakSpeedMetresPerSecond") ? ContactField("peakSpeedMetresPerSecond", c.GetProperty("peakSpeedMetresPerSecond"), ScenarioPointer.Of(at, "peakSpeedMetresPerSecond")) : "its blow's";
        return o;
    }

    JsonNode? Required(string at, string field, string what)
    {
        Error(ScenarioErrorCode.BadContact, ScenarioPointer.Of(at, field), $"is required: {what}");
        return null;
    }

    /// <summary>A sweep's value on a contact's field: as the field's own, and a sweep of <c>meets</c> to <c>body</c> only where each contact has a region.</summary>
    internal JsonNode? ContactSweptValue(string path, string field, JsonElement v, string at)
    {
        var value = ContactField(field, v, at);
        if (field == "meets" && value is JsonValue mv && mv.TryGetValue(out string? m) && m == "body" && meetsWithoutRegion.Contains(path))
        {
            Error(ScenarioErrorCode.BadSweep, at, $"\"body\": a contact {path} sets has no region to meet; give it a \"region\" (it is unused while it meets the guard)");
            return null;
        }
        return value;
    }

    /// <summary>One contact field's value checked (as given in the file, or as a sweep sets it).</summary>
    internal JsonNode? ContactField(string field, JsonElement v, string at) => field switch
    {
        "atSeconds" => Number(v, at, 0, 3600, "s"),
        "region" => Word(v, at, ScenarioWords.AimRegions, "a body region"),
        "meets" => Word(v, at, ContactMeets, "what the contact meets"),
        "part" => v.ValueKind == JsonValueKind.String && v.GetString()!.Equals("flat", StringComparison.OrdinalIgnoreCase)
            ? Refuse(at, "\"flat\": the rules have no flat-of-the-blade blow (a contact is the edge's or the point's; nearer the guard than 0.1 of the blade, the hilt's: bashing); give \"edge\", \"point\", \"haft\", or \"alongBladeShare\"", ["edge", "haft"])
            : Word(v, at, ContactParts, "a place on the blade (edge 0.4 of the way along it, point 1, haft 0.05)"),
        "alongBladeShare" => Number(v, at, 0, 1, "share of the blade from the guard (0) to the tip (1)"),
        "closingSpeedMetresPerSecond" => Number(v, at, 0, 200, "m/s"),
        "squarenessDegrees" => Number(v, at, 0, 90, "degrees: 90 square on, 0 sliding along the surface"),
        "swing" => Word(v, at, ContactSwings, "the blow it stands for"),
        "motionRate" => Number(v, at, 0.05, 10, "the blow's play rate: 1 is 1.1's own timing"),
        "peakSpeedMetresPerSecond" => Number(v, at, 0.01, 500, "m/s"),
        _ => null,
    };

    JsonNode? Refuse(string at, string message, IReadOnlyList<string> near)
    {
        Error(ScenarioErrorCode.OutOfRange, at, message, near);
        return null;
    }

    /// <summary>
    /// A contact scenario's rules for its actors (after the actors and triggers are read): no native blow, so no <c>attack</c> step and no
    /// bow step (<c>equip</c>, <c>aim</c>, <c>draw</c>, <c>loose</c>) in a script or a trigger, no actor on the player's input; a duelling
    /// brain is muted on attack (written in the resolved form and warned). Guarding, dodging, moving, powers and marks stay.
    /// </summary>
    void ContactKindRules(JsonElement root, JsonObject doc)
    {
        string[] native = ["attack", "equip", "aim", "draw", "loose"];
        if (doc["actors"] is not JsonArray actors) return;
        for (int i = 0; i < actors.Count; i++)
        {
            if (actors[i] is not JsonObject a || a["control"] is not JsonObject c) continue;
            string cp = $"/actors/{i}/control";
            if (c["playerInput"] is JsonArray)
            {
                Error(ScenarioErrorCode.BadContact, cp + "/playerInput", "a contact scenario's blows are its fixed contacts: no actor on the player's input (its blows would be native); use a fight scenario");
                continue;
            }
            if (c["ai"] is JsonObject aiNode && aiNode["kind"] is JsonValue kv && kv.TryGetValue(out string? aiKind) && aiKind == "duel" && c["mute"] is JsonArray mute)
            {
                if (!mute.Any(m => m!.GetValue<string>() == "attack"))
                {
                    mute.Add("attack");
                    Warnings.Add($"{cp}/mute: in a contact scenario the duelling brain of {a["name"]} is muted on attack (no native blows; its guard, dodges, moves and powers stay)");
                }
            }
            bool script = root.TryGetProperty("actors", out var ra) && ra.ValueKind == JsonValueKind.Array && ra.GetArrayLength() > i
                && ra[i].ValueKind == JsonValueKind.Object && ra[i].TryGetProperty("control", out var rc) && rc.ValueKind == JsonValueKind.Object && Has(rc, "script");
            if (c["tracks"] is JsonArray tracks)
                for (int t = 0; t < tracks.Count; t++)
                    if (tracks[t] is JsonArray track) NoNativeBlows(track, script ? cp + "/script" : $"{cp}/tracks/{t}", native);
        }
        if (doc["triggers"] is not JsonArray triggers) return;
        for (int t = 0; t < triggers.Count; t++)
        {
            if (triggers[t]?["do"] is not JsonArray dos) continue;
            for (int d = 0; d < dos.Count; d++)
                if (dos[d]?["step"] is JsonObject s) NoNativeBlows(new JsonArray(s.DeepClone()), $"/triggers/{t}/do/{d}/step", native, single: true);
        }
    }

    void NoNativeBlows(JsonArray steps, string at, string[] native, bool single = false)
    {
        for (int s = 0; s < steps.Count; s++)
        {
            if (steps[s] is not JsonObject step) continue;
            string sp = single ? at : ScenarioPointer.Of(at, s);
            foreach (var k in native)
                if (step.ContainsKey(k))
                    Error(ScenarioErrorCode.BadContact, sp, $"a \"{k}\" step makes a native blow, and a contact scenario's blows are its fixed contacts (never mixed with native ones); use a fight scenario for native blows");
            if (step["steps"] is JsonArray inner) NoNativeBlows(inner, sp + "/steps", native);
        }
    }

    /// <summary>A sweep path on a contact: <c>contacts.&lt;index&gt;.&lt;field&gt;</c> or <c>contacts.all.&lt;field&gt;</c> (<see cref="ContactSettable"/>).</summary>
    Settable? ContactPath(string given, string at, JsonObject? doc)
    {
        var parts = given.Split('.');
        int count = doc?["contacts"] is JsonArray list ? list.Count : 0;
        if (doc?["kind"]?.GetValue<string>() != "contact")
        {
            Error(ScenarioErrorCode.BadPath, at, $"\"{given}\": only a scenario of \"kind\": \"contact\" has contacts to set");
            return null;
        }
        if (parts.Length != 3)
        {
            Error(ScenarioErrorCode.BadPath, at, $"\"{given}\" is not a contact's path: contacts.<index>.<field> or contacts.all.<field> (the fields: {string.Join(", ", ContactSettable)})");
            return null;
        }
        string which = parts[1];
        if (which != "all" && !(int.TryParse(which, NumberStyles.None, CultureInfo.InvariantCulture, out int index) && index < count))
        {
            Error(ScenarioErrorCode.BadPath, at, $"\"{given}\": \"{which}\" is not one of the contacts (0 to {count - 1}) or \"all\"");
            return null;
        }
        string? field = ContactSettable.FirstOrDefault(f => f.Equals(parts[2], StringComparison.OrdinalIgnoreCase));
        if (field is null)
        {
            Error(ScenarioErrorCode.BadPath, at, $"\"{given}\": a contact has no settable field \"{parts[2]}\" (the fields: {string.Join(", ", ContactSettable)})",
                ScenarioPointer.Nearest(parts[2], ContactSettable).Select(f => $"contacts.{which}.{f}").ToList());
            return null;
        }
        if (field == "meets" && doc?["contacts"] is JsonArray contacts
            && contacts.Where((c, i) => which == "all" || which == i.ToString(CultureInfo.InvariantCulture)).Any(c => c?["region"] is JsonValue rv && rv.TryGetValue(out string? r) && r == NoRegion))
            meetsWithoutRegion.Add($"contacts.{which}.{field}");
        return new Settable($"contacts.{which}.{field}", "contact", -1, $"{which}:{field}");
    }
}
