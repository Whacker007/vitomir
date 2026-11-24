// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the platform's side of a Celerity swing's extra blows — each its own strike that must still find its
// target at its own moment ("We can go with b").
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Combat;

/// <summary>
/// What a Celerity swing's extra blows are (the rules package asked for the three side by side). The
/// rules module chooses (its switches <c>flurry-compressed</c> and <c>flurry-off</c>); the platform checks the geometric one.
/// </summary>
public enum FlurryModel : byte
{
    /// <summary>, the default: each extra blow its own strike at its own moment, landing only if the blade's line still meets the target then.</summary>
    Geometric = 0,
    /// <summary>The earlier build's translation: every extra blow resolved at once on the swing's one contact (a shield stops all; nothing else can).</summary>
    Compressed = 1,
    /// <summary>Pure tempo: no extra blows; Celerity quickens the body alone.</summary>
    Off = 2,
}

/// <summary>When the extra blows fall (the module's data, <c>Data/rules/flurry.json</c>).</summary>
public enum FlurrySpacing : byte
{
    /// <summary>The k-th <see cref="FlurrySettings.SpacingSeconds"/> × k after the contact (the cue's 0.04 s by default).</summary>
    Fixed = 0,
    /// <summary>Spread evenly through the rest of the swing (the attacker's <see cref="Combatant.Busy"/> at the contact, divided by the blows + 1).</summary>
    Swing = 1,
}

/// <summary>
/// The flurry's numbers (a service, not digested: set once at load from the rules module's data, ENGINE_SANDBOX). The
/// check sweeps the blade's segment at the contact — carried with the attacker's body to where it is now — along the way it went
/// into the body, from <see cref="ApproachMetres"/> before the struck place to <see cref="ReachMetres"/> past it.
/// </summary>
public sealed class FlurrySettings
{
    /// <summary>The model the rules play (the platform schedules and checks blows only under <see cref="FlurryModel.Geometric"/>).</summary>
    public FlurryModel Model { get; set; } = FlurryModel.Geometric;

    public FlurrySpacing Spacing { get; set; } = FlurrySpacing.Fixed;

    /// <summary>Under <see cref="FlurrySpacing.Fixed"/>: the time between blows, s (the cue's 0.04 s).</summary>
    public float SpacingSeconds { get; set; } = 0.04f;

    /// <summary>How far before the struck place the blade's line starts (anything there — a shield raised since — is met first), m.</summary>
    public float ApproachMetres { get; set; } = 0.5f;

    /// <summary>How far past the struck place the blade still reaches (a target that stepped back further is out of reach), m.</summary>
    public float ReachMetres { get; set; } = 0.15f;

    // ---- the engine stitches the motion it needs ----

    /// <summary>
    /// The re-strike motion (off by default): for each extra blow the swing's clip, bitten at the contact, is pulled back a short way
    /// and driven forward through the strike again, arriving at the contact's pose at the blow's moment — the blade seen re-striking
    /// along the line the check sweeps (<see cref="FlurryBlows.Restrike"/>, <see cref="FollowThroughSystem"/>'s
    /// <see cref="BladeHoldState.Restrike"/>). Presentation of the check, which it does not change.
    /// </summary>
    public bool Motion { get; set; }

    /// <summary>How far the clip is pulled back from the contact for a re-strike, in the clip's own seconds (at most the clip's time at the contact).</summary>
    public float PullBackClipSeconds { get; set; } = 0.07f;

    /// <summary>The fastest a re-strike plays the clip, as a multiple of the rate the swing met at (a short spacing pulls back less).</summary>
    public float RestrikeRateMost { get; set; } = 3f;
}

/// <summary>
/// A swing's extra blows still to fall (digested, on the attacker; plain data). Set by the rules after the swing's own blow
/// (<see cref="FlurryBlows.Begin"/>), each blow checked at its moment and the component taken away after the last. The blade's
/// segment through the struck place, the way into the body and the place itself are kept in the attacker's frame at the contact
/// (its feet and its yaw), so the check carries them with the body; the contact's own numbers are kept for each strike's evidence;
/// the swing's verdict as the rules gave it, carried by its blows.
/// </summary>
public struct FlurryPending
{
    public long ContactId, Began;
    public EntityId Target;
    public int Swing, Count, Next, Cancelled;
    /// <summary>Seconds between blows (fixed at the contact).</summary>
    public float Spacing;
    public Vector3 Guard, Tip, Into, Place, Normal;
    public float Radius;
    public float ImpactSpeed, NormalImpulse, PointSpeed, AlongBlade, TipSpeed, PeakSpeed;
    public int Region, Material, Protected;
    public int Angle, Continues;
    public float CutShare, BudgetLeft;
    /// <summary>The attacker's clip time at the contact (its pose when it bit: where a re-strike arrives).</summary>
    public float ClipAt;
}

