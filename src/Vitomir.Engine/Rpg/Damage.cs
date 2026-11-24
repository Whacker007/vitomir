// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taking a blow, and what happens when the last one lands.
//
// `rpgActor::Damage` is the one door every kind of harm goes through —
// a swing, a bullet, an explosion, a poison ticking away. It refuses, soaks, subtracts, and then
// either runs the hit reaction or calls `rpgActor::Kill`. Death itself, the corpse
// timers and what is left on the floor are elsewhere.
using Diag = Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// Where a blow came from. The engine passes this alongside the damage type, and several rules
/// turn on it: gibs fly off a melee hit or an explosion but not a shot, and only a melee hit can
/// take a head off.
/// </summary>
public enum HitKind
{
    /// <summary>A script's `DamageActor` (hit kind 0).</summary>
    Script = 0,
    /// <summary>A melee weapon.</summary>
    Melee = 1,
    /// <summary>Something that was fired or thrown.</summary>
    Projectile = 2,
    /// <summary>A blast.</summary>
    Explosion = 3,
    /// <summary>An effect, instant or over time.</summary>
    Effect = 4,
    /// <summary>
    /// Drained: the kind a feed that empties its victim kills with, and the kind a human
    /// whose blood reaches 0 dies of.
    /// </summary>
    Drain = 5,
}

/// <summary>What one application of harm came to.</summary>
/// <param name="Taken">How much health was actually lost, after the soak.</param>
/// <param name="Killed">Did it take the character down?</param>
/// <param name="Refused">Was the character not harmable by this at all?</param>
/// <param name="Frenzy">
/// How much `ef_increasefrenzy` the blow put on — nothing unless the target is a vampire or a
/// ghoul (<see cref="Combat.FrenzyFromDamage"/>). Applied by the damage path itself and
/// reported here as well.
/// </param>
public readonly record struct Harm(float Taken, bool Killed, bool Refused, float Frenzy = 0f);

/// <summary>Applying damage, and dying of it.</summary>
public static class Damage
{
    /// <summary>
    /// What a character is immune to before any soak is rolled:
    /// <list type="bullet">
    /// <item>THING_AF_INVUL — nothing at all.</item>
    /// <item>THING_AF_INVULTOPHYSICAL — NORMAL, LETHAL and AGGRAVATED.</item>
    /// <item>A human or a monster takes no SUN and no FAITH damage.</item>
    /// <item>A ghoul takes no SUN and no FAITH damage either.</item>
    /// </list>
    /// A character already at less than <see cref="Combat.DeathThreshold"/> health is past caring.
    /// </summary>
    public static bool Refuses(Actor actor, DamageType type)
    {
        var r = Rules.GameRules.Current.Combat;
        if (actor.Health < r.DeathThreshold) return true;
        if (actor.Has(Actor.Flag.Invulnerable)) return true;
        if (actor.Has(Actor.Flag.InvulnerableToPhysical)
            && type is DamageType.Normal or DamageType.Lethal or DamageType.Aggravated) return true;
        bool holyOrSun = type is DamageType.Sun or DamageType.Faith;
        if (holyOrSun && Array.IndexOf(r.SunAndFaithImmuneTypes, (int)actor.Type) >= 0) return true;
        return false;
    }

    /// <summary>
    /// Takes a blow, in the engine's own order: refuse, let the script have it, soak, scale, then
    /// subtract and either shake the character or kill it.
    /// </summary>
    /// <param name="skipSoak">
    /// Whether soak is skipped: some blows apply an amount that has already been reduced.
    /// </param>
    public static Harm Apply(WorldThing target, WorldThing? attacker, HitKind kind, DamageType type,
                             float amount, Rng rng, int nowMs = 0,
                             bool skipSoak = false,
                             Anim.MotionSets? motions = null)
    {
        // A player's machine in a network game hurts nobody: the host decides every blow, and its
        // snapshot carries the health and the death (ours, after 1.1's design
        // in which the client applies the host's hit and death messages, 0xd and
        // 0xf). What flies and bursts there is a picture of what the host's does.
        if (Net.NetOrders.Mode(target.Place) == Net.NetMode.Client) return new Harm(0f, false, true);
        // A container's damage is its own, and a short one: no soak, no frenzy, no dying
        // — just health, and an opening at zero.
        if (target.Type == ThingType.Container)
        {
            float before = target.ContainerHealth;
            Container.Damage(target, amount, attacker, motions);
            return new Harm(MathF.Max(0f, before - target.ContainerHealth), false, false);
        }
        if (target.Actor is not { } actor) return new Harm(0f, false, true);
        if (Refuses(actor, type)) return new Harm(0f, false, true);

        // The character's scripts have the blow first, and their answer is the blow
        // (`damaged`; Rpg.DamagedMessage); at or below 0 it does nothing.
        amount = DamagedMessage.Offer(target, attacker, amount, type);
        if (amount <= 0f) return new Harm(0f, false, false);

        if (!skipSoak)
        {
            // A soak other than 0 goes to the character's scripts, which may change it
            // (`soaked`; Rpg.SoakedMessage).
            amount -= SoakedMessage.Offer(target, attacker, amount, type, Combat.Soak(target, type, rng));
            // Soaked away entirely: the blow costs nothing, and a character with a shield up may
            // show it (Block).
            if (amount <= 0f)
            {
                if (Rules.GameRules.Current.Wounds.ShieldBlockOnFullSoak) Block(target, actor, kind, nowMs);
                return new Harm(0f, false, false);
            }
        }

        // A party member shot by somebody outside the party takes three-quarters of it.
        if (target.Type == ThingType.Player && attacker is { Type: not ThingType.Player })
            if (kind == HitKind.Projectile) amount *= Rules.GameRules.Current.Combat.PartyShotShare;
        // And a Cappadocian takes half again as much from FAITH.
        if (type == DamageType.Faith && actor.ClanId == CappadocianClan) amount *= Rules.GameRules.Current.Combat.CappadocianFaithMultiplier;

        float was = actor.Health;
        actor.Health -= amount;
        if (actor.Health >= Rules.GameRules.Current.Combat.DeathThreshold)
        {
            // A vampire or a ghoul is shaken by being hurt — `ef_increasefrenzy` by value, before
            // anything else the blow does — and everybody else just hurts.
            float frenzy = actor.Type is ActorType.Vampire or ActorType.Ghoul
                ? Combat.FrenzyFromDamage(actor, type, amount) : 0f;
            if (frenzy != 0f) EffectRuntime.Apply(target, "ef_increasefrenzy", 0, durationMs: 1, value: frenzy);
            // The hit effects, all for a character in the world: the noises —
            // `damage_<type>` and `vox_damage_<type>`, for any blow over one point, unless the
            // character flinched within the last 3.5 s — and the flinch roll, a head that may come
            // off, the loose chunk, and what the wound shows by its kind
            // (the sparks, the flames, the frost, the blood; WoundEffects) — HitReaction.
            HitReaction.React(target, actor, attacker, kind, type, amount, rng, nowMs);
            // Its mind hears about it: who did it, and how much.
            if (target.Mind is { } mind && target.Place?.Minds is { } minds) minds.Damaged(mind, attacker, amount);
            // And the wound ladder: a new rung, or any loss at all, lays the penalties again.
            Wounds.Changed(target, was);
            Net.HostSeen.Struck(target, attacker, kind, type, amount, killed: false, 0f);       // on a host
            return new Harm(amount, false, false, frenzy);
        }

        Kill(target, attacker, kind, type, nowMs, amount, motions, rng);        // which tells the players' machines
        return new Harm(amount, true, false);
    }

