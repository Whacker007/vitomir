// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the models repository's clips (hanging from a ledge, the shimmy, the braces, the pull-up, the drop and the grabs), played as
// their notes say (CONVENTIONS, Notes, traverse.hang, shimmy, grab, loops_from).
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.People;
using Vitomir.Platform.Bodies;

namespace Vitomir.Platform.Animation.People;

/// <summary>
/// <b>the ledge on the models repository's clips</b> (<see cref="PersonLedgeClips"/>): whether they play. Off (<see cref="Off"/>:
/// <c>--ledge-clips-off</c>) a person grabs, hangs, shimmies, pulls up and drops on the motor's own paths with the earlier build's stand-ins, and the world has no
/// <see cref="PersonLedge"/> store. They ride on the moves over and onto things and the air's clips: with <c>--over-clips-off</c> or
/// <c>--air-clips-off</c> they are off too. A tape carries them in its note <see cref="TapeNote"/>; a tape without it is the earlier build's.
/// </summary>
public sealed record PersonLedgeSettings
{
    /// <summary>Whether the models repository's ledge clips play (on by default; off: the earlier build's, <c>--ledge-clips-off</c>).</summary>
    public bool Clips { get; init; } = true;

    /// <summary>Whether any is on.</summary>
    public bool Any => Clips;

    /// <summary>None.</summary>
    public static PersonLedgeSettings Off => new() { Clips = false };

    /// <summary>The tape head's note.</summary>
    public const string TapeNote = "ledge";

    /// <summary>The note: <c>off</c>, else <c>clips=1</c>.</summary>
    public string Note() => Clips ? "clips=1" : "off";

    /// <summary>These settings with a tape's note laid on; none (an older tape) or <c>off</c> is <see cref="Off"/>.</summary>
    public PersonLedgeSettings WithNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note) || !note.Contains('=')) return Off;
        var s = this with { Clips = true };
        foreach (var part in note.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0 || !float.TryParse(part[(eq + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) continue;
            if (part[..eq] == "clips") s = s with { Clips = v != 0f };
        }
        return s.Clips ? s : Off;
    }
}

/// <summary>
/// <b>a person's ledge on clips</b> — the simulation's state (digested): the motor's (<see cref="Move"/>, <see cref="LedgeMove"/>) and the animation's
/// last clip and the run's pace to a running grab's footfall. Only the people given one by the session have it (the player: the models repository's note —
/// ordinary townsfolk do not hang), and a world has the store only with the clips on and people who have them (a newer set), so the and
/// The earlier build's digest as before.
/// </summary>
public struct PersonLedge
{
    /// <summary>The motor's: the ledge clip carrying the capsule, the hang's place, a running grab's press held for the run's footfall.</summary>
    public LedgeMove Move;

    /// <summary>1 + the index of the ledge clip last played (<see cref="LedgePaths.Clips"/>), 0 none.</summary>
    public int Played;

    /// <summary>1 while the run's pace is changed to bring its footfall to a running grab.</summary>
    public int Paced;
}

/// <summary>
/// <b>a person's ledge clips</b> (the models repository's clips: <c>hang_free</c>, <c>hang_braced</c>, <c>hang_brace_on</c>, <c>hang_brace_off</c>,
/// <c>shimmy_left_free</c>, <c>shimmy_right_free</c>, <c>shimmy_left_braced</c>, <c>shimmy_right_braced</c>, <c>pull_up_free</c>, <c>pull_up_braced</c>,
/// <c>drop_free</c>, <c>drop_braced</c>, <c>grab_jump_up</c>, <c>grab_run</c>, <c>grab_fall</c>), found by their notes (<c>traverse.hang</c>, <c>shimmy</c>,
/// <c>grab</c>, <c>ends</c>, <c>airborne</c>); null on <see cref="PersonMotion.Ledge"/> for a set without the hang, the pull-up and the drop (an older set).
/// </summary>
public sealed class PersonLedgeClips
{
    /// <summary>The ledge's clips for the motor.</summary>
    public required LedgePaths Paths { get; init; }

    /// <summary>Every clip above, for <see cref="Holds"/>.</summary>
    public required int[] All { get; init; }

    /// <summary>Measured from the free hang's first frame (<see cref="Measure"/>, at the kind's scale): each hand's knuckles forward of, to the left of and above the capsule (m).</summary>
    public Vector3[] Knuckles { get; private set; } = [];

    /// <summary>Whether <paramref name="clip"/> is one of them.</summary>
    public bool Holds(int clip) => clip >= 0 && Array.IndexOf(All, clip) >= 0;

