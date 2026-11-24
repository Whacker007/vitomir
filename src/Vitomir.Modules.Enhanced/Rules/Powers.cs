// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Runtime.CompilerServices;
using Vitomir.Modules.Enhanced.Sheets;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Powers;

namespace Vitomir.Modules.Enhanced.Rules;

/// <summary>
/// Celerity's effect (ours), <b>in one place, replaceable and switchable</b>: while its record is live on a sheet, the
/// curve's move factor is the body's pace scale (<see cref="PaceScale"/>, written by <see cref="PowerSystem"/>) and its attack
/// factor multiplies the tempo (<see cref="CombatTempo"/>'s swing, recovery and block rise, written by the one writer of the
/// tempo, <see cref="SkillTempoSystem"/>).: (1 + rank)x both by default; 1.1's <c>effects.ned</c> curve with
/// the module's switch <c>celerity-curve-oneone</c>. The attack's part is provisional (<i>"multiple
/// attacks using celerity could be modeled by a single weapon swing"</i>; a rules-thread package is coming): the module's
/// switch <c>celerity-attack-off</c> leaves Celerity's movement alone. Nothing else: no casting speed, no jump, no extra
/// contact. Learned and unpaid it does nothing (no record, no factor).
/// <para><b>an earlier build: speed plus flurry</b> (<i>"something approaching option c for attacks, combined
/// with some level of speeding up things so they player can do more, faster"</i>; option c, one swing standing for several
/// attacks). A curve's <c>move</c> and <c>attack</c> are the <b>actions</b> A by rank (Revised's 1 + rank for <c>split</c>
/// and <c>one-plus-rank</c>; 1.1's factors for <c>oneone</c>); its exponent α splits them into a <b>body tempo</b>
/// <c>T = A^α</c> (the pace and the swing, recovery and block rise) and a <b>flurry</b> <c>K = A / T</c>, the blows each real
/// swing that lands on a body carries (the staged harm resolves them: <see cref="HarmSystem"/>), so <c>T × K = A</c>. An
/// exponent of 1 (a curve that names none: <c>one-plus-rank</c>, the earlier build's pure tempo under the switch
/// <c>celerity-curve-tempo</c>, and <c>oneone</c>) is T = A and K = 1 exactly. With the attack's part off, no tempo and no
/// flurry (movement only). The tempi and flurries are worked out once (<see cref="DetMath.Pow"/>), never per tick.</para>
/// </summary>
public sealed class CelerityEffect
{
    readonly float[] moveTempo, attackTempo;
    readonly double[] flurry;

    public CelerityEffect(PowerDef power, PowerCurve curve, bool attackOn)
    {
        Power = power;
        Curve = curve;
        AttackOn = attackOn;
        int n = Math.Max(curve.Move.Count, curve.Attack.Count);
        moveTempo = new float[n];
        attackTempo = new float[n];
        flurry = new double[n];
        for (int i = 0; i < n; i++)
        {
            moveTempo[i] = Split(curve.MoveAt(i + 1), curve.MoveExponent).Tempo;
            (attackTempo[i], flurry[i]) = Split(curve.AttackAt(i + 1), curve.AttackExponent);
        }
    }

    public PowerDef Power { get; }
    public PowerCurve Curve { get; }
    public bool AttackOn { get; }

    /// <summary>
    /// The actions <paramref name="actions"/> (A) split by <paramref name="exponent"/> (α) into the body tempo
    /// <c>T = A^α</c> and the flurry <c>K = A / T</c> (so <c>T × K = A</c>); α = 1 is T = A, K = 1 exactly (no flurry).
    /// </summary>
    public static (float Tempo, double Flurry) Split(double actions, double exponent)
    {
        if (exponent == 1.0 || actions <= 1.0) return ((float)actions, 1.0);
        float t = DetMath.Pow((float)actions, (float)exponent);
        return (t, actions / t);
    }

    /// <summary>The move factor at a rank (the body tempo of the curve's move actions; 1 for a rank it does not hold).</summary>
    public float MoveFactor(int rank) => rank >= 1 && rank <= moveTempo.Length ? moveTempo[rank - 1] : 1f;

    /// <summary>The attack factor at a rank (the body tempo of the curve's attack actions; 1 when the attack's part is off).</summary>
    public float AttackFactor(int rank) => AttackOn && rank >= 1 && rank <= attackTempo.Length ? attackTempo[rank - 1] : 1f;

