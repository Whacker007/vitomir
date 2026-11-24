// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The start of a player's own discipline predicted on her machine ("let's go ahead with the full fix
// for release 1"). 1.1's player's machine runs the cast order as far as the discipline's own
// `cancast`, sends message 0x18 and then plays the cast's start there too; Vitomir's sent the cast at the top
// of the order and showed nothing until the host's snapshot had her casting.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Net;

/// <summary>
/// **Her own discipline's start, at once, on her machine** (a machine guesses only what its own player's input certainly
/// causes). A cast that the host will certainly begin at once — by the same rules the host's cast order uses (<see cref="Cast.Order"/>),
/// asked of her machine's own copy of the world — starts the spell animation here and turns her to face its target or point, as the
/// cast goes (numbered, <see cref="NetCast.Seq"/>). 1.1's order makes no sound at the start (the discipline's
/// sound is the fire's); a noise the spell animation's own keyframes make plays as on any machine, and is stopped with it
/// when the guess is taken back. **Everything else waits for the host**, as before: the blood spent, the effect, any projectile or
/// visual of it, damage, the target's reaction, every roll (the fire is the host's: <see cref="Cast.Fire"/> does nothing on a
/// player's machine).
/// <list type="bullet">
/// <item>**Certain** means, on her machine: she is free (not busy, dead or held, doing nothing but standing or walking, nothing on
/// her action channel, nothing of hers on its way to the host that may keep her busy there, the game not paused, no swing of hers
/// guessed and unanswered); the discipline is hers and not a scroll's; <see cref="Cast.Check"/> says yes — not `NODISC`, no shape, not
/// beheaded, the stat checks, the level, **her recast clock** (her machine's copy: <see cref="Taken"/>, <see cref="Sheet"/>) and
/// **her blood** (the sheet's, less what her casts the host has begun since will spend: <see cref="Blood"/>); the target, by the
/// discipline's flags: an area's point or a thing standing still in the host's snapshots (<see cref="StillMs"/>), a thing in range
/// with <see cref="RangeMargin"/> to spare and one the power may target (<see cref="DisciplineTarget.MayTarget"/>; not a body, whose
/// fade clock is the host's, nor an item, the pack being the host's); and **the discipline's own <c>cancast</c>**, run here on her
/// copy, silently, through <see cref="IScriptObjects.Judge"/>, which answers nothing when the script read anything her machine does
/// not have (counted, <see cref="Counts.Unjudged"/>); and her model has the spell animation to play.</item>
/// <item>**The host's answer** (<see cref="Msg.Taken"/>, with why: <see cref="CastWhy"/>, the recast clock and how long the cast holds
/// her) begun **bears the guess out**: her recast clock set from it, and the host's snapshot of the cast does not start it again
/// (<see cref="WorldMirror.Replayed"/>); at the cast's end her copy is out of ACTION 13, as one the host alone cast leaves it.</item>
/// <item>**Taken back** when the host refuses (busy, no blood, the recast clock, <c>cancast</c>, the target gone or not one, the
/// Storyteller's body …), does otherwise (walks her into range; fires at once with no animation) or does not answer in
/// <see cref="OwnWalk.AnswerWaitMs"/>: the animation stopped, its noise stopped, her action, target, busy time and recast clock put
/// back (a refusal for the clock sets it from the host's), her mark let go so the snapshots have her again; counted. Nothing of the
/// effect was shown.</item>
/// </list>
/// Deterministic under a tape (her machine's state and the host's messages only); a tape says whether it was on
/// (<c>netpredict on strikes casts</c>). On by default with her walk's, outside the faithful, Fixes and QoL settings;
/// <c>--no-predict</c> turns it off with her walk and her swing.
/// </summary>
public sealed class OwnCast(Func<WorldThing?> me, OwnWalk walk, OwnStrike strikes, Func<WorldMirror?> mirror, Func<bool> paused)
{
    /// <summary>On for every player's machine made from now on unless the developer's <c>--no-predict</c> (or a tape made without it) says not.</summary>
    public static bool Enabled { get; set; } = true;