    /// <summary>`MOTION_SHIELD`, the block a soaked blow plays (0x70).</summary>
    public const int ShieldMode = 0x70;

    /// <summary>
    /// **The block** (read in a comparison with 1.1): a **melee** blow soaked away
    /// entirely, on a character with something in its **off hand** (a shield or a
    /// torch) that is not busy, not dead and not restrained, plays <see cref="ShieldMode"/> on the
    /// action channel at thirty frames a second. Only the motion: the
    /// action, the busy time and the flinch clock are left as they were, so the character carries on
    /// with whatever it was doing. A set without the mode plays nothing. Answers whether it played.
    /// </summary>
    public static bool Block(WorldThing thing, Actor actor, HitKind kind, int nowMs)
    {
        if (kind != HitKind.Melee || actor.Offhand is null) return false;
        if ((uint)actor.BusyUntil > (uint)nowMs) return false;
        if (actor.Has(Actor.Flag.Dead) || actor.Has2(Actor.Flag2.Restrained)) return false;
        return Actions.PlayAction(thing, ShieldMode) >= 0;
    }

    /// <summary>How long a flinch keeps a character quiet.</summary>
    public const int HurtQuietMs = 3500;

    /// <summary>
    /// The noises of being hurt and the flinch roll (the start of the damage reaction): for a blow of more than
    /// one point, unless the character flinched in the last 3.5 s — then the roll
    /// (<see cref="Flinch.Roll"/>) when there is a <paramref name="rng"/> to roll with.
    /// </summary>
    public static void Hurt(WorldThing target, Actor actor, DamageType type, float amount, int nowMs, Rng? rng = null)
    {
        var wounds = Rules.GameRules.Current.Wounds;
        if (!target.InWorld || !(amount > wounds.FlinchMinDamage)) return;
        if (actor.FlinchedAt != 0 && (uint)(actor.FlinchedAt + wounds.FlinchQuietMs) >= (uint)nowMs) return;
        Audio.Noise.Foley(target, Audio.FoleyEvent.Damage + (int)type);
        Audio.Noise.Foley(target, Audio.FoleyEvent.VoxDamage + (int)type);
        if (rng is not null) Flinch.Roll(target, actor, amount, rng, nowMs);
    }

    /// <summary>`ACTOR_CLAN_CAPPADOCIAN`, the only clan the combat code names.</summary>
    public const int CappadocianClan = 2;

