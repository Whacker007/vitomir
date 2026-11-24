// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Modules.Enhanced.Configuration;
using Vitomir.Modules.Enhanced.Rules;
using Vitomir.Modules.Enhanced.Sheets;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Modules.Enhanced;

/// <summary>
/// The enhanced module's sheets as the program asks for them (ours; <see cref="IPersonSheets"/>: the arena's fighters
/// and <c>--sheet who=file</c>): a template by id, a whole sheet (the module's save format, <see cref="SheetJson"/>, or one
/// of its sheet templates' files, <see cref="SheetFiles"/>), a trait's purchased value or a power's rank — each checked by
/// <see cref="ActorSheets"/> against the registry and refused by name. The templates are the module's own: the sandbox's
/// townsperson (<see cref="SheetTemplates"/>) and the fighters of <c>Data/sheets/</c> (<c>sandbox.christof</c>,
/// <c>sandbox.guard</c>, <c>sandbox.watchman</c>). A sheet given whole gets the engine-sandbox Health when it carries none
/// (<see cref="EnhancedModule.GiveHealth"/>). <see cref="Give"/> is the research sandbox's only.
/// <para>A vampire's sheet given whole, or a template, gets its Blood (<see cref="EnhancedModule.GiveBlood"/>);
/// <see cref="TrySet"/> takes the purchased capacity <c>blood_pool</c> too (0 to 1000, refused by name outside it or on a
/// mortal); <see cref="TryLearn(EntityId, string, int, IReadOnlyList{string}, out string)"/> takes the options and refuses by
/// name one the module does not know, one of another power, and a power whose given options no rule reads;
/// <see cref="OptionsOf"/> names a power's options (<c>--learn</c>'s convenience); <see cref="TryRead"/> answers
/// <c>blood_pool</c> and <c>blood</c>.</para>
/// <para>Health's purchase <c>health_pool</c> (1 to 1000, 100 by default) the same way —
/// <see cref="TrySet"/> sets it (refused by name outside its range; the maximum follows 1:1, a full pool stays full, the
/// current held to a lower maximum), <see cref="TryRead"/> answers it and <c>health</c>; a sheet given whole keeps its own
/// Health held to its purchase's maximum (<see cref="EnhancedModule.GiveHealth"/>).</para>
/// <para><see cref="TrySet"/> takes the Health a person has now (<c>health</c>, the harm data's resource), 0 to its
/// maximum, refused by name outside it — the platform's sandbox recovery (<c>Combat/Recovery.cs</c>) gives a stopped player
/// its Health back with it (ENGINE_SANDBOX).</para>
/// </summary>
public sealed class PersonSheetsPort : IPersonSheets
{
    readonly ActorSheets sheets;
    readonly EnhancedConfig config;
    readonly HarmData? harm;
    readonly BloodData? blood;
    readonly PowerData? powers;

    /// <summary>The templates by id, each made when asked (in this order).</summary>
    readonly List<(string Id, Func<SheetData> Make)> templates;

    public PersonSheetsPort(ActorSheets sheets, EnhancedConfig config, HarmData? harm = null, IReadOnlyList<SheetFile>? files = null,
        BloodData? blood = null, PowerData? powers = null)
    {
        this.sheets = sheets;
        this.config = config;
        this.harm = harm;
        this.blood = blood;
        this.powers = powers;
        templates = [(SheetTemplates.TownspersonId, () => SheetTemplates.Townsperson(config).Sheet)];
        foreach (var f in files ?? [])
        {
            var sheet = f.Template.Sheet;
            if (f.Template.Id.Length > 0 && templates.All(t => t.Id != f.Template.Id)) templates.Add((f.Template.Id, () => sheet));
        }
    }

    public IReadOnlyList<string> Templates => [.. templates.Select(t => t.Id)];

    // ---- what a person wears (the sheet's condition worn_armor, through ArmorRules)
    /// <summary>Armour's rules (null: the module's data has none, and nobody wears anything).</summary>
    public ArmorRules? Armor { get; init; }