    bool on = Enabled;

    /// <summary>Whether this machine predicts her casts: <see cref="Enabled"/> when it was made, and her walk's prediction on (whose numbers it uses).</summary>
    public bool On { get => on && walk.On; set => on = value; }

    /// <summary>How much of a discipline's range is kept in hand (units): the host's copy of her and of the target may stand a few units off hers (three of <see cref="OwnWalk.Noise"/>).</summary>
    public const float RangeMargin = 24f;

    /// <summary>How long a target must have stood still in the host's snapshots (its milliseconds) to be guessed where she sees it: the swing's (<see cref="OwnStrike.StillMs"/>).</summary>
    public const int StillMs = OwnStrike.StillMs;

    /// <summary>The tally of her casts guessed here and what came of them.</summary>
    public sealed class Counts
    {
        /// <summary>Casts started here at her click.</summary>
        public int Predicted;
        /// <summary>Borne out by the host's answer.</summary>
        public int Confirmed;
        /// <summary>Taken back: refused, walked into range instead, otherwise (fired at once there), no answer.</summary>
        public int Refused, Approached, Differed, Unanswered;
        /// <summary>Not guessed because the discipline's <c>cancast</c> read what her machine does not have (or her machine has no script for it).</summary>
        public int Unjudged;
        /// <summary>The refusals by the host's reason.</summary>
        public readonly Dictionary<CastWhy, int> RefusedFor = [];
        /// <summary>All taken back.</summary>
        public int TakenBack => Refused + Approached + Differed + Unanswered;
    }

    /// <summary>What it did (for the measurement and the tests).</summary>
    public Counts Done { get; } = new();

    /// <summary>What <see cref="Sure"/> found: the discipline, its target or point, and what it costs her now.</summary>
    public readonly record struct Guess(Discipline Inst, WorldThing? Target, Vector3? Point, float Cost);

    // The guess not yet answered, and what it changed.
    ushort number, numberBefore;
    Discipline? inst;
    WorldThing? target, targetBefore;
    ActionMode actionBefore;
    int busyBefore, recastBefore, startedMs, mode = -1;
    bool pending, casting;
    readonly List<int> sounds = [];

    // Her casts and orders the host has not answered: the host may be busy with one her machine knows nothing of yet.
    readonly Dictionary<ushort, (int SentMs, string? Discipline, float Cost)> unanswered = [];

    // Her casts the host has begun, by its clock: what each will spend, until a sheet made after its end has the blood spent.
    readonly List<(int HostStartMs, int BusyMs, float Cost)> spending = [];

    /// <summary>The console's account (<see cref="GameClient.Log"/>): each guess, and what came of it.</summary>
    public Action<string>? Log { get; set; }

    /// <summary>Whether a cast of hers started here waits for the host's word.</summary>
    public bool Pending => pending;

    /// <summary>A cast of hers guessed and unanswered, or an order or cast of hers the host has not answered (her keyboard walk waits for either, as a guessed cast does).</summary>
    public bool Waiting => pending || unanswered.Count > 0;

    /// <summary>Whether <paramref name="thing"/> is her own character, whose casts this predicts.</summary>
    public bool Is(WorldThing thing) => On && ReferenceEquals(me(), thing);

    /// <summary>Forgets a guess and what was on its way (a journey: <see cref="GameClient.Enter"/>).</summary>
    public void Attach()
    {
        pending = casting = false;
        inst = null;
        target = targetBefore = null;
        unanswered.Clear();
        spending.Clear();
        sounds.Clear();
    }

