// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What an effect actually *does*. `Effects.cs` is the list and the fold — the query half. This is
// the half that changes something, function for function with the original's:
//
//   Apply       put an instance on (by level, or by value)
//   Remove      take one off, with its consequences
//   Tick        reap, expire, pay out — once a frame for every character
//   Recompute   push a fold into the character
//
// The interesting part is how the engine makes "apply once" work without a separate mechanism. An
// instant effect is added to the list like any other; the recompute runs while it still counts, so
// its value lands in the **base** row; it is then marked (0x800000) so no later fold counts it
// again; and the next tick removes it. It exists for one frame, and the fold is the only arithmetic
// in the system.
using Vitomir.Engine.Ai;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Rpg;

/// <summary>The part of the effect subsystem that writes to a character.</summary>
public static class EffectRuntime
{
    // ---- GUIDs --------------------------------------------------------------------------------

    static int nextGuid = 1;

    /// <summary>
    /// The next effect GUID. One counter for the whole game, set to 1 at start-up,
    /// saved and restored with the game, never reset by a level change.
    /// </summary>
    public static int TakeGuid() => Interlocked.Increment(ref nextGuid) - 1;

    /// <summary>The counter as it stands — what a save writes — and where a load puts it back.</summary>
    public static int NextGuid
    {
        get => Volatile.Read(ref nextGuid);
        set => Volatile.Write(ref nextGuid, Math.Max(1, value));
    }

    // ---- apply --------------------------------------------------------------------------------

    /// <summary>
    /// `ApplyEffect`. A <paramref name="value"/> applies it **by value**, at level −1
    /// (`AddActorEffectByValue`); otherwise by <paramref name="level"/>, which the by-level
    /// door holds to at most 5 and the definition's table supplies the value for.
    /// <paramref name="durationMs"/> of 0 is an effect that does not end on its own. Answers the
    /// instance, or null when none was made — a negator, or a possession refused — which the
    /// natives turn into the GUID the scripts are given, or 0.
    ///
    /// In the engine's order:
    /// <list type="number">
    /// <item><b>A negator makes no instance</b>: it removes, with all the consequences of a
    /// removal, every instance of its type and subtype worth no more than it is.</item>
    /// <item>The instance: its end, its creator, its flags, its GUID, its level and value, its
    /// first tick at <c>now + 1</c> — so nothing ticks on the frame it went on — and its own script
    /// object, when the row names a class.</item>
    /// <item>It goes on the end of the list.</item>
    /// <item><b>A possession is refused</b> when the character is already possessed — after it has
    /// gone on the list, where it stays, uncounted by nothing and started by nobody, until its time
    /// runs out. That is the engine's.</item>
    /// <item>The recompute, and then the fold mark on the four instant kinds.</item>
    /// <item>Instant damage goes through the damage path as hit kind 4, credited to the creator;
    /// instant healing goes through the heal.</item>
    /// <item>The mind-altering kinds put the character's mind into their mode, toward the creator
    /// (<see cref="Minds.Mesmerize"/>, <see cref="Minds.Frighten"/>, <see cref="Minds.Daze"/>) —
    /// only when it has a mind and the creator is a character.</item>
    /// <item>Its script hears `effectstarted`.</item>
    /// </list>
    /// </summary>
    public static Effect? Apply(WorldThing thing, NedEffect definition, int level, int now,
                                int durationMs = 0, WorldThing? creator = null, float? value = null,
                                Item? source = null, Rng? rng = null,
                                Anim.MotionSets? motions = null, uint flags = 0,
                                int intParam = 0, float floatParam = 0f, int? creatorGuid = null, bool network = false)
    {
        if (Refused(thing, network)) return null;                           // A player's machine, not from the host
        bool byValue = value is not null;
        if (!byValue && level > NedEffect.Levels - 1) level = NedEffect.Levels - 1;
        float worth = byValue ? value!.Value : At(definition, level);

        if (definition.Has(EffectFlags.Negator))
        {
            foreach (var e in thing.Effects.All.ToArray())
                if (e.Kind == definition.Kind && e.Subtype == definition.Subtype && e.Value <= worth)
                    Remove(thing, e, notify: true);
            return null;
        }

        var added = new Effect
        {
            Definition = definition,
            Level = byValue ? -1 : level,
            Value = worth,
            Until = durationMs != 0 ? now + durationMs : 0,
            CreatorGuid = creatorGuid ?? creator?.Guid ?? 0,
            Creator = creator,
            Source = source,
            Flags = flags,
            Guid = TakeGuid(),
            IntParam = intParam,
            FloatParam = floatParam,
            NextTick = definition.TickMs != 0 ? now + 1 : 0,
        };
        if (definition.Script.Length > 0) added.Script = thing.Place?.ScriptObjects?.Make(definition.Script, thing);
        thing.Effects.Append(added);

        if (definition.Kind == EffectKind.Possessing && thing.Mind is { } possessed && (possessed.Flags & Mind.FlagPossessed) != 0)
            return null;

        Recompute(thing, definition.Kind, definition.Subtype);
        if (definition.Kind is EffectKind.BaseStatInstant or EffectKind.BaseSoakInstant
                            or EffectKind.DamageInstant or EffectKind.HealInstant)
            added.Folded = true;

        var by = CreatorOf(thing, added);
        if (definition.Kind == EffectKind.DamageInstant)
            Damage.Apply(thing, by, HitKind.Effect, (DamageType)definition.Subtype,
                         added.Value, rng ?? thing.Place?.Minds?.Random ?? new Rng(1), now,
                         motions: motions ?? thing.Place?.Motions);
        else if (definition.Kind == EffectKind.HealInstant)
            Heal(thing, added.Value);

        if (thing.Mind is { } mind && thing.Place?.Minds is { } minds && by is { IsActor: true })
        {
            switch (definition.Kind)
            {
                case EffectKind.Mesmerized: minds.Mesmerize(mind, by); break;
                case EffectKind.Afraid: minds.Frighten(mind, by); break;
                case EffectKind.Dazed: minds.Daze(mind, by); break;
                case EffectKind.Majesty: minds.Majesty(mind, by); break;
                // The two that need the creator's own mind as well: the pet
                // joins the creator's list, the possession is the creator's to run.
                case EffectKind.Pet when by.Mind is { } master:
                    minds.AddPet(master, thing);
                    // A summoned pet is never saved (read in a comparison with 1.1):
                    // THING_FLAG_SAVENEVER, so a game saved with an elemental out loads without it.
                    if (thing.Actor?.Has(Actor.Flag.Summoned) == true) thing.Flags |= WorldThing.Flag.SaveNever;
                    break;
                case EffectKind.Possessing when by.Mind is { } caster: minds.Possess(caster, thing); break;
            }
        }

        if (added.Script is { } script) thing.Place?.ScriptObjects?.EffectStarted(script, thing, added, durationMs);
        return added;
    }

