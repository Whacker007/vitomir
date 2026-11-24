// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours, on what play showed (the movement on inclines and stairs was not smoothed yet:
// it was jerky), the models repository (the slope clips, made in the capsule's frame) and its stair clips to come.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Animation.People;

/// <summary>
/// How a person is drawn on ground that is not flat — the numbers (<b>SANDBOX</b>, ours, measured on the proving ground's stairs and
/// slopes). Off altogether (<see cref="Off"/>: <c>--stair-line-off</c> and <c>--slope-clips-off</c>) a person is drawn as before and the world
/// has no <see cref="PersonGround"/> store. Only the models repository's people have any of it; the capsule's collide-and-slide is never changed.
/// </summary>
public sealed record PersonGroundSettings
{
    /// <summary>
    /// <b>Stairs</b>: the drawn body and the camera ride a smoothed line (<see cref="PersonGround.Line"/>) rather than each riser the
    /// capsule steps up or down in a tick, and the feet's heights are kept where they were in the world when the capsule steps.
    /// <c>--stair-line-off</c> turns it off.
    /// </summary>
    public bool Stairs { get; init; } = true;

    /// <summary>
    /// <b>Slopes</b>: the models repository's slope clips (<c>walk_uphill</c> …) blended over the flat walk and run by the ground's grade
    /// along the way (<see cref="FullGrade"/>), the feet's IK taking the rest from the clip's own slope. <c>--slope-clips-off</c> turns it off.
    /// </summary>
    public bool Slopes { get; init; } = true;

    /// <summary>
    /// The line's half-life (s; 0.1): the drawn height follows the ground averaged under the body (<see cref="Window"/>) by a critically
    /// damped spring this quick, led by the ground's rise along the way (no lag on a steady flight). Measured on the proving ground's 0.20 m
    /// stair walked down, the pelvis's largest acceleration 51 m/s² at 0.07 s, 25 at 0.1, 19 at 0.14 (with the hips' 0.1).
    /// <c>stairsHalfLife</c>, <c>--stairs-half-life</c>.
    /// </summary>
    public float LineHalfLife { get; init; } = 0.1f;

    /// <summary>
    /// How long a stretch of ground along the way is averaged for the line (m; 0.6, two treads of a common stair): the ground under the body's
    /// middle looked for at <see cref="Samples"/> points over it, ahead and behind. It shrinks with the speed below <see cref="WindowSpeed"/>,
    /// so a body standing is drawn on its capsule's ground. <c>stairsWindow</c>, <c>--stairs-window</c>.
    /// </summary>
    public float Window { get; init; } = 0.6f;

    /// <summary>How many points of ground the window looks at (8).</summary>
    public int Samples { get; init; } = 8;

    /// <summary>The speed from which the window is whole (m/s; 1.0): below it the window is that share of <see cref="Window"/>.</summary>
    public float WindowSpeed { get; init; } = 1.0f;

    /// <summary>
    /// The hips' drop's half-life (s; 0.1): the hips lowered so a foot planted below the line reaches its ground, by a critically damped
    /// spring (eased at 1.5 m/s, a jolt at each start and stop).
    /// </summary>
    public float HipHalfLife { get; init; } = 0.1f;

    /// <summary>
    /// The grade along the way at which the slope clip is the whole of the gait (radians; 15°, the grade the clips were made on): 0 at flat,
    /// linearly between. Steeper ground keeps the whole slope clip, the feet's IK taking the rest.
    /// </summary>
    public float FullGrade { get; init; } = 15f * DetMath.DegToRad;

    /// <summary>
    /// How fast a foot's height follows the ground under it with the stairs' rule (m/s; 6): a foot in its stride's swing rises
    /// over a nosing within a tick or two at a run. A planted foot does not move (its ground is kept in the world).
    /// </summary>
    public float FootLiftSpeed { get; init; } = 6f;

    /// <summary>
    /// How far ahead along its own path a foot's ball is looked under as well (s of the motion; 0.06) with the stairs' rule: a foot in its
    /// swing is lifted onto the tread it is coming to rather than through its riser.
    /// </summary>
    public float FootAhead { get; init; } = 0.06f;

    /// <summary>The grade's half-life (s; 0.1): the ground's grade under the body smoothed so the blend does not flicker on a mesh's facets.</summary>
    public float GradeHalfLife { get; init; } = 0.1f;

    /// <summary>
    /// <b>the stair clips</b> (the models repository: <c>walk_stairs_up</c> …): on a flight the walk and the run take the stair clip
    /// for their way (<see cref="PersonStairs"/>), its share eased in and out at the flight's ends (<see cref="ClipHalfLife"/>), played at the rate
    /// the flight's tread gives and kept in step with the treads (<see cref="LockSeconds"/>), the drawn body on the line through the nosings, the
    /// feet's IK taking only what the flight differs from the clip's. Needs <see cref="Stairs"/> and a set with the clips. Off: the earlier build's (the
    /// flat gait with the IK). A tape's note <c>ground</c> carries it (<c>clips=1</c>); a note without it is the earlier build's. <c>--stair-clips-off</c>.
    /// </summary>
    public bool StairClips { get; init; } = true;

    /// <summary>
    /// The stair clips' share's half-life (s; 0.1, at the walk's 1.25 m/s and slower; faster, shorter by the speed, so it comes in over the same
    /// treads): a critically damped spring toward 1 on a flight and 0 off it, so neither end pops.
    /// </summary>
    public float ClipHalfLife { get; init; } = 0.1f;

    /// <summary>
    /// How quickly the stride is brought into step with the flight's treads (s; 0.1): the clip's time nudged so its feet's treads are the
    /// flight's, at most <see cref="LockMost"/> of its rate.
    /// </summary>
    public float LockSeconds { get; init; } = 0.1f;

    /// <summary>The most the stride's time is nudged to keep in step, as a share of its rate (0.35).</summary>
    public float LockMost { get; init; } = 0.35f;

    /// <summary>
    /// The risers the stair clips are played on whole (m; 0.12 to 0.28 — the clips were made on 0.20), none below <see cref="LeastRiser"/> or
    /// above <see cref="MostRiser"/> (the flat gait with the IK, as before: a 0.40 m riser is a climb, not a stair); between, eased.
    /// </summary>
    public float LowRiser { get; init; } = 0.12f;
    public float HighRiser { get; init; } = 0.28f;
    public float LeastRiser { get; init; } = 0.08f;
    public float MostRiser { get; init; } = 0.34f;