    /// <summary>
    /// Her blood (a human's mana) as the host will judge it: her machine's (the host's sheet, every <see cref="GameServer.SheetMs"/>),
    /// less what each cast of hers the host has begun and the last sheet may not have seen spent will spend. **It can lag** the other
    /// way: what another takes from her (a feed on her, Theft of Vitae on her) reaches her machine with the next sheet, and a guess
    /// made in between is taken back by the host's refusal.
    /// </summary>
    public float Blood(WorldThing her)
    {
        if (her.Actor is not { } a) return 0f;
        float have = a.Stat[(int)(a.Type == ActorType.Human ? ActorStat.Mana : ActorStat.Blood)];
        foreach (var s in spending) have -= s.Cost;
        return have;
    }

    /// <summary>
    /// Whether the cast she asks for will certainly begin at once on the host (asked before it goes, <see cref="Cast.Order"/>): the
    /// rules of the host's order, on her copy, with the margins above; the discipline to cast is <paramref name="power"/> (the
    /// order's, or her current one). Null when not certain.
    /// </summary>
    public Guess? Sure(WorldThing her, WorldThing? at, Vector3? pos, Discipline? power, InputModifier mods)
    {
        if (!On || pending || strikes.Pending || paused() || mirror() is not { } world || !ReferenceEquals(me(), her)) return No("off, waiting or paused");
        if (her.Actor is not { } a || her.Animation is not { } anim || her.Place is not { Motions: not null } place) return No("no model");
        int now = place.Milliseconds;
        Expire(now);
        if (unanswered.Count > 0) return No("an order of hers unanswered");
        if (a.BusyUntil > now || a.Dead || a.Has2(Actor.Flag2.Restrained)) return No("busy");
        if (a.Action is not (ActionMode.None or ActionMode.Move)) return No($"doing {a.Action}");
        if (anim.Action is { } playing && !Anim.ChatLips.Local(playing)) return No("an action playing");
        if (power is null || (power.Flags & Discipline.Temporary) != 0 || !ReferenceEquals(power.Owner ?? her, her)) return No("no discipline of hers");
        if (Cast.Check(her, power, voice: false, blood: Blood(her)) is var gate and not CastWhy.Begun) return No(gate.ToString());
        var def = power.Definition;
        Vector3? point = null;
        if (def.Has(DisciplineFlags.TargetArea))
        {
            if (at is not null && !Standing(world, at)) return No("the target moving");
            point = at?.Position ?? pos;
            if (point is null) return No("no point");
            at = null;
        }
        else if ((def.Flags & Cast.ThingTargets) != 0)
        {
            if (at is null || at.Type is not (ThingType.Actor or ThingType.Player) || at.Actor is not { } them || !Standing(world, at)) return No("no character standing");
            if (them.Dead && at.Type != ThingType.Player) return No("a body");         // a body's fade clock is the host's
            if (Vector3.Distance((power.Owner ?? her).Position, at.Position) > def.MaxDistance - RangeMargin) return No("out of range");
            if (!DisciplineTarget.MayTarget(def, her, at)) return No("not a target");
        }
        else at = null;
        if (def.Script.Length > 0)
        {
            if (power.Script is not { } script || place.ScriptObjects is not { } scripts) { Done.Unjudged++; return No("no script here"); }
            switch (scripts.Judge(script, power.Level, her, at, point ?? pos))
            {
                case null:
                    Done.Unjudged++;
                    Log?.Invoke($"net: her cast of {def.Name} not started here, its cancast reads what this machine does not have");
                    return No("cancast not judged here");
                case false: return No("cancast");
            }
        }
        numberBefore = walk.Last;
        NotWhy = null;
        return new Guess(power, at, point, Cast.Cost(her, power));

        Guess? No(string why)
        {
            NotWhy = why;
            return null;
        }
    }

    /// <summary>Why the last cast asked about was not guessed (for the console's account and the tests), or null.</summary>
    public string? NotWhy { get; private set; }

    /// <summary>A thing the host's snapshots have had standing where she sees it, in the world.</summary>
    static bool Standing(WorldMirror world, WorldThing thing) =>
        thing.InWorld && !thing.Has(WorldThing.Flag.Remove) && world.Still(thing, StillMs);