    /// <summary>
    /// The clips' own numbers measured in the model's space (the model faces +Y, its left −X): the braced hang's balls at its first frame
    /// (<see cref="LedgePaths.BracedFeet"/>, for the wall under the edge) and the free hang's knuckles (<see cref="Knuckles"/>).
    /// </summary>
    public void Measure(CharacterAssets assets, Skeleton skeleton, float scale, in LimbMap limbs, int root = -1)
    {
        // The root's turn through each clip that turns the body (the corners, the leap back), for the facing to carry as it plays.
        if (root >= 0)
            foreach (var c in Paths.Clips)
                if (c.Turn != 0f && assets.Clip(c.Clip) is INotedClip turning) c.Yaws = TurnClip.RootYaws(turning, skeleton, root);
        int n = skeleton.Count;
        var local = new BoneTransform[n];
        var model = new Matrix4x4[n];
        Vector3 At(int bone) => new(model[bone].Translation.Y * scale, -model[bone].Translation.X * scale, model[bone].Translation.Z * scale);
        if (Paths.Of(LedgeRole.Hang, braced: true) is { } braced)
        {
            int l = skeleton.Find("ball_l"), r = skeleton.Find("ball_r");
            if (l >= 0 && r >= 0)
            {
                assets.Clip(braced.Clip).Sample(0f, skeleton, local);
                PoseEvaluator.ToModel(skeleton, local, model);
                Paths.BracedFeet = [At(l), At(r)];
            }
        }
        if (Paths.Of(LedgeRole.Hang, braced: false) is { } free && limbs.LeftArm.Tip >= 0 && limbs.RightArm.Tip >= 0)
        {
            assets.Clip(free.Clip).Sample(0f, skeleton, local);
            PoseEvaluator.ToModel(skeleton, local, model);
            Knuckles = [At(limbs.LeftArm.Tip), At(limbs.RightArm.Tip)];
        }
        // The splayed grip's finger and thumb bones (both hands: its use, laid over a wall clip's), and how much nearer the face its knuckles
        // are than the holds' grip's in the idle (forward, m: the hands moved so onto a face with nothing to hold, where the pads meet it).
        if (Paths.Wall(LedgeRole.WallGrip) is { } grip && Paths.Wall(LedgeRole.WallIdle) is { } idle && limbs.LeftArm.Tip >= 0 && limbs.RightArm.Tip >= 0)
        {
            var bones = new List<int>();
            foreach (var f in new[] { "thumb", "index", "middle", "ring", "pinky" })
                for (int k = 1; k <= 3; k++)
                    foreach (var side in new[] { "l", "r" })
                        if (skeleton.Find($"{f}_0{k}_{side}") is >= 0 and var b) bones.Add(b);
            SplayBones = [.. bones];
            assets.Clip(idle.Clip).Sample(0f, skeleton, local);
            PoseEvaluator.ToModel(skeleton, local, model);
            var held = 0.5f * (At(limbs.LeftArm.Tip) + At(limbs.RightArm.Tip));
            WallKnuckles = [At(limbs.LeftArm.Tip), At(limbs.RightArm.Tip)];
            assets.Clip(grip.Clip).Sample(0f, skeleton, local);
            PoseEvaluator.ToModel(skeleton, local, model);
            var splay = 0.5f * (At(limbs.LeftArm.Tip) + At(limbs.RightArm.Tip));
            SplayNearer = splay.X - held.X;
            // The notes' own: the hold's grip's knuckles off the face (its hands.fwd_m, the hold's depth counted in) and the splayed one's.
            if (!float.IsNaN(idle.HandOff) && !float.IsNaN(grip.HandOff)) SplayNotes = grip.HandOff - idle.HandOff;
        }
    }

    /// <summary>The splayed grip's finger and thumb bones, both hands (empty without the grip).</summary>
    public int[] SplayBones { get; private set; } = [];

    /// <summary>The wall's idle's knuckles at its first frame (forward of, to the left of and above the capsule, m).</summary>
    public Vector3[] WallKnuckles { get; private set; } = [];

    /// <summary>
    /// How much further forward (toward the face) the splayed grip's knuckles are than the idle's (m; measured in their first frames, the arms as each clip
    /// has them), and as the notes have it (the grip's <c>hands.fwd_m</c> less the idle's: each the knuckles off the face, a hold's depth counted in): the hands
    /// are moved by the notes' onto a face with nothing to hold (the measured one says how much the grip's own arms reach to it).
    /// </summary>
    public float SplayNearer { get; private set; } = float.NaN;
    public float SplayNotes { get; private set; } = float.NaN;