/// <summary>The flurry's platform calls: the strike's id and the rules' start of a swing's extra blows.</summary>
public static class FlurryBlows
{
    /// <summary>The most extra blows one swing can carry (the id's room: 15; Celerity 10's flurry is 3.3).</summary>
    public const int MostBlows = 15;

    /// <summary>A logical strike's id: the contact's id × 16 + the blow's place in the flurry (0 the swing's own blow).</summary>
    public static long Id(long contactId, int index) => contactId * 16 + index;

    /// <summary>The logical strike's text for the tables: <c>&lt;contact&gt;/&lt;k&gt;</c>.</summary>
    public static string Text(long strike) => $"{strike >> 4}/{strike & 15}";

    /// <summary>The name of a check for the tables and the log (<c>met</c>, <c>guarded</c>, <c>out_of_reach</c>, …).</summary>
    public static string Name(FlurryCheck check) => check switch
    {
        FlurryCheck.Met => "met",
        FlurryCheck.Guarded => "guarded",
        FlurryCheck.OutOfReach => "out_of_reach",
        FlurryCheck.Dodged => "dodged",
        FlurryCheck.AttackerStopped => "attacker_stopped",
        FlurryCheck.TargetDown => "target_down",
        _ => "compressed",
    };

    /// <summary>
    /// The rules' call (in <see cref="Phase.Resolve"/>, the contact's tick, after its own blow): the swing of
    /// <paramref name="c"/> carries <paramref name="count"/> extra blows, each to be checked at its moment and published as a
    /// <see cref="FlurryStrike"/>. Keeps the blade's line through the struck place in the attacker's frame now. A flurry still
    /// pending for the attacker (an earlier swing's) is ended first, its blows told <see cref="FlurryCheck.AttackerStopped"/>.
    /// False when the platform's side is not registered or the attacker has no body.
    /// </summary>
    public static bool Begin(World world, in ContactMade c, int count, AngleBand angle = AngleBand.None, float cutShare = 1f,
        bool continues = false, float budgetLeft = 0f)
    {
        if (count <= 0 || !world.TryService<FlurrySettings>(out var settings) || !world.TryStore<FlurryPending>(out var pending)) return false;
        if (!world.TryStore<Body>(out var bodies)) return false;   // a world of rules alone: no geometry to check a blow by
        ref var body = ref bodies.Find(c.Attacker);
        if (Unsafe.IsNullRef(ref body)) return false;
        ref var was = ref pending.Find(c.Attacker);
        if (!Unsafe.IsNullRef(ref was)) FlurryBlowSystem.EndAll(world, c.Attacker, ref was, FlurryCheck.AttackerStopped);
        count = Math.Min(count, MostBlows);
        var config = world.TryService<ContactConfig>(out var cc) ? cc : null;
        // The blade through the struck place: its direction now (posed this tick), placed so the contact is where along it it met.
        Vector3 u = Vector3.Zero;
        float length = 0f, radius = config?.HandRadius ?? 0.06f;
        if (ContactSystem.Striking(world, c.Attacker, radius, out var seg))
        {
            length = (seg.Tip - seg.Guard).Length();
            if (length > 1e-4f) u = (seg.Tip - seg.Guard) / length;
            radius = seg.Radius;
        }
        float along = Math.Clamp(c.AlongBlade, 0f, 1f);
        var guard = c.Point - u * (along * length);
        var tip = c.Point + u * ((1f - along) * length);
        var into = c.Normal.LengthSquared() > 1e-8f ? -Vector3.Normalize(c.Normal) : Flat(c.Point - body.Feet);
        float spacing = settings.Spacing == FlurrySpacing.Swing && TryBusy(world, c.Attacker, out float busy) && busy > 0f
            ? busy / (count + 1) : settings.SpacingSeconds;
        var frame = Frame(in body);
        ref var anim = ref world.TryStore<CharacterAnimation>(out var anims) ? ref anims.Find(c.Attacker) : ref Unsafe.NullRef<CharacterAnimation>();
        float clipAt = Unsafe.IsNullRef(ref anim) ? 0f : anim.ActionTime;   // contact put the clip back to the contact (the bite)
        pending.Set(c.Attacker, new FlurryPending
        {
            ContactId = c.ContactId, Began = world.Tick, Target = c.Target, Swing = c.Swing, Count = count, Next = 1, Spacing = spacing,
            Guard = ToLocal(guard, frame), Tip = ToLocal(tip, frame), Into = ToLocalDir(into, frame), Place = ToLocal(c.Point, frame),
            Normal = ToLocalDir(c.Normal, frame), Radius = radius,
            ImpactSpeed = c.ImpactSpeed, NormalImpulse = c.NormalImpulse, PointSpeed = c.PointSpeed, AlongBlade = c.AlongBlade,
            TipSpeed = c.TipSpeed, PeakSpeed = c.PeakSpeed, Region = (int)RegionOf(c.Site), Material = (int)c.Material,
            Protected = c.Protected ? 1 : 0, Angle = (int)angle, Continues = continues ? 1 : 0, CutShare = cutShare, BudgetLeft = budgetLeft,
            ClipAt = clipAt,
        });
        return true;
    }

