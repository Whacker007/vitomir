// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using Vitomir.Platform.Core;

namespace Vitomir.Modules.Enhanced.Sheets;

/// <summary>
/// The fixed-size part of an actor sheet, as a component: content keys (<see cref="Fnv1a64"/> of the text)
/// of its species and of its generation and era metadata, so a system can tell actors apart by species
/// without reaching into <see cref="ActorSheets"/>. Generation and era are metadata only: <b>no rule may
/// read them</b> (the rules package: "actual generation, scenario era … is metadata used to author
/// templates, not an active generic power-law input"). An entity has this component
/// exactly when it has a sheet.
/// </summary>
public struct ActorIdentity
{
    public ulong SpeciesKey;
    public ulong GenerationKey;
    public ulong EraKey;

    public static ulong KeyOf(string text) => Fnv1a64.Of(text);
}

/// <summary>Why the option gate refused (adapted from the rules package's <c>DenialCode</c>; later increments add theirs).</summary>
public enum DenialCode
{
    None,
    /// <summary>The power, or the option of it, is not learned (the package's <c>UNLEARNED_POWER</c>).</summary>
    Unlearned,
    /// <summary>The actor has no sheet.</summary>
    NoSheet,
}

/// <summary>The option gate's answer: accepted, or refused with a code and a sentence naming what is missing.</summary>
public readonly record struct Admission(bool Accepted, DenialCode Code, string Reason)
{
    public static Admission Yes => new(true, DenialCode.None, "");
}

/// <summary>
/// Every actor's sheet (the rules package's actor sheet): purchased traits in the registry's domains, effective values
/// (purchased plus temporary modifiers, the only place an Attribute may pass its cap), learned powers with
/// rank and named options, resources, familiarity by family, conditions, species, and generation and era as
/// metadata that change nothing. Registry-driven: the traits are the <see cref="TraitLayout"/>'s, whatever
/// they are. Data-oriented: purchased values and familiarity sit in flat arrays, a row per sheet, in the
/// layout's order; the variable-size parts in a small record per sheet. The state goes into the world's
/// digest in entity order (<see cref="IDigestedService"/>), and a sheet saves and loads exactly
/// (<see cref="Export"/>, <see cref="Import"/>, <see cref="SheetJson"/>). Every write checks its domain and
/// refuses by name; nothing is clamped silently.
/// </summary>
public sealed class ActorSheets : IDigestedService
{
    sealed class Row
    {
        public EntityId Owner;
        public string Species = "", Generation = "", Era = "", Template = "";
        public readonly List<ResourceState> Resources = [];            // by id, ordinal
        public readonly List<LearnedPower> Powers = [];                 // by power id, ordinal; options ordinal
        public readonly List<KeyValuePair<string, string>> Conditions = []; // by id, ordinal
        public readonly List<TemporaryModifier> Modifiers = [];        // by trait, then source, ordinal
        public readonly List<KeyValuePair<string, int>> Bought = [];    // the module's own purchases (blood_pool), by id, ordinal
        public readonly List<LivePower> Live = [];                      // the cast powers' records, by power id, ordinal
    }

    readonly World world;
    readonly int traitCount, familyCount;
    int[] rowOf = [];                  // entity index -> row + 1 (0: none)
    readonly List<Row?> rows = [];
    readonly Stack<int> freeRows = new();
    int[] purchased = [];              // row * traitCount + trait
    double[] familiarity = [];         // row * familyCount + family

    public ActorSheets(TraitLayout layout, World world)
    {
        Layout = layout;
        this.world = world;
        traitCount = layout.Count;
        familyCount = layout.Families.Count;
    }

    public TraitLayout Layout { get; }

    /// <summary>Actors with a sheet, alive, in entity order.</summary>
    public IReadOnlyList<EntityId> Actors
    {
        get
        {
            var list = new List<EntityId>();
            for (int i = 0; i < rowOf.Length; i++)
                if (rowOf[i] != 0 && rows[rowOf[i] - 1] is { } r && world.Alive(r.Owner)) list.Add(r.Owner);
            return list;
        }
    }