    /// <summary>The clips from a set (the first of a name the one played) at the kind's <paramref name="scale"/>, or null when it has not the hang, the pull-up and the drop.</summary>
    public static PersonLedgeClips? From(IReadOnlyList<INotedClip> motions, Func<string, INotedClip?> named, Func<INotedClip, int> indexOf, float scale)
    {
        var clips = new List<LedgeClip>();
        var later = new List<LedgeClip>();   // the leaps and corners after every clip before them, so those keep their places (a tape's LedgeMove.Clip)
        foreach (var c in motions)
        {
            if (c.Notes.Traverse is not { Hang: { } hang, Times.Length: >= 2 } tv || !ReferenceEquals(named(c.Name), c)) continue;
            var role = RoleOf(c, tv, hang);
            if (role == LedgeRole.None) continue;
            (role is LedgeRole.Leap or LedgeRole.Corner ? later : clips).Add(Clip(c, tv, hang, role, indexOf(c), scale));
        }
        clips.AddRange(later);
        // The walls' clips after every other, so a tape's LedgeMove.Clip keeps its meaning.
        foreach (var c in motions)
        {
            if (c.Notes.Traverse is not { Wall: { } wall, Times.Length: >= 2 } tw || !ReferenceEquals(named(c.Name), c)) continue;
            if (WallClip(c, tw, wall, indexOf(c), scale) is { } wc) clips.Add(wc);
        }
        // Squeezing and cover after every other (a tape's LedgeMove.Clip keeps its meaning), and the quarter turns into and out of a squeeze.
        int sides = 0;
        foreach (var c in motions)
        {
            if (c.Notes.Traverse is not { SideStep: { } ss, Times.Length: >= 2 } ts || !ReferenceEquals(named(c.Name), c)) continue;
            if (SideClip(c, ts, ss, indexOf(c), scale) is { } sc) { clips.Add(sc); sides++; }
        }
        if (sides > 0)
            foreach (var name in new[] { "turn_left_90", "turn_right_90" })
                if (named(name) is { } turn && turn.Notes.RootTurns && !float.IsNaN(turn.Notes.TurnDeg) && turn.Duration > 0f)
                    clips.Add(new LedgeClip
                    {
                        Clip = indexOf(turn), Name = turn.Name, Role = LedgeRole.SideTurn, Braced = false, Duration = turn.Duration, Loops = false,
                        T = [0f, turn.Duration], F = [0f, 0f], U = [0f, 0f], S = [0f, 0f], EdgeFwd = 0f, EdgeUp = 0f, SideKind = 1,
                        Way = turn.Notes.TurnDeg > 0f ? 1 : -1, Turn = turn.Notes.TurnDeg * Core.DetMath.DegToRad,
                    });
        // The quick moves: of a step's two (the careful four-beat and the quick two-beat), the shorter.
        foreach (var c in clips)
            if (c.Role == LedgeRole.WallMove && !c.Long)
                c.Quick = !clips.Any(o => o.Role == LedgeRole.WallMove && !o.Long && !ReferenceEquals(o, c) && MathF.Abs(o.StepLeft - c.StepLeft) < 0.05f
                                          && MathF.Abs(o.StepUp - c.StepUp) < 0.05f && o.Duration < c.Duration - 1e-3f);
        if (clips.FirstOrDefault(c => c.Role == LedgeRole.Hang && !c.Braced) is not { } free
            || !clips.Any(c => c.Role == LedgeRole.PullUp) || !clips.Any(c => c.Role == LedgeRole.Drop)) return null;
        return new PersonLedgeClips
        {
            Paths = new LedgePaths { Clips = [.. clips], EdgeFwd = free.EdgeFwd, EdgeUp = free.EdgeUp },
            All = [.. clips.Where(c => c.Role != LedgeRole.SideTurn).Select(c => c.Clip).Distinct()],   // not the turns on the spot (the legs' turn plays them too)
        };
    }

    /// <summary>What a ledge clip plays, by its notes: a grab by its <c>start</c>, a shimmy by its <c>shimmy</c>, a pull-up by its <c>ends</c>, a drop by its
    /// <c>airborne</c> fall, a brace by its hang's <c>kind</c>, the hang a loop of its own.</summary>
    static LedgeRole RoleOf(INotedClip c, ClipTraverse tv, ClipHang hang)
    {
        // A leap by its leap, a corner by its corner (from the braced or the free hang to it: their start and then).
        if (tv.Leap is { Start: "hang_braced", Then: "hang_braced" } && !c.Loops) return LedgeRole.Leap;
        if (tv.Corner is { Start: "hang_braced" or "hang_free" } cr && cr.Then == cr.Start && tv.Side.Length == tv.Times.Length && !c.Loops
            && !float.IsNaN(cr.CornerAlongM) && !float.IsNaN(cr.EndsAlongM)) return LedgeRole.Corner;
        if (tv.Grab is { } g) return g.Start switch { "stand" => LedgeRole.GrabStand, "run" => LedgeRole.GrabRun, "fall" => LedgeRole.GrabFall, _ => LedgeRole.None };
        if (tv.Shimmy is not null && tv.Side.Length == tv.Times.Length && c.Loops) return LedgeRole.Shimmy;
        if (tv.EndsOnTop && tv.Ends.Length > 0) return LedgeRole.PullUp;
        if (c.Notes.Airborne is { } ab && ab.Then == "fall" && !c.Loops) return LedgeRole.Drop;
        if (hang.Kind == "free to braced") return LedgeRole.BraceOn;
        if (hang.Kind == "braced to free") return LedgeRole.BraceOff;
        if (c.Loops && hang.Kind is "free" or "braced") return LedgeRole.Hang;
        return LedgeRole.None;
    }