    /// <summary>The treads the stair clips are played on (m; whole 0.18 to 0.40, the clips' 0.24; none from 0.50: a terrace).</summary>
    public float LeastTread { get; init; } = 0.14f;
    public float LowTread { get; init; } = 0.18f;
    public float HighTread { get; init; } = 0.40f;
    public float MostTread { get; init; } = 0.50f;

    /// <summary>The fastest the stair clips play (times their rate; 1.6: the player's 3.0 m/s run is 1.39 of the run's 2.16). A sprint up a flight plays the run's clip at this and its feet slide.</summary>
    public float StairMostRate { get; init; } = 1.6f;

    /// <summary>Whether anything here is on (else the world needs no <see cref="PersonGround"/> store).</summary>
    public bool Any => Stairs || Slopes;

    /// <summary>Whether the stair clips are on (they need the stairs' line).</summary>
    public bool Clips => Stairs && StairClips;

    /// <summary>The earlier build's people on stairs and slopes, exactly: the flat walk and run on every ground, the drawn body on its capsule's steps.</summary>
    public static PersonGroundSettings Off => new() { Stairs = false, Slopes = false, StairClips = false };

    /// <summary>These settings with the earlier build's stairs (no stair clips: the flat gait with the IK; <c>--stair-clips-off</c>).</summary>
    public PersonGroundSettings WithoutStairClips => this with { StairClips = false };

    /// <summary>
    /// The numbers as a tape's note (<c>ground</c>): <c>off</c> for the earlier build's, else <c>stairs=1 slopes=1 halfLife=0.1 window=0.6 samples=8
    /// windowSpeed=1 hips=0.1 full=15 grade=0.1 footLift=6 footAhead=0.06</c> (degrees for the grade). A tape without the note was made on the earlier build's (<see cref="WithNote"/>).
    /// </summary>
    public string Note()
    {
        if (!Any) return "off";
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        string note = string.Create(ic,
            $"stairs={(Stairs ? 1 : 0)} slopes={(Slopes ? 1 : 0)} halfLife={LineHalfLife:0.####} window={Window:0.####} samples={Samples} windowSpeed={WindowSpeed:0.####} hips={HipHalfLife:0.####} full={FullGrade * DetMath.RadToDeg:0.####} grade={GradeHalfLife:0.####} footLift={FootLiftSpeed:0.####} footAhead={FootAhead:0.####}");
        // The stair clips, only when on (a note without them is the earlier build's).
        if (Clips)
            note += string.Create(ic,
                $" clips=1 clipHalfLife={ClipHalfLife:0.####} lock={LockSeconds:0.####} lockMost={LockMost:0.####} risers={LeastRiser:0.####},{LowRiser:0.####},{HighRiser:0.####},{MostRiser:0.####} treads={LeastTread:0.####},{LowTread:0.####},{HighTread:0.####},{MostTread:0.####} stairRate={StairMostRate:0.####}");
        return note;
    }

    /// <summary>
    /// These settings with a tape's note <c>ground</c> (<see cref="Note"/>) laid on; no note (an older tape) or <c>off</c> is
    /// <see cref="Off"/>. A number the note lacks keeps this one's; the numbers go through the note's text, so a replay's are the recording's.
    /// </summary>
    public PersonGroundSettings WithNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note) || !note.Contains('=')) return Off;
        var s = this with { StairClips = false };   // a note without clips=1 is the earlier build's
        foreach (var part in note.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq > 0 && part[(eq + 1)..].Contains(','))
            {
                // The ranges (four numbers).
                var nums = part[(eq + 1)..].Split(',').Select(t => float.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : float.NaN).ToArray();
                if (nums.Length == 4 && nums.All(float.IsFinite))
                    s = part[..eq] switch
                    {
                        "risers" => s with { LeastRiser = nums[0], LowRiser = nums[1], HighRiser = nums[2], MostRiser = nums[3] },
                        "treads" => s with { LeastTread = nums[0], LowTread = nums[1], HighTread = nums[2], MostTread = nums[3] },
                        _ => s,
                    };
                continue;
            }
            if (eq <= 0 || !float.TryParse(part[(eq + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) continue;
            s = part[..eq] switch
            {
                "stairs" => s with { Stairs = v != 0f }, "slopes" => s with { Slopes = v != 0f }, "halfLife" => s with { LineHalfLife = v },
                "window" => s with { Window = v }, "samples" => s with { Samples = (int)v }, "windowSpeed" => s with { WindowSpeed = v },
                "hips" => s with { HipHalfLife = v }, "full" => s with { FullGrade = v * DetMath.DegToRad }, "grade" => s with { GradeHalfLife = v },
                "footLift" => s with { FootLiftSpeed = v }, "footAhead" => s with { FootAhead = v },
                "clips" => s with { StairClips = v != 0f }, "clipHalfLife" => s with { ClipHalfLife = v }, "lock" => s with { LockSeconds = v },
                "lockMost" => s with { LockMost = v }, "stairRate" => s with { StairMostRate = v },
                _ => s,
            };
        }
        return s;
    }
}

/// <summary>
/// <b>a person on uneven ground</b> — the simulation's state (it moves the drawn pose, so the hit volumes; digested), moved on by
/// <see cref="PersonGroundSystem"/> before the animation and read by the animation (the slope clip's share for the stride), the rig (the
/// line, the hips, the feet) and the camera. A world has the store only when it has people and <see cref="PersonGroundSettings.Any"/>, so
/// 1.1's worlds (the arena) and the earlier build's people digest as before.
/// </summary>
public struct PersonGround
{
    /// <summary>
    /// <b>The line</b>: how far the drawn body stands above the capsule's feet (m; negative below), and how fast it changes (m/s): a critically
    /// damped spring toward the ground averaged along the way under the body, kept where it was in the world when the capsule steps a riser.
    /// </summary>
    public float Line, LineRate;

    /// <summary>How fast the hips' drop (<see cref="PoseRig.HipDrop"/>, below the line) changes (m/s): its spring's rate.</summary>
    public float HipRate;

    /// <summary>The ground's grade along the way (radians, uphill positive), smoothed, and its rate (rad/s).</summary>
    public float Grade, GradeRate;

    /// <summary>
    /// The slope clips' share of a gait going ahead (−1 to 1: positive the uphill clip, negative the downhill; 0 flat): the grade over
    /// <see cref="PersonGroundSettings.FullGrade"/>, held to ±1. A gait's own way scales it (<see cref="PersonSlopes.Forward"/>).
    /// </summary>
    public float Slope;

