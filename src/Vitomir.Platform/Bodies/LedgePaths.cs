// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the models repository's clips ledge clips carry their capsule from the hang's place under the edge (CONVENTIONS, Notes,
// traverse.hang, shimmy, grab, loops_from): the motor places and moves the capsule by them (BodyMotor.Ledge.cs).
using System.Numerics;

namespace Vitomir.Platform.Bodies;

/// <summary>What a ledge clip plays (the models repository's ).</summary>
public enum LedgeRole
{
    None = 0,
    /// <summary>The hang, a loop: <c>hang_free</c> (the legs down) or <c>hang_braced</c> (the feet on the wall).</summary>
    Hang = 1,
    /// <summary><c>hang_brace_on</c>: from the free hang to the braced, the feet swung up onto the wall.</summary>
    BraceOn = 2,
    /// <summary><c>hang_brace_off</c>: from the braced hang to the free, the feet let off the wall.</summary>
    BraceOff = 3,
    /// <summary>A shimmy's cycle, from its hang to its hang a step along (<c>shimmy_left_free</c> …).</summary>
    Shimmy = 4,
    /// <summary><c>pull_up_free</c>, <c>pull_up_braced</c>: from the hang up onto the top, standing.</summary>
    PullUp = 5,
    /// <summary><c>drop_free</c>, <c>drop_braced</c>: the hands let go, the capsule falling from the first frame.</summary>
    Drop = 6,
    /// <summary><c>grab_jump_up</c>: from standing below, a jump and the hands onto the edge; ends in the free hang.</summary>
    GrabStand = 7,
    /// <summary><c>grab_run</c>: out of a run, the running jump caught at its top; ends in the braced hang.</summary>
    GrabRun = 8,
    /// <summary><c>grab_fall</c>: falling past the edge and catching it; ends in the free hang.</summary>
    GrabFall = 9,
    /// <summary>
    /// 65: a leap from the braced hang to the next ledge, from <c>hang_braced</c>'s first frame to it there — to a side
    /// (<c>hang_leap_left</c>, <c>_right</c>), up (<c>hang_leap_up</c>), down (<c>hang_leap_down</c>), or back to a facing ledge turning about (<c>hang_leap_back</c>).
    /// </summary>
    Leap = 10,
    /// <summary>Hand over hand round a corner of the lip (<c>hang_corner_&lt;out|in&gt;_&lt;braced|free&gt;_&lt;left|right&gt;</c>), the body turning a quarter.</summary>
    Corner = 11,
    /// <summary>On a wall by its holds, still (<c>wall_idle</c>, a loop): the wall position's capsule.</summary>
    WallIdle = 12,
    /// <summary>A move on the wall from <c>wall_idle</c>'s first frame to it a step away (careful, quick or long; up, down, to a side).</summary>
    WallMove = 13,
    /// <summary>Getting onto a wall from the ground (<c>wall_on_ground</c>) or with a jump (<c>wall_on_jump</c>), ending on <c>wall_idle</c>'s first frame.</summary>
    WallOn = 14,
    /// <summary>Down off a wall onto the ground at its foot (<c>wall_off_ground</c>), standing.</summary>
    WallOff = 15,
    /// <summary>Off a wall into the fall: the hands let go (<c>wall_let_go</c>) or a push off it (<c>wall_push_off</c>).</summary>
    WallDrop = 16,
    /// <summary>Over the top: from the wall's holds onto its edge, ending on <c>hang_braced</c>'s first frame (<c>wall_to_hang_braced</c>).</summary>
    WallToHang = 17,
    /// <summary>Round a corner of the wall, outside or inside (<c>wall_corner_&lt;out|in&gt;_&lt;left|right&gt;</c>), the body turning a quarter.</summary>
    WallCorner = 18,
    /// <summary>The hand shape for a face with nothing to hold (<c>wall_grip_splay</c>): not a move, its fingers laid over a wall clip's.</summary>
    WallGrip = 19,
    /// <summary>Still in a squeeze (<c>squeeze_idle</c>) or in cover (<c>cover_idle</c>), a loop.</summary>
    SideIdle = 20,
    /// <summary>A side-step's cycle in a squeeze (<c>squeeze_left</c>, <c>_right</c>) or along cover (<c>cover_left</c>, <c>_right</c>), from the narrow stance to it a step along.</summary>
    SideStep = 21,
    /// <summary>Into cover from standing, two steps back to the wall (<c>cover_in</c>), ending on <c>cover_idle</c>'s first frame.</summary>
    CoverIn = 22,
    /// <summary>Out of cover, up and two steps forward to standing (<c>cover_out</c>), from <c>cover_idle</c>'s first frame.</summary>
    CoverOut = 23,
    /// <summary>A quarter turn on the spot into a squeeze and out of it (<c>turn_left_90</c>, <c>turn_right_90</c>: their root's turn carried by the facing).</summary>
    SideTurn = 24,
}

