// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// <b>The spring a body's starts and stops follow on the ground</b> (<i>"I'd like it to
/// look more human, but we'll need to see how it actually feels in game"</i>): the body's level velocity goes toward the wished one
/// as a <b>critically damped spring</b> of one half-life (<see cref="BodyMotor.Spring"/>, Daniel Holden's "spring-it-on" form), not
/// at the fixed rates of the earlier build's ramp (<see cref="Locomotion.AccelerationCapacity"/> up, <see cref="Locomotion.BrakingDeceleration"/>
/// down). A refinement: <b>the spring is the player's only</b> — the body the player controls
/// (a scenario's player-input actor counts as the player); every other person keeps the earlier build's ramp until the models repository's start and stop
/// clips bring their own speed curves. The spring's own state is the acceleration it carries from tick to tick, kept here, in a
/// component <b>only the player's body carries</b>, its store registered only in a world with a player on the spring
/// (<see cref="StartStopSprings.RegisterPlayer"/>): a world on the ramp (the switch <c>accel-ramp</c>, a tape without the note
/// <c>accel</c>, a world without <see cref="StartStopSprings"/>) or without a player (an arena's bout) has no store of it at all, so it
/// digests exactly as before. Plain four-byte fields, digested.
/// </summary>
public struct StartStopSpring
{
    /// <summary>The half-life in force this tick (s): the body's movement profile's in its mode (<see cref="StartStopSprings.HalfLifeOf"/>), written by <see cref="BodySystem"/> before the motor; 0, the ramp.</summary>
    public float HalfLife;

    /// <summary>The spring's acceleration across (m/s²): its state, zero whenever the body is not stepped on the ground by the spring (in the air, in a move, on a ladder, dodging).</summary>
    public Vector2 Acceleration;
}

/// <summary>
/// <b>The starts and stops on a spring</b>: registered by a rules module with its numbers (the
/// enhanced module's movement profiles each give <c>StartStopHalfLife</c>, SANDBOX: the human 0.2 s, the neonate 0.15, the elder 0.1)
/// unless its switch <c>accel-ramp</c> keeps the player on the earlier build's fixed ramp; the program then gives a world with a player the
/// component's store (<see cref="RegisterPlayer"/>), and the body it controls (<see cref="MovementServices.SpawnControlled"/>) carries a
/// <see cref="StartStopSpring"/> and starts and stops on the spring on the ground; the air keeps <see cref="Locomotion.AirControl"/> ×
/// the ramp, and every other body the ramp (by design). A body of a profile its half-life (held to the human's in the
/// masquerade: the slower, <see cref="MovementProfiles.HalfLife"/>); a body without a profile <see cref="Default"/>; a body under a
/// pace scale (Celerity's movement) the lesser of its own and the human's divided by the factor, as its acceleration is the
/// greater of its own and the human's times the factor. Configuration, not state: the half-life in force is the body's component's.
/// </summary>
public sealed class StartStopSprings
{
    /// <summary>The half-life of a body with no movement profile, and the human's a pace scale divides (s; the world's human profile's, else <see cref="Sandbox"/>).</summary>
    public float Default { get; init; } = Sandbox;

    /// <summary>
    /// <b>SANDBOX</b>: the platform's human half-life when no module gives one (s): 0.2, the models repository's proposal of 0.15–0.25
    /// (its motions' starts and stops will be made to this curve).
    /// </summary>
    public const float Sandbox = 0.2f;

    /// <summary>
    /// The tape head's note (written when the world's player is on the spring): <see cref="NoteValue"/>, or with the half-life given
    /// for the run (<see cref="Override"/>) <c>spring 0.12</c> (<see cref="Note"/>). A tape without it was made on the ramp (before
    /// With <c>accel-ramp</c>, a half-life of 0 given, or with no player).
    /// </summary>
    public const string TapeNote = "accel";

    /// <summary>The note's value (the profile's half-life).</summary>
    public const string NoteValue = "spring";