    /// <summary>
    /// `rpgActor::Kill`. Marks the character dead and starts the corpse's two clocks. An
    /// invulnerable character cannot die at all, which is the engine's own first test.
    /// </summary>
    public static bool Kill(WorldThing thing, WorldThing? killer, HitKind kind, DamageType type, int nowMs,
                            float damage = 0f, Anim.MotionSets? motions = null, Rng? rng = null)
    {
        if (thing.Actor is not { } actor) return false;
        if (actor.Has(Actor.Flag.Invulnerable)) return false;

        // The action the character was in the middle of decides several things below, and it is
        // read before the death clears it.
        var doing = actor.Action;

        actor.Flags |= Actor.Flag.Dead;
        actor.Health = 0f;

        // **The script hears it first** (before anything else the death does): the
        // thing's own script and every capture, `killed(guid, killerGuid, captureId)`. A script that
        // clears the dead flag has refused the death, and nothing more happens. Then
        // every effect comes off but worn gear's.
        thing.Place?.Killed?.Invoke(thing, killer);
        if (!actor.Has(Actor.Flag.Dead)) return false;
        EffectRuntime.RemoveAll(thing);
        // **The weapon goes out of sight** — the hide bit on the render model's weapon (`model+8`,
        // the model's bit 8, which `EnableActorWeapon` writes) unless WEAPONOVERRIDE or
        // WEAPONNOHANDS, and again, unconditionally, at the head of the death effects,
        // which every death reaches. So a body lies empty-handed; it is still carrying the weapon,
        // and a revive shows it again. Read in a comparison with 1.1.
        actor.WeaponHidden = true;

        // The death's noises: whatever the character was saying is cut off — by the channel its
        // mouth remembers, which is only ever right for a conversation line or a `PlayVoice`
        // (the mouth is handed a stale id) — then `vox_death_<type>` and `death_<type>`, and a
        // party member's death rings the interface's own bell.
        if (thing.Place?.Audio is { } audio)
        {
            if ((uint)nowMs < (uint)thing.MouthBusyUntil && thing.MouthChannel > 0) audio.Mixer.Stop(thing.MouthChannel);
            audio.Thing(thing, Audio.FoleyEvent.VoxDeath + (int)type);
            audio.Thing(thing, Audio.FoleyEvent.Death + (int)type);
            if (thing.Type == ThingType.Player) audio.Ui(Audio.FoleyEvent.StMessage1);
        }
        // Then a walking character is stopped through the walker's own stop:
        // its `stopmove` and scuff, before the death's animation (WalkerStop).
        WalkerStop.OnDeath(thing);
        // And the message log, in red: a vampire "enters torpor", anybody else "dies".
        // Only a PLAYER thing — a party member — is announced.
        if (thing.Type == ThingType.Player && thing.Place?.Session is { } session)
            session.Messages.Post(actor.Type == ActorType.Vampire && Rules.GameRules.Current.Death.TorporIsDeathForVampires ? "RPG_DIEDV" : "RPG_DIED",
                                  "%P" + session.NameOf(thing), Ui.MessageLog.Red);

        // The party member the player has selected dying ends any shop: the
        // pane shuts and the shopkeeper's script hears it, as though Done had been pressed.
        if (thing.Place is { Session.Shops: { Active: not null } shops } here && ReferenceEquals(thing, here.Steered))
            shops.End();
        // And control passes to the next member still standing (the party's own
        // SelectNext with the dead passed over): with a companion alive, the game goes on through
        // them. Nobody left is the party down — the search itself asks for the end.
        if (thing.Place is { Minds: not null } place && ReferenceEquals(thing, place.Selected))
            place.SteerNext(skipDead: true);

        bool party = thing.Type == ThingType.Player;
        actor.FadeAt = nowMs + (party ? Rules.GameRules.Current.Death.PartyCorpseFadeMs : Rules.GameRules.Current.Death.CorpseFadeMs);
        // A party member's body is never removed, and neither is a pedestrian's; everybody else's
        // goes at 35 s. The pedestrian test is the mind's mode at this instant, so a civilian
        // killed in a fight is cleaned up like any other combatant.
        if (!party && !IsPedestrian(thing)) thing.ExpireAt = nowMs + Rules.GameRules.Current.Death.CorpseExpireMs;

        // The death effects throw their chunks first, while there is still a body to
        // throw them off — the gib kill below destroys it.
        using (Net.MadeHere.Everywhere(thing)) SprayOnDeath(thing, actor, kind, damage, rng ?? Fallback);   // each machine's own

        // **The death animation is chosen and played first**, and the engine's order is not an
        // accident: the mode it settles on — after the fallback for a set that has not got the one
        // it wanted — is the same number the skeleton death is then handed, so
        // the skeleton falls the way the body fell.
        int mode = Fall(thing, doing, damage, motions, type);
        // A character killed in mid-air gets nothing more of the death effects (the
        // switch on its action ends them for THROWN) — no dust, no gib, no wound effects.
        bool whole = doing != ActionMode.Thrown;
        // **The skeleton death**, here inside the death effects as in 1.1 —
        // before the cancel, the drop and the experience, so its smoke's rolls come before the
        // drop's (a comparison with 1.1: Vitomir dusted last, and in mid-air, and with blood off). The dust
        // itself declines with blood off, and the body is then left like anybody's.
        bool dusted = whole && !Gore.NoBlood && Dusts(thing, id => thing.Place?.Templates?.Has(SkeletonTemplate(id)) ?? true);
        Net.MadeHere.Died(thing, whole, dusted);                        // for the players' machines' own
        if (dusted)
        {
            // The skeleton death cancels the corpse fade and takes the body's alpha to nothing in a
            // second (the model's alpha to 0 over 1000 ms). The body itself stays,
            // invisible, until its ordinary expiry — and the kill sets the fade clock again after
            // the death effects (below), so the zero does not last.
            actor.FadeAt = 0;
            actor.Dusted = true;
            thing.Model3D.SetAlpha(0f, Rules.GameRules.Current.Death.CorpseFadeDurationMs[1]);
            Dust(thing, actor, nowMs, mode, motions, rng ?? Fallback);
        }
        // **The killing blow's wound effects** (read in a comparison with 1.1): a body
        // left whole — not killed in mid-air (the death effects end at once for that), not turning
        // to dust, not gibbed — gets the hit's switch again, each effect living 8 s: an
        // electrocuted body crawls with sparks, a burnt one burns, and an ordinary blow draws its
        // blood. Here, inside the death effects, before the cancel, the drop and the experience.
        else if (whole && !Gibs(actor, damage))
            using (Net.MadeHere.Everywhere(thing)) WoundEffects.Show(thing, killer, type, damage, rng ?? Fallback, nowMs, death: true);
        // Whatever it was doing is over, forced (the cancel with (0, 1)): a feeder lets go of
        // its victim, a victim is let go, a cast is dropped.
        Actions.Cancel(thing, force: true);
        // **What it leaves**: its DROPITEM, else one roll of its treasure
        // class, on the floor beside the body — now, at the death, not when the body goes. A
        // gibbed body still drops, because the engine's gib only marks it for removal; here the
        // drop is placed before the gib below takes the body out of the world. What it carried
        // and wore stays on the corpse in single player.
        Loot.Drop(thing);
        // A kill by a party member — directly, or by its shot — is worth the dead character's
        // experience to every living member of the party.
        if (Credit(killer) is { Type: ThingType.Player } && actor.Xp != 0 && thing.Place is { } there)
            there.AwardPartyExperience(actor.Xp, there.Session is { } game ? game.RankGained : null, near: thing);
        // Its mind's last word: event 5, carrying the killer.
        if (thing.Mind is { } dying && thing.Place?.Minds is { } everyone) everyone.Died(dying, killer);
        // **The fade clock is set here, after the death effects** (`now + 30000` for any
        // but a player thing) — so the dust's zero is overwritten, and **a dusted body
        // stays a `TARGET DEAD` target for thirty seconds** (the target test asks the fade time), invisible.
        // Vitomir set the clock before the death effects (above), which is the same for every body
        // but a dusted one; this puts the dusted one right (a comparison with 1.1's gap).
        if (dusted && !party) actor.FadeAt = nowMs + Rules.GameRules.Current.Death.CorpseFadeMs;

        if (whole && !dusted && Gibs(actor, damage))
        {
            Gib(thing, actor, rng ?? Fallback);
        }

        // Everything this character put on anybody that is meant to die with it.
        EffectRuntime.RemoveCreatedBy(thing);

        // Killing certain people costs the killer Humanity — the price on terrorising a city. The
        // **last** thing the death does in single player, so its message follows any
        // experience's; and the killer is the one **credited** with the kill
        // (the shooter of an arrow, bullet or discipline shot, the maker of a blast), not the shot
        // itself — a comparison with 1.1's fix; Vitomir asked the shot, which has no Humanity.
        float humanity = Net.HostSeen.Humanity(killer);
        if (Credit(killer) is { Actor: { } k } credited && actor.Has(Actor.Flag.HumanityLoss)
            && LoseHumanity(k, Rules.GameRules.Current.Frenzy.HumanityPerKill, Net.NetAwards.TracksHumanity(credited.Place)) > 0f)
            HumanityLost(credited);
        // A network host tells the players' machines of **every** death that gets this far
        // (message 0xf), a script's `KillActor` as much as a blow's — earlier only a blow's was told.
        Net.HostSeen.Struck(thing, killer, kind, type, damage, killed: true, humanity);

        Diag.Journal.Current?.Write(nowMs, Diag.JournalKind.Death,
            ("who", Diag.Watch.Name(thing)),
            ("by", killer is null ? null : Diag.Watch.Name(killer)),
            ("kind", kind), ("type", type), ("damage", damage),
            ("party", party), ("dusts", actor.Dusted),
            ("animation", mode == 0 ? "none" : Formats.NmsMotionSet.BodyModes[mode]),
            ("fadeAt", actor.FadeAt), ("expireAt", thing.ExpireAt));
        return true;
    }

