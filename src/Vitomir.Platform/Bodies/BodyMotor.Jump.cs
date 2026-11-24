// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// <b>The jump, tapped and held</b> (ours). A <b>tap</b>
/// is a human-sized jump for every body, everywhere (<see cref="Locomotion.TapImpulse"/>), and the obstacle decides the
/// move: on the ground a hop up onto a top in reach, in the air from it the vault, the climb onto, the mantle or the
/// grab the obstacle calls for, else a plain jump. A <b>hold</b> charges: held past <see cref="HoldSeconds"/>, the jump
/// rises, linearly over <see cref="Locomotion.ChargeSeconds"/>, from the tap's height to the body's own jump
/// (<see cref="Locomotion.PoweredHeight"/>); let go, it jumps with the charged height (the powered leap: the moves in the
/// air from it reach, whatever the mode, from the mode's numbers towards the body's own, <see cref="Locomotion.PoweredReach"/>,
/// as far as the charge went, <see cref="MovesReach"/>);
/// cancel ends it and nothing jumps. While charging the body crouches and goes at most at a walk.
///
/// <para>The human has nothing to charge (its own jump is the tap's), so its jump goes <b>on the press</b>, as before.
/// A body that can charge (<see cref="Locomotion.CanCharge"/>) holds a press made on the ground (<see cref="Body.Charging"/>)
/// and jumps <b>on the release</b> of a tap — a delay of up to <see cref="HoldSeconds"/>, the price of a charge. A press
/// in the air is buffered for a landing as before (still held on landing, it is held there); one in the grace after an
/// edge jumps at once. Leaving the ground while a press is held lets it go at once within the grace after the edge
/// (a tap, or the charge so far), else nothing jumps.</para>
/// </summary>
public static partial class BodyMotor
{
    /// <summary>What the jump's press does this tick.</summary>
    enum JumpGo : byte
    {
        None = 0,
        /// <summary>A tap: a hop up when one is in reach, else a jump of <see cref="Locomotion.TapImpulse"/>.</summary>
        Tap = 1,
        /// <summary>A charged jump let go (<see cref="ChargedImpulse"/>).</summary>
        Charged = 2,
    }

    /// <summary><see cref="Body.Charging"/>: a press held, still a tap; and past <see cref="HoldSeconds"/>, a charge (its beginning told).</summary>
    const int PressHeld = 1, Charged = 2;

    /// <summary>Whether <paramref name="body"/> is charging a jump: something cancel ends (<see cref="CanCancel"/>).</summary>
    public static bool IsCharging(in Body body) => body.Charging == Charged && body.State == BodyState.Ground;

    /// <summary>Whether the charge holds the body down this tick (crouched, at most at a walk): charging, jump still down, not cancelled.</summary>
    static bool ChargeHeld(in Body body, in MoveIntent intent) =>
        IsCharging(body) && (intent.JumpHeld || intent.Jump) && !intent.Cancel;

    /// <summary>How high the charge held so far lifts the feet (m): from the tap's height to the body's own, linearly over <see cref="Locomotion.ChargeSeconds"/> from <see cref="HoldSeconds"/>.</summary>
    public static float ChargeHeight(in Body body, in Locomotion loco)
    {
        float tap = loco.TapHeight, most = loco.PoweredHeight;
        float f = loco.ChargeSeconds > 0f ? (body.JumpHeldSeconds - HoldSeconds) / loco.ChargeSeconds : 1f;
        return tap + (most - tap) * Math.Clamp(f, 0f, 1f);
    }

    /// <summary>The upward speed of the charge let go: √(2g·<see cref="ChargeHeight"/>).</summary>
    static float ChargedImpulse(in Body body, in Locomotion loco) => Locomotion.ImpulseFor(ChargeHeight(body, loco), loco.Gravity);

    /// <summary>
    /// How far the charge the body is in the air from went (0 to 1): the jump's height above the tap's over the
    /// powered jump's above the tap's (<see cref="Body.JumpRise"/>, <see cref="Locomotion.TapHeight"/>,
    /// <see cref="Locomotion.PoweredHeight"/>). 0 for a tap or no jump; 1 for a full charge, or for a body whose own jump
    /// is no higher than its tap.
    /// </summary>
    public static float ChargeShare(in Body body, in Locomotion loco)
    {
        if (body.Powered == 0) return 0f;
        float tap = loco.TapHeight, span = loco.PoweredHeight - tap;
        return span > 1e-4f ? Math.Clamp((body.JumpRise - tap) / span, 0f, 1f) : 1f;
    }

