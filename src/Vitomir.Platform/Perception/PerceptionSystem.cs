// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Perception;

/// <summary>
/// Who sees (ours, the Perceive phase, after the Masquerade's risk check): each tick,
/// <list type="number">
/// <item>this tick's risks to the Masquerade are put in view (<see cref="PerceptionState.Keep"/>), and the acts in view
/// age — an act stays in view <see cref="PerceptionNumbers.ActSeconds"/> at least, a jump while its body is in the air,
/// a speed while it is over a human's, a move while it lasts;</item>
/// <item>each observer looks every <see cref="PerceptionNumbers.LookSeconds"/> or a little more (staggered by entity, so
/// not all on one tick): for each act in view it finds how clearly it sees the body that did it
/// (<see cref="Clarity"/>: within its cone and its sight — its range × its eyes × the light at the body as its eyes take
/// it × the body's concealment × its motion —, nearer clearer, times the share of the body past cover) and gains
/// awareness for the time since its last look: <see cref="PerceptionNumbers.GainPerSecond"/> × the clarity × the act's
/// size (its measure over a human's limit) × its strangeness × the observer's attention × (½ + its excitability) per
/// second, no higher than the act's highest rung allows. Aware or more, it follows the one it is wary of while that body
/// is in clear sight. The evidence holds while it sees anything (an act, or the one it is wary of) and for its kind's
/// <see cref="ObserverNumbers.HoldSeconds"/> after, then cools from the ceiling to nothing over its
/// <see cref="ObserverNumbers.CoolDownSeconds"/> ("a cool-down runs while you are not seen");</item>
/// <item>its rung of the ladder climbs one step at a time (making out what it saw takes time): each
/// step waits its kind's time on the rung below (<see cref="ObserverNumbers.NoticeSeconds"/>,
/// <see cref="ObserverNumbers.MakeOutSeconds"/>, <see cref="ObserverNumbers.ConcludeSeconds"/>) while the evidence is at
/// or over the next rung's threshold (the world's, lowered by its area's heat) — and goes on after the act has left
/// view, since what was seen is remembered; it steps down one rung at a time once the evidence is under its rung's
/// threshold and the rung has been kept <see cref="PerceptionNumbers.MinRungSeconds"/>. Each change is raised
/// (<see cref="AwarenessChanged"/>); an alarm heats the area of the act, and an alarmed observer with a voice shouts
/// once (a townsman for help, a keeper of the peace to rouse others);</item>
/// <item>each shout (<see cref="ShoutRaised"/>) is heard by every other observer (but the one it is about) within its voice ×
/// <see cref="PerceptionNumbers.ShoutRadius"/>: one less than alerted becomes alerted about the shout's place (halfway to
/// alarmed) at once — it was told;</item>
/// <item>Each observer <b>struck</b> this tick (a <see cref="ContactMade"/> on its body or its shield, by someone else) is
/// alarmed at once about the one who struck (<see cref="AwarenessCause.Struck"/>: no wait, it was not a sight to make out), and
/// marked (<see cref="Observer.Struck"/>) for its reactions to answer;</item>
/// <item>each area's heat cools over <see cref="PerceptionNumbers.HeatCoolSeconds"/>, <see cref="PerceptionNumbers.HeatAwayFactor"/>
/// times faster while the player is not in it (<see cref="AreaHeatRose"/>, <see cref="AreaHeatGone"/>).</item>
/// </list>
/// Deterministic (the store's order, no random), allocating nothing per tick.
/// </summary>
public sealed class PerceptionSystem : ISystem
{
    /// <summary>The longest an act stays in view, whatever it is (s).</summary>
    public const float MaxActSeconds = 10f;

    /// <summary>The points of a body looked at, as shares of its height (crouched, its crouched height): head, chest, hips, knees.</summary>
    public static readonly float[] BodyPoints = [0.92f, 0.72f, 0.5f, 0.25f];

    /// <summary>The most shouts heard in one tick.</summary>
    const int MaxShouts = 16;

    readonly (EntityId Who, Vector3 Where, Vector3 About, EntityId Suspect, float Radius, MasqueradeAct Act, TraversalKind Move)[] shouts =
        new (EntityId, Vector3, Vector3, EntityId, float, MasqueradeAct, TraversalKind)[MaxShouts];
    int shoutCount;
    Store<Traversal>? traversals;
    Store<Controlled>? controlled;
    bool looked;

