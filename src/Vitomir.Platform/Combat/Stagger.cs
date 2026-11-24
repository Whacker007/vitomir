// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: 1.1's flinch (for damage above 1, not within 3.5 s of the last, a chance of damage x 8 - Stamina,
// 5 to 95 %; a flinch cancels a swing or a cast and plays hurt + the carry, busy for the clip: the combat survey) is kept
// behind the switch stagger-oneone; this is the engine's stagger, with no roll.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Combat;

/// <summary>Why a person staggered.</summary>
public enum StaggerCause : byte
{
    /// <summary>A blow that wounded (<see cref="HarmResolved.Wounds"/> above nothing).</summary>
    Wound = 0,
    /// <summary>Its guard overpowered (<see cref="GuardVerdict.Overpowered"/>, the earlier build's broken guard).</summary>
    Overpowered = 1,
}

/// <summary>
/// A person staggered (for the log, the arena and the trace): who, by whom, when, the contact, for how long (s), whether
/// it cut short a wind-up or a swing before its strike (<paramref name="Interrupted"/>, that swing's number), how far the upper body was
/// pushed (degrees, at its most) and why.
/// </summary>
public readonly record struct Staggered(EntityId Who, EntityId By, long Tick, long ContactId, float Seconds, bool Interrupted, int Swing,
    float PushDegrees, StaggerCause Cause);

/// <summary>
/// A person's stagger (digested, plain data): asked on <see cref="Asked"/> (the rules' tick), its hurt played on the next
/// <see cref="Phase.Act"/> (<see cref="Pending"/> until then) for <see cref="Seconds"/>, ending at <see cref="Until"/>; no new stagger
/// before <see cref="FreeAt"/>; the busy time its swing had left when it was asked (<see cref="BusyLeft"/>: a swing cut short never
/// quickens the next blow); and the staggers it took.
/// </summary>
public struct Stagger
{
    public long Asked, Until, FreeAt;
    public float Seconds, BusyLeft;
    public int Count;
    int pending;

    /// <summary>Asked, its hurt not yet played.</summary>
    public bool Pending { readonly get => pending != 0; set => pending = value ? 1 : 0; }
}

/// <summary>
/// The stagger begun (from <see cref="HurtSystem"/>, <see cref="Phase.Resolve"/> after the rules): for each guard overpowered
/// (<see cref="GuardResolved"/>, when <see cref="HurtRule.StaggerOverpowered"/>) and each blow that wounded (<see cref="HarmResolved"/>,
/// <see cref="HarmResolved.Wounds"/> above nothing, the struck alive) — last tick's and this tick's — on a person neither dead nor within
/// its guard (<see cref="Stagger.FreeAt"/>) nor already asked to play something: the hurt asked (<see cref="CombatIntent.Play"/>, played
/// by the attacks on the next <see cref="Phase.Act"/>, which cut short what plays and lower the guard), its time from the blow's impulse
/// (<see cref="HurtRule.StaggerSeconds"/>), the upper body's push begun (<see cref="StaggerPush.Start"/>), <see cref="Staggered"/>
/// published. The blow's impulse, point and direction are its contact's (<see cref="ContactMade"/>, found by its id this tick or last).
/// Allocates nothing.
/// </summary>
public sealed class StaggerStart
{
    int intentsKnown;

    public void Run(World world, HurtRule rule, EventQueue<HarmResolved> harms)
    {
        world.TryService<EventQueue<GuardResolved>>(out var guards);
        world.TryService<EventQueue<ContactMade>>(out var contacts);
        if (rule.StaggerOverpowered && guards is not null)
        {
            Guards(world, rule, guards.LastTick, contacts);
            Guards(world, rule, guards.ThisTick, contacts);
        }
        Harms(world, rule, harms.LastTick, contacts);
        Harms(world, rule, harms.ThisTick, contacts);
    }

