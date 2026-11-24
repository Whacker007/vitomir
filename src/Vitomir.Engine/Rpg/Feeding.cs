// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Feeding. There is no feed verb in this game: feeding is casting the `Feed` discipline, whose
// script calls the `Feed` native, which is this file's order.
// The feeder walks up (ACTION 7), bites — at the neck, or at a friend's wrist — and then
// drains a little every second until the victim is empty, the donor too low, or somebody lets go.
//
//   7   walking up                   with a tick
//   8   feeding at the neck          feeder tick           9   fed on at the neck    victim tick
//   11  feeding at a friend's wrist  feeder tick           12  giving at the wrist   victim tick
using System.Numerics;
using Microsoft.Extensions.Logging;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Rpg;

/// <summary>The feed, from the order to the release.</summary>
public static class Feeding
{
    static readonly Microsoft.Extensions.Logging.ILogger log = Diagnostics.Diag.For("Vitomir.Rpg");

    /// <summary>The motion modes the feed plays, all at thirty frames a second (`Codex.java`'s MOTION_*).</summary>
    public static class Motion
    {
        public const int Feed = 119, Embraced = 120, Feeding = 128, Draining = 129, FeedRelease = 130,
                         FeederDrained = 131, DeathDrained = 132, FeedReleased = 133, FeedWrist = 134,
                         EmbracedWrist = 135, FeedingWrist = 136, DrainingWrist = 137,
                         FeedReleaseWrist = 138, FeedReleasedWrist = 139, FeedFailed = 236;
    }

    /// <summary>`actorfeed`'s status: 0 start, 1 in progress, 2 stopped, 3 stopped by a kill (`Codex.java`).</summary>
    public const int Start = 0, InProgress = 1, Stopped = 2, StoppedKilled = 3;

    /// <summary>Close enough to bite: 60 units, body to body (0x42700000).</summary>
    public const float Reach = 60f;
    /// <summary>How far the approach looks again (250 ms).</summary>
    public const int ApproachRetryMs = 250;
    /// <summary>Within this the feeder walks up; beyond it, runs (centre to centre).</summary>
    public const float WalkWithin = 300f;
    /// <summary>`vox_feedchase` on a run, one time in five.</summary>
    public const float ChaseBarkChance = 0.2f;
    /// <summary>Where the victim is put: 60 in front for the neck, 75 for the wrist.</summary>
    public const float NeckGap = 60f, WristGap = 75f;
    /// <summary>A donor gives nothing at 10 blood or under.</summary>
    public const float DonorFloor = 10f;
    /// <summary>What each drain takes from the victim (0xc0400000) — the feeder gains three times its BloodRating.</summary>
    public const float Drain = 3f, GainPerBloodRating = 3f;
    /// <summary>The resist roll's floor and cap, and what a level of Feed is worth in it.</summary>
    public const float ResistFloor = 5f, ResistCap = 95f, PerFeedLevel = 20f;
    /// <summary>`TEAM_PEDESTRIANS`: a pedestrian never resists.</summary>
    public const int PedestrianTeam = 10;
    /// <summary>What a victim still has, relative to this, decides whether an early release kills it.</summary>
    public const float ReleaseKillBase = 25f;
    /// <summary>The time between drains (1,000 ms), a `FASTDRINKER`'s (500 ms).</summary>
    public const int TickMs = 1000, FastTickMs = 500;
    /// <summary>The first drain a second after the bite, and the bite's busy time (1,000 ms each).</summary>
    public const int FirstDrainMs = 1000, BiteBusyMs = 1000;
    /// <summary>What an AI feeder's mind waits beyond its `feedDuration` (4,000 ms; a party member's 9,000).</summary>
    public const int ThinkPadMs = 4000, CompanionThinkPadMs = 9000;
    /// <summary>How often a held victim looks again (500 ms).</summary>
    public const int VictimCheckMs = 500;
    /// <summary>The chance of each of the drain's two noises (0.1).</summary>
    public const float SuckNoiseChance = 0.1f;

    // ---- the order ----------------------------------------------------------------------------

