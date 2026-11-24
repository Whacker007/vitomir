// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Casting a discipline. One order that every caster goes through,
// the player's right click and the AI's rungs alike; a walk into range when a thing is out of reach
// (with its tick); and the fire that the spell animation's `Cast`
// keyframe calls — or the order itself, straight away, for a caster with no spell to play. The
// discipline's own Java class decides what it does; the engine decides whether, when, and what it
// costs.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Rpg;

/// <summary>Which `cast` overload the engine calls, by the discipline's target flags.</summary>
public enum CastForm
{
    /// <summary>`cast(level, caster, target)` (class slot 11) — any of `0xdc`.</summary>
    Thing,
    /// <summary>`cast(level, caster)` (slot 12) — `TARGET NONE`.</summary>
    Self,
    /// <summary>`cast(level, caster, x, y, z)` (slot 13) — `TARGET AREA`.</summary>
    Area,
}

/// <summary>
/// Why a cast order came out as it did (ours: 1.1's order answers only 0, 2 or 4): what the host tells a player's machine
/// with its answer to her numbered cast (<see cref="Net.Msg.Taken"/>, <see cref="Net.OwnCast"/>), in the order's own order.
/// </summary>
public enum CastWhy : byte
{
    /// <summary>Begun: the recast armed, ACTION 13, the spell animation playing (or fired at once with none).</summary>
    Begun = 0,
    /// <summary>Busy, dead or held, before or after the cancel (the order's first gates).</summary>
    Busy = 1,
    /// <summary>No such discipline (not hers, by the name) or none selected.</summary>
    Unknown = 2,
    /// <summary>The can-cast gates other than the clock and the blood: `NODISC`, a shape, beheaded, the stat checks, level −1.</summary>
    Unable = 3,
    /// <summary>The recast clock still running.</summary>
    Recast = 4,
    /// <summary>Short of blood (or a human's mana) for the cost × `BLOODCOST`.</summary>
    Blood = 5,
    /// <summary>An area power with no point, or a power that needs a thing with none.</summary>
    NoTarget = 6,
    /// <summary>The thing named is not in the host's world (ours: <see cref="Net.ServerOrders.Cast"/> finds nothing by its number).</summary>
    Gone = 7,
    /// <summary>Out of range: a walk into range started instead, the order answering what that answered.</summary>
    Approach = 8,
    /// <summary>Out of range with Space held: refused.</summary>
    Range = 9,
    /// <summary>Not a thing this power may target.</summary>
    Target = 10,
    /// <summary>The discipline's own <c>cancast</c> said no.</summary>
    Script = 11,
    /// <summary>The caster is the Storyteller's possessed body (ours: 1.1's host refuses it silently).</summary>
    Possessed = 12,
}

/// <summary>The cast.</summary>
public static class Cast
{
    /// <summary>
    /// The console's `freecast` bits: 1 waives the blood, 2 the stat
    /// checks. Off unless somebody turns them on.
    /// </summary>
    public static uint FreeCasting { get; set; }
    public const uint FreeBlood = 0x1, FreeChecks = 0x2;

    /// <summary>How often a cast out of range walks up and asks again (250 ms).</summary>
    public const int ApproachRetryMs = 250;
    /// <summary>How far a Terrifying cast's event carries to minds (1,000).</summary>
    public const float TerrifyRange = 1000f;
    /// <summary>The cast count a scroll's temporary discipline is made with (100).</summary>
    public const int ScrollCastCount = 100;

    /// <summary>The keyframe tag the spell animations carry (`Cast`, type 6).</summary>
    public const uint CastTag = 6;

    /// <summary>The spell animations start at 0x91: `spell`, `handsspell`, `throwspell`, `spitspell`, and 4 → `awaken`.</summary>
    public const int MotionBase = 0x91;

    /// <summary>How the cast sound is played (PlayAtThing max 5000, min 200, volume 0.75, release at end).</summary>
    public const float SoundMax = 5000f, SoundMin = 200f, SoundVolume = 0.75f;

    /// <summary>Space held: never walk into range (`mods &amp; 8`).</summary>
    public const InputModifier NoWalk = InputModifier.Space;

