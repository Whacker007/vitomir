// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the fixed contact (FIXED_CONTACT) — the rules package's harness as a scenario kind.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Combat;

/// <summary>
/// One fixed contact as a scenario places it (a scenario of kind <c>contact</c>): at
/// <see cref="AtSeconds"/>, <see cref="Attacker"/>'s blow meets <see cref="Defender"/>'s <see cref="Region"/> (with <see cref="Side"/>,
/// or the side nearer the attacker) at <see cref="AlongBlade"/> of the blade, the striking point closing at <see cref="ClosingSpeed"/>
/// m/s at <see cref="SquarenessDegrees"/> to the struck surface (90 square on, 0 sliding along it). The blow it stands for is the
/// attacker's own <see cref="Strength"/> blow from <see cref="Direction"/> (its clip gives the blow quality's reference and its peak
/// speed), played at <see cref="MotionRate"/> (null: the attacker's own swing rate now); <see cref="PeakSpeed"/>, when given, is the
/// blow's peak tip speed instead of the clip's. <see cref="Index"/> is its place in the scenario's list (the receipts name it).
/// </summary>
public sealed record FixedContactSpec
{
    public int Index { get; init; }
    public float AtSeconds { get; init; }
    public string Attacker { get; init; } = "";
    public string Defender { get; init; } = "";
    public AimRegion Region { get; init; } = AimRegion.UpperArm;
    public StepSide? Side { get; init; }
    public float AlongBlade { get; init; } = 0.4f;
    public float ClosingSpeed { get; init; } = 25.8f;
    public float SquarenessDegrees { get; init; } = 56f;
    public AttackDirection Direction { get; init; } = AttackDirection.Right;
    public AttackStrength Strength { get; init; } = AttackStrength.Quick;
    public float? MotionRate { get; init; }
    public float? PeakSpeed { get; init; }
    /// <summary>What the blow meets: the body (the region's), or the defender's shield, its blade held in guard, or its blade in its parry window.</summary>
    public FixedMeet Meets { get; init; } = FixedMeet.Body;
}

/// <summary>
/// What a fixed contact meets: <see cref="Body"/> (its region); <see cref="Shield"/> (the defender's held shield:
/// <see cref="ContactMet.Shield"/>, <see cref="GuardMeet.Shield"/>); <see cref="HeldBlade"/> (its blade held in guard:
/// <see cref="ContactMet.Weapon"/>, <see cref="GuardMeet.Blade"/>); <see cref="ParryingBlade"/> (its blade in its parry window:
/// <see cref="GuardMeet.Parry"/>). The meeting's kind is given: whether the defender's block is up is not checked. With the guard rules
/// off (<see cref="GuardSettings.Enabled"/>) a guard meeting carries no guard, as a swept one's.
/// </summary>
public enum FixedMeet : byte { Body = 0, Shield = 1, HeldBlade = 2, ParryingBlade = 3 }

/// <summary>
/// A fixed contact placed (the receipt before its <see cref="ContactMade"/>, which the scenario stream links to it): the
/// scenario's contact <paramref name="Index"/>, the contact id it was given, the site met (the region's name as contact names it),
/// the blow it stands for (<paramref name="Blow"/>: <c>quick right</c>; <paramref name="Clip"/> its clip), and the numbers it carries:
/// the striking point's closing speed, the squareness, the speed into the surface (closing × sin(squareness)), the place on the
/// blade, the blow's peak tip speed and the rate it stands at. <paramref name="Placed"/> false (and <paramref name="Why"/>) when it
/// could not be placed — an actor gone, a body with no such region — and no <see cref="ContactMade"/> follows.
/// </summary>
public readonly record struct FixedContactPlaced(EntityId Attacker, long Tick, EntityId Target, long ContactId, int Index, string? Site,
    string Blow, string? Clip, float ClosingSpeed, float SquarenessDegrees, float ImpactSpeed, float AlongBlade, float PeakSpeed, float MotionRate,
    bool Placed, string? Why);

/// <summary>
/// The fixed contacts of a world (a service the scenario's program registers, <see cref="FixedContactServices.Register"/>): the
/// program's list in time order and the cast's people by name, bound once they are made (<see cref="Bind"/>). Not digested: what it
/// holds is the program's (in the tape's head) and which contacts are done follows from the tick.
/// </summary>
public sealed class FixedContactPlan
{
    readonly FixedContactSpec[] contacts;
    readonly long[] due;
    readonly Dictionary<string, EntityId> people = new(StringComparer.Ordinal);
    int next;