    /// <summary>
    /// **The feed order**, reached only through the `Feed` native. Answers 0 for "nothing
    /// to do" (itself, or already on the way to that one), 1 refused, 2 not now, 4 started.
    /// <list type="bullet">
    /// <item>Busy, dead or restrained: not now.</item>
    /// <item>Not a character, or either side ethereal: refused, silently.</item>
    /// <item>A ghoul may only feed on a vampire; nothing may feed on a `NOFEED` character or one
    /// with no motion set — and **a character whose motion set lacks `embraced` is marked `NOFEED`
    /// for good** (unless it is a player thing). Those three say `vox_cannotfeed` when the one
    /// feeding is the steered character and can speak.</item>
    /// <item>Somebody already feeding, or being fed on at the neck: refused.</item>
    /// <item>A neutral or talk-to target when the feeder's blood is full: `vox_sated`, refused.
    /// Anyone else may be fed on full — the blood gained is simply clamped away.</item>
    /// <item>Then whatever it was doing is cancelled, and it walks up — walking within 300, running
    /// beyond with a one-in-five `vox_feedchase` — and the approach is ticked at once.</item>
    /// </list>
    /// </summary>
    public static int Order(WorldThing feeder, WorldThing target)
    {
        if (feeder.Actor is not { } me) return 1;
        var place = feeder.Place;
        int now = place?.Milliseconds ?? 0;
        if (Unready(me, now)) return 2;
        if (ReferenceEquals(target, feeder)) return 0;
        if (me.Action == ActionMode.ApproachFeed && ReferenceEquals(me.Target, target)) return 0;
        if (target.Actor is not { } them || !target.IsActor || me.Has(Actor.Flag.Ethereal) || them.Has(Actor.Flag.Ethereal)) return 1;
        if (me.Type == ActorType.Ghoul && them.Type != ActorType.Vampire) return Cannot(feeder);
        if (them.Has(Actor.Flag.NoFeed) || target.Animation is null) return Cannot(feeder);
        if (target.Type != ThingType.Player && place?.Motions is { } motions
            && motions.Find(target.Animation.Set, Motion.Embraced) is null)
        {
            them.Flags |= Actor.Flag.NoFeed;
            return Cannot(feeder);
        }
        if (them.Action is ActionMode.FedOn or ActionMode.Feeding or ActionMode.WristFeeding) return 1;
        if ((them.Has(Actor.Flag.Neutral) || them.Has(Actor.Flag.TalkTo))
            && me.Stat[(int)ActorStat.BloodPool] <= me.Stat[(int)ActorStat.Blood])
        {
            Audio.Noise.Foley(feeder, Audio.FoleyEvent.VoxSated);
            // Ours, for the log only: 1.1 says nothing on screen, and for Christof — whose sound set
            // has no `vox_sated` — nothing out loud either, so a play-test reads it as a miss.
            log.LogInformation("feed: {Feeder} is full, and {Target} is a bystander ({Why}) - refused, as 1.1 refuses it",
                Diagnostics.Watch.Name(feeder), Diagnostics.Watch.Name(target), them.Has(Actor.Flag.TalkTo) ? "talk-to" : "neutral");
            Diagnostics.Journal.Current?.Write(now, Diagnostics.JournalKind.Order, ("what", "feed"),
                ("who", Diagnostics.Watch.Name(feeder)), ("on", Diagnostics.Watch.Name(target)), ("refused", "sated"));
            return 1;
        }
        Actions.Cancel(feeder);
        if (Unready(me, now)) return 2;
        me.Target = target;
        target.Flags |= WorldThing.Flag.Targetted;
        me.Action = ActionMode.ApproachFeed;
        me.ActionRetryAt = 0;
        float speed;
        if (Vector3.Distance(target.Position, feeder.Position) <= Rules.GameRules.Current.Feeding.FeedWalkWithin) speed = me.WalkSpeed;
        else
        {
            speed = me.RunSpeed;
            if (Audio.Noise.CanSpeak(feeder) && (place?.Minds?.Random.Unit() ?? 1f) < Rules.GameRules.Current.Feeding.FeedChaseBarkChance)
                Audio.Noise.Foley(feeder, Audio.FoleyEvent.VoxFeedChase);
        }
        me.ActionSpeed = speed * me.MoveSpeedScale;
        Actions.Close(feeder, me, target, now, 0);
        Approaching(feeder, me, now);
        return 4;
    }