    /// <summary>The pieces of armour a person may wear (their ids, in the data's order).</summary>
    public IReadOnlyList<string> ArmorPieces => Armor is null ? [] : [.. Armor.Data.Pieces.Select(p => p.Id)];

    /// <summary>Dresses <paramref name="person"/> in <paramref name="pieces"/> (replacing what it wore; none undresses it); false with the reason.</summary>
    public bool TryWear(EntityId person, IReadOnlyList<string> pieces, out string error)
    {
        if (Armor is null) { error = "this module's data has no armour (combat/armor)"; return false; }
        return Armor.TryWear(sheets, person, pieces, out error);
    }

    /// <summary>What <paramref name="person"/> wears (the pieces' ids; none without a sheet or armour).</summary>
    public IReadOnlyList<string> WornBy(EntityId person) =>
        Armor is null || !sheets.Has(person) ? [] : [.. Armor.Worn(sheets, person).Pieces.Select(p => p.Id)];

    /// <summary>Why a sheet's worn armour (its condition) cannot be worn, or null (checked before a sheet is given whole).</summary>
    string? ArmorProblem(SheetData data) =>
        Armor is null || !data.Conditions.TryGetValue(Armor.Data.Condition, out var worn) ? null : Armor.Check(worn);
    // ---- end

    /// <summary>Adds a template the port offers (the module's own sheets of fighters, the first module's harm).</summary>
    public void AddTemplate(string id, Func<SheetData> make) => templates.Add((id, make));

    public bool TryTemplate(EntityId person, string templateId, out string error)
    {
        int i = templates.FindIndex(t => t.Id == templateId);
        if (i < 0) { error = $"there is no sheet template '{templateId}' (the module's: {string.Join(", ", templates.Select(t => t.Id))})"; return false; }
        return Try(() => { sheets.Import(person, templates[i].Make()); EnhancedModule.GiveHealth(sheets, harm, person); EnhancedModule.GiveBlood(sheets, blood, person); }, out error);
    }

    public bool TryImport(EntityId person, string sheetText, out string error)
    {
        try
        {
            var data = SheetFiles.FromText(sheetText, "the sheet", config.Layout);
            if (ArmorProblem(data) is { } armorProblem) { error = armorProblem; return false; }
            return Try(() => { sheets.Import(person, data); EnhancedModule.GiveHealth(sheets, harm, person); EnhancedModule.GiveBlood(sheets, blood, person); }, out error);
        }
        catch (PersonSheetException e) { error = string.Join("; ", e.Problems); return false; }
    }

    public bool TrySet(EntityId person, string traitId, int value, out string error)
    {
        if (sheets.Layout.Purchase(traitId) is not null) return TrySetBought(person, traitId, value, out error);
        // The Blood a person has now (the arena's "blood"): 0 to its maximum, refused by name otherwise.
        if (blood is not null && traitId == blood.Resource)
        {
            if (!sheets.Has(person)) { error = $"{person} has no actor sheet"; return false; }
            if (sheets.Resource(person, blood.Resource) is not { } r)
            {
                error = $"a {sheets.Species(person)} has no Blood ({blood.File}: Blood is the {string.Join(", ", blood.Species)}'s)";
                return false;
            }
            if (value < 0 || value > r.Maximum) { error = $"{traitId} {value} is outside 0 to the pool's {r.Maximum}"; return false; }
            return Try(() => sheets.SetResource(person, r with { Current = value }), out error);
        }
        // The Health a person has now (the platform's sandbox recovery): 0 to its maximum, refused by name otherwise.
        if (harm is not null && traitId == harm.HealthResource)
        {
            if (!sheets.Has(person)) { error = $"{person} has no actor sheet"; return false; }
            if (sheets.Resource(person, harm.HealthResource) is not { } h) { error = $"{person} has no {harm.HealthResource}"; return false; }
            if (value < 0 || value > h.Maximum) { error = $"{traitId} {value} is outside 0 to the maximum {h.Maximum}"; return false; }
            return Try(() => sheets.SetResource(person, h with { Current = value }), out error);
        }
        if (sheets.Layout.IndexOf(traitId) < 0)
        {
            error = $"there is no trait '{traitId}' in the registry (Attributes and Abilities by id, e.g. strength, melee)";
            return false;
        }
        return Try(() => sheets.SetPurchased(person, traitId, value), out error);
    }

