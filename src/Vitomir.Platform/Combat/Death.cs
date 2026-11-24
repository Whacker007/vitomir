// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Runtime.CompilerServices;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Navigation;
using Vitomir.Platform.Perception;

namespace Vitomir.Platform.Combat;

/// <summary>
/// A dead person (ours; digested): the tick it died, the contact that killed it, who struck it, and the death its
/// action channel was asked to play. A body with this component is stopped (<see cref="DeathHoldSystem"/>): it is still
/// there, where it fell, a body the hit volumes and the eyes of others still find, but it moves, walks a route, sees and
/// strikes no more.
/// </summary>
public struct Dead
{
    public long Tick;
    public long ContactId;
    public EntityId Killer;
    public ActionRole Death;
}

/// <summary>
/// What <see cref="ActorDied"/> does to a body (ours: the rules decide who dies, the platform what a death is),
/// in <see cref="Phase.Resolve"/> after the rules (<see cref="DeathServices.DeathOrder"/>), for each person who died
/// and is not already <see cref="Dead"/>:
/// <list type="bullet">
/// <item><b>The death plays</b>: its action channel is asked (<see cref="CombatIntent.Play"/>, taken by the
/// <see cref="AttackSystem"/> on the next tick's <see cref="Phase.Act"/>) for <b>a slow death</b> (1.1's
/// <c>deathslow</c>) when the killing harm was bashing — beaten down, it sinks — and <b>a quick death</b>
/// (<c>deathquick</c>) when it was lethal or aggravated. Ours: 1.1 picks by the killing damage's size (more than 19
/// quick, else slow), a number the staged harm has no equal of. Both are held at their last frame.</item>
/// <item><b>No routes</b>: its <see cref="RouteWalker"/> is taken away (a wanderer wanders no more, a witness leaves its
/// post for no errand).</item>
/// <item><b>No perception</b>: its <see cref="Observer"/> is taken away (it sees nothing, reacts to nothing).</item>
/// <item><b>No control</b>: <see cref="DeathHoldSystem"/> empties its <see cref="MoveIntent"/> every tick, after the
/// player's control and the routes have made one, so the player's keys move a dead body nowhere.</item>
/// <item><b>Its attacks are refused</b>: the death on its action channel makes the <see cref="AttackSystem"/> refuse every
/// press, with <see cref="AttackRefusal.Dead"/>.</item>
/// </list>
/// Its body, its hit volumes and its sheet stay. A death is read from last tick's and this tick's events (so a death
/// published by a later phase is not missed), once: the <see cref="Dead"/> component marks it done. Allocates nothing.
/// </summary>
public sealed class DeathSystem : ISystem
{
    int intentsKnown, walkersKnown, observersKnown;

    public void Run(World world)
    {
        if (!world.TryService<EventQueue<ActorDied>>(out var died)) return;
        if (died.LastTick.Count == 0 && died.ThisTick.Count == 0) return;
        Apply(world, died.LastTick);
        Apply(world, died.ThisTick);
    }

    void Apply(World world, IReadOnlyList<ActorDied> list)
    {
        var intents = PoseRigSystem.Optional<CombatIntent>(world, ref intentsKnown);
        var walkers = PoseRigSystem.Optional<RouteWalker>(world, ref walkersKnown);
        var observers = PoseRigSystem.Optional<Observer>(world, ref observersKnown);
        for (int i = 0; i < list.Count; i++)
        {
            var d = list[i];
            if (!world.Alive(d.Who) || world.Has<Dead>(d.Who)) continue;
            var death = d.Kind == HarmKind.Bashing ? ActionRole.DeathSlow : ActionRole.DeathQuick;
            world.Set(d.Who, new Dead { Tick = d.Tick, ContactId = d.ContactId, Killer = d.Killer, Death = death });
            if (intents is not null)
            {
                ref var intent = ref intents.Find(d.Who);
                if (Unsafe.IsNullRef(ref intent)) intents.Set(d.Who, new CombatIntent { Play = death });
                else
                {
                    intent.Play = death;
                    intent.Attack = false;
                    intent.Block = false;
                }
            }
            if (walkers is not null && walkers.Has(d.Who)) walkers.Remove(d.Who);
            if (observers is not null && observers.Has(d.Who)) observers.Remove(d.Who);
        }
    }
}

