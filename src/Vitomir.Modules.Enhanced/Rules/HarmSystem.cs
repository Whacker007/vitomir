// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Modules.Enhanced.Sheets;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Powers;

namespace Vitomir.Modules.Enhanced.Rules;

/// <summary>
/// The staged harm's running tally (digested): the last contact read (contact ids rise through a run, so one read
/// again — a duplicate — is rejected), and how many contacts were read, resolved, wounding and killing.
/// </summary>
public sealed class HarmLedger : IDigestedService
{
    public long LastContact { get; internal set; } = -1;
    public int Read { get; internal set; }
    /// <summary>The blows resolved (a contact's own and its flurry's) and of them the ones that wounded.</summary>
    public int Resolved { get; internal set; }
    public int Wounding { get; internal set; }
    public int Deaths { get; internal set; }
    public int Duplicates { get; internal set; }
    /// <summary>The extra blows Celerity's flurry resolved (each its own staged harm on its swing's contact).</summary>
    public int FlurryBlows { get; internal set; }
    /// <summary>The parts cut off, and the body contacts resolved from a chain's budget left (<see cref="ContactMade.Chain"/> above 0).</summary>
    public int Severances { get; internal set; }
    public int ChainContacts { get; internal set; }

    public void Digest(ref Fnv1a64 hash)
    {
        hash.Add(LastContact);
        hash.Add(Read); hash.Add(Resolved); hash.Add(Wounding); hash.Add(Deaths); hash.Add(Duplicates);
        hash.Add(FlurryBlows);
        hash.Add(Severances); hash.Add(ChainContacts);
    }
}

/// <summary>
/// An attacker's flurry carry (digested module state): the fraction of a blow Celerity's flurry K has carried from
/// swing to swing — each swing that lands on a body resolves <c>floor(carry + K)</c> blows (its own among them) and keeps
/// the rest. Present only while a carry is held; taken away when Celerity ends (not on a renewal).
/// </summary>
public struct FlurryCarry
{
    public double Carry;
}

/// <summary>
/// Which weapon template a person strikes with, for the staged harm: by default its character's
/// (<see cref="HarmServices.CharacterWeapon"/>, the action set's weapon); null when unknown, empty for bare hands. A
/// service, so a test or the arena may answer it otherwise.
/// </summary>
public sealed class HarmWeapons
{
    public Func<World, EntityId, string?> Of { get; set; } = HarmServices.CharacterWeapon;
}