/// <summary>A hold of a wall clip (<c>traverse.wall.holds</c>): which limb (0 the left hand, 1 the right, 2 the left foot, 3 the right), when it is taken and let go (s; −1 and 99 for before and after the clip), where (m, at the body's scale: to the left of and above the capsule's bottom at the wall position the clip starts at; a corner's along the face from its corner, on the first face or the other).</summary>
public readonly record struct WallHold(int Limb, float On, float Off, float Left, float Up, float Along, bool Other)
{
    /// <summary>Whether it is a hand's.</summary>
    public bool Hand => Limb < 2;
}

/// <summary>
/// <b>one ledge clip</b> for the motor (the models repository's <c>traverse</c> note with its <c>hang</c>, read by the people's motion and handed here as plain
/// numbers at the body's scale): which part of the ledge it plays, whether its hang is braced, its path (a row a frame: the capsule's forward, up and, for
/// a shimmy, to the left from <b>the hang's place</b> — <see cref="EdgeFwd"/> back from the edge and <see cref="EdgeUp"/> below its top), and a grab's and a
/// shimmy's numbers.
/// </summary>
public sealed class LedgeClip
{
    /// <summary>The clip (the character assets' index), for the animation to play.</summary>
    public required int Clip { get; init; }
    public required string Name { get; init; }
    public required LedgeRole Role { get; init; }
    /// <summary>Whether the hang it is in (a hang, a shimmy, a pull-up, a drop) or ends in (a grab, a brace) is braced.</summary>
    public required bool Braced { get; init; }
    /// <summary>
    /// A grab that is a braced twin of the free one of its role (<c>grab_jump_up_braced</c>, <c>grab_fall_braced</c>):
    /// the same up to the catch, ending on <c>hang_braced</c>'s first frame. Never the role's own (<see cref="LedgePaths.Of(LedgeRole)"/>): played in its
    /// place where there is a wall under the edge (<see cref="LedgePaths.TwinOf"/>).
    /// </summary>
    public bool Twin { get; init; }
    public required float Duration { get; init; }
    public required bool Loops { get; init; }
    /// <summary>The path: each row's time (s), forward (towards the edge), up and to the left (m) from the hang's place.</summary>
    public required float[] T { get; init; }
    public required float[] F { get; init; }
    public required float[] U { get; init; }
    public required float[] S { get; init; }
    /// <summary>The hang's place: this far back from the edge and below its top (m; <c>edge_fwd_m</c>, <c>edge_up_m</c>).</summary>
    public required float EdgeFwd { get; init; }
    public required float EdgeUp { get; init; }
    /// <summary>The knuckles in the hang, forward of and above the edge (m; <c>hands</c>), and the hands' spacing.</summary>
    public float HandFwd { get; init; }
    public float HandUp { get; init; }
    public float HandsApart { get; init; }
    /// <summary>A shimmy's way (+1 to the left, −1 to the right; 0 for any other), its step a cycle (m) and its speed (m/s).</summary>
    public int Way { get; init; }
    public float Step { get; init; }
    public float Speed { get; init; }
    /// <summary>A grab's: the edge heights it serves (m above the ground it starts from; NaN for a falling one), its take-off and the hands' contact (s), its take-off speed up and the run's speed (m/s), the run's phase it enters at.</summary>
    public float Low { get; init; } = float.NaN;
    public float High { get; init; } = float.NaN;
    public float TakeoffS { get; init; } = float.NaN;
    public float ContactS { get; init; } = float.NaN;
    public float VUp { get; init; } = float.NaN;
    public float RunSpeed { get; init; } = float.NaN;
    public float EnterPhase { get; init; } = float.NaN;
    /// <summary>A falling grab's: the speed it was made falling at (m/s), and how far above (and forward of) its hang place the capsule is as it begins (m).</summary>
    public float FallSpeed { get; init; } = float.NaN;
    public float StartsOver { get; init; } = float.NaN;
    public float StartsOff { get; init; } = float.NaN;