    /// <summary>By name, the way the natives and the engine's own callers name one. Null when there is no such row.</summary>
    public static Effect? Apply(WorldThing thing, string name, int level, int durationMs = 0,
                                WorldThing? creator = null, float? value = null, uint flags = 0,
                                int intParam = 0, float floatParam = 0f, int? creatorGuid = null)
    {
        if (Definitions(thing)?[name] is not { } def) return null;
        return Apply(thing, def, level, thing.Place?.Milliseconds ?? 0, durationMs, creator, value,
                     flags: flags, intParam: intParam, floatParam: floatParam, creatorGuid: creatorGuid);
    }

    /// <summary>
    /// **A player's machine puts on and takes off only what the host sends.** The original's put-on and
    /// take-off each answer 0 at once when the process is a network client
    /// and the call is not the network's own — the effect message's (which passes 1) or
    /// the whole list's clear (which passes 1). Every other caller passes 0 and
    /// is refused there: a script's `AddActorEffectByLevel`/`…ByValue`/`RemoveActorEffect`,
    /// an effect's expiry and an instant's fold in the tick, a
    /// negator's removals inside a put-on, a death's and a revive's clear, a kind cleared,
    /// a dying creator's, the right click on an icon — so an effect run out on a
    /// player's machine stays on there until the host's word takes it off, and a revive's born effects there are the
    /// host's. Per place here (<see cref="Net.NetOrders.Mode"/>), as the rest of Vitomir's network tests are.
    /// </summary>
    static bool Refused(WorldThing thing, bool network) =>
        !network && Net.NetOrders.Mode(thing.Place) == Net.NetMode.Client;

