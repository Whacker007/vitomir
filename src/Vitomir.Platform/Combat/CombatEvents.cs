// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Combat;

// The contract between contact (the platform), harm (the enhanced module's rules) and the arena (the host).
// Shared records: the code that owns a record's producer may add fields at the end, never change or reorder
// these.

/// <summary>What a blow or a shot met first.</summary>
public enum ContactMet : byte
{
    /// <summary>A person's body: <see cref="ContactMade.Site"/> names the region (the hit volumes' table).</summary>
    Body = 0,
    /// <summary>A held shield (the hit stopped there: <see cref="ContactMade.HitCoverOrShield"/>).</summary>
    Shield = 1,
    /// <summary>A held weapon (a parry by the blade; a clash).</summary>
    Weapon = 2,
    /// <summary>The level: a wall, the ground, cover.</summary>
    Level = 3,
}

/// <summary>The material met (SANDBOX until the rules package's materials; the module's data names each thing's).</summary>
public enum ContactMaterial : byte
{
    Flesh = 0,
    Cloth = 1,
    Leather = 2,
    Wood = 3,
    Steel = 4,
    Stone = 5,
    Earth = 6,
}

/// <summary>
/// One contact found by the physical world (the rules package's <c>PhysicalContactEvidence</c>): never made
/// up from a number, only from a swept blade or a flying projectile meeting something. One per (swing, target): a blade
/// that passes through a body is one contact. <see cref="Target"/> is <see cref="EntityId.None"/> for the level.
/// </summary>
/// <param name="ContactId">Unique in the run, rising (the tick and a counter).</param>
/// <param name="Attacker">Who struck or shot.</param>
/// <param name="Swing">The attacker's swing (or shot) number (<c>AttackBegun.Swing</c>): the combat rules' <c>ActionId</c>.</param>
/// <param name="Site">The body region's name for <see cref="ContactMet.Body"/> (an interned table string), else null.</param>
/// <param name="ImpactSpeed">The striking point's speed along the normal, m/s.</param>
/// <param name="NormalImpulse">A SANDBOX impulse (the weapon's mass, from the module's data, times the speed), N·s.</param>
/// <param name="PreContactBlocked">The swing met a shield or a weapon before this target (for a body: false).</param>
/// <param name="HitCoverOrShield">This contact is a shield's or cover's.</param>
/// <param name="LineOfFire">A projectile had a clear line from where it was loosed (true for melee).</param>
/// <param name="Protected">The target is a protected person: found, but harm does not follow.</param>
/// <param name="PointSpeed">an earlier build (the, added at the end): the striking point's whole speed against the target, m/s
/// (<paramref name="ImpactSpeed"/> is its part along the normal, so their ratio is how square the blow met: 1 square on, near 0
/// glancing).</param>
/// <param name="AlongBlade">an earlier build: where on the blade it met, 0 at the guard to 1 at the tip (0 bare-handed).</param>
/// <param name="TipSpeed">an earlier build: the blade's tip's speed in the world at the contact, m/s.</param>
/// <param name="PeakSpeed">an earlier build: the blow's fastest tip speed as its clip was measured at load (<see cref="ActiveWindow.PeakSpeed"/>)
/// times the rate the swing played at, m/s; <paramref name="TipSpeed"/> over it is how much of the blow's speed was left.
/// These four measure the blow's quality for the rules package's gate-08 (contact quality to damage potential, open);
/// nothing reads them for harm yet.</param>
/// <param name="Guard">an earlier build (the producer's, added at the end; the rules package's guard): for a contact with a
/// shield or a held blade while the guard rules run (<see cref="GuardSettings.Enabled"/>), what kind of guard met the blow —
/// a shield, a blade held in guard, a blade in its parry window, or a clash of two blades both swinging; <see cref="GuardMeet.None"/>
/// for a body, the level, or with the guard off. The rules answer each with a <see cref="GuardResolved"/>.</param>
/// <param name="Chain">an earlier build (added at the end; the rules package's attack budget): the contact's place in its
/// swing's ordered chain — 0 the first thing the swing met, 1 the next it met after the first let it go on (a part severed),
/// and so on. A later contact may be the same person again (another part, reached after the first was cut through): the
/// rules resolve it from what is left of the swing's attack budget (<see cref="HarmResolved.BudgetLeft"/>), never afresh.</param>
/// <param name="Shot">an earlier build (added at the end): above 0, the contact is an <b>arrow's</b> (<see cref="ProjectileSystem"/>): the
/// archer's shot number (<see cref="ArrowLoosed.Shot"/>; the scenario stream's <c>weaponClass</c> <c>arrow</c>). For an arrow
/// <paramref name="Attacker"/> is the archer, <paramref name="Swing"/> 0 (no swing), <paramref name="Chain"/> 0, <paramref name="Guard"/>
/// <see cref="GuardMeet.None"/> (no guard verdict for a missile), <paramref name="AlongBlade"/> 0, <paramref name="TipSpeed"/> the arrow's
/// own speed, <paramref name="PeakSpeed"/> its speed as loosed, <paramref name="PointSpeed"/> its speed against the thing met. 0: a blade's or a hand's.</param>
/// <param name="ProjectileMassKg">an earlier build: the arrow's mass (the bow's data, SANDBOX), kg; 0 for a blade (its mass is the module's weapon data).</param>
/// <param name="Fixed">an earlier build (added at the end): a <b>fixed contact</b> (<c>FIXED_CONTACT</c>: a scenario of kind
/// <c>contact</c>, <see cref="FixedContactSystem"/>) — placed at a time on a region with a given closing speed, squareness and place
/// on the blade, no swing and no animation; resolved by the same harm, soak, wound, severance and stop as a swept contact.
/// <paramref name="Swing"/> 0 (no swing), <paramref name="Chain"/> 0, <paramref name="Guard"/> <see cref="GuardMeet.None"/>,
/// <paramref name="TipSpeed"/> 0 (no blade moved). False: a blade's, a hand's or an arrow's contact the world swept.</param>
/// <param name="BlowClip">an earlier build: for a fixed contact, the clip of the blow it stands for (the attacker's own blow of that direction and
/// strength), which the rules' blow quality takes its reference by (a swept contact's is the clip its swing plays); null otherwise.</param>
public readonly record struct ContactMade(long ContactId, EntityId Attacker, int Swing, EntityId Target, ContactMet Met,
    string? Site, ContactMaterial Material, long Tick, Vector3 Point, Vector3 Normal, float ImpactSpeed, float NormalImpulse,
    bool PreContactBlocked, bool HitCoverOrShield, bool LineOfFire, bool Protected,
    float PointSpeed = 0f, float AlongBlade = 0f, float TipSpeed = 0f, float PeakSpeed = 0f, int Chain = 0,
    GuardMeet Guard = GuardMeet.None,
    int Shot = 0, float ProjectileMassKg = 0f,   // an arrow's contact (Combat/Projectiles.cs); see the remarks above
    bool Fixed = false, string? BlowClip = null);   // a fixed contact (Combat/FixedContacts.cs); see the remarks above