    /// <summary>
    /// (A development control, to judge the feel in play: <c>--start-stop-half-life &lt;s&gt;</c>): the player's half-life for the run in
    /// place of its profile's (s, above 0; a pace scale still shortens it), or null for the profile's. Set by the program before the
    /// first tick; written in the tape's note.
    /// </summary>
    public float? Override { get; set; }

    /// <summary>
    /// A landing on the spring as before — its speed across kept whole for the spring to take up, so a body
    /// landing on a slope slides on (0.19 m on a 39–42° ramp) — when set (the switch <c>landing-slide</c>, a tape with the note
    /// <see cref="TapeNote"/> and without <see cref="LandingNote"/>). By default a landing keeps only the speed along the way asked, up
    /// to the pace asked (<see cref="BodyMotor.Step(ref Body, ref Traversal, ref StartStopSpring, in Locomotion, in MoveIntent, Space.CollisionWorld, ReadOnlySpan{BodyCapsule}, ulong, long, bool, float, bool)"/>).
    /// </summary>
    public bool LandingSlide { get; init; }

    /// <summary>
    /// The tape head's note written beside <see cref="TapeNote"/> when the player's landings stop (<see cref="LandingSlide"/>
    /// not set): <see cref="LandingStops"/>. A tape on the spring without it was made with the earlier build's landings.
    /// </summary>
    public const string LandingNote = "landing", LandingStops = "stop";

    /// <summary>The longest half-life given for a run (s), as a profile's.</summary>
    public const float LongestOverride = 1f;

    /// <summary>The note for a half-life given (<c>spring 0.12</c>, the float written to round-trip) or for the profile's (<c>spring</c>).</summary>
    public static string Note(float? given) =>
        given is { } h ? NoteValue + " " + h.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : NoteValue;