    /// <summary>A ledge clip for the motor: its path and its numbers at the kind's scale.</summary>
    static LedgeClip Clip(INotedClip c, ClipTraverse tv, ClipHang hang, LedgeRole role, int index, float scale)
    {
        var g = tv.Grab;
        var sh = tv.Shimmy;
        return new LedgeClip
        {
            Clip = index, Name = c.Name, Role = role, Braced = hang.EndsBraced, Twin = role is LedgeRole.GrabStand or LedgeRole.GrabFall && hang.EndsBraced, Duration = c.Duration, Loops = c.Loops, T = [.. tv.Times],
            F = [.. tv.Forward.Select(x => x * scale)], U = [.. tv.Up.Select(x => x * scale)], S = [.. tv.Side.Select(x => x * scale)],
            EdgeFwd = hang.EdgeFwd * scale, EdgeUp = hang.EdgeUp * scale, HandFwd = hang.HandFwd * scale, HandUp = hang.HandUp * scale,
            HandsApart = float.IsNaN(hang.HandsApart) ? 0f : hang.HandsApart * scale,
            Way = sh is not null ? (sh.Left ? 1 : -1) : tv.Corner is { } cw ? (cw.Left ? 1 : -1) : 0, Step = sh is null ? 0f : sh.StepM * scale, Speed = sh is null ? 0f : sh.SpeedMs * scale,
            Low = g is null ? float.NaN : g.Low * scale, High = g is null ? float.NaN : g.High * scale, TakeoffS = g?.TakeoffS ?? float.NaN,
            VUp = g is null ? float.NaN : g.VUpMs * scale, RunSpeed = g is null ? float.NaN : g.SpeedMs * scale,
            EnterPhase = g?.EnterPhase ?? float.NaN, FallSpeed = g is null ? float.NaN : g.FallSpeedMs * scale,
            StartsOver = g is null ? float.NaN : g.StartsOverHang * scale, StartsOff = g is null ? float.NaN : g.StartsOffHang * scale,
            // A leap's, a corner's, a turn's.
            LeapLeft = (tv.Leap?.LeftM ?? 0f) * scale, LeapUp = (tv.Leap?.UpM ?? 0f) * scale, LeapBack = (tv.Leap?.BackM ?? 0f) * scale,
            PushS = tv.Leap?.PushS ?? float.NaN, ContactS = g?.ContactS ?? tv.Leap?.ContactS ?? float.NaN,
            Turn = role is LedgeRole.Leap or LedgeRole.Corner && c.Notes.RootTurns && !float.IsNaN(c.Notes.TurnDeg) ? c.Notes.TurnDeg * Core.DetMath.DegToRad : 0f,
            Outside = tv.Corner?.Outside ?? false, CornerAlong = (tv.Corner?.CornerAlongM ?? float.NaN) * scale, EndsAlong = (tv.Corner?.EndsAlongM ?? float.NaN) * scale,
            Room = tv.Corner is { } corner ? CornerRoom(tv, hang, corner, scale) : float.NaN,
        };
    }

