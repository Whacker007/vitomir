// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// <b>The dodge's move</b> (ours; the rules package's Dodge: <i>"Visible evasive action/real motion; no imaginary
/// passive movement"</i>): a body dodging (<see cref="Dodging"/>, begun by <see cref="DodgeSystem"/>) is moved by the motor
/// through the level as any movement is — swept and slid along what it meets, stepped up a kerb, kept on the ground — so a
/// wall or a person stops it, and it is kept from going down any drop higher than a step (the walk's edge guard,
/// <see cref="EdgeGuard.SafeFall"/>, held to <see cref="Locomotion.StepHeight"/>: a dodge never throws a body off a wall or a
/// roof's edge; it stops there, as a guarded walk does). <b>The start</b>: the body's speed brakes at its own rate (the weight shifted onto the
/// pushing leg). <b>The burst</b>: it moves along the dodge's way at the dodge's speed (distance over the burst's time) from
/// the first tick. <b>The recovery</b>: the feet planted, it stands. Its facing is the intent's when the intent faces
/// (a brain facing its foe, the first person's view), else kept: a dodge turns no body. Off the ground (it went over a low
/// drop), the plain motor carries it with no wish while the dodge's time runs on. No jump, crouch, use or move starts during
/// a dodge. There is no invulnerability: the body is where the motor put it, and a blade that reaches it meets it.
/// </summary>
public static partial class BodyMotor
{
    /// <summary>
    /// One tick of a dodging body (<paramref name="dodge"/> active): moved as above, the dodge's clock advanced and its phase
    /// moved on; <paramref name="ended"/> on the tick its recovery ends (its phase then <see cref="DodgePhase.None"/>).
    /// </summary>
    public static MotorOutcome DodgeStep(ref Body body, ref Traversal move, ref Dodging dodge, in Locomotion loco, in MoveIntent intent,
                                         CollisionWorld level, ReadOnlySpan<BodyCapsule> others, ulong self, long tick, out bool ended)
    {
        const float dt = World.TickSeconds;
        ended = false;
        var o = default(MotorOutcome);
        // The plain intent the dodge moves by: no wish, nothing pressed, the walk's edge guard.
        var still = intent;
        still.Direction = Vector2.Zero;
        still.Jump = still.JumpHeld = still.Crouch = still.Use = still.Cancel = false;
        still.Guard = EdgeGuard.SafeFall;
        still.GuardCorner = default;
        if (body.State != BodyState.Ground)
        {
            o = Step(ref body, ref move, loco, still, level, others, self, tick, moves: false);
        }
        else
        {
            var m = new Mover(level, others, self, loco, body.CapsuleHeight(loco));
            var feet = body.Feet;
            Depenetrate(m, ref feet);
            if (intent.Faces) body.Yaw = DetMath.WrapAngle(intent.Facing);
            var vh = new Vector2(body.Velocity.X, body.Velocity.Y);
            vh = dodge.Phase switch
            {
                DodgePhase.Burst => dodge.Way * dodge.Speed,
                DodgePhase.Start => MoveToward(vh, Vector2.Zero, loco.BrakingDeceleration * dt),
                _ => Vector2.Zero,
            };
            var start = feet;
            body.CrouchWas = 0;
            var guarded = loco;
            guarded.SafeFall = 0f;      // the guard's limit then the step (GuardLimit: max(SafeFall, StepHeight))
            GuardedMove(m, ref body, ref move, ref feet, vh, vh.LengthSquared() > 1e-8f ? Vector2.Normalize(vh) : Vector2.Zero, guarded, still, false, ref o);
            if (dodge.Phase == DodgePhase.Burst)
                dodge.Moved += new Vector2(feet.X - start.X, feet.Y - start.Y).Length();
            Depenetrate(m, ref feet);
            body.Feet = feet;
            if (body.State == BodyState.Ground) body.GroundSeconds += dt;
            if (move.Kind != TraversalKind.None) move = default;
        }

        // The clock: the start, the burst, the recovery, then none.
        dodge.Seconds += dt;
        const float eps = 1e-6f;
        if (dodge.Phase == DodgePhase.Start && dodge.Seconds >= dodge.Ready - eps) { dodge.Phase = DodgePhase.Burst; dodge.Seconds = 0f; }
        else if (dodge.Phase == DodgePhase.Burst && dodge.Seconds >= dodge.Move - eps)
        {
            dodge.Phase = DodgePhase.Recover;
            dodge.Seconds = 0f;
            if (body.State == BodyState.Ground) body.Velocity = new Vector3(0f, 0f, body.Velocity.Z);
        }
        else if (dodge.Phase == DodgePhase.Recover && dodge.Seconds >= dodge.Recover - eps)
        {
            dodge.Phase = DodgePhase.None;
            dodge.Seconds = 0f;
            ended = true;
        }
        return o;
    }
}
