// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: 1.1 decides a melee hit by a reach test and a to-hit roll, never by geometry.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;
using static Vitomir.Platform.Combat.Geometry;

namespace Vitomir.Platform.Combat;

/// <summary>The people a swing has met, in order: at most <see cref="SwingContacts.MaxTargets"/>.</summary>
[InlineArray(SwingContacts.MaxTargets)]
public struct SwingTargets
{
    EntityId first;
}

/// <summary>
/// A fighter's swing as contact keeps it (digested): which swing (<see cref="Combatant.Swing"/>) the record is of,
/// whether it is open (the swing plays), whether a shield, a parrying blade or the level stopped it (nothing more is met
/// by it), the people it has met (one contact per swing and person: a blade that passes through a body is one contact)
/// and how many meetings with them again were rejected (published when the swing ends, <see cref="ContactDuplicates"/>).
/// </summary>
public struct SwingContacts
{
    /// <summary>The most people one swing can meet.</summary>
    public const int MaxTargets = 8;

    public int Swing;
    public int Met;
    public int Duplicates;
    int stopped, open;
    public SwingTargets Targets;

    /// <summary>
    /// The contacts the swing has made so far, in order (the next one's <see cref="ContactMade.Chain"/>), while blades
    /// stop where they stop (<see cref="BladeStopRule.Enabled"/>); a swing still sweeping after one went on past a severed part.
    /// </summary>
    public int Chain;

    public bool Stopped { readonly get => stopped != 0; set => stopped = value ? 1 : 0; }
    public bool Open { readonly get => open != 0; set => open = value ? 1 : 0; }

    /// <summary>
    /// At the end: whose guard the swing pushed through (an <see cref="GuardVerdict.Overpowered"/> guard, a
    /// clash it dominated): their shield and blade are met no more by this swing, so it goes on to what is behind them.
    /// </summary>
    public EntityId Through;

    /// <summary>Whether the swing has met <paramref name="e"/> already.</summary>
    public readonly bool Has(EntityId e)
    {
        for (int i = 0; i < Met && i < MaxTargets; i++) if (Targets[i] == e) return true;
        return false;
    }
}

/// <summary>A swing's meetings with people it had already met, rejected: published as the swing ends, when there were any.</summary>
public readonly record struct ContactDuplicates(EntityId Attacker, int Swing, long Tick, int Rejected, int Contacts);

/// <summary>
/// A swing's end (for the traces the rules package asks for): every swing, met or not — the people it met
/// (<paramref name="Contacts"/>, nothing for a swing that met no one), the meetings rejected as repeats, and whether a
/// shield, a blade or the level stopped it. A swing with no <see cref="ContactMade"/> and not stopped met nothing.
/// </summary>
public readonly record struct SwingEnded(EntityId Attacker, int Swing, long Tick, int Contacts, int Rejected, bool Stopped);

/// <summary>A protected person struck: they protest. For the log; the attempt is seen as violence.</summary>
public readonly record struct ProtectedProtest(EntityId Who, EntityId Attacker, long Tick, long ContactId);

/// <summary>
/// The volumes struck lately, for the overlay to mark for a moment (presentation only, not digested): who, which of
/// their volumes (an index into <see cref="HitVolumes.Posed(World, EntityId)"/>), what was met, and when.
/// </summary>
public sealed class StruckVolumes
{
    /// <summary>How long a struck volume stays marked (s).</summary>
    public const float MarkSeconds = 0.6f;

    readonly (EntityId Who, int Volume, ContactMet Met, long Tick, Vector3 Point)[] ring = new (EntityId, int, ContactMet, long, Vector3)[16];
    int next;

    /// <summary>A contact's mark: who (none for the level), which volume (−1 their shield, −2 their blade, −3 the level), what, when and where.</summary>
    public void Add(EntityId who, int volume, ContactMet met, long tick, Vector3 point)
    {
        ring[next] = (who, volume, met, tick, point);
        next = (next + 1) % ring.Length;
    }

    /// <summary>The points on the level struck within <see cref="MarkSeconds"/> of <paramref name="tick"/> (for the picture; allocates).</summary>
    public List<Vector3> LevelPoints(long tick)
    {
        var points = new List<Vector3>();
        foreach (var r in ring)
            if (r.Met == ContactMet.Level && r.Tick > 0 && tick - r.Tick >= 0 && tick - r.Tick < MarkSeconds * World.TicksPerSecond) points.Add(r.Point);
        return points;
    }

    /// <summary>Whether <paramref name="who"/>'s volume <paramref name="volume"/> (or, for −1, their shield) was struck within <see cref="MarkSeconds"/> of <paramref name="tick"/>.</summary>
    public bool Marked(EntityId who, int volume, long tick)
    {
        foreach (var r in ring)
            if (r.Who == who && r.Volume == volume && r.Tick > 0 && tick - r.Tick >= 0 && tick - r.Tick < MarkSeconds * World.TicksPerSecond) return true;
        return false;
    }
}