/// <summary>
/// The staged harm in the world (<see cref="Phase.Resolve"/>, order <see cref="HarmServices.HarmOrder"/>): every
/// <see cref="ContactMade"/> of this tick (contact publishes them in <see cref="Phase.Contact"/>), in their order, once:
/// <list type="bullet">
/// <item>a contact on a <b>shield</b> or a <b>weapon</b>, or blocked before the body, is <see cref="HarmOutcome.Blocked"/>; on
/// the <b>level</b> <see cref="HarmOutcome.Missed"/>; on a <b>protected</b> person <see cref="HarmOutcome.Denied"/> — no harm,
/// no <see cref="HarmResolved"/> (no harm through a shield or a wall);</item>
/// <item>a contact on a body is resolved by <see cref="HarmRules"/> from the two sheets (the attacker's Strength and
/// Potence, the target's species, Stamina, Fortitude and Health) and the attacker's weapon: the target's Health is written
/// to its sheet, <see cref="HarmResolved"/> published, and <see cref="ActorDied"/> when its Health reaches nothing;</item>
/// <item>one it cannot resolve is <see cref="HarmOutcome.Unresolved"/>, the reason named (in the trace).</item>
/// </list>
/// Each contact read gives one trace line when the program asks (<see cref="CombatTrace"/>). No hidden miss: a body met is
/// resolved, never rolled for again. Allocates nothing on a tick without contacts; with one, only the trace's line.
/// <para><b>an earlier build: Celerity's flurry</b> (<see cref="CelerityEffect"/>). When the attacker of a body contact the
/// rules resolve has a flurry K above 1 live, the swing carries <c>floor(carry + K) − 1</c> extra blows (the fraction kept
/// in its <see cref="FlurryCarry"/>): after the swing's own blow, each extra blow is <b>a whole staged resolution of the same
/// contact</b> — its own pool, damage roll, Potence's automatic successes, the contest when on, soak and a vampire's halving,
/// its own draws from the purpose streams in that order — its own <see cref="HarmResolved"/> (<c>Flurry</c> 1, 2, …, the same
/// contact id) and trace line, its Health applied in order; none after the target stops. A shield, a held blade, the level, a
/// protected or a dead target resolves no blow at all and moves no carry. The carry is taken away when Celerity ends
/// (<see cref="PowerEnded"/>, not a renewal). A recorded departure from the package's "no duplicate harm from one contact",
/// by design.</para>
/// <para><b>an earlier build: each body contact its own staged harm from the swing's attack budget; severance</b> (<i>"each
/// hit gets it's own calculation on damage and what it does. So if a swing is hard enough from a strong enough character, it
/// could go through the arm if it severs it, and keep going"</i>; the rules package's attack budget and injury rules). A
/// swing's first body contact (<see cref="ContactMade.Chain"/> 0) sets its budget (<see cref="SwingBudget"/>) to the whole and is
/// resolved as ever; a later one (the platform's follow-through, after a cut let the blade go on) is resolved from the share
/// left, never refilled (no budget left: unresolved, by name). The swing's own blow is then judged (<see cref="SeveranceRules.Judge"/>):
/// a cut writes what the body is missing (<see cref="MissingParts"/> and the sheet's condition), publishes
/// <see cref="PartSevered"/> after the blow's <see cref="HarmResolved"/> (its <c>Severed</c>, <c>Continues</c>, <c>BudgetLeft</c>,
/// <c>StructuralLoss</c>), and the neck or the trunk stops the body (<see cref="ActorStop.SeveredStop"/>) whatever Health is
/// left; current Health is charged once, by the blow. A blow that does not cut stops the blade. A body part already
/// missing is no tissue to strike (denied). Celerity's flurry stays on the chain's first contact; its blows carry the own
/// blow's verdict and never cut.</para>
/// <para><b>an earlier build: what an extra blow is</b> (<i>"We can go with b"</i>; the rules package's
/// models). By default (<see cref="FlurryModel.Geometric"/>) the swing's own blow is resolved here as ever and its extra blows are
/// handed to the platform (<see cref="FlurryBlows.Begin"/>), which checks each at its own moment against the geometry and
/// publishes a <see cref="FlurryStrike"/>; <see cref="Strikes"/> resolves the ones that met the body (each its own staged harm on
/// the part met then, naming its logical strike, <see cref="HarmResolved.Strike"/>) and judges the ones met at a guard. The
/// switch <c>flurry-compressed</c> keeps the earlier build's loop above (its blows named as logical strikes too); <c>flurry-off</c> carries
/// none.</para>
/// </summary>
public sealed class HarmSystem : ISystem
{
    readonly HarmRules rules;
    readonly HarmResult result = new();
    IDeterministicRandom? random;
    HarmWeapons? weapons;
    int deadKnown, fightersKnown, celerityKnown;
    Store<Dead>? deadStore;
    Store<Platform.Control.Combatant>? fighters;
    IReadOnlyList<ActorDied>? diedThisTick;
    CelerityEffect? celerity;
    Store<FlurryCarry>? carries;
    SeveranceRules? severance;          // null without rules/severance (no severance; every contact stops the blade)
    Store<SwingBudget>? budgets;
    ArrowHarmRules? arrowRules;   // an arrow's harm, when the world has bows
    EventQueue<PartSevered>? severedQueue;
    ArmorRules? armor;                  // null without combat/armor (no armour)
    FlurryModel flurryModel = FlurryModel.Compressed;   // the model the module's switches chose (FlurrySettings)
    EventQueue<FlurryStrike>? strikeQueue;

    public HarmSystem(HarmRules rules) { this.rules = rules; }

    /// <summary>The last contact's result (for tests and the trace).</summary>
    public HarmResult Last => result;

