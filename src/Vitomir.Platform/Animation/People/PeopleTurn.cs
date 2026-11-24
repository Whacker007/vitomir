// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours, on the earlier build's legs' turn (LegTurn) and the models repository's turns on the spot (its
// rule: the root bone carries the turn, and the one-frame double-turn trap).
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Animation.People;

/// <summary>
/// <b>The legs' turn of a person</b> (<see cref="LegTurn"/> for the models repository's bodies). by default the turn is a
/// chain that catches up (<see cref="StepChain"/>, <see cref="TurnChain"/>); what follows is the earlier build's (<see cref="Step"/>,
/// <c>--turn-chain-off</c>), which the chain builds on. The chest faces the view at once, as 1.1's kinds' do; what differs is the legs:
/// <list type="bullet">
/// <item><b>Moving</b>, the legs ease back to the facing (<see cref="LegTurnSettings.Rate"/>): the eight directions' clips turn
/// the hips toward the way themselves (<see cref="Gait"/>). A gait without its directions turns the legs as 1.1's kinds'.</item>
/// <item><b>Standing</b>, the legs keep their place in the world while the body turns, until the view has left them behind by
/// more than <see cref="LegTurnSettings.PersonTurnFrom"/> (45°); then the turn on the spot that fits plays
/// (<see cref="TurnSet.Clip"/>), and <b>the root bone's turn in it is taken into the legs' turn every tick</b>, by how much it
/// turned since the last (<see cref="TurnClip.Yaw"/>): the clip is drawn in place, so its steps carry the feet round over the
/// ground while the legs' turn carries the body. Nothing is added at the clip's end — the models repository's one-frame
/// double-turn trap cannot happen, because the root's turn is never both drawn and given. When it ends the legs are on the
/// facing (it was played for the degrees they needed); if the view moved on meanwhile by more than
/// <see cref="LegTurnSettings.PersonTurnOn"/> (20°), the next turn follows, else the stand. Moving cancels a turn into the walk
/// (the <see cref="AnimationSystem"/> fades to it as from any motion).</item>
/// <item><b>Crouched</b>, the crouched turns the same way from the crouch held or the kneel (<see cref="PersonCrouch"/>), ending in the
/// crouch held; without them (or with no turns at all) the legs catch up at a rate as 1.1's kinds' do.</item>
/// </list>
/// The turn is kept where 1.1's kinds keep theirs (<see cref="PoseRig.LegYaw"/>, <see cref="PoseRig.LegFrom"/>,
/// <see cref="PoseRig.LegMode"/>), the clip in the <see cref="CharacterAnimation"/> (<see cref="MotionRole.Turn"/>, its clip and
/// time, moved on here: the <see cref="AnimationSystem"/> leaves a standing person's turn alone); nothing new is digested.
/// </summary>
public static class PeopleTurn
{
    /// <summary>
    /// One tick of a person's legs' turn (<see cref="LegTurnSystem"/>): the rig's turn and, while it turns on the spot, the
    /// animation's turn clip moved on; <paramref name="fadeSeconds"/> is the animation's fade (a turn begun or ended fades as
    /// any change of motion).
    /// </summary>
    public static void Step(ref PoseRig rig, ref CharacterAnimation anim, CharacterAssets assets, CharacterKind kind, PersonMotion person,
                            in MotionInput input, float yaw, LegTurnSettings s, float seconds, float fadeSeconds)
    {
        if (!s.On)
        {
            rig.LegYaw = 0f;
            rig.LegFrom = 0f;
            rig.LegMode = LegMode.Unset;
            return;
        }
        if (rig.LegMode == LegMode.Unset)
        {
            rig.LegYaw = 0f;
            rig.LegFrom = yaw;
            rig.LegMode = LegMode.Follow;
        }
        // The legs keep their place in the world as the body turns: the turn of the body this tick taken off them.
        float rel = DetMath.WrapAngle(rig.LegYaw - DetMath.WrapAngle(yaw - rig.LegFrom));
        rig.LegFrom = yaw;

        bool grounded = input.Grounded && input.Traversal == TraversalKind.None && !input.Dodging;
        var role = anim.Role;
        bool moving = grounded && role is MotionRole.Walk or MotionRole.Run or MotionRole.CrouchWalk;
        if (!grounded || moving)
        {
            // The clips of the eight directions turn the hips themselves: the legs go back to the facing, or by the rest of the angle
            // where a forward clip or a backpedal plays alone (Gait); a gait without its directions turns them as 1.1's kinds'.
            float target = !moving ? 0f
                : person.GaitOf(role) is null || person.AheadOnly(anim.Clip) ? LegTurn.Toward(input.ForwardSpeed, input.SideSpeed, input.GroundSpeed, s)   // the sprint is drawn ahead only (and the player's own gaits)
                : anim.Clip >= 0 && assets.Clip(anim.Clip) is GaitBlendClip blend ? blend.Turn : 0f;
            rig.LegMode = LegMode.Follow;
            rig.LegYaw = Math.Clamp(Towards(rel, target, s.Rate * seconds), -s.MostTwist, s.MostTwist);
            return;
        }

        // Standing, the standing turns; crouched (the crouch held or its kneel), the crouched turns.
        var turns = role == MotionRole.Turn && anim.Clip >= 0 ? person.TurnsOf(anim.Clip) : null;
        var crouch = person.Crouch;
        var crouchTurns = role == MotionRole.Crouch && crouch?.Turns is { } ct && anim.Clip >= 0 && (anim.Clip == crouch.Hold || anim.Clip == crouch.Kneel) ? ct : null;
        if (turns is not null && assets.Clip(anim.Clip) is TurnClip turning)
        {
            float t0 = anim.Time, d = turning.Duration;
            if (t0 < d)
            {
                float t1 = MathF.Min(t0 + seconds, d);
                rel += turning.Yaw(t1) - turning.Yaw(t0);
                anim.Time = t1;
                rig.LegMode = LegMode.CatchUp;
            }
            else if (MathF.Abs(rel) > s.PersonTurnOn) rel = Begin(ref anim, turns, assets, rel, seconds, fadeSeconds);   // the view moved on: the next turn
            else
            {
                // Back to the stand, or a crouched turn back to the crouch held (it settles into the kneel after).
                if (crouch is not null && ReferenceEquals(turns, crouch.Turns) && crouch.Hold >= 0) Switch(ref anim, MotionRole.Crouch, crouch.Hold, 0f, fadeSeconds);
                else Stand(ref anim, kind, fadeSeconds);
                rig.LegMode = LegMode.Follow;
            }
        }
        else if (person.Turns is { } standing && role == MotionRole.Idle && MathF.Abs(rel) > s.PersonTurnFrom)
        {
            rel = Begin(ref anim, standing, assets, rel, seconds, fadeSeconds);
            rig.LegMode = LegMode.CatchUp;
        }
        else if (crouchTurns is not null && MathF.Abs(rel) > s.PersonTurnFrom)
        {
            rel = Begin(ref anim, crouchTurns, assets, rel, seconds, fadeSeconds);
            rig.LegMode = LegMode.CatchUp;
        }
        else if (person.Turns is null || role != MotionRole.Idle)
        {
            // Crouched without the turns, or a set without turns: 1.1's kinds' catch-up at a rate.
            float target = rel, rate = s.Rate;
            if (rig.LegMode == LegMode.CatchUp || MathF.Abs(rel) > s.StandTwist)
            {
                rig.LegMode = LegMode.CatchUp;
                target = 0f;
                rate = s.CatchUpRate;
            }
            rel = Towards(rel, target, rate * seconds);
            if (rig.LegMode == LegMode.CatchUp && rel == 0f) rig.LegMode = LegMode.Follow;
        }
        else rig.LegMode = LegMode.Follow;   // planted
        rig.LegYaw = Math.Clamp(rel, -s.MostTwist, s.MostTwist);
    }