/// <summary>The kind of harm (the rules package's <c>DamageKind</c>).</summary>
public enum HarmKind : byte
{
    Bashing = 0,
    Lethal = 1,
    Aggravated = 2,
}

/// <summary>
/// What a contact did once the rules had it (the enhanced module's): the staged numbers and the Health it left.
/// Published once per contact the rules resolve (a protected target, a shield, the level or a dead body gets none).
/// <para>Added at the end: the <b>contest</b> on contact — whether it ran (<paramref name="Contested"/>;
/// false with the module's contest off), the attacker's pool and successes (φ(Dexterity) + Melee, or Brawl bare-handed),
/// the defender's (φ(Dexterity) + the better of Dodge and, holding a weapon, Melee), and the change it made to the damage
/// pool (<paramref name="ContestChange"/>: <paramref name="DamageDice"/> is the pool rolled, after it). Never a gate on the
/// hit: the contact is real and is resolved whatever the contest says.</para>
/// <para>Added at the end: <paramref name="ResistedBy"/>, why a resolved contact dealt no wound —
/// <see cref="Combat.ResistedBy.None"/> when it dealt one.</para>
/// <para>Added at the end: <paramref name="HealthBefore"/> and <paramref name="HealthAfter"/> are now Health
/// on the 1–1000 scale (a wound is the module's <c>hp_per_wound</c> of it), the types unchanged.
/// <paramref name="Flurry"/> is the blow's place in its swing's flurry (Celerity's swing carrying several
/// blows): 0 the swing's own blow, 1, 2, … the extra blows the swing carried, each a whole staged resolution of the same
/// contact (the same <paramref name="ContactId"/>); <paramref name="FlurryBlows"/> how many extra blows the swing carried (0
/// without Celerity's flurry; fewer are resolved when the target stops first). <paramref name="Band"/> the target's Health
/// band after the blow (labels only: the rules package's proposals).</para>
/// <para>(The rules package's attack budget and severance:
/// <i>"each hit gets it's own calculation on damage and what it does. So if a swing is hard enough from a strong
/// enough character, it could go through the arm if it severs it, and keep going"</i>): <paramref name="Severed"/> the part
/// this blow cut off (<see cref="BodyRegion.None"/> for none); <paramref name="Continues"/> whether the blade goes on past it
/// (the platform's follow-through reads it: without it, or with no line for a contact at all, the blade stops where it met);
/// <paramref name="BudgetLeft"/> the share of the swing's attack budget left after this contact (1 before the first; never
/// refilled); <paramref name="StructuralLoss"/> the Health the newly missing anatomy took off the person's effective maximum
/// (the union of what is missing, never per event; the Health lost now is <paramref name="HealthBefore"/> −
/// <paramref name="HealthAfter"/>, once).</para>
/// <para>(The angle the blow lands at, the attacker's strength,
/// the defender's soak and armour; the rules package's angle bands): <paramref name="Angle"/> the band the
/// blow met the body in; <paramref name="CutShare"/> the share of the blow's harm that counts toward cutting a part off (1
/// square on; what the bands make of it is the rules' data); <paramref name="ArmorDice"/> the soak dice the armour over the
/// struck part gave (included in <paramref name="SoakDice"/>); <paramref name="Armor"/> the armour piece met there, or null.</para>
/// <para>Added at the end: <paramref name="ArmorKept"/> the wounds the armour kept off (what the body's own soak
/// alone would have let through, less the wounds dealt; 0 without armour there).</para>
/// </summary>
public readonly record struct HarmResolved(long ContactId, EntityId Attacker, EntityId Target, long Tick, HarmKind Kind,
    int DamageDice, int DamageSuccesses, int AutoSuccesses, int SoakDice, int SoakSuccesses, int Wounds,
    int HealthBefore, int HealthAfter,
    bool Contested = false, int AttackDice = 0, int AttackSuccesses = 0, int DefenceDice = 0, int DefenceSuccesses = 0, int ContestChange = 0,
    ResistedBy ResistedBy = ResistedBy.None,
    int Flurry = 0, int FlurryBlows = 0, HealthBand Band = HealthBand.None,
    BodyRegion Severed = BodyRegion.None, bool Continues = false, float BudgetLeft = 0f, int StructuralLoss = 0,
    AngleBand Angle = AngleBand.None, float CutShare = 1f, int ArmorDice = 0, string? Armor = null,
    int ArmorKept = 0,
    long Strike = 0,      // the logical strike (FlurryBlows.Id(ContactId, Flurry)); see FlurryStrike
    // ---- blow quality (gate-08): Q (the contact's q over its blow's reference; 1 unmeasured or
    // no reference), the factor that scaled the damage potential (1 with blow-quality-off), the reference q_ref (0: none), the damage
    // potential before and after the factor, whether the hilt made the blow bashing, and where the cut share came from
    // ("quality", "band", "off"; null when severance did not judge the blow)
    float Quality = 1f, float QualityFactor = 1f, float QualityRef = 0f, float PotentialBefore = 0f, float PotentialAfter = 0f,
    bool Hilt = false, string? CutShareFrom = null);