    static bool Unready(Actor me, int now) =>
        now < me.BusyUntil || me.Has(Actor.Flag.Dead) || me.Has2(Actor.Flag2.Restrained);

    /// <summary>`vox_cannotfeed`, for the steered character when it can speak, and a refusal.</summary>
    static int Cannot(WorldThing feeder)
    {
        if (Audio.Noise.CanSpeak(feeder) && ReferenceEquals(feeder.Place?.Steered, feeder))
            Audio.Noise.Foley(feeder, Audio.FoleyEvent.VoxCannotFeed);
        return 1;
    }

    /// <summary>
    /// ACTION 7's tick, when its retry is due: the target has gone, or left the place —
    /// stop; within 60 units body to body — <see cref="Bite"/>; otherwise walk on toward where it is
    /// now and look again in a quarter-second.
    /// </summary>
    public static void Approaching(WorldThing feeder, Actor me, int now)
    {
        if (now < me.ActionRetryAt) return;
        if (me.Target is not { IsActor: true } target || !ReferenceEquals(target.Place, feeder.Place) || !target.InWorld)
        {
            Actions.StopWalking(feeder);
            Actions.Cancel(feeder, force: true);
            return;
        }
        if (Bodies.InReach(feeder, target, Rules.GameRules.Current.Feeding.FeedReach)) { Bite(feeder, target); return; }
        Actions.Close(feeder, me, target, now, Rules.GameRules.Current.Feeding.FeedApproachRetryMs);
    }

    // ---- the bite ----------------------------------------------------------------------------