    /// <summary>
    /// Who a kill is credited to: a character itself; a projectile's owner;
    /// an explosion's; anything else nobody.
    /// </summary>
    static WorldThing? Credit(WorldThing? killer) => killer switch
    {
        null => null,
        { IsActor: true } => killer,
        { Type: ThingType.Projectile } => Projectiles.Owner(killer),
        { Type: ThingType.Explosion } => killer.SpawnedBy != 0 ? killer.Place?.Thing(killer.SpawnedBy) : null,
        _ => null,
    };

    // --- the death animation ----------------------------------------------

    /// <summary>
    /// The six death modes the engine names, by the id it plays them with — indices into
    /// <see cref="NmsMotionSet.BodyModes"/>, which is the engine's own table (its initialiser
    /// fills its mode table, one slot per mode in index order).
    ///
    /// **Only three of the six can ever play.** No shipped motion set contains
    /// <c>deathdramatic</c>, <c>deathclawed</c> or <c>deahtsun</c> — and that last is the engine's
    /// own spelling, a typo for <c>deathsun</c>, so even a set that provided one could not be asked
    /// for it.
    /// </summary>
    public static class DeathMode
    {
        public const int Quick = 121, Slow = 122, Dramatic = 123, Sun = 124,
                         Staked = 125, Clawed = 126, Drained = 132;
    }

    /// <summary>
    /// Above this much damage in the killing blow the character drops where it stands rather than
    /// folding up slowly (19.0). Note which way round it is: the **big** hit gets the
    /// quick death.
    /// </summary>
    public const float QuickDeathDamage = 19f;

    /// <summary>
    /// Which death a character plays, in the engine's own order:
    /// <list type="number">
    /// <item>staked — <c>THING_AF_STAKED</c>;</item>
    /// <item>drained — it was in <see cref="ActionMode.FedOn"/> or <see cref="ActionMode.WristFedOn"/> — being fed on,
    /// at the neck or at the wrist; the feeder never plays it;</item>
    /// <item>quick — the killing blow was over <see cref="QuickDeathDamage"/>;</item>
    /// <item>the sun's — a killing blow of <see cref="QuickDeathDamage"/> or less whose damage type
    /// (the death effects' own argument, the one their switch plays the electric sparks for at 3) is
    /// <see cref="DamageType.Sun"/> or <see cref="DamageType.Faith"/> (5, or 6,
    /// gives <c>0x7c</c>). No shipped set has <c>deahtsun</c>, so it falls back to the slow death
    /// (<see cref="Fall"/>) — but a set that had the mode would play it;</item>
    /// <item>slow — everything else.</item>
    /// </list>
    /// </summary>
    public static int DeathModeFor(Actor actor, ActionMode doing, float damage, DamageType kind = DamageType.Normal) =>
        actor.Has(Actor.Flag.Staked) ? DeathMode.Staked
        : doing is ActionMode.FedOn or ActionMode.WristFedOn ? DeathMode.Drained
        : damage > Rules.GameRules.Current.Death.QuickDeathDamage ? DeathMode.Quick
        : kind is DamageType.Sun or DamageType.Faith ? DeathMode.Sun
        : DeathMode.Slow;

    /// <summary>
    /// Plays the death on the action channel. The modes are all <c>noloop endpause</c>, so the
    /// body holds the animation's last frame — which is what a corpse on the floor *is*; there is
    /// no separate lying-down pose anywhere in the game.
    ///
    /// A set that has not got the chosen mode falls back to <see cref="DeathMode.Slow"/>,
    /// and one that has not got that either simply keeps whatever it was playing.
    /// A character that was throwing somebody drops them first (<see cref="SpecialMove.Dropped"/>),
    /// and a character killed in mid-air after being thrown plays no death at all: its
    /// thrown motion plays on, and the `Throwdeath` tag lays the body down (<see cref="SpecialMove.Throwdeath"/>).
    /// </summary>
    /// <returns>
    /// The mode that actually played, or 0. **The caller needs this**, because the skeleton death
    /// below is handed the same number: the death resolves the fallback, plays it on the body, and
    /// then passes the *resolved* mode into the skeleton death so the skeleton falls the same way.
    /// </returns>
    public static int Fall(WorldThing thing, ActionMode doing, float damage, Anim.MotionSets? motions,
                           DamageType kind = DamageType.Normal)
    {
        if (thing.Actor is not { } actor) return 0;
        // Killed in mid-air after being thrown: the engine leaves before it gets this far.
        if (doing == ActionMode.Thrown) return 0;
        if (doing == ActionMode.Throwing) SpecialMove.Dropped(thing);

        actor.Action = ActionMode.None;
        actor.BusyUntil = 0;
        // Whatever it was walking towards, it is not going there. The mind has already stopped
        // thinking (the AI gate drops a dead character), so nothing would clear the route.
        thing.Mind?.Walker.Stop();
        if (motions is null || thing.Animation is not { } animation) return 0;

        int mode = DeathModeFor(actor, doing, damage, kind);
        var found = motions.Find(animation.Set, mode);
        if (found is null) { mode = DeathMode.Slow; found = motions.Find(animation.Set, mode); }
        if (found is not { } death) return 0;

        animation.Play(death.Mode, death.Animation, motions, onMotion: false, rate: Anim.ThingAnimation.Rate);
        return mode;
    }