    /// <summary>The flurry at a rank — the blows a swing that lands on a body carries, its own among them (1: none extra; 1 when the attack's part is off).</summary>
    public double FlurryFactor(int rank) => AttackOn && rank >= 1 && rank <= flurry.Length ? flurry[rank - 1] : 1.0;

    /// <summary>The person's flurry now (1 when Celerity is not live, or its attack's part is off).</summary>
    public double Flurry(ActorSheets sheets, EntityId e, long tick) => LiveRank(sheets, e, tick) is int r and > 0 ? FlurryFactor(r) : 1.0;

    /// <summary>The rank of the person's live Celerity at <paramref name="tick"/>, or 0 when none is live (or it has no sheet).</summary>
    public int LiveRank(ActorSheets sheets, EntityId e, long tick)
    {
        if (!sheets.Has(e)) return 0;
        var live = sheets.Live(e);
        for (int i = 0; i < live.Count; i++)
            if (live[i].PowerId == Power.Id) return live[i].LiveAt(tick) ? live[i].Rank : 0;
        return 0;
    }

    /// <summary>The person's pace factor now (1 when Celerity is not live).</summary>
    public float Move(ActorSheets sheets, EntityId e, long tick) => LiveRank(sheets, e, tick) is int r and > 0 ? MoveFactor(r) : 1f;

    /// <summary>The person's attack factor now (1 when Celerity is not live, or its attack's part is off).</summary>
    public float Attack(ActorSheets sheets, EntityId e, long tick) => LiveRank(sheets, e, tick) is int r and > 0 ? AttackFactor(r) : 1f;
}

/// <summary>The module's powers by index (the platform's <see cref="IPowerCatalogue"/>): <c>rules/powers</c>' order, each by its first option and label.</summary>
public sealed class PowerCatalogue(PowerData data) : IPowerCatalogue
{
    public int Count => data.Powers.Count;
    public string Id(int power) => data.Powers[power].Option;
    public string Label(int power) => data.Powers[power].Short;   // the data's slot_label when it gives one
    public string Name(int power) => data.Powers[power].Label;    // the full name, for words

    /// <summary>A power's index by its id (<c>celerity</c>), or −1.</summary>
    public int IndexOf(string powerId)
    {
        for (int i = 0; i < data.Powers.Count; i++) if (data.Powers[i].Id == powerId) return i;
        return -1;
    }
}

/// <summary>
/// The powers in the world (a service): their data, the catalogue, the ledger, Celerity's effect (null when the data has
/// no cast power with that effect), the Health resource the vitals show, and <see cref="Use"/>, the one way a power is used —
/// from a quick slot by <see cref="PowerSystem"/>, or by id (a test, a brain). The admission, in the contract's order:
/// a sheet (<see cref="PowerDenial.NoSheet"/>), alive (<see cref="PowerDenial.Dead"/>), the slot not empty
/// (<see cref="PowerDenial.EmptySlot"/>), the power and its option learned (<see cref="PowerDenial.Unlearned"/>), not passive
/// (<see cref="PowerDenial.Passive"/>), a rank with semantics (<see cref="PowerDenial.NoSemantics"/>: 6 to 10 by name), the
/// recast (<see cref="PowerDenial.Recast"/>): an effect it would have (<see cref="PowerDenial.NoEffect"/>: a heal at full
/// Health), Blood (<see cref="PowerDenial.NoBlood"/>); then reserve, commit, the record written on the sheet, and the effect
/// (by the data's kind): Celerity's nothing more (its effect reads the record), a temporary Attribute's modifier on the
/// effective value until the record's end (one a power: a renewal replaces it), a heal's Health at once. A refusal spends
/// nothing and has no effect.
/// </summary>
public sealed class PowerBook
{
    public PowerBook(PowerData data, BloodLedger ledger, CelerityEffect? celerity, string? healthResource)
    {
        Data = data;
        Ledger = ledger;
        Celerity = celerity;
        HealthResource = healthResource;
        Catalogue = new PowerCatalogue(data);
        var cast = new List<int>();
        for (int i = 0; i < data.Powers.Count; i++) if (data.Powers[i].Kind == PowerKind.Cast) cast.Add(i);
        castPowers = [.. cast];
        uses = new SlotUse[data.Powers.Count];
        for (int i = 0; i < uses.Length; i++) uses[i] = data.Powers[i].Effect == PowerData.EffectHeal ? SlotUse.Hurt : SlotUse.Engage;
    }