    /// <summary>
    /// A leap's (<c>traverse.leap</c>): where the next hang is from this one (m; to the left, up; the leap back's facing face this far behind), the feet
    /// leaving the wall (s; the hands' contact is <see cref="ContactS"/>).
    /// </summary>
    public float LeapLeft { get; init; }
    public float LeapUp { get; init; }
    public float LeapBack { get; init; }
    public float PushS { get; init; } = float.NaN;

    /// <summary>
    /// A clip that turns the body (<c>turn_deg</c>, <c>root_turns</c>: the corners, the leap back): the turn over its whole length (radians, to the left
    /// positive; 0 for none). Its path is in its first frame's axes; after it the body faces this much further round.
    /// </summary>
    public float Turn { get; init; }

    /// <summary>
    /// The root bone's turn through a turning clip, 30 times a second and at its end (radians, from 0 at its start; measured at load,
    /// <c>PersonLedgeClips.Measure</c>): the turn the body's facing carries as it plays, the root's own taken out of the drawn pose. Empty for none.
    /// </summary>
    public float[] Yaws { get; set; } = [];

    /// <summary>A corner's (<c>traverse.corner</c>): outside (the face turns away) or inside, the corner from the hang's middle and the end's hang from the corner (m).</summary>
    public bool Outside { get; init; }
    public float CornerAlong { get; init; } = float.NaN;
    public float EndsAlong { get; init; } = float.NaN;

    /// <summary>
    /// A corner's room — how near its own path comes to the block it was made round (the lip's two faces from its corner), less two skins (m; measured
    /// at load): the radius the body is swept with through the turn, so the test is of the room the clip itself needs.
    /// </summary>
    public float Room { get; set; } = float.NaN;

    /// <summary>
    /// A wall clip's (<c>traverse.wall</c>): the face ahead of the capsule's bottom at the wall position, the ground under it (<c>ground_up_m</c>, negative),
    /// where the move ends (to the left, up), every hold, a long move's or a careful one's (the quick ones are the shorter of a step's two), the knuckles off a
    /// hold's front edge (<c>hands.fwd_m</c>), the hand holds' depth; a getting-on with a jump's (<c>jump</c>: <see cref="TakeoffS"/>, <see cref="ContactS"/>);
    /// the top's edge for the clip over it (ahead, up; NaN none).
    /// </summary>
    public float WallFace { get; init; } = float.NaN;
    public float WallGround { get; init; }
    public float StepLeft { get; init; }
    public float StepUp { get; init; }
    public WallHold[] Holds { get; init; } = [];
    public bool Long { get; init; }
    public bool Quick { get; set; }
    public float HandOff { get; init; } = float.NaN;
    public float HoldDepth { get; init; }
    public bool Jump { get; init; }
    /// <summary>A drop off the wall that pushes off it (<c>wall_push_off</c>: its <c>airborne.v_away_m_s</c> over nought), not the let-go.</summary>
    public bool Push { get; init; }
    public float TopFwd { get; init; } = float.NaN;
    public float TopUp { get; init; } = float.NaN;
    /// <summary>A long move's part with no hand on a hold (s; <c>long.airborne_s</c>), NaN for none.</summary>
    public float AirFrom { get; init; } = float.NaN;
    public float AirTo { get; init; } = float.NaN;
    /// <summary>
    /// A side-step clip's (<c>traverse.side_step</c>): squeezing (1) or in cover (2), 0 for none (a <see cref="LedgeRole.SideTurn"/> is a squeeze's, 1);
    /// from the capsule's middle the skin's reach ahead and behind (m; a squeeze's <c>body_front_m</c>, <c>body_back_m</c>, a cover's <c>body_front_m</c> and its
    /// wall behind, <c>wall_back_m</c>), the gap a squeeze needs (<c>gap_min_m</c>), where <c>cover_in</c> starts from where the cover's capsule will be
    /// (<c>stand_off_wall_m</c>); a step's way is <see cref="Way"/> (+1 to the left) and its length <see cref="Step"/>.
    /// </summary>
    public byte SideKind { get; init; }
    public float SideFront { get; init; } = float.NaN;
    public float SideBack { get; init; } = float.NaN;
    public float GapMin { get; init; } = float.NaN;
    public float WallBack { get; init; } = float.NaN;
    public float StandOff { get; init; } = float.NaN;
    /// <summary>Whether it is one of the squeeze's or the cover's clips (<see cref="LedgeRole.SideIdle"/> … <see cref="LedgeRole.SideTurn"/>).</summary>
    public bool OnSide => Role is >= LedgeRole.SideIdle and <= LedgeRole.SideTurn;

