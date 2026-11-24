// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Text.Json;
using Vitomir.Modules.Enhanced.Configuration;
using Vitomir.Modules.Enhanced.Sheets;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Modules.Enhanced.Rules;

/// <summary>
/// One piece of armour (ours, <c>combat/armor</c>, SANDBOX): its id and name, the slot it is worn in (one piece a slot),
/// the body regions it covers (one bit a <see cref="BodyRegion"/>), its soak in 1.1's points by kind of harm (bashing, lethal,
/// aggravated) and the dice they make (<see cref="ArmorData.PointsPerDie"/>), the 1.1 template its numbers are (empty for
/// ours) and where its numbers and its coverage come from.
/// </summary>
public sealed record ArmorPiece(string Id, string Name, string Slot, uint Covers, double[] Points, double[] Dice, string OneOne,
    string Source, string CoversSource)
{
    /// <summary>Whether it covers <paramref name="region"/>.</summary>
    public bool CoversRegion(BodyRegion region) => region != BodyRegion.None && (Covers & (1u << (int)region)) != 0;

    /// <summary>Its soak dice against harm of <paramref name="kind"/>.</summary>
    public double DiceOf(HarmKind kind) => (uint)kind < (uint)Dice.Length ? Dice[(int)kind] : 0;
}

/// <summary>How the dice of pieces worn over one another on one part are put together.</summary>
public enum ArmorLayering : byte
{
    /// <summary>They add (the default: a padded coat under mail; 1.1 adds every worn piece's soak).</summary>
    Sum,
    /// <summary>The most any of them gives.</summary>
    Highest,
}

/// <summary>
/// Armour's numbers (ours, <c>enhanced:combat/armor</c>, schema <see cref="SchemaId"/>, SANDBOX): the bridge from 1.1's
/// soak points to dice, the layering rule, which kinds of harm armour soaks (bashing, lethal, and aggravated only from the
/// classes named: the combat rules' claws and fangs), the sheet condition that keeps what a person wears, the slots, the pieces, and
/// what 1.1's people are dressed in by their template. Read strictly: a missing or wrong field is refused by name.
/// </summary>
public sealed class ArmorData
{
    public const string SchemaId = "VITOMIR_ENHANCED_ARMOR";
    public const string Asset = "combat/armor";
    public const string SumRule = "sum", HighestRule = "highest";

    public string File { get; private init; } = "";
    public string Status { get; private init; } = "";
    /// <summary>1.1's soak points to one die (20: Stamina's raw/20).</summary>
    public double PointsPerDie { get; private init; } = 20;
    public string BridgeSource { get; private init; } = "";
    public ArmorLayering Layering { get; private init; }
    public string LayeringSource { get; private init; } = "";
    /// <summary>Which kinds of harm armour soaks (by <see cref="HarmKind"/>); aggravated only from <see cref="AggravatedClasses"/>.</summary>
    public IReadOnlyList<bool> Kinds { get; private init; } = [true, true, false];
    /// <summary>The weapon classes (rules/harm's) whose aggravated harm armour soaks (claws, fangs).</summary>
    public IReadOnlyList<string> AggravatedClasses { get; private init; } = [];
    public string KindsSource { get; private init; } = "";
    /// <summary>The sheet condition that keeps what a person wears (<c>worn_armor</c>: the pieces' ids, a comma between).</summary>
    public string Condition { get; private init; } = "worn_armor";
    public IReadOnlyList<string> Slots { get; private init; } = [];
    /// <summary>The pieces, in the file's order (the order a worn set's text names them in).</summary>
    public IReadOnlyList<ArmorPiece> Pieces { get; private init; } = [];
    /// <summary>What 1.1's people wear, by the 1.1 template they are drawn as (the pieces' ids) and why.</summary>
    public IReadOnlyDictionary<string, (string[] Pieces, string Source)> Dress { get; private init; } = new Dictionary<string, (string[], string)>();

    /// <summary>A piece by its id, or null.</summary>
    public ArmorPiece? Piece(string id)
    {
        for (int i = 0; i < Pieces.Count; i++) if (Pieces[i].Id == id) return Pieces[i];
        return null;
    }