    readonly int[] castPowers;
    readonly SlotUse[] uses;

    /// <summary>When a brain would use the power at catalogue index <paramref name="power"/> (<see cref="SlotView.Use"/>): a heal when hurt, the rest in a fight.</summary>
    public SlotUse UseOf(int power) => power >= 0 && power < uses.Length ? uses[power] : SlotUse.Engage;

    public PowerData Data { get; }
    public BloodLedger Ledger { get; }
    public CelerityEffect? Celerity { get; }
    public PowerCatalogue Catalogue { get; }
    public string? HealthResource { get; }

    /// <summary>The reason of the last refusal, in words (for a test or the log: <c>not enough Blood (20 of 30)</c>).</summary>
    public string LastReason { get; private set; } = "";

    /// <summary>The catalogue index of the power in the person's quick slot <paramref name="slot"/> (1 to <see cref="QuickSlots.Count"/>), or −1: its cast powers fill slots 1, 2, … in the data's order.</summary>
    public int SlotPower(ActorSheets sheets, EntityId e, int slot)
    {
        if (slot < 1 || slot > QuickSlots.Count || !sheets.Has(e)) return -1;
        int n = 0;
        foreach (int p in castPowers)
            if (RankOf(sheets, e, Data.Powers[p].Id) > 0 && ++n == slot) return p;
        return -1;
    }

    /// <summary>Whether the person has a cast power learned (so it has quick slots to show).</summary>
    public bool HasCastPower(ActorSheets sheets, EntityId e) => SlotPower(sheets, e, 1) >= 0;

    /// <summary>A power's rank on the sheet (0 when not learned), found without allocating.</summary>
    public static int RankOf(ActorSheets sheets, EntityId e, string powerId)
    {
        var powers = sheets.Powers(e);
        for (int i = 0; i < powers.Count; i++) if (powers[i].PowerId == powerId) return powers[i].Rank;
        return 0;
    }

    /// <summary>Whether the sheet has the power's option learned, found without allocating.</summary>
    public static bool OptionLearned(ActorSheets sheets, EntityId e, string powerId, string option)
    {
        var powers = sheets.Powers(e);
        for (int i = 0; i < powers.Count; i++)
        {
            if (powers[i].PowerId != powerId) continue;
            var o = powers[i].Options;
            for (int k = 0; k < o.Count; k++) if (o[k] == option) return true;
            return false;
        }
        return false;
    }

    /// <summary>Uses the power in the person's quick slot <paramref name="slot"/> now (<see cref="PowerSystem"/> does for <see cref="PowerIntent"/>).</summary>
    public PowerDenial UseSlot(World world, EntityId e, int slot)
    {
        var sheets = world.Service<ActorSheets>();
        int power = sheets.Has(e) ? SlotPower(sheets, e, slot) : -1;
        return Admit(world, sheets, e, power, slot, slotEmpty: power < 0);
    }

    /// <summary>Uses a power by its id now (slot 0: not from a slot; a test, a brain). An id the module does not know is <see cref="PowerDenial.Unlearned"/>.</summary>
    public PowerDenial Use(World world, EntityId e, string powerId)
    {
        var sheets = world.Service<ActorSheets>();
        return Admit(world, sheets, e, Catalogue.IndexOf(powerId), 0, slotEmpty: false);
    }