    public void Run(World world)
    {
        if (celerityKnown == 0)
        {
            // Celerity's effect from the powers (registered after the harm), and the carries' store.
            celerity = world.TryService<PowerBook>(out var book) ? book.Celerity : null;
            carries = world.TryStore<FlurryCarry>(out var fc) ? fc : null;
            // Severance's rules, the budgets and the event.
            severance = world.TryService<SeveranceRules>(out var sv) ? sv : null;
            budgets = world.TryStore<SwingBudget>(out var bs) ? bs : null;
            severedQueue = world.TryService<EventQueue<PartSevered>>(out var ps) ? ps : null;
            armor = world.TryService<ArmorRules>(out var ar) ? ar : null;
            flurryModel = world.TryService<FlurrySettings>(out var fls) ? fls.Model : FlurryModel.Compressed;
            strikeQueue = world.TryService<EventQueue<FlurryStrike>>(out var sq) ? sq : null;
            celerityKnown = 1;
        }
        // A carry is taken away when Celerity ends (expired or stopped; a renewal keeps it).
        if (carries is not null && carries.Count > 0 && celerity is not null && world.TryService<EventQueue<PowerEnded>>(out var ends))
        {
            var endList = ends.ThisTick;
            for (int i = 0; i < endList.Count; i++)
                if (endList[i].Cause != PowerEndCause.Renewed && endList[i].Power == celerity.Power.Option && carries.Has(endList[i].Entity))
                    carries.Remove(endList[i].Entity);
        }
        if (strikeQueue is { } strikesNow && strikesNow.ThisTick.Count > 0) Strikes(world, strikesNow.ThisTick);
        if (!world.TryService<EventQueue<ContactMade>>(out var contacts)) return;
        var list = contacts.ThisTick;
        if (list.Count == 0) return;
        var sheets = world.Service<ActorSheets>();
        var ledger = world.Service<HarmLedger>();
        world.TryService<CombatTrace>(out var trace);
        world.TryService<EventQueue<HarmResolved>>(out var harmed);
        world.TryService<EventQueue<ActorDied>>(out var died);
        // A test may stand in for the dice (an IDeterministicRandom service: exact harm); else the world's streams.
        random ??= world.TryService<IDeterministicRandom>(out var dice) ? dice : new WorldRandom(world.Random);
        weapons ??= world.TryService<HarmWeapons>(out var hw) ? hw : new HarmWeapons();
        if (deadKnown == 0)
        {
            try { deadStore = world.Store<Dead>(); deadKnown = 1; }
            catch (InvalidOperationException) { deadKnown = 2; }
        }
        diedThisTick = died?.ThisTick;
        if (fightersKnown == 0)
        {
            try { fighters = world.Store<Platform.Control.Combatant>(); fightersKnown = 1; }
            catch (InvalidOperationException) { fightersKnown = 2; }
        }

        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i];
            if (c.ContactId <= ledger.LastContact) { ledger.Duplicates++; continue; }
            ledger.LastContact = c.ContactId;
            ledger.Read++;
            result.Reset();
            float share = BudgetFor(in c);       // the whole for the chain's first contact, else what the swing has left
            Resolve(world, sheets, in c, share);
            result.Strike = FlurryBlows.Id(c.ContactId, 0);   // the contact's own blow is its logical strike 0
            if (!Resolved(result))
            {
                SetBudget(world, in c, 0f);       // a shield, a blade, the level, a refusal: the chain ends
                if (trace is { Enabled: true }) trace.Write(HarmTrace.Line(rules, in c, result));
                continue;
            }
            if (c.Chain > 0) ledger.ChainContacts++;
            // The swing's own blow judged — severance, the blade's verdict and the budget it leaves.
            if (c.Shot == 0) Judge(world, sheets, in c);   // an arrow is not judged for severance (it stops where it met)
            SetBudget(world, in c, result.BudgetOut);
            bool continues = result.Continues, ownStops = result.StopsBody;
            float left = result.BudgetOut;
            var (angle, cutShare) = (result.Angle, result.CutShare);   // a flurry's blows carry their swing's