    /// <summary>
    /// Whether this character turns to dust rather than leaving a body: a vampire that is not a
    /// party/multiplayer character, without THING_AF_NOSKELDEATH, whose SKELETON_ID names a
    /// `skeleton_&lt;n&gt;` template that exists. The caller supplies the last test, because only
    /// it knows the template set; with no test the id is assumed good.
    /// </summary>
    public static bool Dusts(WorldThing thing, Func<int, bool>? skeletonExists = null) =>
        thing.Actor is { } a
        && thing.Type != ThingType.Player
        && a.Type == ActorType.Vampire
        && !a.Has(Actor.Flag.NoSkeletonDeath)
        && (skeletonExists?.Invoke(a.SkeletonId) ?? true);

    /// <summary>
    /// The engine's inlined "is this a pedestrian": it has a mind, and either that mind is in
    /// PEDWANDER or its AI class uses the second search style. Used in exactly two places, both
    /// about corpses — skipping the expiry here, and teleporting one home on revive.
    /// </summary>
    public static bool IsPedestrian(WorldThing thing) =>
        thing.Mind is { } mind
        && (mind.Mode == AiMode.PedWander || mind.Class.SearchStyle == 1);

    /// <summary>
    /// Whether a discipline with `TARGET DEAD` may still be cast on this body: a party member for
    /// ever, anybody else only while the fade clock is still running. It stops the moment the fade
    /// *begins*, which is five seconds before the body disappears.
    /// </summary>
    public static bool CanBeRaised(WorldThing thing) =>
        thing.Actor is { Dead: true } a && (thing.Type == ThingType.Player || a.FadeAt != 0);

    /// <summary>
    /// `rpgActor::Revive`. Clears the death, both clocks and the stake — but not the
    /// beheading — and takes a dead pedestrian home first (<see cref="GoHome"/>); then, whether
    /// or not it was dead:
    /// <list type="bullet">
    /// <item>permanent frenzy is cleared and a frenzy is ended (<see cref="Frenzy.End"/>);</item>
    /// <item>every effect but worn gear's comes off, and the ones a character is born with go back
    /// on — `ef_decreasefrenzy` for a vampire **or a ghoul** (the birth only gives it to a vampire),
    /// the two regenerations for a human;</item>
    /// <item>frenzy to 0, health, blood and mana to their fractions of the maxima, and Humanity back
    /// up to 20 if it had fallen below;</item>
    /// <item>the limp, the model's alpha and the death pose go, and the locomotion comes back;</item>
    /// <item>the mind starts over (<see cref="Ai.Minds.Revived"/>): home mode, no target,
    /// thinking at once.</item>
    /// </list>
    /// </summary>
    /// <param name="sound">
    /// Whether the revived character makes the awakening noise (foley 0x4d, <c>DeathAwaken</c>) when
    /// its mouth is free — the script's `ReviveActor` asks for it (it passes 1).
    /// </param>
    public static void Revive(WorldThing thing, float healthFraction = 1f, float poolFraction = 1f, bool sound = false)
    {
        if (thing.Actor is not { } actor) return;
        if (actor.Has(Actor.Flag.Dead))
        {
            // **A pedestrian gets up where it was first placed**: the spawn
            // transform copied back over the live one, the sector found again under it, and its
            // collide shape put back to a cylinder (shape 2). Not a player.
            if (thing.Type != ThingType.Player && IsPedestrian(thing) && Rules.GameRules.Current.Death.PedestrianGoesHome) GoHome(thing);
            actor.Flags &= ~(Actor.Flag.Dead | Actor.Flag.Staked);
            thing.ExpireAt = 0;
            actor.FadeAt = 0;
            // DON'T RENDER off — what a conversation's end gives a fallen member (FallenListener).
            thing.RenderFlags &= ~FallenListener.DontRender;
            ShapeShift.End(thing);                                            // A corpse gets up in its own shape
        }
        actor.Dusted = false;
        actor.Flags2 &= ~Actor.Flag2.PermaFrenzy;
        if (thing.Mind is { Mode: AiMode.Frenzied }) Frenzy.End(thing);
        EffectRuntime.RemoveAll(thing);
        foreach (var born in Rules.GameRules.Current.Healing.Born(actor.Type)) EffectRuntime.Apply(thing, born, 0);
        int frenzy
 = (int)ActorStat.Frenzy, blood = (int)ActorStat.Blood, mana = (int)ActorStat.Mana;
        actor.SetBaseStat(frenzy, Rules.GameRules.Current.Death.ReviveDefaults);
        Stats.Set(thing, frenzy, Rules.GameRules.Current.Death.ReviveDefaults);
        // Through the health setter, which holds what it is given between 0 and the
        // maximum. The scripts' `ReviveActor(100, 100)` — `healthPct`, in a native that takes fractions —
        // asks for a hundred times the maximum and gets the maximum; earlier, Vitomir gave it all
        // (Christof at 30,000 of 300 after Etrius, seen in play).
        actor.Health = Math.Clamp(actor.MaxHealth * healthFraction, 0f, actor.MaxHealth);
        float b = actor.Stat[(int)ActorStat.BloodPool] * poolFraction;
        actor.SetBaseStat(blood, b);
        Stats.Set(thing, blood, b);
        float m = actor.Stat[(int)ActorStat.ManaPool] * poolFraction;
        actor.SetBaseStat(mana, m);
        Stats.Set(thing, mana, m);
        int humanity = (int)ActorStat.Humanity;
        if (actor.Stat[humanity] < Rules.GameRules.Current.Frenzy.HumanityFloorOnRevive)
        {
            actor.SetBaseStat(humanity, Rules.GameRules.Current.Frenzy.HumanityFloorOnRevive);
            Stats.Set(thing, humanity, Rules.GameRules.Current.Frenzy.HumanityFloorOnRevive);
        }
        actor.Flags2 &= ~Actor.Flag2.Limping;
        thing.ModelAlpha = 1f;
        actor.FadingFrom = 0;
        thing.Flags &= ~WorldThing.Flag.Remove;
        // The death pose comes off the action channel and the locomotion goes back on the motion
        // channel — the second of which is only an event, not a frame's work.
        thing.Animation?.StopAction();
        thing.Mind?.Walker.Reanimate();
        // The weapon the death hid is shown again — whether or not anything hid it.
        actor.WeaponHidden = false;
        // And the mind starts over — home mode, no target, thinking at once.
        if (thing.Mind is { } mind) thing.Place?.Minds?.Revived(mind);
        // Played here and not relayed (the foley's relay flag 0) — each player's machine plays its own
        // with the revive's message, which the revive sends last (`Net.SeenRevive`).
        if (sound && Audio.Noise.CanSpeak(thing)) using (Net.HostSeen.Local()) Audio.Noise.Foley(thing, Audio.FoleyEvent.DeathAwaken);
        Net.SeenRevive.Told(thing, healthFraction, poolFraction);
    }