    /// <summary>
    /// **The bite**. Refused and stopped if the feeder is ethereal or the victim is
    /// already feeding or fed on, `NOFEED`, or ethereal. The feeder turns to its victim. Then, unless
    /// <paramref name="forced"/>, <b>an armed victim who is not a friend, not neutral or talk-to,
    /// not a pedestrian and not mesmerised resists</b>: the feeder's chance is 20 per level of
    /// Feed plus its Wits, less the victim's Wits and its feed protection, held to 5..95 — and the
    /// victim's mind is told it was attacked whether or not the bite then lands.
    ///
    /// A player thing, not frenzied, biting a friend takes the <b>wrist</b>: a donor at 10 blood or
    /// less says no (`vox_partyfeedno`) and it fails; otherwise `vox_partyfeedok`, and the donor is put
    /// 75 in front, facing the feeder. Anybody else takes the <b>neck</b>: the victim is restrained
    /// (`ef_restrain`) and put 60 in front, facing away. Both play their opening animations; a player
    /// feeder's portrait bites; the feeder is busy for a second and says `kiss`; the victim's mind
    /// targets the feeder; an AI feeder does not think again for its `feedDuration` plus 4 s (9 s for a
    /// party member); `actorfeed` START goes out; and the first drain is a second away.
    /// </summary>
    public static void Bite(WorldThing feeder, WorldThing target, bool forced = false)
    {
        if (feeder.Actor is not { } me || target.Actor is not { } them) return;
        var place = feeder.Place;
        int now = place?.Milliseconds ?? 0;
        var minds = place?.Minds;
        if (me.Has(Actor.Flag.Ethereal) || them.Action is ActionMode.FedOn or ActionMode.WristFedOn or ActionMode.Feeding or ActionMode.WristFeeding
            || them.Has(Actor.Flag.NoFeed) || them.Has(Actor.Flag.Ethereal))
        {
            Actions.Cancel(feeder);
            return;
        }
        feeder.Face(target);
        if (!forced && target.Mind is not { Mode: Ai.AiMode.Mesmerized } && !Sight.Friendly(feeder, target)
            && !them.Has(Actor.Flag.Neutral) && !them.Has(Actor.Flag.TalkTo)
            && them.Weapon is not null && them.Team != PedestrianTeam)
        {
            float chance = FeedLevel(me) * Rules.GameRules.Current.Feeding.FeedResistPerLevel + me.Stat[(int)ActorStat.Wits] - them.Stat[(int)ActorStat.Wits]
                           - target.Effects.Value(EffectKind.FeedProtection);
            chance = Math.Clamp(chance, Rules.GameRules.Current.Feeding.FeedResistClamp[0], Rules.GameRules.Current.Feeding.FeedResistClamp[1]);
            if (target.Mind is { } told) minds?.AttackedBy(told, feeder);
            if ((minds?.Random.Unit() ?? 0f) * 100f > chance) { Failed(feeder, target); return; }
        }
        Actions.StopWalking(feeder);
        Actions.StopWalking(target);
        HideWeapon(me);
        var forward = Vector3.TransformNormal(Vector3.UnitY, feeder.Rotation);
        forward.Z = 0f;
        if (forward.LengthSquared() > 0f) forward = Vector3.Normalize(forward);
        if (feeder.Type == ThingType.Player && !Frenzy.Frenzied(feeder) && Sight.Friendly(feeder, target))
        {
            if (them.Stat[(int)ActorStat.Blood] <= Rules.GameRules.Current.Feeding.FeedDonorFloor)
            {
                Audio.Noise.Foley(target, Audio.FoleyEvent.VoxPartyFeedNo);
                Failed(feeder, target);
                return;
            }
            Audio.Noise.Foley(target, Audio.FoleyEvent.VoxPartyFeedOk);
            HideWeapon(them);
            target.Orientation = feeder.Orientation + new Vector3(0f, 0f, 180f);
            Put(target, feeder.Position + forward * Rules.GameRules.Current.Feeding.FeedVictimGap[1]);
            Begin(me, ActionMode.WristFeeding, target);
            Actions.PlayAction(feeder, Motion.FeedWrist);
            Begin(them, ActionMode.WristFedOn, feeder);
            Actions.PlayAction(target, Motion.EmbracedWrist);
        }
        else
        {
            EffectRuntime.Apply(target, "ef_restrain", 0);
            target.Orientation = feeder.Orientation;
            Put(target, feeder.Position + forward * Rules.GameRules.Current.Feeding.FeedVictimGap[0]);
            Begin(me, ActionMode.Feeding, target);
            Actions.PlayAction(feeder, Motion.Feed);
            Begin(them, ActionMode.FedOn, feeder);
            Actions.PlayAction(target, Motion.Embraced);
        }
        if (feeder.Type == ThingType.Player) me.HeadAction = Ui.HeadMode.Bite;
        me.BusyUntil = now + Rules.GameRules.Current.Feeding.FeedFirstDrainMs[1];
        Audio.Noise.Foley(feeder, Audio.FoleyEvent.Kiss);
        if (target.Mind is { } victimMind) minds?.SetTarget(victimMind, feeder);
        if (feeder.Mind is { } feederMind && minds is not null)
        {
            bool companion = feeder.Type == ThingType.Player && me.Has(Actor.Flag.Party);
            var pad = Rules.GameRules.Current.Feeding.FeedAiThinkPad;
            feederMind.NextThink = now + (int)(minds.Random.Unit() * feederMind.Class.FeedDuration + (companion ? pad[1] : pad[0]));
        }
        place?.ScriptObjects?.ActorFeed(feeder, target, Start);
        me.ActionRetryAt = now + Rules.GameRules.Current.Feeding.FeedFirstDrainMs[0];
        Diagnostics.Journal.Current?.Write(now, Diagnostics.JournalKind.Order, ("what", "bite"),
            ("who", Diagnostics.Watch.Name(feeder)), ("on", Diagnostics.Watch.Name(target)),
            ("wrist", me.Action == ActionMode.WristFeeding));
    }

    /// <summary>
    /// The victim is moved to the feeder with `SetPosition`, which re-finds
    /// its sector and stands it **on the floor there** (the surface's height at the new
    /// point) — the feeder's height is only where the point starts. On a stair it shows: a 1.1
    /// run had Christof at −367.2 bite a Cappadocian who landed 60 in front
    /// at −382.5, a step down. Earlier, Vitomir wrote the point and left it at the feeder's height,
    /// in the sector it came from.
    /// </summary>
    static void Put(WorldThing thing, Vector3 at)
    {
        if (thing.Place is not { } place) { thing.Position = at; return; }
        place.Move(thing, at);
        place.WalkerFor(thing)?.Ground();
    }