    void Guards(World world, HurtRule rule, IReadOnlyList<GuardResolved> list, EventQueue<ContactMade>? contacts)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var g = list[i];
            if (g.Verdict != GuardVerdict.Overpowered) continue;
            Begin(world, rule, g.Defender, g.Attacker, g.Tick, g.ContactId, contacts, StaggerCause.Overpowered);
        }
    }

    void Harms(World world, HurtRule rule, IReadOnlyList<HarmResolved> list, EventQueue<ContactMade>? contacts)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var h = list[i];
            if (h.Wounds <= 0 || h.HealthAfter <= 0) continue;
            Begin(world, rule, h.Target, h.Attacker, h.Tick, h.ContactId, contacts, StaggerCause.Wound);
        }
    }

    void Begin(World world, HurtRule rule, EntityId who, EntityId by, long tick, long contactId, EventQueue<ContactMade>? contacts, StaggerCause cause)
    {
        if (!world.Alive(who) || world.Has<Dead>(who)) return;
        var intents = PoseRigSystem.Optional<CombatIntent>(world, ref intentsKnown);
        if (intents is null) return;
        // A world without bodies drawn (a rules test's) has no one to stagger.
        if (!world.TryStore<Stagger>(out var staggers) || !world.TryStore<CharacterAnimation>(out var anims) || !world.TryStore<Combatant>(out var fighters)
            || !world.TryStore<Body>(out var bodies)) return;
        ref var st = ref staggers.Find(who);
        if (!Unsafe.IsNullRef(ref st) && tick < st.FreeAt) return;   // within its guard (also the same harm seen again)
        ref var intent = ref intents.Find(who);
        if (Unsafe.IsNullRef(ref intent) || intent.Play != ActionRole.None) return;
        ref var anim = ref anims.Find(who);
        ref var c = ref fighters.Find(who);
        if (Unsafe.IsNullRef(ref anim) || Unsafe.IsNullRef(ref c) || ActionRoles.IsDeath(anim.Action)) return;

        // The blow: its contact's impulse, point and push (into the struck, out of the attacker's side), else a default from the attacker.
        float impulse = rule.StaggerImpulseDefault;
        Vector3 point = default, push = default;
        bool found = false;
        if (contacts is not null) found = Find(contacts.ThisTick, contactId, out impulse, out point, out push) || Find(contacts.LastTick, contactId, out impulse, out point, out push);
        if (!found) impulse = rule.StaggerImpulseDefault;
        ref var body = ref bodies.Find(who);
        if (Unsafe.IsNullRef(ref body)) return;
        if (!found || push == Vector3.Zero)
        {
            ref var ab = ref bodies.Find(by);
            var d = Unsafe.IsNullRef(ref ab) ? Vector3.Zero : body.Feet - ab.Feet;
            d.Z = 0f;
            push = LimbIK.Unit(d);
            if (push == Vector3.Zero) { var (sy, cy) = DetMath.SinCos(body.Yaw); push = new Vector3(-cy, -sy, 0f); }   // straight back
            point = body.Feet + new Vector3(0f, 0f, rule.PushChestMetres);
        }

        float seconds = rule.StaggerSeconds(impulse);
        // 1.1's hurt played whole — the stagger lasts the clip's own busy time (the attacks play it so: AttackSystem's Play).
        if (rule.StaggerHurtClip && rule.StaggerHurtWhole && HurtSeconds(world, in anim) is > 0f and var whole) seconds = whole;
        bool interrupted = ActionRoles.IsAttack(anim.Action) && !anim.ActionEnding && c.StrikeAt >= 0f && anim.ActionTime < c.StrikeAt;
        long ticks = Math.Max(1, (long)MathF.Ceiling(seconds * World.TicksPerSecond));
        var s = new Stagger
        {
            Asked = tick, Until = tick + 1 + ticks, Seconds = seconds, BusyLeft = c.Busy, Pending = rule.StaggerHurtClip,
            Count = Unsafe.IsNullRef(ref st) ? 1 : st.Count + 1,
        };
        if (rule.StaggerHurtClip) intent.Play = ActionRole.Hurt;   // 1.1's hurt, played by the attacks on the next Act (StaggerSystem times it)
        else PushOnly(world, who, ref c, ref anim, seconds);
        s.FreeAt = s.Until + Math.Max(0, (long)MathF.Round(rule.StaggerGuardSeconds * World.TicksPerSecond));
        if (Unsafe.IsNullRef(ref st)) staggers.Set(who, s); else st = s;
        float angle = StaggerPush.Start(world, who, ref anim, in body, rule, point, push, impulse);
        if (world.TryService<EventQueue<Staggered>>(out var q))
            q.Publish(new Staggered(who, by, tick, contactId, seconds, interrupted, interrupted ? c.Swing : 0, angle * DetMath.RadToDeg, cause));
    }

    /// <summary>
    /// A development control, <see cref="HurtRule.StaggerHurtClip"/> false: the stagger without 1.1's hurt — a swing playing is cut
    /// short (faded from where it is, its blade's hold let go, as a cancel does), a waiting blow dropped, the guard lowered and kept down
    /// for the stagger's time (the earlier build's broken guard: the attacks lower it and do not raise it while it runs), the body busy for it (never
    /// less than its swing had left); the push does the rest.
    /// </summary>
    static void PushOnly(World world, EntityId who, ref Combatant c, ref CharacterAnimation anim, float seconds)
    {
        var rule = world.Service<HurtRule>();
        if (ActionRoles.IsAttack(anim.Action) && !anim.ActionEnding)
        {
            ActionChannel.FadeFromHere(ref anim, rule.StaggerFadeSeconds);
            if (world.TryStore<BladeHold>(out var holds))
            {
                ref var h = ref holds.Find(who);
                if (!Unsafe.IsNullRef(ref h)) h.State = BladeHoldState.None;
            }
        }
        c.Pending = AttackStrength.None;
        c.Buffered = false;
        c.AttackHeld = -1f;
        c.ParryLeft = 0f;
        c.Busy = MathF.Max(c.Busy, seconds);
        c.GuardBroken = MathF.Max(c.GuardBroken, seconds);
    }

    /// <summary>The hurt's own busy time for <paramref name="anim"/>'s kind, as the attacks give it (its motion's busy seconds, else its clip's length); 0 with none.</summary>
    static float HurtSeconds(World world, in CharacterAnimation anim)
    {
        if (!world.TryService<CharacterAssets>(out var assets) || (uint)anim.Kind >= (uint)assets.KindCount) return 0f;
        if (assets.Kind(anim.Kind).Actions is not { } set || set.For(ActionRole.Hurt) is not { Has: true } m) return 0f;
        return m.BusySeconds > 0f ? m.BusySeconds : assets.Clip(m.Clip).Duration;
    }

    static bool Find(IReadOnlyList<ContactMade> list, long id, out float impulse, out Vector3 point, out Vector3 push)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var m = list[i];
            if (m.ContactId != id) continue;
            impulse = MathF.Max(0f, m.NormalImpulse);
            point = m.Point;
            push = LimbIK.Unit(-m.Normal);   // the contact's normal points out of what was met: the push goes into it
            return true;
        }
        impulse = 0f;
        point = push = default;
        return false;
    }
}