/// <summary>When a person last flinched (<see cref="HurtSystem"/>).</summary>
public struct Flinch
{
    public long LastTick;
}

/// <summary>
/// When a blow makes a person flinch (SANDBOX): with rules that resolve harm, only a blow that did harm
/// (<see cref="HarmResolved.Wounds"/> above nothing), and at most once in <see cref="FlinchSeconds"/> — 1.1's own flinch
/// waits 3.5 s — so a blow soaked to nothing does not stop a swing, and a quick foe cannot keep
/// one flinching. Without such rules (<see cref="OnContact"/>, the default), every body hit flinches, as the first contact rule
/// first built.
/// </summary>
public sealed class HurtRule
{
    /// <summary>Flinch on every body contact (no rules resolve harm); a rules module that does sets it false.</summary>
    public bool OnContact { get; set; } = true;

    /// <summary>The least time between two flinches of one person (s).</summary>
    public float FlinchSeconds { get; set; } = 3.5f;

    // ---- the stagger when hit (after playing two earlier builds: "I think we need to bring the stagger
    // when hit back mechanic as well"). ENGINE_SANDBOX, ours: the enhanced module's guard.json "stagger" fills them and turns it on;
    // off (the platform's default, and the module's switch stagger-oneone) the flinch above exactly. ----

    /// <summary>
    /// Whether a wounding blow <b>staggers</b> (instead of the first flinch at most once in <see cref="FlinchSeconds"/>): the hurt
    /// plays for <see cref="StaggerSeconds"/> from the blow's impulse — cutting short a wind-up or a swing before its strike (the blow
    /// lost), lowering a guard — with the upper body pushed away from the blow on the side struck (<see cref="StaggerPush"/>); an
    /// overpowered guard staggers its owner too (<see cref="StaggerOverpowered"/>); no new stagger until <see cref="StaggerGuardSeconds"/>
    /// after the last one ended (no lock). A blow soaked to nothing or resisted by low work deals no wound and so does not stagger.
    /// False (the default; the switch <c>stagger-oneone</c>): the first flinch.
    /// </summary>
    public bool Stagger { get; set; }

    /// <summary>The stagger's time (s; ENGINE_SANDBOX): <see cref="StaggerLeastSeconds"/> + <see cref="StaggerSecondsPerImpulse"/> × the blow's impulse (N·s), at most <see cref="StaggerMostSeconds"/>.</summary>
    public float StaggerLeastSeconds { get; set; } = 0.3f;
    public float StaggerSecondsPerImpulse { get; set; } = 0.01f;
    public float StaggerMostSeconds { get; set; } = 0.6f;

    /// <summary>The impulse (N·s) taken for a blow whose contact is not found (a test, a rule's harm without contact).</summary>
    public float StaggerImpulseDefault { get; set; } = 15f;

    /// <summary>No new stagger of a person until this long after its last stagger ended (s; ENGINE_SANDBOX 0.5): the guard against a lock.</summary>
    public float StaggerGuardSeconds { get; set; } = 0.5f;

    /// <summary>At its end the hurt fades out over this (s; the channel's own fade-out, 0.2), playing on as it fades.</summary>
    public float StaggerFadeSeconds { get; set; } = 0.2f;

    /// <summary>Whether a guard overpowered (the earlier build's broken guard) staggers its owner.</summary>
    public bool StaggerOverpowered { get; set; } = true;

    /// <summary>
    /// A development control: whether a stagger plays 1.1's hurt clip (true: the platform's own default, the module's switch
    /// <c>stagger-hurt-clip</c>) or is the push alone (false: the enhanced module's default, guard.json's <c>stagger.hurt_clip</c>): the swing still cut short (faded from where it is over
    /// <see cref="StaggerFadeSeconds"/>), a waiting blow dropped, the guard lowered and not raised for the stagger's time (the earlier build's broken
    /// guard), the body busy for it, the chest pushed — but the body stays upright, in its stance, where 1.1's hurt doubles it over.
    /// </summary>
    public bool StaggerHurtClip { get; set; } = true;

