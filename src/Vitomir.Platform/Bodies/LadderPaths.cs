// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the models repository's clips (ladders, and a ladder into a braced hang and back) carry their capsule from the
// ladder position (CONVENTIONS, Notes, traverse.ladder, top, landing, hang): BodyMotor.LadderClips.cs.
using System.Numerics;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// <b>tier 1's moves for the motor</b> (the people's <c>PersonMovesSettings</c>, handed here as plain numbers), null for a body without them:
/// the grabs that end braced, the low vaults at a run, the ladders on clips and their sprint's rate.
/// </summary>
public sealed class MovesRules
{
    /// <summary><c>grab_jump_up_braced</c> and <c>grab_fall_braced</c> where there is a wall under the edge.</summary>
    public bool BracedGrabs { get; init; } = true;

    /// <summary><c>hurdle_low</c> and <c>vault_speed</c> by themselves at a run or a sprint.</summary>
    public bool LowVaults { get; init; } = true;

    /// <summary>Ladders on the clips.</summary>
    public bool Ladders { get; init; } = true;

    /// <summary>Sprint held on a ladder: the climb's clips this much faster.</summary>
    public float LadderSprintRate { get; init; } = 2f;

    /// <summary>The climb's cycles this much faster than their own (sprint's on top of it, at most <see cref="BodyMotor.MaxLadderRate"/>).</summary>
    public float LadderClimbRate { get; init; } = 1f;

    /// <summary>The getting-on, the getting-off and the transitions (the foot, the top and its rails pair, the side landings, to and from the hang) this much faster.</summary>
    public float LadderMoveRate { get; init; } = 1f;

    /// <summary>How far a level's ladder may lean off a set's own lean and still be climbed on it (radians).</summary>
    public float LeanTolerance { get; init; } = 2f * Core.DetMath.DegToRad;

    /// <summary>The hang's corners and leaps (BodyMotor.Hang.cs), null for none: the earlier build's.</summary>
    public HangRules? Hang { get; init; }

    /// <summary>Climbing walls (BodyMotor.Wall.cs), riding on the hang's corners and leaps; null for none: the earlier build's.</summary>
    public WallRules? Wall { get; init; }

    /// <summary>These numbers with <see cref="Hang"/> as given.</summary>
    public MovesRules WithHang(HangRules? hang) => new()
    {
        BracedGrabs = BracedGrabs, LowVaults = LowVaults, Ladders = Ladders, LadderSprintRate = LadderSprintRate, LeanTolerance = LeanTolerance,
        LadderClimbRate = LadderClimbRate, LadderMoveRate = LadderMoveRate, Hang = hang, Wall = Wall,
    };

    /// <summary>These numbers with <see cref="Wall"/> as given.</summary>
    public MovesRules WithWall(WallRules? wall) => new()
    {
        BracedGrabs = BracedGrabs, LowVaults = LowVaults, Ladders = Ladders, LadderSprintRate = LadderSprintRate, LeanTolerance = LeanTolerance,
        LadderClimbRate = LadderClimbRate, LadderMoveRate = LadderMoveRate, Hang = Hang, Wall = wall,
    };
}

/// <summary>What a ladder clip plays (the models repository's ).</summary>
public enum LadderRole
{
    None = 0,
    /// <summary><c>ladder_idle</c>, <c>ladder_v_idle</c>: holding on at the ladder position (a loop).</summary>
    Idle = 1,
    /// <summary><c>ladder_up</c>: a cycle of two rungs up, from the idle's first frame to it.</summary>
    Up = 2,
    /// <summary><c>ladder_down</c>: a cycle of two rungs down.</summary>
    Down = 3,
    /// <summary><c>ladder_on_bottom</c>: from standing at the foot onto rungs 1 and 2.</summary>
    OnBottom = 4,
    /// <summary><c>ladder_off_bottom</c>: from rungs 1 and 2 down onto the ground at the foot.</summary>
    OffBottom = 5,
    /// <summary><c>ladder_on_top</c> (and <c>_rails</c>): from the floor at the top down onto the ladder.</summary>
    OnTop = 6,
    /// <summary><c>ladder_off_top</c> (and <c>_rails</c>): from the ladder over onto the floor at the top, standing.</summary>
    OffTop = 7,
    /// <summary><c>ladder_on_side_l</c>, <c>_r</c>: from a landing beside the ladder onto it.</summary>
    OnSide = 8,
    /// <summary><c>ladder_off_side_l</c>, <c>_r</c>: from the ladder onto a landing beside it, standing.</summary>
    OffSide = 9,
    /// <summary><c>ladder_v_to_hang_braced_l</c>, <c>_r</c>: from the upright ladder into the braced hang on a ledge beside it.</summary>
    ToHang = 10,
    /// <summary><c>ladder_v_from_hang_braced_l</c>, <c>_r</c>: from that hang back onto the ladder.</summary>
    FromHang = 11,
}