    public bool Has(EntityId e) => RowIndex(e) >= 0;

    // ---- giving and taking ----

    /// <summary>
    /// Gives an entity a blank sheet (every trait at its domain's minimum, familiarity at its minimum, nothing
    /// learned) and the <see cref="ActorIdentity"/> component, replacing any sheet it had.
    /// </summary>
    public void Give(EntityId e, string species, string generation = "", string era = "")
    {
        if (!world.Alive(e)) throw new InvalidOperationException($"{e} is not alive");
        int r = RowIndex(e);
        if (r < 0) r = NewRow(e);
        var row = rows[r]!;
        row.Species = species; row.Generation = generation; row.Era = era; row.Template = "";
        row.Resources.Clear(); row.Powers.Clear(); row.Conditions.Clear(); row.Modifiers.Clear();
        row.Bought.Clear(); row.Live.Clear();
        for (int t = 0; t < traitCount; t++) purchased[r * traitCount + t] = Layout.Traits[t].Domain.Min;
        for (int f = 0; f < familyCount; f++) familiarity[r * familyCount + f] = Layout.FamiliarityMin;
        WriteIdentity(e, row);
    }

    /// <summary>Takes an entity's sheet away (and its component).</summary>
    public void Take(EntityId e)
    {
        int r = RowIndex(e);
        if (r < 0) return;
        rows[r] = null;
        rowOf[e.Index] = 0;
        freeRows.Push(r);
        if (world.Has<ActorIdentity>(e)) world.Remove<ActorIdentity>(e);
    }

    // ---- metadata ----

    public string Species(EntityId e) => RowFor(e).Species;
    public string Generation(EntityId e) => RowFor(e).Generation;
    public string Era(EntityId e) => RowFor(e).Era;
    public string Template(EntityId e) => RowFor(e).Template;

    /// <summary>Changes the species and the generation and era metadata. No effective value depends on generation or era.</summary>
    public void SetMetadata(EntityId e, string species, string generation, string era)
    {
        var row = RowFor(e);
        row.Species = species; row.Generation = generation; row.Era = era;
        WriteIdentity(e, row);
    }

    // ---- traits ----

    public int Purchased(EntityId e, string traitId) => purchased[RowIndexOrThrow(e) * traitCount + Layout.Require(traitId)];

    public int Purchased(EntityId e, int trait) => purchased[RowIndexOrThrow(e) * traitCount + CheckTrait(trait)];

    /// <summary>Sets a purchased value; refuses, by name, one outside the trait's domain (−1 or 1,001 for an Attribute, 11 for an Ability).</summary>
    public void SetPurchased(EntityId e, string traitId, int value) => SetPurchased(e, Layout.Require(traitId), value);

    public void SetPurchased(EntityId e, int trait, int value)
    {
        int r = RowIndexOrThrow(e);
        var def = Layout.Traits[CheckTrait(trait)];
        if (!def.Domain.Allows(value)) throw new SheetValueException($"purchased {def.Id}", value.ToString(CultureInfo.InvariantCulture), $"{def.Domain.Min}..{def.Domain.MaxPurchased}");
        purchased[r * traitCount + trait] = value;
    }

    /// <summary>The effective value now (at the world's tick): purchased plus the live modifiers, within the trait's domain, past its cap only if the domain allows overcap.</summary>
    public int Effective(EntityId e, string traitId) => Effective(e, Layout.Require(traitId));

    public int Effective(EntityId e, int trait) => EffectiveAt(e, trait, world.Tick);

    /// <summary>The effective value at a given tick (the modifiers live at that tick).</summary>
    public int EffectiveAt(EntityId e, int trait, long tick)
    {
        int r = RowIndexOrThrow(e);
        var def = Layout.Traits[CheckTrait(trait)];
        long sum = 0;
        foreach (var m in rows[r]!.Modifiers)
            if (m.ExpiresAtTick > tick && m.TraitId == def.Id) sum += m.Delta;
        return def.Domain.Effective(purchased[r * traitCount + trait], sum);
    }

    // ---- temporary modifiers ----