    /// <summary>The half-life a note gives (<c>spring 0.12</c>: 0.12), or null for <c>spring</c> (the profile's) or anything else.</summary>
    public static float? GivenIn(string? note) =>
        note is { } n && n.StartsWith(NoteValue + " ", StringComparison.Ordinal)
        && float.TryParse(n[(NoteValue.Length + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float h)
        && h > 0f && h <= LongestOverride ? h : null;

    /// <summary>
    /// Registers the spring's numbers: this service, configuration only (not digested). A rules module registers it unless
    /// its switch keeps the ramp; nothing moves on the spring until the program gives the world a player on it (<see cref="RegisterPlayer"/>).
    /// </summary>
    public static WorldBuilder Register(WorldBuilder b, StartStopSprings springs)
    {
        if (!b.Peek.TryService<StartStopSprings>(out _)) b.Service(springs);
        return b;
    }

    /// <summary>
    /// The world's player starts and stops on the spring (the player only): the component
    /// <see cref="StartStopSpring"/> (digested) registered when the world has the spring's numbers (<see cref="Register"/>), so the body
    /// spawned controlled (<see cref="MovementServices.SpawnControlled"/>) carries one. The program calls it for a world with a player
    /// (play, its checks and recordings, a scenario with a player-input actor), never for an arena's bout; a world without it is the earlier build's,
    /// digest for digest. Answers whether the world now has it.
    /// </summary>
    public static bool RegisterPlayer(WorldBuilder b)
    {
        if (!b.Peek.TryService<StartStopSprings>(out _)) return false;
        if (!b.Peek.TryStore<StartStopSpring>(out _)) b.Component<StartStopSpring>();
        return true;
    }

    /// <summary>Whether the world's player starts and stops on the spring (it registers <see cref="StartStopSpring"/>, <see cref="RegisterPlayer"/>).</summary>
    public static bool In(World world) => world.TryStore<StartStopSpring>(out _);

    /// <summary>
    /// The half-life in force for <paramref name="e"/> (s): the run's (<see cref="Override"/>) when given, else its profile's in its mode
    /// when it has a <see cref="MasqueradeState"/> and the world <paramref name="profiles"/>, else <see cref="Default"/>; under a pace factor above 1 (<see cref="BodyMotor.PaceFactor"/>)
    /// the lesser of that and the human's over the factor. No allocation.
    /// </summary>
    public float HalfLifeOf(Store<MasqueradeState>? masks, MovementProfiles? profiles, EntityId e, float factor)
    {
        float h = Default;
        if (Override is { } given) h = given;   // the run's half-life, whatever the profile and its mode
        else if (masks is not null && profiles is not null)
        {
            ref var state = ref masks.Find(e);
            if (!Unsafe.IsNullRef(ref state) && state.Profile >= 0 && state.Profile < profiles.All.Count) h = profiles.HalfLife(state.Profile, state.Mode);
        }
        if (factor > 1f && h > 0f)
        {
            float human = profiles is not null && profiles.HumanIndex >= 0 ? profiles.HalfLife(profiles.HumanIndex, MasqueradeMode.Unbound) : Default;
            if (human > 0f) h = MathF.Min(h, human / factor);
        }
        return h;
    }
}

public static partial class BodyMotor
{
    /// <summary>ln 2, for a half-life's damping.</summary>
    const float Ln2 = 0.693147181f;

    /// <summary>
    /// One tick of the spring: the velocity <paramref name="v"/> toward <paramref name="goal"/>, a critically
    /// damped spring of half-life <paramref name="halfLife"/> over <paramref name="dt"/>, its acceleration <paramref name="a"/> carried
    /// (Daniel Holden's exact form: y = 2·ln2 / h; j0 = v − goal; j1 = a + j0·y; e = exp(−y·dt); v' = e·(j0 + j1·dt) + goal;
    /// a' = e·(a − j1·y·dt)). The models repository makes its starts and stops to this curve: keep it exactly so. <see cref="DetMath.Exp"/>,
    /// no C-runtime maths. Pure.
    /// </summary>
    public static Vector2 Spring(Vector2 v, ref Vector2 a, Vector2 goal, float halfLife, float dt)
    {
        float y = 2f * Ln2 / halfLife;
        var j0 = v - goal;
        var j1 = a + j0 * y;
        float e = DetMath.Exp(-y * dt);
        var next = e * (j0 + j1 * dt) + goal;
        a = e * (a - j1 * y * dt);
        return next;
    }

    /// <summary>The spring's damping for a half-life (1/s): 2·ln2 / h.</summary>
    public static float SpringDamping(float halfLife) => 2f * Ln2 / halfLife;

    /// <summary>
    /// How far a body on the spring slides before it stops (m) when nothing is wished, from speed <paramref name="v"/> with the
    /// spring's acceleration <paramref name="a"/> along the same way: the spring's integral, v·2/y + a/y² (from a standstill of the
    /// acceleration, h/ln2 a metre a second: 0.87 m from the human's run at 0.2 s). 0 for a half-life of 0 or less.
    /// </summary>
    public static float SpringStopping(float v, float a, float halfLife)
    {
        if (!(halfLife > 0f)) return 0f;
        float y = SpringDamping(halfLife);
        return MathF.Max(0f, 2f * v / y + a / (y * y));
    }

    /// <summary>
    /// How far a body on the spring (<paramref name="spring"/>, its half-life above 0) slides before it stops (m) when nothing is
    /// wished, from speed <paramref name="v"/> along <paramref name="way"/> (a unit vector), plus a tick's latency: the brains'
    /// estimate (the duelling brain, 1.1's approach, the scenario director), as v² / 2b + v·dt is for a body on the ramp.
    /// </summary>
    public static float SpringStoppingAlong(in StartStopSpring spring, float v, Vector2 way) =>
        v <= 0f ? 0f : SpringStopping(v, Vector2.Dot(spring.Acceleration, way), spring.HalfLife) + v * World.TickSeconds;
}
