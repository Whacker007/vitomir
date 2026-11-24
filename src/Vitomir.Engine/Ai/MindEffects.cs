// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The modes a discipline puts a mind into. Mesmerised (10), dazed (11), afraid
// (12) and frenzied (9): each has a way in, a way out and a handler, and each is short. The way in
// is called by the effect apply with the effect's creator; the way out by the effect
// removal, **whatever mode the mind has moved on to by then**. Pet (2), possessing (13)
// and majesty (14) are the other three the apply reaches, each as 1.1's disciplines
// put a mind into it.
using System.Numerics;
using Microsoft.Extensions.Logging;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Ai;

public sealed partial class Minds
{
    /// <summary>`AIMIND_FLAG_*`, the `.nai` `mindflags` word — `Codex.java` names them.</summary>
    public static class MindFlag
    {
        public const uint NoAfraid = 0x1, NoDazed = 0x2, NoMesmerized = 0x4, NoPossessed = 0x8, NoMajesty = 0x10;
    }

    /// <summary>
    /// The mind's target, when it is still a character in the same place — what every
    /// handler here starts from.
    /// </summary>
    static WorldThing? TargetOf(Mind mind) =>
        mind.Target is { IsActor: true } t && t.InWorld && ReferenceEquals(t.Place, mind.Thing.Place) ? t : null;

    /// <summary>
    /// Whether a character still qualifies as what this mind is after: alive, in the same
    /// place, within `searchDist` when <paramref name="range"/> is asked for, noticed and in sight.
    /// </summary>
    bool Qualifies(Mind mind, WorldThing target, bool range)
    {
        if (target.Actor is not { } them || them.Has(World.Actor.Flag.Dead)) return false;
        if (!ReferenceEquals(target.Place, mind.Thing.Place) || !target.InWorld) return false;
        if (range && Vector3.Distance(target.Position, mind.Thing.Position) > mind.Class.SearchDist) return false;
        return Rpg.Sight.Notices(mind.Thing, target, Moving(target)) && Rpg.Sight.Sees(Level, mind.Thing, target);
    }

    /// <summary>
    /// Busy, dead or held down — the four tests every handler here makes before it does anything
    /// (walking, `THING_AF_DEAD`, `THING_AF2_RESTRAINED`).
    /// </summary>
    bool Occupied(Mind mind) =>
        mind.Moving || mind.Thing.Actor is not { } a || a.BusyUntil > Now || mind.BusyUntil > Now
        || a.Has(World.Actor.Flag.Dead) || a.Has2(World.Actor.Flag2.Restrained);

    /// <summary>
    /// Back to what it was — `pet` for a summoned pet, else its home mode — with no target, thinking
    /// again in a tenth of a second (the common tail of the mode's end).
    /// </summary>
    void Release(Mind mind)
    {
        mind.Enter((mind.Flags & Mind.FlagPet) != 0 ? AiMode.Pet : mind.BaseMode, andBase: false);
        mind.Aim(null, Now);
        mind.NextThink = Now + 100;
    }

    /// <summary>
    /// The unit vector toward the target, once per target; zero when there is no living
    /// target in the same place.
    /// </summary>
    static void Heading(Mind mind)
    {
        if (mind.HeadingKnown) return;
        mind.HeadingKnown = true;
        if (mind.Target is { IsActor: true } t && !t.Actor!.Has(World.Actor.Flag.Dead)
            && ReferenceEquals(t.Place, mind.Thing.Place))
        {
            var d = t.Position - mind.Thing.Position;
            mind.Heading = d.LengthSquared() > 0f ? Vector3.Normalize(d) : d;
            return;
        }
        mind.Heading = Vector3.Zero;
    }

    /// <summary>A vector turned about the vertical by <paramref name="degrees"/> (Euler 0, 0, angle).</summary>
    static Vector3 Turn(Vector3 v, float degrees)
    {
        float r = degrees * (MathF.PI / 180f), c = MathF.Cos(r), s = MathF.Sin(r);
        return new Vector3(v.X * c - v.Y * s, v.X * s + v.Y * c, v.Z);
    }

    /// <summary>
    /// Wander a step: <paramref name="distance"/> along a heading, 50 units up, onto the floor the ground lookup
    /// finds under it with a reach of 100 (<see cref="Location.FloorUnder"/>: the character's own
    /// surface, its sector's faces, the point's sector — below the point only), and the move order to
    /// that floor point — or try again in <paramref name="retry"/> ms when there is none.
    /// Earlier, this asked the walk map for the highest node 100 below to 16 above the point, which
    /// refused a step off a drop of more than 50 and could take a floor above.
    /// </summary>
    void Stray(Mind mind, Vector3 heading, float distance, bool run, int retry)
    {
        var point = mind.Thing.Position + heading * distance;
        point.Z += StrayLift;
        // The ground lookup with the character's own surface and sector and a reach of 100: its surface if the
        // point is over it, else a walkable face of that sector 0..128 below, else one of the point's
        // sector by a ray 228 down — never a floor above the point (World/FloorProbe.cs).
        var floor = where?.FloorUnder(point, mind.Thing, StrayReach);
        int node = floor is { } at ? walk?.Find(at, 1f, 1f) ?? -1 : -1;
        if (node < 0) { mind.NextThink = Now + retry; return; }
        Go(mind, floor!.Value, node, run: run);
    }

    /// <summary>How far up the point is lifted before the surface search, and how far it looks (0x42c80000).</summary>
    public const float StrayLift = 50f, StrayReach = 100f;

    // ---- mesmerized (the handler, and the way out) -----------------------------