    /// <summary>
    /// A wall clip for the motor (<c>traverse.wall</c>) by its kind: the idle (a loop), a move (its step; a long one), a getting-on (a
    /// jump's by its <c>jump</c>), a getting-off onto the ground, a drop into the fall (the push-off by its <c>airborne</c> rise), over the top into the
    /// braced hang, a corner (its <c>corner</c> and turn), the splayed grip; null for anything else.
    /// </summary>
    static LedgeClip? WallClip(INotedClip c, ClipTraverse tv, ClipWall w, int index, float scale)
    {
        var role = w.Kind switch
        {
            "idle" when c.Loops => LedgeRole.WallIdle,
            "move" or "long" when !c.Loops && (MathF.Abs(w.StepLeftM) > 0.05f || MathF.Abs(w.StepUpM) > 0.05f) => LedgeRole.WallMove,
            "on" when !c.Loops => LedgeRole.WallOn,
            "off" when !c.Loops && c.Notes.Airborne is { Then: "fall" } => LedgeRole.WallDrop,
            "off" when !c.Loops => LedgeRole.WallOff,
            "to hang" when !c.Loops && !float.IsNaN(w.TopEdgeUpM) => LedgeRole.WallToHang,
            "corner" when !c.Loops && tv.Corner is { } cr && tv.Side.Length == tv.Times.Length && !float.IsNaN(cr.CornerAlongM) => LedgeRole.WallCorner,
            "grip" => LedgeRole.WallGrip,
            _ => LedgeRole.None,
        };
        if (role == LedgeRole.None || float.IsNaN(w.FaceFwdM)) return null;
        var holds = new List<WallHold>();
        foreach (var h in w.Holds)
        {
            int limb = h.Limb switch { "hand_l" => 0, "hand_r" => 1, "foot_l" => 2, "foot_r" => 3, _ => -1 };
            if (limb < 0) continue;
            holds.Add(new WallHold(limb, h.OnS, h.OffS, (float.IsNaN(h.LeftM) ? 0f : h.LeftM) * scale, (float.IsNaN(h.UpM) ? 0f : h.UpM) * scale,
                                   (float.IsNaN(h.AlongM) ? float.NaN : h.AlongM * scale), h.Face == "other"));
        }
        var corner = role == LedgeRole.WallCorner ? tv.Corner : null;
        return new LedgeClip
        {
            Clip = index, Name = c.Name, Role = role, Braced = false, Duration = c.Duration, Loops = c.Loops, T = [.. tv.Times],
            F = [.. tv.Forward.Select(x => x * scale)], U = [.. tv.Up.Select(x => x * scale)], S = [.. tv.Side.Select(x => x * scale)],
            EdgeFwd = w.FaceFwdM * scale, EdgeUp = 0f,
            WallFace = w.FaceFwdM * scale, WallGround = w.GroundUpM * scale, StepLeft = w.StepLeftM * scale, StepUp = w.StepUpM * scale, Holds = [.. holds],
            Long = w.Kind == "long", HandOff = float.IsNaN(w.HandFwdM) ? float.NaN : w.HandFwdM * scale, HoldDepth = w.HoldDepthM * scale,
            Jump = role == LedgeRole.WallOn && !float.IsNaN(w.JumpContactS), TakeoffS = w.JumpTakeoffS, ContactS = w.JumpContactS,
            Push = role == LedgeRole.WallDrop && c.Notes.Airborne is { VUpMs: > 0f },
            TopFwd = w.TopEdgeFwdM * scale, TopUp = w.TopEdgeUpM * scale, AirFrom = w.LongFromS, AirTo = w.LongToS,
            Way = corner is { } cw ? (cw.Left ? 1 : -1) : 0,
            Turn = corner is not null && c.Notes.RootTurns && !float.IsNaN(c.Notes.TurnDeg) ? c.Notes.TurnDeg * Core.DetMath.DegToRad : 0f,
            Outside = corner?.Outside ?? false, CornerAlong = (corner?.CornerAlongM ?? float.NaN) * scale, EndsAlong = (corner?.EndsAlongM ?? float.NaN) * scale,
            Room = corner is { } cc ? CornerRoom(tv, w.FaceFwdM, cc, scale) : float.NaN,
        };
    }

    /// <summary>
    /// A squeeze's or a cover's clip for the motor (<c>traverse.side_step</c>) by its name's part: the idle (a loop), a step's cycle (a loop with
    /// its path's fourth number: its way), <c>cover_in</c>, <c>cover_out</c>; null for anything else.
    /// </summary>
    static LedgeClip? SideClip(INotedClip c, ClipTraverse tv, ClipSideStep ss, int index, float scale)
    {
        byte kind = ss.Squeeze ? (byte)1 : ss.Cover ? (byte)2 : (byte)0;
        if (kind == 0) return null;
        bool stepping = c.Loops && tv.Side.Length == tv.Times.Length && tv.Side.Length > 1 && MathF.Abs(tv.Side[^1]) > 0.05f;
        var role = stepping ? LedgeRole.SideStep
                 : c.Loops ? LedgeRole.SideIdle
                 : kind == 2 && !float.IsNaN(ss.StandOffWallM) && ss.Then != "stand" ? LedgeRole.CoverIn
                 : kind == 2 && ss.Then == "stand" ? LedgeRole.CoverOut
                 : LedgeRole.None;
        if (role == LedgeRole.None) return null;
        float S(float v) => float.IsNaN(v) ? float.NaN : v * scale;
        // The entry may be cancelled until the back meets the wall: cover_in's contact, where its path comes within a centimetre of its end.
        float contact = float.NaN;
        if (role == LedgeRole.CoverIn && tv.Forward.Length == tv.Times.Length && tv.Forward.Length > 1)
            for (int k = 0; k < tv.Forward.Length; k++)
                if (MathF.Abs(tv.Forward[k] - tv.Forward[^1]) * scale <= 0.01f) { contact = tv.Times[k]; break; }
        return new LedgeClip
        {
            Clip = index, Name = c.Name, Role = role, Braced = false, Duration = c.Duration, Loops = c.Loops, T = [.. tv.Times],
            F = [.. tv.Forward.Select(x => x * scale)], U = [.. tv.Up.Select(x => x * scale)],
            S = tv.Side.Length == tv.Times.Length ? [.. tv.Side.Select(x => x * scale)] : new float[tv.Times.Length],
            EdgeFwd = 0f, EdgeUp = 0f, SideKind = kind,
            Way = stepping ? (tv.Side[^1] > 0f ? 1 : -1) : 0, Step = stepping ? MathF.Abs(tv.Side[^1]) * scale : S(ss.StepM),
            Speed = !float.IsNaN(ss.CycleS) && ss.CycleS > 0f && !float.IsNaN(ss.StepM) ? ss.StepM * scale / ss.CycleS : 0f,
            SideFront = S(ss.BodyFrontM), SideBack = kind == 2 ? S(ss.WallBackM) : S(ss.BodyBackM), GapMin = S(ss.GapMinM), WallBack = S(ss.WallBackM),
            StandOff = S(ss.StandOffWallM), ContactS = contact,
        };
    }