    /// <summary>
    /// A level's value. Levels count from zero; the engine indexes the table directly (by
    /// level x 4`), so a level of −1 reads the word before it — the tick column, an integer read as
    /// a float, which is what `DreadGaze`'s `level-1` at its first level is given. Nothing below −1
    /// is ever asked for.
    /// </summary>
    public static float At(NedEffect definition, int level) =>
        level == -1 ? BitConverter.Int32BitsToSingle(definition.TickMs) : definition.At(level);

    /// <summary>The effect definitions a thing's place has, when it has been given them.</summary>
    static EffectDefinitions? Definitions(WorldThing thing) => thing.Place?.Effects;

    /// <summary>
    /// Who applied an effect, resolved by GUID as the engine does each time it needs the thing
    /// — so a creator that has left the place is nobody.
    /// </summary>
    static WorldThing? CreatorOf(WorldThing thing, Effect effect) =>
        effect.CreatorGuid == 0 ? null
        : thing.Place?.Thing(effect.CreatorGuid) ?? (effect.Creator is { Removed: false } c ? c : null);

    // ---- remove -------------------------------------------------------------------------------

    /// <summary>
    /// `RemoveEffect`. The instance is marked removed — it stays in the list, skipped,
    /// until the next tick reaps it. With <paramref name="notify"/> the mind-altering kinds let the
    /// character's mind go (<see cref="Minds.Unmesmerize"/>, <see cref="Minds.Unfrighten"/>,
    /// <see cref="Minds.Undaze"/>) — **whatever mode the mind is in by then**, which is the
    /// engine's. Its script hears `effectended` with reason 2, the only reason the engine ever
    /// sends. Then, unless it was an instant already folded into the base, the recompute takes its
    /// share back out.
    /// </summary>
    public static bool Remove(WorldThing thing, Effect effect, bool notify = true, bool network = false)
    {
        if (Refused(thing, network)) return false;                          // A player's machine, not from the host
        effect.Spent = true;
        if (notify && thing.Mind is { } mind && thing.Place?.Minds is { } minds)
        {
            switch (effect.Kind)
            {
                case EffectKind.Mesmerized: minds.Unmesmerize(mind); break;
                case EffectKind.Afraid: minds.Unfrighten(mind); break;
                case EffectKind.Dazed: minds.Undaze(mind); break;
                case EffectKind.Majesty: minds.Unmajesty(mind); break;
                // A pet's effect off: out of its creator's list and let go.
                case EffectKind.Pet when CreatorOf(thing, effect) is { Mind: { Pets: not null } master }:
                    minds.ReleasePet(master, thing);
                    break;
                // A possession off: only while the body is still marked possessed, and only through
                // a creator that is still about with a mind.
                case EffectKind.Possessing when (mind.Flags & Ai.Mind.FlagPossessed) != 0
                                            && CreatorOf(thing, effect) is { Mind: { } caster }:
                    minds.Unpossess(caster, thing);
                    break;
            }
        }
        if (effect.Script is { } script) thing.Place?.ScriptObjects?.EffectEnded(script, thing, effect, EndedReason);
        Save.LoadedEffects.Ended(thing, effect);                           // ours: what a load made again for it and its script did not take (the repair `loadedeffects`)
        if (!effect.Folded) Recompute(thing, effect.Kind, effect.Subtype);
        return true;
    }

    /// <summary>The `reason` every `effectended` carries (a literal 2).</summary>
    public const int EndedReason = 2;

    /// <summary>The first live instance of a named effect removed; false when there is none.</summary>
    public static bool Remove(WorldThing thing, string name, bool notify = true) =>
        thing.Effects.Find(name) is { } e && Remove(thing, e, notify);

    /// <summary>The live instance with this GUID removed; false when there is none.</summary>
    public static bool Remove(WorldThing thing, int guid, bool notify = true) =>
        thing.Effects.Find(guid) is { } e && Remove(thing, e, notify);

