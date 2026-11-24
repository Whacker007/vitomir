// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours, on the models repository: its crouch and the use it suggests.
namespace Vitomir.Platform.Animation.People;

/// <summary>
/// A person's crouch from the models repository, when its motions file has it (looked up by name; a set
/// without it keeps the stand-ins: the stand lowered still, the walk lowered moving): <c>crouch</c> (the crouch held, 8 s),
/// <c>crouch_down</c> and <c>crouch_up</c> (from and to the stand), <c>crouch_kneel</c> (6 s) with <c>crouch_settle</c> (from the
/// crouch into it), <c>crouch_rise</c> (back to the crouch), <c>crouch_kneel_down</c> and <c>crouch_kneel_up</c> (straight from and
/// to the stand), <c>crouch_walk</c> and its seven ways (a gait as the walk's, <see cref="Gait"/>), and the turns
/// <c>crouch_turn_left_45</c> … (root-carried, played as the standing turns). Places in the kind's clips; −1 for one the set lacks.
/// </summary>
public sealed class PersonCrouch
{
    public int Hold { get; init; } = -1;
    public int Down { get; init; } = -1;
    public int Up { get; init; } = -1;
    public int Kneel { get; init; } = -1;
    public int Settle { get; init; } = -1;
    public int Rise { get; init; } = -1;
    public int KneelDown { get; init; } = -1;
    public int KneelUp { get; init; } = -1;

    /// <summary>
    /// The models repository's crouch held on the balls of the feet, asked for and not yet named. When it is, its name
    /// goes here (one line): it is then the crouch held (<see cref="Hold"/>, <see cref="HeelsUp"/>) and the platform's heel lift
    /// (<see cref="PersonFeetSettings.HeelLift"/>) is not laid on. The models repository answered by making <c>crouch</c>
    /// itself that squat (heels 40° up), not a clip of another name, so this stays empty: the heel lift, which adds only what the clip
    /// lacks, adds nothing on a newer set and still lifts an older repository's flat crouch.
    /// </summary>
    public const string HeelsUpClip = "";

    /// <summary>The heels-up crouch's place in the kind's clips when the set has it (<see cref="HeelsUpClip"/>), else −1.</summary>
    public int HeelsUp { get; init; } = -1;

    /// <summary>The crouched walk's ways (blended as the walk's), or null with only <c>crouch_walk</c> ahead.</summary>
    public Gait? Walk { get; init; }

    /// <summary>The crouched turns on the spot, or null.</summary>
    public TurnSet? Turns { get; init; }

    /// <summary>How long a person who stops crouched holds the crouch before settling into the kneel (s; the models repository's "about half a second").</summary>
    public const float SettleAfter = 0.5f;

    /// <summary>Whether the clip is the kneel or on the way into it.</summary>
    public bool Kneeling(int clip) => clip >= 0 && (clip == Kneel || clip == Settle || clip == KneelDown);

    /// <summary>Whether the clip is one of the crouch's (the held crouch, its ways in and out, the kneel's, the crouched walk's, the crouched turns).</summary>
    public bool Holds(int clip) => clip >= 0 && (clip == Hold || clip == Down || clip == Up || clip == Rise || clip == KneelUp || Kneeling(clip)
                                                  || Walk?.Holds(clip) == true || Turns?.Holds(clip) == true);
}