    /// <summary>Where the capsule's feet were after the last tick (m), and whether that tick found the body on the ground.</summary>
    public Vector3 LastFeet;

    int flags;
    const int SeenBit = 1, GroundedBit = 2, StairsBit = 4;

    /// <summary>Whether the body has been seen (its <see cref="LastFeet"/> are good).</summary>
    public bool Seen { readonly get => (flags & SeenBit) != 0; set => flags = value ? flags | SeenBit : flags & ~SeenBit; }

    /// <summary>Whether the body was on the ground (not in a move) last tick.</summary>
    public bool WasGrounded { readonly get => (flags & GroundedBit) != 0; set => flags = value ? flags | GroundedBit : flags & ~GroundedBit; }

    /// <summary>Whether the line is on for this body (the stairs' rule): the rig draws the body on it, the camera follows it.</summary>
    public bool Stairs { readonly get => (flags & StairsBit) != 0; set => flags = value ? flags | StairsBit : flags & ~StairsBit; }
}

/// <summary>
/// A person's slope clips (the models repository): for the walk and the run, the uphill and the downhill clip ahead (their places
/// in the kind's clips, −1 for none), made on <see cref="Grade"/> in the capsule's frame. Blended over a gait's blend (<see cref="GaitBlendClip"/>)
/// at the same share of the cycle by <see cref="Weight"/>; the stride by the same share.
/// </summary>
public sealed class PersonSlopes
{
    /// <summary>The clips' names in the models repository's set (<c>gait.SLOPES</c>).</summary>
    public const string WalkUpName = "walk_uphill", WalkDownName = "walk_downhill", RunUpName = "run_uphill", RunDownName = "run_downhill";

    /// <summary>A gait going this near ahead (degrees either side) takes the whole share; from <see cref="NoneFrom"/> none (sideways and back: the flat gait, the IK).</summary>
    public const float WholeWithin = 30f, NoneFrom = 75f;

    public required int WalkUp { get; init; }
    public required int WalkDown { get; init; }
    public required int RunUp { get; init; }
    public required int RunDown { get; init; }

    /// <summary>The grade the clips were made on (radians, positive; their notes' <c>grade_deg</c>), and its tangent.</summary>
    public required float Grade { get; init; }
    public float Tan => DetMath.Tan(Grade);

    /// <summary>Whether the set has any (all four, in practice).</summary>
    public bool Any => WalkUp >= 0 || WalkDown >= 0 || RunUp >= 0 || RunDown >= 0;

    /// <summary>The slope clips by name from a set (null when it has none: an older models repository).</summary>
    public static PersonSlopes? From(Func<string, INotedClip?> named, Func<INotedClip, int> indexOf)
    {
        int Of(string name) => named(name) is { Loops: true, Notes.Ground.Slope: true } c ? indexOf(c) : -1;
        float grade = named(WalkUpName)?.Notes.Ground?.GradeDeg ?? named(RunUpName)?.Notes.Ground?.GradeDeg ?? 15f;
        var s = new PersonSlopes { WalkUp = Of(WalkUpName), WalkDown = Of(WalkDownName), RunUp = Of(RunUpName), RunDown = Of(RunDownName), Grade = MathF.Abs(grade) * DetMath.DegToRad };
        return s.Any ? s : null;
    }

    /// <summary>
    /// How much a gait's way takes the slope clip (0 to 1): 1 within <see cref="WholeWithin"/>° of ahead, none from <see cref="NoneFrom"/>°,
    /// smoothly between. A way is the gait's blend's degree (0 to 359, to the body's left); an alternate (near the backpedal's line) none.
    /// </summary>
    public static float Forward(int degree)
    {
        if (degree < 0 || degree >= Gait.Bins) return 0f;
        float a = degree > 180 ? 360 - degree : degree;
        if (a <= WholeWithin) return 1f;
        if (a >= NoneFrom) return 0f;
        float t = (NoneFrom - a) / (NoneFrom - WholeWithin);
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// The slope clip for <paramref name="clip"/> (one of a person's gait blends, walk or run) at the share <paramref name="slope"/>
    /// (<see cref="PersonGround.Slope"/>), and how much of it: −1 and 0 for any other clip (the sprint, the crouched walk, a stand-in, a
    /// townsperson's styled walk), flat ground, or a set without the clip.
    /// </summary>
    public (int Clip, float Weight) For(PersonMotion person, int clip, float slope)
    {
        if (slope == 0f || clip < 0) return (-1, 0f);
        bool up = slope > 0f;
        int slopeClip;
        Gait? gait;
        if (person.Walk is { } walk && walk.Holds(clip)) (gait, slopeClip) = (walk, up ? WalkUp : WalkDown);
        else if (person.Run is { } run && run.Holds(clip)) (gait, slopeClip) = (run, up ? RunUp : RunDown);
        else return (-1, 0f);
        if (slopeClip < 0) return (-1, 0f);
        float w = MathF.Min(1f, MathF.Abs(slope)) * Forward(clip - gait.First);
        return w > 0f ? (slopeClip, w) : (-1, 0f);
    }

    /// <summary>
    /// The ground the pose's own slope has under a point <paramref name="forward"/> metres ahead of the feet along the legs (m): the clip's
    /// plane (it rises <see cref="Tan"/> a metre ahead going up) by the share it has of the pose; the feet's IK lays the rest on.
    /// </summary>
    public float GroundAt(float forward, float signedWeight) => forward * Tan * signedWeight;

    /// <summary>
    /// The ground speed the gait blend <paramref name="blend"/> is drawn for with its slope clip <paramref name="slopeClip"/> at
    /// <paramref name="weight"/> laid over it (m/s): the two strides (speed × cycle) blended, over the blend's own cycle — so the blend's time
    /// keeps going round at the rate that keeps the feet on the ground (<see cref="AnimationSystem"/>).
    /// </summary>
    public static float NaturalSpeed(GaitBlendClip blend, INotedClip slopeClip, float weight)
    {
        float d = blend.Duration;
        if (weight <= 0f || d <= 0f) return blend.NaturalSpeed;
        float flat = blend.NaturalSpeed * d, slope = slopeClip.Notes.GroundSpeed * slopeClip.Duration;
        return (flat + (slope - flat) * MathF.Min(1f, weight)) / d;
    }

    /// <summary>
    /// Lays a person's slope clip over a gait's pose (<paramref name="local"/>, sampled from <paramref name="clip"/> at <paramref name="seconds"/>):
    /// the slope clip sampled at the same share of its cycle, each bone blended by its share (<see cref="PoseEvaluator.Crossfade"/>). Nothing for
    /// any other clip or flat ground. Allocates nothing (the slope's pose on the stack).
    /// </summary>
    public static void Lay(CharacterAssets assets, PersonMotion person, int clip, float seconds, float slope, Skeleton skeleton, Span<BoneTransform> local)
    {
        if (person.Slopes is not { } slopes) return;
        var (slopeClip, w) = slopes.For(person, clip, slope);
        if (slopeClip < 0) return;
        var made = assets.Clip(clip);
        var on = assets.Clip(slopeClip);
        float d = made.Duration, p = d > 0f ? seconds / d : 0f;
        p -= MathF.Floor(p);
        int n = Math.Min(local.Length, skeleton.Count);
        Span<BoneTransform> other = n <= 512 ? stackalloc BoneTransform[n] : new BoneTransform[n];
        on.Sample(p * on.Duration, skeleton, other);
        PoseEvaluator.Crossfade(local[..n], other, w, local[..n]);
    }
}

/// <summary>
/// <b>a person on a flight of stairs</b> (the models repository's stair clips; <see cref="PersonStairs"/>) — the simulation's state (it
/// moves the drawn pose; digested), moved on by <see cref="PersonGroundSystem"/> with the ground and read by the animation (the clip's share, its
/// rate and its step), the rig (the feet) and the pose. A world has the store only when its people have the stair clips and they are on
/// (<see cref="PersonGroundSettings.Clips"/>), so the people and the earlier build's digest as before.
/// </summary>
public struct PersonFlight
{
    /// <summary>The stair clips' share of a gait going ahead (0 to 1), and its spring's rate (1/s): toward 1 on a flight and 0 off it.</summary>
    public float Share, ShareRate;