    /// <summary>
    /// The re-strike motion's clip time for the next tick (<paramref name="tick"/> + 1) of <paramref name="a"/>'s
    /// <paramref name="swing"/>, with the motion on and blows of it still to fall: between one blow's moment (the contact's, for the
    /// first) and the next, the clip is pulled back from the contact's time over the first half and driven forward to it over the
    /// second, arriving at the contact's pose at the blow's moment. The pull-back is <see cref="FlurrySettings.PullBackClipSeconds"/>
    /// of clip time, at most what <see cref="FlurrySettings.RestrikeRateMost"/> × <paramref name="rate"/> covers in half the
    /// interval, and never before the clip's start. False when there is none to drive (the motion off, no flurry of that swing
    /// pending, its last moment come): the follow-through then brings the blade back to guard.
    /// </summary>
    public static bool Restrike(World world, EntityId a, int swing, long tick, float rate, out float clipTime)
    {
        clipTime = 0f;
        if (!world.TryService<FlurrySettings>(out var s) || !s.Motion || !world.TryStore<FlurryPending>(out var pending)) return false;
        ref var p = ref pending.Find(a);
        if (Unsafe.IsNullRef(ref p) || p.Swing != swing || p.Next > p.Count) return false;
        long next = tick + 1;
        long to = FlurryBlowSystem.Moment(in p, p.Next);
        long from = p.Next == 1 ? p.Began : FlurryBlowSystem.Moment(in p, p.Next - 1);
        if (next > to) return false;
        float span = (to - from) * World.TickSeconds;
        float back = MathF.Min(MathF.Min(s.PullBackClipSeconds, s.RestrikeRateMost * MathF.Abs(rate) * span * 0.5f), p.ClipAt);
        float u = Math.Clamp((next - from) / (float)Math.Max(1, to - from), 0f, 1f);
        float tri = u < 0.5f ? u * 2f : (1f - u) * 2f;   // out and back: 0 at either moment, the whole pull-back half way
        clipTime = p.ClipAt - back * tri;
        return true;
    }

    /// <summary>Whether <paramref name="a"/>'s <paramref name="swing"/> has extra blows to re-strike (the motion on, some still to fall).</summary>
    public static bool Restrikes(World world, EntityId a, int swing)
    {
        if (!world.TryService<FlurrySettings>(out var s) || !s.Motion || !world.TryStore<FlurryPending>(out var pending)) return false;
        ref var p = ref pending.Find(a);
        return !Unsafe.IsNullRef(ref p) && p.Swing == swing && p.Next <= p.Count;
    }

    /// <summary>The region a contact's site names (the hit volumes' table names), <see cref="BodyRegion.None"/> for none.</summary>
    public static BodyRegion RegionOf(string? site)
    {
        if (site is null) return BodyRegion.None;
        foreach (var r in BodyRegions.All) if (string.Equals(BodyRegions.Name(r), site, StringComparison.Ordinal)) return r;
        return BodyRegion.None;
    }

