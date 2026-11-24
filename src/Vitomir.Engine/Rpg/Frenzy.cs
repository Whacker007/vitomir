// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The Beast. Frenzy is stat 8, and a vampire or ghoul whose frenzy reaches 100 goes into a frenzy:
// stronger, clumsier, deaf to its player, and hungry for the nearest creature with a mind, friend or
// foe. The original has three routines for it — the check every frenzy write runs,
// the way in and the way out — and the mind's frenzied mode is `Ai.Minds`'.
using Vitomir.Engine.Ai;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>Frenzy, in and out.</summary>
public static class Frenzy
{
    /// <summary>At or above this a vampire frenzies.</summary>
    public const float Threshold = 100f;
    /// <summary>At or below this a frenzy ends.</summary>
    public const float Calm = 25f;
    /// <summary>Crossing this upward says `vox_nearfrenzy`.</summary>
    public const float Near = 80f;
    /// <summary>The effects a frenzy lays beside `ef_frenzy` and takes off at its end.</summary>
    public static readonly string[] StatEffects = ["ef_frz_strength", "ef_frz_dexterity"];
    /// <summary>What `ActorFrenzy` and `ActorUnfrenzy` apply (+100, −99).</summary>
    public const float ProvokeValue = 100f, SootheValue = -99f;

    /// <summary>`MOTION_FRENZY`, 234 (0xea), played on the way in.</summary>
    public const int MotionFrenzy = 234;

    /// <summary>
    /// Whether a character is frenzied: **its mind is in mode 9**. There is no flag for it
    /// — the state *is* the mode, with `ef_frenzy` hung on the character to show it. A character with
    /// no mind can never be frenzied.
    /// </summary>
    public static bool Frenzied(WorldThing thing) => thing.Mind is { Mode: AiMode.Frenzied };

    /// <summary>
    /// The check, run by every write of a vampire's or ghoul's frenzy
    /// (<see cref="Stats.Set"/>): frenzied and at or under 25 — it ends; not frenzied and at or over
    /// 100 — it begins; otherwise, crossing 80 upward, `vox_nearfrenzy` when the character can speak.
    /// </summary>
    public static void Watch(WorldThing thing, float was)
    {
        if (thing.Actor is not { } a) return;
        float now = a.Stat[(int)ActorStat.Frenzy];
        var r = Rules.GameRules.Current.Frenzy;
        if (Frenzied(thing) && now <= r.FrenzyCalm) { End(thing); return; }
        if (!Frenzied(thing) && now >= r.FrenzyThreshold) { Begin(thing); return; }
        if (now >= r.FrenzyNearBark && was < r.FrenzyNearBark && Audio.Noise.CanSpeak(thing))
            Audio.Noise.Foley(thing, Audio.FoleyEvent.VoxNearFrenzy);
    }

