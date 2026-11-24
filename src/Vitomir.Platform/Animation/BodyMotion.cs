// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Animation;

/// <summary>
/// What a body did this tick, handed to its animation (ours): its speed over the ground and along its facing, its
/// vertical speed and whether it stands on ground, from <see cref="Body"/> into <see cref="MotionInput"/> for every entity that has
/// both; and the move it is making from its <see cref="Traversal"/> when the world has those, and whether it
/// is crouched; whether it dodges and how far through its motion. Runs just before the animation system (at the end of Move, <see cref="AnimationRegistration.PosePhase"/>), so
/// the motion chosen is the one for where the body is now.
/// </summary>
public sealed class BodyMotionSystem : ISystem
{
    /// <summary>Its place before the animation system (relative to <see cref="AnimationRegistration.AnimationOrder"/>).</summary>
    public const int Order = -50;

    int traversalKnown;   // 0 not looked for, 1 registered, 2 not registered
    int dodgesKnown;      // the dodge

    public void Run(World world)
    {
        var bodies = world.Store<Body>();
        var inputs = world.Store<MotionInput>();
        var moves = PoseRigSystem.Optional<Traversal>(world, ref traversalKnown);
        var dodges = PoseRigSystem.Optional<Dodging>(world, ref dodgesKnown);
        float low = world.TryService<DodgeSettings>(out var ds) ? ds.MotionLow : 18f / 39f;
        var data = inputs.Data;
        var entities = inputs.Entities;
        for (int i = 0; i < data.Length; i++)
        {
            ref var body = ref bodies.Find(entities[i]);
            if (System.Runtime.CompilerServices.Unsafe.IsNullRef(ref body)) continue;
            var v = body.Velocity;
            var (sin, cos) = DetMath.SinCos(body.Yaw);
            data[i].GroundSpeed = MathF.Sqrt(v.X * v.X + v.Y * v.Y);
            data[i].ForwardSpeed = v.X * cos + v.Y * sin;
            data[i].SideSpeed = v.Y * cos - v.X * sin;      // along the body's left, (−sin, cos)
            data[i].VerticalSpeed = v.Z;
            data[i].Grounded = body.State == BodyState.Ground;
            // Crouched: the body's own flag (the capsule lowered; it stays down under a low beam until it can stand).
            data[i].Crouched = body.Crouched != 0 && body.State == BodyState.Ground;
            data[i].Traversal = TraversalKind.None;
            data[i].TraversalProgress = 0f;
            data[i].Dodging = false;
            data[i].DodgeProgress = 0f;
            data[i].DodgeAside = 0f;
            if (dodges is not null)
            {
                ref var dodge = ref dodges.Find(entities[i]);
                if (!System.Runtime.CompilerServices.Unsafe.IsNullRef(ref dodge) && dodge.Active)
                {
                    data[i].Dodging = true;
                    data[i].DodgeProgress = dodge.Progress(low);
                    data[i].DodgeAside = dodge.Way.X * sin - dodge.Way.Y * cos;    // along the body's right, (sin, −cos)
                }
            }
            if (moves is not null)
            {
                ref var move = ref moves.Find(entities[i]);
                if (!System.Runtime.CompilerServices.Unsafe.IsNullRef(ref move))
                {
                    data[i].Traversal = move.Kind;
                    data[i].TraversalProgress = move.Progress;
                }
            }
        }
    }

    /// <summary>Registers the system (after the movement and the animation are registered): in the animation's phase, just before it (the end of Move).</summary>
    public static WorldBuilder Register(WorldBuilder b) => b.System(AnimationRegistration.PosePhase, AnimationRegistration.AnimationOrder + Order, "platform.body-motion", new BodyMotionSystem());
}