    /// <summary>
    /// How near a corner's own path comes to the block it was made round (the edge's face <c>edge_fwd_m</c> ahead of the hang's place, the other face
    /// <c>corner_along_m</c> to the side of it), less two skins (m, at the kind's scale): the radius the body is swept with through it.
    /// </summary>
    static float CornerRoom(ClipTraverse tv, ClipHang hang, ClipCorner corner, float scale) => CornerRoom(tv, hang.EdgeFwd, corner, scale);

    /// <summary>As the overload before, from a face <paramref name="ef"/> m ahead (a wall corner's <c>face_fwd_m</c>).</summary>
    static float CornerRoom(ClipTraverse tv, float ef, ClipCorner corner, float scale)
    {
        float along = corner.CornerAlongM, sgn = corner.Left ? 1f : -1f, least = float.PositiveInfinity;
        for (int i = 0; i < tv.Times.Length; i++)
        {
            float f = tv.Forward[i], s = sgn * tv.Side[i], d;
            if (corner.Outside)
            {
                bool inF = f >= ef, inS = s <= along;
                d = inF && inS ? 0f : inF ? s - along : inS ? ef - f : MathF.Sqrt((f - ef) * (f - ef) + (s - along) * (s - along));
            }
            else d = MathF.Min(ef - f, along - s);
            least = MathF.Min(least, d);
        }
        return MathF.Max(0f, least * scale - 2f * BodyMotor.Skin);
    }
}

/// <summary>
/// <b>playing the ledge's clips</b> (<see cref="AnimationSystem"/> calls it for a person with a <see cref="PersonLedge"/>, before the moves over and
/// onto things): the clip the motor carries the capsule by, at the motor's time (the motor runs the clip's path, so the drawn body and the capsule go
/// together), faded in over <see cref="BodyMotor.LedgeBlend"/> when the motor says it does not begin on the frame the last one ended on (a running grab from
/// the run at its own phase never); and a running grab's press held for the run's footfall told how long, the run paced to it (as the running vault's).
/// After a pull-up the gaits, after a drop the air (the fall loop and the landing by the drop's band), as for any other.
/// </summary>
public static class PeopleLedge
{
    [ThreadStatic] static BoneTransform[]? shown, frame;

    /// <summary>
    /// Of clip <paramref name="c"/> from <paramref name="first"/> to <paramref name="last"/> s (a frame apart), the time whose limbs (each arm's clavicle,
    /// upper arm, forearm and hand, each leg's thigh and calf: their turns in their parents) are nearest those of the clip shown at its time.
    /// </summary>
    public static float MatchEntry(CharacterAssets assets, PersonMotion person, in CharacterAnimation a, LedgeClip c, float first, float last)
    {
        var skeleton = assets.ModelOf(a.Kind).Skeleton;
        int n = skeleton.Count;
        if (shown is null || shown.Length < n) { shown = new BoneTransform[n]; frame = new BoneTransform[n]; }
        assets.Clip(a.Clip).Sample(a.Time, skeleton, shown);
        var rig = person.Rig;
        Span<int> bones = stackalloc int[14];
        int k = 0;
        for (int side = 0; side < 2; side++)
        {
            var arm = side == 0 ? rig.LeftArm : rig.RightArm;
            var leg = side == 0 ? rig.LeftLeg : rig.RightLeg;
            if (arm.Has)
            {
                int clavicle = skeleton.Bones[arm.Upper].Parent;
                if (clavicle >= 0) bones[k++] = clavicle;
                bones[k++] = arm.Upper; bones[k++] = arm.Lower; bones[k++] = arm.End;
            }
            if (leg.Has) { bones[k++] = leg.Upper; bones[k++] = leg.Lower; }
        }
        float best = first, least = float.PositiveInfinity;
        const float step = 1f / 30f;
        for (float t = first; t <= last + 1e-4f; t += step)
        {
            assets.Clip(c.Clip).Sample(t, skeleton, frame);
            float cost = 0f;
            for (int i = 0; i < k; i++) cost += 1f - MathF.Abs(Quaternion.Dot(shown[bones[i]].Rotation, frame![bones[i]].Rotation));
            if (cost < least - 1e-6f) { least = cost; best = t; }
        }
        return best;
    }