    /// <summary>
    /// The revive's teleport: the twelve words of the spawn transform
    /// over the live one, the sector under the new position taken when it
    /// differs from the thing's hint, and the collide shape set to 2, a cylinder. The walker
    /// is put back on its ground where it now stands (ours: the engine's next step finds it).
    /// </summary>
    static void GoHome(WorldThing thing)
    {
        var place = thing.Place;
        long was = thing.Sector;
        thing.Position = thing.SpawnPosition;
        thing.Orientation = thing.SpawnOrientation;
        if (place?.Solid is { } solid && solid.SectorAt(thing.Position, (int)was) is var sector and >= 0 && sector != was)
        {
            thing.Sector = sector;
            place.Props?.Moved(thing, was, sector);
        }
        thing.Collide = CollideShape.Cylinder;
        thing.Locomotion?.Stop();
        thing.Locomotion?.Ground(snap: false);
        place?.Regions?.Moved(thing, was);
    }

    /// <summary>What a revive puts Humanity back up to when it is below it.</summary>
    public const float HumanityFloorOnRevive = 20f;

    /// <summary>
    /// Takes Humanity off a killer, the way the original does: only a **vampire** that
    /// carries `THING_AF_HUMANITYTRACK` is charged — in single player, and in a network game only on
    /// the host of a game made with Track Humanity (<paramref name="tracked"/>, `Net.NetAwards`). It is
    /// the **base** stat that moves. Returns what was actually taken.
    /// </summary>
    public static float LoseHumanity(Actor killer, float amount, bool tracked = true)
    {
        if (!tracked || !killer.Has(Actor.Flag.HumanityTrack)
            || Array.IndexOf(Rules.GameRules.Current.Frenzy.HumanityLossActorTypes, (int)killer.Type) < 0)
            return 0f;
        int i = (int)ActorStat.Humanity;
        // 1.1 answers a Humanity loss (and a console stat change, and player creation) by recomputing
        // all nineteen stats from base plus effects (nineteen calls to
        // `Recompute`); here base and live move together for the one stat that changed.
        killer.BaseStat[i] -= amount;
        killer.Stat[i] -= amount;
        return amount;
    }

    /// <summary>What one `THING_AF_HUMANITYLOSS` kill costs (5.0).</summary>
    public const float HumanityPerKill = 5f;

    /// <summary>
    /// Where Humanity counts as low for the voice (20): at or above it a loss is
    /// <c>vox_lostHumanity</c>, below it <c>vox_lowHumanity</c>.
    /// </summary>
    public const float LowHumanity = 20f;

    /// <summary>
    /// The rest of the Humanity loss once Humanity is taken: the message log says so — <c>RPG_HUMANITYLOST</c>,
    /// "%P lost some Humanity!", in the box's own colour — when the one who lost it is being steered,
    /// and the character says so when it is free to speak.
    /// </summary>
    public static void HumanityLost(WorldThing who)
    {
        if (who.Actor is not { } actor) return;
        if (who.Place is { } place && ReferenceEquals(who, place.Steered) && place.Session is { } session)
            session.Messages.Post("RPG_HUMANITYLOST", "%P" + session.NameOf(who));
        if (Audio.Noise.CanSpeak(who))
            Audio.Noise.Foley(who, actor.Stat[(int)ActorStat.Humanity] >= Rules.GameRules.Current.Frenzy.LowHumanityBark
                ? Audio.FoleyEvent.VoxLostHumanity : Audio.FoleyEvent.VoxLowHumanity);
    }

    /// <summary>
    /// A script setting Humanity outright (`SetActorStat`): nothing when it did not move;
    /// raised, the character says <c>vox_gainHumanity</c> when free to speak; lowered, the same as a
    /// kill's loss (<see cref="HumanityLost"/>), with the voice chosen by the new value.
    /// </summary>
    public static void HumanityChanged(WorldThing who, float was, float now)
    {
        if (was == now) return;
        if (was <= now)
        {
            if (Audio.Noise.CanSpeak(who)) Audio.Noise.Foley(who, Audio.FoleyEvent.VoxGainHumanity);
            return;
        }
        HumanityLost(who);
    }