            // The swing's own blow, then the extra blows its flurry carried, each a whole staged resolution; on the
            // chain's first contact only (a later contact carries none).
            // Flurry-off carries none (pure tempo: no carry moved).
            int extra = c.Chain == 0 && c.Shot == 0 && !c.Fixed && flurryModel != FlurryModel.Off ? FlurryOf(sheets, c.Attacker, world.Tick) : 0;   // an arrow carries no flurry; nor a fixed contact (one blow, no swing)
            result.FlurryBlows = extra;
            Apply(world, sheets, ledger, harmed, died, in c);
            if (trace is { Enabled: true }) trace.Write(HarmTrace.Line(rules, in c, result));
            // By default each extra blow is its own strike at its own moment — the platform
            // checks it then (FlurryBlowSystem) and Strikes resolves the ones that land; none after the target stops.
            if (flurryModel == FlurryModel.Geometric)
            {
                if (extra > 0 && result.HealthAfter > 0 && !ownStops) FlurryBlows.Begin(world, in c, extra, angle, cutShare, continues, left);
                continue;
            }
            for (int k = 1; k <= extra && result.HealthAfter > 0 && !ownStops; k++)    // none after the target stops
            {
                result.Reset();
                Resolve(world, sheets, in c, 1f, flurryBlow: true);
                result.Flurry = k;
                result.FlurryBlows = extra;
                result.Continues = continues;     // the swing's verdict, its own blow's
                result.BudgetOut = left;
                (result.Angle, result.CutShare) = (angle, cutShare);
                result.Strike = FlurryBlows.Id(c.ContactId, k);       // The earlier build's translation, its logical strikes named
                result.FlurryCheck = FlurryCheck.Compressed;
                if (Resolved(result))
                {
                    ledger.FlurryBlows++;
                    strikeQueue?.Publish(new FlurryStrike(result.Strike, c.ContactId, c.Attacker, c.Swing, c.Target, k, extra, c.Tick,
                        FlurryCheck.Compressed, ContactMet.Body, GuardMeet.None, SeveranceData.RegionOf(c.Site), c.Point, 0f, c, angle, cutShare, continues, left));
                    Apply(world, sheets, ledger, harmed, died, in c);
                }
                if (trace is { Enabled: true }) trace.Write(HarmTrace.Line(rules, in c, result));
                if (!Resolved(result)) break;
            }
        }
    }

    /// <summary>
    /// The share of its swing's attack budget a contact is resolved from — the whole for the chain's first
    /// (<see cref="ContactMade.Chain"/> 0); for a later one what the swing has left (<see cref="SwingBudget"/>), or −1 when it
    /// has none (no budget kept for that swing, or nothing left).
    /// </summary>
    float BudgetFor(in ContactMade c)
    {
        if (c.Chain <= 0) return 1f;
        if (budgets is null) return -1f;
        ref var b = ref budgets.Find(c.Attacker);
        if (System.Runtime.CompilerServices.Unsafe.IsNullRef(ref b) || b.Swing != c.Swing || b.Left <= 0f) return -1f;
        return b.Left;
    }

    /// <summary>What the attacker's swing has left after the contact (0: the blade stopped). Never more than it had.</summary>
    void SetBudget(World world, in ContactMade c, float left)
    {
        if (budgets is null || !world.Alive(c.Attacker)) return;
        ref var b = ref budgets.Find(c.Attacker);
        if (System.Runtime.CompilerServices.Unsafe.IsNullRef(ref b)) budgets.Set(c.Attacker, new SwingBudget { Swing = c.Swing, Left = left });
        else
        {
            if (b.Swing == c.Swing && left > b.Left) left = b.Left;   // never refilled
            b.Swing = c.Swing;
            b.Left = left;
        }
    }

    /// <summary>The swing's own blow's severance and the blade's verdict (<see cref="SeveranceRules.Judge"/>); without severance's rules, the blade stops.</summary>
    void Judge(World world, ActorSheets sheets, in ContactMade c)
    {
        if (severance is null) return;
        long purchased = sheets.Resource(c.Target, rules.Data.HealthResource)?.Maximum ?? 0;
        // The struck body's part table, and the attacker's edge Ability (Melee; the class's own, Brawl for the claws).
        string ability = severance.Data.Edge.AbilityFor(result.Class?.Id);
        int rank = sheets.Has(c.Attacker) ? sheets.Effective(c.Attacker, ability) : -1;
        result.EdgeAbility = ability;
        severance.Judge(in c, result, SeveranceRules.Missing(world, c.Target), purchased, severance.BodyOf(world, c.Target), rank);
        result.SeveranceJudged = true;
    }

    static bool Resolved(HarmResult r) => r.Outcome is HarmOutcome.Applied or HarmOutcome.Resisted;

    /// <summary>
    /// One resolved blow: the target's Health written to its sheet (once): what it is missing after a cut,
    /// <see cref="HarmResolved"/> published: <see cref="PartSevered"/> after it, and the stop — the zero-Health stop, or
    /// <see cref="ActorStop.SeveredStop"/> when the part cut off stops the body, whatever Health is left (it wins when
    /// both come at once).
    /// </summary>
    void Apply(World world, ActorSheets sheets, HarmLedger ledger, EventQueue<HarmResolved>? harmed, EventQueue<ActorDied>? died, in ContactMade c)
    {
        ledger.Resolved++;
        if (result.Wounds > 0) ledger.Wounding++;
        var res = sheets.Resource(c.Target, rules.Data.HealthResource)!.Value;
        sheets.SetResource(c.Target, res with { Current = result.HealthAfter });
        if (result.Severed != BodyRegion.None)
        {
            ledger.Severances++;
            severance!.SetMissing(world, sheets, c.Target, result.MissingAfter);
        }
        harmed?.Publish(new HarmResolved(c.ContactId, c.Attacker, c.Target, c.Tick, result.Kind, result.DamagePool,
            result.DamageSuccesses, result.AutoSuccesses, result.SoakPool, result.SoakSuccesses, result.Wounds,
            (int)result.HealthBefore, (int)result.HealthAfter,
            result.Contested, result.AttackPool, result.AttackSuccesses, result.DefencePool, result.DefenceSuccesses, result.ContestChange,
            result.ResistedBy, result.Flurry, result.FlurryBlows, result.BandAfter,
            result.Severed, result.Continues, result.BudgetOut, (int)result.StructuralLoss,
            Angle: result.Angle, CutShare: result.CutShare,
            ArmorDice: result.ArmorPool, Armor: result.Armor, ArmorKept: result.ArmorKept,
            Strike: result.Strike,
            Quality: result.Quality, QualityFactor: result.QualityFactor, QualityRef: result.QualityRef,
            PotentialBefore: (float)result.PotentialBeforeQuality, PotentialAfter: (float)result.Potential, Hilt: result.Hilt,
            CutShareFrom: result.SeveranceJudged ? BlowQuality.CutShareFrom(result) : null));
        if (result.Severed != BodyRegion.None)
            severedQueue?.Publish(new PartSevered(c.Target, c.Attacker, result.Severed, c.Tick, c.ContactId, (int)result.StructuralLoss,
                (int)result.PostSoakHealth, result.SeverThreshold, (int)(result.PurchasedMaximum - result.DeficitAfter), (int)result.PurchasedMaximum));
        bool zero = result.Died && rules.Data.HealthAtNothing == "died";
        if (zero || result.StopsBody)
        {
            ledger.Deaths++;
            died?.Publish(new ActorDied(c.Target, c.Tick, c.ContactId, c.Attacker, result.Kind,
                result.StopsBody ? ActorStop.SeveredStop : ActorStop.ZeroHealthStop));
        }
    }

    /// <summary>
    /// The extra blows the attacker's swing carries (Celerity's flurry K while live; 0 without), moving its carry:
    /// <c>blows = floor(carry + K)</c>, <c>carry = carry + K − blows</c>, the extra blows <c>blows − 1</c>.
    /// </summary>
    int FlurryOf(ActorSheets sheets, EntityId attacker, long tick)
    {
        if (celerity is null || carries is null) return 0;
        double k = celerity.Flurry(sheets, attacker, tick);
        ref var held = ref carries.Find(attacker);
        bool has = !System.Runtime.CompilerServices.Unsafe.IsNullRef(ref held);
        if (k <= 1.0)
        {
            if (has) carries.Remove(attacker);
            return 0;
        }
        double total = (has ? held.Carry : 0.0) + k;
        int blows = (int)Math.Floor(total);
        double rest = total - blows;
        if (has) held.Carry = rest;
        else carries.Set(attacker, new FlurryCarry { Carry = rest });
        return Math.Max(0, blows - 1);
    }

    void Resolve(World world, ActorSheets sheets, in ContactMade c, float share, bool flurryBlow = false)
    {
        switch (c.Met)
        {
            case ContactMet.Shield or ContactMet.Weapon:
                result.Outcome = HarmOutcome.Blocked;
                result.Refusal = c.Met == ContactMet.Shield ? "a shield was met: no harm through a shield" : "a weapon was met (a parry): no harm";
                return;
            case ContactMet.Level:
                result.Outcome = HarmOutcome.Missed;
                result.Refusal = "the level was met: no person";
                return;
        }
        if (c.PreContactBlocked || c.HitCoverOrShield)
        {
            result.Outcome = HarmOutcome.Blocked;
            result.Refusal = c.PreContactBlocked ? "the blow met a shield or a weapon before the body" : "the contact is a shield's or cover's";
            return;
        }
        if (c.Protected)
        {
            result.Outcome = HarmOutcome.Denied;
            result.Refusal = $"{c.Target} is a protected person: found, but harm does not follow";
            return;
        }
        // No harm on the dead — a body already dead (the platform's Dead), or killed by an earlier contact this tick.
        if (IsDead(c.Target))
        {
            result.Outcome = HarmOutcome.Denied;
            result.DeadTarget = true;
            result.Refusal = $"{c.Target} is dead: no harm on the dead";
            return;
        }
        // A part already missing is no tissue to strike (contact skips its volumes; no re-charge). Not for a
        // flurry's extra blow: the attacks the swing stands for, on the person, after its own blow cut the part off.
        if (!flurryBlow && SeveranceData.RegionOf(c.Site) is var region and not BodyRegion.None && (SeveranceRules.Missing(world, c.Target) & (1u << (int)region)) != 0)
        {
            result.Outcome = HarmOutcome.Denied;
            result.Refusal = $"{c.Target}'s {c.Site} is missing: no tissue to strike";
            return;
        }
        // A later contact of a chain is resolved from what the swing has left, never afresh.
        if (share < 0f)
        {
            result.Refusal = $"swing {c.Swing}'s contact {c.Chain} in its chain has no attack budget left (the contact before it stopped the blade, or none was resolved)";
            return;
        }
        if (!world.Alive(c.Target) || !sheets.Has(c.Target)) { result.Refusal = $"{c.Target} has no actor sheet"; return; }
        if (!world.Alive(c.Attacker) || !sheets.Has(c.Attacker)) { result.Refusal = $"the attacker {c.Attacker} has no actor sheet"; return; }

        var d = rules.Data;
        var (potence, potenceLearned) = Power(sheets, c.Attacker, d.PotencePower, d.PotenceOption);
        // ---- an arrow's contact: the archer's bow (the harm data's class "arrow"), its work's dice, no Strength, no Potence.
        if (c.Shot > 0)
        {
            if (arrowRules is null)
            {
                if (!world.TryService<ArrowHarmRules>(out var found)) { result.Refusal = $"an arrow's contact (shot {c.Shot}) but the module gives no arrow's harm (no bow in this world)"; return; }
                arrowRules = found;
            }
            var arrowStriker = new Striker(c.Attacker, sheets.Species(c.Attacker), 0, 0, false, arrowRules.BowOf(world, in c),
                WorkDice: arrowRules.Harm.Dice(c.ProjectileMassKg, c.PointSpeed));
            var hp = sheets.Resource(c.Target, d.HealthResource);
            var (fort, fortLearned) = Power(sheets, c.Target, d.FortitudePower, d.FortitudeOption);
            var struck = new Struck(c.Target, sheets.Species(c.Target), sheets.Effective(c.Target, "stamina"), fort, fortLearned,
                hp is not null, hp?.Current ?? 0, hp?.Maximum ?? 0, Armor: armor?.Worn(sheets, c.Target));
            rules.Resolve(in c, in arrowStriker, in struck, random!, result, share);
            result.Arrow = true;
            result.ArrowShot = c.Shot;
            result.ArrowSpeed = c.PointSpeed;
            result.ArrowMassKg = c.ProjectileMassKg;
            result.ArrowWork = ArrowHarm.Work(c.ProjectileMassKg, c.PointSpeed);
            ArmorResisted(result);
            return;
        }
        // ---- end
        var a = new Striker(c.Attacker, sheets.Species(c.Attacker), sheets.Effective(c.Attacker, "strength"),
            potence, potenceLearned, weapons!.Of(world, c.Attacker));
        if (rules.Quality is not null)   // ---- the clip the swing plays, for its blow's quality reference
            a = a with { SwingClip = BlowQuality.ClipOf(world, in c, out var kind), Character = kind };   // a fixed contact's, the blow it stands for
        var health = sheets.Resource(c.Target, d.HealthResource);
        var (fortitude, fortitudeLearned) = Power(sheets, c.Target, d.FortitudePower, d.FortitudeOption);
        var t = new Struck(c.Target, sheets.Species(c.Target), sheets.Effective(c.Target, "stamina"),
            fortitude, fortitudeLearned, health is not null, health?.Current ?? 0, health?.Maximum ?? 0,
            Armor: armor?.Worn(sheets, c.Target));   // what it wears
        if (rules.Contest is { Data: var k })
        {
            // What the contest reads — the attacker's Dexterity, Melee and Brawl; the defender's Dexterity, Dodge and Melee, and whether it holds a weapon.
            a = a with { Dexterity = sheets.Effective(c.Attacker, k.DexterityTrait), Melee = sheets.Effective(c.Attacker, k.ArmedAbility), Brawl = sheets.Effective(c.Attacker, k.BareAbility) };
            bool armed = d.ClassOf(weapons.Of(world, c.Target)) is { } held && !k.IsBareHanded(held.Id);   // no weapon known: bare hands
            bool guarding = fighters is not null && fighters.Has(c.Target) && fighters.Get(c.Target).Blocking;   // a performed defence: the block raised
            t = t with { Dexterity = sheets.Effective(c.Target, k.DexterityTrait), Dodge = sheets.Effective(c.Target, k.DodgeAbility),
                         Melee = sheets.Effective(c.Target, k.ArmedAbility), Brawl = sheets.Effective(c.Target, k.BareAbility), Armed = armed, Guarding = guarding,
                         Dodging = SkillContest.Dodging(world, c.Target) };   // a performed dodge (its burst only)
        }
        rules.Resolve(in c, in a, in t, random!, result, share);
        ArmorResisted(result);   // ---- the armour's own "resisted"
        BlowQuality.LowWorkResisted(result);   // ---- low work, the earlier stage, ahead of the armour's reason
    }

    // ---- the armour's own "resisted" ----
    /// <summary>
    /// (The rules package: <i>"Keep NoDamage, Soak and Halving distinct; also separate ... armor
    /// protected ..."</i>): a blow the armour over the struck part turned to nothing — no wound, where the body's own soak (and a
    /// vampire's halving) alone would have let at least one through (<see cref="HarmResult.ArmorKept"/> above 0) — is resisted by
    /// <see cref="ResistedBy.Armor"/>, not <see cref="ResistedBy.Soak"/>. Everything else as the stages set it. The trace and the
    /// event carry it.
    /// </summary>
    internal static void ArmorResisted(HarmResult r)
    {
        if (r.Outcome == HarmOutcome.Resisted && r.Wounds == 0 && r.ArmorKept > 0) r.ResistedBy = ResistedBy.Armor;
    }
    // ---- end

    /// <summary>Whether <paramref name="e"/> is dead: marked by the platform's death (<see cref="Dead"/>), or killed by a contact earlier in this tick (the death is marked later in the tick).</summary>
    bool IsDead(EntityId e)
    {
        if (deadStore is not null && deadStore.Has(e)) return true;
        if (diedThisTick is { } list)
            for (int i = 0; i < list.Count; i++) if (list[i].Who == e) return true;
        return false;
    }

    /// <summary>A power's rank (0 when not learned) and whether its option is learned: the option gate's answer, found without allocating.</summary>
    static (int Rank, bool OptionLearned) Power(ActorSheets sheets, EntityId e, string power, string option)
    {
        var powers = sheets.Powers(e);
        for (int i = 0; i < powers.Count; i++)
        {
            var p = powers[i];
            if (p.PowerId != power) continue;
            bool learned = false;
            for (int k = 0; k < p.Options.Count; k++) if (p.Options[k] == option) { learned = true; break; }
            return (p.Rank, learned);
        }
        return (0, false);
    }

    // ---- the extra blows checked at their moments, each its own staged harm ----

    /// <summary>
    /// The extra blows the platform checked this tick (<see cref="FlurryStrike"/>, published in
    /// <see cref="Phase.Contact"/>), in their order. One whose line met the body (<see cref="FlurryCheck.Met"/>) is <b>its own staged
    /// harm</b> on the part it met now — its own pool, roll, soak (that part's armour), its own <see cref="HarmResolved"/> naming its
    /// logical strike (<see cref="HarmResolved.Strike"/>, <c>Flurry</c> its place), the swing's verdict carried, never a cut. One met at
    /// a guard (<see cref="FlurryCheck.Guarded"/>) is judged by the earlier build's contest (<see cref="FlurryRules.JudgeGuard"/>): overpowered, it
    /// lands on the part behind the guard from the budget the guard left; held or parried (or with the guard off), no harm. Any other
    /// is told why (the trace's line). Draws from the purpose streams in that order.
    /// </summary>
    void Strikes(World world, IReadOnlyList<FlurryStrike> list)
    {
        var sheets = world.Service<ActorSheets>();
        var ledger = world.Service<HarmLedger>();
        world.TryService<CombatTrace>(out var trace);
        world.TryService<EventQueue<HarmResolved>>(out var harmed);
        world.TryService<EventQueue<ActorDied>>(out var died);
        random ??= world.TryService<IDeterministicRandom>(out var dice) ? dice : new WorldRandom(world.Random);
        weapons ??= world.TryService<HarmWeapons>(out var hw) ? hw : new HarmWeapons();
        if (deadKnown == 0)
        {
            try { deadStore = world.Store<Dead>(); deadKnown = 1; }
            catch (InvalidOperationException) { deadKnown = 2; }
        }
        if (fightersKnown == 0)
        {
            try { fighters = world.Store<Platform.Control.Combatant>(); fightersKnown = 1; }
            catch (InvalidOperationException) { fightersKnown = 2; }
        }
        bool tracing = trace is { Enabled: true };
        for (int i = 0; i < list.Count; i++)
        {
            var s = list[i];
            if (s.Check == FlurryCheck.Compressed) continue;
            diedThisTick = died?.ThisTick;
            result.Reset();
            var c = s.Contact;
            GuardOutcome? verdict = null;
            float share = 1f;
            bool land = s.Check == FlurryCheck.Met;
            if (s.Check == FlurryCheck.Guarded)
            {
                verdict = FlurryRules.JudgeGuard(world, sheets, rules, weapons, random, in s);
                if (verdict is { Verdict: GuardVerdict.Overpowered } v && s.Region != BodyRegion.None)
                {
                    land = true;
                    share = v.BudgetLeft;
                    c = c with { Met = ContactMet.Body, Site = BodyRegions.Name(s.Region), HitCoverOrShield = false, Guard = GuardMeet.None };
                }
            }
            if (land) Resolve(world, sheets, in c, share, flurryBlow: true);
            else
            {
                result.Outcome = s.Check switch
                {
                    FlurryCheck.Guarded => HarmOutcome.Blocked,
                    FlurryCheck.OutOfReach or FlurryCheck.Dodged => HarmOutcome.Missed,
                    _ => HarmOutcome.Denied,
                };
                if (tracing) result.Refusal = FlurryRules.Why(in s, verdict);
            }
            result.Flurry = s.Index;
            result.FlurryBlows = s.Count;
            result.Continues = s.Continues;
            result.BudgetOut = s.BudgetLeft;
            (result.Angle, result.CutShare) = (s.Angle, s.CutShare);
            result.Strike = s.Strike;
            result.FlurryCheck = s.Check;
            result.GuardVerdict = verdict?.Verdict;
            if (land && Resolved(result))
            {
                ledger.FlurryBlows++;
                Apply(world, sheets, ledger, harmed, died, in c);
            }
            if (tracing) trace!.Write(HarmTrace.Line(rules, in c, result));
        }
    }
    // ---- end
}