    /// <summary>
    /// Mesmerised by <paramref name="by"/> — unless <paramref name="force"/>d, refused to a
    /// mind that is unaffected, whose class says `NOMESMERIZED`, or that is frenzied. Any daze or
    /// fear on it ends, it takes the mesmeriser as its target, stops, and goes to mode 10.
    /// </summary>
    public void Mesmerize(Mind mind, WorldThing? by, bool force = false)
    {
        if (!force && ((mind.Flags & Mind.FlagUnaffected) != 0 || (mind.Class.MindFlags & MindFlag.NoMesmerized) != 0
                       || mind.Mode == AiMode.Frenzied)) return;
        Rpg.EffectRuntime.RemoveKind(mind.Thing, EffectKind.Dazed, 0);
        Rpg.EffectRuntime.RemoveKind(mind.Thing, EffectKind.Afraid, 0);
        if (by is not null) SetTarget(mind, by);
        mind.Walker.Stop();
        mind.Enter(AiMode.Mesmerized, andBase: false);
        mind.NextThink = Now + 100;
    }

    /// <summary>
    /// The mesmerised handler, every 0.8 s: while it can still make out the one who
    /// mesmerised it — **at any range** — it follows them the way a companion follows its leader;
    /// once it cannot, it is released.
    /// </summary>
    void Mesmerized(Mind mind)
    {
        mind.NextThink = Now + 800;
        if (Occupied(mind)) return;
        if (TargetOf(mind) is { } t && Qualifies(mind, t, range: false)) { Follow(mind, t); return; }
        Release(mind);
    }

    /// <summary>The mesmerise effect ended: released, whatever mode it is in now.</summary>
    public void Unmesmerize(Mind mind) => Release(mind);

    // ---- dazed (the handler, and the way out) ----------------------------------

    /// <summary>
    /// Dazed — refused, unless forced, to a mind that is unaffected, whose class says
    /// `NODAZED`, or that is frenzied or mesmerised. Any fear ends; it takes the target, stops, mode 11.
    /// </summary>
    public void Daze(Mind mind, WorldThing? by, bool force = false)
    {
        if (!force && ((mind.Flags & Mind.FlagUnaffected) != 0 || (mind.Class.MindFlags & MindFlag.NoDazed) != 0
                       || mind.Mode is AiMode.Frenzied or AiMode.Mesmerized)) return;
        Rpg.EffectRuntime.RemoveKind(mind.Thing, EffectKind.Afraid, 0);
        if (by is not null) SetTarget(mind, by);
        mind.Walker.Stop();
        mind.Enter(AiMode.Dazed, andBase: false);
        mind.NextThink = Now + 100;
    }

    /// <summary>
    /// The dazed handler, every half second: when it is standing free, it walks a few
    /// steps — 10 to 40 units — in any direction at all (0 to 359° off the way to its target).
    /// </summary>
    void Dazed(Mind mind)
    {
        mind.NextThink = Now + 500;
        if (Occupied(mind)) return;
        Heading(mind);
        var heading = Turn(mind.Heading, Random.Unit() * 359f);
        Stray(mind, heading, Random.Unit() * 30f + 10f, run: false, retry: 200);
    }

    /// <summary>The daze ended.</summary>
    public void Undaze(Mind mind) => Release(mind);

    // ---- afraid (the handler, and the way out) ---------------------------------

    /// <summary>
    /// Afraid of <paramref name="of"/> — refused, unless forced, to a mind that is
    /// unaffected, whose class says `NOAFRAID`, or that is frenzied, mesmerised or dazed. It takes
    /// what it fears as its target, may say so in a second and shout in a millisecond, stops, mode 12.
    /// </summary>
    public void Frighten(Mind mind, WorldThing? of, bool force = false)
    {
        if (!force && ((mind.Flags & Mind.FlagUnaffected) != 0 || (mind.Class.MindFlags & MindFlag.NoAfraid) != 0
                       || mind.Mode is AiMode.Frenzied or AiMode.Mesmerized or AiMode.Dazed)) return;
        if (of is not null) SetTarget(mind, of);
        mind.NextFear = Now + 1000;
        mind.NextFearShout = Now + 1;
        mind.Walker.Stop();
        mind.Enter(AiMode.Afraid, andBase: false);
        mind.NextThink = Now + 100;
    }

    /// <summary>
    /// The afraid handler, every 0.4 s once it has stopped moving. While what it fears
    /// is still there and within `searchDist`:
    /// <list type="bullet">
    /// <item>`vox_fear`, an even chance a second, then quiet for five;</item>
    /// <item>event 1 to everybody within 1,500, once a second, carrying what it fears — which is
    /// what sends a street full of pedestrians running (<see cref="Witnessed"/>);</item>
    /// <item>and a run: 40 to 240 units in a direction 90° to 180° either side of the way to the
    /// thing it fears.</item>
    /// </list>
    /// Once it is not, the mind turns to face where it last was and is released.
    /// </summary>
    void Afraid(Mind mind)
    {
        mind.NextThink = Now + 400;
        if (Occupied(mind)) return;
        if (TargetOf(mind) is { } t && Qualifies(mind, t, range: true))
        {
            if (mind.NextFear < Now)
            {
                if (Random.Unit() < 0.5f)
                {
                    Audio.Noise.Foley(mind.Thing, Audio.FoleyEvent.VoxFear);
                    mind.NextFear = Now + 5000;
                }
                else mind.NextFear = Now + 1000;
            }
            if (mind.NextFearShout < Now)
            {
                Shout(mind, AiEvent.Fear, Rules.GameRules.Current.Ai.FearShoutRange, t);
                mind.NextFearShout = Now + 1000;
            }
            Heading(mind);
            float angle = Random.Unit() * 90f + 90f;
            if (Random.Unit() < 0.5f) angle = -angle;
            Stray(mind, Turn(mind.Heading, angle), Random.Unit() * 200f + 40f, run: true, retry: 30);
            return;
        }
        Unfrighten(mind);
    }