    /// <summary>The corpse clocks, all in milliseconds (`rpgActor::Kill`).</summary>
    public static class Corpse
    {
        /// <summary>An ordinary body lies still this long, then fades.</summary>
        public const int FadeMs = 30_000;
        /// <summary>A party member's body starts fading after five seconds.</summary>
        public const int PartyFadeMs = 5_000;
        /// <summary>And an ordinary body is removed five seconds after the fade starts.</summary>
        public const int ExpireMs = 35_000;
        /// <summary>How long the fade itself takes, once it starts (4800.0).</summary>
        public const float FadeDurationMs = 4_800f;
        /// <summary>A dusting body fades in a second instead (1000.0).</summary>
        public const float DustFadeMs = 1_000f;
        /// <summary>How long the skeleton and its smoke last.</summary>
        public const int SkeletonMs = 5_000, SmokeMs = 1_000;
    }

    /// <summary>
    /// Whether a killing blow blows the body apart instead of leaving it: THING_AF_ALWAYSGIBKILL,
    /// or THING_AF_CANGIBKILL with more than <see cref="GibDamage"/> damage. A gibbed
    /// body is destroyed on the spot — no corpse, no outline, nothing to raise.
    /// </summary>
    public static bool Gibs(Actor actor, float damage) =>
        actor.Has(Actor.Flag.AlwaysGibKill)
        || (actor.Has(Actor.Flag.CanGibKill) && damage > Rules.GameRules.Current.Stake.GibKillDamage);

    /// <summary>What a can-gib character has to be hit for to come apart.</summary>
    public const float GibDamage = 80f;
    /// <summary>How many sub-pieces the kill asks for, twice each.</summary>
    public const int GibPieces = 8;
    /// <summary>The two damage thresholds that throw a loose chunk off a hit.</summary>
    public const float GibSprayFirst = 5f, GibSpraySecond = 19f;

    // --- what a death actually leaves behind -----------------

    /// <summary>The template a vampire's `SKELETON_ID` names — `skeleton_0`, `skeleton_1`, …</summary>
    public static string SkeletonTemplate(int id) => $"skeleton_{id}";

    /// <summary>The template one piece of a gib class is — `gib_4_11`, and so on.</summary>
    public static string GibTemplate(int gibClass, int piece) => $"gib_{gibClass}_{piece}";

    /// <summary>
    /// The skeleton death: the vampire's body fades out over a second and a
    /// **skeleton stands where it fell**, lasting five seconds before it goes too. The skeleton is
    /// an ordinary thing spawned from `skeleton_&lt;SKELETON_ID&gt;`, so it is subject to the same
    /// expiry as anything else.
    ///
    /// Then, on the body (read in a comparison with 1.1): its shadows go (render flags `&amp;= ~0xC`,
    /// DROP SHADOW and FULL SHADOW), it plays <see cref="DustNoise"/>, and **smoke comes
    /// off it**: for every bone of its skeleton but the root, one roll on the shared stream
    /// (<c>rand × 1/32767 &lt; 0.33</c>) hangs a
    /// <see cref="DustSmoke"/> on that bone, made at the body and attached with no offset and
    /// <c>0x8001</c>, gone in <see cref="Corpse.SmokeMs"/>. With no
    /// such template there are no rolls; with no model, no bones and no rolls.
    /// </summary>
    /// <returns>The skeleton, when one was spawned.</returns>
    public static WorldThing? Dust(WorldThing body, Actor actor, int nowMs,
                                   int mode = 0, Anim.MotionSets? motions = null, Rng? rng = null)
    {
        if (body.Place is not { } place) return null;
        var bones = place.Place(SkeletonTemplate(actor.SkeletonId), body.Position, body.Orientation, body.Sector);
        if (bones is null) return null;
        bones.ExpireAt = nowMs + (int)Rules.GameRules.Current.Death.SkeletonLifetimeMs[0];
        // **And it falls the way the body fell.** The skeleton death plays the mode it was handed on the
        // skeleton's own mixer, at the usual thirty frames a second. The five `skeleton_<n>`
        // templates exist for nothing else: their motion sets (`player_id_0death.nms` and its
        // four siblings) contain **the four death modes and nothing at all besides** —
        // `deathdrained`, `deathslow`, `deathquick`, `deathstaked` — plus the tag and mouth
        // sections every set carries.
        //
        // Without it the skeleton stands in its rest pose, upright, while the body it came out of
        // lies on the floor. Play showed exactly that: the skeleton that pops out for a
        // second when they turn to dust stayed up and frozen in place.
        if (mode != 0 && motions is not null && bones.Animation is { } animation
            && motions.Find(animation.Set, mode) is { } death)
            animation.Play(death.Mode, death.Animation, motions, onMotion: false,
                           rate: Anim.ThingAnimation.Rate);
        body.RenderFlags &= ~(Shadows.DropShadow | Shadows.FullShadow);
        Audio.Noise.Foley(body, DustNoise);
        if (place.Templates?.Has(DustSmoke) == true)
        {
            var r = rng ?? Fallback;
            int count = body.Animation?.BoneCount ?? 0;
            for (int bone = 1; bone < count; bone++)                                    // The root is passed over
            {
                if (!(r.Unit() < Rules.GameRules.Current.Death.SkeletonLifetimeMs[2])) continue;
                if (place.Spawn(DustSmoke, body) is not { } smoke) continue;
                Attachments.Attach(body, smoke, bone, System.Numerics.Vector3.Zero,
                                   System.Numerics.Matrix4x4.Identity, WoundEffects.AttachFlags);
                smoke.ExpireAt = nowMs + (int)Rules.GameRules.Current.Death.SkeletonLifetimeMs[1];
            }
        }
        return bones;
    }

    /// <summary>The smoke a dusting vampire gives off (`vampDeathSmoke`, a particle system).</summary>
    public const string DustSmoke = "vampDeathSmoke";

    /// <summary>The chance each bone past the root smokes (0.33).</summary>
    public const float DustSmokeChance = 0.33f;

    /// <summary>
    /// The noise of a dusting body (slot 0x46, 70) — <c>death_sun</c> in the foley's order,
    /// whatever killed it: the burning-up noise over the kill's own <c>death_&lt;type&gt;</c>.
    /// </summary>
    public const int DustNoise = Audio.FoleyEvent.Death + (int)DamageType.Sun;