    public FixedContactPlan(IEnumerable<FixedContactSpec> list)
    {
        contacts = [.. list.OrderBy(c => TickOf(c.AtSeconds)).ThenBy(c => c.Index)];
        due = [.. contacts.Select(c => TickOf(c.AtSeconds))];
    }

    /// <summary>The program's contacts, in time order.</summary>
    public IReadOnlyList<FixedContactSpec> Contacts => contacts;

    /// <summary>A time in seconds as the tick it falls on (rounded to the nearest, as a step's <c>at</c>).</summary>
    public static long TickOf(float seconds) => (long)Math.Round(seconds * World.TicksPerSecond, MidpointRounding.AwayFromZero);

    /// <summary>The cast's people by name (the program's names), once they are made; a contact naming nobody is refused here.</summary>
    public void Bind(IReadOnlyList<string> names, IReadOnlyList<EntityId> entities)
    {
        people.Clear();
        for (int i = 0; i < names.Count && i < entities.Count; i++) people[names[i]] = entities[i];
        foreach (var c in contacts)
        {
            if (!people.ContainsKey(c.Attacker)) throw new ScenarioProgramException($"scenario: fixed contact {c.Index} names the attacker \"{c.Attacker}\", who is not in the cast ({string.Join(", ", names)})");
            if (!people.ContainsKey(c.Defender)) throw new ScenarioProgramException($"scenario: fixed contact {c.Index} names the defender \"{c.Defender}\", who is not in the cast ({string.Join(", ", names)})");
        }
    }

    internal bool Next(long tick, out FixedContactSpec spec, out EntityId attacker, out EntityId defender)
    {
        spec = null!;
        attacker = defender = EntityId.None;
        if (next >= contacts.Length || due[next] > tick) return false;
        spec = contacts[next++];
        attacker = people.GetValueOrDefault(spec.Attacker, EntityId.None);
        defender = people.GetValueOrDefault(spec.Defender, EntityId.None);
        return true;
    }
}

/// <summary>
/// The fixed contacts delivered (<see cref="Phase.Contact"/>, <see cref="FixedContactServices.Order"/> 75, after the blades'
/// contact (60), the arrows (65) and Celerity's extra blows (70)): each contact due this tick (or earlier, never twice) is published
/// as a <see cref="ContactMade"/> with <see cref="ContactMade.Fixed"/> set — the defender's region's posed hit volumes give the point,
/// the surface faces the attacker, the speeds and the place on the blade are the scenario's — preceded by its
/// <see cref="FixedContactPlaced"/>. What follows is a swept contact's: the rules' harm, soak, wounds, severance and stop read it, the
/// struck body flinches when the rules say (or at once when the hurt is the contact's), a protected person protests, violence in a
/// public place is a masquerade risk, a catch lets go (as <c>ContactSystem.Publish</c> does for a swept one). No blade moves: no
/// swing, no bite, no rebound, no follow-through. Ids: <c>tick × 1000 + 900 + n</c> (the blades' from 0, the arrows' from 500).
/// </summary>
public sealed class FixedContactSystem : ISystem
{
    /// <summary>The first contact id's offset within a tick for fixed contacts (the blades' start at 0, the arrows' at 500).</summary>
    public const int ContactIdBase = 900;

    static readonly ContactConfig Defaults = new();

    public void Run(World world)
    {
        if (!world.TryService<FixedContactPlan>(out var plan)) return;
        long tick = world.Tick;
        int serial = 0;
        while (plan.Next(tick, out var spec, out var a, out var t))
        {
            long id = tick * 1000 + ContactIdBase + serial++;
            Deliver(world, spec, a, t, tick, id);
        }
    }