/// <summary>
/// Contact (<see cref="Phase.Contact"/>, after the strike's moment, WYSIWYG: "the blade you
/// see swing is the blade that hits"). Each tick, for each fighter whose blow plays and whose clip's time this tick overlaps
/// the blow's <see cref="ActiveWindow"/>: its striking segment — the held blade from its guard to its tip
/// (<see cref="HeldItems.Blade"/>), or bare-handed a ball about the weapon hand — is <b>swept</b> from where it was last
/// tick to where it is now (<see cref="ContactMath.Sweep(Vector3, Vector3, Vector3, Vector3, float, in OrientedCapsule, in OrientedCapsule, out ContactHit)"/>,
/// conservative advancement: no tunnelling however fast), against
/// <list type="bullet">
/// <item>every other fighter's <b>shield</b> (<see cref="HeldItems.Shield"/>, an oriented box, from last tick's place to this tick's);</item>
/// <item>their <b>blade</b>, when it parries (<see cref="ContactConfig.Parry"/>: by default only while they hold the block);</item>
/// <item>their <b>bodies</b>: the broad capsule first (<see cref="HitVolumes.Broad"/>, grown for a dying body), then each
/// hit volume, from last tick's volumes (<see cref="HitVolumes.Last"/>) to this tick's — a moving body swept, not sampled;</item>
/// <item><b>the level</b>: the blade's tip and its middle as moving points against the collision.</item>
/// </list>
/// What is met is taken in the order it is met. A shield, a parrying blade or the level <b>stops the swing</b>: nothing
/// after it is met, this tick or later (so <see cref="ContactMade.PreContactBlocked"/> is never true of anything struck),
/// and the attacker's swing is cut short (<see cref="CombatServices.CutShort"/>, the recoil). A body does not stop it: the
/// blade goes on to whoever is behind. One contact per (swing, person); meeting them again is rejected and counted. Each
/// contact is published as <see cref="ContactMade"/>, exactly, and does what it does to the body (presentation and motion
/// only; harm is the rules'): a body plays its hurt (<see cref="CombatIntent.Play"/>) unless protected, dead or dying; a
/// protected person protests (<see cref="ProtectedProtest"/>); a blow that meets a person in a public place is violence
/// (<see cref="MasqueradeAct.Violence"/>, for perception); and a blow that meets a body lets its masquerade's catch go
/// (<see cref="MasqueradeCatch.LetGo"/>; the player's, any body's).
/// <para>
/// Everyone near a fighter whose blow plays (within <see cref="ContactConfig.ReachAtMost"/>) has their volumes, shield
/// and blade kept each tick (<see cref="HitVolumes.Keep"/>, and this system's own memory), so the next tick sweeps from
/// them. That memory is derived from digested state (the poses, the bodies) and not digested itself. Deterministic, the
/// stores' order; nothing allocated per tick once each character's buffers are made.
/// </para>
/// <para>
/// <b>A fast swing in sub-steps</b> (Celerity's tempo, up to 14× with skill): a blow whose
/// <see cref="CombatTempo.SwingRate"/> is above <see cref="SubStepTempo"/> covers so much of its clip in a tick that the straight
/// sweep from last tick's blade to this tick's cuts the arc's chord (at 11× it met the chest where the swing passes the arm).
/// Such a tick is swept in <c>ceil(rate / </c><see cref="SubStepTempo"/><c>)</c> sub-steps (at most <see cref="MaxSubSteps"/>):
/// the blade posed at the clip's times between (the attacker's pose evaluated at those times, its placement this tick's), the
/// targets' shapes between last tick's and this tick's by the same shares, and each sub-step swept as a tick is; what is met
/// is ordered by its time in the whole tick, and its striking speed is the sub-step's. At a rate up to
/// <see cref="SubStepTempo"/> (1.1's timing, the Abilities' tempo) nothing changes, digest for digest.
/// </para>
/// </summary>
public sealed class ContactSystem : ISystem
{
    /// <summary>The swing rate up to which a tick is swept whole: each sub-step covers at most this much of a 1.1 tick's clip.</summary>
    public const float SubStepTempo = 2f;

    /// <summary>The most sub-steps a tick is swept in.</summary>
    public const int MaxSubSteps = 8;

    struct Hit
    {
        public float Time;
        public ContactMet Met;
        public EntityId Target;
        public Vector3 Point, Normal, TargetVelocity;
        public int Volume;
        public BodyRegion Region;
        public ContactMaterial Material;
        /// <summary>The sub-step it was met in, and its time within that sub-step (<see cref="Time"/> is the whole tick's).</summary>
        public int Sub;
        public float Local;
        /// <summary>The guard met (<see cref="ContactMade.Guard"/>; none with the guard off).</summary>
        public GuardMeet Guard;
    }

    // The guard's settings this tick, the attackers whose blades are in their windows (a clash's candidates),
    // and the clash's pre-pass — each such attacker's first hit, the pair it makes, and whether the pair was resolved.
    GuardSettings guardNow = DefaultGuard;
    bool guardOn, clashPass;
    EntityId[] windowed = new EntityId[8];
    int windowedCount;
    Hit[] firstHit = new Hit[8];
    BladePlacement[] firstB0 = new BladePlacement[8], firstB1 = new BladePlacement[8];
    int[] firstN = new int[8], clashWith = new int[8];
    bool[] clashDone = new bool[8];
    static readonly GuardSettings DefaultGuard = new() { Enabled = false };
    readonly ContactMade[] pairMade = new ContactMade[2];

    struct Kept
    {
        public EntityId Of;
        public long Tick;
        public BladePlacement Blade;
        public BoxPlacement Shield;
        public int HasBlade, HasShield;
    }

    const int MaxHits = 64;
    readonly Hit[] hits = new Hit[MaxHits];
    int hitCount;
    EntityId[] attackers = new EntityId[8];
    EntityId[] nearList = new EntityId[16];
    Kept[] kept = [];
    static readonly ContactConfig Defaults = new();

    // The sub-steps of a fast swing — the blades between, the pose they come from, the shares of the tick swept now.
    readonly BladePlacement[] subBlades = new BladePlacement[MaxSubSteps + 1];
    readonly PoseEvaluator subPoser = new();
    Matrix4x4[] subModel = [], subSkin = [];
    int temposKnown, rigsKnown;
    int curSub, curN = 1;
    float curF0, curF1 = 1f;

    // The swing being swept — its record, whether it went on past a severed part (so its people may be met again where
    // the blade newly enters them), and how many of their parts it was found still in (rejected).
    SwingContacts curRecord;
    bool curContinued;
    int curOverlaps;

    /// <summary>Whether a hit on someone the going-on swing already met is the blade still in them (it started the step inside or touching), not a new entry.</summary>
    bool StillIn(EntityId t, in ContactHit h) => curContinued && curRecord.Has(t) && (h.Inside || h.Time <= 0f);