    public IReadOnlyList<TemporaryModifier> Modifiers(EntityId e) => RowFor(e).Modifiers;

    /// <summary>
    /// Adds a temporary modifier, or replaces the one the same source already gives that trait (one source,
    /// one effect: no stacking duplicates). Refuses an unknown trait, a blank source, or one that has already
    /// expired.
    /// </summary>
    public void AddModifier(EntityId e, TemporaryModifier m)
    {
        var row = RowFor(e);
        Layout.Require(m.TraitId);
        if (string.IsNullOrWhiteSpace(m.SourceId)) throw new SheetValueException($"modifier of {m.TraitId}: source", "''", "a named source");
        if (m.ExpiresAtTick <= world.Tick) throw new SheetValueException($"modifier of {m.TraitId} from {m.SourceId}: expires_at_tick", m.ExpiresAtTick.ToString(CultureInfo.InvariantCulture), $"a tick after now ({world.Tick})");
        InsertSorted(row.Modifiers, m, ModifierOrder, replaceEqual: true);
    }

    public bool RemoveModifier(EntityId e, string traitId, string sourceId) =>
        RowFor(e).Modifiers.RemoveAll(m => m.TraitId == traitId && m.SourceId == sourceId) > 0;

    /// <summary>Drops every modifier whose tick has come (a later system calls this each tick; effective values ignore them already). Returns how many went.</summary>
    public int ExpireModifiers()
    {
        long now = world.Tick;
        int n = 0;
        foreach (var row in rows)
            if (row is not null) n += row.Modifiers.RemoveAll(m => m.ExpiresAtTick <= now);
        return n;
    }

    // ---- familiarity ----

    public double Familiarity(EntityId e, string family) => familiarity[RowIndexOrThrow(e) * familyCount + RequireFamily(family)];

    /// <summary>Sets a family's familiarity, a percentage in the registry's domain (0..100); refuses, by name, anything else.</summary>
    public void SetFamiliarity(EntityId e, string family, double percent)
    {
        int r = RowIndexOrThrow(e);
        int f = RequireFamily(family);
        if (!double.IsFinite(percent) || percent < Layout.FamiliarityMin || percent > Layout.FamiliarityMax)
            throw new SheetValueException($"familiarity {family}", percent.ToString(CultureInfo.InvariantCulture), $"{Layout.FamiliarityMin}..{Layout.FamiliarityMax} %");
        familiarity[r * familyCount + f] = percent;
    }

    // ---- resources ----

    public IReadOnlyList<ResourceState> Resources(EntityId e) => RowFor(e).Resources;

    public ResourceState? Resource(EntityId e, string id)
    {
        foreach (var x in RowFor(e).Resources) if (x.Id == id) return x;
        return null;
    }

    /// <summary>Sets a resource (adding it if new); refuses a current outside 0..maximum or a negative maximum or limit.</summary>
    public void SetResource(EntityId e, ResourceState r)
    {
        var row = RowFor(e);
        CheckResource(r);
        InsertSorted(row.Resources, r, (a, b) => string.CompareOrdinal(a.Id, b.Id), replaceEqual: true);
    }

    public bool RemoveResource(EntityId e, string id) => RowFor(e).Resources.RemoveAll(x => x.Id == id) > 0;

    // ---- powers ----

    public IReadOnlyList<LearnedPower> Powers(EntityId e) => RowFor(e).Powers;

    /// <summary>
    /// Learns a power at a rank (in the registry's discipline domain) with the named options, added to any
    /// already learned; learning it again changes its rank. A rank never brings options with it.
    /// </summary>
    public void Learn(EntityId e, string powerId, int rank, params string[] options)
    {
        var row = RowFor(e);
        if (!TraitLayout.IsId(powerId)) throw new SheetValueException("power id", $"'{powerId}'", "a lower-case identifier");
        if (rank < Layout.PowerRankMin || rank > Layout.PowerRankMax)
            throw new SheetValueException($"rank of power {powerId}", rank.ToString(CultureInfo.InvariantCulture), $"{Layout.PowerRankMin}..{Layout.PowerRankMax}");
        foreach (var o in options)
            if (!TraitLayout.IsId(o)) throw new SheetValueException($"option of power {powerId}", $"'{o}'", "a lower-case identifier");
        var existing = row.Powers.Find(p => p.PowerId == powerId);
        var all = new SortedSet<string>(existing?.Options ?? [], StringComparer.Ordinal);
        foreach (var o in options) all.Add(o);
        InsertSorted(row.Powers, new LearnedPower(powerId, rank, [.. all]), (a, b) => string.CompareOrdinal(a.PowerId, b.PowerId), replaceEqual: true);
    }

