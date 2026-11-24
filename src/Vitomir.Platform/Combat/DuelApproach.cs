// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Combat;

/// <summary>
/// How the duelling brain comes on from beyond its striking range (after playing the earlier build's szlachta:
/// <i>"In the base game they move and pause more frequently, move much more slowly."</i>). Within its striking range every approach
/// fights as the duelling brain does.
/// </summary>
public enum DuelApproach : byte
{
    /// <summary>The duelling brain's own: straight at its foe, at a run beyond <see cref="DuelBrainSettings.WalkWithin"/>, never pausing.</summary>
    Duel = 0,
    /// <summary>
    /// 1.1's enemy's (the 1.1 engine's <c>Vitomir.Engine/Ai/Minds.cs</c>, <c>Manoeuvre</c>): every think
    /// a step toward its foe turned by a random angle and of a random length, walked at its run, the next think after its AI file's
    /// <c>moveRate</c>; a step ended before then and it stands (<see cref="DuelOneOnes"/>).
    /// </summary>
    OneOne = 1,
}

/// <summary>
/// 1.1's approach on a duelling brain's body (digested, plain data; the store is registered only in a world where a
/// brain uses it, <see cref="DuelOneOnes.Register"/>): the seconds to its next think, the spot its step walks to (feet, across the
/// ground) and whether it is walking there, and its counts — the steps chosen, those that ended before the next think (it then stood),
/// and the thinks it touched its foe (no step).
/// </summary>
public struct DuelOneOne
{
    public float ThinkIn;
    public Vector2 Spot;
    int walking;
    public int Steps, StepsEnded, Touching;

    /// <summary>Whether it is walking its step (false: standing until its next think).</summary>
    public bool Walking { readonly get => walking != 0; set => walking = value ? 1 : 0; }
}

/// <summary>
/// 1.1's approach: the manoeuvre of 1.1's enemy mind, driven by the duelling brain while
/// its foe is beyond its striking range (<see cref="DuelBrainSettings.StrikeShare"/> of its reach). Each <b>think</b> (at once the
/// first time, then every <see cref="DuelBrainSettings.OneOneMoveSeconds"/>, its AI file's <c>moveRate</c>):
/// <list type="bullet">
/// <item>its own blow still playing: it thinks again as soon as the blow is over;</item>
/// <item><b>touching</b> its foe (the bodies' gap across the ground below zero): no draw, it stands and thinks again in
/// <see cref="TouchThinkSeconds"/> (before the angle's roll);</item>
/// <item>else a <b>step</b>: the line to its foe turned by a random angle within ±<see cref="ArcDegrees"/>/2, a random
/// length up to <see cref="DuelBrainSettings.OneOneStepMostMetres"/> (its <c>chaseDist</c>), a roll under
/// <see cref="FloorMetres"/> taken as the floor and a longer one cut to the bodies' gap; walked at its run when
/// <see cref="DuelBrainSettings.OneOneRuns"/> (its <c>attackFlags</c> bit 0), else at its walk, the body turning to the way it goes.
/// A step reached before the next think ends there and the body <b>stands</b> until that think: 1.1's move-and-pause.</item>
/// </list>
/// Draws from its own stream <see cref="Stream"/> (two numbers a step, in the brains' store order), so no other stream moves. The
/// numbers of the creature (<c>moveRate</c>, <c>chaseDist</c>, <c>attackFlags</c>) are read from the player's install at run time;
/// the arc, the floor and the touching think are 1.1's engine's (the old engine's <c>Minds.ManoeuvreArc</c>, <c>ManoeuvreFloor</c>,
/// <c>EnemyThink</c>). Not here: 1.1's walk map (the spot's ground, a refused occupied spot), its taunt.
/// </summary>
public static class DuelOneOnes
{
    /// <summary>The spread of the step's angle about the line to the foe, degrees (1.1's engine: ±30°).</summary>
    public const float ArcDegrees = 60f;
    /// <summary>The shortest step, metres (1.1's engine: 50 units, at 72 to the metre).</summary>
    public const float FloorMetres = 50f / 72f;
    /// <summary>The think after touching its foe, seconds (1.1's engine's enemy think, 1000 ms).</summary>
    public const float TouchThinkSeconds = 1f;
    /// <summary>The brain's own stream for the steps.</summary>
    public const string Stream = "combat.duel-brain.oneone";
    /// <summary>A step is reached within this of its spot (m) beyond the body's own stopping distance.</summary>
    public const float ArriveMetres = 0.02f;