    /// <summary>The fear ended: it looks toward what it feared, if that is still about, and is released.</summary>
    public void Unfrighten(Mind mind)
    {
        if (TargetOf(mind) is { } t) mind.Thing.Face(t);
        Release(mind);
    }

    /// <summary>How far the afraid mode's event 1 carries (0x44bb8000).</summary>
    public const float FearShoutRange = 1500f;

    // ---- frenzied (the handler, and the way out) -------------------------------

    /// <summary>
    /// Into frenzy (from `rpgActor::Frenzy`) — unless the mind is unaffected. Every
    /// mesmerise, daze and fear on it ends, and it goes to mode 9, which is **not** its home mode.
    /// </summary>
    public void EnterFrenzy(Mind mind)
    {
        if ((mind.Flags & Mind.FlagUnaffected) != 0) return;
        Rpg.EffectRuntime.RemoveKind(mind.Thing, EffectKind.Mesmerized, 0);
        Rpg.EffectRuntime.RemoveKind(mind.Thing, EffectKind.Dazed, 0);
        Rpg.EffectRuntime.RemoveKind(mind.Thing, EffectKind.Afraid, 0);
        mind.NextFrenzyShout = Now + 1000;
        mind.Enter(AiMode.Frenzied, andBase: false);
        mind.NextThink = Now + 100;
    }

    /// <summary>Out of frenzy (from `rpgActor::Unfrenzy`): released, target cleared.</summary>
    public void LeaveFrenzy(Mind mind) => Release(mind);

    /// <summary>
    /// The frenzied handler, every two seconds once it is free. It shouts event 2 every
    /// two seconds — which the broadcast throws away (<see cref="Broadcastable"/>) — and then goes
    /// for **the nearest creature with a mind within 500 units, friend or foe**, that it can make out
    /// within its `searchDist`: nine times in ten it tries to feed on it (the feed rung, forced),
    /// and otherwise, or when the feed is refused, it attacks.
    /// </summary>
    void Frenzied(Mind mind)
    {
        mind.NextThink = Now + 2000;
        if (Occupied(mind)) return;
        if (mind.NextFrenzyShout < Now)
        {
            Shout(mind, AiEvent.Frenzy, 1000f);
            mind.NextFrenzyShout = Now + 2000;
        }
        WorldThing? best = null;
        float nearest = 99999.9f;
        foreach (var other in Nearby(mind.Thing, Rules.GameRules.Current.Ai.FrenzyTargetReach))
        {
            if (other.Mind is null || ReferenceEquals(other, mind.Thing)) continue;
            float d = Vector3.Distance(mind.Thing.Position, other.Position);
            if (d > Rules.GameRules.Current.Ai.FrenzyTargetReach) continue;
            if (d < nearest && Qualifies(mind, other, range: true)) { best = other; nearest = d; }
        }
        if (best is null) return;
        SetTarget(mind, best);
        if (Random.Unit() <= 0.9f && FeedRung(mind, best, ignoreCooldown: true, skipRoll: true)) return;
        AttackOrder(mind, best);
    }

    /// <summary>How far a frenzied character looks for something to fall on (0x43fa0000).</summary>
    public const float FrenzyReach = 500f;

    /// <summary>
    /// An attack order from the AI: the second attack when the class's `attackAlt` roll
    /// comes up, then event 9 to everybody within 500, and the fight marked engaged.
    /// </summary>
    void AttackOrder(Mind mind, WorldThing target)
    {
        if (mind.Thing.Actor is not { } me) return;
        int which = mind.Class.AttackAlt != 0f && Random.Unit() < mind.Class.AttackAlt ? 1 : 0;
        Rpg.Actions.Attack(mind.Thing, target, me.Weapon, which, Now, motions: motions);
        Shout(mind, AiEvent.Attacked, Rules.GameRules.Current.Ai.AttackShoutRange);
        mind.EngagedAt = Now;
    }

    // ---- being fed on (each drain, and the release) ---------------------------

    /// <summary>
    /// A drain tick, as the victim's mind has it: event 6 to everybody within 2,000,
    /// carrying the feeder. That is the whole of it while the feed goes on; if the victim is no
    /// longer being fed on by then, it is <see cref="FedOnReleased"/>.
    /// </summary>
    public void BeingFedOn(Mind mind)
    {
        if (TargetOf(mind) is { } feeder) Shout(mind, AiEvent.FedOn, Rules.GameRules.Current.Ai.FedOnShoutRange, feeder);
        if (mind.Thing.Actor?.Action is Rpg.ActionMode.FedOn or Rpg.ActionMode.WristFedOn) return;
        FedOnReleased(mind);
    }

    /// <summary>
    /// Let go: a pedestrian, or a guard, is afraid of whoever fed on it; anybody else just
    /// forgets them. Either way it thinks again in a tenth of a second.
    /// </summary>
    public void FedOnReleased(Mind mind)
    {
        if (TargetOf(mind) is { } feeder
            && (mind.BaseMode == AiMode.PedWander || mind.Class.SearchStyle == GuardSearchStyle))
        {
            Frighten(mind, feeder);
            mind.NextThink = Now + 100;
            return;
        }
        mind.Aim(null, Now);
        mind.NextThink = Now + 100;
    }

    /// <summary>How far a victim's cry carries (0x44fa0000).</summary>
    public const float FedOnShoutRange = 2000f;

