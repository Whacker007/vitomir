// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The start of a player's own swing predicted on her machine (the second part). Ours:
// 1.1's player's machine sends the attack order and swings only when the host's strike comes back
// (message 0xc).
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>
/// **Her own swing, at once, on her machine** (the second part; a machine guesses only what its own player's
/// input certainly causes). A click to attack that the host will certainly turn into a swing at once — she can swing now and the
/// target is in her reach, standing still — starts the swing's animation and its noise here and turns her to face the target, as the
/// order goes (<see cref="Actions.StartSwing"/>); everything decided by dice or by others waits for the host, as before: the throw
/// that may replace the swing, the war cry, the hit or the miss, the damage, the target's reaction.
/// <list type="bullet">
/// <item>**The host's strike** (<see cref="Msg.Struck"/>, replayed by <see cref="ClientArms"/>) at the same target bears the guess
/// out: the rest of the strike is done (<see cref="Actions.StrikeStarted"/>) and the swing is not started a second time; the host's
/// snapshot of the swing is not either (<see cref="WorldMirror.Replayed"/>).</item>
/// <item>**The host's answer** to the order (<see cref="Msg.Taken"/>, which comes after the strike it made) with no strike before
/// it — refused, busy, or started as an approach because the host found her out of reach — **takes the guess back**: the swing's
/// animation stopped, its noise stopped, her action, target and busy time put back, and her shown as the snapshots have her. So is
/// a strike or a throw of the host's at another target, and an answer that has not come in <see cref="OwnWalk.AnswerWaitMs"/>.
/// Each is counted (<see cref="Done"/>).</item>
/// <item>**Not guessed** (the order goes as before and the swing comes back with the host's strike): an approach (the host walks
/// her into reach; her walk is the host's, <see cref="OwnWalk"/>), a target moving on her screen or not yet still for
/// <see cref="StillMs"/>, a reach closer than <see cref="ReachMargin"/> to the edge, a weapon whose attack may become a throw
/// against a character (the host's roll), a ranged attack (the shot and its ammunition are the host's), the held button's next
/// swing and a click within a reload and <see cref="RepeatGapMs"/> of a strike of hers (her held button's order sent at the
/// swing's end may be on its way), anything she is already doing (a swing, a cast, a feed, a reaction), and a guess not yet
/// answered. Letting the button go sends nothing and takes nothing back.</item>
/// </list>
/// The attack's order is numbered as her walk's are (<see cref="OwnWalk.Numbered"/>), and she stands where she is until the host
/// answers (<see cref="OwnWalk.Stand"/>). Deterministic under a tape (her machine's state and the host's messages only); a tape
/// says whether it was on (<c>netpredict on strikes</c>). On by default, outside the faithful, Fixes and QoL settings;
/// <c>--no-predict</c> turns it off with her walk.
/// </summary>
public sealed class OwnStrike(Func<WorldThing?> me, OwnWalk walk, Func<WorldMirror?> mirror, Func<bool> paused)
{
    /// <summary>On for every player's machine made from now on unless the developer's <c>--no-predict</c> (or a tape made without it) says not.</summary>
    public static bool Enabled { get; set; } = true;

    bool on = Enabled;

    /// <summary>Whether this machine predicts her swing: <see cref="Enabled"/> when it was made, and her walk's prediction on (whose numbers it uses).</summary>
    public bool On { get => on && walk.On; set => on = value; }

    /// <summary>How much of her weapon's reach is kept in hand (units): the host's copy of her and of the target may stand a few units off hers (<see cref="OwnWalk.Noise"/>).</summary>
    public const float ReachMargin = 12f;

    /// <summary>How long the target must have stood still in the host's snapshots (its milliseconds) to be guessed in reach on the host too.</summary>
    public const int StillMs = 250;