    public bool TryLearn(EntityId person, string powerId, int rank, out string error)
    {
        if (rank == 0) return Try(() => sheets.Unlearn(person, powerId), out error);
        return Try(() => sheets.Learn(person, powerId, rank), out error);
    }

    /// <summary>
    /// Learns a power at a rank with the named options (<c>--learn</c>, the arena's <c>powers</c> with <c>options</c>).
    /// Refused by name: an option no power of the module has (<c>rules/powers</c>), an option of another power, and a power at a
    /// nonzero rank none of whose given options a rule reads (it would do nothing, silently). Rank 0 unlearns it.
    /// </summary>
    public bool TryLearn(EntityId person, string powerId, int rank, IReadOnlyList<string> options, out string error)
    {
        if (options.Count == 0 || rank == 0) return TryLearn(person, powerId, rank, out error);
        if (powers is null) { error = $"this module's powers are not configured, so it takes no options ({powerId})"; return false; }
        var def = powers.Find(powerId);
        var problems = new List<string>();
        foreach (var o in options)
        {
            var owner = powers.Powers.FirstOrDefault(p => p.Options.Contains(o));
            if (owner is null) problems.Add($"the module knows no option '{o}' (its options: {string.Join(", ", powers.Powers.SelectMany(p => p.Options))})");
            else if (owner.Id != powerId) problems.Add($"option '{o}' is {owner.Label}'s, not {powerId}'s");
        }
        if (def is null) problems.Add($"the module has no power '{powerId}' (its powers: {string.Join(", ", powers.Powers.Select(p => p.Id))})");
        else if (problems.Count == 0 && !options.Any(o => Read(def, o)))
            problems.Add($"none of the options {string.Join(", ", options)} of {def.Label} is read by a rule, so {def.Label} {rank} would do nothing (the read: {string.Join(", ", def.Options.Where(o => Read(def, o)))})");
        if (problems.Count > 0) { error = string.Join("; ", problems); return false; }
        return Try(() => sheets.Learn(person, powerId, rank, [.. options]), out error);
    }

    /// <summary>A power's options as the module lists them (<c>rules/powers</c>; empty for a power it does not know), for <c>--learn</c>'s convenience.</summary>
    public IReadOnlyList<string> OptionsOf(string powerId) => powers?.Find(powerId)?.Options ?? [];

    /// <summary>Whether a rule reads this option of the power: a cast power's by the powers (its option), a passive's by the staged harm (rules/harm's potence and fortitude).</summary>
    bool Read(PowerDef def, string option) => def.Kind == PowerKind.Cast
        ? option == def.Option
        : harm is not null && ((def.Id == harm.PotencePower && option == harm.PotenceOption) || (def.Id == harm.FortitudePower && option == harm.FortitudeOption));

    /// <summary>
    /// Sets a purchase of the module's own (<c>blood_pool</c>): refused by name on a person whose species has no Blood
    /// and outside its domain; then the Blood's maximum follows (1:1), its current held to it when it falls — and a full pool
    /// stays full when it rises (the arena sets values on a person just made, full).
    /// </summary>
    bool TrySetBought(EntityId person, string id, int value, out string error)
    {
        if (!sheets.Has(person)) { error = $"{person} has no actor sheet"; return false; }
        if (blood is not null && id == blood.PurchaseId && !blood.HasBlood(sheets.Species(person)))
        {
            error = $"a {sheets.Species(person)} has no Blood, so no {id} ({blood.File}: Blood is the {string.Join(", ", blood.Species)}'s)";
            return false;
        }
        var before = blood is null ? null : sheets.Resource(person, blood.Resource);
        if (!Try(() => sheets.SetBought(person, id, value), out error)) return false;
        if (blood is not null && id == blood.PurchaseId)
        {
            EnhancedModule.GiveBlood(sheets, blood, person);
            if (before is { } b && b.Current == b.Maximum && sheets.Resource(person, blood.Resource) is { } now && now.Maximum > b.Maximum)
                sheets.SetResource(person, now with { Current = now.Maximum });
        }
        // Health's purchased capacity (health_pool, 1 to 1000): the maximum follows 1:1, the current held to it when it
        // falls, a full one full when it rises (EnhancedModule.GiveHealth).
        if (harm is not null && id == harm.HealthPurchaseId) EnhancedModule.GiveHealth(sheets, harm, person);
        return true;
    }