    /// <summary>
    /// Events 1 and 6 — somebody is afraid of something, somebody is being fed on. They
    /// mean something only when the one shouting is a pedestrian at heart (home mode `pedwander`) or
    /// a guard (`searchStyle 1`). Then a listening guard with no target of its own, and not stopped by
    /// `AIMAIN_FLAG_NOGUARDSEARCH`, goes for the thing being shouted about; and a listening pedestrian
    /// with no target is afraid of it. The gather is a flood through the sectors with **no sight
    /// test**.
    /// </summary>
    void Witnessed(Mind me, Mind sender)
    {
        if (sender.BaseMode != AiMode.PedWander && sender.Class.SearchStyle != GuardSearchStyle) return;
        // What is being shouted about is read off the **sender's own target**, not
        // off the event: for a frightened pedestrian that is what it fears, for a victim its feeder.
        if (sender.Target is not { IsActor: true } about) return;
        if (me.Class.SearchStyle == GuardSearchStyle && me.Target is null && (MainFlags & AiMainFlag.NoGuardSearch) == 0)
        {
            SetTarget(me, about);
            me.Enter(AiMode.Enemy, andBase: false);
            return;
        }
        if (me.BaseMode == AiMode.PedWander && me.Target is null) Frighten(me, about);
    }

    /// <summary>
    /// Event 14 — a terrifying discipline was used, or somebody was attacked,
    /// or killed. A pedestrian at heart is afraid of the one who did it; a guard, not
    /// stopped by `NOGUARDSEARCH`, goes for them.
    /// </summary>
    void Terrified(Mind me, Mind sender)
    {
        if (me.BaseMode == AiMode.PedWander) { Frighten(me, sender.Thing); return; }
        if (me.Class.SearchStyle == GuardSearchStyle && (MainFlags & AiMainFlag.NoGuardSearch) == 0)
        {
            SetTarget(me, sender.Thing);
            me.Enter(AiMode.Enemy, andBase: false);
            me.NextThink = Now + 200;
        }
    }

    /// <summary>
    /// Event 12 — a guard cast something harmful: another guard with no target
    /// of its own, not stopped by `NOGUARDSEARCH`, takes the caster's target.
    /// </summary>
    void CastAgainst(Mind me, Mind sender)
    {
        if (sender.Class.SearchStyle != GuardSearchStyle || me.Class.SearchStyle != GuardSearchStyle) return;
        if (me.Target is not null || (MainFlags & AiMainFlag.NoGuardSearch) != 0) return;
        if (sender.Target is not { IsActor: true } t) return;
        SetTarget(me, t);
        me.Enter(AiMode.Enemy, andBase: false);
    }

    // ---- being attacked, hurt and killed ----------

    /// <summary>The character a blow is credited to: itself, or a projectile's firer.</summary>
    static WorldThing? Culprit(WorldThing? by) =>
        by is { Type: ThingType.Projectile } shot ? Rpg.Projectiles.Owner(shot) : by;

    /// <summary>
    /// **Somebody went for this character** — every swing at it, hit or miss
    /// (and from the damage below), a feed's resist roll, an offensive discipline.
    /// It raises event 3 to everybody within 2,000 (which nobody handles); then, for an
    /// attacker that is a character — and not a player thing attacking a player thing — by mode:
    /// <list type="bullet">
    /// <item><b>party</b>, and a <b>pet</b> whose master is in the party: the party piles in
    /// (<see cref="Rally"/>) — the pet itself is not in the rally's roster, so it keeps its own
    /// target;</item>
    /// <item><b>enemy</b>, <b>approach</b>: badly hurt, it is afraid of the attacker;</item>
    /// <item><b>pedwander</b>: afraid of it;</item>
    /// <item><b>enemysearch</b>: badly hurt, afraid; otherwise it goes for the attacker;</item>
    /// <item><b>afraid</b>: what it fears is now the attacker.</item>
    /// </list>
    /// And when the attacker has a mind, event 14 goes out **from the attacker** — its own mind is
    /// the sender (the broadcast is made from the attacker's mind), so a pedestrian
    /// who hears it is afraid of the one swinging, and a guard goes for them. Vitomir sent it from
    /// the victim once, which frightened the monks of whoever was being hit and would
    /// have sent guards after the victim; 1.1's monks in the Monastery
    /// are afraid of the one striking every time.
    /// </summary>
    public void AttackedBy(Mind mind, WorldThing? by)
    {
        if (mind.Thing.Actor is not { } me) return;
        Shout(mind, AiEvent.Assaulted, Rules.GameRules.Current.Ai.AssaultShoutRange, by);
        var who = Culprit(by);
        if (who is null || !who.IsActor) return;
        if (who.Type == ThingType.Player && mind.Thing.Type == ThingType.Player) return;
        switch (mind.Mode)
        {
            // A pet brings the party in only when its master is a party member (the master
            // a player-type character carrying THING_AF_PARTY); a companion always does.
            case AiMode.Pet:
                if (MasterOf(mind) is { Type: ThingType.Player, Actor: { } master } && master.Has(World.Actor.Flag.Party))
                    Rally(mind, who);
                break;
            case AiMode.Party:
                if (mind.Thing.Actor?.Has(World.Actor.Flag.Party) == true) Rally(mind, who);
                break;
            case AiMode.Enemy:
            case AiMode.Approach:
                if (Hurt(mind, me, mind.Class.LowHealth)) Frighten(mind, who);
                break;
            case AiMode.PedWander:
                Frighten(mind, who);
                break;
            case AiMode.EnemySearch:
                if (Hurt(mind, me, mind.Class.LowHealth)) Frighten(mind, who);
                else { SetTarget(mind, who); mind.Enter(AiMode.Enemy, andBase: false); }
                break;
            case AiMode.Afraid:
                SetTarget(mind, who);
                break;
        }
        if (who.Mind is { } theirs) Shout(theirs, AiEvent.Terrifying, Rules.GameRules.Current.Ai.AssaultShoutRange);
    }