    public void Run(World world)
    {
        if (!world.TryService<CharacterAssets>(out var assets)) return;
        var config = world.TryService<ContactConfig>(out var cc) ? cc : Defaults;
        var windows = world.Service<ActiveWindows>();
        world.TryService<HitVolumes>(out var volumes);
        world.TryService<StruckVolumes>(out var struck);
        var fighters = world.Store<Combatant>();
        var anims = world.Store<CharacterAnimation>();
        var bodies = world.Store<Body>();
        var swings = world.Store<SwingContacts>();
        long tick = world.Tick;
        world.TryService<Presentation.SwingTrails>(out var trails);
        world.TryService<Cameras.ViewState>(out var viewNow);
        world.TryService<Cameras.ReticleState>(out var reticleNow);

        // 1. The swings: a record opened as a blow begins and closed as it ends (its rejected meetings said then).
        int attackerCount = 0;
        var fents = fighters.Entities;
        var fdata = fighters.Data;
        for (int i = 0; i < fents.Length; i++)
        {
            var e = fents[i];
            ref var c = ref fdata[i];
            ref var anim = ref anims.Find(e);
            if (Unsafe.IsNullRef(ref anim)) continue;
            bool playing = ActionRoles.IsAttack(anim.Action) && !anim.ActionEnding;
            ref var sc = ref swings.Find(e);
            if (Unsafe.IsNullRef(ref sc))
            {
                if (!playing) continue;
                world.Set(e, new SwingContacts());
                sc = ref swings.Get(e);
            }
            if (sc.Open && (!playing || sc.Swing != c.Swing)) Close(world, e, ref sc, tick);
            if (!playing) continue;
            if (!sc.Open)
            {
                sc = new SwingContacts { Swing = c.Swing, Open = true, Through = EntityId.None };
                trails?.Begin(e, c.Swing, tick, TrailAim(viewNow, reticleNow, e));   // the aim at the blow's start
            }
            if (attackerCount == attackers.Length) Array.Resize(ref attackers, attackers.Length * 2);
            attackers[attackerCount++] = e;
        }
        if (attackerCount == 0) return;

        // 2. Who is near a swinging fighter (they, and the fighters, are kept for the next tick at the end).
        int nearCount = 0;
        var aents = anims.Entities;
        for (int i = 0; i < aents.Length; i++)
        {
            var e = aents[i];
            ref var body = ref bodies.Find(e);
            if (Unsafe.IsNullRef(ref body)) continue;
            bool near = false;
            for (int k = 0; k < attackerCount && !near; k++)
            {
                if (attackers[k] == e) { near = true; break; }
                ref var ab = ref bodies.Find(attackers[k]);
                if (Unsafe.IsNullRef(ref ab)) continue;
                float dx = body.Feet.X - ab.Feet.X, dy = body.Feet.Y - ab.Feet.Y;
                float reach = config.ReachAtMost + 1.5f;
                near = dx * dx + dy * dy <= reach * reach;
            }
            if (!near) continue;
            if (nearCount == nearList.Length) Array.Resize(ref nearList, nearList.Length * 2);
            nearList[nearCount++] = e;
        }

        // 3. Each swing in its window, swept.
        int serial = 0;
        var level = world.Service<LevelSpace>().Collision;
        var stop = config.Stop;
        guardNow = world.TryService<GuardSettings>(out var gs) ? gs : DefaultGuard;
        guardOn = guardNow.Enabled;
        // The blades in their windows this tick, and the clash's pre-pass : two blades
        // whose first meeting this tick is each other are one paired transaction, found before either is published, so the
        // result does not hang on which body the stores hold first.
        windowedCount = 0;
        if (guardOn && guardNow.Clash && attackerCount > 1)
        {
            for (int k = 0; k < attackerCount; k++)
                if (Sweepable(world, assets, config, windows, attackers[k], tick, out _, out _))
                {
                    if (windowedCount == windowed.Length) GrowWindowed();
                    clashWith[windowedCount] = -1;
                    clashDone[windowedCount] = false;
                    windowed[windowedCount++] = attackers[k];
                }
            if (windowedCount > 1) PreClash(world, assets, config, windows, volumes, level, tick, nearCount);
        }
        for (int k = 0; k < attackerCount; k++)
        {
            var a = attackers[k];
            ref var c = ref fighters.Get(a);
            ref var anim = ref anims.Get(a);
            ref var sc = ref swings.Get(a);
            if (!Sweepable(world, assets, config, windows, a, tick, out var blade0, out var blade1)) continue;
            // A clash the pre-pass found, published and stopped for both at once (the pair's first in the stores' order does it).
            int wi = WindowIndex(a);
            if (wi >= 0 && clashWith[wi] >= 0)
            {
                if (!clashDone[wi]) Clash(world, assets, config, struck, wi, clashWith[wi], tick, ref serial);
                continue;
            }
            var window = windows.For(assets, anim.Kind, anim.ActionClip, c.StrikeAt, config.Window);
            ref var ab = ref bodies.Get(a);
            bool continued = stop.Enabled && sc.Chain > 0;
            int n = Sweep(world, assets, config, volumes, level, a, ref anim, in sc, ab.Feet, blade0, blade1, nearCount, clash: false);
            bool met = false;
            float stoppedAt = 1f;   // where in the tick the blade stopped, for the swing's trail
            for (int h = 0; h < hitCount; h++)
            {
                ref var hit = ref hits[h];
                if (hit.Met != ContactMet.Level)
                {
                    bool known = sc.Has(hit.Target);
                    if (known && !continued) { sc.Duplicates++; continue; }
                    if (!known)
                    {
                        if (sc.Met >= SwingContacts.MaxTargets) continue;
                        sc.Targets[sc.Met++] = hit.Target;
                    }
                }
                met = true;
                // With blades stopping, each contact's place in the swing's chain; without, the first rule's contacts each a first.
                int chain = stop.Enabled ? sc.Chain++ : 0;
                var made = Publish(world, assets, config, struck, a, ref c, ref anim, ref sc, in hit, subBlades[hit.Sub], subBlades[hit.Sub + 1], n, tick, tick * 1000 + serial++,
                    window.PeakSpeed * MathF.Abs(anim.ActionRate), chain);
                if (stop.Enabled)
                {
                    // A body bites — the blade held there while the rules decide (FollowThroughSystem);
                    // a shield, a parrying blade or the level makes it spring back. Nothing beyond it is met this tick.
                    // A guard with the rules deciding holds the blade there for their verdict (GuardResolved), the same tick.
                    if (hit.Met == ContactMet.Body) FollowThrough.Bite(world, a, ref anim, sc.Swing, hit.Time, in made, hit.Region);
                    else if (hit.Guard != GuardMeet.None && guardNow.RulesDecide) FollowThrough.Guarded(world, a, ref anim, sc.Swing, hit.Time, in made);
                    else
                    {
                        sc.Stopped = true;
                        FollowThrough.Rebound(world, a, ref anim, ref c, stop, hit.Time, in made);
                    }
                    stoppedAt = hit.Time;
                    break;
                }
                if (hit.Met != ContactMet.Body)
                {
                    sc.Stopped = true;
                    CombatServices.CutShort(ref anim, hit.Time);
                    stoppedAt = hit.Time;
                    break;
                }
            }
            if (!met) sc.Duplicates += curOverlaps;   // a going-on blade still in a part it did not cut (none without the stop)
            // The blades swept this tick, as swept, to the swing's trail (presentation only; not digested).
            trails?.Record(a, sc.Swing, tick, subBlades.AsSpan(0, n + 1), stoppedAt);
        }

        // 4. Keep everyone near: their striking segment, their shield, their volumes, for the next tick's sweep.
        for (int i = 0; i < nearCount; i++)
        {
            var e = nearList[i];
            if (e.Index >= kept.Length) Array.Resize(ref kept, Math.Max(e.Index + 1, Math.Max(16, kept.Length * 2)));
            ref var m = ref kept[e.Index];
            m.Of = e;
            m.Tick = tick;
            m.HasBlade = Striking(world, e, config.HandRadius, out m.Blade) ? 1 : 0;
            m.HasShield = HeldItems.Shield(world, e, out m.Shield) ? 1 : 0;
            volumes?.Keep(world, e);
        }
    }