    // ---- whether ------------------------------------------------------------------------------

    /// <summary>
    /// The stat requirements: only a `THING_TYPE_PLAYER` is held to them, and not while
    /// `freecast` bit 2 is on. For each of the row's checks a positive value is a minimum and a
    /// negative one a maximum of its negation — Decay's `(7, −60)` is Humanity at most 60. The same
    /// test colours the rollover red and gates buying a level.
    /// </summary>
    public static bool MeetsRequirements(WorldThing actor, NddDiscipline def)
    {
        if (actor.Type != ThingType.Player || actor.Actor is not { } a || (FreeCasting & FreeChecks) != 0) return true;
        foreach (var (stat, value) in def.Checks)
        {
            float have = a.Stat[stat];
            if (value > 0f ? have < value : have > -value) return false;
        }
        return true;
    }

    /// <summary>
    /// Can this character cast this now? In order, any "no" refusing:
    /// `THING_AF_NODISC`; `THING_AF_NOSHAPEDISC` against a shape change; a shape change already on;
    /// beheaded; the stat requirements (not for a temporary instance); the recast still running;
    /// level −1; and — unless `freecast` — the blood: cost × the `BLOODCOST` multiplier against
    /// blood, or mana for a human. The blood refusal is the only one with a voice:
    /// `vox_notenoughblood`, for the character the player is steering, when it can speak and is a
    /// vampire or ghoul.
    /// </summary>
    public static bool CanCast(WorldThing caster, Discipline inst) => Check(caster, inst) == CastWhy.Begun;

    /// <summary>
    /// <see cref="CanCast"/>'s gates, in its order, answering which said no (<see cref="CastWhy.Begun"/>: none). With
    /// <paramref name="voice"/> false the blood's refusal says nothing (her own machine asking before it guesses, <see cref="Net.OwnCast"/>),
    /// and <paramref name="blood"/> is the blood or mana to judge by in place of the character's own (her machine's, less what her casts
    /// the host has begun since its last sheet will have spent).
    /// </summary>
    public static CastWhy Check(WorldThing caster, Discipline inst, bool voice = true, float? blood = null)
    {
        if (caster.Actor is not { } a) return CastWhy.Unable;
        var def = inst.Definition;
        if (a.Has(Actor.Flag.NoDiscipline)) return CastWhy.Unable;
        if (a.Has(Actor.Flag.NoShapeDiscipline) && def.Has(DisciplineFlags.ShapeChange)) return CastWhy.Unable;
        if (a.ShapeChange is not null) return CastWhy.Unable;
        if (a.Has2(Actor.Flag2.IsBeheaded)) return CastWhy.Unable;
        if ((inst.Flags & Discipline.Temporary) == 0 && !MeetsRequirements(caster, def)) return CastWhy.Unable;
        int now = caster.Place?.Milliseconds ?? 0;
        if ((uint)now < (uint)inst.RecastUntil) return CastWhy.Recast;
        if (inst.Level < 0) return CastWhy.Unable;
        if ((FreeCasting & FreeBlood) == 0 && !Affords(caster, inst, blood))
        {
            if (voice && ReferenceEquals(caster.Place?.Steered, caster) && Audio.Noise.CanSpeak(caster)
                && a.Type is ActorType.Vampire or ActorType.Ghoul)
                Audio.Noise.Foley(caster, Audio.FoleyEvent.VoxNotEnoughBlood);
            return CastWhy.Blood;
        }
        return CastWhy.Begun;
    }

    /// <summary>Whether the caster has the blood (or, human, the mana) for a cast at its level.</summary>
    public static bool Affords(WorldThing caster, Discipline inst, float? blood = null)
    {
        if (caster.Actor is not { } a) return false;
        float have = blood ?? a.Stat[(int)(a.Type == ActorType.Human ? ActorStat.Mana : ActorStat.Blood)];
        return Cost(caster, inst) <= have;
    }

    /// <summary>What a cast at its level costs the caster now: the row's blood (none for a free instance) × the `BLOODCOST` multiplier.</summary>
    public static float Cost(WorldThing caster, Discipline inst) =>
        ((inst.Flags & Discipline.Free) != 0 ? 0 : inst.Definition.CostAt(inst.Level)) * CostScale(caster);