/// <summary>
/// The angle a blow met a body at (the rules package: <i>"Use 90° as the direct impact angle
/// perpendicular to a struck surface, 0° as a tangential skim; explore approximately normal 33–90°, acute 10–33°, glancing
/// 0–10° for mundane sword slashes"</i>): a label the rules give a contact from its geometry (the contact's
/// <see cref="ContactMade.ImpactSpeed"/> over its <see cref="ContactMade.PointSpeed"/> is the sine of the angle). The band's
/// limits and what each does are the rules module's data.
/// </summary>
public enum AngleBand : byte
{
    /// <summary>Not measured (no speed at the contact, or the rules did not look).</summary>
    None = 0,
    /// <summary>Square on: about 33° to 90°.</summary>
    Normal = 1,
    /// <summary>About 10° to 33°.</summary>
    Acute = 2,
    /// <summary>A skim: under about 10°.</summary>
    Glancing = 3,
}

/// <summary>
/// A person's Health band (the rules package's chapter 06 and checklist: <i>"under ~25% Health injured; under ~10%
/// severe/Crippled; zero incapacitated"</i>, provisional thresholds, penalties OPEN): <b>labels only</b>, nothing reads them
/// for an effect. Of the purchased maximum.
/// </summary>
public enum HealthBand : byte
{
    /// <summary>At or above the injured threshold (25 % of the maximum by default): no band.</summary>
    None = 0,
    /// <summary>Below the injured threshold (25 %), at or above the crippled one.</summary>
    Injured = 1,
    /// <summary>Below the crippled threshold (10 %), above nothing.</summary>
    Crippled = 2,
    /// <summary>At nothing: the zero-Health stop (not death).</summary>
    Stopped = 3,
}

/// <summary>
/// A person's Health band changed (the enhanced module's, from its <c>Vitals</c>): by harm, by healing, or by a change
/// of its maximum. Labels only (<see cref="HealthBand"/>). <paramref name="Health"/> and <paramref name="HealthMax"/> are the
/// Health it now has and its maximum (the 1–1000 scale).
/// </summary>
public readonly record struct HealthBandChanged(EntityId Who, long Tick, HealthBand From, HealthBand To, int Health, int HealthMax);

/// <summary>
/// Why a contact the rules resolved dealt no wound (the staged harm's "resisted" split so the arena and the trace can
/// tell them apart): the damage made nothing, soak took it all, or a vampire's bashing was halved to nothing.
/// </summary>
public enum ResistedBy : byte
{
    /// <summary>It dealt a wound (or was not resolved).</summary>
    None = 0,
    /// <summary>The damage made nothing: no success rolled and none automatic (<c>N_damage + N_auto = 0</c>).</summary>
    NoDamage = 1,
    /// <summary>Soak took every success (<c>N_damage + N_auto &gt; 0</c>, <c>≤ N_soak</c>).</summary>
    Soak = 2,
    /// <summary>A wound after soak, halved to nothing (a vampire's bashing, <c>vampire_bashing_halves_postsoak</c>: 1 → 0).</summary>
    Halving = 3,
    /// <summary>
    /// (The rules package: <i>"Keep NoDamage, Soak and Halving distinct; also separate NoContact,
    /// shield/guard intercept, armor protected, illegal target and other no-harm causes"</i>): the armour over the struck part
    /// turned the wounds to nothing — the body's own soak (and a vampire's halving) alone would have let at least one through
    /// (<see cref="HarmResolved.ArmorKept"/> above 0, no wound). Kept apart from <see cref="Soak"/> (the body's soak, the armour's
    /// with it, took every success, and the body's alone would have too) and <see cref="Halving"/>. A shield's stop and a
    /// protected person stay their own: no harm is resolved for them (no <see cref="HarmResolved"/>).
    /// </summary>
    Armor = 4,
    /// <summary>
    /// (The rules package: <i>"Almost no effective closing/cutting motion can still be a true contact, but
    /// should not automatically deal a full clean-cut injury or severance. Avoid hidden failure roll; require credible delivered
    /// work"</i>): the blow's <b>low work</b> turned it to nothing — its damage potential scaled by its quality (the enhanced module's
    /// blow quality: the contact's speed into the surface over its blow's own) gave no wound where the same dice at the blow's
    /// typical quality (Q = 1), against the same soak, would have given one. No roll decided it: the contact is real and was
    /// resolved. Ahead of <see cref="Armor"/> when both were necessary (quality is the earlier stage).
    /// </summary>
    LowWork = 5,
}