/// <summary>The staged harm registered.</summary>
public static class HarmServices
{
    /// <summary>In <see cref="Phase.Resolve"/>, before the platform's death (<see cref="DeathServices.DeathOrder"/>).</summary>
    public const int HarmOrder = 100;

    /// <summary>
    /// Registers the <see cref="HarmRules"/>, <see cref="HarmLedger"/> and <see cref="HarmWeapons"/> services, the events <see cref="ContactMade"/>,
    /// <see cref="HarmResolved"/> and <see cref="ActorDied"/> (each unless the platform registered it already) and the
    /// system <c>enhanced.harm</c>; with <paramref name="severance"/>, severance's (<see cref="SeveranceServices.Register"/>).
    /// </summary>
    public static WorldBuilder Register(WorldBuilder b, HarmRules rules, SeveranceRules? severance = null)
    {
        b.Service(rules);
        if (severance is not null) SeveranceServices.Register(b, severance);   // severance, the budget, the anatomy
        b.Service(new HarmLedger());
        b.Service(new HarmWeapons());
        b.Component<FlurryCarry>();                 // Celerity's flurry carry, digested
        FlurryBlowServices.Register(b);             // the platform's side of the extra blows (once; the program registers it first)
        HealthBandServices.Register(b, rules.Data); // the Health bands on the vitals (labels only)
        if (!b.Peek.TryService<EventQueue<ContactMade>>(out _)) b.Event<ContactMade>();
        if (!b.Peek.TryService<EventQueue<HarmResolved>>(out _)) b.Event<HarmResolved>();
        if (!b.Peek.TryService<EventQueue<ActorDied>>(out _)) b.Event<ActorDied>();
        b.System(Phase.Resolve, HarmOrder, "enhanced.harm", new HarmSystem(rules));
        // With harm resolved here, a person flinches for harm done, not for every touch (the platform's HurtRule).
        if (b.Peek.TryService<HurtRule>(out var hurt)) hurt.OnContact = false; else b.Service(new HurtRule { OnContact = false });
        return b;
    }

    /// <summary>The weapon template a person's character carries (its action set's): empty for bare hands; null when it has no character.</summary>
    public static string? CharacterWeapon(World world, EntityId e)
    {
        if (!world.TryService<CharacterAssets>(out var assets) || !world.Has<CharacterAnimation>(e)) return null;
        int kind = world.Get<CharacterAnimation>(e).Kind;
        if ((uint)kind >= (uint)assets.KindCount) return null;
        return assets.Kind(kind).Actions?.Weapon ?? "";
    }
}