    /// <summary>The caster's `BLOODCOST` multiplier, or 1 when the rules file has the cost unscaled.</summary>
    static float CostScale(WorldThing caster) =>
        Rules.GameRules.Current.Casting.CastCostScale ? caster.Effects.Value(EffectKind.BloodCost) : 1f;

    /// <summary>
    /// In range: straight-line between the discipline's **owner** — not necessarily the
    /// caster — and the point, against `maxDist`.
    /// </summary>
    public static bool InRange(Discipline inst, WorldThing caster, Vector3 at) =>
        Vector3.Distance((inst.Owner ?? caster).Position, at) <= inst.Definition.MaxDistance;

    // ---- the order ----------------------------------------------------------------------------

    /// <summary>
    /// **The cast order**. <paramref name="inst"/> is the discipline to cast, or null for
    /// the scroll's or, failing that, the one selected. Answers <see cref="OrderResult.Refused"/>,
    /// <see cref="OrderResult.Busy"/> or <see cref="OrderResult.Started"/>, or whatever the walk into
    /// range answered when it handed over to one.
    /// <list type="number">
    /// <item>Busy, dead or restrained: not now.</item>
    /// <item><b>Whatever it was doing is cancelled and it stops walking — before anything is
    /// checked</b>, so even a refused cast stops the character in its tracks.</item>
    /// <item>Which discipline: the given one; a scroll that is really the caster's (a temporary
    /// instance, flagged <see cref="Discipline.FromScroll"/>); the selected one; none — refused.</item>
    /// <item><see cref="CanCast"/>.</item>
    /// <item>The target, by the row's flags: an <b>area</b> takes the point — a thing's position at
    /// order time, or the point given — and turns to face it; a discipline that needs a
    /// <b>thing</b> refuses without one, walks into range when out of it (unless Space is held, or
    /// it refuses), must pass <see cref="DisciplineTarget.MayTarget"/>, and faces it; a
    /// <b>self</b> discipline takes neither.</item>
    /// <item>The script's `cancast` — a class without one answers yes.</item>
    /// <item>The <b>recast starts now</b>, the character goes into ACTION 13, the discipline waits
    /// in the pending slot, and the spell animation plays on the action channel at thirty frames a
    /// second times the character's attack speed. With no animation to play it fires at once;
    /// otherwise the character is busy for the animation and the `Cast` tag fires it.</item>
    /// </list>
    /// </summary>
    public static OrderResult Order(WorldThing caster, WorldThing? target, Vector3? pos, Discipline? inst,
                                    InputModifier mods, Item? scroll = null) =>
        Order(caster, target, pos, inst, mods, scroll, out _);