    /// <summary>The pieces a 1.1 character is dressed in (its template, as the platform draws it), or null for none.</summary>
    public string[]? DressOf(string? character) =>
        character is not null && Dress.TryGetValue(character, out var d) ? d.Pieces : null;

    /// <summary>Whether armour soaks harm of <paramref name="kind"/> from weapon class <paramref name="classId"/>.</summary>
    public bool Soaks(HarmKind kind, string? classId)
    {
        if ((uint)kind >= (uint)Kinds.Count || !Kinds[(int)kind]) return false;
        if (kind != HarmKind.Aggravated) return true;
        for (int i = 0; i < AggravatedClasses.Count; i++) if (AggravatedClasses[i] == classId) return true;
        return false;
    }

    static readonly string[] KindNames = ["bashing", "lethal", "aggravated"];

    internal static ArmorData? Read(JsonElement root, string file, List<string> problems, HarmData? harm)
    {
        int before = problems.Count;
        var c = new JsonChecks(file, problems);
        c.Schema(root, SchemaId);
        string status = c.Str(root, "status") ?? "";
        if (status.Length > 0 && status != "SANDBOX") c.Fail($"status is '{status}': armour's numbers are SANDBOX until the rules package gives them (gate-13, armor_matrix_profile)");
        _ = c.Str(root, "title");
        _ = c.Str(root, "note");

        double perDie = 20;
        string bridgeSource = "";
        if (c.Obj(root, "bridge") is { } b)
        {
            if (b.TryGetProperty("points_per_die", out var pd) && c.Num(pd, "bridge.points_per_die") is { } v)
            {
                if (!(v > 0)) c.Fail($"bridge.points_per_die must be above 0, not {v}");
                else perDie = v;
            }
            else if (!b.TryGetProperty("points_per_die", out _)) c.Fail("bridge.points_per_die is missing (1.1's soak points to one die)");
            bridgeSource = c.Str(b, "source") ?? "";
        }
        var layering = ArmorLayering.Sum;
        switch (c.Str(root, "layering"))
        {
            case SumRule: layering = ArmorLayering.Sum; break;
            case HighestRule: layering = ArmorLayering.Highest; break;
            case { } other: c.Fail($"layering '{other}' is not one this module knows ({SumRule}, {HighestRule})"); break;
        }
        string layeringSource = c.Str(root, "layering_source") ?? "";

        var kinds = new bool[3];
        var aggravated = new List<string>();
        string kindsSource = "";
        if (c.Obj(root, "kinds") is { } k)
        {
            kinds[(int)HarmKind.Bashing] = c.Bool(k, "bashing") ?? false;
            kinds[(int)HarmKind.Lethal] = c.Bool(k, "lethal") ?? false;
            if (c.Arr(k, "aggravated_classes") is { } ac)
                foreach (var x in ac.EnumerateArray())
                {
                    if (x.ValueKind != JsonValueKind.String) { c.Fail("kinds.aggravated_classes must hold class ids"); continue; }
                    string id = x.GetString()!;
                    if (harm is not null && !harm.Classes.ContainsKey(id)) c.Fail($"kinds.aggravated_classes names class '{id}', which {harm.File} does not have");
                    else if (harm is not null && harm.Classes[id].Kind != HarmKind.Aggravated) c.Fail($"kinds.aggravated_classes names class '{id}', whose harm is not aggravated");
                    aggravated.Add(id);
                }
            kinds[(int)HarmKind.Aggravated] = aggravated.Count > 0;
            kindsSource = c.Str(k, "source") ?? "";
        }
        string condition = c.Str(root, "condition") ?? "";
        if (condition.Length > 0 && !TraitLayout.IsId(condition)) c.Fail($"condition '{condition}' is not a lower-case identifier (a sheet condition's id)");

        var slots = new List<string>();
        if (c.Arr(root, "slots") is { } sl)
            foreach (var x in sl.EnumerateArray())
            {
                if (x.ValueKind != JsonValueKind.String || !TraitLayout.IsId(x.GetString())) { c.Fail($"slots: {x.GetRawText()} is not a lower-case identifier"); continue; }
                if (slots.Contains(x.GetString()!)) c.Fail($"slots names '{x.GetString()}' twice");
                else slots.Add(x.GetString()!);
            }
        _ = c.Str(root, "slots_source", required: false);

        var pieces = new List<ArmorPiece>();
        if (c.Obj(root, "pieces") is { } ps)
            foreach (var p in ps.EnumerateObject())
            {
                string where = $"pieces.{p.Name}";
                if (!TraitLayout.IsId(p.Name)) c.Fail($"{where}: '{p.Name}' is not a lower-case identifier");
                if (p.Name.Contains(',')) c.Fail($"{where}: an id holds no comma (the worn text is a list of ids)");
                var v = p.Value;
                if (v.ValueKind != JsonValueKind.Object) { c.Fail($"{where} must be an object"); continue; }
                string name = c.Str(v, "name") ?? "";
                string slot = c.Str(v, "slot") ?? "";
                if (slot.Length > 0 && !slots.Contains(slot)) c.Fail($"{where}.slot '{slot}' is not one of the slots ({string.Join(", ", slots)})");
                uint covers = 0;
                if (c.Arr(v, "covers") is { } cv)
                    foreach (var x in cv.EnumerateArray())
                    {
                        var r = x.ValueKind == JsonValueKind.String ? ArmorRules.RegionOf(x.GetString()) : BodyRegion.None;
                        if (r == BodyRegion.None) { c.Fail($"{where}.covers: {x.GetRawText()} is no body region (the hit volumes' names: head, neck, chest, belly, hips, left upper arm, …, right foot)"); continue; }
                        if ((covers & (1u << (int)r)) != 0) c.Fail($"{where}.covers names '{x.GetString()}' twice");
                        covers |= 1u << (int)r;
                    }
                if (covers == 0) c.Fail($"{where}.covers names no region: a piece covers something");
                var points = new double[3];
                if (c.Obj(v, "points") is { } pts)
                    for (int i = 0; i < 3; i++)
                    {
                        if (!pts.TryGetProperty(KindNames[i], out var n)) { c.Fail($"{where}.points.{KindNames[i]} is missing (1.1's soak points; 0 for none)"); continue; }
                        if (c.Num(n, $"{where}.points.{KindNames[i]}") is { } d)
                        {
                            if (d < 0) c.Fail($"{where}.points.{KindNames[i]} must not be below 0, not {d}");
                            else points[i] = d;
                        }
                    }
                var dice = new double[3];
                for (int i = 0; i < 3; i++) dice[i] = points[i] / perDie;
                string oneone = c.Str(v, "oneone") ?? "";
                string source = c.Str(v, "source") ?? "";
                if (source.Length == 0) c.Fail($"{where}.source is empty (every number its source)");
                string coversSource = c.Str(v, "covers_source") ?? "";
                pieces.Add(new ArmorPiece(p.Name, name, slot, covers, points, dice, oneone, source, coversSource));
            }

        var dress = new Dictionary<string, (string[], string)>(StringComparer.Ordinal);
        if (c.Obj(root, "dress") is { } dr)
            foreach (var p in dr.EnumerateObject())
            {
                string where = $"dress.{p.Name}";
                if (p.Value.ValueKind != JsonValueKind.Object) { c.Fail($"{where} must be an object"); continue; }
                var list = new List<string>();
                if (c.Arr(p.Value, "pieces") is { } pl)
                    foreach (var x in pl.EnumerateArray())
                    {
                        if (x.ValueKind != JsonValueKind.String || pieces.All(q => q.Id != x.GetString())) { c.Fail($"{where}.pieces: {x.GetRawText()} is no piece of this file"); continue; }
                        list.Add(x.GetString()!);
                    }
                string source = c.Str(p.Value, "source") ?? "";
                if (ArmorRules.SlotClash(list, pieces) is { } clash) c.Fail($"{where}: {clash}");
                dress[p.Name] = ([.. list], source);
            }

        if (problems.Count > before) return null;
        return new ArmorData
        {
            File = file, Status = status, PointsPerDie = perDie, BridgeSource = bridgeSource, Layering = layering, LayeringSource = layeringSource,
            Kinds = kinds, AggravatedClasses = aggravated, KindsSource = kindsSource, Condition = condition.Length > 0 ? condition : "worn_armor",
            Slots = slots, Pieces = pieces, Dress = dress,
        };
    }
}