    /// <summary>How many ticks apart observer <paramref name="e"/> looks: <see cref="PerceptionNumbers.LookSeconds"/>, and a sixth, a third or a half more by its slot.</summary>
    public static int LookTicks(EntityId e, in PerceptionNumbers n)
    {
        int p = Math.Max(1, (int)MathF.Round(n.LookSeconds * World.TicksPerSecond));
        return p + p * (e.Index % 3) / 3;
    }

    /// <summary>Whether observer <paramref name="e"/> looks on <paramref name="tick"/>.</summary>
    public static bool Looks(EntityId e, long tick, in PerceptionNumbers n)
    {
        int p = LookTicks(e, n);
        return (tick + (long)e.Index * 7) % p == 0;
    }

    public void Run(World world)
    {
        var config = world.Service<PerceptionConfig>();
        if (config.Kinds.Count == 0) return;
        if (!looked)
        {
            looked = true;
            foreach (var s in world.Stores) if (s is Store<Traversal> t) traversals = t;
            foreach (var s in world.Stores) if (s is Store<Controlled> c) controlled = c;
        }
        var state = world.Service<PerceptionState>();
        var n = config.Numbers;
        var level = world.Service<LevelSpace>().Collision;
        var bodies = world.Store<Body>();
        var locos = world.Store<Locomotion>();
        const float dt = World.TickSeconds;

        // 1. This tick's risks into view, then the acts in view aged.
        IReadOnlyList<MasqueradeRisk> risks = world.TryService<EventQueue<MasqueradeRisk>>(out var queue) ? queue.ThisTick : [];
        for (int i = 0; i < risks.Count; i++)
        {
            var r = risks[i];
            float size = r.HumanLimit > 1e-4f && float.IsFinite(r.HumanLimit) ? Math.Clamp(r.Measured / r.HumanLimit, 1f, n.BiggestAct) : n.BiggestAct;
            state.Keep(new ActInView { Actor = r.Entity, Act = r.Act, Move = r.Move, Size = size, Limit = r.HumanLimit, Raised = r.Tick });
        }
        var acts = state.ActsToChange;
        for (int i = acts.Length - 1; i >= 0; i--)
        {
            ref var a = ref acts[i];
            a.Seconds += dt;
            if (!bodies.Has(a.Actor) || !Lasts(a, bodies.Get(a.Actor), n)) state.Drop(i);
        }

        // 2-3. Each observer: what it sees when it looks, its value, its rung.
        shoutCount = 0;
        var observers = world.Store<Observer>();
        var oents = observers.Entities;
        var odata = observers.Data;
        for (int i = 0; i < oents.Length; i++)
        {
            var e = oents[i];
            ref var o = ref odata[i];
            if (!bodies.Has(e) || !locos.Has(e) || o.Kind < 0 || o.Kind >= config.Kinds.Count) continue;
            ref readonly var body = ref bodies.Get(e);
            ref readonly var loco = ref locos.Get(e);
            var kind = config.Kinds[o.Kind].Numbers;
            o.Area = state.Areas.At(body.Feet);
            float wary = 1f - n.HeatWariness * state.Heat[o.Area];
            o.SuspectUnseen += dt;

            if (Looks(e, world.Tick, n))
            {
                float since = LookTicks(e, n) * dt;
                var eyes = body.Feet + new Vector3(0, 0, loco.EyeHeight);
                var (sin, cos) = DetMath.SinCos(body.Yaw);
                var facing = new Vector3(cos, sin, 0);
                float value = o.Value;
                bool saw = false;
                var inView = state.Acts;
                for (int k = 0; k < inView.Length; k++)
                {
                    ref readonly var a = ref inView[k];
                    if (a.Actor == e) continue;
                    float clarity = Clarity(config, state.Areas, level, bodies, locos, o.Kind, eyes, facing, a.Actor, e);
                    if (clarity <= 0f) continue;
                    var response = config.Act(a.Act);
                    float gain = n.GainPerSecond * clarity * a.Size * response.Strangeness * kind.Attention * (0.5f + kind.Excitability) * since;
                    float cap = response.Highest switch
                    {
                        Awareness.Alarmed => n.Ceiling,
                        Awareness.Alerted => n.AlarmedAt * wary - 0.01f,
                        Awareness.Aware => n.AlertedAt * wary - 0.01f,
                        _ => 0f,
                    };
                    value = MathF.Max(value, MathF.Min(MathF.Min(value + gain, cap), n.Ceiling));
                    saw = true;
                    ref readonly var actor = ref bodies.Get(a.Actor);
                    o.Place = actor.Feet; o.Act = a.Act; o.Move = a.Move;
                    o.Suspect = a.Actor; o.SuspectPlace = actor.Feet; o.SuspectUnseen = 0f;
                    o.SawActTick = (int)a.Raised;     // a keeper stood down joins no fight over an act raised before
                }
                o.Value = value;
                // The one it is wary of is followed while it is in clear sight, act or none (a guard runs to where it was
                // last seen); while it keeps them in sight (seen within LostSeconds) it does not lose them for standing still.
                bool watched = !saw && o.Level >= Awareness.Aware && !o.Suspect.IsNone && bodies.Has(o.Suspect) && locos.Has(o.Suspect)
                    && Clarity(config, state.Areas, level, bodies, locos, o.Kind, eyes, facing, o.Suspect, e, o.SuspectUnseen < ReactionSystem.LostSeconds) > 0f;
                if (watched)
                {
                    o.SuspectPlace = bodies.Get(o.Suspect).Feet;
                    o.SuspectUnseen = 0f;
                }
                o.Seeing = saw || watched ? 1 : 0;
            }
            // The evidence holds while it sees and for its kind's hold after the last sight of anything, then cools.
            if (o.Seeing != 0) o.Unseen = 0f;
            else if ((o.Unseen += dt) > kind.HoldSeconds)
                o.Value = MathF.Max(0f, o.Value - n.Ceiling / MathF.Max(kind.CoolDownSeconds, dt) * dt);

            // The rung: one step up after its kind's wait on this rung with the evidence at or over the next threshold (what
            // was seen is still being taken in after the act has gone); one step down once the evidence is under this rung's
            // threshold and the rung has been kept its least time.
            var to = o.Level;
            o.OnRung += dt;
            if (o.Level < Awareness.Alarmed && o.Value >= n.Threshold(o.Level + 1, wary))
            {
                o.Climb += dt;
                if (o.Climb >= kind.StepSeconds(o.Level) - dt * 0.5f) to = o.Level + 1;
            }
            else
            {
                o.Climb = 0f;
                if (o.Level > Awareness.Unaware && o.Value < n.Threshold(o.Level, wary) && o.OnRung >= n.MinRungSeconds - dt * 0.5f) to = o.Level - 1;
            }
            if (to != o.Level) Change(world, state, config, ref o, e, body.Feet, to, to > o.Level ? AwarenessCause.Seen : AwarenessCause.Cooled);
        }

        // Struck — this tick's blows that met an observer's body or shield, by someone else: alarmed at once about the
        // one who struck (the area's heat and a shout, as any alarm), and marked for its reactions (fight back or flee).
        if (world.TryService<EventQueue<ContactMade>>(out var contacts))
        {
            var made = contacts.ThisTick;
            for (int k = 0; k < made.Count; k++)
            {
                var c = made[k];
                if (c.Met is not (ContactMet.Body or ContactMet.Shield) || c.Target == c.Attacker || !bodies.Has(c.Target) || !bodies.Has(c.Attacker)) continue;
                ref var o = ref observers.Find(c.Target);
                if (Unsafe.IsNullRef(ref o) || o.Kind < 0 || o.Kind >= config.Kinds.Count) continue;
                var at = bodies.Get(c.Attacker).Feet;
                o.Value = MathF.Max(o.Value, n.Ceiling);
                o.Unseen = 0f;
                o.Place = at; o.Act = MasqueradeAct.Violence; o.Move = TraversalKind.None;
                o.Suspect = c.Attacker; o.SuspectPlace = at; o.SuspectUnseen = 0f;
                o.SawActTick = (int)c.Tick;
                o.Struck = 1;
                o.StruckBy = c.Attacker;
                if (o.Level != Awareness.Alarmed) Change(world, state, config, ref o, c.Target, bodies.Get(c.Target).Feet, Awareness.Alarmed, AwarenessCause.Struck);
            }
        }

        // 4. The shouts, each heard once by every other observer within reach.
        for (int s = 0; s < shoutCount; s++)
        {
            var (who, where, about, suspect, radius, act, move) = shouts[s];
            int heard = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1) world.Events<ShoutRaised>().Publish(new ShoutRaised(who, world.Tick, where, about, radius, heard));
                for (int i = 0; i < oents.Length; i++)
                {
                    var e = oents[i];
                    ref var o = ref odata[i];
                    // The one the shout is about does not take it as news (a townsman struck by the watchman shouts; the watchman does not go to look at himself).
                    if (e == who || e == suspect || !bodies.Has(e) || o.Level >= Awareness.Alerted || o.Kind < 0 || o.Kind >= config.Kinds.Count) continue;
                    ref readonly var body = ref bodies.Get(e);
                    if (Vector3.DistanceSquared(body.Feet, where) > radius * radius) continue;
                    if (pass == 0) { heard++; continue; }
                    float wary = 1f - n.HeatWariness * state.Heat[o.Area];
                    // Told, not seen: straight on alerted, no wait.
                    o.Value = MathF.Max(o.Value, 0.5f * (n.AlertedAt + n.AlarmedAt) * wary);
                    o.Unseen = 0f;
                    o.Place = about;
                    o.Act = act; o.Move = move;
                    if (o.Suspect.IsNone) o.Suspect = suspect;
                    o.SuspectPlace = about;
                    Change(world, state, config, ref o, e, body.Feet, Awareness.Alerted, AwarenessCause.Heard);
                }
            }
        }

        // 5. The areas' heat cools, faster where the player is not.
        int playerArea = -1;
        if (controlled is { } cs)
        {
            var cents = cs.Entities;
            for (int i = 0; i < cents.Length; i++)
                if (bodies.Has(cents[i])) { playerArea = state.Areas.At(bodies.Get(cents[i]).Feet); break; }
        }
        for (int a = 0; a < state.Heat.Length; a++)
        {
            if (state.Heat[a] <= 0f) continue;
            float rate = 1f / MathF.Max(n.HeatCoolSeconds, dt) * (a == playerArea ? 1f : MathF.Max(1f, n.HeatAwayFactor));
            state.Heat[a] -= rate * dt;
            state.HotSeconds[a] += dt;
            if (state.Heat[a] > 0f) continue;
            state.Heat[a] = 0f;
            world.Events<AreaHeatGone>().Publish(new AreaHeatGone(a, world.Tick, state.HotSeconds[a]));
            state.HotSeconds[a] = 0f;
        }
    }

    /// <summary>The rung of <paramref name="value"/>: the highest whose threshold (× <paramref name="wary"/>, the area's heat) it reaches.</summary>
    public static Awareness Rung(float value, in PerceptionNumbers n, float wary = 1f) =>
        value >= n.AlarmedAt * wary ? Awareness.Alarmed
        : value >= n.AlertedAt * wary ? Awareness.Alerted
        : value >= n.AwareAt * wary ? Awareness.Aware
        : Awareness.Unaware;

    void Change(World world, PerceptionState state, PerceptionConfig config, ref Observer o, EntityId e, Vector3 feet, Awareness to, AwarenessCause cause)
    {
        var from = o.Level;
        o.Level = to;
        o.OnRung = 0f;
        o.Climb = 0f;
        world.Events<AwarenessChanged>().Publish(new AwarenessChanged(e, world.Tick, from, to, cause, o.Act, o.Move, o.Place, o.Value));
        if (to == Awareness.Alarmed)
        {
            Heat(world, state, config.Numbers, state.Areas.At(o.Place), o.Place);
            float voice = config.Kinds[o.Kind].Numbers.Voice;
            if (voice > 0.05f && o.Shouted == 0)
            {
                o.Shouted = 1;
                if (shoutCount < MaxShouts) shouts[shoutCount++] = (e, feet, o.Place, o.Suspect, config.Numbers.ShoutRadius * voice, o.Act, o.Move);
            }
        }
        else if (to == Awareness.Unaware)
        {
            o.Shouted = 0;
            o.Suspect = EntityId.None;
        }
    }

    static void Heat(World world, PerceptionState state, in PerceptionNumbers n, int area, Vector3 where)
    {
        if (state.Heat[area] <= 0f) state.HotSeconds[area] = 0f;
        state.Heat[area] = MathF.Min(1f, state.Heat[area] + n.HeatPerAlarm);
        world.Events<AreaHeatRose>().Publish(new AreaHeatRose(area, world.Tick, state.Heat[area], where));
    }

    /// <summary>Whether act <paramref name="a"/> is still in view: its least time, then a jump while in the air, a speed while over the limit, a move while it lasts.</summary>
    bool Lasts(in ActInView a, in Body actor, in PerceptionNumbers n)
    {
        if (a.Seconds < n.ActSeconds) return true;
        if (a.Seconds >= MaxActSeconds) return false;
        return a.Act switch
        {
            MasqueradeAct.Jump => actor.State != BodyState.Ground,
            MasqueradeAct.Speed => new Vector2(actor.Velocity.X, actor.Velocity.Y).Length() > a.Limit,
            MasqueradeAct.Move => traversals is { } ts && ts.Has(a.Actor) && ts.Get(a.Actor).Kind != TraversalKind.None,
            _ => false,
        };
    }

    /// <summary>
    /// How much a body's motion draws the eye (<see cref="PerceptionNumbers"/>' motion factors): in the air as running, or
    /// sprinting when faster than its run; on the ground still, crouched and moving, walking, running or sprinting by its
    /// speed against its own paces.
    /// </summary>
    public static float Motion(in Body body, in Locomotion loco, in PerceptionNumbers n)
    {
        if (body.State != BodyState.Ground)
            return body.Velocity.Length() > loco.RunSpeed + 0.3f ? n.MotionSprint : n.MotionRun;
        float v = new Vector2(body.Velocity.X, body.Velocity.Y).Length();
        if (v < 0.3f) return n.MotionStill;
        if (body.Crouched != 0) return n.MotionCrouched;
        if (v <= loco.WalkSpeed + 0.3f) return n.MotionWalk;
        return v <= loco.RunSpeed + 0.3f ? n.MotionRun : n.MotionSprint;
    }

    /// <summary>
    /// How clearly an observer of kind <paramref name="kind"/> with its eyes at <paramref name="eyes"/>, facing
    /// <paramref name="facing"/> (horizontal, a unit), sees body <paramref name="target"/> (0, not at all, to 1): the
    /// body's sight (<see cref="PerceptionConfig.Sight"/> in the light at its feet, × its concealment there, × its
    /// <see cref="Motion"/>); its chest within that and inside the cone; then the share of its points
    /// (<see cref="BodyPoints"/>) with a clear line from the eyes past the level and every body but the two, which must
    /// reach <see cref="PerceptionNumbers.CoverFloor"/>. The clarity is (1 − distance / sight) × that share. <paramref name="tracking"/>:
    /// the observer has the body in sight already and is watching it, so its motion counts at least as a run's (it
    /// is not lost for standing still). Allocates nothing.
    /// </summary>
    public static float Clarity(PerceptionConfig config, LevelAreas areas, CollisionWorld level, Store<Body> bodies, Store<Locomotion> locos,
        int kind, Vector3 eyes, Vector3 facing, EntityId target, EntityId self, bool tracking = false)
    {
        if (!bodies.Has(target) || !locos.Has(target)) return 0f;
        ref readonly var body = ref bodies.Get(target);
        ref readonly var loco = ref locos.Get(target);
        var n = config.Numbers;
        float motion = Motion(body, loco, n);
        if (tracking) motion = MathF.Max(motion, n.MotionRun);
        float sight = config.Sight(kind, areas.LightAt(body.Feet, config.Light)) * areas.ConcealAt(body.Feet) * motion;
        float height = body.CapsuleHeight(loco);
        var chest = body.Feet + new Vector3(0, 0, height * BodyPoints[1]);
        var to = chest - eyes;
        float distance = to.Length();
        if (sight <= 0f || distance > sight) return 0f;
        if (distance > 1e-3f && Vector3.Dot(facing, to) < config.CosHalf(kind) * distance) return 0f;
        int clear = 0;
        for (int p = 0; p < BodyPoints.Length; p++)
            if (Clear(level, bodies, locos, eyes, body.Feet + new Vector3(0, 0, height * BodyPoints[p]), self, target)) clear++;
        float share = clear / (float)BodyPoints.Length;
        if (share < n.CoverFloor) return 0f;
        return (1f - distance / sight) * share;
    }

    /// <summary>Whether the line from <paramref name="eyes"/> to <paramref name="point"/> is clear of the level and of every body but <paramref name="self"/> and <paramref name="seen"/> (the reticle's test).</summary>
    public static bool Clear(CollisionWorld level, Store<Body> bodies, Store<Locomotion> locos, Vector3 eyes, Vector3 point, EntityId self, EntityId seen)
    {
        var to = point - eyes;
        float distance = to.Length();
        if (distance < 1e-3f) return true;
        var dir = to / distance;
        if (level.Ray(eyes, dir, distance - 0.05f) is not null) return false;
        var data = bodies.Data;
        var ents = bodies.Entities;
        for (int i = 0; i < data.Length; i++)
        {
            if (ents[i] == self || ents[i] == seen || !locos.Has(ents[i])) continue;
            ref readonly var l = ref locos.Get(ents[i]);
            var cap = Capsule.OfBody(data[i].Feet, l.Radius, data[i].CapsuleHeight(l));
            if (SweepMath.RayCapsule(eyes, dir, distance - 0.3f, cap.Lower, cap.Segment, cap.Radius) is not null) return false;
        }
        return true;
    }
}