    /// <summary>
    /// Every instance of a type and subtype worth no more than <paramref name="upTo"/> removed
    /// — the AI's way of ending a daze or a fear by kind, with 999999.9 for "all".
    /// </summary>
    public static void RemoveKind(WorldThing thing, EffectKind kind, int subtype, float upTo = AllValues, bool notify = true)
    {
        foreach (var e in thing.Effects.All.ToArray())
            if (e.Kind == kind && e.Subtype == subtype && e.Value <= upTo) Remove(thing, e, notify);
    }

    /// <summary>The "every value" the AI passes to <see cref="RemoveKind"/> (0x497423fe).</summary>
    public const float AllValues = 999999.9f;

    /// <summary>
    /// Every effect off — except, unless <paramref name="worn"/> is set, those from gear
    /// being worn (`EI_FLAG_WORN`). Death, a revive and a thing leaving the
    /// world all call it with the worn ones kept.
    /// </summary>
    public static void RemoveAll(WorldThing thing, bool worn = false)
    {
        // With the worn ones this is the whole list's clear, which passes the network's 1
        // and so is not refused on a player's machine; without, the take-off passes 0, which is.
        foreach (var e in thing.Effects.All.ToArray())
            if (!e.Spent && (worn || (e.Flags & Effect.Worn) == 0)) Remove(thing, e, notify: true, network: worn);
    }

    /// <summary>
    /// Every `DiesWithCreator` effect a dying character put on anybody here removed (the
    /// end of `rpgActor::Kill`). The engine walks the characters of the dying one's world.
    /// </summary>
    public static void RemoveCreatedBy(WorldThing creator)
    {
        if (creator.Place is not { } place) return;
        foreach (var other in place.Snapshot)
        {
            if (!other.IsActor) continue;
            foreach (var e in other.Effects.All.ToArray())
                if (e.CreatorGuid == creator.Guid && e.Definition.Has(EffectFlags.DiesWithCreator))
                    Remove(other, e, notify: true);
        }
    }

    /// <summary>
    /// Every effect a particular item put on, which is what taking it off does. Unlike
    /// <see cref="RemoveCreatedBy"/> this ignores <see cref="EffectFlags.DiesWithCreator"/>: worn
    /// gear's effects last exactly as long as the gear is worn.
    /// </summary>
    public static void RemoveFrom(WorldThing thing, Item source)
    {
        foreach (var e in thing.Effects.All.ToArray())
            if (!e.Spent && ReferenceEquals(e.Source, source)) Remove(thing, e, notify: true);
    }

    /// <summary>
    /// `ExpandActorEffect`: appending adds the duration to the end the
    /// instance already has, **whatever that is** — a permanent effect's 0 becomes a moment a few
    /// seconds into the game, long past, and the next tick takes it off; otherwise the end is set
    /// to now plus the duration.
    /// </summary>
    public static void Expand(Effect effect, int durationMs, bool append, int now) =>
        effect.Until = append ? effect.Until + durationMs : now + durationMs;

    // ---- the tick -----------------------------------------------------------------------------