/// <summary>
/// What a person wears, put together once: its pieces in the data's order, its text (the sheet condition's), and for
/// each body region and kind of harm the dice the pieces over it give (by the layering rule) and their ids
/// (<c>padded_coat+mail_shirt</c>, null where none covers). Shared by everyone wearing the same; read without allocating.
/// </summary>
public sealed class WornArmor
{
    readonly double[] dice = new double[BodyRegions.Count * 3];
    readonly string?[] label = new string?[BodyRegions.Count];

    internal WornArmor(ArmorRules rules, string text, ArmorPiece[] pieces, string? problem)
    {
        Rules = rules;
        Text = text;
        Pieces = pieces;
        Problem = problem;
        for (int r = 1; r < BodyRegions.Count; r++)
        {
            string? names = null;
            for (int k = 0; k < 3; k++)
            {
                double d = 0;
                foreach (var p in pieces)
                {
                    if ((p.Covers & (1u << r)) == 0) continue;
                    double x = p.Dice[k];
                    d = rules.Data.Layering == ArmorLayering.Sum ? d + x : Math.Max(d, x);
                }
                dice[r * 3 + k] = d;
            }
            foreach (var p in pieces)
                if ((p.Covers & (1u << r)) != 0) names = names is null ? p.Id : names + "+" + p.Id;
            label[r] = names;
        }
    }