    /// <summary>
    /// How long past a strike of hers a click is not guessed (its reload, and this): **the held button's repeat** is her machine's
    /// own next attack order, sent at her swing's end (<see cref="Actions"/>' ACTION_SWING; the host
    /// repeats nobody's attack by itself), and it is not guessed — the host's strike starts it here, once; nor is a click that may
    /// meet that order at the host. Earlier, the host repeated a player's attack itself until she walked.
    /// </summary>
    public const int RepeatGapMs = 500;

    /// <summary>The tally of her swings guessed here and what came of them.</summary>
    public sealed class Counts
    {
        /// <summary>Swings started here at her click.</summary>
        public int Predicted;
        /// <summary>Borne out by the host's strike.</summary>
        public int Confirmed;
        /// <summary>Taken back: the host's answer with no strike (refused or busy; started as an approach), a strike or throw at another target, no answer.</summary>
        public int Refused, Approached, Differed, Unanswered;
        /// <summary>All taken back.</summary>
        public int TakenBack => Refused + Approached + Differed + Unanswered;
    }

    /// <summary>What it did (for the measurement and the tests).</summary>
    public Counts Done { get; } = new();

    // The guess not yet borne out or taken back.
    ushort number, numberBefore;
    WorldThing? target;
    int mode = -1, startedMs, swingMs, sound, busyBefore;
    int lastStruckMs = int.MinValue / 2;
    bool pending, confirmed;

    /// <summary>Whether a swing of hers started here waits for the host's word.</summary>
    public bool Pending => pending;

    /// <summary>Whether <paramref name="thing"/> is her own character, whose swings this predicts.</summary>
    public bool Is(WorldThing thing) => On && ReferenceEquals(me(), thing);

    /// <summary>Forgets a guess (a journey: <see cref="GameClient.Enter"/>).</summary>
    public void Attach()
    {
        pending = confirmed = false;
        target = null;
        lastStruckMs = int.MinValue / 2;
    }

    /// <summary>
    /// Whether her attack on <paramref name="target"/> will certainly swing at once on the host (asked before the order goes): she
    /// is free, in nothing else, with this weapon in hand and a melee attack that cannot become a throw, and the target is one she
    /// may attack, standing still on her screen and in reach with <see cref="ReachMargin"/> to spare.
    /// </summary>
    public bool Sure(WorldThing her, WorldThing target, Weapon? weapon, int attack)
    {
        if (!On || pending || paused() || mirror() is not { } world || !ReferenceEquals(me(), her)) return false;
        if (her.Actor is not { } a || her.Animation is not { } anim || her.Place is not { Motions: not null } place) return false;
        int now = place.Milliseconds;
        if (a.BusyUntil > now || a.Dead || a.Has2(Actor.Flag2.Restrained) || a.Has(Actor.Flag.Ethereal)) return false;
        if (a.Action is not (ActionMode.None or ActionMode.Move)) return false;
        if (anim.Action is { } playing && !Anim.ChatLips.Local(playing)) return false;
        if (weapon is null || !ReferenceEquals(weapon, a.Weapon)) return false;
        var swing = weapon.Attacks[attack >= 0 && attack < weapon.AttackCount ? attack : weapon.CurrentAttack];
        if (swing.Is(WeaponFlag.Ranged)) return false;
        if (target.Type is not (ThingType.Actor or ThingType.Player or ThingType.Container) || !target.InWorld
            || target.Has(WorldThing.Flag.Remove) || ReferenceEquals(target, her)) return false;
        if (target.IsActor && (target.Actor!.Flags & Actions.Unattackable) != 0) return false;
        if (target.Type is ThingType.Actor or ThingType.Player && swing.Is(WeaponFlag.CanSpecialMove)) return false;
        if (now - lastStruckMs < swing.ReloadTime + RepeatGapMs) return false;
        if (!world.Still(target, StillMs)) return false;
        if (!Bodies.InReach(her, target, swing.Reach(attackerMoving: false) - ReachMargin)) return false;
        numberBefore = walk.Last;
        return true;
    }