    /// <summary>The flight's riser (m, its rise from tread to tread) and tread (m, nosing to nosing along the way), as last measured.</summary>
    public float Riser, Tread;

    /// <summary>Where the capsule's feet are along a tread: the share of a tread they are past a nosing (0 to 1), as the clips' own steps count it.</summary>
    public float Phase;

    /// <summary>The line through the nosings above the capsule's feet (m), where the drawn body stands on a flight, and how much it rises a metre along the way.</summary>
    public float Nosings, Rise;

    /// <summary>Which way the flight goes: 1 up, −1 down (kept while the share eases out), 0 none yet.</summary>
    public int Way;

    /// <summary>Whether the flight was found under the body this tick (risers and treads within the clips' ranges, the capsule on it).</summary>
    public int Found;
}

/// <summary>
/// <b>the models repository's stair clips</b> (0.20 m risers on 0.24 m treads, <c>walk_stairs_up</c>/<c>_down</c> at 1.25 m/s
/// two treads a step, <c>run_stairs_up</c>/<c>_down</c> at 2.16 m/s three treads a step; each made in its capsule's frame on the line through the
/// nosings, its notes' <c>ground</c> giving the riser, tread and <c>nosing_offset_m</c>), found by name and kept on the person
/// (<see cref="PersonMotion.Stairs"/>). An earlier build plays them: laid over the walk's or the run's blend going ahead by the flight's share
/// (<see cref="PersonFlight.Share"/>) at the same share of the cycle, as the slope clips are; the stride's rate by the flight's tread; the step kept
/// on the flight's treads (<see cref="Lock"/>); the feet's IK taking only the difference between the flight and the clip's own treads
/// (<see cref="ClipGround(int, float, float)"/>).
/// </summary>
public sealed class PersonStairs
{
    public const string WalkUpName = "walk_stairs_up", WalkDownName = "walk_stairs_down", RunUpName = "run_stairs_up", RunDownName = "run_stairs_down";

    public required int WalkUp { get; init; }
    public required int WalkDown { get; init; }
    public required int RunUp { get; init; }
    public required int RunDown { get; init; }

    /// <summary>Each clip's ground (its notes'), speed and cycle, by its place (WalkUp, WalkDown, RunUp, RunDown; null for one the set lacks).</summary>
    public required (ClipGround Ground, float Speed, float Duration)?[] Made { get; init; }

    /// <summary>The stair clips by name from a set (null when it has none).</summary>
    public static PersonStairs? From(Func<string, INotedClip?> named, Func<INotedClip, int> indexOf)
    {
        INotedClip? Clip(string name) => named(name) is { Loops: true, Notes.Ground.Stairs: true } c ? c : null;
        var clips = new[] { Clip(WalkUpName), Clip(WalkDownName), Clip(RunUpName), Clip(RunDownName) };
        int Of(INotedClip? c) => c is null ? -1 : indexOf(c);
        var s = new PersonStairs
        {
            WalkUp = Of(clips[0]), WalkDown = Of(clips[1]), RunUp = Of(clips[2]), RunDown = Of(clips[3]),
            Made = [.. clips.Select(c => c is { Notes.Ground: { } g } && g.TreadM > 0f && c.Notes.GroundSpeed > 0f ? ((ClipGround, float, float)?)(g, c.Notes.GroundSpeed, c.Duration) : null)],
        };
        return s.WalkUp >= 0 || s.WalkDown >= 0 || s.RunUp >= 0 || s.RunDown >= 0 ? s : null;
    }

    /// <summary>The clips' places in order (WalkUp, WalkDown, RunUp, RunDown).</summary>
    public int[] All => [WalkUp, WalkDown, RunUp, RunDown];

    /// <summary>
    /// The stair clip for <paramref name="clip"/> (one of a person's gait blends, walk or run) on a flight going <paramref name="way"/> (1 up,
    /// −1 down) at the share <paramref name="share"/>, its slot (0 to 3, for <see cref="Made"/>), and how much of it: the share by how near ahead the
    /// gait goes (<see cref="PersonSlopes.Forward"/>: sideways and back none, the flat gait with the IK). −1 and 0 for any other clip (the sprint, the
    /// crouched walk, a townsperson's styled walk), off a flight, or a set without it.
    /// </summary>
    public (int Clip, int Slot, float Weight) For(PersonMotion person, int clip, int way, float share)
    {
        if (share <= 0f || clip < 0 || way == 0) return (-1, -1, 0f);
        Gait? gait;
        int slot;
        if (person.Walk is { } walk && walk.Holds(clip)) (gait, slot) = (walk, way > 0 ? 0 : 1);
        else if (person.Run is { } run && run.Holds(clip)) (gait, slot) = (run, way > 0 ? 2 : 3);
        else return (-1, -1, 0f);
        int stair = All[slot];
        if (stair < 0 || Made[slot] is null) return (-1, -1, 0f);
        float w = MathF.Min(1f, share) * PersonSlopes.Forward(clip - gait.First);
        return w > 0f ? (stair, slot, w) : (-1, -1, 0f);
    }