/// <summary>A hand on a hold in a ladder clip (<c>traverse.hands</c>): which, from when to when (−1 held from before, 99 past its end), where (forward, up and left of the ladder position, m).</summary>
public readonly record struct LadderHand(bool Left, float ContactS, float ReleaseS, float Forward, float Up, float LeftM);

/// <summary>
/// <b>one ladder clip</b> for the motor (the models repository's <c>traverse</c> note with its <c>ladder</c>, read by the people's motion and handed here as plain
/// numbers at the body's scale): what it plays, its side (+1 left, −1 right, 0 none), its path (a row a frame: the capsule's forward toward the ladder, up and to
/// the left from <b>the ladder position</b> — the capsule where the body stands in the idle, the left foot on rung 1 and the right on rung 2 — of the clip's
/// start or end), its top's, landing's or hang's numbers.
/// </summary>
public sealed class LadderClip
{
    public required int Clip { get; init; }
    public required string Name { get; init; }
    public required LadderRole Role { get; init; }
    public required float Duration { get; init; }
    public required bool Loops { get; init; }
    public int Side { get; init; }
    /// <summary>The top's: whether the rails go on above the floor (the <c>_rails</c> pair).</summary>
    public bool Rails { get; init; }
    public required float[] T { get; init; }
    public required float[] F { get; init; }
    public required float[] U { get; init; }
    public required float[] S { get; init; }
    /// <summary>The top's (<c>traverse.top</c>): the rung level with the floor, the floor over the capsule's bottom, its edge and where the capsule stands (forward); NaN none.</summary>
    public int TopRung { get; init; }
    public float FloorUp { get; init; } = float.NaN;
    public float LipFwd { get; init; } = float.NaN;
    public float StandFwd { get; init; } = float.NaN;
    /// <summary>A side landing's (<c>traverse.landing</c>): the rung level with it, its floor over the capsule's bottom, where the capsule stands on it (left, forward).</summary>
    public int LandRung { get; init; }
    public float LandUp { get; init; } = float.NaN;
    public float LandLeft { get; init; } = float.NaN;
    public float LandFwd { get; init; } = float.NaN;
    /// <summary>A ladder to a hang's (<c>traverse.hang</c>): the rung, the hang's middle on the edge left, the edge ahead and its top up from the ladder position; the hands along the edge.</summary>
    public int HangRung { get; init; }
    public float EdgeLeft { get; init; } = float.NaN;
    public float EdgeFwd { get; init; } = float.NaN;
    public float EdgeUp { get; init; } = float.NaN;
    public float[] HandsLeft { get; init; } = [];
    /// <summary>Every hold of the hands.</summary>
    public LadderHand[] Hands { get; init; } = [];

    /// <summary>The path's forward, up and left at <paramref name="t"/> s (between its rows linearly; held before the first and after the last).</summary>
    public (float Forward, float Up, float Left) At(float t)
    {
        var ts = T;
        if (ts.Length == 0) return (0f, 0f, 0f);
        if (t <= ts[0]) return (F[0], U[0], S.Length > 0 ? S[0] : 0f);
        if (t >= ts[^1]) return (F[^1], U[^1], S.Length > 0 ? S[^1] : 0f);
        int lo = 0, hi = ts.Length - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (ts[mid] <= t) lo = mid; else hi = mid;
        }
        float span = ts[hi] - ts[lo], k = span > 1e-6f ? (t - ts[lo]) / span : 0f;
        float s = S.Length > 0 ? S[lo] + (S[hi] - S[lo]) * k : 0f;
        return (F[lo] + (F[hi] - F[lo]) * k, U[lo] + (U[hi] - U[lo]) * k, s);
    }

    /// <summary>The first hand's contact on a new hold in the clip (s; the clip's length when none): where a getting-on's start is warped by.</summary>
    public float FirstContact
    {
        get
        {
            float t = Duration;
            foreach (var h in Hands) if (h.ContactS >= 0f && h.ContactS < t) t = h.ContactS;
            return t;
        }
    }
}