    /// <summary>The rules it was made by (and whether they are on: <see cref="ArmorRules.On"/>).</summary>
    public ArmorRules Rules { get; }
    /// <summary>The sheet condition's text (the pieces' ids in the data's order, a comma between; empty for nothing).</summary>
    public string Text { get; }
    public IReadOnlyList<ArmorPiece> Pieces { get; }
    /// <summary>What was wrong with the text it was read from (an id that is no piece; left out), or null.</summary>
    public string? Problem { get; }
    public bool Nothing => Pieces.Count == 0;

    /// <summary>The dice the pieces over <paramref name="region"/> give against harm of <paramref name="kind"/> (0 where none covers).</summary>
    public double Dice(BodyRegion region, HarmKind kind) =>
        region == BodyRegion.None || (uint)region >= BodyRegions.Count || (uint)kind > 2 ? 0 : dice[(int)region * 3 + (int)kind];

    /// <summary>The pieces over <paramref name="region"/> (<c>padded_coat+mail_shirt</c>), or null where none covers it.</summary>
    public string? Over(BodyRegion region) => region == BodyRegion.None || (uint)region >= BodyRegions.Count ? null : label[(int)region];
}

/// <summary>What the armour did for a blow (the trace says it in words: <see cref="ArmorRules.Reason"/>).</summary>
public enum ArmorVerdict : byte
{
    /// <summary>Not judged (no armour's rules, or the blow was not staged).</summary>
    NotJudged = 0,
    /// <summary>Armour is off (<c>armor-off</c>): it soaks nothing.</summary>
    Off,
    /// <summary>The struck one wears nothing.</summary>
    NothingWorn,
    /// <summary>The contact named no body region.</summary>
    NoRegion,
    /// <summary>No piece worn covers the part struck: a gap is a gap.</summary>
    Gap,
    /// <summary>The part is covered, but armour does not soak this harm (aggravated harm not from claws or fangs).</summary>
    KindPasses,
    /// <summary>The part is covered and the armour's dice joined the soak.</summary>
    Covered,
}

/// <summary>
/// Armour in the staged harm (ours, a world service): what a person wears (the sheet's condition <see cref="ArmorData.Condition"/>,
/// its sets put together once and kept by their text), dressing a person, and the soak by the struck part —
/// <see cref="Soak"/>, called by <see cref="HarmRules.Resolve"/> after Stamina and Fortitude: the dice of the pieces over the
/// part for the harm's kind, made whole on their own (the combat rules' independent <c>ArmorPool</c>) and rolled at the soak's difficulty
/// from the soak's stream; their successes join the soak. With <see cref="On"/> false (the switch <c>armor-off</c>) nothing is
/// soaked by armour: the harm of master, outcome for outcome. Allocates nothing per blow once a set is known.
/// </summary>
public sealed class ArmorRules
{
    readonly Dictionary<string, WornArmor> sets = new(StringComparer.Ordinal);