    /// <summary>The share of a flight of risers <paramref name="riser"/> on treads <paramref name="tread"/> (m) the clips take (0 to 1; <see cref="PersonGroundSettings.LowRiser"/> …).</summary>
    public static float Fit(PersonGroundSettings s, float riser, float tread) =>
        Band(riser, s.LeastRiser, s.LowRiser, s.HighRiser, s.MostRiser) * Band(tread, s.LeastTread, s.LowTread, s.HighTread, s.MostTread);

    static float Band(float v, float least, float low, float high, float most)
    {
        if (v <= least || v >= most) return 0f;
        if (v >= low && v <= high) return 1f;
        float t = v < low ? (v - least) / MathF.Max(1e-6f, low - least) : (most - v) / MathF.Max(1e-6f, most - high);
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// The ground speed the gait blend <paramref name="blend"/> is drawn for with the stair clip of <paramref name="slot"/> laid over it by
    /// <paramref name="weight"/> on a flight of <paramref name="tread"/> (m), for a body drawn <paramref name="scale"/> its base's size: the stair clip's
    /// stride counted on the flight's treads (its speed × <paramref name="tread"/> / its own tread: played at (speed / its speed) × (its tread / the
    /// flight's), the models repository's rule), blended with the flat stride over the blend's cycle (<see cref="PersonSlopes.NaturalSpeed"/>'s way).
    /// </summary>
    public float NaturalSpeed(GaitBlendClip blend, float flatSpeed, int slot, float weight, float tread, float scale)
    {
        float d = blend.Duration;
        if (weight <= 0f || d <= 0f || Made[slot] is not { } m || tread <= 0f) return flatSpeed;
        float stairs = m.Speed * tread / (m.Ground.TreadM * MathF.Max(1e-3f, scale)) * m.Duration;
        float flat = flatSpeed * d;
        return (flat + (stairs - flat) * MathF.Min(1f, weight)) / d;
    }

    /// <summary>
    /// <b>the step kept on the treads</b>: how far (as a share of the cycle, within half a tread's either way) the stair clip of
    /// <paramref name="slot"/> at the share <paramref name="share"/> of its cycle is from having its own capsule as far past a nosing as the body's is
    /// on the flight (<paramref name="phase"/>, a share of a tread): its capsule is <c>nosing_offset_m</c> past one at its start and goes
    /// <c>treads_a_step</c> × 2 treads a cycle.
    /// </summary>
    public float Lock(int slot, float share, float phase)
    {
        if (Made[slot] is not { } m) return 0f;
        float n = m.Speed * m.Duration / m.Ground.TreadM;   // treads a cycle: 4 walking, 6 running
        if (n < 0.5f) return 0f;
        float own = m.Ground.NosingOffsetM / m.Ground.TreadM + share * n;
        float e = phase - (own - MathF.Floor(own));
        e -= MathF.Round(e);
        return e / n;
    }

    /// <summary>
    /// The clip of <paramref name="slot"/>'s own tread under a point <paramref name="forward"/> metres ahead of its capsule (model space, the
    /// base's size) at <paramref name="seconds"/> into it, above its capsule (m): its capsule rides the line through the nosings, <c>nosing_offset_m</c>
    /// past one at its start; going up a tread is as high as the nosing behind it, going down as the nosing ahead of it.
    /// </summary>
    public float ClipGround(int slot, float seconds, float forward)
    {
        if (Made[slot] is not { } m) return 0f;
        var g = m.Ground;
        float t = g.TreadM, r = g.RiserM;
        float past = g.NosingOffsetM + m.Speed * seconds, at = past + forward;
        return g.Up ? r * MathF.Floor(at / t) - r * past / t : -r * MathF.Ceiling(at / t) + r * past / t;
    }

    /// <summary>
    /// Lays the person's stair clip over a gait's pose (<paramref name="local"/>, sampled from <paramref name="clip"/> at <paramref name="seconds"/>)
    /// on its flight (<paramref name="flight"/>): the stair clip at the same share of its cycle, each bone blended by its share. Nothing for any other
    /// clip or off a flight. Allocates nothing.
    /// </summary>
    public static void Lay(CharacterAssets assets, PersonMotion person, int clip, float seconds, in PersonFlight flight, Skeleton skeleton, Span<BoneTransform> local)
    {
        if (person.Stairs is not { } stairs) return;
        var (stair, _, w) = stairs.For(person, clip, flight.Way, flight.Share);
        if (stair < 0) return;
        var made = assets.Clip(clip);
        var on = assets.Clip(stair);
        float d = made.Duration, p = d > 0f ? seconds / d : 0f;
        p -= MathF.Floor(p);
        int n = Math.Min(local.Length, skeleton.Count);
        Span<BoneTransform> other = n <= 512 ? stackalloc BoneTransform[n] : new BoneTransform[n];
        on.Sample(p * on.Duration, skeleton, other);
        PoseEvaluator.Crossfade(local[..n], other, w, local[..n]);
    }
}

/// <summary>
/// Moves every person's <see cref="PersonGround"/> on by one tick, after the bodies have moved and before the animation chooses
/// (<see cref="AnimationRegistration.AnimationOrder"/> + <see cref="Order"/>), for each character with a body whose kind is a person:
/// <list type="number">
/// <item><b>The capsule's step kept out of the drawing</b> (the stairs' rule): a tick on the ground whose capsule rose or fell (a riser, at
/// most a step) takes that rise out of the line and out of the rig's feet's eased lifts, so the drawn body and its feet stay where they were in
/// the world and then ease (the body and its planted foot popped up a riser in a tick and sank back).</item>
/// <item><b>The line</b>: the ground under the body's middle looked for at <see cref="PersonGroundSettings.Samples"/> points along its way over
/// <see cref="PersonGroundSettings.Window"/> (shrunk with the speed; points further than a step from the capsule's feet, a wall's top or a
/// ledge's foot, read as the feet's own), averaged — on a stair, a line rising as the stair does — and the line follows it by a critically
/// damped spring. On a slope or flat ground it is the capsule's own ground. Off the ground it goes back to the capsule; in a move it is 0.</item>
/// <item><b>The grade</b> along the way (the velocity's, the facing's when still) from the ground's normal under the capsule (a stair's treads
/// are level: stairs are no slope), smoothed by a spring; the slope clips' share from it.</item>
/// </list>
/// Deterministic (<see cref="DetMath"/>); allocates nothing.
/// </summary>
public sealed class PersonGroundSystem(CharacterAssets assets, PersonGroundSettings settings) : ISystem
{
    /// <summary>The name it is registered under, and its place before the animation (it runs first of the pose's systems).</summary>
    public const string Name = "platform.person-ground";
    public const int Order = -5;