    static bool TryBusy(World world, EntityId e, out float busy)
    {
        busy = 0f;
        if (!world.TryStore<Combatant>(out var fighters)) return false;
        ref var c = ref fighters.Find(e);
        busy = Unsafe.IsNullRef(ref c) ? 0f : c.Busy;
        return !Unsafe.IsNullRef(ref c);
    }

    /// <summary>The level part of a direction, made unit (the body's forward when none).</summary>
    static Vector3 Flat(Vector3 d)
    {
        d.Z = 0f;
        return d.LengthSquared() > 1e-8f ? Vector3.Normalize(d) : Vector3.UnitX;
    }

    /// <summary>A body's frame: its feet and its yaw (a turn about the up axis), the place's own scale left out.</summary>
    internal static (Vector3 Feet, float Cos, float Sin) Frame(in Body body) => (body.Feet, DetMath.Cos(body.Yaw), DetMath.Sin(body.Yaw));

    internal static Vector3 ToLocal(Vector3 p, (Vector3 Feet, float Cos, float Sin) f) => ToLocalDir(p - f.Feet, f);

    internal static Vector3 ToLocalDir(Vector3 d, (Vector3 Feet, float Cos, float Sin) f) => new(d.X * f.Cos + d.Y * f.Sin, -d.X * f.Sin + d.Y * f.Cos, d.Z);

    internal static Vector3 FromLocal(Vector3 l, (Vector3 Feet, float Cos, float Sin) f) => f.Feet + FromLocalDir(l, f);

    internal static Vector3 FromLocalDir(Vector3 l, (Vector3 Feet, float Cos, float Sin) f) => new(l.X * f.Cos - l.Y * f.Sin, l.X * f.Sin + l.Y * f.Cos, l.Z);
}