    /// <summary>Registers <see cref="DuelOneOne"/> (digested). Only for a world where a brain uses 1.1's approach: a world without it is the world earlier, digest for digest.</summary>
    public static WorldBuilder Register(WorldBuilder b)
    {
        b.Component<DuelOneOne>();
        return b;
    }

    /// <summary>Whether 1.1's approach is registered in <paramref name="world"/>.</summary>
    public static bool Registered(World world) => world.TryStore<DuelOneOne>(out _);

    /// <summary>Whether <paramref name="s"/> asks for 1.1's approach and carries the creature's numbers.</summary>
    public static bool Uses(DuelBrainSettings s) => s.Approach == DuelApproach.OneOne && s.OneOneMoveSeconds > 0f;

    /// <summary>
    /// One tick of the approach for brain <paramref name="e"/> (called by <see cref="DuelBrainSystem"/> where it would close): answers
    /// false when its foe is within its striking range (the duelling brain's own closing then), true when it set the move — a step walked
    /// or standing. <paramref name="d"/> is the distance to its foe and <paramref name="toward"/> the way to it, across the ground.
    /// </summary>
    public static bool Step(Store<DuelOneOne> store, EntityId e, DuelBrainSettings s, in DuelBrain brain, ref MoveIntent move, in Body body,
        Store<Locomotion> locos, EntityId foe, float d, Vector2 toward, bool busy, Rng rng, Store<StartStopSpring>? springs = null)
    {
        ref var st = ref store.Find(e);
        if (Unsafe.IsNullRef(ref st))
        {
            store.Set(e, default);
            st = ref store.Find(e);
        }
        if (d <= brain.Reach * s.StrikeShare)
        {
            // Within its striking range: the duel's own (it thinks again at once if its foe steps out of it).
            st.Walking = false;
            st.ThinkIn = 0f;
            return false;
        }
        var feet = new Vector2(body.Feet.X, body.Feet.Y);
        if (st.ThinkIn > 0f) st.ThinkIn -= World.TickSeconds;
        if (st.ThinkIn <= 0f && !busy)
        {
            float gap = d - RadiusOf(locos, e) - RadiusOf(locos, foe);
            if (gap < 0f)
            {
                st.Walking = false;
                st.ThinkIn = TouchThinkSeconds;
                st.Touching++;
            }
            else
            {
                float turn = (rng.NextFloat() * ArcDegrees - ArcDegrees / 2f) * DetMath.DegToRad;
                float cos = DetMath.Cos(turn), sin = DetMath.Sin(turn);
                var heading = new Vector2(toward.X * cos - toward.Y * sin, toward.X * sin + toward.Y * cos);
                float step = rng.NextFloat() * s.OneOneStepMostMetres;
                step = step < FloorMetres ? FloorMetres : MathF.Min(step, gap);
                st.Spot = feet + heading * step;
                st.Walking = true;
                st.ThinkIn = s.OneOneMoveSeconds;
                st.Steps++;
            }
        }
        move.Faces = false;   // the body turns to the way it goes (standing, it keeps its facing)
        if (st.Walking)
        {
            var way = st.Spot - feet;
            float left = way.Length();
            var dir = left > 1e-4f ? way / left : Vector2.Zero;
            float v = body.Velocity.X * dir.X + body.Velocity.Y * dir.Y;
            ref var loco = ref locos.Find(e);
            float brake = Unsafe.IsNullRef(ref loco) || loco.BrakingDeceleration <= 0f ? 8f : loco.BrakingDeceleration;
            float stopping = v > 0f ? v * v / (2f * brake) + v * World.TickSeconds : 0f;
            ref var spring = ref springs is null ? ref Unsafe.NullRef<StartStopSpring>() : ref springs.Find(e);   // on the spring, by the spring
            if (!Unsafe.IsNullRef(ref spring) && spring.HalfLife > 0f) stopping = BodyMotor.SpringStoppingAlong(spring, v, dir);
            if (left <= stopping + ArriveMetres)
            {
                st.Walking = false;
                if (st.ThinkIn > 0f) st.StepsEnded++;
            }
            else
            {
                move.Direction = dir;
                move.Gait = s.OneOneRuns ? Gait.Run : Gait.Walk;
                return true;
            }
        }
        move.Direction = Vector2.Zero;
        return true;
    }

    static float RadiusOf(Store<Locomotion> locos, EntityId e)
    {
        ref var l = ref locos.Find(e);
        return Unsafe.IsNullRef(ref l) ? 0.3f : l.Radius;
    }
}