    /// <summary>
    /// **The Fix `wakeonhit`** (<see cref="Fixes.WakeOnHit"/>, ours; *"Yes let's
    /// get a fix for it"*). A character a script holds (`THING_AF_AIPAUSED` — the Society of Leopold's
    /// soldiers under `ActorAIOff_TriggerOn`, until the player walks into their region) is handed its
    /// attacker by the event handler like anybody else, but 1.1's think is gated on the flag and nothing
    /// clears it on damage: it stands and is shot dead from outside its region. With the Fix on, a blow
    /// from a **party member** that it survives lets it go — the flag comes off, and the hurt event that
    /// follows gives it its attacker — so it fights. Only somebody outside the party is woken, and only by
    /// the party; the scenes' own hold on the whole AI (`AIOff`, the main flags) is another word and still
    /// stops it thinking in a cutscene.
    /// </summary>
    void WakeOnHit(Mind mind, World.Actor me, WorldThing who)
    {
        if (!me.Has(World.Actor.Flag.AiPaused) || me.Has(World.Actor.Flag.Party) || me.Has(World.Actor.Flag.Dead)) return;
        // Not one a script has put away for a later scene — unrendered or bodiless: waking it gives an
        // enemy nobody can see (Vukodlak in the Cathedral of Flesh's first level, as seen in play).
        if (mind.Thing.Collide == CollideShape.None || (mind.Thing.RenderFlags & Rpg.FallenListener.DontRender) != 0) return;
        if (who.Actor is not { } theirs || !theirs.Has(World.Actor.Flag.Party)) return;
        if (!(mind.Thing.Place?.Fixes ?? Fixes.Current).WakeOnHit) return;
        me.Flags &= ~World.Actor.Flag.AiPaused;
        mind.NextThink = Now;
        log.LogInformation("AI: {Who} was held and is let go, hurt by {By} (the Fix wakeonhit)", Diagnostics.Watch.Name(mind.Thing), Diagnostics.Watch.Name(who));
    }

    /// <summary>
    /// **Hurt** (from the damage path once the character has survived it). Event 4 to
    /// everybody within 2,000, carrying the attacker — a companion that cannot see who did it reaches
    /// for a sense discipline. With no attacker, a party member off away from the one being steered
    /// runs back to them. With one, <see cref="AttackedBy"/>, and then by mode: a party member or an
    /// enemy **changes target** to the attacker when the blow was big enough to be worth it
    /// (`highDamage` of its health, scaled by stance — <see cref="WorthSwitching"/>), a party member
    /// also rallying the others; a **mesmerised** character is shaken out of it nine times in ten —
    /// a pedestrian at heart then flees, a searcher fights; an approaching one fights.
    /// </summary>
    public void Damaged(Mind mind, WorldThing? by, float damage)
    {
        if (mind.Thing.Actor is not { } me) return;
        Shout(mind, AiEvent.Hurt, Rules.GameRules.Current.Ai.AssaultShoutRange, by);
        if (by is null)
        {
            if (mind.Thing.Type == ThingType.Player && me.Has(World.Actor.Flag.Party) && Selected is { } lead
                && !ReferenceEquals(lead, mind.Thing))
            {
                float gap = Vector3.Distance(mind.Thing.Position, lead.Position);
                if (gap > Radius(lead) + Radius(mind.Thing) && walk?.Find(lead.Position) is >= 0 and var node)
                {
                    // Flags `0x10`: the free-spot test is asked at the ground under the
                    // leader, where the leader stands, so 1.1 refuses this walk whenever
                    // the leader is alive and solid — and so does Vitomir (Rpg.MoveOrder.Head).
                    Go(mind, lead.Position, node, run: true);
                    mind.NextThink = Now + 1500;
                }
            }
            return;
        }
        var who = Culprit(by);
        if (who is null || !who.IsActor) return;
        if (who.Type == ThingType.Player && mind.Thing.Type == ThingType.Player) return;
        WakeOnHit(mind, me, who);
        AttackedBy(mind, who);
        switch (mind.Mode)
        {
            case AiMode.Party:
                if (!ReferenceEquals(who, mind.Target) && WorthSwitching(mind, me, damage)) { SetTarget(mind, who); Rally(mind, who); }
                break;
            case AiMode.Enemy:
                if (!ReferenceEquals(who, mind.Target) && WorthSwitching(mind, me, damage)) SetTarget(mind, who);
                break;
            case AiMode.Mesmerized:
                if (Random.Unit() < Rules.GameRules.Current.Ai.MesmerizeShakenOffChance)
                {
                    Rpg.EffectRuntime.RemoveKind(mind.Thing, EffectKind.Mesmerized, 0);
                    if (mind.BaseMode == AiMode.PedWander) Frighten(mind, who);
                    else if (mind.BaseMode == AiMode.EnemySearch) { SetTarget(mind, who); mind.Enter(AiMode.Enemy, andBase: false); }
                }
                break;
            case AiMode.Approach:
                if (!ReferenceEquals(who, mind.Target) && WorthSwitching(mind, me, damage))
                {
                    mind.Enter(AiMode.Enemy, andBase: false);
                    SetTarget(mind, who);
                }
                break;
        }
    }