/// <summary>
/// The extra blows checked at their moments (<see cref="Phase.Contact"/>, after contact): for each swing with
/// blows pending (<see cref="FlurryPending"/>), each blow whose moment has come is checked from the geometry the platform has —
/// never a roll — and published as a <see cref="FlurryStrike"/>:
/// <list type="number">
/// <item>the <b>attacker</b> still swinging that swing (not cancelled — a feint or a dodge, <see cref="SwingCancelled"/> —, no new
/// swing, not stopped, its weapon hand not cut off), else <see cref="FlurryCheck.AttackerStopped"/> (and every blow after it);</item>
/// <item>the <b>target</b> up (not stopped, not dead, still there), else <see cref="FlurryCheck.TargetDown"/> (and every blow after it);</item>
/// <item>the blade's <b>line</b>: its segment at the contact, carried with the attacker's body (its feet and yaw now), swept along the
/// way it went into the body from <see cref="FlurrySettings.ApproachMetres"/> before the struck place to
/// <see cref="FlurrySettings.ReachMetres"/> past it (<see cref="ContactMath"/>'s sweep, as contact sweeps a swing) against the
/// target's shield, its blade when it guards (or is in its parry window), and its hit volumes on its pose now (a part cut off is
/// met no more). What it meets first decides: a shield or a guarding blade <see cref="FlurryCheck.Guarded"/> (the guard met, as
/// contact names it: shield, blade, parry, incidental), a body part <see cref="FlurryCheck.Met"/> (that part); nothing,
/// <see cref="FlurryCheck.Dodged"/> when the target is dodging or dodged since the contact, else
/// <see cref="FlurryCheck.OutOfReach"/>.</item>
/// </list>
/// Deterministic (the store's order, from its end); nothing allocated per tick. Under any model but
/// <see cref="FlurryModel.Geometric"/> nothing is pending, and it does nothing.
/// </summary>
public sealed class FlurryBlowSystem : ISystem
{
    public void Run(World world)
    {
        if (!world.TryStore<FlurryPending>(out var pending) || pending.Count == 0) return;
        var settings = world.Service<FlurrySettings>();
        // A cancel this tick (a feint or a dodge) ends its swing's flurry.
        if (world.TryService<EventQueue<SwingCancelled>>(out var cancels))
        {
            var list = cancels.ThisTick;
            for (int i = 0; i < list.Count; i++)
            {
                ref var p = ref pending.Find(list[i].Entity);
                if (!Unsafe.IsNullRef(ref p) && list[i].Swing == p.Swing) p.Cancelled = 1;
            }
        }
        var ents = pending.Entities;
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            var a = ents[i];
            ref var p = ref pending.Data[i];
            while (p.Next <= p.Count && world.Tick >= Moment(in p, p.Next))
            {
                var check = Check(world, settings, a, in p, out var strike);
                world.Events<FlurryStrike>().Publish(strike);
                p.Next++;
                if (check is FlurryCheck.AttackerStopped or FlurryCheck.TargetDown)
                {
                    EndAll(world, a, ref p, check);
                    break;
                }
            }
            if (p.Next > p.Count) pending.Remove(a);
        }
    }

    /// <summary>The tick blow <paramref name="k"/> falls on: the contact's tick and k spacings, at least k ticks after it.</summary>
    public static long Moment(in FlurryPending p, int k) => p.Began + Math.Max(k, (long)MathF.Round(k * p.Spacing * World.TicksPerSecond));

    /// <summary>Tells every blow of the flurry still to fall as <paramref name="why"/> now (published), and ends it.</summary>
    internal static void EndAll(World world, EntityId a, ref FlurryPending p, FlurryCheck why)
    {
        var events = world.Events<FlurryStrike>();
        for (; p.Next <= p.Count; p.Next++)
        {
            var c = Evidence(world, a, in p, world.Tick, ContactMet.Body, GuardMeet.None, BodyRegion.None, Vector3.Zero, Vector3.Zero, false);
            events.Publish(Strike(in p, a, world.Tick, why, ContactMet.Body, GuardMeet.None, BodyRegion.None, c.Point, 0f, in c));
        }
    }

    static FlurryStrike Strike(in FlurryPending p, EntityId a, long tick, FlurryCheck check, ContactMet met, GuardMeet guard, BodyRegion region,
        Vector3 point, float moved, in ContactMade c) =>
        new(FlurryBlows.Id(p.ContactId, p.Next), p.ContactId, a, p.Swing, p.Target, p.Next, p.Count, tick, check, met, guard, region, point, moved, c,
            (AngleBand)p.Angle, p.CutShare, p.Continues != 0, p.BudgetLeft);

    /// <summary>The strike's evidence: the swing's contact as the strike meets it now.</summary>
    static ContactMade Evidence(World world, EntityId a, in FlurryPending p, long tick, ContactMet met, GuardMeet guard, BodyRegion region,
        Vector3 point, Vector3 normal, bool measured)
    {
        if (!measured)
        {
            ref var body = ref world.Store<Body>().Find(a);
            var f = Unsafe.IsNullRef(ref body) ? (Vector3.Zero, 1f, 0f) : FlurryBlows.Frame(in body);
            point = FlurryBlows.FromLocal(p.Place, f);
            normal = FlurryBlows.FromLocalDir(p.Normal, f);
            region = (BodyRegion)p.Region;
        }
        return new ContactMade(p.ContactId, a, p.Swing, p.Target, met, met == ContactMet.Body && region != BodyRegion.None ? BodyRegions.Name(region) : null,
            (ContactMaterial)p.Material, tick, point, normal, p.ImpactSpeed, p.NormalImpulse, PreContactBlocked: false,
            HitCoverOrShield: met == ContactMet.Shield, LineOfFire: true, Protected: p.Protected != 0, PointSpeed: p.PointSpeed, AlongBlade: p.AlongBlade,
            TipSpeed: p.TipSpeed, PeakSpeed: p.PeakSpeed, Chain: 0, Guard: guard);
    }

    /// <summary>The check at a blow's moment (the class's remarks); the strike to publish.</summary>
    static FlurryCheck Check(World world, FlurrySettings settings, EntityId a, in FlurryPending p, out FlurryStrike strike)
    {
        long tick = world.Tick;
        var t = p.Target;
        FlurryCheck Done(FlurryCheck why, out FlurryStrike s, in FlurryPending q)
        {
            var c = Evidence(world, a, in q, tick, ContactMet.Body, GuardMeet.None, BodyRegion.None, Vector3.Zero, Vector3.Zero, false);
            s = Strike(in q, a, tick, why, ContactMet.Body, GuardMeet.None, BodyRegion.None, c.Point, 0f, in c);
            return why;
        }
        // 1. The attacker still swinging that swing.
        if (!AttackerSwinging(world, a, in p)) return Done(FlurryCheck.AttackerStopped, out strike, in p);
        // 2. The target up.
        if (!TargetUp(world, t)) return Done(FlurryCheck.TargetDown, out strike, in p);
        // 3. The blade's line, carried with the attacker's body, swept against the target's guard and body now.
        ref var ab = ref world.Store<Body>().Get(a);
        var f = FlurryBlows.Frame(in ab);
        var into = FlurryBlows.FromLocalDir(p.Into, f);
        var g = FlurryBlows.FromLocal(p.Guard, f);
        var tip = FlurryBlows.FromLocal(p.Tip, f);
        var place = FlurryBlows.FromLocal(p.Place, f);
        Vector3 back = into * settings.ApproachMetres, on = into * settings.ReachMetres;
        Vector3 p0 = g - back, q0 = tip - back, p1 = g + on, q1 = tip + on;
        float r = p.Radius;

        float guardTime = 2f;
        ContactMet guardMet = ContactMet.Body;
        GuardMeet guardMeet = GuardMeet.None;
        ContactHit guardHit = default;
        var guardOn = world.TryService<GuardSettings>(out var gs) && gs.Enabled;
        ref var tf = ref world.Store<Combatant>().Find(t);
        bool raised = !Unsafe.IsNullRef(ref tf) && tf.Blocking;
        if (HeldItems.Shield(world, t, out var sb))
        {
            var box = new OrientedBox(sb.Centre, sb.AxisX, sb.AxisY, sb.AxisZ, sb.HalfSize);
            if (ContactMath.Sweep(p0, q0, p1, q1, r, in box, out var h) && h.Time < guardTime)
                (guardTime, guardMet, guardHit, guardMeet) = (h.Time, ContactMet.Shield, h, !guardOn ? GuardMeet.None : raised ? GuardMeet.Shield : GuardMeet.Incidental);
        }
        // Their blade, when it guards (the block raised, not swinging: as contact's ParryRule.WhileBlocking).
        bool attacking = world.TryStore<CharacterAnimation>(out var anims) && IsAttacking(anims, t);
        if (raised && !attacking && HeldItems.Blade(world, t, out var w))
        {
            var cap = new OrientedCapsule(w.Guard, w.Tip, w.Radius);
            if (ContactMath.Sweep(p0, q0, p1, q1, r, in cap, out var h) && h.Time < guardTime)
                (guardTime, guardMet, guardHit, guardMeet) = (h.Time, ContactMet.Weapon, h, !guardOn ? GuardMeet.None : tf.ParryLeft > 0f ? GuardMeet.Parry : GuardMeet.Blade);
        }
        // The body: each volume on its pose now (a part cut off met no more).
        float bodyTime = 2f;
        ContactHit bodyHit = default;
        BodyRegion bodyRegion = BodyRegion.None;
        float nearest = float.MaxValue;
        if (world.TryService<HitVolumes>(out var volumes))
        {
            var now = volumes.Posed(world, t);
            uint missing = volumes.Missing(world, t, out var collapse);
            for (int v = 0; v < now.Length; v++)
            {
                if (missing != 0 && collapse is not null && collapse.VolumeGone(missing, now[v].Region, now[v].Bone)) continue;
                var cap = now[v].Capsule;
                Geometry.ClosestOnSegment(place, cap.A, cap.B, out float s);
                float d = MathF.Max(0f, (place - Vector3.Lerp(cap.A, cap.B, s)).Length() - cap.Radius);
                if (d < nearest) nearest = d;
                if (!ContactMath.Sweep(p0, q0, p1, q1, r, in cap, out var h) || h.Time >= bodyTime) continue;
                (bodyTime, bodyHit, bodyRegion) = (h.Time, h, now[v].Region);
            }
        }
        float moved = nearest == float.MaxValue ? 0f : nearest;
        if (guardTime <= 1f && guardTime <= bodyTime)
        {
            var c = Evidence(world, a, in p, tick, guardMet, guardMeet, bodyRegion, guardHit.Point, guardHit.Normal, true);
            strike = Strike(in p, a, tick, FlurryCheck.Guarded, guardMet, guardMeet, bodyRegion, guardHit.Point, moved, in c);
            return FlurryCheck.Guarded;
        }
        if (bodyTime <= 1f)
        {
            var c = Evidence(world, a, in p, tick, ContactMet.Body, GuardMeet.None, bodyRegion, bodyHit.Point, bodyHit.Normal, true);
            strike = Strike(in p, a, tick, FlurryCheck.Met, ContactMet.Body, GuardMeet.None, bodyRegion, bodyHit.Point, moved, in c);
            return FlurryCheck.Met;
        }
        var why = Dodging(world, t, p.Began) ? FlurryCheck.Dodged : FlurryCheck.OutOfReach;
        var e = Evidence(world, a, in p, tick, ContactMet.Body, GuardMeet.None, BodyRegion.None, Vector3.Zero, Vector3.Zero, false);
        strike = Strike(in p, a, tick, why, ContactMet.Body, GuardMeet.None, BodyRegion.None, place, moved, in e);
        return why;
    }

    static bool IsAttacking(Store<CharacterAnimation> anims, EntityId e)
    {
        ref var anim = ref anims.Find(e);
        return !Unsafe.IsNullRef(ref anim) && ActionRoles.IsAttack(anim.Action);
    }

    /// <summary>Whether <paramref name="a"/> still swings the flurry's swing: alive, not stopped, the same swing, not cancelled, its weapon hand on.</summary>
    static bool AttackerSwinging(World world, EntityId a, in FlurryPending p)
    {
        if (p.Cancelled != 0 || !world.Alive(a)) return false;
        if (world.TryStore<Dead>(out var dead) && dead.Has(a)) return false;
        ref var c = ref world.Store<Combatant>().Find(a);
        if (Unsafe.IsNullRef(ref c) || c.Swing != p.Swing) return false;
        if (Unsafe.IsNullRef(ref world.Store<Body>().Find(a))) return false;
        ref var anim = ref world.Store<CharacterAnimation>().Find(a);
        if (Unsafe.IsNullRef(ref anim) || ActionRoles.IsDeath(anim.Action)) return false;
        return !HeldItems.WeaponHandGone(world, a);
    }

    /// <summary>Whether <paramref name="t"/> is up: alive, with a body, not stopped (no <see cref="Dead"/>, no death playing).</summary>
    static bool TargetUp(World world, EntityId t)
    {
        if (t.IsNone || !world.Alive(t)) return false;
        if (world.TryStore<Dead>(out var dead) && dead.Has(t)) return false;
        if (Unsafe.IsNullRef(ref world.Store<Body>().Find(t))) return false;
        ref var anim = ref world.Store<CharacterAnimation>().Find(t);
        return Unsafe.IsNullRef(ref anim) || !ActionRoles.IsDeath(anim.Action);
    }

    /// <summary>Whether <paramref name="t"/> is dodging now, or began a dodge since the contact (tick <paramref name="since"/>).</summary>
    static bool Dodging(World world, EntityId t, long since)
    {
        if (!world.TryStore<Bodies.Dodging>(out var dodges)) return false;
        ref var d = ref dodges.Find(t);
        return !Unsafe.IsNullRef(ref d) && (d.Active || d.Began >= since);
    }
}

/// <summary>The flurry's platform side registered.</summary>
public static class FlurryBlowServices
{
    /// <summary>In <see cref="Phase.Contact"/>, after contact (<see cref="ContactServices.ContactOrder"/>, 60).</summary>
    public const int FlurryOrder = 70;

    /// <summary>
    /// Registers <see cref="FlurrySettings"/>, the digested <see cref="FlurryPending"/>, the event <see cref="FlurryStrike"/> and the
    /// system <c>Contact/70 platform.flurry-blows</c> — once (a second call does nothing: the rules module registers it too, for a
    /// world the program did not).
    /// </summary>
    public static WorldBuilder Register(WorldBuilder b)
    {
        if (b.Peek.TryService<FlurrySettings>(out _)) return b;
        b.Service(new FlurrySettings());
        b.Component<FlurryPending>();
        if (!b.Peek.TryService<EventQueue<FlurryStrike>>(out _)) b.Event<FlurryStrike>();
        b.System(Phase.Contact, FlurryOrder, "platform.flurry-blows", new FlurryBlowSystem());
        return b;
    }
}