    static void Deliver(World world, FixedContactSpec spec, EntityId a, EntityId t, long tick, long id)
    {
        var placedQ = world.Events<FixedContactPlaced>();
        string blow = $"{spec.Strength.ToString().ToLowerInvariant()}{(spec.Strength == AttackStrength.Strong ? "" : " " + spec.Direction.ToString().ToLowerInvariant())}";
        float sine = DetMath.Sin(Math.Clamp(spec.SquarenessDegrees, 0f, 90f) * DetMath.DegToRad);   // the platform's own trigonometry (deterministic)
        float impact = spec.ClosingSpeed * sine;
        void Refuse(string why) =>
            placedQ.Publish(new FixedContactPlaced(a, tick, t, 0, spec.Index, null, blow, null, spec.ClosingSpeed, spec.SquarenessDegrees, impact, spec.AlongBlade, 0f, 0f, false, why));
        var bodies = world.Store<Body>();
        if (!world.Alive(a) || !bodies.Has(a)) { Refuse($"the attacker {spec.Attacker} has no body"); return; }
        if (!world.Alive(t) || !bodies.Has(t)) { Refuse($"the defender {spec.Defender} has no body"); return; }
        ref readonly var ab = ref bodies.Get(a);
        ref readonly var tb = ref bodies.Get(t);

        if (spec.Meets != FixedMeet.Body) { DeliverGuard(world, spec, a, t, tick, id, blow, impact, ab.Feet); return; }
        // The region met: its posed hit volumes' middle (the side asked, else the one nearer the attacker).
        var (left, right) = Sides(spec.Region);
        var want = spec.Side == StepSide.Right ? right : left;
        Vector3 point = default;
        int n = 0;
        if (world.TryService<HitVolumes>(out var volumes))
        {
            var posed = volumes.Posed(world, t);
            if (spec.Side is null && left != right)
            {
                var l = Middle(posed, left, out int nl);
                var r = Middle(posed, right, out int nr);
                want = nl > 0 && nr > 0
                    ? (Vector2.DistanceSquared(new(l.X, l.Y), new(ab.Feet.X, ab.Feet.Y)) <= Vector2.DistanceSquared(new(r.X, r.Y), new(ab.Feet.X, ab.Feet.Y)) ? left : right)
                    : nl > 0 ? left : right;
            }
            point = Middle(posed, want, out n);
        }
        if (n == 0) { Refuse($"{spec.Defender} has no hit volumes for the {BodyRegions.Name(want)} (no model, or a body without that part)"); return; }
        string site = BodyRegions.Name(want);

        // The surface faces the attacker (across the ground); the blow's clip and peak from the attacker's own blow.
        var across = new Vector2(ab.Feet.X - point.X, ab.Feet.Y - point.Y);
        var normal = across.LengthSquared() > 1e-8f ? Vector3.Normalize(new Vector3(across, 0f)) : Vector3.UnitX;
        var config = world.TryService<ContactConfig>(out var cc) ? cc : Defaults;
        world.TryService<CharacterAssets>(out var assets);
        ref var anim = ref world.Store<CharacterAnimation>().Find(a);
        int kind = Unsafe.IsNullRef(ref anim) ? -1 : anim.Kind;
        var motion = assets is null || kind < 0 ? ActionMotion.None : MotionOf(world, assets, kind, spec);
        string? clip = motion.Has && assets is not null ? assets.Clip(motion.Clip).Name : null;
        float rate = spec.MotionRate ?? (world.TryStore<CombatTempo>(out var tempos) ? CombatTempos.Of(tempos, a).SwingRate : 1f);
        float peak = spec.PeakSpeed ?? (motion.Has && assets is not null && world.TryService<ActiveWindows>(out var windows)
            ? windows.For(assets, kind, motion.Clip, motion.StrikeSeconds, config.Window).PeakSpeed * MathF.Abs(motion.Rate * rate) : 0f);
        float mass = assets is not null && kind >= 0 ? config.WeaponOf(assets, kind).MassKg : 0f;
        bool protectedPerson = ProtectedPeople.Is(world, t);

        placedQ.Publish(new FixedContactPlaced(a, tick, t, id, spec.Index, site, blow, clip, spec.ClosingSpeed, spec.SquarenessDegrees, impact, spec.AlongBlade, peak, rate, true, null));
        var made = new ContactMade(id, a, 0, t, ContactMet.Body, site, config.Body, tick, point, normal, impact, mass * impact,
            PreContactBlocked: false, HitCoverOrShield: false, LineOfFire: true, Protected: protectedPerson,
            PointSpeed: spec.ClosingSpeed, AlongBlade: spec.AlongBlade, TipSpeed: 0f, PeakSpeed: peak, Chain: 0, Guard: GuardMeet.None,
            Fixed: true, BlowClip: clip);
        world.Events<ContactMade>().Publish(made);

        // What it does to the body, as a swept contact's (ContactSystem.Publish): a hurt unless protected, dead or dying (with rules
        // that resolve harm, the flinch waits for the harm: HurtSystem); a protest; violence seen; a catch let go.
        if (!protectedPerson && (!world.TryService<HurtRule>(out var hurtRule) || hurtRule.OnContact))
        {
            ref var ta = ref world.Store<CharacterAnimation>().Find(t);
            ref var intent = ref world.Store<CombatIntent>().Find(t);
            if (!Unsafe.IsNullRef(ref ta) && !ActionRoles.IsDeath(ta.Action) && !Unsafe.IsNullRef(ref intent) && intent.Play == ActionRole.None)
                intent.Play = ActionRole.Hurt;
        }
        if (protectedPerson) world.Events<ProtectedProtest>().Publish(new ProtectedProtest(t, a, tick, id));
        if (world.TryService<EventQueue<MasqueradeRisk>>(out var risks) && world.TryService<LevelSpace>(out var level) && level.Places.At(ab.Feet) == PlaceKind.Public)
            risks.Publish(new MasqueradeRisk(a, tick, MasqueradeAct.Violence, TraversalKind.None, ab.Feet, impact, 0f));
        MasqueradeCatch.LetGo(world, t, a, id);
        _ = tb;
    }