/// <summary>
/// The stagger played out (<c>Act/101 platform.stagger</c>, just after the attacks, which play the hurt asked of them): on the
/// tick its hurt began, the busy time set to the larger of what the cut-short swing had left (<see cref="Stagger.BusyLeft"/>, run on by
/// a tick: being struck never quickens a blow) and the stagger's time, and a blade held by its swing's stop let go (as a cancel does);
/// at <see cref="Stagger.Until"/> the hurt fades out (<see cref="HurtRule.StaggerFadeSeconds"/>), playing on as it fades. Nothing
/// allocated.
/// </summary>
public sealed class StaggerSystem : ISystem
{
    public void Run(World world)
    {
        if (!world.TryStore<Stagger>(out var staggers) || staggers.Count == 0) return;
        if (!world.TryService<HurtRule>(out var rule) || !rule.Stagger) return;
        if (!world.TryStore<Combatant>(out var fighters) || !world.TryStore<CharacterAnimation>(out var anims)) return;
        var data = staggers.Data;
        var ents = staggers.Entities;
        long tick = world.Tick;
        for (int i = 0; i < data.Length; i++)
        {
            ref var s = ref data[i];
            if (!s.Pending && tick != s.Until) continue;
            var e = ents[i];
            ref var anim = ref anims.Find(e);
            ref var c = ref fighters.Find(e);
            if (Unsafe.IsNullRef(ref anim) || Unsafe.IsNullRef(ref c)) { s.Pending = false; continue; }
            if (s.Pending)
            {
                s.Pending = false;
                if (anim.Action != ActionRole.Hurt) continue;   // not played (dead, no hurt clip): nothing more
                c.Busy = MathF.Max(MathF.Max(0f, s.BusyLeft - World.TickSeconds), s.Seconds);
                if (world.TryStore<BladeHold>(out var holds))
                {
                    ref var h = ref holds.Find(e);
                    if (!Unsafe.IsNullRef(ref h)) h.State = BladeHoldState.None;
                }
            }
            else if (anim.Action == ActionRole.Hurt && !anim.ActionEnding)
            {
                anim.ActionEnding = true;
                anim.ActionFadeOut = MathF.Max(rule.StaggerFadeSeconds, World.TickSeconds);
            }
        }
    }
}