    PowerDenial Admit(World world, ActorSheets sheets, EntityId e, int power, int slot, bool slotEmpty)
    {
        long tick = world.Tick;
        var def = power >= 0 ? Data.Powers[power] : null;
        string? option = def?.Option;
        if (!world.Alive(e) || !sheets.Has(e)) return Refuse(world, e, null, slot, PowerDenial.NoSheet, $"{e} has no sheet");
        if (IsDead(world, e)) return Refuse(world, e, option, slot, PowerDenial.Dead, $"{e} is dead");
        if (slotEmpty) return Refuse(world, e, null, slot, PowerDenial.EmptySlot, $"slot {slot} holds nothing");
        if (def is null) return Refuse(world, e, null, slot, PowerDenial.Unlearned, "the module has no such power");
        int rank = RankOf(sheets, e, def.Id);
        if (rank <= 0 || !OptionLearned(sheets, e, def.Id, def.Option))
            return Refuse(world, e, option, slot, PowerDenial.Unlearned, rank <= 0 ? $"{def.Label} is not learned" : $"{def.Label}'s option {def.Option} is not learned");
        if (def.Kind == PowerKind.Passive) return Refuse(world, e, option, slot, PowerDenial.Passive, $"{def.Label} is passive: always on, nothing to use");
        if (def.At(rank) is not { } at) return Refuse(world, e, option, slot, PowerDenial.NoSemantics, $"{def.Label} {rank} has no semantics here (ranks 1 to {def.Ranks.Count}; gate-29)");
        var had = sheets.LiveOf(e, def.Id);
        if (had is { } h0 && tick < h0.RecastTick) return Refuse(world, e, option, slot, PowerDenial.Recast, $"{def.Label}'s recast runs ({(h0.RecastTick - tick) / (double)World.TicksPerSecond:0.##} s left)");
        // A heal that would do nothing is refused before anything is reserved (1.1's cancast: RPG_DISC_NOBLOODHEALING).
        ResourceState? health = null;
        long cap = 0;
        if (def.Effect == PowerData.EffectHeal)
        {
            health = HealthResource is { } hr ? sheets.Resource(e, hr) : null;
            if (health is not { } hn) return Refuse(world, e, option, slot, PowerDenial.NoEffect, $"{def.Label}: {e} has no Health to heal");
            // No further than the effective maximum while anatomy is missing (the rules package's).
            cap = world.TryService<SeveranceRules>(out var sv) ? sv.EffectiveMaximum(world, e, hn.Maximum) : hn.Maximum;
            if (hn.Current >= cap)
                return Refuse(world, e, option, slot, PowerDenial.NoEffect, cap < hn.Maximum
                    ? $"{def.Label}: Health is at its effective maximum ({hn.Current} of {cap}, of {hn.Maximum} bought: {SeveranceData.Text(SeveranceRules.Missing(world, e))} missing)"
                    : $"{def.Label}: Health is full ({hn.Current} of {hn.Maximum})");
        }
        if (!Ledger.HasBlood(e)) return Refuse(world, e, option, slot, PowerDenial.NoBlood, "no Blood to spend");
        long id = Ledger.NewActionId();
        if (!Ledger.TryReserve(e, at.Cost, id, out string why)) return Refuse(world, e, option, slot, PowerDenial.NoBlood, why);
        Ledger.Commit(id, out long before, out long after);

        bool renewed = had is { } h1 && h1.LiveAt(tick);
        if (renewed && had is { } old) world.Events<PowerEnded>().Publish(new PowerEnded(e, def.Option, tick, PowerEndCause.Renewed, old.StartTick, old.ActionId, old.Rank));
        long end = tick + Ticks(at.Seconds), recast = tick + Ticks(at.Recast);
        // An instant power's record holds only its recast (ended at once: no window, no PowerEnded).
        bool instant = def.Effect == PowerData.EffectHeal;
        sheets.SetLive(e, new LivePower(def.Id, def.Option, rank, tick, end, recast, id, Ended: instant));
        int amount = 0;
        if (def.Effect == PowerData.EffectTempAttribute)
        {
            amount = def.AmountAt(rank);
            sheets.AddModifier(e, new TemporaryModifier(def.Trait, amount, def.Option, end));   // one a source and trait: a renewal replaces it
        }
        else if (instant && health is { } h)
        {
            long gained = Math.Min(def.AmountAt(rank), cap - h.Current);   // never past the (effective) maximum
            sheets.SetResource(e, h with { Current = h.Current + gained });
            amount = (int)gained;
        }
        world.Events<PowerUsed>().Publish(new PowerUsed(e, def.Option, rank, slot, tick, id, at.Cost, (int)before, (int)after, (float)at.Seconds, renewed, end, amount));
        LastReason = "";
        return PowerDenial.None;
    }

    /// <summary>
    /// A live power's effect ends with its record: a temporary Attribute's modifier goes (its tick has come already when the
    /// record expires, so the effective value is the purchased one again from that tick; on a stop before it, now). Allocates only
    /// when there is a modifier to take.
    /// </summary>
    public void EndEffect(ActorSheets sheets, EntityId e, in LivePower live)
    {
        for (int i = 0; i < Data.Powers.Count; i++)
        {
            var def = Data.Powers[i];
            if (def.Id != live.PowerId) continue;
            if (def.Effect == PowerData.EffectTempAttribute) sheets.RemoveModifier(e, def.Trait, live.Option);
            return;
        }
    }