    public ArmorRules(ArmorData data, bool on = true)
    {
        Data = data;
        On = on;
        Nothing = new WornArmor(this, "", [], null);
        sets[""] = Nothing;
    }

    public ArmorData Data { get; }
    /// <summary>Whether armour soaks (false: the switch <c>armor-off</c>).</summary>
    public bool On { get; }
    /// <summary>The set of nothing worn.</summary>
    public WornArmor Nothing { get; }

    /// <summary>A region by its name as the hit volumes' table says it (<c>"left forearm"</c>); <see cref="BodyRegion.None"/> for none or an unknown name. Allocates nothing.</summary>
    public static BodyRegion RegionOf(ReadOnlySpan<char> name)
    {
        if (name.IsEmpty) return BodyRegion.None;
        foreach (var r in BodyRegions.All)
            if (name.SequenceEqual(BodyRegions.Name(r))) return r;
        return BodyRegion.None;
    }

    /// <summary>Why <paramref name="ids"/> cannot be worn together (two pieces in one slot, or one twice), or null.</summary>
    public static string? SlotClash(IReadOnlyList<string> ids, IReadOnlyList<ArmorPiece> pieces)
    {
        for (int i = 0; i < ids.Count; i++)
            for (int j = i + 1; j < ids.Count; j++)
            {
                if (ids[i] == ids[j]) return $"'{ids[i]}' is named twice";
                var a = pieces.FirstOrDefault(p => p.Id == ids[i]);
                var b = pieces.FirstOrDefault(p => p.Id == ids[j]);
                if (a is not null && b is not null && a.Slot == b.Slot)
                    return $"'{a.Id}' and '{b.Id}' are both worn in the {a.Slot} slot (one piece a slot)";
            }
        return null;
    }

    /// <summary>
    /// The canonical text of <paramref name="ids"/> (the pieces in the data's order, a comma between), or null with the reason:
    /// an id that is no piece (the pieces named), or two pieces in one slot.
    /// </summary>
    public string? Canonical(IReadOnlyList<string> ids, out string error)
    {
        error = "";
        foreach (var id in ids)
            if (Data.Piece(id) is null)
            {
                error = $"'{id}' is no piece of armour ({Data.File}: {string.Join(", ", Data.Pieces.Select(p => p.Id))})";
                return null;
            }
        if (SlotClash(ids, Data.Pieces) is { } clash) { error = clash; return null; }
        return string.Join(",", Data.Pieces.Where(p => ids.Contains(p.Id)).Select(p => p.Id));
    }

    /// <summary>
    /// The set a sheet condition's text names: put together the first time a text is met and kept (an id that is no piece is
    /// left out, and named in <see cref="WornArmor.Problem"/>). Allocates nothing for a text already met.
    /// </summary>
    public WornArmor Set(string? text)
    {
        if (string.IsNullOrEmpty(text)) return Nothing;
        if (sets.TryGetValue(text, out var known)) return known;
        var ids = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var found = new List<ArmorPiece>();
        var unknown = new List<string>();
        foreach (var p in Data.Pieces) if (ids.Contains(p.Id)) found.Add(p);
        foreach (var id in ids) if (Data.Piece(id) is null) unknown.Add(id);
        string? problem = unknown.Count > 0 ? $"no such piece: {string.Join(", ", unknown)} (left out)" : null;
        var set = new WornArmor(this, text, [.. found], problem);
        sets[text] = set;
        return set;
    }

    /// <summary>What <paramref name="e"/> wears (its sheet's condition; <see cref="Nothing"/> without one or without a sheet).</summary>
    public WornArmor Worn(ActorSheets sheets, EntityId e) => sheets.Has(e) ? Set(sheets.Condition(e, Data.Condition)) : Nothing;