/// <summary>
/// The upper body pushed by a stagger: an additive turn of the spine about the axis the blow's push turns the chest about — the
/// struck point's lever from the chest crossed with the push, so a blow at the left shoulder turns the left shoulder back and a blow to
/// the middle leans the body back — by min(<see cref="HurtRule.PushRadiansPerImpulse"/> × the impulse, <see cref="HurtRule.PushMostRadians"/>),
/// x(t) = A (t/T) e^(1 − t/T) (no overshoot; the recoil's spring, <see cref="Recoil"/>), let go at <see cref="HurtRule.PushSettleSeconds"/>.
/// <b>Pose, not only presentation</b>: its state is <see cref="CharacterAnimation"/>'s (digested), moved on by <see cref="StaggerPushSystem"/>
/// before the rig and laid on by the pose evaluator, so contact poses the struck body's hit volumes as it is drawn. Scalar arithmetic and
/// <see cref="DetMath"/> only; nothing allocated.
/// </summary>
public static class StaggerPush
{
    /// <summary>Begins the push on <paramref name="anim"/> (the world's point and push taken into the model's frame); its most (radians).</summary>
    public static float Start(World world, EntityId e, ref CharacterAnimation anim, in Body body, HurtRule rule, Vector3 point, Vector3 push, float impulse)
    {
        float peak = MathF.Min(MathF.Max(0f, impulse) * rule.PushRadiansPerImpulse, rule.PushMostRadians);
        if (peak <= 0f || rule.PushPeakSeconds <= 0f) return 0f;
        var chest = body.Feet + new Vector3(0f, 0f, rule.PushChestMetres);
        var axis = LimbIK.Cross(point - chest, push);
        if (LimbIK.Length(axis) < 0.05f) axis = LimbIK.Cross(Vector3.UnitZ, push);   // a lever along the push: lean with it
        axis = LimbIK.Unit(axis);
        if (axis == Vector3.Zero) return 0f;
        float facing = 0f;
        if (world.TryService<CharacterAssets>(out var assets) && (uint)anim.Kind < (uint)assets.KindCount) facing = assets.Kind(anim.Kind).FacingYaw;
        var (sin, cos) = DetMath.SinCos(body.Yaw - facing);
        anim.StaggerAxis = LimbIK.Unit(new Vector3(cos * axis.X + sin * axis.Y, -sin * axis.X + cos * axis.Y, axis.Z));   // PoseRig's ToModel
        anim.StaggerPeak = peak;
        anim.StaggerSince = world.Tick;
        anim.StaggerTurn = 0f;
        return peak;
    }

    /// <summary>Moves one push on to <paramref name="tick"/>; let go (all zero) once settled.</summary>
    public static void Step(ref CharacterAnimation anim, HurtRule rule, long tick)
    {
        if (anim.StaggerPeak <= 0f) return;
        float t = (tick - anim.StaggerSince) * World.TickSeconds;
        if (t >= rule.PushSettleSeconds)
        {
            anim.StaggerPeak = anim.StaggerTurn = 0f;
            anim.StaggerAxis = Vector3.Zero;
            anim.StaggerSince = 0;
            return;
        }
        anim.StaggerTurn = anim.StaggerPeak * Recoil.Share(t, rule.PushPeakSeconds, rule.PushSettleSeconds);
    }

    /// <summary>The push laid on a pose (the evaluator calls it after the recoil): each spine bone turned its share of <see cref="CharacterAnimation.StaggerTurn"/> about the axis.</summary>
    public static void Apply(Skeleton skeleton, CharacterKind kind, in CharacterAnimation state, Span<BoneTransform> local)
    {
        if (state.StaggerTurn == 0f || state.StaggerAxis == Vector3.Zero || kind.Actions is not { } set || set.Spine.Length == 0) return;
        var axis = state.StaggerAxis;
        var (s, c) = DetMath.SinCos(state.StaggerTurn / set.Spine.Length * 0.5f);
        for (int i = 0; i < set.Spine.Length; i++)
        {
            int b = set.Spine[i];
            if ((uint)b >= (uint)local.Length) continue;
            int parent = skeleton.Bones[b].Parent;
            var seen = parent >= 0 && parent < b ? LimbIK.Rotate(LimbIK.Conjugate(LimbIK.ModelRotation(skeleton, local, parent)), axis) : axis;
            seen = LimbIK.Unit(seen);
            var turn = new Quaternion(seen.X * s, seen.Y * s, seen.Z * s, c);
            local[b] = local[b] with { Rotation = LimbIK.Normalize(PoseMath.Multiply(turn, local[b].Rotation)) };
        }
    }
}

/// <summary>The stagger's pushes moved on (<c>Move/208 platform.stagger-push</c>: after the recoil, 207, before the rig, 210). Nothing allocated.</summary>
public sealed class StaggerPushSystem : ISystem
{
    public const string Name = "platform.stagger-push";
    public const int Order = 8;

    static readonly HurtRule Defaults = new();

    public void Run(World world)
    {
        if (!world.TryStore<CharacterAnimation>(out var anims)) return;
        var rule = world.TryService<HurtRule>(out var r) ? r : Defaults;
        var data = anims.Data;
        long tick = world.Tick;
        for (int i = 0; i < data.Length; i++)
            if (data[i].StaggerPeak > 0f) StaggerPush.Step(ref data[i], rule, tick);
    }
}