/// <summary>
/// What a stop at nothing is (the rules package's Health 0 is not "death" — <i>"emit a neutral ZERO_HEALTH_STOP
/// or INCAPACITATED pending authorized cause-/species-based states"</i>).
/// </summary>
public enum ActorStop : byte
{
    /// <summary>Health at nothing: a harness stop (ENGINE_SANDBOX), not a ruled death — no torpor, no final death decided.</summary>
    ZeroHealthStop = 0,
    /// <summary>
    /// A part whose loss stops the body (the neck: the head off; the trunk cut through) was severed, whatever Health is
    /// left: a harness stop (ENGINE_SANDBOX) too — what it is for a mortal and for a vampire (death, torpor, Final Death) is
    /// The rules package's, species-owned (the rules package's).
    /// </summary>
    SeveredStop = 1,
}

/// <summary>
/// A person whose Health reached nothing: the platform plays the fall and stops the body
/// (<see cref="DeathSystem"/>). <paramref name="Kind"/> (added at the end) is the harm's kind: the platform plays
/// a slow fall for bashing, a quick one otherwise. <paramref name="Stop"/> (added at the end) is what the
/// stop is: <see cref="ActorStop.ZeroHealthStop"/>, a harness stop, not a ruled death; the name of the record is
/// kept so the contract's readers are unchanged.
/// </summary>
public readonly record struct ActorDied(EntityId Who, long Tick, long ContactId, EntityId Killer, HarmKind Kind = HarmKind.Lethal,
    ActorStop Stop = ActorStop.ZeroHealthStop);

/// <summary>
/// A part of a person cut off (the rules package's severance): published by the rules with the
/// <see cref="HarmResolved"/> of the blow that did it, for the platform's presentation (the part's bones collapsed, its hit
/// volumes gone, a held item in it dropped) and the trace. <paramref name="StructuralLoss"/> is the Health the newly missing
/// anatomy took off the effective maximum (the union: a forearm after its hand adds only the difference).
/// <para>The producer's, added at the end, for the log and the trace's readers: <paramref name="Harm"/> the
/// blow's post-soak harm in Health that cut (before Health capped it), <paramref name="Threshold"/> the part's, and the
/// person's <paramref name="EffectiveMaximum"/> after the cut and <paramref name="PurchasedMaximum"/> (the bands'
/// denominator, unchanged).</para>
/// </summary>
public readonly record struct PartSevered(EntityId Who, EntityId By, BodyRegion Region, long Tick, long ContactId, int StructuralLoss,
    int Harm = 0, int Threshold = 0, int EffectiveMaximum = 0, int PurchasedMaximum = 0);

// ---- the guard — what met a blow at a guard, and the rules' verdict on it ----

/// <summary>
/// What kind of guard a blow met (on <see cref="ContactMade.Guard"/>): found by the platform's geometry, never
/// assumed (the rules package: <i>"a shield or held weapon Block is directional, based on the actor-selected guard and
/// actual interposed geometry"</i>).
/// </summary>
public enum GuardMeet : byte
{
    /// <summary>Not a guard: a body, the level, or the guard rules off.</summary>
    None = 0,
    /// <summary>A held shield (raised or not: a shield in the way is in the way).</summary>
    Shield = 1,
    /// <summary>A blade held in guard (its holder's block raised), outside the parry window.</summary>
    Blade = 2,
    /// <summary>A blade moving to meet the blow inside its holder's parry window (the block's press).</summary>
    Parry = 3,
    /// <summary>Two blades both in their active windows met : one paired transaction.</summary>
    Clash = 4,
    /// <summary>
    /// A shield (or, with <see cref="ParryRule.Always"/>, a blade) that happened to be in the way, its holder's guard not raised:
    /// it stops the blow physically (<i>"Real accidental physical interception is still honored"</i>) but is no
    /// Block for the rules (<i>"passive physical shield interception is not an automatic active defense"</i>): no
    /// guard's hold bonus, no parry.
    /// </summary>
    Incidental = 5,
}