    public bool Unlearn(EntityId e, string powerId) => RowFor(e).Powers.RemoveAll(p => p.PowerId == powerId) > 0;

    /// <summary>A learned power's rank, or null when it is not learned.</summary>
    public int? Rank(EntityId e, string powerId) => RowFor(e).Powers.Find(p => p.PowerId == powerId)?.Rank;

    /// <summary>
    /// The option gate: may this actor use this option of this power? Refused, by name, when the actor has no
    /// sheet, has not learned the power, or has not learned that option of it — whatever its rank, its
    /// Abilities or its Attributes (the package: "a high Intelligence/Occult or actual-generation tag cannot
    /// create an unlearned magic action"). Whether the rank, the target, the resources and the senses allow
    /// the use is for later increments.
    /// </summary>
    public Admission AdmitOption(EntityId e, string powerId, string optionId)
    {
        int r = RowIndex(e);
        if (r < 0) return new Admission(false, DenialCode.NoSheet, $"{e} has no actor sheet");
        var power = rows[r]!.Powers.Find(p => p.PowerId == powerId);
        if (power is null) return new Admission(false, DenialCode.Unlearned, $"power '{powerId}' is not learned");
        if (!power.Options.Contains(optionId))
            return new Admission(false, DenialCode.Unlearned, $"option '{optionId}' of power '{powerId}' is not learned (rank {power.Rank} learns no option by itself)");
        return Admission.Yes;
    }

    // ---- the module's own purchases ----

    /// <summary>A purchase of the module's own (<see cref="TraitLayout.Purchases"/>: <c>blood_pool</c>), or null when the sheet does not hold one.</summary>
    public int? Bought(EntityId e, string id)
    {
        foreach (var kv in RowFor(e).Bought) if (kv.Key == id) return kv.Value;
        return null;
    }

    /// <summary>Sets a purchase of the module's own; refuses, by name, one the layout does not know or a value outside its domain.</summary>
    public void SetBought(EntityId e, string id, int value)
    {
        var row = RowFor(e);
        var def = Layout.Purchase(id) ?? throw new UnknownTraitException(id);
        if (!def.Domain.Allows(value)) throw new SheetValueException($"purchased {id}", value.ToString(CultureInfo.InvariantCulture), $"{def.Domain.Min}..{def.Domain.MaxPurchased}");
        InsertSorted(row.Bought, new KeyValuePair<string, int>(id, value), (a, b) => string.CompareOrdinal(a.Key, b.Key), replaceEqual: true);
    }

    public bool ClearBought(EntityId e, string id) => RowFor(e).Bought.RemoveAll(kv => kv.Key == id) > 0;

    // ---- the cast powers' records ----

    /// <summary>The cast powers' records (<see cref="LivePower"/>), by power id.</summary>
    public IReadOnlyList<LivePower> Live(EntityId e) => RowFor(e).Live;

    /// <summary>A power's record, or null.</summary>
    public LivePower? LiveOf(EntityId e, string powerId)
    {
        foreach (var l in RowFor(e).Live) if (l.PowerId == powerId) return l;
        return null;
    }