    /// <summary>
    /// Killed: event 5 to everybody within 2,000, carrying the killer — and, when the
    /// killer is a character with a mind, event 14 **from the killer** (sent from the
    /// killer's mind, as the attack handler does).
    /// </summary>
    public void Died(Mind mind, WorldThing? by)
    {
        Shout(mind, AiEvent.Killed, Rules.GameRules.Current.Ai.AssaultShoutRange, by);
        var who = by switch
        {
            { Type: ThingType.Projectile } shot => Rpg.Projectiles.Owner(shot),
            { Type: ThingType.Explosion } blast => blast.SpawnedBy != 0 ? blast.Place?.Thing(blast.SpawnedBy) : null,
            _ => by,
        };
        if (who is { IsActor: true, Mind: { } theirs }) Shout(theirs, AiEvent.Terrifying, Rules.GameRules.Current.Ai.AssaultShoutRange);
    }

    /// <summary>
    /// Events 4 and 5 — one of my side was hurt, or killed, by something, in
    /// single player only. Same side means both party members, or neither. If this character **cannot notice** the one who
    /// did it, it reaches for a sense discipline, forced, and thinks again in a second.
    /// </summary>
    void Unseen(Mind me, Mind sender, WorldThing? by)
    {
        // Single player only: in a network game, host or client, 1.1 hears events 4 and 5 and does
        // nothing (a comparison with 1.1).
        if (by is null || Net.NetOrders.Mode(me.Thing.Place) != Net.NetMode.Single) return;
        bool senderParty = sender.Thing.Type == ThingType.Player && sender.Thing.Actor?.Has(World.Actor.Flag.Party) == true;
        bool mine = me.Thing.Type == ThingType.Player && me.Thing.Actor?.Has(World.Actor.Flag.Party) == true;
        if (senderParty != mine) return;
        if (Rpg.Sight.Notices(me.Thing, by, Moving(by))) return;
        SenseRung(me, ignoreCooldown: true);
        me.NextThink = Now + 1000;
    }

    /// <summary>How far events 3, 4, 5 and the attacked-by event 14 carry (0x44fa0000).</summary>
    public const float AssaultShoutRange = 2000f;

    /// <summary>How often a blow shakes a mesmerised character out of it (0.9 — the same 0.9 the wound ladder misuses).</summary>
    public const float ShakenOff = 0.9f;

    /// <summary>
    /// A blow big enough to change targets for: at least `highDamage` of the character's
    /// health, times 0.8 in the attack stance, 1.8 in defence, and 1 otherwise — the stance only
    /// counting for a party member.
    /// </summary>
    static bool WorthSwitching(Mind mind, World.Actor me, float damage)
    {
        float stance = mind.Thing.Type == ThingType.Player && me.Has(World.Actor.Flag.Party)
            ? me.Stance switch { PartyStance.Attack => 0.8f, PartyStance.Defense => 1.8f, _ => 1f }
            : 1f;
        return damage >= mind.Class.HighDamage * me.Health * stance;
    }

    /// <summary>A thing's collision radius, or 0 with no collide shape (the shape is tested before the radius).</summary>
    static float Radius(WorldThing t) => t.Collide == CollideShape.None ? 0f : t.Radius;

    // ---- majesty (the handler, and the way out) ---------------------------------

    /// <summary>
    /// Struck by <paramref name="by"/>'s Majesty — refused, unless <paramref name="force"/>d,
    /// to a mind that is unaffected, whose class says `NOMAJESTY`, or that is frenzied, mesmerised,
    /// dazed or afraid (modes 9 to 12). It takes the one with the Majesty as its target, stops
    /// walking, and goes to mode 14. Majesty itself is a script (`EffectDiscMajesty`): every two
    /// seconds it puts `ef_disc_majesty2`, five seconds long, on everybody within 384 of the caster,
    /// or tops it up — so a character stays struck for as long as it stays near.
    /// </summary>
    public void Majesty(Mind mind, WorldThing? by, bool force = false)
    {
        if (!force && ((mind.Flags & Mind.FlagUnaffected) != 0 || (mind.Class.MindFlags & MindFlag.NoMajesty) != 0
                       || mind.Mode is AiMode.Frenzied or AiMode.Mesmerized or AiMode.Dazed or AiMode.Afraid)) return;
        if (by is not null) SetTarget(mind, by);
        mind.Walker.Stop();
        mind.Enter(AiMode.Majesty, andBase: false);
        mind.NextThink = Now + 100;
    }

    /// <summary>
    /// The majesty handler, four times a second: it stops walking, the order it was given
    /// is cancelled (the unforced cancel — which lets a swing or a cast already playing finish), and it turns to
    /// face the one with the Majesty — unless it is being fed on, staked or held still by a script
    /// (actions 9, 12, 19 and 20). It does not attack, cast or move of its own accord;
    /// in 1.1 a Cappadocian struck mid-cast finished the cast and then stood, and could be fed on.
    /// When the caster is gone, dead or out of sight, it is released.
    /// </summary>
    void Majestic(Mind mind)
    {
        mind.NextThink = Now + Rules.GameRules.Current.Ai.MajestyThinkMs[0];
        mind.Walker.Stop();
        Rpg.Actions.Cancel(mind.Thing);
        if (TargetOf(mind) is not { } t || !Qualifies(mind, t, range: false)) { Release(mind); return; }
        if (mind.Thing.Actor?.Action is Rpg.ActionMode.FedOn or Rpg.ActionMode.WristFedOn
                                     or Rpg.ActionMode.Staked or Rpg.ActionMode.Still) return;
        mind.Thing.Face(t);
    }