    /// <summary>Whether it is one of the wall's clips (<see cref="LedgeRole.WallIdle"/> … <see cref="LedgeRole.WallGrip"/>).</summary>
    public bool OnWall => Role is >= LedgeRole.WallIdle and <= LedgeRole.WallGrip;

    /// <summary>The body's turn by <paramref name="t"/> s of a turning clip (radians, to the left positive): its root's, or eased over its length without the table.</summary>
    public float TurnAt(float t)
    {
        if (Turn == 0f) return 0f;
        var y = Yaws;
        if (y.Length < 2)
        {
            float k = Duration > 0f ? Math.Clamp(t / Duration, 0f, 1f) : 1f;
            return Turn * k * k * (3f - 2f * k);
        }
        if (t >= Duration) return y[^1];
        float f = MathF.Max(0f, t) * 30f;
        int i = Math.Min((int)f, y.Length - 2);
        float span = i == y.Length - 2 ? MathF.Max(1e-6f, Duration * 30f - i) : 1f;
        float a = Math.Clamp((f - i) / span, 0f, 1f);
        return y[i] + (y[i + 1] - y[i]) * a;
    }

    /// <summary>A grab from the ground's own edge, m above the ground its first frame stands on (its path's first row on that ground).</summary>
    public float OwnHeight => EdgeUp - (U.Length > 0 ? U[0] : 0f);

    /// <summary>How far ahead of the capsule's centre the edge is at its first frame (m).</summary>
    public float OwnFace => EdgeFwd - (F.Length > 0 ? F[0] : 0f);

    /// <summary>Whether an edge <paramref name="h"/> m above the ground is within a grab's band (a millimetre over for the level's rounding).</summary>
    public bool Serves(float h) => !float.IsNaN(Low) && h >= Low - BodyMotor.Within && h <= High + BodyMotor.Within;

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
}

/// <summary>
/// <b>a person's ledge</b> for the motor (<see cref="BodyMotor"/>, BodyMotor.Ledge.cs): every <see cref="LedgeClip"/> of the set and which plays what. Null
/// for a body without them (1.1's, a set, a person not given the ledge).
/// </summary>
public sealed class LedgePaths
{
    public required LedgeClip[] Clips { get; init; }

    /// <summary>The hang's place from the edge (m): the free hang's.</summary>
    public required float EdgeFwd { get; init; }
    public required float EdgeUp { get; init; }

    /// <summary>
    /// Measured from the braced hang's first frame (the people's motion, at the kind's scale): each foot's ball forward of, to the left of and above the
    /// capsule (m) — where the feet are on the wall, for the wall under the edge (<see cref="BodyMotor.WallUnder"/>).
    /// </summary>
    public Vector3[] BracedFeet { get; set; } = [];

    /// <summary>The clip of index <paramref name="index"/> (from 0), or null.</summary>
    public LedgeClip? At(int index) => index >= 0 && index < Clips.Length ? Clips[index] : null;

    public int IndexOf(LedgeClip clip) => Array.IndexOf(Clips, clip);

    /// <summary>The first clip of <paramref name="role"/> whose hang is <paramref name="braced"/> (a shimmy's of <paramref name="way"/> too), or null.</summary>
    public LedgeClip? Of(LedgeRole role, bool braced, int way = 0)
    {
        foreach (var c in Clips) if (c.Role == role && c.Braced == braced && (way == 0 || c.Way == way)) return c;
        return null;
    }

    /// <summary>The first clip of <paramref name="role"/> (a grab's; not a braced twin), or null.</summary>
    public LedgeClip? Of(LedgeRole role)
    {
        foreach (var c in Clips) if (c.Role == role && !c.Twin) return c;
        return null;
    }

    /// <summary>The braced twin of a grab that ends free (<c>grab_jump_up</c>'s <c>grab_jump_up_braced</c>, <c>grab_fall</c>'s <c>grab_fall_braced</c>), or null.</summary>
    public LedgeClip? TwinOf(LedgeClip grab)
    {
        if (grab.Twin) return grab;
        if (grab.Braced) return null;
        foreach (var c in Clips) if (c.Twin && c.Role == grab.Role) return c;
        return null;
    }