    /// <summary>
    /// Whether <paramref name="a"/>'s swing is swept this tick (its record open and not stopped, its blade not held by a stop,
    /// its clip's time in its window, its striking segment found now and kept last tick): its segment then and now.
    /// </summary>
    bool Sweepable(World world, CharacterAssets assets, ContactConfig config, ActiveWindows windows, EntityId a, long tick, out BladePlacement blade0, out BladePlacement blade1)
    {
        blade0 = blade1 = default;
        ref var c = ref world.Store<Combatant>().Get(a);
        ref var anim = ref world.Store<CharacterAnimation>().Get(a);
        ref var sc = ref world.Store<SwingContacts>().Get(a);
        if (sc.Stopped) return false;
        if (config.Stop.Enabled && FollowThrough.Held(world, a, c.Swing)) return false;   // a bitten, rebounding or returning blade meets nothing new
        var window = windows.For(assets, anim.Kind, anim.ActionClip, c.StrikeAt, config.Window);
        if (!window.Overlaps(anim.ActionLast, anim.ActionTime)) return false;
        if (!Striking(world, a, config.HandRadius, out blade1)) return false;
        if (!KeptOn(a, tick - 1, out var was) || was.HasBlade == 0) return false;
        blade0 = was.Blade;
        return !Unsafe.IsNullRef(ref world.Store<Body>().Find(a));
    }

    /// <summary>
    /// Sweeps <paramref name="a"/>'s blade from <paramref name="blade0"/> to <paramref name="blade1"/> against everyone near and
    /// the level (in sub-steps for a fast swing), the hits sorted in the order met; answers the sub-steps. with
    /// <paramref name="clash"/>, other swinging blades are swept too (the clash's candidates).
    /// </summary>
    int Sweep(World world, CharacterAssets assets, ContactConfig config, HitVolumes? volumes, CollisionWorld level, EntityId a, ref CharacterAnimation anim,
        in SwingContacts sc, Vector3 feet, BladePlacement blade0, BladePlacement blade1, int nearCount, bool clash)
    {
        hitCount = 0;
        clashPass = clash;
        // A fast swing swept in sub-steps of its clip's time (the class's remarks); else the tick whole, as before.
        int n = SubSteps(world, a);
        subBlades[0] = blade0;
        subBlades[n] = blade1;
        for (int sIx = 1; sIx < n; sIx++)
            if (!BladeAt(world, assets, config, a, in anim, anim.ActionLast + (anim.ActionTime - anim.ActionLast) * sIx / n, out subBlades[sIx]))
            {
                n = 1;
                subBlades[1] = blade1;
                break;
            }
        // A swing that went on past a part it cut off may meet the same people again, but only where it newly enters.
        bool continued = config.Stop.Enabled && sc.Chain > 0;
        (curRecord, curContinued, curOverlaps) = (sc, continued, 0);
        for (int sIx = 0; sIx < n; sIx++)
        {
            (curSub, curN) = (sIx, n);
            (curF0, curF1) = n == 1 ? (0f, 1f) : (sIx / (float)n, (sIx + 1) / (float)n);
            Collect(world, assets, config, volumes, level, a, feet, subBlades[sIx], subBlades[sIx + 1], nearCount);
        }
        (curSub, curN, curF0, curF1) = (0, 1, 0f, 1f);
        clashPass = false;
        Sort();
        return n;
    }

    int WindowIndex(EntityId e)
    {
        for (int i = 0; i < windowedCount; i++) if (windowed[i] == e) return i;
        return -1;
    }

    void GrowWindowed()
    {
        int m = windowed.Length * 2;
        Array.Resize(ref windowed, m); Array.Resize(ref firstHit, m); Array.Resize(ref firstB0, m); Array.Resize(ref firstB1, m);
        Array.Resize(ref firstN, m); Array.Resize(ref clashWith, m); Array.Resize(ref clashDone, m);
    }