    /// <summary>
    /// One character's effects, one frame. In the engine's own order,
    /// which matters:
    ///
    /// <list type="number">
    /// <item>reap everything removed since the last tick, freeing its script;</item>
    /// <item>walk the rest: remove — with all the consequences — anything whose time is up, and
    /// anything already folded into the base, so an instant effect lives exactly one frame;</item>
    /// <item>set the icon's blink for the last five seconds of anything that ends;</item>
    /// <item>pay out anything due to tick, and set its next tick from the definition.</item>
    /// </list>
    ///
    /// Two details the engine leaves as they are. The walk does not re-test the removed bit, so an
    /// over-time effect removed earlier in the same walk (a death partway through the list) still
    /// pays out if it is due. And a tick that lands late is not caught up: the next one is measured
    /// from whenever it was noticed, so a frame spike costs the effect a tick.
    /// </summary>
    public static void Tick(WorldThing thing, int now, Rng rng, Anim.MotionSets? motions = null)
    {
        var effects = thing.Effects;
        effects.Reap(thing.Place?.ScriptObjects is { } scripts ? scripts.Free : null);

        // By index: a script's `effectended` may put another effect on, and the engine's linked
        // list would walk onto it.
        for (int i = 0; i < effects.All.Count; i++)
        {
            var effect = effects.All[i];
            if (effect.Until != 0 && (uint)effect.Until <= (uint)now) { Remove(thing, effect, notify: true); continue; }
            if (effect.Folded) { Remove(thing, effect, notify: true); continue; }

            effect.Fade = Blink(effect, now);

            if (effect.NextTick == 0 || (uint)now < (uint)effect.NextTick) continue;
            switch (effect.Kind)
            {
                case EffectKind.BaseStatOverTime when effect.Subtype < Actor.Stats:
                    // Through the clamp. `ef_decreasefrenzy` takes 2 off frenzy every two seconds
                    // for every vampire alive, for ever; without the floor it runs to −infinity,
                    // and with it the effect is a no-op until a fight puts frenzy up.
                    if (thing.Actor is { } a) a.SetBaseStat(effect.Subtype, a.BaseStat[effect.Subtype] + effect.Value);
                    Recompute(thing, EffectKind.Stat, effect.Subtype);
                    break;
                case EffectKind.BaseSoakOverTime when effect.Subtype < Actor.Soaks:
                    if (thing.Actor is { } s) s.BaseSoak[effect.Subtype] += effect.Value;
                    Recompute(thing, EffectKind.Soak, effect.Subtype);
                    break;
                // Damage over time is dealt by whoever cast it, as hit kind 4 — "an effect" — with
                // the definition's subtype as the damage type. That is how a burning character's
                // death is credited to the one who set the fire.
                case EffectKind.DamageOverTime:
                    Damage.Apply(thing, CreatorOf(thing, effect), HitKind.Effect, (DamageType)effect.Subtype,
                                 effect.Value, rng, now, motions: motions);
                    break;
                case EffectKind.HealOverTime:
                    Heal(thing, effect.Value);
                    break;
            }
            effect.NextTick = effect.Definition.TickMs + now;
        }
    }

    /// <summary>
    /// The icon's blink: full (200) unless the effect ends, may blink
    /// (`effects.ned` flag 0x1 says it must not), and is within five seconds of its end. Then the
    /// clock's `now % 500` decides: over the first 50 ms it ramps from 200 down to 0, it stays out
    /// until 250, ramps back up to 200 by 300, and is full for the rest — two icon flashes a second.
    /// </summary>
    static float Blink(Effect effect, int now)
    {
        var blink = Rules.GameRules.Current.Time.EffectIconBlink;
        if (effect.Until == 0 || effect.Definition.Has(EffectFlags.NoBlink) || (uint)(effect.Until - blink[0]) > (uint)now)
            return Effect.FullFade;
        uint phase = (uint)now % (uint)blink[1];
        if (phase <= 250) return phase <= BlinkRamp ? phase * BlinkStep * -Effect.FullFade + Effect.FullFade : 0f;
        return phase < BlinkBack ? (phase - 250) * BlinkStep * Effect.FullFade : Effect.FullFade;
    }

    /// <summary>How long before its end an icon starts to blink, and the period it blinks at.</summary>
    public const int BlinkMs = 5000;
    public const uint BlinkPeriod = 500;
    /// <summary>The ramp: 50 ms, in steps of 0.02, back up by 300.</summary>
    const float BlinkRamp = 50f, BlinkStep = 0.02f, BlinkBack = 300f;

    // ---- the recompute ------------------------------------------------------------------------