    /// <summary>A rise or fall in a tick beyond this is not a step and is drawn as it is (m).</summary>
    public const float MostStep = 0.6f;

    /// <summary>A move across beyond this in a tick is a placing, not a walk, and clears the line (m).</summary>
    public const float Put = 96f / 72f;

    /// <summary>Points of ground further than this beyond the body's step from its feet are not ground it walks on (m).</summary>
    public const float Beyond = 0.05f;

    int bodiesKnown, locosKnown;

    public PersonGroundSettings Settings => settings;

    public void Run(World world)
    {
        if (!world.TryStore<PersonGround>(out var grounds)) return;
        world.TryStore<PersonFlight>(out var flights);   // only with the stair clips on
        var animations = world.Store<CharacterAnimation>();
        var inputs = world.Store<MotionInput>();
        var bodies = PoseRigSystem.Optional<Body>(world, ref bodiesKnown);
        var locos = PoseRigSystem.Optional<Locomotion>(world, ref locosKnown);
        var rigs = world.Store<PoseRig>();
        world.TryService<LevelSpace>(out var level);
        if (bodies is null) return;
        var ents = animations.Entities;
        for (int i = 0; i < ents.Length; i++)
        {
            var e = ents[i];
            if (assets.Kind(animations.Data[i].Kind).Person is null) continue;
            ref var body = ref bodies.Find(e);
            if (Unsafe.IsNullRef(ref body)) continue;
            if (!grounds.Has(e)) world.Set(e, new PersonGround());
            ref var g = ref grounds.Get(e);
            // A person with the stair clips keeps its flight.
            ref var flight = ref Unsafe.NullRef<PersonFlight>();
            if (flights is not null && assets.Kind(animations.Data[i].Kind).Person!.Stairs is not null)
            {
                if (!flights.Has(e)) world.Set(e, new PersonFlight());
                flight = ref flights.Get(e);
            }
            ref var input = ref inputs.Find(e);
            var motion = Unsafe.IsNullRef(ref input) ? MotionInput.Standing : input;
            ref var rig = ref rigs.Find(e);
            float step = 0.4f;
            if (locos is not null) { ref var loco = ref locos.Find(e); if (!Unsafe.IsNullRef(ref loco)) step = loco.StepHeight; }
            Step(ref g, ref rig, in body, in motion, step, level?.Collision, World.TickSeconds, ref flight);
        }
    }

    /// <summary>One person's ground for one tick (<paramref name="rig"/> may be a null reference: a body not yet posed).</summary>
    public void Step(ref PersonGround g, ref PoseRig rig, in Body body, in MotionInput input, float stepHeight, CollisionWorld? level, float seconds) =>
        Step(ref g, ref rig, body, input, stepHeight, level, seconds, ref Unsafe.NullRef<PersonFlight>());

    /// <summary>
    /// As the overload before, with the person's flight (<paramref name="flight"/>, a null reference for a person without the stair clips): the
    /// flight looked for along the way when the ground under the body steps, its share eased toward the clips' fit on it, and the line drawn
    /// toward the line through its nosings by that share.
    /// </summary>
    public void Step(ref PersonGround g, ref PoseRig rig, in Body body, in MotionInput input, float stepHeight, CollisionWorld? level, float seconds, ref PersonFlight flight)
    {
        bool flying = !Unsafe.IsNullRef(ref flight) && settings.Clips;
        var s = settings;
        bool grounded = input.Grounded && input.Traversal == TraversalKind.None && body.State == BodyState.Ground;
        bool moved = g.Seen && g.WasGrounded && grounded;
        g.Stairs = s.Stairs;
        if (s.Stairs)
        {
            float rise = body.Feet.Z - g.LastFeet.Z;
            float across = new Vector2(body.Feet.X - g.LastFeet.X, body.Feet.Y - g.LastFeet.Y).Length();
            if (moved && MathF.Abs(rise) <= MostStep && across <= Put)
            {
                // The capsule's step taken out of the drawing: the line and the feet stay where they were in the world.
                g.Line -= rise;
                if (!Unsafe.IsNullRef(ref rig)) { rig.LeftGround -= rise; rig.RightGround -= rise; }
            }
            else if (g.Seen && (MathF.Abs(rise) > MostStep || across > Put)) g.Line = g.LineRate = 0f;
            var (target, rate, stepped) = grounded && level is not null ? Average(level, body, stepHeight) : (0f, 0f, false);
            // On a flight, the line drawn toward the line through its nosings by the stair clips' share (they are made on it).
            if (flying)
            {
                OnFlight(ref flight, level, in body, grounded && input.Traversal == TraversalKind.None && (stepped || flight.Share > 0f || flight.Found != 0), seconds);
                if (flight.Share > 0f && flight.Found != 0)
                {
                    target += flight.Share * (flight.Nosings - target);
                    float speed = new Vector2(body.Velocity.X, body.Velocity.Y).Length();
                    rate += flight.Share * (flight.Rise * speed - rate);   // the line's own climb, not the averaged ground's
                }
            }
            if (input.Traversal != TraversalKind.None) { g.Line = g.LineRate = 0f; }
            else Follow(ref g.Line, ref g.LineRate, target, rate, s.LineHalfLife, seconds);
        }
        else { g.Line = g.LineRate = 0f; }
        if (flying && !s.Stairs) { flight.Share = flight.ShareRate = 0f; flight.Found = 0; }

        // The grade along the way, from the ground's normal under the capsule (a stair's treads are level).
        float grade = 0f;
        if (s.Slopes && grounded)
        {
            var v = new Vector2(body.Velocity.X, body.Velocity.Y);
            float speed = v.Length();
            Vector2 way;
            if (speed > 0.1f) way = v / speed;
            else { var (sy, cy) = DetMath.SinCos(body.Yaw); way = new Vector2(cy, sy); }
            var n = body.GroundNormal;
            if (n.Z > 0.5f) grade = DetMath.Atan(-(n.X * way.X + n.Y * way.Y) / n.Z);
        }
        if (s.Slopes) Damped(ref g.Grade, ref g.GradeRate, grade, s.GradeHalfLife, seconds);
        else g.Grade = g.GradeRate = 0f;
        g.Slope = s.Slopes && s.FullGrade > 0f ? Math.Clamp(g.Grade / s.FullGrade, -1f, 1f) : 0f;

        g.LastFeet = body.Feet;
        g.WasGrounded = grounded;
        g.Seen = true;
    }