    /// <summary>
    /// <i>"let's go with the full stagger v1.1 base game animation"</i>: with the hurt clip,
    /// whether it plays <b>whole</b> (true: the stagger lasts the clip's own busy time — 1.1's <c>hurt</c> for the carry, 39 frames,
    /// 1.3 s, as 1.1's flinch played it — the fighter busy for the larger of that and what its cut-short swing had left) or is cut at
    /// <see cref="StaggerSeconds"/> from the blow's impulse (false: the earlier build's short hurt, the switch <c>stagger-hurt-short</c>). The push,
    /// the cut-short swing, the lowered guard and the guard against a lock (<see cref="StaggerGuardSeconds"/> after it ends) either way.
    /// </summary>
    public bool StaggerHurtWhole { get; set; }

    /// <summary>
    /// The push of the upper body (an additive turn of the spine, <see cref="StaggerPush"/>): about the axis the blow's push
    /// turns the chest about (the struck point's lever from the chest crossed with the push), by min(<see cref="PushRadiansPerImpulse"/>
    /// × the impulse, <see cref="PushMostRadians"/>), at its most <see cref="PushPeakSeconds"/> after the blow, sprung back critically
    /// damped (x = A (t/T) e^(1 − t/T)) and let go at <see cref="PushSettleSeconds"/>.
    /// </summary>
    public float PushRadiansPerImpulse { get; set; } = 0.015f;
    public float PushMostRadians { get; set; } = 0.35f;
    public float PushPeakSeconds { get; set; } = 0.08f;
    public float PushSettleSeconds { get; set; } = 0.7f;

    /// <summary>The chest's height above the feet the push's lever is taken from (m).</summary>
    public float PushChestMetres { get; set; } = 1.2f;

    /// <summary>A stagger's time for a blow of <paramref name="impulse"/> N·s.</summary>
    public float StaggerSeconds(float impulse) => Math.Clamp(StaggerLeastSeconds + StaggerSecondsPerImpulse * MathF.Max(0f, impulse), StaggerLeastSeconds, MathF.Max(StaggerLeastSeconds, StaggerMostSeconds));
    // ---- end
}

/// <summary>
/// Plays a hurt for harm done (<see cref="Phase.Resolve"/> after the rules, before the death): each
/// <see cref="HarmResolved"/> with wounds (last tick's and this tick's, like <see cref="DeathSystem"/>), on a person neither
/// dead nor flinched within <see cref="HurtRule.FlinchSeconds"/>. Allocates nothing.
/// </summary>
public sealed class HurtSystem : ISystem
{
    int intentsKnown;

    public void Run(World world)
    {
        var rule = world.Service<HurtRule>();
        if (rule.OnContact || !world.TryService<EventQueue<HarmResolved>>(out var harms)) return;
        if (rule.Stagger) { stagger.Run(world, rule, harms); return; }   // the stagger when hit
        if (harms.LastTick.Count == 0 && harms.ThisTick.Count == 0) return;
        Apply(world, rule, harms.LastTick);     // a harm published after this system last tick (a later phase, a test)
        Apply(world, rule, harms.ThisTick);     // the rules' own, earlier in this phase; the wait keeps one from counting twice
    }

    readonly StaggerStart stagger = new();

    void Apply(World world, HurtRule rule, IReadOnlyList<HarmResolved> list)
    {
        if (list.Count == 0) return;
        var intents = PoseRigSystem.Optional<CombatIntent>(world, ref intentsKnown);
        if (intents is null) return;
        var dead = world.Store<Dead>();
        var flinch = world.Store<Flinch>();
        long wait = Math.Max(1, (long)MathF.Round(rule.FlinchSeconds * World.TicksPerSecond));
        for (int i = 0; i < list.Count; i++)
        {
            var h = list[i];
            if (h.Wounds <= 0 || h.HealthAfter <= 0 || !world.Alive(h.Target) || dead.Has(h.Target)) continue;
            ref var f = ref flinch.Find(h.Target);
            if (!Unsafe.IsNullRef(ref f) && h.Tick - f.LastTick < wait) continue;   // also the same harm seen again
            ref var intent = ref intents.Find(h.Target);
            if (Unsafe.IsNullRef(ref intent) || intent.Play != ActionRole.None) continue;
            intent.Play = ActionRole.Hurt;
            if (Unsafe.IsNullRef(ref f)) flinch.Set(h.Target, new Flinch { LastTick = h.Tick });
            else f.LastTick = h.Tick;
        }
    }
}