/// <summary>The rules' verdict on a blow met at a guard (<see cref="GuardResolved"/>).</summary>
public enum GuardVerdict : byte
{
    /// <summary>The guard held: the blade stops and springs back a little (the earlier build's rebound).</summary>
    Held = 0,
    /// <summary>Met in the parry window and the contest won: the blade turned aside (a larger rebound), its owner's recovery lengthened by a riposte window (never a free hit).</summary>
    Parried = 1,
    /// <summary>The guard pushed through : the blade goes on with the budget left, the guard broken for a moment.</summary>
    Overpowered = 2,
    /// <summary>Two blades both swinging met with comparable force : both stop, no harm, no riposte, no free hit.</summary>
    Clashed = 3,
    /// <summary>
    /// In a clash, this contact's blade dominated (the rules package's <i>"conditional mutual stop, domination or deflection"</i>; Strength
    /// first): it goes on with what is left of its budget to whatever its real path meets next; the other blade stopped
    /// (<see cref="Clashed"/> on its side of the pair).
    /// </summary>
    ClashDominates = 4,
    /// <summary>In a clash, both blades deflected (they met obliquely and slid off each other): each turned aside, a larger rebound, no riposte.</summary>
    ClashDeflected = 5,
}

/// <summary>
/// The rules' verdict on a contact with a guard (published by the rules module in <see cref="Core.Phase.Resolve"/>
/// beside <see cref="HarmResolved"/>, read by the platform's follow-through as it reads <see cref="HarmResolved.Continues"/>):
/// one per <see cref="ContactMade"/> whose <see cref="ContactMade.Guard"/> is not <see cref="GuardMeet.None"/>. The inputs and
/// the chances are carried for the trace and the arena : the blow's force (Strength first, the weapon, the budget
/// it had), the guard's hold (Strength first, a shield holding better, technique second), the defender's technique for a parry,
/// the chances they made and the draws. <paramref name="BudgetLeft"/> is what the blade goes on with when overpowered (0
/// otherwise: the chain ends there); <paramref name="RiposteSeconds"/> lengthens the parried attacker's recovery;
/// <paramref name="BrokenSeconds"/> is how long an overpowered guard cannot be raised again. Without a rules module (or its
/// guard switch off) no verdict comes and the platform holds the guard as before (the earlier build's rebound).
/// </summary>
public readonly record struct GuardResolved(long ContactId, EntityId Attacker, EntityId Defender, long Tick, ContactMet Met, GuardMeet Meet,
    GuardVerdict Verdict, float Force, float Hold, float Technique, float OverpowerChance, float ParryChance, double OverpowerDraw, double ParryDraw,
    float BudgetBefore, float BudgetLeft, float RiposteSeconds, float BrokenSeconds);

// ---- a Celerity swing's extra blows, each its own strike at its own moment ----

/// <summary>
/// What the check at an extra blow's moment found (<see cref="FlurryStrike.Check"/>;
/// Each extra blow of a Celerity swing is its own strike that must still find its target at its own moment). The
/// platform's geometry decides it, never a roll (<see cref="FlurryBlowSystem"/>).
/// </summary>
public enum FlurryCheck : byte
{
    /// <summary>The blade's line, carried by the attacker's body to where it is now, met the target's body: the blow lands (the rules resolve it).</summary>
    Met = 0,
    /// <summary>A shield, or a blade held in guard or in its parry window, met the line first: a guard contact for the rules' verdict (held, parried or overpowered).</summary>
    Guarded = 1,
    /// <summary>The line met nothing of the target's: it moved (or the attacker turned) out of the blade's reach.</summary>
    OutOfReach = 2,
    /// <summary>The line met nothing of the target's, and the target is dodging or dodged since the swing's contact (a dodge's real motion carried it away).</summary>
    Dodged = 3,
    /// <summary>The attacker no longer swings that swing: a cancel (a feint or a dodge), a new swing, its stop, its weapon hand cut off.</summary>
    AttackerStopped = 4,
    /// <summary>The target is stopped (its zero-Health or severed stop), dead, or gone.</summary>
    TargetDown = 5,
    /// <summary>The earlier build's translation (<c>flurry-compressed</c>): resolved on the contact at once, with no check.</summary>
    Compressed = 6,
}

/// <summary>
/// One extra blow of a Celerity swing tried (the rules package's <i>"keep distinct logical action IDs
/// from unique physical-contact IDs"</i>): a <b>logical strike</b>, apart from the physical contact it re-meets. Published by the
/// platform at the blow's moment (<see cref="FlurryBlowSystem"/>, <see cref="Core.Phase.Contact"/>) with what its check found; the
/// rules resolve the ones that meet the body (and judge the ones met at a guard) in <see cref="Core.Phase.Resolve"/>, each its own
/// staged harm whose <see cref="HarmResolved.Strike"/> is <paramref name="Strike"/>. Under <c>flurry-compressed</c> the rules
/// publish one per extra blow they resolve, <see cref="FlurryCheck.Compressed"/>, at the contact's tick.
/// </summary>
/// <param name="Strike">The logical strike's id: <see cref="FlurryBlows.Id"/>(<paramref name="ContactId"/>, <paramref name="Index"/>).</param>
/// <param name="ContactId">The physical contact the swing made (its own blow's), which this strike re-meets.</param>
/// <param name="Index">The blow's place in the swing's flurry: 1, 2, …</param>
/// <param name="Count">How many extra blows the swing carried.</param>
/// <param name="Tick">The blow's moment.</param>
/// <param name="Met">What the line met first (<see cref="ContactMet.Body"/> for none).</param>
/// <param name="Guard">The guard met (<see cref="Check"/> <see cref="FlurryCheck.Guarded"/>), else <see cref="GuardMeet.None"/>.</param>
/// <param name="Region">The body part the line meets (behind a guard, for an overpowering verdict), <see cref="BodyRegion.None"/> for none.</param>
/// <param name="Point">Where it met (the struck place re-measured, when it met nothing).</param>
/// <param name="Moved">How far the struck place is now from where the line expects it, in the attacker's frame, m (the target's and the attacker's motion since the contact).</param>
/// <param name="Contact">The evidence the rules resolve the strike from: the swing's contact as the strike meets it now (its tick, the part, the point and the normal now; the blow's speeds the contact's).</param>
/// <param name="Angle">The swing's own blow's angle band, carried by its blows.</param>
/// <param name="CutShare">The swing's own blow's cut share, carried.</param>
/// <param name="Continues">The swing's own blow's verdict, carried.</param>
/// <param name="BudgetLeft">The swing's budget left after its own blow, carried.</param>
public readonly record struct FlurryStrike(long Strike, long ContactId, EntityId Attacker, int Swing, EntityId Target, int Index, int Count,
    long Tick, FlurryCheck Check, ContactMet Met, GuardMeet Guard, BodyRegion Region, Vector3 Point, float Moved, ContactMade Contact,
    AngleBand Angle = AngleBand.None, float CutShare = 1f, bool Continues = false, float BudgetLeft = 0f);