    /// <summary>How often a majestic character looks round (250 ms).</summary>
    public const int MajestyThink = 250;

    /// <summary>The Majesty ended: released — to `pet` for a pet — with no target.</summary>
    public void Unmajesty(Mind mind) => Release(mind);

    // ---- possession (the handler, and the way out) -----------------------------

    /// <summary>
    /// **A possession takes** (from the apply of `ef_disc_possession` or
    /// `ef_disc_entrancement` on the <paramref name="body"/>, with the caster's mind). Nothing
    /// happens if the caster is unaffected or itself possessed, if the body's class says
    /// `NOPOSSESSED`, or if the body is possessed already; a body that is no longer worth anything
    /// just drops the caster's target. Otherwise:
    /// <list type="bullet">
    /// <item>if the caster is the character being steered, **the controls go to the body**: the
    /// steered character, the panes and the camera — but the party's
    /// selected member stays the caster;</item>
    /// <item>the body's mind lets go of its target, is marked possessed (0x10000000, which stops it
    /// thinking), and goes back to its home mode; any mesmerise, daze or fear on it ends;</item>
    /// <item>the caster takes the body as its target and goes to mode 13.</item>
    /// </list>
    /// The team change is the effect's script's (`EffectDiscPossession` gives the body the caster's
    /// team and gives it back when the effect ends) — which is why every Cappadocian in the room
    /// turned on the possessed one within a tenth of a second in 1.1.
    /// </summary>
    public void Possess(Mind caster, WorldThing body)
    {
        if ((caster.Flags & Mind.FlagUnaffected) != 0) return;
        if (!Worth(caster, body)) { caster.Aim(null, Now); return; }
        if (body.Mind is not { } possessed) return;
        if ((possessed.Class.MindFlags & MindFlag.NoPossessed) != 0) return;
        if ((caster.Flags & Mind.FlagPossessed) != 0 || (possessed.Flags & Mind.FlagPossessed) != 0) return;
        if (ReferenceEquals(caster.Thing, Steered)) where?.SetSteered(body);
        possessed.Aim(null, Now);
        possessed.Flags |= Mind.FlagPossessed;
        possessed.Enter(possessed.BaseMode, andBase: false);
        Rpg.EffectRuntime.RemoveKind(body, EffectKind.Mesmerized, 0);
        Rpg.EffectRuntime.RemoveKind(body, EffectKind.Dazed, 0);
        Rpg.EffectRuntime.RemoveKind(body, EffectKind.Afraid, 0);
        SetTarget(caster, body);
        caster.Enter(AiMode.Possessing, andBase: false);
        caster.NextThink = Now + 100;
    }

    /// <summary>
    /// The caster's own handler while it possesses, five times a second: it stands where
    /// it is, and **once it is badly hurt** (`verylowhealth`) the possession is ended from
    /// its side — every possessing effect on the body comes off. Otherwise the effect
    /// runs its length, or ends with the body.
    /// </summary>
    void Possessing(Mind mind)
    {
        mind.NextThink = Now + Rules.GameRules.Current.Ai.MajestyThinkMs[1];
        if (mind.Thing.Actor is not { } me || !Hurt(mind, me, mind.Class.VeryLowHealth)) return;
        if (mind.Target is { Removed: false } body && ReferenceEquals(body.Place, mind.Thing.Place))
            Rpg.EffectRuntime.RemoveKind(body, EffectKind.Possessing, 0);
    }

    /// <summary>How often a possessing caster checks on itself (200 ms).</summary>
    public const int PossessingThink = 200;

    /// <summary>
    /// **The possession ends** (from the removal of the possessing effect on
    /// <paramref name="body"/> — its time up, the caster badly hurt, or the body dead). If the caster
    /// is still the party's selected member, the controls come back to it (steered, panes, camera);
    /// the caster goes back to its home mode — **keeping the body as its target** — and the body's
    /// mind is no longer possessed, lets go of its target and goes home. In 1.1 a body taken
    /// over died after 33 seconds, and the controls were Christof's again in the same tenth.
    /// </summary>
    public void Unpossess(Mind caster, WorldThing? body)
    {
        if (ReferenceEquals(caster.Thing, Selected)) where?.SetSteered(caster.Thing);
        caster.Enter(caster.BaseMode, andBase: false);
        caster.NextThink = Now + 100;
        if (body?.Mind is not { } freed) return;
        freed.Flags &= ~Mind.FlagPossessed;
        freed.Aim(null, Now);
        freed.Enter(freed.BaseMode, andBase: false);
    }

    // ---- pets (the handler, and the way out) -------------

    /// <summary>
    /// **A pet joins its master** (the apply of a `pet` effect — Summon Elemental, Summon Soul, Dark
    /// Hunter, Hatch the Viper, Beckoning ). The master keeps a list of four
    /// (<c>AI_MAXPETS</c>, made on the first); with it full nothing happens. The pet
    /// is marked a pet (bit 0x4, which lets it think even while steered), remembers its master,
    /// goes to mode 2, and takes the master's own target if it has one.
    /// </summary>
    public bool AddPet(Mind master, WorldThing pet)
    {
        if (pet.Mind is not { } p) return false;
        master.Pets ??= new int[MaxPets];
        int slot = Array.IndexOf(master.Pets, 0);
        if (slot < 0) return false;
        p.Flags |= Mind.FlagPet;
        p.MasterGuid = master.Thing.Guid;
        p.Enter(AiMode.Pet, andBase: false);
        p.NextThink = Now + 100;
        if (master.Target is { } theirs) SetTarget(p, theirs);
        master.Pets[slot] = pet.Guid;
        return true;
    }