    /// <summary>
    /// The clash's pre-pass — each blade in its window swept with the others' swinging blades as candidates; its first
    /// meeting kept (what it would meet first, a repeat of someone it met already passed over as the main pass does). Two blades
    /// whose first meetings are each other clash (<see cref="clashWith"/>); any other pair does not, and the main pass sweeps
    /// without swinging blades (a blade that bit or rebounded first is not where the other passes).
    /// </summary>
    void PreClash(World world, CharacterAssets assets, ContactConfig config, ActiveWindows windows, HitVolumes? volumes, CollisionWorld level, long tick, int nearCount)
    {
        var swings = world.Store<SwingContacts>();
        for (int i = 0; i < windowedCount; i++)
        {
            var a = windowed[i];
            clashWith[i] = -1;
            clashDone[i] = false;
            firstHit[i] = new Hit { Time = 2f };
            Sweepable(world, assets, config, windows, a, tick, out var b0, out var b1);
            ref var sc = ref swings.Get(a);
            bool continued = config.Stop.Enabled && sc.Chain > 0;
            int n = Sweep(world, assets, config, volumes, level, a, ref world.Store<CharacterAnimation>().Get(a), in sc, world.Store<Body>().Get(a).Feet, b0, b1, nearCount, clash: true);
            for (int h = 0; h < hitCount; h++)
            {
                if (hits[h].Met != ContactMet.Level && sc.Has(hits[h].Target) && !continued) continue;
                firstHit[i] = hits[h];
                firstB0[i] = subBlades[hits[h].Sub];
                firstB1[i] = subBlades[hits[h].Sub + 1];
                firstN[i] = n;
                break;
            }
        }
        for (int i = 0; i < windowedCount; i++)
        {
            if (firstHit[i].Guard != GuardMeet.Clash) continue;
            int j = WindowIndex(firstHit[i].Target);
            if (j >= 0 && firstHit[j].Guard == GuardMeet.Clash && firstHit[j].Target == windowed[i]) clashWith[i] = j;
        }
    }

    /// <summary>
    /// A clash — one paired transaction: both contacts published together, the pair's lower entity
    /// first (so the same pair gives the same order whichever the stores hold first), at the earlier of the two meeting times,
    /// and both blades stopped in the same call: held for the rules' verdict when they decide (both stop, one dominates, both
    /// deflect), else both sprung back at once. Neither person is harmed (a weapon contact is no body's).
    /// </summary>
    void Clash(World world, CharacterAssets assets, ContactConfig config, StruckVolumes? struck, int i, int j, long tick, ref int serial)
    {
        clashDone[i] = clashDone[j] = true;
        if (windowed[j].Index < windowed[i].Index) (i, j) = (j, i);
        float time = MathF.Min(firstHit[i].Time, firstHit[j].Time);
        var windows = world.Service<ActiveWindows>();
        Span<int> pair = [i, j];
        var made = pairMade;
        for (int p = 0; p < 2; p++)
        {
            int x = pair[p];
            var a = windowed[x];
            ref var c = ref world.Store<Combatant>().Get(a);
            ref var anim = ref world.Store<CharacterAnimation>().Get(a);
            ref var sc = ref world.Store<SwingContacts>().Get(a);
            var hit = firstHit[x];
            hit.Time = time;
            if (!sc.Has(hit.Target) && sc.Met < SwingContacts.MaxTargets) sc.Targets[sc.Met++] = hit.Target;
            int chain = config.Stop.Enabled ? sc.Chain++ : 0;
            var window = windows.For(assets, anim.Kind, anim.ActionClip, c.StrikeAt, config.Window);
            made[p] = Publish(world, assets, config, struck, a, ref c, ref anim, ref sc, in hit, firstB0[x], firstB1[x], firstN[x], tick, tick * 1000 + serial++,
                window.PeakSpeed * MathF.Abs(anim.ActionRate), chain);
        }
        for (int p = 0; p < 2; p++)
        {
            var a = windowed[pair[p]];
            ref var c = ref world.Store<Combatant>().Get(a);
            ref var anim = ref world.Store<CharacterAnimation>().Get(a);
            ref var sc = ref world.Store<SwingContacts>().Get(a);
            if (config.Stop.Enabled && guardNow.RulesDecide) FollowThrough.Guarded(world, a, ref anim, sc.Swing, time, in made[p]);
            else
            {
                sc.Stopped = true;
                if (config.Stop.Enabled) FollowThrough.Rebound(world, a, ref anim, ref c, config.Stop, time, in made[p]);
                else CombatServices.CutShort(ref anim, time);
            }
        }
    }

    /// <summary>
    /// The viewer's aim as contact sees it as a blow begins, for the swing's trail — the view's ray (from the
    /// camera) and the reticle's point, both as the last tick's Post phase left them (what the attack's aim was taken from);
    /// none for anyone else.
    /// </summary>
    static Presentation.SwingTrails.Aim TrailAim(Cameras.ViewState? view, Cameras.ReticleState? reticle, EntityId a)
    {
        if (view is null || !view.HasView || view.Of != a) return Presentation.SwingTrails.Aim.None;
        var answer = reticle?.Answer ?? Presentation.ReticleAnswer.Nothing;
        return new Presentation.SwingTrails.Aim(true, view.View.Eye, view.View.Forward, answer.Kind != Presentation.ReticleTarget.Nothing, answer.Point);
    }

    bool KeptOn(EntityId e, long tick, out Kept k)
    {
        k = default;
        if ((uint)e.Index >= (uint)kept.Length) return false;
        k = kept[e.Index];
        return k.Of == e && k.Tick == tick;
    }

    /// <summary>A fighter's striking segment now: its first blade, or bare-handed a ball of <paramref name="handRadius"/> about its weapon hand.</summary>
    public static bool Striking(World world, EntityId e, float handRadius, out BladePlacement segment)
    {
        if (HeldItems.Blade(world, e, out segment)) return true;
        if (HeldItems.Point(world, e, out var p)) { segment = new BladePlacement(p, p, handRadius); return true; }
        return false;
    }