// ---- the scenario director's events (4.6, 5.3) ----
// A step is named by its track (0, 1, … in the actor's control; −1 for a trigger's step) and its number (depth-first through the
// actor's tracks in order, a repeat's steps numbered after the repeat; for a trigger's step, the trigger's index), its kind and
// its label. Published by the scenario director (Control/ScenarioDirector.cs, Think 96) only in a world with a scenario program.

/// <summary>A step began (<paramref name="Iteration"/>: its repeat's round, 0 for the first or for a step in no repeat). Ids (at the end):
/// <paramref name="Instance"/> names this running of the step, run-unique, shared by every event of it (<see cref="StepEnded"/>,
/// <see cref="StepTimedOut"/>, <see cref="StepRefused"/>, <see cref="StepLate"/>, <see cref="AimedBlow"/>); <paramref name="Parent"/> is the
/// <paramref name="Instance"/> of the repeat it runs in, or the <see cref="TriggerFired.Firing"/> that started it, or 0. One counter, from 1.</summary>
public readonly record struct StepBegan(EntityId Entity, long Tick, int Track, int Step, Control.ScenarioStepKind Kind, string? Label, int Iteration,
    long Instance = 0, long Parent = 0);

/// <summary>A step ended, how, and after how long (s).</summary>
public readonly record struct StepEnded(EntityId Entity, long Tick, int Track, int Step, Control.ScenarioStepKind Kind, string? Label,
    Control.StepOutcome Outcome, float Seconds, long Instance = 0);

/// <summary>A step's goal was not met in its time: <paramref name="Why"/> in words (<c>not arrived</c>, <c>never ready</c>, …); for an aimed blow, the region aimed and the region met (<see cref="BodyRegions.Name"/>'s words; empty for none).</summary>
public readonly record struct StepTimedOut(EntityId Entity, long Tick, int Track, int Step, Control.ScenarioStepKind Kind, string? Label,
    string Why, string? AimedRegion = null, string? RegionMet = null, long Instance = 0);

/// <summary>A step could not be asked; nothing was written for it.</summary>
public readonly record struct StepRefused(EntityId Entity, long Tick, int Track, int Step, Control.ScenarioStepKind Kind, string? Label,
    Control.StepRefusal Why, long Instance = 0);

/// <summary>A step with an <c>at</c> began late (the step before it had not ended by then), by how much (s).</summary>
public readonly record struct StepLate(EntityId Entity, long Tick, int Track, int Step, Control.ScenarioStepKind Kind, string? Label, float LateSeconds,
    long Instance = 0);

/// <summary>Who holds an actor's channels changed: the director's steps (<paramref name="Script"/>), the AI (<paramref name="Ai"/>), muted (<paramref name="Muted"/>: cleared each tick); a channel in none of them is nobody's.</summary>
public readonly record struct ControlChanged(EntityId Entity, long Tick, Control.ControlChannels Script, Control.ControlChannels Ai, Control.ControlChannels Muted);

/// <summary>A <c>mark</c> step's text, on its tick.</summary>
public readonly record struct Marker(EntityId Entity, long Tick, string Text);

/// <summary>A trigger fired (its index in the program and its label); <paramref name="Firing"/>, this firing's id (the step ids' counter).</summary>
public readonly record struct TriggerFired(long Tick, int Trigger, string? Label, long Firing = 0);

/// <summary>The program ended the run (an <c>end</c> step, a trigger's <c>end</c>, <c>then: end</c>): the runner stops after this tick with <c>SCENARIO_END:</c><paramref name="Reason"/>. <paramref name="Entity"/> is the actor whose step ended it, or none for a trigger.</summary>
public readonly record struct ScenarioEnded(long Tick, EntityId Entity, string Reason);