    /// <summary>
    /// The cast's start here as it went (<see cref="Sure"/> said yes): the spell animation on her action channel, as the host's order
    /// plays it (<see cref="Cast.PlaySpell"/>), her walk stopped and her turned to the target or the point, ACTION 13, busy for the
    /// animation, her recast clock armed; she stands where she is until the host answers.
    /// </summary>
    public void Start(WorldThing her, Guess guess)
    {
        // The cast went numbered (it is not when it could not be sent): the host will answer it.
        if (her.Actor is not { } a || mirror() is not { } world || walk.Last == numberBefore || her.Place is not { } place) return;
        int now = place.Milliseconds;
        var def = guess.Inst.Definition;
        int was = her.Animation?.Action?.Mode.Index ?? -1;
        int ms = Cast.PlaySpell(her, a, def);
        if (ms < 0) return;                                                    // nothing to show: the host's fire is the start
        number = walk.Last;
        inst = guess.Inst;
        target = guess.Target;
        targetBefore = a.Target;
        actionBefore = a.Action;
        busyBefore = a.BusyUntil;
        recastBefore = inst.RecastUntil;
        her.Mind?.Walker.Stop();
        Actions.StopWalking(her);
        if (guess.Point is { } p) her.Face(p);
        else if (guess.Target is { } t) her.Face(t);
        a.Target = guess.Target;
        a.Action = ActionMode.Cast;
        a.BusyUntil = now + ms;
        inst.RecastUntil = now + def.RecastAt(inst.Level);
        mode = her.Animation?.Action?.Mode.Index ?? was;
        startedMs = now;
        sounds.Clear();
        pending = true;
        casting = false;
        walk.Stand(her);
        // The host's snapshots sent before its start do not stop it, nor does its snapshot of the cast start it again.
        world.Replayed(her);
        Done.Predicted++;
        Log?.Invoke($"net: her cast of {def.Name} started here at her click ({ms} ms)");
    }

    /// <summary>A noise her spell animation's keyframe made while the guess waits (<see cref="Simulation"/>'s animation step), stopped with it if it is taken back.</summary>
    public void Sounded(int channel)
    {
        if (pending && channel != 0) sounds.Add(channel);
    }

    /// <summary>A numbered cast or order of hers went (<see cref="GameClient.Send(NetCast)"/>, <see cref="GameClient.Send(NetOrder)"/>): kept until the host answers.</summary>
    public void Sent(ushort seq, string? discipline, float cost, int nowMs)
    {
        if (seq != 0) unanswered[seq] = (nowMs, discipline, cost);
    }

    /// <summary>
    /// The host's answer to a numbered cast or order of hers (<see cref="Msg.Taken"/>): for a cast, why, its recast clock left
    /// (<paramref name="recastLeftMs"/>, −1 none) and how long it holds her there (<paramref name="busyMs"/>), at the host's
    /// <paramref name="hostMs"/>. Her machine's recast clock is set from it, what a begun cast will spend is kept, and a guess is borne
    /// out or taken back.
    /// </summary>
    public void Taken(ushort answered, OrderResult result, CastWhy why, int recastLeftMs, int busyMs, int hostMs)
    {
        if (!unanswered.Remove(answered, out var sent)) sent = (0, null, 0f);
        bool mine = pending && answered == number;
        bool begun = result == OrderResult.Started && why == CastWhy.Begun;
        if (begun && sent.Discipline is not null) spending.Add((hostMs, busyMs, sent.Cost));
        if (mine)
        {
            string what = inst?.Definition.Name ?? "?";
            if (begun && busyMs > 0)
            {
                pending = false;
                casting = true;
                Done.Confirmed++;
                Log?.Invoke($"net: her cast of {what} borne out by the host");
            }
            else
            {
                if (begun) TakeBack(ref Done.Differed);                        // fired at once there: no animation on the host's copy
                else if (result == OrderResult.Started && why == CastWhy.Approach) TakeBack(ref Done.Approached);
                else
                {
                    TakeBack(ref Done.Refused);
                    Done.RefusedFor[why] = Done.RefusedFor.GetValueOrDefault(why) + 1;
                }
                Log?.Invoke($"net: her cast of {what} taken back, the host's answer {result} ({why})");
            }
        }
        // Her recast clock, the host's word (a round trip late, so it ends here a little after the host's: never too soon).
        if (recastLeftMs >= 0 && sent.Discipline is { } name && me() is { Actor: { } a, Place: { } place } && a.Disciplines[name] is { } power)
        {
            int now = place.Milliseconds;
            if (recastLeftMs > 0) power.RecastUntil = now + recastLeftMs;
            else if ((uint)now < (uint)power.RecastUntil) power.RecastUntil = now;          // over there: over here
        }
    }