    /// <summary>
    /// A fixed contact on the defender's guard: its shield's posed box's middle, or its blade's; met as a swept one's (the
    /// rules' guard contest judges it, <c>GuardResolved</c>; the harm blocks it). What the follow-through does after a swept guard
    /// meeting — the attacker's rebound and stun, an overpowering blade going on into the body, the guard broken — needs a swinging
    /// blade, so a fixed contact has none of it.
    /// </summary>
    static void DeliverGuard(World world, FixedContactSpec spec, EntityId a, EntityId t, long tick, long id, string blow, float impact, Vector3 attackerFeet)
    {
        var placedQ = world.Events<FixedContactPlaced>();
        Vector3 point;
        bool shield = spec.Meets == FixedMeet.Shield;
        if (shield && HeldItems.Shield(world, t, out var box)) point = box.Centre;
        else if (!shield && HeldItems.Blade(world, t, out var seg)) point = (seg.Guard + seg.Tip) * 0.5f;
        else
        {
            placedQ.Publish(new FixedContactPlaced(a, tick, t, 0, spec.Index, null, blow, null, spec.ClosingSpeed, spec.SquarenessDegrees, impact, spec.AlongBlade, 0f, 0f, false,
                $"{spec.Defender} holds no {(shield ? "shield" : "blade")} to meet"));
            return;
        }
        var config = world.TryService<ContactConfig>(out var cc) ? cc : Defaults;
        world.TryService<CharacterAssets>(out var assets);
        ref var anim = ref world.Store<CharacterAnimation>().Find(a);
        int kind = Unsafe.IsNullRef(ref anim) ? -1 : anim.Kind;
        ref var tanim = ref world.Store<CharacterAnimation>().Find(t);
        int tkind = Unsafe.IsNullRef(ref tanim) ? -1 : tanim.Kind;
        var motion = assets is null || kind < 0 ? ActionMotion.None : MotionOf(world, assets, kind, spec);
        string? clip = motion.Has && assets is not null ? assets.Clip(motion.Clip).Name : null;
        float rate = spec.MotionRate ?? (world.TryStore<CombatTempo>(out var tempos) ? CombatTempos.Of(tempos, a).SwingRate : 1f);
        float peak = spec.PeakSpeed ?? (motion.Has && assets is not null && world.TryService<ActiveWindows>(out var windows)
            ? windows.For(assets, kind, motion.Clip, motion.StrikeSeconds, config.Window).PeakSpeed * MathF.Abs(motion.Rate * rate) : 0f);
        float mass = assets is not null && kind >= 0 ? config.WeaponOf(assets, kind).MassKg : 0f;
        var material = assets is null || tkind < 0 ? ContactMaterial.Steel : shield ? config.ShieldOf(assets, tkind) : config.WeaponOf(assets, tkind).Material;
        bool guardOn = !world.TryService<GuardSettings>(out var gs) || gs.Enabled;
        var guard = !guardOn ? GuardMeet.None : spec.Meets switch { FixedMeet.Shield => GuardMeet.Shield, FixedMeet.HeldBlade => GuardMeet.Blade, _ => GuardMeet.Parry };
        var across = new Vector2(attackerFeet.X - point.X, attackerFeet.Y - point.Y);
        var normal = across.LengthSquared() > 1e-8f ? Vector3.Normalize(new Vector3(across, 0f)) : Vector3.UnitX;
        bool protectedPerson = ProtectedPeople.Is(world, t);
        string site = shield ? "shield" : spec.Meets == FixedMeet.HeldBlade ? "blade held in guard" : "parrying blade";
        placedQ.Publish(new FixedContactPlaced(a, tick, t, id, spec.Index, site, blow, clip, spec.ClosingSpeed, spec.SquarenessDegrees, impact, spec.AlongBlade, peak, rate, true, null));
        world.Events<ContactMade>().Publish(new ContactMade(id, a, 0, t, shield ? ContactMet.Shield : ContactMet.Weapon, null, material, tick, point, normal, impact, mass * impact,
            PreContactBlocked: false, HitCoverOrShield: shield, LineOfFire: true, Protected: protectedPerson,
            PointSpeed: spec.ClosingSpeed, AlongBlade: spec.AlongBlade, TipSpeed: 0f, PeakSpeed: peak, Chain: 0, Guard: guard,
            Fixed: true, BlowClip: clip));
        if (protectedPerson) world.Events<ProtectedProtest>().Publish(new ProtectedProtest(t, a, tick, id));
        if (world.TryService<EventQueue<MasqueradeRisk>>(out var risks) && world.TryService<LevelSpace>(out var level) && level.Places.At(attackerFeet) == PlaceKind.Public)
            risks.Publish(new MasqueradeRisk(a, tick, MasqueradeAct.Violence, TraversalKind.None, attackerFeet, impact, 0f));
        MasqueradeCatch.LetGo(world, t, a, id);
    }