    /// <summary>
    /// **The weapon goes out of sight for the feed** (read in a comparison with 1.1): the feeder's
    /// when the bite lands, and a wrist donor's too — the hide bit on the render model's weapon, the
    /// one `EnableActorWeapon` writes, unless the character has WEAPONOVERRIDE or WEAPONNOHANDS.
    /// Letting go shows both again (the release for the victim and the feeder;
    /// the drain to death for the feeder), whether or not they were hidden.
    /// </summary>
    static void HideWeapon(Actor who)
    {
        if ((who.Flags2 & (Actor.Flag2.WeaponOverride | Actor.Flag2.WeaponNoHands)) == 0) who.WeaponHidden = true;
    }

    static void Begin(Actor who, ActionMode mode, WorldThing other)
    {
        who.Action = mode;
        who.ActionRetryAt = 0;
        who.Target = other;
    }

    /// <summary>
    /// The feeder's level of Feed: its 0-based level, or −2 when it does not know
    /// the discipline at all.
    /// </summary>
    public static int FeedLevel(Actor me) => me.Disciplines["Feed"]?.Level ?? -2;

    /// <summary>
    /// The bite failed: the feeder stops, the victim's mind is told it was attacked, and
    /// the feeder plays `feedfailed` and is busy for it.
    /// </summary>
    static void Failed(WorldThing feeder, WorldThing target)
    {
        if (feeder.Actor is not { } me) return;
        var place = feeder.Place;
        Actions.StopWalking(feeder);
        Actions.Cancel(feeder, force: true);
        if (target.Mind is { } told) place?.Minds?.AttackedBy(told, feeder);
        int ms = Actions.PlayAction(feeder, Motion.FeedFailed);
        if (ms > 0) me.BusyUntil = (place?.Milliseconds ?? 0) + ms;
        // Ours, for the journal: a failed bite is otherwise only an animation.
        Diagnostics.Journal.Current?.Write(place?.Milliseconds ?? 0, Diagnostics.JournalKind.Order, ("what", "bite"),
            ("who", Diagnostics.Watch.Name(feeder)), ("on", Diagnostics.Watch.Name(target)), ("failed", 1), ("ms", ms));
    }

    /// <summary>
    /// The bite has landed — the opening animation has ended, and its `endtag` is keyframe tag 19:
    /// the loops start, `feeding`/`draining` at the neck, `feedingwrist`/`drainingwrist`
    /// at the wrist, and a player feeder's portrait feeds.
    /// </summary>
    public static void Landed(WorldThing thing)
    {
        if (thing.Actor is not { } a) return;
        switch (a.Action)
        {
            case ActionMode.Feeding:
                Actions.PlayAction(thing, Motion.Feeding);
                if (thing.Type == ThingType.Player) a.HeadAction = Ui.HeadMode.Feed;
                break;
            case ActionMode.FedOn: Actions.PlayAction(thing, Motion.Draining); break;
            case ActionMode.WristFeeding:
                Actions.PlayAction(thing, Motion.FeedingWrist);
                if (thing.Type == ThingType.Player) a.HeadAction = Ui.HeadMode.Feed;
                break;
            case ActionMode.WristFedOn: Actions.PlayAction(thing, Motion.DrainingWrist); break;
        }
    }

    /// <summary>`endtag`: the synthetic keyframe the mixer fires when a `noloop endtag` mode runs out.</summary>
    public const uint EndTag = 19;

    // ---- the drain ---------------------------------------------------------------------------