    /// <summary>
    /// The ground under the body averaged along its way (m above its feet): <see cref="PersonGroundSettings.Samples"/> points over the window
    /// (the window that share of its length below <see cref="PersonGroundSettings.WindowSpeed"/>), each the ground straight down there,
    /// or the feet's own height where there is none within the body's step. 0 standing.
    /// </summary>
    (float Mean, float Rate, bool Stepped) Average(CollisionWorld level, in Body body, float stepHeight)
    {
        var s = settings;
        var v = new Vector2(body.Velocity.X, body.Velocity.Y);
        float speed = v.Length();
        float window = s.Window * MathF.Min(1f, speed / MathF.Max(1e-3f, s.WindowSpeed));
        int count = Math.Clamp(s.Samples, 2, 32);
        if (window < 0.05f || speed < 1e-3f) return (0f, 0f, false);
        var way = v / speed;
        float reach = stepHeight + Beyond, sum = 0f, moment = 0f, spread = 0f, last = float.NaN;
        bool stepped = false;   // whether two neighbouring points differ by a riser (a flight may be under the body)
        for (int k = 0; k < count; k++)
        {
            float along = -0.5f * window + (k + 0.5f) * window / count;
            var at = new Vector3(body.Feet.X + way.X * along, body.Feet.Y + way.Y * along, body.Feet.Z + reach);
            float rel = 0f;
            if (level.Ray(at, -Vector3.UnitZ, 2f * reach) is { } hit && hit.Normal.Z >= 0.6f)
            {
                rel = hit.Point.Z - body.Feet.Z;
                if (MathF.Abs(rel) > reach) rel = 0f;
            }
            if (!float.IsNaN(last) && MathF.Abs(rel - last) > LeastRiser) stepped = true;
            last = rel;
            sum += rel;
            moment += along * rel;
            spread += along * along;
        }
        // The ground's rise along the way over the window (a least-squares line through the points: on a stair its pitch, on a slope its
        // grade), by the speed: how fast the line rises in the world.
        return (sum / count, spread > 0f ? moment / spread * speed : 0f, stepped);
    }

    /// <summary>The least rise between two points of ground along the way that is a riser (m).</summary>
    public const float LeastRiser = 0.04f;

    /// <summary>How far along the way, each side of the feet, the flight is looked for (m), and how many points of ground it is looked at.</summary>
    public const float FlightReach = 0.72f;
    public const int FlightPoints = 25;

    /// <summary>
    /// One tick of a person's flight. When <paramref name="look"/> (on the ground and the ground stepping under the body, or a flight already
    /// in hand), the ground is looked at along the way (<see cref="Nosings"/>); a flight is two risers or more the same way, within the clips' ranges
    /// (<see cref="PersonStairs.Fit"/>), the capsule within a tread and a half of one of its nosings. The share eases toward the fit there (none off it,
    /// and none until the share has gone when the flight turns the other way) by a critically damped spring.
    /// </summary>
    void OnFlight(ref PersonFlight f, CollisionWorld? level, in Body body, bool look, float seconds)
    {
        var s = settings;
        float want = 0f;
        f.Found = 0;
        var v = new Vector2(body.Velocity.X, body.Velocity.Y);
        float speed = v.Length();
        // Moving on the ground with nothing in hand, a glance ahead (three points to a stride's length): a flight met is found before its first step.
        if (!look && level is not null && speed > 0.2f && body.State == BodyState.Ground && Ahead(level, body, v / speed)) look = true;
        if (look && level is not null && speed > 0.2f && Nosings(level, body, v / speed, out int way, out float riser, out float tread, out float phase, out float line, out float behind, out float ahead, out float rise))
        {
            f.Found = 1;
            (f.Riser, f.Tread, f.Phase, f.Nosings, f.Rise) = (riser, tread, phase, line, rise);
            if (f.Way == 0 || f.Share < 0.05f) f.Way = way;
            // On the flight whole (a nosing within a tread and a half behind and ahead). At its ends (the clips are made on a flight without end):
            // going up, whole from a tread and a half before its first nosing (the first step is onto its first tread); going down, eased in over
            // the three quarters of a tread before its first (the top landing runs on to its nosing: a foot lowered sooner would go into it);
            // either way eased out over the three quarters of a tread after its last.
            float on = behind <= 1.5f * tread && ahead <= 1.5f * tread ? 1f
                     : behind > 1.5f * tread ? (way > 0 ? (ahead <= 1.5f * tread ? 1f : 0f) : Math.Clamp(1f - ahead / (0.75f * tread), 0f, 1f))
                     : Math.Clamp(1f - behind / (0.75f * tread), 0f, 1f);
            if (way == f.Way) want = PersonStairs.Fit(s, riser, tread) * on;
        }
        Damped(ref f.Share, ref f.ShareRate, want, s.ClipHalfLife * MathF.Min(1f, 1.25f / MathF.Max(speed, 1e-3f)), seconds);

        if (f.Share < 1e-4f && want == 0f) { f.Share = f.ShareRate = 0f; if (f.Found == 0) f.Way = 0; }
        else if (f.Share > 1f) { f.Share = 1f; f.ShareRate = 0f; }
    }

    /// <summary>Whether the ground steps by a riser between the feet and <see cref="FlightReach"/> ahead along <paramref name="way"/> (three points).</summary>
    static bool Ahead(CollisionWorld level, in Body body, Vector2 way)
    {
        float last = float.NaN;
        for (int k = 0; k < 3; k++)
        {
            float along = k * 0.5f * FlightReach;
            var at = new Vector3(body.Feet.X + way.X * along, body.Feet.Y + way.Y * along, body.Feet.Z + 1f);
            float h = level.Ray(at, -Vector3.UnitZ, 2f) is { } hit && hit.Normal.Z >= 0.6f ? hit.Point.Z : float.NaN;
            if (!float.IsNaN(last) && !float.IsNaN(h) && MathF.Abs(h - last) > LeastRiser) return true;
            last = h;
        }
        return false;
    }