    /// <summary>
    /// <see cref="Order(WorldThing, WorldThing?, Vector3?, Discipline?, InputModifier, Item?)"/>, saying why it came out as it did
    /// (the host's answer to a player's numbered cast, <see cref="Net.OwnCast"/>). The same order, the same steps.
    /// </summary>
    public static OrderResult Order(WorldThing caster, WorldThing? target, Vector3? pos, Discipline? inst,
                                    InputModifier mods, Item? scroll, out CastWhy why)
    {
        why = CastWhy.Unknown;
        if (caster.Actor is not { } me) return OrderResult.Refused;
        // A player's machine sends it to the host (message 0x18). when the cast's start is certain on her
        // machine (Net.OwnCast, asked before it goes, by the same rules as below on her copy) its start is shown at once.
        string? asked = inst?.Name ?? (scroll is { ScrollDiscipline.Length: > 0 } ? scroll.ScrollDiscipline : me.Disciplines.Current?.Name);
        var own = asked is not null && scroll is null ? Net.NetOrders.Casts(caster) : null;
        var sure = own?.Sure(caster, target, pos, inst ?? me.Disciplines.Current, mods);
        if (asked is not null && Net.NetOrders.ForwardCast(caster, target, pos, asked, (int)mods, scroll?.Thing))
        {
            if (sure is { } certain) own!.Start(caster, certain);
            why = CastWhy.Begun;
            return OrderResult.Started;
        }
        var place = caster.Place;
        int now = place?.Milliseconds ?? 0;
        why = CastWhy.Busy;
        if (Unready(me, now)) { PausedOrders.Busy(caster); return OrderResult.Busy; }   // RPG_CANTDOACTION while paused
        Actions.Cancel(caster);
        if (Unready(me, now)) return OrderResult.Busy;
        Actions.StopWalking(caster);

        if (inst is null)
        {
            if (scroll is { ScrollDiscipline.Length: > 0 } && (scroll.Thing?.Owner ?? scroll.Thing?.Holder) == caster)
                inst = FromScroll(caster, scroll);
            inst ??= me.Disciplines.Current;
            why = CastWhy.Unknown;
            if (inst is null) return OrderResult.Refused;
        }
        if ((why = Check(caster, inst)) != CastWhy.Begun) return Refuse(caster, inst);

        var def = inst.Definition;
        why = CastWhy.NoTarget;
        if (def.Has(DisciplineFlags.TargetArea))
        {
            me.Target = null;
            Vector3? p = target?.Position ?? pos;
            if (p is null) return Refuse(caster, inst);
            me.ActionPoint = p.Value;
            caster.Face(p.Value);
        }
        else if ((def.Flags & ThingTargets) != 0)
        {
            if (target is null) return Refuse(caster, inst);
            if (!InRange(inst, caster, target.Position))
            {
                why = CastWhy.Range;
                if ((mods & NoWalk) != 0) return Refuse(caster, inst);
                var walk = Approach(caster, target, inst, mods);
                why = walk switch { OrderResult.Started => CastWhy.Approach, OrderResult.Busy => CastWhy.Busy, _ => CastWhy.Range };
                return walk;
            }
            why = CastWhy.Target;
            if (!DisciplineTarget.MayTarget(def, caster, target)) return Refuse(caster, inst);
            me.Target = target;
            target.Flags |= WorldThing.Flag.Targetted;
            me.ActionPoint = Vector3.Zero;
            caster.Face(target);
        }
        else if (def.Has(DisciplineFlags.TargetNone))
        {
            me.Target = null;
            me.ActionPoint = Vector3.Zero;
        }

        why = CastWhy.Script;
        if (inst.Script is { } script && place?.ScriptObjects is { } scripts
            && !scripts.CanCast(script, inst.Level, caster, target, pos))
            return Refuse(caster, inst);

        why = CastWhy.Begun;
        inst.RecastUntil = now + def.RecastAt(inst.Level);
        me.Action = ActionMode.Cast;
        if (!ReferenceEquals(me.PendingDiscipline, inst))
        {
            if (me.PendingDiscipline is { } old && (old.Flags & Discipline.Temporary) != 0) DisciplineBook.Discard(caster, old);
            me.PendingDiscipline = inst;
        }
        Diagnostics.Journal.Current?.Write(now, Diagnostics.JournalKind.Order,
            ("what", "cast"), ("who", Diagnostics.Watch.Name(caster)), ("discipline", def.Name),
            ("level", inst.Level), ("at", target is null ? null : Diagnostics.Watch.Name(target)));
        int ms = PlaySpell(caster, me, def);
        if (ms < 0) { Fire(caster); return OrderResult.Started; }
        me.BusyUntil = now + ms;
        return OrderResult.Started;
    }

    /// <summary>The spell animation on the action channel: 0x91 + the row's column, at thirty frames a second × the attack speed; −1 when the set has none.</summary>
    internal static int PlaySpell(WorldThing caster, Actor me, NddDiscipline def) =>
        Actions.PlayAction(caster, MotionBase + def.CastAnimation, me.AttackSpeedScale * Anim.ThingAnimation.Rate);

    /// <summary>`TARGET INVENTORY | ENEMY | FRIENDLY | DEAD | HEAD`: the flags that need a thing (`0xdc`).</summary>
    public const DisciplineFlags ThingTargets = DisciplineFlags.TargetInventory | DisciplineFlags.TargetEnemy
        | DisciplineFlags.TargetFriendly | DisciplineFlags.TargetDead | DisciplineFlags.TargetHead;