    /// <summary>
    /// (The design note.) <b>One tick of a person's turn as a chain that catches up</b>. The body's
    /// facing is the view, as before (what aims, moves and swings); the drawn body's parts each chase it, in order, at limited rates:
    /// <list type="number">
    /// <item><b>The head</b> (<see cref="PersonTurn.Head"/>) chases the view, a critically damped chase of lag
    /// <see cref="LegTurnSettings.HeadLag"/> (so it eases off and on, never snaps) at most <see cref="LegTurnSettings.HeadRate"/>, and is never
    /// more than <see cref="LegTurnSettings.NeckMost"/> ahead of the chest: further, it waits there.</item>
    /// <item><b>The chest</b> (<see cref="PersonTurn.Chest"/>) chases the head the same way (lag <see cref="LegTurnSettings.ChestLag"/>, at most
    /// <see cref="LegTurnSettings.ChestRate"/>), never more than <see cref="LegTurnSettings.SpineMost"/> ahead of the hips.</item>
    /// <item><b>The hips and legs</b> (<see cref="PoseRig.LegYaw"/>) as before (below), with three changes standing: the turn on the spot
    /// starts at <see cref="LegTurnSettings.PersonTurnFrom"/> (30°) and <b>plays faster the further the hips are behind</b> (its own rate
    /// there, up to <see cref="LegTurnSettings.TurnMostSpeedup"/> times it from <see cref="LegTurnSettings.PivotFrom"/>) and <b>is re-aimed
    /// every tick</b> at the view as it is now (<see cref="TurnSet.Retarget"/>: the same steps, their turn stretched or shrunk within
    /// what the 45's or the 90's steps are played for, so it ends on the view and not where the view was when it began); <b>it is turned
    /// round</b> when the view has gone back so far that it would carry the legs more than <see cref="LegTurnSettings.PersonTurnOn"/> past
    /// the view — the turn begun afresh from where the legs are, toward the view, faded as any change (or the stand, with the legs
    /// within <see cref="LegTurnSettings.PersonTurnOn"/> of it); and <b>past <see cref="LegTurnSettings.PivotFrom"/></b> the legs pivot on
    /// their own as well, at <see cref="LegTurnSettings.PivotRate"/>, back to it. Moving, the legs are the earlier build's.</item>
    /// </list>
    /// Nothing is held by <see cref="LegTurnSettings.MostTwist"/> any more: the chain's own limits hold it (the hips are kept within
    /// half a turn of the view, the one way they can be told apart). So a 180° flick leaves the whole body catching up for a moment.
    /// And the hips are never more than <see cref="LegTurnSettings.Leash"/> (90°) behind the view, whatever the view does (a play-test spun
    /// it at up to 1,800°/s for seconds and the body stood side-on and backwards): past it the body is carried round with the view.
    /// The angles are from the facing, toward its left, and keep their place in the world as the body turns (as the legs' always
    /// have); a turn clip's own upper-body lead is taken out of the pose (<see cref="TurnChain"/>). Deterministic, allocates nothing.
    /// </summary>
    public static void StepChain(ref PoseRig rig, ref CharacterAnimation anim, ref PersonTurn turn, CharacterAssets assets, CharacterKind kind, PersonMotion person,
                                 in MotionInput input, float yaw, LegTurnSettings s, float seconds, float fadeSeconds)
    {
        if (!s.On)
        {
            rig.LegYaw = 0f;
            rig.LegFrom = 0f;
            rig.LegMode = LegMode.Unset;
            turn = default;
            return;
        }
        if (rig.LegMode == LegMode.Unset || !turn.On)
        {
            rig.LegYaw = 0f;
            rig.LegFrom = yaw;
            rig.LegMode = LegMode.Follow;
            turn = default;
            turn.On = true;
        }
        // Every part keeps its place in the world as the body turns: the body's turn this tick taken off each.
        float turned = DetMath.WrapAngle(yaw - rig.LegFrom);
        rig.LegFrom = yaw;
        float rel = rig.LegYaw - turned;
        float head = turn.Head - turned, chest = turn.Chest - turned;

        bool grounded = input.Grounded && input.Traversal == TraversalKind.None && !input.Dodging;
        var role = anim.Role;
        bool moving = grounded && role is MotionRole.Walk or MotionRole.Run or MotionRole.CrouchWalk;
        if (!grounded || moving)
        {
            // As the earlier build's: the eight directions' clips turn the hips themselves; the legs ease back to the facing (or the rest of the angle).
            float target = !moving ? 0f
                : person.GaitOf(role) is null || person.AheadOnly(anim.Clip) ? LegTurn.Toward(input.ForwardSpeed, input.SideSpeed, input.GroundSpeed, s)   // the sprint is drawn ahead only (and the player's own gaits)
                : anim.Clip >= 0 && assets.Clip(anim.Clip) is GaitBlendClip blend ? blend.Turn : 0f;
            rig.LegMode = LegMode.Follow;
            rel = Towards(rel, target, s.Rate * seconds);
        }
        else rel = Standing(ref rig, ref anim, assets, kind, person, rel, s, seconds, fadeSeconds);
        rel = Math.Clamp(rel, -DetMath.Pi, DetMath.Pi);
        // Never more than the leash behind the view: past it the whole body is carried round with the view.
        if (s.Leashed) rel = Math.Clamp(rel, -s.Leash, s.Leash);
        rig.LegYaw = rel;

        // The chain above the hips: the head after the view, the chest after the head, each within its limit of the one below.
        float head0 = head, chest0 = chest;
        Chase(ref head, ref turn.HeadSpeed, 0f, s.HeadLag, s.HeadRate, seconds);
        float chasedHead = head;
        head = Math.Clamp(head, chest - s.NeckMost, chest + s.NeckMost);
        Chase(ref chest, ref turn.ChestSpeed, head, s.ChestLag, s.ChestRate, seconds);
        float chased = chest;
        chest = Math.Clamp(chest, rel - s.SpineMost, rel + s.SpineMost);
        if (chest != chased) turn.ChestSpeed = (chest - chest0) / seconds;
        float held = Math.Clamp(head, chest - s.NeckMost, chest + s.NeckMost);
        if (held != chasedHead) turn.HeadSpeed = (held - head0) / seconds;   // a limit held it: the speed it really had
        turn.Head = held;
        turn.Chest = chest;
    }