    /// <summary>
    /// Pushes everything of one kind and subtype into the character's row. An
    /// **instant** kind writes the base row (through its clamp) and then runs again as its
    /// **modified** partner, so the current row is always base-plus-live; the current stat goes
    /// through `SetStat` (<see cref="Stats.Set"/>), which is what makes a frenzy effect start a
    /// frenzy and a drained human die.
    /// </summary>
    public static void Recompute(WorldThing thing, EffectKind kind, int subtype)
    {
        if (thing.Actor is not { } actor) return;
        while (true)
        {
            float value = thing.Effects.Value(kind, subtype);
            switch (kind)
            {
                case EffectKind.BaseStatInstant when subtype < Actor.Stats:
                    actor.SetBaseStat(subtype, actor.BaseStat[subtype] + value);
                    kind = EffectKind.Stat;
                    continue;
                case EffectKind.Stat when subtype < Actor.Stats:
                    Stats.Set(thing, subtype, actor.BaseStat[subtype] + value);
                    return;
                // Setting a base soak (also reached from the SetActorBaseSoak native): 1.1
                // refreshes the character sheet afterwards only if the actor is being watched;
                // the sheet reads soak live here instead.
                case EffectKind.BaseSoakInstant when subtype < Actor.Soaks:
                    actor.BaseSoak[subtype] = actor.BaseSoak[subtype] + value;
                    kind = EffectKind.Soak;
                    continue;
                case EffectKind.Soak when subtype < Actor.Soaks:
                    actor.Soak[subtype] = actor.BaseSoak[subtype] + value;
                    return;

                // The two multipliers the engine keeps on the character rather than folding on
                // demand, and the limp that comes with one of them: **MOVESPEED is what sets
                // `THING_AF2_LIMPING`**. The recompute compares the fold with 0.8 and clears the bit only
                // when it is *above* — at exactly 0.8 the character limps.
                case EffectKind.MoveSpeed:
                    actor.MoveSpeedScale = value;
                    if (value > Rules.GameRules.Current.Wounds.LimpBelowMoveSpeed) actor.Flags2 &= ~Actor.Flag2.Limping;
                    else actor.Flags2 |= Actor.Flag2.Limping;
                    return;
                case EffectKind.AttackSpeed:
                    actor.AttackSpeedScale = value;
                    return;

                // How hard it is to notice this character, and how well it notices others — the two
                // bytes `SENSE` and `HIDE` write, truncated to a byte.
                case EffectKind.Hide:
                    actor.Stealth = unchecked((byte)(int)value);
                    return;
                case EffectKind.Sense:
                    actor.Perception = unchecked((byte)(int)value);
                    return;

                // The model's own alpha and scale, when it has a model: the fold,
                // or 1 when the fold is exactly 0.
                case EffectKind.ModelAlpha:
                    if (thing.Animation is not null || thing.Model.Length > 0) thing.ModelAlpha = value != 0f ? value : 1f;
                    return;
                case EffectKind.ModelScale:
                    if (thing.Animation is not null || thing.Model.Length > 0) thing.ModelScale = value != 0f ? value : 1f;
                    return;

                // IMMOBILIZE is `THING_AF2_RESTRAINED`: above zero, held.
                case EffectKind.Immobilize:
                    if (value > 0f) actor.Flags2 |= Actor.Flag2.Restrained;
                    else actor.Flags2 &= ~Actor.Flag2.Restrained;
                    return;
            }
            return;
        }
    }

    // ---- healing ------------------------------------------------------------------------------

    /// <summary>Frenzy a heal takes off for each point of health asked (1.1's 1: the frenzy effect's value is the amount asked).</summary>
    public const float HealCalmsFrenzy = 1f;

    /// <summary>
    /// `rpgActor::Heal`. Nothing for a character under 1 health — the dead and the dying
    /// are not healed. Otherwise health goes up to the maximum and no further, and then:
    /// <list type="bullet">
    /// <item>a vampire or ghoul loses frenzy — `ef_increasefrenzy` by value **minus the amount
    /// asked for**, not the amount that fitted;</item>
    /// <item>the wound penalties are redone when the wound level changed (<see cref="Wounds"/>).</item>
    /// </list>
    /// Answers how much health was actually gained.
    /// </summary>
    public static float Heal(WorldThing thing, float amount)
    {
        var rules = Rules.GameRules.Current.Healing;
        if (thing.Actor is not { } actor || actor.Health < rules.HealFloor) return 0f;
        float was = actor.Health;
        actor.Health = Math.Min(actor.MaxHealth, actor.Health + amount);
        if (actor.Type is ActorType.Vampire or ActorType.Ghoul)
            Apply(thing, "ef_increasefrenzy", 0, durationMs: 1, value: -amount * rules.HealCalmsFrenzy);
        Wounds.Changed(thing, was);
        Diagnostics.Journal.Current?.Write(thing.Place?.Milliseconds ?? 0, Diagnostics.JournalKind.Heal,
            ("who", Diagnostics.Watch.Name(thing)), ("asked", amount), ("gained", actor.Health - was),
            ("hp", actor.Health), ("of", actor.MaxHealth));
        return actor.Health - was;
    }
}