    PowerDenial Refuse(World world, EntityId e, string? option, int slot, PowerDenial why, string words)
    {
        LastReason = words;
        world.Events<PowerRefused>().Publish(new PowerRefused(e, option, slot, why, world.Tick));
        return why;
    }

    /// <summary>Whole ticks in <paramref name="seconds"/> (rounded to the nearest).</summary>
    public static long Ticks(double seconds) => (long)Math.Round(seconds * World.TicksPerSecond, MidpointRounding.AwayFromZero);

    Store<Dead>? dead;
    int deadKnown;

    /// <summary>Whether the person is stopped (the platform's <see cref="Dead"/>, a zero-Health stop).</summary>
    public bool IsDead(World world, EntityId e)
    {
        if (deadKnown == 0)
        {
            deadKnown = 2;
            if (world.TryStore<Dead>(out var s)) { dead = s; deadKnown = 1; }
        }
        return dead is not null && dead.Has(e);
    }
}

/// <summary>
/// The powers in the world each tick (<see cref="Phase.Act"/>, order <see cref="PowerServices.PowersOrder"/>, before the
/// tempo and the attacks), for every person with a sheet: a live power's end at its exact tick (<see cref="PowerEnded"/>,
/// <see cref="PowerEndCause.Expired"/>) or on a zero-Health stop (<see cref="PowerEndCause.Died"/>); then each quick slot
/// pressed (<see cref="PowerIntent"/>, read and cleared: <see cref="PowerBook.UseSlot"/>); then Celerity's pace scale on the
/// body (<see cref="PaceScale"/>, present only while it is live, so a world with Celerity learned and unpaid is the world
/// without it, digest for digest); then the views — <see cref="Vitals"/> for everyone with a sheet, <see cref="PowerSlots"/>
/// for everyone with a cast power learned. Allocates nothing per tick (but the events' queues as they grow).
/// </summary>
public sealed class PowerSystem(PowerBook book) : ISystem
{
    public void Run(World world)
    {
        var sheets = world.Service<ActorSheets>();
        long tick = world.Tick;
        var people = world.Store<ActorIdentity>().Entities;
        var ended = world.Events<PowerEnded>();

        // The ends first: a press on the tick a power ends is a new use, not a renewal.
        for (int i = 0; i < people.Length; i++)
        {
            var e = people[i];
            if (!sheets.Has(e)) continue;
            var live = sheets.Live(e);
            bool dead = book.IsDead(world, e);
            for (int k = live.Count - 1; k >= 0; k--)
            {
                var l = live[k];
                if (!l.Ended && (dead || tick >= l.EndTick))
                {
                    ended.Publish(new PowerEnded(e, l.Option, tick, dead && tick < l.EndTick ? PowerEndCause.Died : PowerEndCause.Expired, l.StartTick, l.ActionId, l.Rank));
                    book.EndEffect(sheets, e, l);   // a temporary Attribute's modifier goes with it
                    l = l with { Ended = true };
                    sheets.SetLive(e, l);
                }
                if (l.Ended && tick >= l.RecastTick) sheets.RemoveLive(e, l.PowerId);
            }
        }

        // The quick slots pressed.
        if (world.TryStore<PowerIntent>(out var intents))
        {
            var pressed = intents.Entities;
            var data = intents.Data;
            for (int i = 0; i < pressed.Length; i++)
            {
                int slot = data[i].Slot;
                if (slot == 0) continue;
                data[i].Slot = 0;
                book.UseSlot(world, pressed[i], slot);
            }
        }

        // Celerity's pace and the views.
        var paces = world.Store<PaceScale>();
        var vitals = world.Store<Vitals>();
        var slots = world.Store<PowerSlots>();
        for (int i = 0; i < people.Length; i++)
        {
            var e = people[i];
            if (!sheets.Has(e)) continue;
            float f = book.Celerity is { } cel ? cel.Move(sheets, e, tick) : 1f;
            ref var pace = ref paces.Find(e);
            if (f > 1f)
            {
                if (Unsafe.IsNullRef(ref pace)) paces.Set(e, new PaceScale { Factor = f });
                else if (pace.Factor != f) pace.Factor = f;
            }
            else if (!Unsafe.IsNullRef(ref pace)) paces.Remove(e);

            if (!vitals.Has(e)) vitals.Set(e, default);
            Vitals(world, sheets, e, ref vitals.Get(e));

            if (book.HasCastPower(sheets, e))
            {
                if (!slots.Has(e)) slots.Set(e, default);
                Slots(world, sheets, e, tick, ref slots.Get(e));
            }
            else if (slots.Has(e)) slots.Remove(e);
        }
    }