    /// <summary>
    /// Gibbing, the tail of the death's looks: for each of the eight piece indices the engine asks for
    /// `gib_&lt;ACTOR_GIBCLASS&gt;_&lt;i&gt;` **twice** — sixteen spawns — and then destroys the
    /// body on the spot. **A gibbed body leaves nothing**: no corpse, no outline, nothing to raise.
    ///
    /// The loop always asks for 0 through 7 whatever the class has, and a template that is not
    /// there is simply skipped, so the real piece counts are the shipped ones: 8 for class 2, 10
    /// for class 3, 16 for class 4, 8 for class 5 — and **none at all for class 1**, which ships no
    /// `gib_1_*` templates, so a class-1 actor vanishes silently. That is the original's, and it is
    /// what makes the count worth asserting rather than assuming.
    /// </summary>
    /// <returns>How many pieces actually came off.</returns>
    public static int Gib(WorldThing body, Actor actor, Rng? rng = null)
    {
        if (body.Place is not { } place) return 0;
        var r = rng ?? Fallback;
        int made = 0;
        // A network host tells every player's machine first, whatever its own setting (the gib message with
        // 0x1b), and each throws its own chunks (Net.ClientSeen), so these are not sent.
        Net.MadeHere.Gibbed(body);
        using var ownChunks = Net.MadeHere.Everywhere(body);
        // The no-gore setting is tested again here, outside the loop, so it skips the
        // whole thing rather than refusing sixteen chunks one at a time. Same result either way.
        if (!Gore.NoBlood)
            for (int piece = 0; piece < Rules.GameRules.Current.Stake.GibPieces; piece++)
                for (int again = 0; again < 2; again++)
                    // **No bone tag.** The loop passes 0, MOTIONTAG_INVALID, which no motion set can
                    // declare — so every chunk comes off a bone picked at random and the sixteen of
                    // them scatter over the body instead of stacking at one joint.
                    if (Gore.Spray(body, GibTemplate(actor.GibClass, piece), Gore.NoTag, r) is not null) made++;
        // The body goes the same frame, through the same teardown as anything else, and it
        // goes whether or not a single chunk was thrown.
        place.Remove(body);
        return made;
    }

    // --- the gib spray ---------------------------------------

    /// <summary>
    /// Whether an ordinary blow throws a loose chunk (in the hit effects).
    ///
    /// **This is a roll, not a threshold** — it is the death path that uses the two damage
    /// thresholds, and the two were conflated in this document's first reading. What the engine
    /// tests here is `rand01 × health &lt; 2 × damage`, against the health the blow has *already*
    /// been subtracted from. So the same blow sheds a chunk more and more readily as the victim
    /// weakens: a scratch on somebody nearly dead is as likely to throw one as a heavy blow on
    /// somebody fresh. On a target at or below zero health it is certain, which is a case the hit
    /// path cannot reach because that target is dead.
    ///
    /// The three gates around the roll: the actor must not carry `NOGIBSPRAY` (`0x08000000`, 96
    /// templates), the blow must be **melee or an explosion** — a shot or an effect never sheds
    /// anything — and the actor must have a gib class.
    /// </summary>
    public static bool SpraysOnHit(Actor actor, HitKind kind, float damage, Rng rng)
    {
        if (actor.Has(Actor.Flag.NoGibSpray)) return false;
        if (kind is not (HitKind.Melee or HitKind.Explosion)) return false;
        // The roll comes **before** the gib-class test, so it is drawn even for an actor that could
        // never shed anything. Keeping the order keeps the sequence of random numbers the same.
        if (rng.Unit() * actor.Health >= damage + damage) return false;
        return actor.GibClass != 0;
    }

    /// <inheritdoc cref="SpraysOnHit"/>
    /// <summary>Throws it, and answers how many were made — 0 or 1.</summary>
    public static int SprayOnHit(WorldThing thing, Actor actor, HitKind kind, float damage, Rng rng) =>
        SpraysOnHit(actor, kind, damage, rng)
        && Gore.Spray(thing, GibTemplate(actor.GibClass, 0), Gore.HeadTag, rng) is not null ? 1 : 0;

    /// <summary>
    /// How many chunks the killing blow throws (in the death effects): one over
    /// <see cref="GibSprayFirst"/> damage and **another** over <see cref="GibSpraySecond"/>, so a
    /// blow over 19 throws two and a blow over 5 throws one. No roll here, and both are the same
    /// piece — `gib_&lt;class&gt;_0` — off the head.
    ///
    /// The same two gates as the hit path guard both, and the gib class is re-tested before each,
    /// which is the engine's own redundancy.
    /// </summary>
    public static int SpraysOnDeath(Actor actor, HitKind kind, float damage)
    {
        // The engine re-tests the gib class inside each of the two ifs; it cannot change between
        // them, so hoisting it changes nothing and no random number is drawn on this path at all.
        if (actor.Has(Actor.Flag.NoGibSpray) || actor.GibClass == 0) return 0;
        if (kind is not (HitKind.Melee or HitKind.Explosion)) return 0;
        var spray = Rules.GameRules.Current.Stake.GibSprayThresholds;
        return damage > spray[1] ? 2 : damage > spray[0] ? 1 : 0;
    }

    /// <inheritdoc cref="SpraysOnDeath"/>
    /// <summary>Throws them, and answers how many were made.</summary>
    public static int SprayOnDeath(WorldThing thing, Actor actor, HitKind kind, float damage, Rng rng)
    {
        int want = SpraysOnDeath(actor, kind, damage), made = 0;
        for (int i = 0; i < want; i++)
            if (Gore.Spray(thing, GibTemplate(actor.GibClass, 0), Gore.HeadTag, rng) is not null) made++;
        return made;
    }

    /// <summary>
    /// The generator a caller that has none of its own falls back to — the same arrangement
    /// <see cref="World.Simulation"/> makes for the effect tick. Everything that comes through
    /// <see cref="Apply"/> passes the combat's own.
    /// </summary>
    static readonly Rng Fallback = new(1);
}