    /// <summary>
    /// A standing (or crouched) person's legs for one tick of the chain (<see cref="StepChain"/>): the turn on the spot sped up,
    /// turned round, chained, begun from <see cref="LegTurnSettings.PersonTurnFrom"/>; 1.1's kinds' catch-up for a set without turns;
    /// the pivot past <see cref="LegTurnSettings.PivotFrom"/>. Answers the legs' turn after it.
    /// </summary>
    static float Standing(ref PoseRig rig, ref CharacterAnimation anim, CharacterAssets assets, CharacterKind kind, PersonMotion person, float rel,
                          LegTurnSettings s, float seconds, float fadeSeconds)
    {
        var role = anim.Role;
        var turns = role == MotionRole.Turn && anim.Clip >= 0 ? person.TurnsOf(anim.Clip) : null;
        var crouch = person.Crouch;
        var crouchTurns = role == MotionRole.Crouch && crouch?.Turns is { } ct && anim.Clip >= 0 && (anim.Clip == crouch.Hold || anim.Clip == crouch.Kneel) ? ct : null;
        if (turns is not null && assets.Clip(anim.Clip) is TurnClip turning)
        {
            float t0 = anim.Time, d = turning.Duration;
            float way = turning.Turn >= 0f ? 1f : -1f;
            bool ended = t0 >= d;
            // The view gone back: the rest of the clip would carry the legs past it by more than PersonTurnOn — begun afresh toward it.
            bool overshoots = !ended && way * (rel + turning.Yaw(d) - turning.Yaw(t0)) > s.PersonTurnOn;
            if (!ended && !overshoots)
            {
                // The same steps turned for what the legs need now (the view moved on, or back a little), then on, faster the further behind.
                int aimed = turns.Retarget(anim.Clip, turning, t0, rel);
                if (aimed != anim.Clip && assets.Clip(aimed) is TurnClip retargeted) (anim.Clip, turning) = (aimed, retargeted);
                float t1 = MathF.Min(t0 + seconds * Speedup(rel, s), d);
                rel += turning.Yaw(t1) - turning.Yaw(t0);
                anim.Time = t1;
                rig.LegMode = LegMode.CatchUp;
            }
            else if (MathF.Abs(rel) > s.PersonTurnOn) rel = Begin(ref anim, turns, assets, rel, seconds, fadeSeconds);   // the next turn, or the turn the other way
            else
            {
                if (crouch is not null && ReferenceEquals(turns, crouch.Turns) && crouch.Hold >= 0) Switch(ref anim, MotionRole.Crouch, crouch.Hold, 0f, fadeSeconds);
                else Stand(ref anim, kind, fadeSeconds);
                rig.LegMode = LegMode.Follow;
            }
        }
        else if (person.Turns is { } standing && role == MotionRole.Idle && MathF.Abs(rel) > s.PersonTurnFrom)
        {
            rel = Begin(ref anim, standing, assets, rel, seconds, fadeSeconds);
            rig.LegMode = LegMode.CatchUp;
        }
        else if (crouchTurns is not null && MathF.Abs(rel) > s.PersonTurnFrom)
        {
            rel = Begin(ref anim, crouchTurns, assets, rel, seconds, fadeSeconds);
            rig.LegMode = LegMode.CatchUp;
        }
        else if (person.Turns is null || role != MotionRole.Idle)
        {
            // Crouched without the turns, a set without turns, a landing: 1.1's kinds' catch-up at a rate.
            float target = rel, rate = s.Rate;
            if (rig.LegMode == LegMode.CatchUp || MathF.Abs(rel) > s.StandTwist)
            {
                rig.LegMode = LegMode.CatchUp;
                target = 0f;
                rate = s.CatchUpRate;
            }
            rel = Towards(rel, target, rate * seconds);
            if (rig.LegMode == LegMode.CatchUp && rel == 0f) rig.LegMode = LegMode.Follow;
        }
        else rig.LegMode = LegMode.Follow;   // planted
        // Past what the clips can follow: the legs pivot on their own as well, back to PivotFrom.
        if (MathF.Abs(rel) > s.PivotFrom) rel = Towards(rel, MathF.Sign(rel) * s.PivotFrom, s.PivotRate * seconds);
        return rel;
    }