    /// <summary>
    /// The feeder's tick, each drain. The victim must still be a character in the same
    /// place, or the feed stops. A donor with 13 or less would go to 10 or under: `vox_partyfeedstop`
    /// and it stops. Otherwise the feeder gains **three times its BloodRating** — first, so the tick
    /// that empties the victim still pays — and a victim that is dead, or would be at 0 or under, is
    /// drained to death (a donor is only let go). Else a one-in-ten `suck` and `bloodpointdrain`, the
    /// victim loses 3, the next drain is a second away (half a second for a `FASTDRINKER`), the
    /// victim's mind cries out (<see cref="Ai.Minds.BeingFedOn"/>) and `actorfeed` IN PROGRESS goes out.
    /// </summary>
    public static void Tick(WorldThing feeder, Actor me, int now)
    {
        if (now < me.ActionRetryAt) return;
        if (me.Target is not { IsActor: true, Actor: { } them } victim || !ReferenceEquals(victim.Place, feeder.Place))
        {
            Actions.Cancel(feeder, force: true);
            return;
        }
        var place = feeder.Place;
        float gain = me.Stat[(int)ActorStat.BloodRating] * Rules.GameRules.Current.Feeding.FeedGainPerBloodRating;
        var rules = Rules.GameRules.Current.Feeding;
        float remaining = them.Stat[(int)ActorStat.Blood] - rules.FeedDrainPerTick;
        if (me.Action == ActionMode.WristFeeding && remaining <= Rules.GameRules.Current.Feeding.FeedDonorFloor)
        {
            Audio.Noise.Foley(victim, Audio.FoleyEvent.VoxPartyFeedStop);
            Actions.Cancel(feeder);
            return;
        }
        EffectRuntime.Apply(feeder, "ef_increaseblood", 0, durationMs: 0, value: gain);
        if (them.Has(Actor.Flag.Dead) || remaining <= 0f)
        {
            if (me.Action == ActionMode.WristFeeding) Actions.Cancel(feeder);
            else DrainedDead(feeder, victim);
            return;
        }
        var rng = place?.Minds?.Random;
        if ((rng?.Unit() ?? 1f) < rules.FeedSuckNoiseChance[0]) Audio.Noise.Foley(feeder, Audio.FoleyEvent.Suck);
        if ((rng?.Unit() ?? 1f) < rules.FeedSuckNoiseChance[1]) Audio.Noise.Foley(victim, Audio.FoleyEvent.BloodPointDrain);
        EffectRuntime.Apply(victim, "ef_decreaseblood", 0, durationMs: 0, value: -rules.FeedDrainPerTick);
        me.ActionRetryAt = now + (me.Has(Actor.Flag.FastDrinker) ? rules.FeedTickMs[1] : rules.FeedTickMs[0]);
        if (victim.Mind is { } mind) place?.Minds?.BeingFedOn(mind);
        place?.ScriptObjects?.ActorFeed(feeder, victim, InProgress);
    }

    /// <summary>
    /// The victim's tick, every half-second: while its feeder is still a character in the
    /// same place, feeding on it, it goes on; otherwise it is let go — the feeder's feed stopped if
    /// the feeder is still there, its own animation stopped, `ef_restrain` off, and its mind released
    /// (<see cref="Ai.Minds.FedOnReleased"/>).
    /// </summary>
    public static void VictimTick(WorldThing victim, Actor me, int now)
    {
        if (now < me.ActionRetryAt) return;
        if (!me.Has(Actor.Flag.Dead) && me.Target is { IsActor: true, Actor: { } f } feeder
            && f.Action is ActionMode.Feeding or ActionMode.WristFeeding && ReferenceEquals(f.Target, victim)
            && ReferenceEquals(feeder.Place, victim.Place))
        {
            me.ActionRetryAt = now + Rules.GameRules.Current.Feeding.FeedVictimCheckMs;
            return;
        }
        Actions.Cancel(victim, force: true, release: true);
        victim.Animation?.StopAction();
        EffectRuntime.Remove(victim, "ef_restrain");
        if (victim.Mind is { } mind) victim.Place?.Minds?.FedOnReleased(mind);
    }

    // ---- letting go --------------------------------------------------------------------------