    /// <summary>
    /// Her swing started here as the order went (<see cref="Sure"/> said yes): the attack selected, the swing begun
    /// (<see cref="Actions.StartSwing"/>), and she stands where she is until the host answers.
    /// </summary>
    public void Start(WorldThing her, WorldThing at, Weapon weapon, int attack, int nowMs, Anim.MotionSets? motions)
    {
        // The order went numbered (it is not when it could not be sent): the host will answer it.
        if (her.Actor is not { } a || mirror() is not { } world || walk.Last == numberBefore) return;
        number = walk.Last;
        busyBefore = a.BusyUntil;
        weapon.Select(attack);
        swingMs = Actions.StartSwing(her, at, weapon, nowMs, motions, out sound);
        mode = her.Animation?.Action?.Mode.Index ?? -1;
        startedMs = nowMs;
        target = at;
        pending = true;
        confirmed = false;
        walk.Stand(her);
        // The host's snapshots sent before its strike do not stop the swing, nor does its snapshot of the swing start it again.
        world.Replayed(her);
        Done.Predicted++;
    }

    /// <summary>
    /// The host's strike by <paramref name="body"/> at <paramref name="at"/> replayed here (<see cref="ClientArms"/>): true when it is
    /// her swing started here, which it bears out (the caller does the rest of it without starting it again, with
    /// <see cref="SwingMs"/>); a strike of hers at another target takes the guess back first, and is replayed as any.
    /// </summary>
    public bool Struck(WorldThing body, WorldThing? at)
    {
        if (!ReferenceEquals(me(), body)) return false;
        if (body.Place is { } place) lastStruckMs = place.Milliseconds;
        if (!pending || confirmed) return false;
        if (!ReferenceEquals(at, target)) { TakeBack(ref Done.Differed); return false; }
        confirmed = true;
        Done.Confirmed++;
        return true;
    }

    /// <summary>The length of the swing started here (for the scripts' <c>beginswing</c> when the host's strike comes).</summary>
    public int SwingMs => swingMs;

    /// <summary>The host's throw by <paramref name="body"/> (<see cref="ClientArms"/>): a swing of hers guessed here is taken back before the grab plays.</summary>
    public void Thrown(WorldThing body)
    {
        if (pending && !confirmed && ReferenceEquals(me(), body)) TakeBack(ref Done.Differed);
    }

    /// <summary>The host's answer to a numbered order (<see cref="Msg.Taken"/>, after any strike it made): the guess borne out, or taken back.</summary>
    public void Taken(ushort answered, OrderResult result)
    {
        if (!pending || answered != number) return;
        if (confirmed) { pending = false; target = null; return; }
        if (result == OrderResult.Started) TakeBack(ref Done.Approached);
        else TakeBack(ref Done.Refused);
    }

    /// <summary>One frame (<see cref="GameClient.Poll"/>): a guess the host has not answered in time is taken back.</summary>
    public void Step()
    {
        if (!pending || me() is not { Place: { } place }) return;
        if (place.Milliseconds - startedMs > OwnWalk.AnswerWaitMs)
        {
            if (confirmed) { pending = false; target = null; }
            else TakeBack(ref Done.Unanswered);
        }
    }

    /// <summary>A guess the host did not bear out, undone: the swing stopped and silenced, her action, target and busy time put back.</summary>
    void TakeBack(ref int count)
    {
        count++;
        pending = false;
        var at = target;
        target = null;
        if (me() is not { Actor: { } a } her) return;
        if (her.Animation is { Action: { } playing } anim && playing.Mode.Index == mode) anim.StopAction();
        if (sound != 0) her.Place?.Audio?.Mixer.Stop(sound);
        sound = 0;
        if (a.Action == ActionMode.Swing) a.Action = ActionMode.None;
        if (ReferenceEquals(a.Target, at)) a.Target = null;
        a.BusyUntil = busyBefore;
        mirror()?.Forget(her);
    }
}