    void Collect(World world, CharacterAssets assets, ContactConfig config, HitVolumes? volumes, CollisionWorld level, EntityId a, Vector3 feet,
        BladePlacement b0, BladePlacement b1, int nearCount)
    {
        float r = b1.Radius;
        var fighters = world.Store<Combatant>();
        var bodies = world.Store<Body>();
        for (int i = 0; i < nearCount; i++)
        {
            var t = nearList[i];
            if (t == a) continue;
            ref var tb = ref bodies.Find(t);
            if (Unsafe.IsNullRef(ref tb)) continue;
            float dx = tb.Feet.X - feet.X, dy = tb.Feet.Y - feet.Y, reach = config.ReachAtMost + 1.5f;
            if (dx * dx + dy * dy > reach * reach) continue;
            bool known = KeptOn(t, world.Tick - 1, out var was);

            ref var tf = ref fighters.Find(t);
            // A guard the swing pushed through is met no more by it; a guard raised (on the channel or the shield arm's
            // layer) is a Block for the rules, one merely in the way is an incidental interception.
            bool through = guardOn && curRecord.Through == t;
            bool raised = !Unsafe.IsNullRef(ref tf) && tf.Blocking;
            // Their shield, from where it was to where it is.
            if (!through && HeldItems.Shield(world, t, out var s1))
            {
                var box1 = Box(s1);
                var box0 = known && was.HasShield != 0 ? Box(was.Shield) : box1;
                Share(ref box0, ref box1);
                if (ContactMath.Sweep(b0.Guard, b0.Tip, b1.Guard, b1.Tip, r, box0, box1, out var h) && !StillIn(t, h))
                    Add(new Hit { Time = h.Time, Met = ContactMet.Shield, Target = t, Point = h.Point, Normal = h.Normal, Volume = -1,
                        TargetVelocity = (box1.Centre - box0.Centre) * World.TicksPerSecond, Material = config.ShieldOf(assets, Kind(world, t)),
                        Guard = !guardOn ? GuardMeet.None : raised ? GuardMeet.Shield : GuardMeet.Incidental });
            }
            // Their blade, when it parries; in the clash's pre-pass, when it swings in its window too (a clash's candidate).
            bool swinging = guardOn && WindowIndex(t) >= 0;
            bool parries = config.Parry == ParryRule.Always || (config.Parry == ParryRule.WhileBlocking && raised && !swinging
                && (!guardOn || !ActionRoles.IsAttack(world.Get<CharacterAnimation>(t).Action)));
            bool clashes = clashPass && swinging;
            if (!through && (parries || clashes) && HeldItems.Blade(world, t, out var w1))
            {
                var c1 = new OrientedCapsule(w1.Guard, w1.Tip, w1.Radius);
                var c0 = known && was.HasBlade != 0 && was.Blade.Length > 1e-4f ? new OrientedCapsule(was.Blade.Guard, was.Blade.Tip, was.Blade.Radius) : c1;
                Share(ref c0, ref c1);
                if (ContactMath.Sweep(b0.Guard, b0.Tip, b1.Guard, b1.Tip, r, c0, c1, out var h) && !StillIn(t, h))
                    Add(new Hit { Time = h.Time, Met = ContactMet.Weapon, Target = t, Point = h.Point, Normal = h.Normal, Volume = -2,
                        TargetVelocity = VelocityOn(h.Point, c0, c1, h.Time), Material = config.WeaponOf(assets, Kind(world, t)).Material,
                        Guard = !guardOn ? GuardMeet.None : clashes ? GuardMeet.Clash : !raised ? GuardMeet.Incidental : tf.ParryLeft > 0f ? GuardMeet.Parry : GuardMeet.Blade });
            }
            // Their body: the broad capsule, then each volume, from last tick's to this tick's.
            if (volumes is null || !volumes.Broad(world, t, out var broad1)) continue;
            bool kept0 = volumes.Last(world, t, out var last, out var broad0);
            if (!kept0) broad0 = broad1;
            Share(ref broad0, ref broad1);
            if (!ContactMath.Sweep(b0.Guard, b0.Tip, b1.Guard, b1.Tip, r, broad0, broad1, out _)) continue;
            var now = volumes.Posed(world, t);
            bool same = kept0 && last.Length == now.Length;
            uint missing = volumes.Missing(world, t, out var collapse);   // a part cut off is met no more
            float best = 2f;
            bool stillIn = false;
            Hit found = default;
            for (int v = 0; v < now.Length; v++)
            {
                if (missing != 0 && collapse is not null && collapse.VolumeGone(missing, now[v].Region, now[v].Bone)) continue;
                var v1 = now[v].Capsule;
                var v0 = same ? last[v].Capsule : v1;
                Share(ref v0, ref v1);
                if (!ContactMath.Sweep(b0.Guard, b0.Tip, b1.Guard, b1.Tip, r, v0, v1, out var h) || h.Time >= best) continue;
                if (StillIn(t, h)) { stillIn = true; continue; }
                best = h.Time;
                found = new Hit { Time = h.Time, Met = ContactMet.Body, Target = t, Point = h.Point, Normal = h.Normal, Volume = v, Region = now[v].Region,
                    TargetVelocity = VelocityOn(h.Point, v0, v1, h.Time), Material = config.Body };
            }
            if (best <= 1f) Add(found);
            else if (stillIn && curSub == curN - 1) curOverlaps++;
        }

        // The level: the blade's tip and its middle.
        var mid0 = (b0.Guard + b0.Tip) * 0.5f;
        var mid1 = (b1.Guard + b1.Tip) * 0.5f;
        bool tipHit = ContactMath.Moving(b0.Tip, b1.Tip, 0f, level, out var lt, out int tt);
        bool midHit = ContactMath.Moving(mid0, mid1, 0f, level, out var lm, out int tm);
        if (tipHit || midHit)
        {
            bool tip = tipHit && (!midHit || lt.Time <= lm.Time);
            var h = tip ? lt : lm;
            Add(new Hit { Time = h.Time, Met = ContactMet.Level, Target = EntityId.None, Point = h.Point, Normal = h.Normal, Volume = -3,
                Material = config.LevelSurface(level, tip ? tt : tm) });
        }
    }

    static int Kind(World world, EntityId e) => world.Get<CharacterAnimation>(e).Kind;

    static OrientedBox Box(in BoxPlacement b) => new(b.Centre, b.AxisX, b.AxisY, b.AxisZ, b.HalfSize);

    /// <summary>The velocity (m/s) of the point of a capsule moving from <paramref name="c0"/> to <paramref name="c1"/> over the tick nearest <paramref name="p"/> at <paramref name="t"/>.</summary>
    static Vector3 VelocityOn(Vector3 p, in OrientedCapsule c0, in OrientedCapsule c1, float t)
    {
        ClosestOnSegment(p, Lerp(c0.A, c1.A, t), Lerp(c0.B, c1.B, t), out float u);
        return Lerp(c1.A - c0.A, c1.B - c0.B, u) * World.TicksPerSecond;
    }