    static bool Unready(Actor me, int now) =>
        now < me.BusyUntil || me.Has(Actor.Flag.Dead) || me.Has2(Actor.Flag2.Restrained);

    /// <summary>A refusal: a temporary instance made for it is let go.</summary>
    static OrderResult Refuse(WorldThing caster, Discipline inst)
    {
        if ((inst.Flags & Discipline.Temporary) != 0 && !ReferenceEquals(caster.Actor?.PendingDiscipline, inst))
            DisciplineBook.Discard(caster, inst);
        return OrderResult.Refused;
    }

    /// <summary>
    /// A scroll's temporary instance: the scroll's discipline at the scroll's level,
    /// flagged temporary, free and from-a-scroll (0x13), counting from 100, carrying the scroll's GUID.
    /// </summary>
    static Discipline? FromScroll(WorldThing caster, Item scroll)
    {
        if (caster.Place?.Powers?[scroll.ScrollDiscipline] is not { } def) return null;
        object? script = caster.Place.ScriptObjects?.Make(def.Script, caster);
        if (caster.Place.ScriptObjects is not null && script is null) return null;
        return new Discipline
        {
            Definition = def, Owner = caster, Level = scroll.ScrollLevel, QuickSlot = -1, Script = script,
            Flags = Discipline.Temporary | Discipline.Free | Discipline.FromScroll, CastCount = Rules.GameRules.Current.Casting.ScrollCastCount,
            ScrollGuid = scroll.Thing?.Guid ?? 0,
        };
    }

    // ---- walking into range (ACTION 22) -------------------------------------------------------

    /// <summary>
    /// Out of range: walk into it. Not now if busy; already going there is nothing;
    /// ethereal is refused; a target on its way out is nothing. The current order is cancelled, a
    /// party member offensive enough rallies the party before it goes, and the walk starts — at run
    /// speed times the MOVESPEED of this moment — and is ticked once straight away.
    /// </summary>
    public static OrderResult Approach(WorldThing caster, WorldThing target, Discipline inst, InputModifier mods)
    {
        if (caster.Actor is not { } me) return OrderResult.Refused;
        var place = caster.Place;
        int now = place?.Milliseconds ?? 0;
        if (Unready(me, now)) return OrderResult.Busy;
        if (me.Action == ActionMode.ApproachCast && ReferenceEquals(me.Target, target)) return OrderResult.Ignored;
        if (me.Has(Actor.Flag.Ethereal)) return OrderResult.Refused;
        if (target.Has(WorldThing.Flag.Remove)) return OrderResult.Ignored;
        Actions.Cancel(caster);
        if (Unready(me, now)) return OrderResult.Busy;
        if (inst.Definition.Has(DisciplineFlags.Offensive) && ReferenceEquals(place?.Steered, caster)
            && caster.Mind is { } lead && place?.Minds is { } minds)
            minds.Rally(lead, target, forced: (mods & InputModifier.Ctrl) != 0);   // Mods & 2
        Actions.StopWalking(caster);
        me.PendingDiscipline = inst;
        me.Target = target;
        target.Flags |= WorldThing.Flag.Targetted;
        me.Action = ActionMode.ApproachCast;
        me.ActionRetryAt = 0;
        me.BusyUntil = now;
        me.OrderMods = mods;
        me.ActionSpeed = me.RunSpeed * me.MoveSpeedScale;
        Actions.Close(caster, me, target, now, 0);
        Approaching(caster, me, now);
        return OrderResult.Started;
    }

    /// <summary>
    /// The walk's tick, whenever its retry is due: in range — **the whole order again**,
    /// from the top, with no modifiers; still able to notice the target — walk on and look again in
    /// a quarter-second; otherwise give up. The walk spends nothing and arms no recast. The give-up is
    /// the cancel **unforced** (the cancel with (0, 0)), so a caster made busy on the way — a
    /// flinch — is left walking under ACTION 22 until it is free, and the next tick gives up again.
    /// </summary>
    internal static void Approaching(WorldThing caster, Actor me, int now)
    {
        if (now < me.ActionRetryAt) return;
        var target = me.Target;
        var inst = me.PendingDiscipline;
        if (target is not null && inst is not null && InRange(inst, caster, target.Position))
        {
            Order(caster, target, null, inst, InputModifier.None);
            return;
        }
        if (target is not null && Sight.Notices(caster, target, target.Mind?.Moving ?? false))
        {
            Actions.Close(caster, me, target, now, Rules.GameRules.Current.Casting.CastApproachRetryMs);
            return;
        }
        Actions.Cancel(caster);                 // Unforced
    }