    SeveranceRules? severance;
    Store<MissingParts>? missing;
    int severanceKnown;

    void Vitals(World world, ActorSheets sheets, EntityId e, ref Vitals v)
    {
        var h = book.HealthResource is { } hr ? sheets.Resource(e, hr) : null;
        var (blood, most) = book.Ledger.Blood(e);
        v.Health = h?.Current ?? 0;
        v.HealthMax = h?.Maximum ?? 0;
        // The effective maximum while anatomy is missing , 0 when nothing is; HealthMax stays the purchased.
        if (severanceKnown == 0)
        {
            severance = world.TryService<SeveranceRules>(out var sv) ? sv : null;
            missing = world.TryStore<MissingParts>(out var ms) ? ms : null;
            severanceKnown = 1;
        }
        v.HealthCap = 0;
        if (severance is not null && missing is not null && h is { } hv)
        {
            ref var m = ref missing.Find(e);
            if (!Unsafe.IsNullRef(ref m) && m.Bits != 0) v.HealthCap = hv.Maximum - severance.Data.Deficit(m.Bits, hv.Maximum);
        }
        v.Blood = blood;
        v.BloodMax = most;
        v.HasBlood = book.Ledger.HasBlood(e);
    }

    void Slots(World world, ActorSheets sheets, EntityId e, long tick, ref PowerSlots s)
    {
        bool dead = book.IsDead(world, e);
        var (blood, _) = book.Ledger.Blood(e);
        for (int k = 0; k < QuickSlots.Count; k++)
        {
            int p = book.SlotPower(sheets, e, k + 1);
            ref var view = ref s.Slots[k];
            if (p < 0) { view = SlotView.None; continue; }
            var def = book.Data.Powers[p];
            int rank = PowerBook.RankOf(sheets, e, def.Id);
            var at = def.At(rank);
            view.Power = p;
            view.Cost = at?.Cost ?? 0;
            view.Use = book.UseOf(p);
            view.Tempo = 1f;            // the: the power's own tempo while live, below
            view.SecondsLeft = view.SecondsTotal = view.RecastLeft = 0f;
            var live = sheets.LiveOf(e, def.Id);
            if (live is { } l)
            {
                if (l.LiveAt(tick))
                {
                    view.SecondsLeft = (l.EndTick - tick) / (float)World.TicksPerSecond;
                    view.SecondsTotal = (l.EndTick - l.StartTick) / (float)World.TicksPerSecond;
                    if (book.Celerity is { } cel && def.Id == cel.Power.Id) view.Tempo = cel.AttackFactor(l.Rank);
                }
                if (tick < l.RecastTick) view.RecastLeft = (l.RecastTick - tick) / (float)World.TicksPerSecond;
            }
            view.State = dead || at is null || !PowerBook.OptionLearned(sheets, e, def.Id, def.Option) ? SlotState.Unusable
                : live is { } l1 && l1.LiveAt(tick) ? SlotState.Live
                : view.RecastLeft > 0f ? SlotState.Recast
                : !book.Ledger.HasBlood(e) || blood < at.Value.Cost ? SlotState.Short
                : SlotState.Ready;
        }
    }
}

/// <summary>The powers registered.</summary>
public static class PowerServices
{
    /// <summary>In <see cref="Phase.Act"/>, before the tempo (<see cref="SkillTempoServices.TempoOrder"/>) and the attacks.</summary>
    public const int PowersOrder = 80;

    /// <summary>
    /// Registers the contract (<see cref="PowerContract.Register"/>: <see cref="PowerIntent"/>, <see cref="PowerSlots"/>,
    /// <see cref="Vitals"/>, <see cref="PaceScale"/> and the three events), the <see cref="PowerBook"/> and the
    /// <see cref="BloodLedger"/>, the platform's <see cref="IPowerCatalogue"/>, and the system <c>enhanced.powers</c>.
    /// </summary>
    public static WorldBuilder Register(WorldBuilder b, PowerBook book)
    {
        PowerContract.Register(b);
        b.Service(book);
        b.Service(book.Ledger);
        b.Service<IPowerCatalogue>(book.Catalogue);
        b.System(Phase.Act, PowersOrder, "enhanced.powers", new PowerSystem(book));
        return b;
    }
}