    /// <summary>
    /// How much faster than its own rate a turn on the spot plays with the hips <paramref name="rel"/> off the view: 1 at
    /// <see cref="LegTurnSettings.PersonTurnFrom"/> or less, rising evenly to <see cref="LegTurnSettings.TurnMostSpeedup"/> at
    /// <see cref="LegTurnSettings.PivotFrom"/> and beyond.
    /// </summary>
    public static float Speedup(float rel, LegTurnSettings s)
    {
        float most = MathF.Max(1f, s.TurnMostSpeedup), span = s.PivotFrom - s.PersonTurnFrom;
        if (span <= 1e-6f) return MathF.Abs(rel) > s.PersonTurnFrom ? most : 1f;
        float share = Math.Clamp((MathF.Abs(rel) - s.PersonTurnFrom) / span, 0f, 1f);
        return 1f + (most - 1f) * share;
    }

    /// <summary>
    /// One tick of a part's chase of <paramref name="target"/> (radians; <paramref name="speed"/> its speed): critically damped,
    /// trailing a steady turn by <paramref name="lag"/> seconds (its rate 2 ÷ lag), stepped exactly over the tick, its speed held to
    /// <paramref name="rate"/> a second and gathered no faster than to the whole rate in <paramref name="lag"/> (so a flick far off the
    /// part does not start it at full speed in a tick: it eases on; it eases off as the chase's own). Never past the target. A lag of 0
    /// or less goes straight to the target at the rate.
    /// </summary>
    public static void Chase(ref float angle, ref float speed, float target, float lag, float rate, float seconds)
    {
        float x0 = angle - target, most = rate * seconds;
        if (lag <= 0f)
        {
            float jump = Math.Clamp(-x0, -most, most);
            angle += jump;
            speed = jump / seconds;
            return;
        }
        float w = 2f / lag, e = DetMath.Exp(-w * seconds), k = speed + w * x0;
        float x1 = (x0 + k * seconds) * e;
        float v1 = Math.Clamp((speed - w * k * seconds) * e, -rate, rate);
        float gather = rate / lag * seconds;
        if (MathF.Abs(v1) > MathF.Abs(speed) && v1 * speed >= 0f && MathF.Abs(v1 - speed) > gather)
        {
            // Speeding up faster than it may gather: the speed by what it may gather, the step its mean over the tick.
            v1 = speed + MathF.Sign(v1 - speed) * gather;
            x1 = x0 + (speed + v1) * 0.5f * seconds;
            if (x1 * x0 < 0f) (x1, v1) = (0f, 0f);
        }
        angle = target + x0 + Math.Clamp(x1 - x0, -most, most);
        speed = v1;
    }