    /// <summary>
    /// The leap of the set that goes to a side (<paramref name="left"/> +1 left, −1 right, 0 neither) and up or down (<paramref name="up"/> +1, −1, 0), or
    /// back to a facing ledge (<paramref name="back"/>); null for none.
    /// </summary>
    public LedgeClip? LeapOf(int left, int up, bool back = false)
    {
        foreach (var c in Clips)
            if (c.Role == LedgeRole.Leap && (back ? c.LeapBack > 0f : c.LeapBack <= 0f && Sign(c.LeapLeft) == left && Sign(c.LeapUp) == up)) return c;
        return null;
        static int Sign(float v) => v > 0.05f ? 1 : v < -0.05f ? -1 : 0;
    }

    /// <summary>The corner of the set, outside or inside, from the braced or the free hang, to the left (+1) or the right (−1); null for none.</summary>
    public LedgeClip? CornerOf(bool outside, bool braced, int way)
    {
        foreach (var c in Clips) if (c.Role == LedgeRole.Corner && c.Outside == outside && c.Braced == braced && c.Way == way) return c;
        return null;
    }

    /// <summary>Whether the set has any leap or corner (the models repository's and after).</summary>
    public bool HasHangMoves
    {
        get
        {
            foreach (var c in Clips) if (c.Role is LedgeRole.Leap or LedgeRole.Corner) return true;
            return false;
        }
    }

    /// <summary>Whether the set has the walls' clips (the models repository's clips: the idle, the getting on and off, the moves).</summary>
    public bool HasWalls => Wall(LedgeRole.WallIdle) is not null && Wall(LedgeRole.WallOn) is not null && WallMoveOf(0, 1, quick: true) is not null;

    /// <summary>The first wall clip of <paramref name="role"/>, or null.</summary>
    public LedgeClip? Wall(LedgeRole role)
    {
        foreach (var c in Clips) if (c.Role == role) return c;
        return null;
    }

    /// <summary>The getting-on from the ground (<paramref name="jump"/> false) or with a jump; the let-go (<paramref name="push"/> false) or the push-off.</summary>
    public LedgeClip? WallOn(bool jump)
    {
        foreach (var c in Clips) if (c.Role == LedgeRole.WallOn && c.Jump == jump) return c;
        return null;
    }

    public LedgeClip? WallDrop(bool push)
    {
        foreach (var c in Clips) if (c.Role == LedgeRole.WallDrop && c.Push == push) return c;
        return null;
    }

    /// <summary>The wall's move to a side (<paramref name="left"/> +1, −1, 0) and up or down (<paramref name="up"/>): a long one (<paramref name="long"/>), else the quick or the careful one.</summary>
    public LedgeClip? WallMoveOf(int left, int up, bool quick, bool @long = false)
    {
        LedgeClip? any = null;
        foreach (var c in Clips)
        {
            if (c.Role != LedgeRole.WallMove || c.Long != @long || Sign(c.StepLeft) != left || Sign(c.StepUp) != up) continue;
            if (@long || c.Quick == quick) return c;
            any ??= c;
        }
        return any;
        static int Sign(float v) => v > 0.05f ? 1 : v < -0.05f ? -1 : 0;
    }

    /// <summary>The wall's corner, outside or inside, to the left (+1) or the right (−1); null for none.</summary>
    public LedgeClip? WallCornerOf(bool outside, int way)
    {
        foreach (var c in Clips) if (c.Role == LedgeRole.WallCorner && c.Outside == outside && c.Way == way) return c;
        return null;
    }

    /// <summary>The squeeze's (<paramref name="kind"/> 1) or the cover's (2) clip of <paramref name="role"/> (a step's or a turn's of <paramref name="way"/>, +1 left, −1 right), or null.</summary>
    public LedgeClip? Side(byte kind, LedgeRole role, int way = 0)
    {
        foreach (var c in Clips) if (c.Role == role && c.SideKind == kind && (way == 0 || c.Way == way)) return c;
        return null;
    }