    /// <summary>
    /// Dresses <paramref name="e"/> in <paramref name="ids"/> (replacing what it wore; none takes everything off), written to its
    /// sheet's condition; false with the reason (no sheet, an id that is no piece, two in one slot).
    /// </summary>
    public bool TryWear(ActorSheets sheets, EntityId e, IReadOnlyList<string> ids, out string error)
    {
        if (!sheets.Has(e)) { error = $"{e} has no sheet"; return false; }
        if (Canonical(ids, out error) is not { } text) return false;
        if (text.Length == 0) sheets.ClearCondition(e, Data.Condition);
        else sheets.SetCondition(e, Data.Condition, text);
        return true;
    }

    /// <summary>Checks a sheet's worn text (a save's or an import's condition); null when it is good, else why.</summary>
    public string? Check(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var ids = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Canonical(ids, out string error) is null ? $"condition {Data.Condition} '{text}': {error}" : null;
    }

    /// <summary>
    /// The armour's soak for one blow (<see cref="HarmRules.Resolve"/>'s soak stage, after Stamina and Fortitude): the pieces of
    /// <paramref name="worn"/> over <paramref name="region"/> for the harm's kind, their dice made whole on their own (a draw from
    /// the pool's stream when fractional) and rolled at <paramref name="difficulty"/> from the soak's stream; written into
    /// <paramref name="r"/> (<see cref="HarmResult.ArmorPool"/>, <see cref="HarmResult.ArmorSuccesses"/>, the verdict and the
    /// pieces). Draws nothing when the armour gives nothing.
    /// </summary>
    public void Soak(WornArmor worn, BodyRegion region, HarmKind kind, string? classId, int difficulty, IDeterministicRandom random,
        string poolStream, string soakStream, HarmResult r)
    {
        r.ArmorJudged = true;
        r.ArmorRegion = region;
        r.ArmorWorn = worn.Text;
        if (!On) { r.ArmorVerdict = ArmorVerdict.Off; return; }
        if (worn.Nothing) { r.ArmorVerdict = ArmorVerdict.NothingWorn; return; }
        if (region == BodyRegion.None) { r.ArmorVerdict = ArmorVerdict.NoRegion; return; }
        r.Armor = worn.Over(region);
        if (r.Armor is null) { r.ArmorVerdict = ArmorVerdict.Gap; return; }
        if (!Data.Soaks(kind, classId)) { r.ArmorVerdict = ArmorVerdict.KindPasses; return; }
        r.ArmorVerdict = ArmorVerdict.Covered;
        r.ArmorPotential = worn.Dice(region, kind);
        r.ArmorPool = Dice.Pool(r.ArmorPotential, random, poolStream, r.Draws);
        r.ArmorSuccesses = Dice.Successes(r.ArmorPool, difficulty, random, soakStream, r.Draws);
    }

    /// <summary>The verdict in words, for the trace (built only when the trace is written).</summary>
    public static string Reason(HarmResult r) => r.ArmorVerdict switch
    {
        ArmorVerdict.NotJudged => "not judged",
        ArmorVerdict.Off => "armour is off (the switch armor-off): it soaks nothing",
        ArmorVerdict.NothingWorn => "the one struck wears no armour",
        ArmorVerdict.NoRegion => "the contact named no body region: no armour is known to cover it",
        ArmorVerdict.Gap => $"no piece worn covers the {BodyRegions.Name(r.ArmorRegion)} (worn: {r.ArmorWorn.Replace(",", ", ", StringComparison.Ordinal)}): a gap is a gap",
        ArmorVerdict.KindPasses => $"the {BodyRegions.Name(r.ArmorRegion)} is covered ({r.Armor}), but armour does not soak {r.Kind.ToString().ToLowerInvariant()} harm from {r.Class?.Id ?? "this class"} (fire and sunlight pass it; aggravated only from claws and fangs)",
        _ => string.Create(CultureInfo.InvariantCulture, $"the {BodyRegions.Name(r.ArmorRegion)} is covered by {r.Armor}: {r.ArmorPotential:0.##} dice"),
    };
}

/// <summary>Armour registered.</summary>
public static class ArmorServices
{
    /// <summary>Registers the <see cref="ArmorRules"/> service (the staged harm reads it when it is there).</summary>
    public static WorldBuilder Register(WorldBuilder b, ArmorRules rules)
    {
        b.Service(rules);
        return b;
    }
}