    /// <summary>
    /// Her sheet (<see cref="Msg.Sheet"/>) made at the host's <paramref name="hostMs"/>: a begun cast that has ended by then has had its
    /// blood taken there, which the sheet's blood says; and each discipline's recast clock left, which her machine's copy takes
    /// when it is later than its own (a clock her machine armed itself, or the host's answer, is not put back by an older sheet).
    /// </summary>
    public void Sheet(int hostMs, IReadOnlyList<(string Name, int RecastLeftMs)> recasts)
    {
        spending.RemoveAll(s => s.HostStartMs + s.BusyMs <= hostMs);
        if (me() is not { Actor: { } a, Place: { } place }) return;
        foreach (var (name, left) in recasts)
            if (left > 0 && a.Disciplines[name] is { } power && (uint)power.RecastUntil < (uint)(place.Milliseconds + left))
                power.RecastUntil = place.Milliseconds + left;
    }

    /// <summary>
    /// One frame (<see cref="GameClient.Poll"/>): a guess the host has not answered in time is taken back; a borne-out cast ends here
    /// as the host's alone would leave her copy (out of ACTION 13) once its busy time is over; what was on its way too long is let go.
    /// </summary>
    public void Step()
    {
        if (me() is not { Actor: { } a, Place: { } place }) return;
        int now = place.Milliseconds;
        if (pending && now - startedMs > OwnWalk.AnswerWaitMs)
        {
            Log?.Invoke($"net: her cast of {inst?.Definition.Name ?? "?"} taken back, no answer in {OwnWalk.AnswerWaitMs} ms");
            TakeBack(ref Done.Unanswered);
        }
        if (casting && now >= a.BusyUntil)
        {
            casting = false;
            if (a.Action == ActionMode.Cast) a.Action = ActionMode.None;
            if (ReferenceEquals(a.Target, target)) a.Target = targetBefore;
            target = null;
            inst = null;
        }
        Expire(now);
    }

    void Expire(int now)
    {
        if (unanswered.Count == 0) return;
        foreach (var (seq, s) in unanswered.ToList())
            if (now - s.SentMs > OwnWalk.AnswerWaitMs) unanswered.Remove(seq);
    }

    /// <summary>A guess the host did not bear out, undone: the animation stopped and silenced, her action, target, busy time and clock put back.</summary>
    void TakeBack(ref int count)
    {
        count++;
        pending = false;
        casting = false;
        var guessed = target;
        target = null;
        if (me() is not { Actor: { } a } her) return;
        if (her.Animation is { Action: { } playing } anim && playing.Mode.Index == mode) anim.StopAction();
        foreach (int s in sounds) her.Place?.Audio?.Mixer.Stop(s);
        sounds.Clear();
        if (a.Action == ActionMode.Cast) a.Action = actionBefore == ActionMode.Move ? ActionMode.None : actionBefore;
        if (ReferenceEquals(a.Target, guessed)) a.Target = targetBefore;
        a.BusyUntil = busyBefore;
        if (inst is not null) inst.RecastUntil = recastBefore;
        inst = null;
        mirror()?.Forget(her);
    }
}