/// <summary>
/// The swing a director's attack step pressed has begun (published when the body's swings move on, the tick after the press
/// at the earliest): its <paramref name="Swing"/> (join <see cref="ContactMade"/> by attacker and swing), the target, the
/// <paramref name="Region"/> of the target's posed hit volumes it was aimed at (the side resolved: a side-less <c>upperArm</c> is the
/// arm nearer the attacker; <see cref="BodyRegion.None"/> for an aim at a point or by angles, or none), the words asked
/// (<c>chest</c>, <c>left upperArm</c>; empty for none), and the step (track, number, label, <paramref name="Instance"/>).
/// </summary>
public readonly record struct AimedBlow(EntityId Entity, long Tick, int Swing, EntityId Target, BodyRegion Region, string Asked, int Track, int Step,
    string? Label, long Instance);

// ---- the bow v0, headless (Combat/Bow.cs, Combat/Projectiles.cs) ----

/// <summary>How an arrow's flight ended (<see cref="ArrowSpent.End"/>).</summary>
public enum ArrowEnd : byte
{
    /// <summary>It met something — a body, a shield, a held blade or the level — and stopped there (<see cref="ArrowSpent.ContactId"/> its <see cref="ContactMade"/>).</summary>
    Met = 0,
    /// <summary>It fell out of the level (below the bow data's <c>fallen_below_metres</c> under where it was loosed) without meeting anything.</summary>
    Fell = 1,
    /// <summary>It flew longer than the bow data's <c>flight_seconds_at_most</c> without meeting anything.</summary>
    TimedOut = 2,
}

/// <summary>Why a draw was let down without loosing (<see cref="DrawCancelled.Cause"/>).</summary>
public enum DrawCancelCause : byte
{
    /// <summary>Let down on purpose (<see cref="BowIntent.LetDown"/>: the player's cancel, the script's <c>cancelDraw</c>).</summary>
    LetDown = 0,
    /// <summary>Aim let go while drawn (the bow and its nocked arrow go back low at the waist).</summary>
    AimLetGo = 1,
    /// <summary>The bow put away (<see cref="BowIntent.Ranged"/> let go).</summary>
    Unequipped = 2,
    /// <summary>The archer was hurt (a flinch played).</summary>
    Hurt = 3,
    /// <summary>The draw was let go below the bow's least share to loose (<c>least_share_to_loose</c>): let down, not loosed.</summary>
    TooLittle = 4,
    /// <summary>The archer stopped (dead, or a harness stop).</summary>
    Stopped = 5,
}

/// <summary>
/// An arrow nocked: the bow raised in aim mode with an arrow on the string, ready to draw. <paramref name="Shot"/> is the
/// number the arrow will carry when loosed (<see cref="ArrowLoosed.Shot"/>); <paramref name="ArrowsLeft"/> what is left in the quiver.
/// </summary>
public readonly record struct ArrowNocked(EntityId Archer, long Tick, int Shot, int ArrowsLeft);

/// <summary>A draw begun: attack held with an arrow nocked; the draw rises to <paramref name="DrawToShare"/> of full and holds there.</summary>
public readonly record struct DrawBegan(EntityId Archer, long Tick, int Shot, float DrawToShare);

/// <summary>
/// An arrow loosed (<i>"the player releases the arrow which should fly at whatever proper speed and
/// trajectory based on how far back the draw was"</i>): from <paramref name="From"/> (the release point: shoulder height in front of the
/// chest) with <paramref name="Velocity"/> (m/s; the bow's speed at <paramref name="DrawShare"/> along the aim, the aim's spread drawn, the
/// archer's own motion added). <paramref name="Target"/> and <paramref name="AimedRegion"/> are what the aim was held on (none: an aim by
/// yaw and pitch or the facing); <paramref name="YawDegrees"/> and <paramref name="PitchDegrees"/> the direction it left on (after the
/// spread); <paramref name="SpreadDegrees"/> the spread it was drawn from (the module's, by Archery; 0 without).
/// </summary>
public readonly record struct ArrowLoosed(EntityId Archer, long Tick, int Shot, float DrawShare, float SpeedMetresPerSecond, Vector3 From, Vector3 Velocity,
    EntityId Target, BodyRegion AimedRegion, float YawDegrees, float PitchDegrees, float SpreadDegrees, int ArrowsLeft);

/// <summary>
/// An arrow's flight ended: published <b>once for every arrow loosed</b>, whatever ended it (<paramref name="End"/>), so
/// an archer's <see cref="ArrowLoosed"/> and <see cref="ArrowSpent"/> counts agree at a run's end unless arrows are still in the air. For
/// <see cref="ArrowEnd.Met"/>: what it met (<paramref name="Met"/>, <paramref name="Target"/>; <paramref name="Site"/> the body region's name),
/// the <see cref="ContactMade"/>'s id, the point; it stops there (stuck). <paramref name="FlightSeconds"/> from the loose,
/// <paramref name="DistanceMetres"/> from the release point in a straight line, <paramref name="SpeedMetresPerSecond"/> its speed then.
/// </summary>
public readonly record struct ArrowSpent(EntityId Archer, long Tick, int Shot, ArrowEnd End, ContactMet Met, EntityId Target, long ContactId, Vector3 Point,
    float FlightSeconds, float DistanceMetres, float SpeedMetresPerSecond, string? Site);

/// <summary>A draw let down without loosing: the share it had reached and why (<see cref="DrawCancelCause"/>). The arrow stays nocked.</summary>
public readonly record struct DrawCancelled(EntityId Archer, long Tick, int Shot, float DrawShare, DrawCancelCause Cause);