    /// <summary>
    /// The flight along <paramref name="way"/> (a unit vector) under a body: the ground looked at straight down at <see cref="FlightPoints"/>
    /// points over <see cref="FlightReach"/> each side of its feet, every step of more than <see cref="LeastRiser"/> between two neighbours a riser,
    /// its nosing (where the ground steps) found to a few millimetres between them. With two or more the same way as the nearest: which way it goes
    /// (1 up, −1 down), its riser and tread (their means), how far past a nosing the feet are (a share of a tread), the line through the nosings above
    /// the feet (least squares; held between the lowest tread and the highest), how far behind and ahead along the way the nearest nosings are
    /// (<see cref="float.MaxValue"/> for none) and how much the line rises a metre along the way. False without.
    /// </summary>
    public static bool Nosings(CollisionWorld level, in Body body, Vector2 way, out int dir, out float riser, out float tread, out float phase, out float line, out float behind,
                               out float ahead, out float rise)
    {
        (dir, riser, tread, phase, line, behind, ahead, rise) = (0, 0f, 0f, 0f, 0f, float.MaxValue, float.MaxValue, 0f);
        var feet = body.Feet;
        Span<float> h = stackalloc float[FlightPoints];
        float step = 2f * FlightReach / (FlightPoints - 1);
        float Ground(float along)
        {
            var at = new Vector3(feet.X + way.X * along, feet.Y + way.Y * along, feet.Z + 1f);
            return level.Ray(at, -Vector3.UnitZ, 2f) is { } hit && hit.Normal.Z >= 0.6f ? hit.Point.Z - feet.Z : float.NaN;
        }
        for (int k = 0; k < FlightPoints; k++) h[k] = Ground(-FlightReach + k * step);
        Span<float> xs = stackalloc float[FlightPoints];
        Span<float> zs = stackalloc float[FlightPoints];
        Span<float> rises = stackalloc float[FlightPoints];
        Span<int> signs = stackalloc int[FlightPoints];
        int count = 0;
        for (int k = 0; k + 1 < FlightPoints; k++)
        {
            float a = h[k], b = h[k + 1];
            if (float.IsNaN(a) || float.IsNaN(b) || MathF.Abs(b - a) <= LeastRiser || MathF.Abs(b - a) > 0.6f) continue;
            // The nosing: where the ground steps between them, by halving.
            float lo = -FlightReach + k * step, hi = lo + step;
            for (int i = 0; i < 6; i++)
            {
                float mid = 0.5f * (lo + hi), m = Ground(mid);
                if (float.IsNaN(m) || MathF.Abs(m - a) < MathF.Abs(m - b)) lo = mid; else hi = mid;
            }
            xs[count] = 0.5f * (lo + hi);
            zs[count] = MathF.Max(a, b);
            rises[count] = MathF.Abs(b - a);
            signs[count] = b > a ? 1 : -1;
            count++;
        }
        if (count < 2) return false;
        // The flight's way: the nearest riser's; the risers the other way are not its.
        int near = 0;
        for (int k = 1; k < count; k++) if (MathF.Abs(xs[k]) < MathF.Abs(xs[near])) near = k;
        dir = signs[near];
        int kept = 0;
        for (int k = 0; k < count; k++)
            if (signs[k] == dir) { xs[kept] = xs[k]; zs[kept] = zs[k]; rises[kept] = rises[k]; kept++; }
        if (kept < 2) return false;
        float sumX = 0f, sumZ = 0f, sumR = 0f, gaps = 0f, lowest = float.MaxValue, highest = float.MinValue;
        for (int k = 0; k < kept; k++)
        {
            sumX += xs[k]; sumZ += zs[k]; sumR += rises[k];
            lowest = MathF.Min(lowest, zs[k]); highest = MathF.Max(highest, zs[k]);
            if (k > 0) gaps += xs[k] - xs[k - 1];
        }
        riser = sumR / kept;
        tread = gaps / (kept - 1);
        if (tread < 0.05f) return false;
        float mx = sumX / kept, mz = sumZ / kept, sxx = 0f, sxz = 0f;
        for (int k = 0; k < kept; k++) { sxx += (xs[k] - mx) * (xs[k] - mx); sxz += (xs[k] - mx) * (zs[k] - mz); }
        float slope = sxx > 0f ? sxz / sxx : 0f;
        line = Math.Clamp(mz - slope * mx, lowest - riser, highest);
        rise = line > lowest - riser && line < highest ? slope : 0f;   // past its ends the line is held level
        // How far past a nosing the feet are, by the nearest kept one.
        near = 0;
        for (int k = 1; k < kept; k++) if (MathF.Abs(xs[k]) < MathF.Abs(xs[near])) near = k;
        for (int k = 0; k < kept; k++)
        {
            if (xs[k] <= 0f) behind = MathF.Min(behind, -xs[k]);
            else ahead = MathF.Min(ahead, xs[k]);
        }
        float past = -xs[near] / tread;
        phase = past - MathF.Floor(past);
        return true;
    }

    /// <summary>
    /// One tick of the line (<see cref="PersonGround.Line"/>): a critically damped spring of half-life <paramref name="halfLife"/> following a goal
    /// that moves at <paramref name="goalRate"/> (m/s, in the world) to <paramref name="goal"/> by the tick's end, so a stair or slope climbed at a
    /// steady pace is followed with no lag; <paramref name="x"/> relative to the capsule's feet (the capsule's own step taken out before),
    /// <paramref name="v"/> the line's rate in the world.
    /// </summary>
    public static void Follow(ref float x, ref float v, float goal, float goalRate, float halfLife, float dt)
    {
        if (halfLife <= 0f) { x = goal; v = goalRate; return; }
        float y = BodyMotor.SpringDamping(halfLife);
        float j0 = x - (goal - goalRate * dt), j1 = (v - goalRate) + j0 * y;
        float e = DetMath.Exp(-y * dt);
        x = goal + e * (j0 + j1 * dt);
        v = goalRate + e * (v - goalRate - j1 * y * dt);
    }

    /// <summary>
    /// One tick of a critically damped spring (the start and stop's form, <see cref="BodyMotor.Spring"/>: Daniel Holden's exact one) of
    /// half-life <paramref name="halfLife"/> taking <paramref name="x"/> (rate <paramref name="v"/>) toward <paramref name="goal"/>; at once with none.
    /// </summary>
    public static void Damped(ref float x, ref float v, float goal, float halfLife, float dt)
    {
        if (halfLife <= 0f) { x = goal; v = 0f; return; }
        float y = BodyMotor.SpringDamping(halfLife);
        float j0 = x - goal, j1 = v + j0 * y;
        float e = DetMath.Exp(-y * dt);
        x = e * (j0 + j1 * dt) + goal;
        v = e * (v - j1 * y * dt);
    }
}