    /// <summary>The attacker's own blow the contact stands for: a quick blow by direction (the directions table), or its strong blow.</summary>
    internal static ActionMotion MotionOf(World world, CharacterAssets assets, int kind, FixedContactSpec spec)
    {
        if (assets.Kind(kind).Actions is not { HasAttack: true } set) return ActionMotion.None;
        if (spec.Strength == AttackStrength.Strong) return set.Strong;
        if (world.TryService<DirectionalAttacks>(out var directions))
        {
            var m = directions.Motion(world, assets, kind, spec.Direction);
            if (m.Has) return m;
        }
        // The platform's own table (DirectionalAttacks.DefaultRows): the second quick blow from the left, the first from the right.
        int q = spec.Direction == AttackDirection.Left ? 1 : 0;
        return q < set.Quick.Length ? set.Quick[q] : set.Quick.Length > 0 ? set.Quick[0] : ActionMotion.None;
    }

    static Vector3 Middle(ReadOnlySpan<HitVolume> posed, BodyRegion region, out int n)
    {
        var sum = Vector3.Zero;
        n = 0;
        foreach (var v in posed)
        {
            if (v.Region != region) continue;
            sum += (v.Capsule.A + v.Capsule.B) * 0.5f;
            n++;
        }
        return n > 0 ? sum / n : Vector3.Zero;
    }

    /// <summary>A region as <c>aimAt</c> names it, on the left and on the right (one region for those with no side).</summary>
    public static (BodyRegion Left, BodyRegion Right) Sides(AimRegion r) => r switch
    {
        AimRegion.Head => (BodyRegion.Head, BodyRegion.Head),
        AimRegion.Neck => (BodyRegion.Neck, BodyRegion.Neck),
        AimRegion.Chest => (BodyRegion.Chest, BodyRegion.Chest),
        AimRegion.Belly => (BodyRegion.Belly, BodyRegion.Belly),
        AimRegion.Hips => (BodyRegion.Hips, BodyRegion.Hips),
        AimRegion.UpperArm => (BodyRegion.LeftUpperArm, BodyRegion.RightUpperArm),
        AimRegion.Forearm => (BodyRegion.LeftForearm, BodyRegion.RightForearm),
        AimRegion.Hand => (BodyRegion.LeftHand, BodyRegion.RightHand),
        AimRegion.Thigh => (BodyRegion.LeftThigh, BodyRegion.RightThigh),
        AimRegion.Shin => (BodyRegion.LeftShin, BodyRegion.RightShin),
        _ => (BodyRegion.LeftFoot, BodyRegion.RightFoot),
    };
}

/// <summary>The fixed contacts registered: only in a world whose scenario program places some, so every other world is unchanged.</summary>
public static class FixedContactServices
{
    /// <summary>In <see cref="Phase.Contact"/>, after the blades' contact (60), the arrows (65) and the extra blows (70).</summary>
    public const int Order = 75;

    /// <summary>Registers <see cref="FixedContactPlaced"/>, the <see cref="FixedContactPlan"/> and <c>Contact/75 platform.fixed-contacts</c>. After <see cref="ContactServices.Register"/>.</summary>
    public static WorldBuilder Register(WorldBuilder b, IEnumerable<FixedContactSpec> contacts)
    {
        if (!b.Peek.TryService<EventQueue<FixedContactPlaced>>(out _)) b.Event<FixedContactPlaced>();
        if (!b.Peek.TryService<EventQueue<ContactMade>>(out _)) b.Event<ContactMade>();
        b.Service(new FixedContactPlan(contacts));
        b.System(Phase.Contact, Order, "platform.fixed-contacts", new FixedContactSystem());
        return b;
    }
}