    void Add(in Hit h)
    {
        if (hitCount >= MaxHits) return;
        // The time within the sub-step kept, the time in the whole tick for the order; the target's speed over the sub-step.
        var x = h;
        x.Sub = curSub;
        x.Local = h.Time;
        if (curN > 1)
        {
            x.Time = curF0 + h.Time * (curF1 - curF0);
            x.TargetVelocity = h.TargetVelocity * curN;
        }
        hits[hitCount++] = x;
    }

    /// <summary>A target's shapes at the start and end of the sub-step swept now (the tick's own when it is swept whole).</summary>
    void Share(ref OrientedCapsule c0, ref OrientedCapsule c1)
    {
        if (curN == 1) return;
        var (a0, b0, a1, b1) = (c0.A, c0.B, c1.A, c1.B);
        c0 = new OrientedCapsule(Lerp(a0, a1, curF0), Lerp(b0, b1, curF0), c0.Radius);
        c1 = new OrientedCapsule(Lerp(a0, a1, curF1), Lerp(b0, b1, curF1), c1.Radius);
    }

    void Share(ref OrientedBox x0, ref OrientedBox x1)
    {
        if (curN == 1) return;
        var (o0, o1) = (x0, x1);
        x0 = Mid(o0, o1, curF0);
        x1 = Mid(o0, o1, curF1);
    }

    static OrientedBox Mid(in OrientedBox a, in OrientedBox b, float t) =>
        new(Lerp(a.Centre, b.Centre, t), Vector3.Normalize(Lerp(a.AxisX, b.AxisX, t)), Vector3.Normalize(Lerp(a.AxisY, b.AxisY, t)),
            Vector3.Normalize(Lerp(a.AxisZ, b.AxisZ, t)), Lerp(a.Half, b.Half, t));

    /// <summary>How many sub-steps the attacker's swing is swept in this tick (1 up to <see cref="SubStepTempo"/>).</summary>
    int SubSteps(World world, EntityId a)
    {
        var tempos = PoseRigSystem.Optional<CombatTempo>(world, ref temposKnown);
        float rate = CombatTempos.Of(tempos, a).SwingRate;
        return rate <= SubStepTempo ? 1 : Math.Min(MaxSubSteps, (int)MathF.Ceiling(rate / SubStepTempo));
    }

    /// <summary>
    /// The attacker's striking segment at clip time <paramref name="clipTime"/> of its action (its pose evaluated so, its
    /// placement now): the first blade held, or bare-handed a ball about its weapon hand (as <see cref="Striking"/>).
    /// </summary>
    bool BladeAt(World world, CharacterAssets assets, ContactConfig config, EntityId a, in CharacterAnimation anim, float clipTime, out BladePlacement blade)
    {
        blade = default;
        if (!HeldItems.Placement(world, a, out var place)) return false;
        int bones = assets.ModelOf(anim.Kind).Skeleton.Count;
        if (subModel.Length < bones) { subModel = new Matrix4x4[bones]; subSkin = new Matrix4x4[bones]; }
        var state = anim;
        state.ActionTime = clipTime;
        var rigs = PoseRigSystem.Optional<PoseRig>(world, ref rigsKnown);
        if (rigs is not null && rigs.Has(a)) subPoser.Evaluate(assets, state, rigs.Get(a), subModel.AsSpan(0, bones), subSkin.AsSpan(0, bones));
        else subPoser.Evaluate(assets, state, subModel.AsSpan(0, bones), subSkin.AsSpan(0, bones));
        var held = HeldItems.Of(world, a);
        for (int m = 0; m < held.Count; m++)
            for (int t = 0; t < held[m].Things.Count; t++)
            {
                var thing = held[m].Things[t];
                if (thing.Kind != HeldKind.Blade || (uint)thing.Bone >= (uint)bones) continue;
                var mtx = PoseMath.Multiply(subModel[thing.Bone], place);
                float scale = LimbIK.Length(PoseMath.TransformNormal(Vector3.UnitX, mtx));
                blade = new BladePlacement(PoseMath.Transform(thing.Guard, mtx), PoseMath.Transform(thing.Tip, mtx), thing.Radius * scale);
                return true;
            }
        if (assets.Kind(anim.Kind).Actions is not { WeaponHand: >= 0 } set || set.WeaponHand >= bones) return false;
        var hand = PoseMath.Multiply(subModel[set.WeaponHand], place).Translation;
        blade = new BladePlacement(hand, hand, config.HandRadius);
        return true;
    }

    /// <summary>The hits in the order they were met: by time, and at one time a shield, a blade, the level, then a body.</summary>
    void Sort()
    {
        for (int i = 1; i < hitCount; i++)
        {
            var h = hits[i];
            int j = i - 1;
            while (j >= 0 && Before(h, hits[j])) { hits[j + 1] = hits[j]; j--; }
            hits[j + 1] = h;
        }
    }

    static bool Before(in Hit x, in Hit y) => x.Time < y.Time || (x.Time == y.Time && Rank(x.Met) < Rank(y.Met));

    static int Rank(ContactMet m) => m switch { ContactMet.Shield => 0, ContactMet.Weapon => 1, ContactMet.Level => 2, _ => 3 };