    /// <summary>
    /// The numbers the moves in the air and the pull-up reach with (from a walk in play): the body's numbers
    /// now (the mode's: a human's in public), or from a charged jump the reach, the vault and the mantle
    /// <b>between</b> them and the body's own (<see cref="Locomotion.PoweredReach"/>) in the proportion the charge went
    /// (<see cref="ChargeShare"/>). So a press held a hair past <see cref="HoldSeconds"/> (a jump of 0.51 m) reaches as
    /// a tap does — a mantle at a top of 1.3 m in public, not the body's own climb onto it and a breach of the
    /// Masquerade — and a full charge as the body's own; the jump's height is the charge's either way.
    /// </summary>
    public static Locomotion MovesReach(in Body body, in Locomotion loco)
    {
        if (body.Powered == 0) return loco;
        float f = ChargeShare(body, loco);
        var own = loco.PoweredReach();
        var l = loco;
        l.StandingReach += (own.StandingReach - loco.StandingReach) * f;
        l.VaultMax += (own.VaultMax - loco.VaultMax) * f;
        l.MantleMax += (own.MantleMax - loco.MantleMax) * f;
        return l;
    }

    /// <summary>
    /// The press and the hold this tick (after a press has set the jump's buffer): answers whether a tap or a charged
    /// jump goes now, keeping a press held by a body that can charge, and telling a charge's steps.
    /// </summary>
    static JumpGo JumpPress(ref Body body, in Locomotion loco, in MoveIntent intent, bool down, ref MotorOutcome o)
    {
        if (body.Charging != 0)
        {
            bool past = body.Charging == Charged || body.JumpHeldSeconds >= HoldSeconds - Eps;
            body.JumpBuffer = 0f;
            if (intent.Cancel)
            {
                body.Charging = 0;
                o.Cancelled = true;
                if (past) Tell(ref o, ChargeStep.Cancelled, ChargeHeight(body, loco), loco);
                return JumpGo.None;
            }
            bool left = body.State != BodyState.Ground;
            if (!down || left)
            {
                body.Charging = 0;
                if (left && body.Coyote <= Eps)
                {
                    if (past) Tell(ref o, ChargeStep.Lost, ChargeHeight(body, loco), loco);
                    return JumpGo.None;
                }
                if (!past) return JumpGo.Tap;
                Tell(ref o, ChargeStep.LetGo, ChargeHeight(body, loco), loco);
                return JumpGo.Charged;
            }
            if (past && body.Charging != Charged)
            {
                body.Charging = Charged;
                Tell(ref o, ChargeStep.Began, loco.TapHeight, loco);
            }
            return JumpGo.None;
        }
        if (body.JumpBuffer <= Eps) return JumpGo.None;
        if (body.State == BodyState.Ground && down && loco.CanCharge)
        {
            // Held: a tap waits for its release, a charge for the hold.
            body.Charging = PressHeld;
            body.JumpBuffer = 0f;
            return JumpGo.None;
        }
        return body.State == BodyState.Ground || body.Coyote > Eps ? JumpGo.Tap : JumpGo.None;
    }

    /// <summary>A charge's step told in the outcome.</summary>
    static void Tell(ref MotorOutcome o, ChargeStep step, float height, in Locomotion loco)
    {
        o.Charge = true;
        o.ChargeStep = step;
        o.ChargeHeight = height;
        o.ChargeMost = loco.PoweredHeight;
    }

    /// <summary>A jump taken off: the body remembers how high it goes and whether it is the powered leap; the outcome tells it.</summary>
    static void Launch(ref Body body, in Locomotion loco, JumpGo go, float impulse, ref MotorOutcome o)
    {
        float rise = loco.Gravity > 0f ? impulse * impulse / (2f * loco.Gravity) : 0f;
        body.JumpRise = rise;
        body.Powered = go == JumpGo.Charged ? 1 : 0;
        body.RefusalTold = 0;
        o.Launched = true;
        o.LaunchKind = go == JumpGo.Charged ? JumpKind.Charged : JumpKind.Tap;
        o.LaunchHeight = rise;
        o.ChargedSeconds = go == JumpGo.Charged ? MathF.Max(0f, body.JumpHeldSeconds - HoldSeconds) : 0f;
    }

    /// <summary>A move refused at a face marked not to be climbed, told once since the body last stood (a press on the ground: each time).</summary>
    static void Refuse(ref Body body, TraversalKind kind, in Obstacle ob, float height, ref MotorOutcome o, bool once)
    {
        if (once && body.RefusalTold != 0) return;
        if (once) body.RefusalTold = 1;
        o.Refused = kind;
        o.RefusedLip = ob.Lip;
        o.RefusedHeight = height;
    }
}