    // ---- the fire -----------------------------------------------------------------------------

    /// <summary>
    /// **The fire** — at the spell animation's `Cast` tag, or from the order when there
    /// was no animation. In the engine's order:
    /// <list type="number">
    /// <item>No longer in ACTION 13 (a flinch, a death, a new order): nothing, and the recast
    /// already armed stays armed.</item>
    /// <item>A scroll is used up now, before the cast — or, if it has gone from the caster, the cast
    /// is off.</item>
    /// <item>The blood is checked again, silently: short now — the cast is off, nothing spent.</item>
    /// <item>TERRIFYING: event 14 from the caster's mind to everybody within 1,000.</item>
    /// <item>The script's `cast`, by the row's flags — area, thing or self; a thing is re-checked
    /// first (<see cref="DisciplineTarget.MayTarget"/>: range, death, everything) and the cast is off
    /// if it fails. `THING_AF2_SCROLLCAST` is set around a scroll's call. <b>What the script
    /// answers is thrown away</b>: a fizzle and a resist cost exactly what a success does.</item>
    /// <item>OFFENSIVE at a thing: the steered caster's party rallies (unless `NOPARTYNOTIFY`), and
    /// the target's mind is attacked (<see cref="Ai.Minds.AttackedBy"/>).</item>
    /// <item>The blood — `ef_decreaseblood` (or `ef_decreasemana`) by value, −cost × BLOODCOST,
    /// re-sampled here.</item>
    /// <item>The cast sound, at the caster.</item>
    /// <item>The cast count goes up, and then `actorcast` goes out with it.</item>
    /// <item>A temporary instance is let go.</item>
    /// </list>
    /// Every "the cast is off" above is the cancel **unforced** (the cancel with (0, 0)), and at
    /// the animation's `Cast` tag the caster is still busy with the gesture, so that cancel does
    /// nothing: ACTION 13 and the pending discipline stay, and the caster finishes the gesture and is
    /// busy to its end, as after a cast that went off. Only a fire with
    /// no animation (straight from the order) finds the caster free and puts it back to nothing.
    /// </summary>
    public static void Fire(WorldThing caster)
    {
        if (caster.Actor is not { } me) return;
        // On a player's machine the fire is the host's (the effect, the blood, the sound come from there); a start guessed
        // there (Net.OwnCast) leaves her copy in ACTION 13 while its spell animation plays, and its `Cast` tag fires nothing.
        if (Net.NetOrders.Mode(caster.Place) == Net.NetMode.Client) return;
        var inst = me.PendingDiscipline;
        if (me.Action != ActionMode.Cast) return;
        if (inst is null) { Actions.Cancel(caster); return; }
        var place = caster.Place;
        var scripts = place?.ScriptObjects;
        bool fromScroll = (inst.Flags & Discipline.FromScroll) != 0;
        if (fromScroll)
        {
            if (place?.Thing(inst.ScrollGuid) is { Item: { Type: ItemType.Scroll } scroll } paper
                && (paper.Owner ?? paper.Holder) == caster)
            {
                UseScroll(caster, scroll);
            }
            else
            {
                DisciplineBook.Discard(caster, inst);
                me.PendingDiscipline = null;
                Actions.Cancel(caster);                 // Unforced — busy with the gesture, nothing
                return;
            }
        }
        var def = inst.Definition;
        float mult = CostScale(caster);
        int cost = (inst.Flags & Discipline.Free) != 0 ? 0 : def.CostAt(inst.Level);
        bool human = me.Type == ActorType.Human;
        if ((FreeCasting & FreeBlood) == 0 && cost * mult > me.Stat[(int)(human ? ActorStat.Mana : ActorStat.Blood)])
        {
            Actions.Cancel(caster);                 // Unforced — busy with the gesture, nothing
            return;
        }
        if (def.Has(DisciplineFlags.Terrifying) && caster.Mind is { } mind && place?.Minds is { } minds)
            minds.Raise(mind, Ai.Minds.AiEvent.Terrifying, Rules.GameRules.Current.Casting.TerrifyEventRange);

        if (def.Has(DisciplineFlags.TargetArea))
        {
            if (fromScroll) me.Flags2 |= Actor.Flag2.ScrollCast;
            if (inst.Script is { } s) scripts?.Cast(s, CastForm.Area, inst.Level, caster, null, me.ActionPoint);
            me.Flags2 &= ~Actor.Flag2.ScrollCast;
        }
        if ((def.Flags & ThingTargets) != 0)
        {
            var target = me.Target;
            if (target is null || !DisciplineTarget.MayTarget(def, caster, target)) { Actions.Cancel(caster); return; }
            if (fromScroll) me.Flags2 |= Actor.Flag2.ScrollCast;
            if (inst.Script is { } s) scripts?.Cast(s, CastForm.Thing, inst.Level, caster, target, Vector3.Zero);
            me.Flags2 &= ~Actor.Flag2.ScrollCast;
            if (def.Has(DisciplineFlags.Offensive))
            {
                if (ReferenceEquals(place?.Steered, caster) && !def.Has(DisciplineFlags.NoPartyNotify)
                    && caster.Mind is { } lead && place?.Minds is { } party)
                    party.Rally(lead, target);
                if (target.Mind is { } hit && place?.Minds is { } victims) victims.AttackedBy(hit, caster);
            }
        }
        else if (def.Has(DisciplineFlags.TargetNone))
        {
            if (fromScroll) me.Flags2 |= Actor.Flag2.ScrollCast;
            if (inst.Script is { } s) scripts?.Cast(s, CastForm.Self, inst.Level, caster, null, Vector3.Zero);
            me.Flags2 &= ~Actor.Flag2.ScrollCast;
        }

        if ((FreeCasting & FreeBlood) == 0 && (inst.Flags & Discipline.Free) == 0 && def.CostAt(inst.Level) != 0)
            EffectRuntime.Apply(caster, human ? "ef_decreasemana" : "ef_decreaseblood", 0, durationMs: 1,
                                value: -def.CostAt(inst.Level) * mult);
        if (def.Sound != "-") Net.HostSeen.Sound(caster, def.Sound, SoundMin, SoundMax, SoundVolume, Audio.ChannelFlag.ReleaseAtEnd);
        if (def.Sound != "-" && place?.Audio is { } audio && audio.Bank.Load(def.Sound) is { } sound)
            audio.Mixer.PlayAtThing(sound, caster, SoundMax, SoundMin, SoundVolume, Audio.ChannelFlag.ReleaseAtEnd);
        inst.CastCount++;
        scripts?.ActorCast(caster, def.EngineName, inst.Level, inst.CastCount);
        if ((inst.Flags & Discipline.Temporary) != 0)
        {
            if (ReferenceEquals(me.Disciplines.Current, inst)) me.Disciplines.Current = null;
            DisciplineBook.Discard(caster, inst);
            me.PendingDiscipline = null;
            if (ReferenceEquals(place?.Steered, caster)) DisciplineBook.Selected?.Invoke(caster);
        }
    }

    /// <summary>A scroll used up by its cast: taken out of the world and off whoever held it.</summary>
    static void UseScroll(WorldThing caster, Item scroll)
    {
        if (caster.Actor is not { } me) return;
        if (ReferenceEquals(me.Held, scroll)) me.Held = null;
        me.Inventory.Remove(scroll);
        if (scroll.Thing is { } t) caster.Place?.Remove(t, drop: false);
    }

    /// <summary>
    /// A cast cancelled (the cancel's case 13): a temporary instance waiting to fire is let
    /// go, and the pending slot is emptied.
    /// </summary>
    public static void Discard(WorldThing caster)
    {
        if (caster.Actor is not { } me) return;
        if (me.PendingDiscipline is { } d && (d.Flags & Discipline.Temporary) != 0) DisciplineBook.Discard(caster, d);
        me.PendingDiscipline = null;
    }
}