/// <summary>
/// <b>one ladder set</b> (the leaning <c>ladder_*</c> or the vertical <c>ladder_v_*</c>): its ladder's numbers from the notes (<c>traverse.ladder</c>, at the
/// body's scale) and its clips by role.
/// </summary>
public sealed class LadderSet
{
    /// <summary>True for the upright set (<c>tilt_deg</c> 0).</summary>
    public required bool Vertical { get; init; }
    /// <summary>The lean off upright (radians), the rungs' spacing along the rails, rung 1's height over the ground, its front ahead of and its top over the ladder position's capsule bottom (m).</summary>
    public required float Tilt { get; init; }
    public required float Rung { get; init; }
    public required float FirstRung { get; init; }
    public required float RungFwd { get; init; }
    public required float LowerRungUp { get; init; }
    public required float HandsApart { get; init; }
    public required LadderClip Idle { get; init; }
    public required LadderClip Up { get; init; }
    public required LadderClip Down { get; init; }
    public required LadderClip OnBottom { get; init; }
    public required LadderClip OffBottom { get; init; }
    public required LadderClip OnTop { get; init; }
    public required LadderClip OffTop { get; init; }
    public LadderClip? OnTopRails { get; init; }
    public LadderClip? OffTopRails { get; init; }
    /// <summary>The side landings' clips, [left, right] (null for none).</summary>
    public LadderClip?[] OnSide { get; init; } = [null, null];
    public LadderClip?[] OffSide { get; init; } = [null, null];
    /// <summary>The upright set's ladder to a braced hang and back, [left, right] (null for none).</summary>
    public LadderClip?[] ToHang { get; init; } = [null, null];
    public LadderClip?[] FromHang { get; init; } = [null, null];

    /// <summary>The rise of a rung along the rails, upright (m).</summary>
    public float RungUp => Rung * Core.DetMath.Cos(Tilt);

    /// <summary>The way a rung comes forward along the rails (toward the wall), each rung up (m).</summary>
    public float RungIn => Rung * Core.DetMath.Sin(Tilt);

    /// <summary>The side's index ([left, right]) for a way (+1 left, −1 right).</summary>
    public static int SideIndex(int way) => way > 0 ? 0 : 1;
}

/// <summary>
/// <b>a person's ladders</b> for the motor (BodyMotor.LadderClips.cs): every <see cref="LadderClip"/> of the set and the two sets. Null for a body
/// without them (1.1's, a set, a person not given them).
/// </summary>
public sealed class LadderPaths
{
    public required LadderClip[] Clips { get; init; }
    public LadderSet? Vertical { get; init; }
    public LadderSet? Leaning { get; init; }

    /// <summary>The clip of index <paramref name="index"/> (from 0), or null.</summary>
    public LadderClip? At(int index) => index >= 0 && index < Clips.Length ? Clips[index] : null;

    public int IndexOf(LadderClip clip) => Array.IndexOf(Clips, clip);

    /// <summary>The set of a number (1 the upright, 2 the leaning), or null.</summary>
    public LadderSet? Set(int set) => set == 1 ? Vertical : set == 2 ? Leaning : null;
}

/// <summary>
/// <b>a ladder climbed on its clips</b> — the motor's state (part of the people's digested <c>PersonLadder</c>): which clip carries the capsule and how far
/// into it, the ladder and its set, the ladder position's rung, the place the clip's path is laid from and the way into the ladder, the capsule's difference at
/// the start taken out and the difference to where the clip must end laid on, the rate (the sprint's), a climb's way asked, a ledge's lip and top.
/// <see cref="Traversal"/> keeps the move's kind (<see cref="TraversalKind.LadderOn"/>, <see cref="TraversalKind.Ladder"/>, <see cref="TraversalKind.PullUp"/>,
/// <see cref="TraversalKind.LadderOff"/>), as for every move.
/// </summary>
public struct LadderClipMove
{
    /// <summary>1 + the index of the clip carrying the capsule (<see cref="LadderPaths.Clips"/>), 0 for none.</summary>
    public int Clip;
    /// <summary>How far into it (s of the clip), and how long since it began (s).</summary>
    public float Time;
    public float Age;
    /// <summary>The set (1 upright, 2 leaning) and the ladder position's rung (the left foot's, from 1 at the foot).</summary>
    public int Set;
    public int Rung;
    /// <summary>The ladder position the path is laid from (the capsule's bottom), the level unit way into the ladder.</summary>
    public Vector3 Place;
    public Vector2 In;
    /// <summary>The capsule's difference at the start from the path (m), taken out by <see cref="BlendTo"/> s of age.</summary>
    public Vector3 Delta;
    public float BlendTo;
    /// <summary>What is laid on so the clip ends where it must (the next ladder position, the floor, the landing, the hang): across by the clip's time, up by its rise (m).</summary>
    public Vector3 Fix;
    /// <summary>The rate the clip plays at (1 its own; the sprint's).</summary>
    public float Rate;
    /// <summary>1 when the animation fades into it (it does not begin on the frame the last one ended on), 0 when it follows with no fade.</summary>
    public int Fade;
    /// <summary>A climb's way under way (+1 up, −1 down), 0 none; a side's (+1 left, −1 right).</summary>
    public int Way;
    /// <summary>A hang's lip (the hands' middle on the edge) and the ledge's top (world Z), going to or from it.</summary>
    public Vector3 Lip;
    public float Top;
}