    /// <summary>True when it chose the clip (its time set, its rate nought); false to let the others choose.</summary>
    public static bool Choose(CharacterAssets assets, PersonMotion person, ref CharacterAnimation a, ref PersonAir air, ref PersonLedge ledge, in MotionInput input) =>
        Choose(assets, person, ref a, ref air, ref ledge, ref System.Runtime.CompilerServices.Unsafe.NullRef<PersonCommit>(), input);

    /// <summary>
    /// As the overload before, for a person with the commit point in the air (<paramref name="commit"/>, a null reference for none): a catch's clip
    /// (begun in the air at the commit) entered at its matching phase — of its air part (from its take-off rising, its first frame falling, to its hands'
    /// contact) the frame whose limbs are nearest those of the clip shown (<see cref="MatchEntry"/>) — and its time laid from there to the contact at the
    /// contact's moment, so the fade into it is short of a limb's half turn (a blend over one flips it: a jump of the hand).
    /// </summary>
    public static bool Choose(CharacterAssets assets, PersonMotion person, ref CharacterAnimation a, ref PersonAir air, ref PersonLedge ledge, ref PersonCommit commit,
                              in MotionInput input)
    {
        var paths = person.Ledge?.Paths;
        if (paths is null) return false;
        ref var l = ref ledge.Move;
        bool commitOn = !System.Runtime.CompilerServices.Unsafe.IsNullRef(ref commit);
        if (l.Clip > 0 && paths.At(l.Clip - 1) is { } c
            && input.Traversal is TraversalKind.Grab or TraversalKind.Hang or TraversalKind.PullUp or TraversalKind.Drop or TraversalKind.WallOn or TraversalKind.Wall or TraversalKind.WallOff   // the walls
                                   or TraversalKind.Squeeze or TraversalKind.Cover)   // squeezing and cover
        {
            var role = MotionSlots.Of(input.Traversal);
            if (commitOn && a.Clip != c.Clip)
            {
                commit.Matched = 0;
                if (l.Fade == 2 && !float.IsNaN(c.ContactS) && l.Time < c.ContactS - 2f * Core.World.TickSeconds && a.Clip >= 0)
                {
                    float first = c.Role == LedgeRole.GrabFall || float.IsNaN(c.TakeoffS) ? 0f : c.TakeoffS;
                    commit.AnimFrom = MatchEntry(assets, person, a, c, first, c.ContactS - 2f * Core.World.TickSeconds);
                    commit.MotorFrom = l.Time;
                    commit.Matched = 1;
                }
                else if (l.Fade == 4 && c.Role == LedgeRole.PullUp && a.Clip >= 0)
                {
                    // A pull-up going on, entered past its still start: the animation in at the frame of its start to there nearest the pose shown.
                    commit.AnimFrom = MatchEntry(assets, person, a, c, 0f, l.Time);
                    commit.MotorFrom = l.Time;
                    commit.Matched = 2;
                }
            }
            // A wall's move flowing out of the same move before its end (Fade 5, the motor's clip begun again near its start): faded as a new one.
            bool again = l.Fade == 5 && a.Clip == c.Clip && ledge.Played == l.Clip && l.Age < 1.5f * Core.World.TickSeconds && a.Time > l.Time + 0.02f;
            if (a.Clip != c.Clip || again)
            {
                // A catch's clip (Fade 2) faded in by half its time to the hands' contact (its mean rate's), between two ticks and LedgeBlend; the brace begun
                // before a grab's end (Fade 3) over BraceBlend.
                float fade = l.Fade switch
                {
                    0 => 0f,
                    2 when !float.IsNaN(c.ContactS) && l.Rate > 0f => Math.Clamp(0.5f * (c.ContactS - l.Time) / l.Rate, 2f * Core.World.TickSeconds, BodyMotor.LedgeBlend),
                    3 => BodyMotor.BraceBlend,
                    4 => BodyMotor.FlowFade,   // a pull-up going on
                    5 => Math.Clamp(l.AcrossTo - l.AcrossFrom, 2f * Core.World.TickSeconds, 0.5f),   // the wall's flow, over its blend
                    BodyMotor.SideFade => BodyMotor.SideBlend,   // a side-step's loop and its idle, a squeeze's turn into them
                    _ => BodyMotor.LedgeBlend,
                };
                // The standing grab from the stand: none (its first frame is the stand; faded, the stand's feet would go down with the clip's capsule, which
                // follows its pelvis into the dip, up to 3 cm into the floor).
                if (c.Role == LedgeRole.GrabStand && a.Role is MotionRole.Idle or MotionRole.Turn) fade = 0f;
                // A getting-onto a wall from the stand likewise (its first frame is the stand's), from its first frame.
                if (c.Role == LedgeRole.WallOn && l.Time < 1e-4f && a.Role is MotionRole.Idle or MotionRole.Turn) fade = 0f;
                // A falling grab: faded in by the hands' contact when that comes sooner (a late entry, a fast fall), as the motor takes the capsule's difference out.
                if (c.Role == LedgeRole.GrabFall && !float.IsNaN(c.ContactS) && l.Fade != 2)
                {
                    float by = (c.ContactS - l.Time) / MathF.Max(1f, l.Rate);
                    fade = by < 2f * Core.World.TickSeconds ? 2f * Core.World.TickSeconds : MathF.Min(fade, by);
                }
                // Out of the run at the running grab's own phase: no fade (its first frames are the run's there).
                if (c.Role == LedgeRole.GrabRun && !float.IsNaN(c.EnterPhase) && a.Role == MotionRole.Run && a.Clip >= 0 && assets.Clip(a.Clip).Duration > 0f)
                {
                    float p = a.Time / assets.Clip(a.Clip).Duration - c.EnterPhase;
                    p -= MathF.Round(p);
                    if (MathF.Abs(p) <= 0.04f) fade = 0f;
                }
                if (again)
                {
                    // The same clip begun again: faded from where it was (it goes on there at its own rate).
                    a.PreviousClip = c.Clip;
                    a.PreviousTime = a.Time;
                    a.Blend = 0f;
                    air.Fade = fade;
                    a.Clip = c.Clip;
                    a.Role = role;
                    a.Time = l.Time;
                    a.RateScale = 1f;
                }
                else PeopleAir.Begin(ref a, ref air, c.Clip, role, l.Time, fade);
                // A catch's fade from the jump's or the fall's clip holds that clip where it was.
                if (l.Fade is 2 or 4 && a.PreviousClip >= 0) a.PreviousRate = 0f;   // and a pull-up's going on from the grab or the hang
                // The wall's flow: the clip faded from goes on at its rate (the motor's, in the ledge state's Rate).
                if (l.Fade == 5 && a.PreviousClip >= 0) a.PreviousRate = l.Rate > 0f ? l.Rate : 1f;
            }
            a.Role = role;
            a.Rate = 0f;
            a.Time = l.Time;
            // A catch entered at its matching phase: its time from there to the contact as the motor's goes to it, the motor's own after.
            if (commitOn && commit.Matched == 2)
            {
                // A pull-up entered at its matching phase: its time brought onto the motor's over FlowConverge of it.
                float to = commit.MotorFrom + BodyMotor.FlowConverge;
                if (c.Role == LedgeRole.PullUp && l.Time < to) a.Time = commit.AnimFrom + (l.Time - commit.MotorFrom) * (to - commit.AnimFrom) / (to - commit.MotorFrom);
                else commit.Matched = 0;
            }
            else if (commitOn && commit.Matched != 0)
            {
                if (l.Time < c.ContactS && c.ContactS - commit.MotorFrom > 1e-4f)
                    a.Time = commit.AnimFrom + (l.Time - commit.MotorFrom) * (c.ContactS - commit.AnimFrom) / (c.ContactS - commit.MotorFrom);
                else commit.Matched = 0;
            }
            air.Mode = AirMode.None;   // The earlier build's air has nothing to do on a ledge
            ledge.Played = l.Clip;
            return true;
        }
        if (ledge.Played > 0) ledge.Played = 0;   // a pull-up ended (the gaits, faded in) or a drop (the air)
        if (l.Pending != 0 && l.Wait < -0.5f && paths.Of(LedgeRole.GrabRun) is { } run)
        {
            // A press held running at a lip: how long, and the run's pace to its footfall then (as before the running vault).
            (l.Wait, float pace) = PeopleOver.FootfallWait(assets, a, run.EnterPhase, run.OwnFace, l.Face, l.Speed);
            if (pace != 1f) { a.RateScale = pace; ledge.Paced = 1; }
        }
        else if (ledge.Paced == 1 && l.Pending == 0)
        {
            if (a.Role == MotionRole.Run) a.RateScale = 1f;   // the press let go without a grab: the run's own pace again
            ledge.Paced = 0;
        }
        return false;
    }
}