    /// <summary>
    /// Starts the turn that brings legs <paramref name="rel"/> radians off the facing back onto it, fading from what plays, and
    /// moves it on by this tick (so it starts now, not a tick late); answers the legs' turn after it.
    /// </summary>
    static float Begin(ref CharacterAnimation a, TurnSet turns, CharacterAssets assets, float rel, float seconds, float fadeSeconds)
    {
        int clip = turns.Clip(-rel);
        Switch(ref a, MotionRole.Turn, clip, 0f, fadeSeconds);
        if (assets.Clip(clip) is TurnClip turn)
        {
            float t1 = MathF.Min(seconds, turn.Duration);
            rel += turn.Yaw(t1) - turn.Yaw(0f);
            a.Time = t1;
        }
        return rel;
    }

    /// <summary>Back to the stand from a finished turn, fading as any change of motion.</summary>
    static void Stand(ref CharacterAnimation a, CharacterKind kind, float fadeSeconds)
    {
        var idle = kind.Motion(MotionRole.Idle);
        Switch(ref a, MotionRole.Idle, idle.Clip, idle.StartSeconds, fadeSeconds);
        a.Rate = 1f;
    }

    /// <summary>As the <see cref="AnimationSystem"/> starts a motion: the one playing fades out while it keeps playing (a turn's held at its end).</summary>
    static void Switch(ref CharacterAnimation a, MotionRole role, int clip, float time, float fadeSeconds)
    {
        if (a.Clip >= 0 && fadeSeconds > 0f)
        {
            a.PreviousClip = a.Clip;
            a.PreviousTime = a.Time;
            a.PreviousRate = a.Role == MotionRole.Turn ? 0f : a.Rate;
            a.Blend = 0f;
        }
        else
        {
            a.PreviousClip = -1;
            a.Blend = 1f;
        }
        a.Role = role;
        a.Clip = clip;
        a.Time = time;
        a.Rate = 0f;
        a.RateScale = 1f;
    }

    static float Towards(float value, float target, float step) =>
        value < target ? MathF.Min(target, value + step) : MathF.Max(target, value - step);
}