    ContactMade Publish(World world, CharacterAssets assets, ContactConfig config, StruckVolumes? struck, EntityId a, ref Combatant c, ref CharacterAnimation anim,
        ref SwingContacts sc, in Hit hit, BladePlacement b0, BladePlacement b1, int subSteps, long tick, long id, float peakSpeed, int chain)
    {
        // The striking point's velocity: the blade's point nearest the contact, its two ends' moves mixed by where it is along the blade.
        // B0 and b1 are the sub-step's (the tick's when it was swept whole), its time hit.Local, its length 1/subSteps of a tick.
        var g = Lerp(b0.Guard, b1.Guard, hit.Local);
        var tip = Lerp(b0.Tip, b1.Tip, hit.Local);
        ClosestOnSegment(hit.Point, g, tip, out float s);
        var v = Lerp(b1.Guard - b0.Guard, b1.Tip - b0.Tip, s) * (float)(World.TicksPerSecond * subSteps) - hit.TargetVelocity;
        float speed = MathF.Max(0f, -Dot(v, hit.Normal));
        // The blow's quality, measured (gate-08 is open: nothing reads these for harm yet).
        float pointSpeed = v.Length();
        float along = (b1.Tip - b1.Guard).LengthSquared() > 1e-8f ? s : 0f;
        float tipSpeed = (b1.Tip - b0.Tip).Length() * (float)(World.TicksPerSecond * subSteps);
        var matter = config.WeaponOf(assets, anim.Kind);
        var t = hit.Target;
        bool person = !t.IsNone;
        bool protectedPerson = person && ProtectedPeople.Is(world, t);
        string? site = hit.Met == ContactMet.Body ? BodyRegions.Name(hit.Region) : null;
        var made = new ContactMade(id, a, sc.Swing, t, hit.Met, site, hit.Material, tick, hit.Point, hit.Normal, speed, matter.MassKg * speed,
            PreContactBlocked: false, HitCoverOrShield: hit.Met is ContactMet.Shield or ContactMet.Level, LineOfFire: true, Protected: protectedPerson,
            PointSpeed: pointSpeed, AlongBlade: along, TipSpeed: tipSpeed, PeakSpeed: peakSpeed, Chain: chain, Guard: hit.Guard);
        world.Events<ContactMade>().Publish(made);
        struck?.Add(t, hit.Volume, hit.Met, tick, hit.Point);
        if (!person) return made;

        // What it does to the body: a hurt, unless protected, dead or dying (harm, and death, are the rules').
        // With rules that resolve harm, the flinch waits for the harm (HurtSystem).
        if (hit.Met == ContactMet.Body && !protectedPerson && (!world.TryService<HurtRule>(out var hurtRule) || hurtRule.OnContact))
        {
            ref var ta = ref world.Store<CharacterAnimation>().Find(t);
            ref var intent = ref world.Store<CombatIntent>().Find(t);
            if (!Unsafe.IsNullRef(ref ta) && !ActionRoles.IsDeath(ta.Action) && !Unsafe.IsNullRef(ref intent) && intent.Play == ActionRole.None)
                intent.Play = ActionRole.Hurt;
        }
        if (protectedPerson) world.Events<ProtectedProtest>().Publish(new ProtectedProtest(t, a, tick, id));

        // Violence seen: a blow that met a person in a public place, for the one who struck.
        ref var ab = ref world.Store<Body>().Find(a);
        if (world.TryService<EventQueue<MasqueradeRisk>>(out var risks) && !Unsafe.IsNullRef(ref ab) && world.Service<LevelSpace>().Places.At(ab.Feet) == PlaceKind.Public)
            risks.Publish(new MasqueradeRisk(a, tick, MasqueradeAct.Violence, TraversalKind.None, ab.Feet, speed, 0f));

        // The catch lets go when a body is struck by someone else (at first the player's; any body's: a vampire
        // attacked is unbound, its powers' movement no longer held to a human's).
        MasqueradeCatch.LetGo(world, t, a, id);
        return made;
    }

    static void Close(World world, EntityId e, ref SwingContacts sc, long tick)
    {
        if (sc.Duplicates > 0) world.Events<ContactDuplicates>().Publish(new ContactDuplicates(e, sc.Swing, tick, sc.Duplicates, sc.Met));
        world.Events<SwingEnded>().Publish(new SwingEnded(e, sc.Swing, tick, sc.Met, sc.Duplicates, sc.Stopped));
        sc.Open = false;
    }
}

/// <summary>Contact registered in one call, and its preparation at load.</summary>
public static class ContactServices
{
    /// <summary>Contact's place: after the strike's moment (<see cref="CombatServices.StrikeOrder"/>, 50).</summary>
    public const int ContactOrder = 60;

    /// <summary>
    /// Registers the components <see cref="SwingContacts"/> and <see cref="ProtectedPerson"/>; the events
    /// <see cref="ContactMade"/>, <see cref="ContactDuplicates"/>, <see cref="ProtectedProtest"/> and <see cref="CatchLetGo"/>;
    /// the services <see cref="ContactConfig"/> (for the modules to fill), <see cref="ActiveWindows"/> and
    /// <see cref="StruckVolumes"/> (and <see cref="Presentation.SwingTrails"/>, the swing's trail, filled from the blades
    /// swept); and the system <c>Contact/60 platform.contact</c>. After the animation, the hit volumes,
    /// the masquerade and the fighting (<see cref="CombatServices.Register"/>); before the modules.
    /// </summary>
    public static WorldBuilder Register(WorldBuilder b, ContactConfig? config = null)
    {
        b.Component<SwingContacts>().Component<ProtectedPerson>();
        AnatomyServices.Register(b);   // what a body is missing (the rules write it, contact and the pose read it)
        // ContactMade is the contract's (CombatEvents.cs): registered here unless whoever registered first (harm, the arena) did.
        if (!b.Peek.TryService<EventQueue<ContactMade>>(out _)) b.Event<ContactMade>();
        b.Event<ContactDuplicates>().Event<ProtectedProtest>().Event<CatchLetGo>().Event<SwingEnded>();
        b.Service(config ?? new ContactConfig()).Service(new ActiveWindows()).Service(new StruckVolumes());
        b.Service(new Presentation.SwingTrails());   // the swing's trail, presentation only
        b.System(Phase.Contact, ContactOrder, "platform.contact", new ContactSystem());
        FollowThroughServices.Register(b);   // the blade stops where it stops (Resolve/950)
        return b;
    }

    /// <summary>At load, after the modules have filled the config: every kind's weapon, shield and protection resolved, the level's surfaces named, every attack clip's window worked out, and every model's collapse of lost parts made.</summary>
    public static void Prepare(World world)
    {
        if (!world.TryService<ContactConfig>(out var config)) return;
        world.TryService<CharacterAssets>(out var assets);
        world.TryService<LevelSpace>(out var level);
        config.Prepare(assets, level);
        if (assets is not null) world.Service<ActiveWindows>().Prepare(assets, config.Window);
        // Each model's collapse of lost parts made now, so no tick makes one when a part is first cut off.
        if (assets is not null && world.TryService<CharacterPoses>(out var poses))
            for (int m = 0; m < assets.ModelCount; m++) poses.Collapse(m);
    }
}