    /// <summary>Writes a power's record, replacing the one it had (one record per power: a renewal replaces, never stacks).</summary>
    public void SetLive(EntityId e, LivePower live)
    {
        var row = RowFor(e);
        if (!TraitLayout.IsId(live.PowerId)) throw new SheetValueException("live power id", $"'{live.PowerId}'", "a lower-case identifier");
        if (!TraitLayout.IsId(live.Option)) throw new SheetValueException($"live option of power {live.PowerId}", $"'{live.Option}'", "a lower-case identifier");
        if (live.EndTick < live.StartTick || live.RecastTick < live.StartTick)
            throw new SheetValueException($"live power {live.PowerId}: ticks", $"{live.StartTick}..{live.EndTick} (recast {live.RecastTick})", "an end and a recast not before the start");
        InsertSorted(row.Live, live, (a, b) => string.CompareOrdinal(a.PowerId, b.PowerId), replaceEqual: true);
    }

    /// <summary>Removes a power's record.</summary>
    public bool RemoveLive(EntityId e, string powerId)
    {
        var list = RowFor(e).Live;
        for (int i = 0; i < list.Count; i++) if (list[i].PowerId == powerId) { list.RemoveAt(i); return true; }
        return false;
    }

    // ---- conditions ----

    public IReadOnlyList<KeyValuePair<string, string>> Conditions(EntityId e) => RowFor(e).Conditions;

    public string? Condition(EntityId e, string id)
    {
        foreach (var c in RowFor(e).Conditions) if (c.Key == id) return c.Value;
        return null;
    }

    /// <summary>Sets a condition (an id and a state text, for example <c>torpor</c> → <c>true</c>).</summary>
    public void SetCondition(EntityId e, string id, string state)
    {
        var row = RowFor(e);
        if (!TraitLayout.IsId(id)) throw new SheetValueException("condition id", $"'{id}'", "a lower-case identifier");
        InsertSorted(row.Conditions, new KeyValuePair<string, string>(id, state), (a, b) => string.CompareOrdinal(a.Key, b.Key), replaceEqual: true);
    }

    public bool ClearCondition(EntityId e, string id) => RowFor(e).Conditions.RemoveAll(c => c.Key == id) > 0;

    // ---- save and load ----