/// <summary>
/// Holds the dead still (ours; <see cref="Phase.Think"/>, after the routes, the reactions and the townsfolk's
/// script): every <see cref="Dead"/> body's <see cref="MoveIntent"/> is emptied — no way, no jump, no crouch, no use,
/// no cancel, no turn — so it stays where it fell (a body in the air still falls to the ground: the motor is not
/// stopped, only the wish). Allocates nothing.
/// </summary>
public sealed class DeathHoldSystem : ISystem
{
    int intentsKnown;

    public void Run(World world)
    {
        var dead = world.Store<Dead>();
        if (dead.Count == 0) return;
        var intents = PoseRigSystem.Optional<MoveIntent>(world, ref intentsKnown);
        if (intents is null) return;
        var ents = dead.Entities;
        for (int i = 0; i < ents.Length; i++)
        {
            ref var intent = ref intents.Find(ents[i]);
            if (Unsafe.IsNullRef(ref intent)) continue;
            var guard = intent.Guard;
            intent = default;
            intent.Guard = guard;
        }
    }
}

/// <summary>Death on the platform registered in one call.</summary>
public static class DeathServices
{
    /// <summary>After the modules' rules in <see cref="Phase.Resolve"/>; after the routes (100), the reactions (90) and the script (90) in <see cref="Phase.Think"/>.</summary>
    public const int DeathOrder = 900, HoldOrder = 900, HurtOrder = 800;

    /// <summary>The stagger played out in <see cref="Phase.Act"/> just after the attacks (<see cref="CombatServices.AttackOrder"/> 100), which play the hurt.</summary>
    public const int StaggerOrder = 101;

    /// <summary>
    /// Registers, in this order (the digest's): the component <see cref="Dead"/>; the events <see cref="HarmResolved"/>
    /// and <see cref="ActorDied"/> (the rules publish them; each is registered here unless already registered); the
    /// systems <c>platform.death</c> (<see cref="Phase.Resolve"/>) and <c>platform.death-hold</c> (<see cref="Phase.Think"/>).
    /// </summary>
    public static WorldBuilder Register(WorldBuilder b)
    {
        b.Component<Dead>();
        b.Component<Flinch>();
        if (!b.Peek.TryService<HurtRule>(out _)) b.Service(new HurtRule());
        b.System(Phase.Resolve, HurtOrder, "platform.hurt", new HurtSystem());
        // The stagger when hit — its state, its event, its playing out after the attacks, its push before the rig.
        b.Component<Stagger>();
        if (!b.Peek.TryService<EventQueue<Staggered>>(out _)) b.Event<Staggered>();
        b.System(Phase.Act, StaggerOrder, "platform.stagger", new StaggerSystem());
        b.System(AnimationRegistration.PosePhase, AnimationRegistration.AnimationOrder + StaggerPushSystem.Order, StaggerPushSystem.Name, new StaggerPushSystem());
        if (!b.Peek.TryService<EventQueue<HarmResolved>>(out _)) b.Event<HarmResolved>();
        if (!b.Peek.TryService<EventQueue<ActorDied>>(out _)) b.Event<ActorDied>();
        b.System(Phase.Resolve, DeathOrder, "platform.death", new DeathSystem());
        b.System(Phase.Think, HoldOrder, "platform.death-hold", new DeathHoldSystem());
        return b;
    }

    /// <summary>Whether <paramref name="e"/> is dead (has died and been stopped).</summary>
    public static bool IsDead(World world, EntityId e) => world.Has<Dead>(e);
}