    /// <summary>`AI_MAXPETS` (`Codex.java`) — the size of the engine's list, not a tunable.</summary>
    public const int MaxPets = 4;

    /// <summary>How many pets a master has: the filled slots, or 0 with no list at all.</summary>
    public static int PetCount(Mind master) => master.Pets?.Count(g => g != 0) ?? 0;

    /// <summary>
    /// **A pet's effect ends** (then the pet's release). The slot is emptied and the pet
    /// let go. An ordinary pet — a beckoned animal — loses the mark and goes back to its home mode
    /// with no target. A **summoned** one (<c>THING_AF_SUMMONED</c>) runs from its master instead,
    /// afraid of it whatever it is (forced), fades out over five seconds, and is gone when it has.
    /// </summary>
    public void ReleasePet(Mind master, WorldThing pet)
    {
        if (master.Pets is not { } list) return;
        int slot = Array.IndexOf(list, pet.Guid);
        if (slot < 0) return;
        list[slot] = 0;
        if (pet.Mind is not { } p || p.MasterGuid != master.Thing.Guid) return;
        p.MasterGuid = 0;
        if (pet.Actor?.Has(World.Actor.Flag.Summoned) != true)
        {
            p.Flags &= ~Mind.FlagPet;
            p.Enter(p.BaseMode, andBase: false);
            p.NextThink = Now + 100;
            p.Aim(null, Now);
            return;
        }
        p.NextThink = Now + 100;
        Frighten(p, master.Thing, force: true);
        pet.ExpireAt = Now + Rules.GameRules.Current.Ai.SummonedLingerMs[0];
        pet.Model3D.SetAlpha(0f, Rules.GameRules.Current.Ai.SummonedLingerMs[0]);
    }

    /// <summary>
    /// How long a released summoned pet lasts, and how long it takes to fade out entirely — the same
    /// five seconds, as a clock (<c>now + 5000</c>) and as the fade's length in ms (5000.0).
    /// </summary>
    public const int SummonedLinger = 5000;

    /// <summary>A pet's master, while it is still about (the master looked up by GUID).</summary>
    WorldThing? MasterOf(Mind pet) =>
        pet.MasterGuid == 0 ? null : where?.Thing(pet.MasterGuid) is { Removed: false } m ? m : null;

    /// <summary>
    /// The pet's turn, once a second. Badly hurt, it tries to heal (the heal rung,
    /// the pet's think). With a target, it fights (<see cref="PetFight"/>), and a turn spent casting or
    /// attacking is the whole turn. Otherwise, when it is standing free and not held, it walks after
    /// its master the way a companion walks after the leader.
    /// </summary>
    void PetTurn(Mind mind)
    {
        mind.NextThink = Now + Rules.GameRules.Current.Ai.SummonedLingerMs[1];
        if (mind.Thing.Actor is not { } me) return;
        if (Hurt(mind, me, mind.Class.VeryLowHealth) && HealRung(mind) is { Acted: true } healed) { Reached(mind, healed); return; }
        if (mind.Target is not null && PetFight(mind)) return;
        if (me.BusyUntil > Now || me.Has(World.Actor.Flag.Dead) || me.Has2(World.Actor.Flag2.Restrained)) return;
        if (MasterOf(mind) is { } master) Follow(mind, master);
    }

    /// <summary>How often a pet thinks (000 ms).</summary>
    public const int PetThink = 1000;

    /// <summary>
    /// **The pet's fight** (the pet's own, called from nowhere else).
    /// Thinks again after its class's `attackRate`. A leash first: with `breakDist` set, a pet more
    /// than that far from its master drops its target (no stance to scale it). A target
    /// no longer worth it is dropped; one to leave alone, or no weapon, drops it and thinks again in
    /// a tenth. Then the discipline and item ladder, and a cast is the turn; a ranged attack with no
    /// clear shot sidesteps instead (<see cref="Sidestep"/>) and the turn goes on to the
    /// follow, since no flag is set; and otherwise the attack, the event-9 shout within 500,
    /// and the fight marked engaged. Answers whether it cast or attacked — the two flags
    /// that end the pet's turn.
    /// </summary>
    bool PetFight(Mind mind)
    {
        mind.NextThink = Now + mind.Class.AttackRate;
        if (mind.Target is not { } target || mind.Thing.Actor is not { } me) return false;
        if (mind.Class.BreakDist != 0f && MasterOf(mind) is { } master
            && Vector3.Distance(master.Position, mind.Thing.Position) > mind.Class.BreakDist)
        {
            mind.Aim(null, Now);
            return false;
        }
        if (!Worth(mind, target)) { mind.Aim(null, Now); return false; }
        if (LeaveAlone(mind, target) || me.Weapon is null)
        {
            mind.Aim(null, Now);
            mind.NextThink = Now + Rules.GameRules.Current.Ai.RetryCombatMs;
            return false;
        }
        if (Reach(mind, target) is { Acted: true } chose) { Reached(mind, chose); return true; }
        var current = me.Weapon.Current;
        if (current.Is(Rpg.WeaponFlag.Ranged) && World.Bodies.InReach(mind.Thing, target, current.MaxDistance)
            && !ClearShot(mind.Thing, target))
        {
            Sidestep(mind, target);                                      // no flag set
            return false;
        }
        int which = mind.Class.AttackAlt > 0f && Random.Unit() < mind.Class.AttackAlt ? 1 : 0;
        Rpg.Actions.Attack(mind.Thing, target, me.Weapon, which, Now, motions: motions);
        Shout(mind, AiEvent.Attacked, Rules.GameRules.Current.Ai.AttackShoutRange);
        mind.EngagedAt = Now;
        return true;
    }
}