/// <summary>
/// Which of a person's crouch clips plays (the models repository's suggested use), chosen by the
/// <see cref="AnimationSystem"/> from what plays now and what the body asks for — so the crouch's progress is the animation's
/// own clip and time, and nothing new is digested:
/// <list type="bullet">
/// <item>crouched while still: from the stand, <c>crouch_kneel_down</c> and then the kneel; from moving, the crouch held, and
/// after <see cref="PersonCrouch.SettleAfter"/> <c>crouch_settle</c> into the kneel;</item>
/// <item>crouched and moving: from the kneel (or on the way into it) <c>crouch_rise</c> first, then the crouched walk's blends
/// (from a walk or a run the gait fades into it);</item>
/// <item>standing up while still: <c>crouch_kneel_up</c> from the kneel, <c>crouch_up</c> from the crouch, then the stand;
/// moving, the walk or run fades in as from any motion.</item>
/// </list>
/// A clip played once plays at its own rate to its end; the joins between them match within 0.6° (the models repository), so the
/// fifth of a second's fade between motions is enough.
/// <para>
/// <i>"park the kneeling crouch for now"</i>: by default there is <b>no kneel</b>
/// (<see cref="AnimationSettings.PersonKneel"/> off): crouched and still, the crouch held, from the stand or a stride by
/// <c>crouch_down</c> and from the crouched walk straight (its own low stride, nearly stopped, fading into it); turns end in the crouch
/// held (<see cref="PeopleTurn"/>). The kneel's clips are not played. <c>--crouch-kneel</c> gives the earlier build's above.
/// </para>
/// </summary>
public static class PeopleCrouch
{
    /// <summary>
    /// The role and clip a person's crouch plays for <paramref name="want"/> (what the body asks for: the crouch, the crouched walk,
    /// the stand), and whether it plays by the clock (a clip played once) rather than matched to the ground; false when the role
    /// plays as any other (the crouched walk's blends, the stand, a walk).
    /// </summary>
    public static bool Choose(PersonCrouch c, in CharacterAnimation a, MotionRole want, CharacterAssets assets, CharacterKind kind,
                              out MotionRole role, out int clip, bool kneel = true)
    {
        int now = a.Clip;
        float time = a.Time;
        bool Finished(int which) => now == which && (assets.Clip(which).Loops || time >= assets.Clip(which).Duration - 1e-4f);
        (role, clip) = (want, -1);
        if (!kneel && want == MotionRole.Crouch)
        {
            // No kneel — the crouch held, come to by crouch_down from the stand or a stride on its feet (its bend
            // played rather than a stride faded into the low pose), and from the kneel (a tape's) by crouch_rise.
            if (now >= 0 && (now == c.Down || now == c.Rise)) clip = Finished(now) && c.Hold >= 0 ? c.Hold : now;
            else if (now >= 0 && c.Kneeling(now)) clip = c.Rise >= 0 ? c.Rise : c.Hold;
            else if (now >= 0 && now == c.Hold) clip = c.Hold;
            else if (c.Down >= 0 && (a.Role is MotionRole.Idle or MotionRole.Walk or MotionRole.Run) && !c.Holds(now)) clip = c.Down;
            else clip = c.Hold;
            return clip >= 0;
        }
        switch (want)
        {
            case MotionRole.Crouch:
                if (now >= 0 && (now == c.KneelDown || now == c.Settle)) clip = Finished(now) && c.Kneel >= 0 ? c.Kneel : now;
                else if (now >= 0 && now == c.Kneel) clip = c.Kneel;
                else if (now >= 0 && now == c.Hold) clip = a.Time >= PersonCrouch.SettleAfter && c.Settle >= 0 ? c.Settle : c.Hold;
                else if (now >= 0 && (now == c.Rise || now == c.Down)) clip = Finished(now) && c.Hold >= 0 ? c.Hold : now;
                else if (a.Role == MotionRole.Idle && now == kind.Motion(MotionRole.Idle).Clip && c.KneelDown >= 0) clip = c.KneelDown;
                else clip = c.Hold;
                return clip >= 0;
            case MotionRole.CrouchWalk:
                if (c.Rise < 0) return false;
                if (c.Kneeling(now) || (now == c.Rise && !Finished(now))) { clip = c.Rise; return true; }
                return false;
            case MotionRole.Idle:
                if (now < 0 || !c.Holds(now)) return false;
                if (now == c.Up || now == c.KneelUp) { clip = now; return !Finished(now); }
                clip = c.Kneeling(now) ? c.KneelUp : c.Up;
                return clip >= 0;
            default:
                return false;
        }
    }
}