    /// <summary>
    /// Into frenzy. Nothing for a staked character, or one that is neither a vampire nor
    /// a ghoul. <paramref name="permanent"/> — the multiplayer "Humanity lost" end — sets
    /// `THING_AF2_PERMAFRENZY`, and on a character already frenzied only stops the decay. Otherwise,
    /// in order:
    /// <list type="number">
    /// <item>`vox_frenzy`, with no can-speak gate;</item>
    /// <item>the wound penalties come off and stay off (<see cref="Wounds.Apply"/>, frenzied);</item>
    /// <item>`ef_frenzy` on (the marker, with its icon), `ef_frz_strength` (+20) and
    /// `ef_frz_dexterity` (−10); `ef_decreasefrenzy` off, and unless permanent
    /// `ef_decreasefrenzyfast` on at level 1 — four a half-second;</item>
    /// <item>for the character the player is steering, a red flash, 0 → 255 over 800 ms, bouncing;</item>
    /// <item>a Gangrel loses a point of Appearance one time in ten;</item>
    /// <item>whatever it was doing is cancelled — a feed ends — and, if it is free, it plays
    /// `frenzy` and is busy for it;</item>
    /// <item>its mind goes to mode 9 (<see cref="Minds.EnterFrenzy"/>);</item>
    /// <item>"%P enters Frenzy!" in the message log, and a party member's portrait snarls.</item>
    /// </list>
    /// </summary>
    public static void Begin(WorldThing thing, bool permanent = false)
    {
        if (thing.Actor is not { } a || a.Has(Actor.Flag.Staked) || a.Type is not (ActorType.Vampire or ActorType.Ghoul)) return;
        if (permanent) a.Flags2 |= Actor.Flag2.PermaFrenzy;
        if (Frenzied(thing) && permanent) { EffectRuntime.Remove(thing, "ef_decreasefrenzyfast"); return; }
        Audio.Noise.Foley(thing, Audio.FoleyEvent.VoxFrenzy);
        Wounds.Apply(thing, Wounds.Level(thing, a.Health), frenzied: true);
        EffectRuntime.Apply(thing, "ef_frenzy", 0, value: 0f);
        foreach (var effect in Rules.GameRules.Current.Frenzy.FrenzyEffects) EffectRuntime.Apply(thing, effect, 0);
        EffectRuntime.Remove(thing, "ef_decreasefrenzy");
        if (!permanent) EffectRuntime.Apply(thing, "ef_decreasefrenzyfast", 1);
        var place = thing.Place;
        if (place is not null && ReferenceEquals(place.Steered, thing))
            place.Session?.Screen?.AddFlash(0f, 255f, FlashMs, FlashColour, bounce: true, place.Milliseconds);
        if (a.ClanRecord is { } clan && (clan.Weaknesses & Clan.WeakFrenzyAppearance) != 0
            && (place?.Minds?.Random.Unit() ?? 1f) < Rules.GameRules.Current.Frenzy.GangrelFrenzyAppearanceChance)
        {
            int app = (int)ActorStat.Appearance;
            // The **base** Appearance less one (baseStats[13]; the
            // A comparison with 1.1's fix — Vitomir took it from the live stat, so a worn Appearance bonus became
            // permanent), and then the live stat recomputed from it.
            a.SetBaseStat(app, a.BaseStat[app] - 1f);
            EffectRuntime.Recompute(thing, Formats.EffectKind.Stat, app);
        }
        Actions.Cancel(thing);
        int now = place?.Milliseconds ?? 0;
        if (a.BusyUntil <= now && !a.Has(Actor.Flag.Dead) && !a.Has2(Actor.Flag2.Restrained))
        {
            int ms = Actions.PlayAction(thing, MotionFrenzy);
            if (ms > 0) a.BusyUntil = now + ms;
        }
        if (thing.Mind is { } mind) place?.Minds?.EnterFrenzy(mind);
        if (place?.Session is { } session)
            session.Messages.Post("RPG_FRENZY", "%P" + session.NameOf(thing));
        if (thing.Type == ThingType.Player) a.HeadAction = Ui.HeadMode.Frenzy;
    }

    /// <summary>The red flash of going into frenzy (800 ms, 0xff0000, bounce).</summary>
    public const int FlashMs = 800;
    /// <inheritdoc cref="FlashMs"/>
    public const uint FlashColour = 0xff0000;
    /// <summary>A Gangrel's chance of losing Appearance to a frenzy.</summary>
    public const float GangrelChance = 0.1f;

    /// <summary>
    /// Out of frenzy — only a frenzied character, and never a permanently frenzied one:
    /// `vox_unfrenzy`, the wound penalties laid again for its health, the four frenzy effects off,
    /// `ef_decreasefrenzy` back on, the mind released (<see cref="Minds.LeaveFrenzy"/>), "%P's Frenzy
    /// ends." in the log, and the portrait's snarl stopped.
    /// </summary>
    public static void End(WorldThing thing)
    {
        if (thing.Actor is not { } a || !Frenzied(thing) || a.Has2(Actor.Flag2.PermaFrenzy)) return;
        Audio.Noise.Foley(thing, Audio.FoleyEvent.VoxUnfrenzy);
        // The rung is worked out while the mind is still in mode 9, so it is 0 — and the penalties
        // for rung 0 are none: they come back at the next blow or heal.
        Wounds.Apply(thing, Wounds.Level(thing, a.Health), frenzied: false);
        EffectRuntime.Remove(thing, "ef_frenzy");
        foreach (var effect in Rules.GameRules.Current.Frenzy.FrenzyEffects) EffectRuntime.Remove(thing, effect);
        EffectRuntime.Remove(thing, "ef_decreasefrenzyfast");
        EffectRuntime.Apply(thing, "ef_decreasefrenzy", 0);
        var place = thing.Place;
        if (thing.Mind is { } mind) place?.Minds?.LeaveFrenzy(mind);
        if (place?.Session is { } session)
            session.Messages.Post("RPG_UNFRENZY", "%P" + session.NameOf(thing));
        if (thing.Type == ThingType.Player) a.HeadAction = Ui.HeadMode.None;         // the head's action stopped
    }

    /// <summary>`ActorFrenzy`: `ef_increasefrenzy` by value +100, which the check turns into a frenzy.</summary>
    public static void Provoke(WorldThing thing) => EffectRuntime.Apply(thing, "ef_increasefrenzy", 0, value: Rules.GameRules.Current.Frenzy.ProvokeFrenzyValue[0]);

    /// <summary>`ActorUnfrenzy`: `ef_increasefrenzy` by value −99. It does nothing to the character's team.</summary>
    public static void Soothe(WorldThing thing) => EffectRuntime.Apply(thing, "ef_increasefrenzy", 0, value: Rules.GameRules.Current.Frenzy.ProvokeFrenzyValue[1]);
}