    public bool TryRead(EntityId person, string id, out int value)
    {
        value = 0;
        if (!sheets.Has(person)) return false;
        if (sheets.Layout.IndexOf(id) >= 0) { value = sheets.Effective(person, id); return true; }
        // The purchased capacity and the Blood now.
        if (sheets.Layout.Purchase(id) is not null) { value = sheets.Bought(person, id) ?? 0; return true; }
        if (blood is not null && id == blood.Resource) { value = (int)(sheets.Resource(person, id)?.Current ?? 0); return true; }
        if (harm is not null && id == harm.HealthResource) { value = (int)(sheets.Resource(person, id)?.Current ?? 0); return true; }   // Health now
        if (!TraitLayout.IsId(id)) return false;
        value = sheets.Rank(person, id) ?? 0;
        return true;
    }

    public string Give(EntityId person, string text, string source)
    {
        if (!config.IsSandbox) throw new PersonSheetException(source, [$"sheets are given from files only in the research sandbox (profile {config.Profile.ProfileId})"]);
        var data = SheetFiles.FromText(text, source, config.Layout);
        if (ArmorProblem(data) is { } armorProblem) throw new PersonSheetException(source, [armorProblem]);
        try { sheets.Import(person, data); }
        catch (SheetFormatException e) { throw new PersonSheetException(source, e.Problems); }
        catch (SheetValueException e) { throw new PersonSheetException(source, [e.Message]); }
        bool given = EnhancedModule.GiveHealth(sheets, harm, person);
        EnhancedModule.GiveBlood(sheets, blood, person);
        var h = harm is null ? null : sheets.Resource(person, harm.HealthResource);
        return $"{person} has the sheet {(data.Template.Length > 0 ? data.Template : "(no template)")} from {source}: {data.Species}, Strength {sheets.Purchased(person, "strength")}, Dexterity {sheets.Purchased(person, "dexterity")}, Stamina {sheets.Purchased(person, "stamina")}"
               + (h is { } hh ? $", Health {hh.Current}/{hh.Maximum}{(given ? " (engine-sandbox)" : "")}" : "")
               + (blood is not null && sheets.Resource(person, blood.Resource) is { } bl ? $", Blood {bl.Current}/{bl.Maximum}" : "");
    }

    public bool TryDescribe(EntityId person, out string json)
    {
        json = "";
        if (!sheets.Has(person)) return false;
        var buffer = new System.Buffers.ArrayBufferWriter<byte>(4096);
        using (var w = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("status", "SANDBOX (the research sandbox's sheets)");
            w.WritePropertyName("sheet");
            using (var doc = System.Text.Json.JsonDocument.Parse(SheetJson.Write(sheets.Export(person), indented: false))) doc.RootElement.WriteTo(w);
            w.WriteStartObject("effective");
            foreach (var t in sheets.Layout.Traits) w.WriteNumber(t.Id, sheets.Effective(person, t.Id));
            w.WriteEndObject();
            w.WriteEndObject();
        }
        json = System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
        return true;
    }

    static bool Try(Action act, out string error)
    {
        try { act(); error = ""; return true; }
        catch (Exception e) when (e is SheetValueException or SheetFormatException or UnknownTraitException or InvalidOperationException
                                      or System.Text.Json.JsonException or KeyNotFoundException)
        {
            error = e.Message;
            return false;
        }
    }
}