    /// <summary>Whether the set has the squeeze's clips (the models repository: the idle, both steps, and the quarter turns).</summary>
    public bool HasSqueeze => Side(1, LedgeRole.SideIdle) is not null && Side(1, LedgeRole.SideStep, 1) is not null && Side(1, LedgeRole.SideStep, -1) is not null
                              && Side(1, LedgeRole.SideTurn, 1) is not null && Side(1, LedgeRole.SideTurn, -1) is not null;

    /// <summary>Whether the set has the cover's clips (the idle, both steps, in and out).</summary>
    public bool HasCover => Side(2, LedgeRole.SideIdle) is not null && Side(2, LedgeRole.SideStep, 1) is not null && Side(2, LedgeRole.SideStep, -1) is not null
                            && Side(2, LedgeRole.CoverIn) is not null && Side(2, LedgeRole.CoverOut) is not null;

    /// <summary>The highest edge any grab of the set serves from the ground (m), 0 for none.</summary>
    public float HighestGrab
    {
        get
        {
            float h = 0f;
            foreach (var c in Clips) if (!float.IsNaN(c.High)) h = MathF.Max(h, c.High);
            return h;
        }
    }
}

/// <summary>
/// <b>the ledge's state</b> (the motor's; part of the people's digested <c>PersonLedge</c>): which clip carries the capsule now and how far into it, the
/// hang's place (the feet for the path's nought) and the way into the wall, whether the hang is braced, the capsule's difference from the clip's path taken
/// out after a change of clip; a shimmy's way; a pull-up asked while another clip plays; and a running grab's press held for the run's footfall.
/// <see cref="Traversal"/> keeps the move's kind (<see cref="TraversalKind.Grab"/>, <see cref="TraversalKind.Hang"/>, <see cref="TraversalKind.PullUp"/>,
/// <see cref="TraversalKind.Drop"/>), its lip and the face's normal, as for every move.
/// </summary>
public struct LedgeMove
{
    /// <summary>1 + the index of the <see cref="LedgeClip"/> carrying the capsule (<see cref="LedgePaths.Clips"/>), 0 for none.</summary>
    public int Clip;
    /// <summary>How far into it (s of the clip), and how long since it began (s).</summary>
    public float Time;
    public float Age;
    /// <summary>1 when the hang is braced (the feet on the wall under the edge).</summary>
    public int Braced;
    /// <summary>The hang's place (the feet for the path's nought: the clip's hang place from the real edge), the level unit way into the wall, the real top (world Z).</summary>
    public Vector3 Place;
    public Vector2 In;
    public float Top;
    /// <summary>The capsule's difference from the clip's path at its start (m), taken out across between <see cref="AcrossFrom"/> and <see cref="AcrossTo"/> and up between <see cref="UpFrom"/> and <see cref="UpTo"/> (s of <see cref="Age"/>).</summary>
    public Vector3 Delta;
    public float AcrossFrom, AcrossTo, UpFrom, UpTo;
    /// <summary>The ground a grab from the ground starts on (world Z; the swept capsule's floor until it leaves); −∞ for none.</summary>
    public float Floor;
    /// <summary>A shimmy's way (+1 to the left, −1 to the right), 0 for none.</summary>
    public int Way;
    /// <summary>1 when a pull-up was asked while a clip that is not the hang played (taken when the hang comes).</summary>
    public int Want;
    /// <summary>1 when the animation should fade into the clip (it does not begin on the frame the last one ended on), 0 when it follows with no fade.</summary>
    public int Fade;
    /// <summary>1 while a press of jump made running at a running grab is held for the run's footfall (the animation tells how long: <see cref="Wait"/>).</summary>
    public int Pending;
    /// <summary>The seconds left to hold the press (−1: not yet told), and how long it has been held (s).</summary>
    public float Wait;
    public float Held;
    /// <summary>The face's distance ahead at the press (m) and the speed then (m/s), for the animation's choice of the footfall.</summary>
    public float Face;
    public float Speed;
    /// <summary>1 on the tick a press goes to start a grab from the ground.</summary>
    public int Go;
    /// <summary>1 while a move of the motor's own (a grab outside the clips' bands, a lowering from an edge) is under way to end in the clip's hang.</summary>
    public int Into;
    /// <summary>After a drop, the seconds left before a lip can be taken again.</summary>
    public float Again;
    /// <summary>A falling grab's: the rate its clip plays at to the hands' contact (the fall's speed over the clip's own; 0 or 1: its own).</summary>
    public float Rate;
}