// ---- the player's swap between the melee weapon and the bow, and a shield raised against a drawn bow

/// <summary>
/// The player's swap between the melee weapon and the bow (<paramref name="Archer"/> the one swapping): published as it begins (<paramref name="Ended"/> false; the
/// swap takes <paramref name="Seconds"/>, the bow's <c>swap_seconds</c>) and as it ends (true: the weapon <paramref name="ToRanged"/> is in hand).
/// </summary>
public readonly record struct WeaponSwap(EntityId Archer, long Tick, bool ToRanged, bool Ended, float Seconds);

/// <summary>
/// A shield raised against a drawn bow, or lowered: <paramref name="Defender"/> (a duelling brain with a shield) saw
/// <paramref name="Archer"/>'s bow drawn and aimed at it within range and in its view, and after its reaction raised its shield toward
/// him (<paramref name="Up"/> true), or lowered it as the bow was let down, loosed, turned away or went out of range (false).
/// </summary>
public readonly record struct ShieldAgainstBow(EntityId Defender, long Tick, EntityId Archer, bool Up, float DistanceMetres);
// ---- end

// ---- a moveTo that routes round the level, or fails honestly; the objectives' results

/// <summary>Why a <c>moveTo</c> did not get there (<see cref="MoveToFailed"/>).</summary>
public enum MoveToFailure
{
    /// <summary>The straight line to the goal meets the level and the navigation mesh has no route there (either end off it, or not joined).</summary>
    NoRoute = 0,
    /// <summary>A route was being walked and the body made no progress along it for <see cref="Navigation.RouteSteer.StallSeconds"/> twice over (asked again between): something the mesh does not know stops it (a doorway narrower than the body, another body).</summary>
    Blocked,
    /// <summary>The step's <c>forSeconds</c> passed before it arrived (its <c>StepTimedOut</c> follows).</summary>
    TimedOut,
}

/// <summary>
/// A <c>moveTo</c> took a route round the level: the straight line from
/// <paramref name="From"/> to <paramref name="Goal"/> met the level (<paramref name="BlockedAt"/>, the first wall it met), so the step walks
/// the navigation mesh's route, <paramref name="Corners"/> corners and <paramref name="RouteMetres"/> long (the straight line
/// <paramref name="StraightMetres"/>), the corners as <c>x,y;x,y</c> (<paramref name="Path"/>). Published when the route is first asked and
/// each time it is asked again (<paramref name="Asked"/>: 1 up). A moveTo whose straight line is clear walks it as before and publishes nothing.
/// </summary>
public readonly record struct MoveToRouted(EntityId Entity, long Tick, int Track, int Step, string? Label, long Instance, Vector3 From, Vector3 Goal,
    Vector3 BlockedAt, int Corners, float RouteMetres, float StraightMetres, string Path, int Asked);

/// <summary>
/// A <c>moveTo</c> that did not get there, said honestly (the rules package's Gate 0A item 4: <i>"If a scripted instruction
/// cannot reach a waypoint, report that fact, not fictional progress"</i>): <paramref name="Why"/>, the goal, where the body is
/// (<paramref name="At"/>), how near it came (<paramref name="ClosestMetres"/>) and how far it is now (<paramref name="LeftMetres"/>), how far it
/// walked (<paramref name="WalkedMetres"/>), the path it actually walked as <c>x,y;x,y</c> (its feet every quarter second, thinned to at most
/// 64 points; <paramref name="Walked"/>), and the route it was given (<paramref name="Route"/>, empty when it walked straight).
/// <see cref="MoveToFailure.NoRoute"/> ends the step at once (refused, <c>NoRoute</c>); <see cref="MoveToFailure.Blocked"/> ends it timed
/// out ("blocked"); <see cref="MoveToFailure.TimedOut"/> comes with its <c>StepTimedOut</c>.
/// </summary>
public readonly record struct MoveToFailed(EntityId Entity, long Tick, int Track, int Step, string? Label, long Instance, MoveToFailure Why,
    Vector3 Goal, Vector3 At, float ClosestMetres, float LeftMetres, float WalkedMetres, string Walked, string Route, string Reason);

/// <summary>What an objective came to: met, failed, or not decided when the run ended.</summary>
public enum ObjectiveResult { Unresolved = 0, Met, Failed }

/// <summary>
/// An objective met during a run: written by the scenario runner into the run's event stream
/// (never published in a world: objectives measure, they change nothing), <paramref name="Parent"/> the event that decided it (an
/// <c>ActorDied</c> — a harness stop, not a death — or none for a place reached). <paramref name="Who"/> the actor the objective is
/// given to (none for a side's: <paramref name="Side"/>); <paramref name="Target"/> the actor it is about (a foe defeated, an actor
/// protected), or none; <paramref name="Reason"/> in words. A side's result is the side's: it is never awarded to whoever dealt the last harm.
/// </summary>
public readonly record struct ObjectiveMet(EntityId Who, long Tick, string Objective, string Kind, string? Side, EntityId Target, string Reason);

/// <summary>An objective failed during a run: as <see cref="ObjectiveMet"/>.</summary>
public readonly record struct ObjectiveFailed(EntityId Who, long Tick, string Objective, string Kind, string? Side, EntityId Target, string Reason);
// ---- end