    /// <summary>The sheet as data (every trait of the layout), for saving or cloning.</summary>
    public SheetData Export(EntityId e)
    {
        int r = RowIndexOrThrow(e);
        var row = rows[r]!;
        var p = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int t = 0; t < traitCount; t++) p[Layout.Traits[t].Id] = purchased[r * traitCount + t];
        foreach (var kv in row.Bought) p[kv.Key] = kv.Value;
        var f = new Dictionary<string, double>(StringComparer.Ordinal);
        for (int i = 0; i < familyCount; i++) f[Layout.Families[i]] = familiarity[r * familyCount + i];
        var c = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in row.Conditions) c[kv.Key] = kv.Value;
        return new SheetData(row.Species, row.Generation, row.Era, row.Template, p, f,
            [.. row.Resources], [.. row.Powers.Select(x => x with { Options = [.. x.Options] })], c, [.. row.Modifiers],
            row.Live.Count == 0 ? null : [.. row.Live]);
    }

    /// <summary>
    /// Gives an entity a sheet from data (a save, a clone, a template), all or nothing: every value is checked
    /// against the layout first, and the data is refused with every reason (<see cref="SheetFormatException"/>)
    /// when a trait or family is missing or unknown, or any value is outside its domain.
    /// </summary>
    public void Import(EntityId e, SheetData d)
    {
        if (!world.Alive(e)) throw new InvalidOperationException($"{e} is not alive");
        var problems = new List<string>();
        foreach (var t in Layout.Traits)
        {
            if (!d.Purchased.TryGetValue(t.Id, out int v)) problems.Add($"purchased {t.Id} is missing");
            else if (!t.Domain.Allows(v)) problems.Add($"purchased {t.Id} = {v} is outside {t.Domain.Min}..{t.Domain.MaxPurchased}");
        }
        foreach (var (k, v) in d.Purchased)
        {
            if (Layout.IndexOf(k) >= 0) continue;
            if (Layout.Purchase(k) is { } bought)
            {
                if (!bought.Domain.Allows(v)) problems.Add($"purchased {k} = {v} is outside {bought.Domain.Min}..{bought.Domain.MaxPurchased}");
            }
            else problems.Add($"the layout has no trait '{k}'");
        }
        foreach (var fam in Layout.Families)
        {
            if (!d.Familiarity.TryGetValue(fam, out double v)) problems.Add($"familiarity {fam} is missing");
            else if (!double.IsFinite(v) || v < Layout.FamiliarityMin || v > Layout.FamiliarityMax) problems.Add($"familiarity {fam} = {v} is outside {Layout.FamiliarityMin}..{Layout.FamiliarityMax} %");
        }
        foreach (var k in d.Familiarity.Keys) if (Layout.FamilyIndexOf(k) < 0) problems.Add($"the layout has no familiarity family '{k}'");
        foreach (var r in d.Resources)
            try { CheckResource(r); } catch (SheetValueException x) { problems.Add(x.Message); }
        if (d.Resources.Select(r => r.Id).Distinct().Count() != d.Resources.Count) problems.Add("a resource appears twice");
        foreach (var p in d.Powers)
        {
            if (!TraitLayout.IsId(p.PowerId)) problems.Add($"power id '{p.PowerId}' is not a lower-case identifier");
            if (p.Rank < Layout.PowerRankMin || p.Rank > Layout.PowerRankMax) problems.Add($"rank of power {p.PowerId} = {p.Rank} is outside {Layout.PowerRankMin}..{Layout.PowerRankMax}");
            foreach (var o in p.Options) if (!TraitLayout.IsId(o)) problems.Add($"option '{o}' of power {p.PowerId} is not a lower-case identifier");
            if (p.Options.Distinct().Count() != p.Options.Count) problems.Add($"an option of power {p.PowerId} appears twice");
        }
        if (d.Powers.Select(p => p.PowerId).Distinct().Count() != d.Powers.Count) problems.Add("a power appears twice");
        foreach (var c in d.Conditions.Keys) if (!TraitLayout.IsId(c)) problems.Add($"condition id '{c}' is not a lower-case identifier");
        foreach (var m in d.Modifiers)
        {
            if (Layout.IndexOf(m.TraitId) < 0) problems.Add($"a modifier names trait '{m.TraitId}', which the layout does not have");
            if (string.IsNullOrWhiteSpace(m.SourceId)) problems.Add($"a modifier of {m.TraitId} has no source");
        }
        if (d.Modifiers.Select(m => (m.TraitId, m.SourceId)).Distinct().Count() != d.Modifiers.Count) problems.Add("one source gives one trait two modifiers");
        foreach (var l in d.LivePowers)
        {
            if (!TraitLayout.IsId(l.PowerId) || !TraitLayout.IsId(l.Option)) problems.Add($"a live power '{l.PowerId}' ('{l.Option}') is not named by lower-case identifiers");
            if (l.Rank < Layout.PowerRankMin || l.Rank > Layout.PowerRankMax) problems.Add($"rank of live power {l.PowerId} = {l.Rank} is outside {Layout.PowerRankMin}..{Layout.PowerRankMax}");
            if (l.EndTick < l.StartTick || l.RecastTick < l.StartTick) problems.Add($"live power {l.PowerId} ends or recasts before it starts");
        }
        if (d.LivePowers.Select(l => l.PowerId).Distinct().Count() != d.LivePowers.Count) problems.Add("a power is live twice (one record per power)");
        if (problems.Count > 0) throw new SheetFormatException(problems);

        Give(e, d.Species, d.Generation, d.Era);
        int row = RowIndex(e);
        var rw = rows[row]!;
        rw.Template = d.Template;
        for (int t = 0; t < traitCount; t++) purchased[row * traitCount + t] = d.Purchased[Layout.Traits[t].Id];
        for (int i = 0; i < familyCount; i++) familiarity[row * familyCount + i] = d.Familiarity[Layout.Families[i]];
        rw.Resources.AddRange(d.Resources.OrderBy(x => x.Id, StringComparer.Ordinal));
        rw.Powers.AddRange(d.Powers.OrderBy(x => x.PowerId, StringComparer.Ordinal).Select(x => x with { Options = [.. x.Options.Order(StringComparer.Ordinal)] }));
        rw.Conditions.AddRange(d.Conditions.OrderBy(x => x.Key, StringComparer.Ordinal));
        rw.Modifiers.AddRange(SheetJson.Order(d.Modifiers));
        foreach (var (k, v) in d.Purchased.OrderBy(x => x.Key, StringComparer.Ordinal))
            if (Layout.IndexOf(k) < 0) rw.Bought.Add(new KeyValuePair<string, int>(k, v));
        rw.Live.AddRange(d.LivePowers.OrderBy(x => x.PowerId, StringComparer.Ordinal));
    }

    // ---- digest ----

    /// <summary>
    /// Feeds every live sheet to the world's digest, in entity order: the entity, its metadata, its purchased
    /// values and familiarity in the layout's order, then its resources, powers, conditions and modifiers in
    /// their kept (ordinal) order. Texts go in with their lengths, so no two different sheets run together.
    /// </summary>
    public void Digest(ref Fnv1a64 hash)
    {
        int live = 0;
        for (int i = 0; i < rowOf.Length; i++)
            if (rowOf[i] != 0 && rows[rowOf[i] - 1] is { } r0 && world.Alive(r0.Owner)) live++;
        hash.Add(live);
        hash.Add(traitCount);
        hash.Add(familyCount);
        for (int i = 0; i < rowOf.Length; i++)
        {
            if (rowOf[i] == 0) continue;
            int r = rowOf[i] - 1;
            var row = rows[r]!;
            if (!world.Alive(row.Owner)) continue;
            hash.Add(row.Owner.Packed);
            Text(ref hash, row.Species); Text(ref hash, row.Generation); Text(ref hash, row.Era); Text(ref hash, row.Template);
            for (int t = 0; t < traitCount; t++) hash.Add(purchased[r * traitCount + t]);
            for (int f = 0; f < familyCount; f++) hash.Add(BitConverter.DoubleToInt64Bits(familiarity[r * familyCount + f]));
            hash.Add(row.Resources.Count);
            foreach (var x in row.Resources)
            {
                Text(ref hash, x.Id); hash.Add(x.Current); hash.Add(x.Maximum); hash.Add(x.PerActionSpendLimit); hash.Add(x.PerCycleSpendLimit);
            }
            hash.Add(row.Powers.Count);
            foreach (var p in row.Powers)
            {
                Text(ref hash, p.PowerId); hash.Add(p.Rank); hash.Add(p.Options.Count);
                foreach (var o in p.Options) Text(ref hash, o);
            }
            hash.Add(row.Conditions.Count);
            foreach (var c in row.Conditions) { Text(ref hash, c.Key); Text(ref hash, c.Value); }
            hash.Add(row.Modifiers.Count);
            foreach (var m in row.Modifiers) { Text(ref hash, m.TraitId); hash.Add(m.Delta); Text(ref hash, m.SourceId); hash.Add(m.ExpiresAtTick); }
            // The module's own purchases and the cast powers' records, only when the sheet holds any (a sheet without
            // them digests as before).
            if (row.Bought.Count == 0 && row.Live.Count == 0) continue;
            hash.Add(-14);
            hash.Add(row.Bought.Count);
            foreach (var kv in row.Bought) { Text(ref hash, kv.Key); hash.Add(kv.Value); }
            hash.Add(row.Live.Count);
            foreach (var l in row.Live)
            {
                Text(ref hash, l.PowerId); Text(ref hash, l.Option); hash.Add(l.Rank);
                hash.Add(l.StartTick); hash.Add(l.EndTick); hash.Add(l.RecastTick); hash.Add(l.ActionId); hash.Add(l.Ended ? 1 : 0);
            }
        }
    }

    /// <summary>A digest of one sheet's content (of its canonical save), for comparing two sheets in different worlds or entities.</summary>
    public ulong DigestOf(EntityId e)
    {
        var copy = Export(e);
        var h = new Fnv1a64();
        h.Add(SheetJson.Write(copy, indented: false));
        return h.Value;
    }

    // ---- inside ----

    static void Text(ref Fnv1a64 h, string s) { h.Add(s.Length); h.Add(s); }

    static readonly Comparison<TemporaryModifier> ModifierOrder = (a, b) =>
    {
        int c = string.CompareOrdinal(a.TraitId, b.TraitId);
        return c != 0 ? c : string.CompareOrdinal(a.SourceId, b.SourceId);
    };

    static void InsertSorted<T>(List<T> list, T item, Comparison<T> order, bool replaceEqual)
    {
        int lo = 0, hi = list.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            int c = order(list[mid], item);
            if (c == 0 && replaceEqual) { list[mid] = item; return; }
            if (c < 0) lo = mid + 1; else hi = mid;
        }
        list.Insert(lo, item);
    }

    static void CheckResource(ResourceState r)
    {
        if (!TraitLayout.IsId(r.Id)) throw new SheetValueException("resource id", $"'{r.Id}'", "a lower-case identifier");
        if (r.Maximum < 0) throw new SheetValueException($"maximum of resource {r.Id}", r.Maximum.ToString(CultureInfo.InvariantCulture), "0 or more");
        if (r.Current < 0 || r.Current > r.Maximum) throw new SheetValueException($"current of resource {r.Id}", r.Current.ToString(CultureInfo.InvariantCulture), $"0..{r.Maximum} (its maximum)");
        if (r.PerActionSpendLimit < 0) throw new SheetValueException($"per-action spend limit of resource {r.Id}", r.PerActionSpendLimit.ToString(CultureInfo.InvariantCulture), "0 or more");
        if (r.PerCycleSpendLimit < 0) throw new SheetValueException($"per-cycle spend limit of resource {r.Id}", r.PerCycleSpendLimit.ToString(CultureInfo.InvariantCulture), "0 or more");
    }

    int CheckTrait(int trait) =>
        trait >= 0 && trait < traitCount ? trait : throw new ArgumentOutOfRangeException(nameof(trait), $"the layout has {traitCount} traits");

    int RequireFamily(string family) =>
        Layout.FamilyIndexOf(family) is int f and >= 0 ? f : throw new SheetValueException("familiarity family", $"'{family}'", $"one of {string.Join(", ", Layout.Families)}");

    int RowIndex(EntityId e)
    {
        if (e.Index < 0 || e.Index >= rowOf.Length || rowOf[e.Index] == 0) return -1;
        int r = rowOf[e.Index] - 1;
        return rows[r] is { } row && row.Owner == e && world.Alive(e) ? r : -1;
    }

    int RowIndexOrThrow(EntityId e) => RowIndex(e) is int r and >= 0 ? r : throw new KeyNotFoundException($"{e} has no actor sheet");

    Row RowFor(EntityId e) => rows[RowIndexOrThrow(e)]!;

    int NewRow(EntityId e)
    {
        if (e.Index >= rowOf.Length) Array.Resize(ref rowOf, Math.Max(e.Index + 1, rowOf.Length * 2));
        // A stale sheet left by an entity destroyed in this slot goes now.
        if (rowOf[e.Index] != 0) { int old = rowOf[e.Index] - 1; rows[old] = null; freeRows.Push(old); rowOf[e.Index] = 0; }
        int r;
        if (freeRows.Count > 0) r = freeRows.Pop();
        else
        {
            r = rows.Count;
            rows.Add(null);
            int need = rows.Count;
            if (purchased.Length < need * traitCount) Array.Resize(ref purchased, Math.Max(need * traitCount, purchased.Length * 2));
            if (familiarity.Length < need * familyCount) Array.Resize(ref familiarity, Math.Max(need * familyCount, familiarity.Length * 2));
        }
        rows[r] = new Row { Owner = e };
        rowOf[e.Index] = r + 1;
        return r;
    }

    void WriteIdentity(EntityId e, Row row) =>
        world.Set(e, new ActorIdentity
        {
            SpeciesKey = ActorIdentity.KeyOf(row.Species),
            GenerationKey = ActorIdentity.KeyOf(row.Generation),
            EraKey = ActorIdentity.KeyOf(row.Era),
        });
}