    /// <summary>
    /// **Stop feeding** — any order the feeder is given (every one cancels first), the
    /// `StopFeeding` native, a hit that knocks it off, a frenzy, a death. At the **neck**, letting go
    /// early can still kill: with Feed at level L the victim dies when its blood is under 25/L (25 at
    /// levels 0 and 1) and under a roll of that — one minus blood over the threshold, a 60% chance at
    /// 10 blood. Otherwise the feeder plays its release and is busy for it, its portrait stops
    /// feeding, and it says `detach`; it barks the end of the feed — **swapped in 1.1**: a
    /// wrist feed says `vox_endfeed` and a neck feed `vox_endfeedwrist`. The victim is let go, plays
    /// its own release, loses `ef_restrain`, and `actorfeed` STOPPED goes out.
    /// </summary>
    public static void Stop(WorldThing feeder, bool release = false)
    {
        if (feeder.Actor is not { } me) return;
        var place = feeder.Place;
        int now = place?.Milliseconds ?? 0;
        var victim = me.Target is { IsActor: true } v ? v : null;
        bool wrist = me.Action == ActionMode.WristFeeding;
        if (victim?.Actor is { } them && me.Action == ActionMode.Feeding)
        {
            float t = Rules.GameRules.Current.Feeding.FeedReleaseKillBase;
            int level = FeedLevel(me);
            if (level > 0) t = Rules.GameRules.Current.Feeding.FeedReleaseKillBase / level;
            float blood = them.Stat[(int)ActorStat.Blood];
            if (blood < t && blood < (place?.Minds?.Random.Unit() ?? 0f) * t) { DrainedDead(feeder, victim); return; }
        }
        if (!me.Has(Actor.Flag.Dead))
        {
            feeder.Animation?.StopAction();
            int ms = Actions.PlayAction(feeder, wrist ? Motion.FeedReleaseWrist : Motion.FeedRelease);
            if (ms > 0) me.BusyUntil = now + ms;
            if (feeder.Type == ThingType.Player) me.HeadAction = Ui.HeadMode.None;
            Audio.Noise.Foley(feeder, Audio.FoleyEvent.Detach);
        }
        if (Audio.Noise.CanSpeak(feeder))
            Audio.Noise.Foley(feeder, wrist ? Audio.FoleyEvent.VoxEndFeed : Audio.FoleyEvent.VoxEndFeedWrist);
        if (victim?.Actor is { } held && held.Action is ActionMode.FedOn or ActionMode.WristFedOn)
        {
            held.Action = ActionMode.None;
            held.Target = null;
            held.PendingDiscipline = null;
            held.BusyUntil = 0;
            victim.Animation?.StopAction();
            EffectRuntime.Remove(victim, "ef_restrain");
            if (victim.Mind is { } mind) place?.Minds?.FedOnReleased(mind);
            int ms = Actions.PlayAction(victim, wrist ? Motion.FeedReleasedWrist : Motion.FeedReleased);
            if (ms > 0) held.BusyUntil = now + ms;
            held.WeaponHidden = false;
            place?.ScriptObjects?.ActorFeed(feeder, victim, Stopped);
        }
        me.WeaponHidden = false;
        me.Action = ActionMode.None;
        me.Target = null;
        me.PendingDiscipline = null;
    }

    /// <summary>
    /// **Drained to death**: the feeder lets go and plays `feederdrained`, busy for it, its
    /// portrait stops, and it says `detach` — and, when it can speak, `vox_endfeedkillinnocent` if it
    /// tracks its Humanity and the victim was an innocent, else `vox_endfeedkill`. The victim is let
    /// go and, unless already dead, killed by the feeder as hit kind 5 — and then plays
    /// `deathdrained` over whatever death the kill chose. `actorfeed` STOPPED-KILLED goes out.
    /// </summary>
    public static void DrainedDead(WorldThing feeder, WorldThing victim)
    {
        if (feeder.Actor is not { } me || victim.Actor is not { } them) return;
        var place = feeder.Place;
        int now = place?.Milliseconds ?? 0;
        me.Action = ActionMode.None;
        me.Target = null;
        me.PendingDiscipline = null;
        feeder.Animation?.StopAction();
        int ms = Actions.PlayAction(feeder, Motion.FeederDrained);
        if (ms > 0) me.BusyUntil = now + ms;
        if (feeder.Type == ThingType.Player) me.HeadAction = Ui.HeadMode.None;
        Audio.Noise.Foley(feeder, Audio.FoleyEvent.Detach);
        if (Audio.Noise.CanSpeak(feeder))
            Audio.Noise.Foley(feeder, me.Has(Actor.Flag.HumanityTrack) && them.Has(Actor.Flag.HumanityLoss)
                ? Audio.FoleyEvent.VoxEndFeedKillInnocent : Audio.FoleyEvent.VoxEndFeedKill);
        them.Action = ActionMode.None;
        them.Target = null;
        them.PendingDiscipline = null;
        victim.Animation?.StopAction();
        EffectRuntime.Remove(victim, "ef_restrain");
        if (!them.Has(Actor.Flag.Dead))
            Damage.Kill(victim, feeder, HitKind.Drain, DamageType.Normal, now, motions: place?.Motions, rng: place?.Minds?.Random);
        Actions.PlayAction(victim, Motion.DeathDrained);
        place?.ScriptObjects?.ActorFeed(feeder, victim, StoppedKilled);
        me.WeaponHidden = false;
    }

}
