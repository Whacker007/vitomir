// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the models repository's starts and stops, played as its note says, with
// option a for now: a stop waits for its enter_phase.
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Animation.People;

/// <summary>
/// <b>the starts and stops</b> (the models repository; <see cref="PersonStarts"/>): whether they play and how (<b>SANDBOX</b>, ours). Off
/// (<see cref="Off"/>: <c>--start-clips-off</c>) a person starts and stops as before did (the player on the spring with the loops' fade, everyone else
/// on the ramp) and the world has no <see cref="PersonStep"/> store. A tape carries them in its note <see cref="TapeNote"/>; a tape without it is the earlier build's.
/// </summary>
public sealed record PersonStartSettings
{
    /// <summary>The player's starts and stops on the spring clips (its capsule on the spring as before; a stop waits for its phase: <see cref="WaitForPhase"/>).</summary>
    public bool Player { get; init; } = true;

    /// <summary>
    /// Everyone else on the models repository's bodies (the townsfolk on its crowd): the natural clips, the capsule moved by the clip's own speed (this
    /// changes their simulation: a walking start takes 3.6 m on the clips, 2.9 m on an older set's). <c>--crowd-start-clips-off</c> keeps them on the earlier build's ramp.
    /// </summary>
    public bool Crowd { get; init; } = true;

    /// <summary>:
    /// a stop waits for the loop to reach the stop clip's <c>enter_phase</c> (the nearer ahead of the two feet's: up to 0.55 s walking, 0.37 s
    /// running), the capsule going on at its speed till then. False (<c>--stop-nearest</c>): the stop starts at once, the clip whose phase is
    /// nearer, and the feet's IK takes the difference.
    /// </summary>
    public bool WaitForPhase { get; init; } = true;

    /// <summary>Wanted below this (m/s) is a stop asked; at it or above, a start (the animation's idle threshold).</summary>
    public float Asked { get; init; } = 0.2f;

    /// <summary>A start or stop goes only this near ahead (degrees from the facing; the clips are made ahead): further, the gait's blends as before.</summary>
    public float Ahead { get; init; } = 45f;

    /// <summary>Whether any is on.</summary>
    public bool Any => Player || Crowd;

    /// <summary>None.</summary>
    public static PersonStartSettings Off => new() { Player = false, Crowd = false };

    /// <summary>The tape head's note.</summary>
    public const string TapeNote = "starts";

    /// <summary>The note: <c>off</c>, else <c>player=1 crowd=1 wait=1</c>.</summary>
    public string Note() => !Any ? "off" : $"player={(Player ? 1 : 0)} crowd={(Crowd ? 1 : 0)} wait={(WaitForPhase ? 1 : 0)}";

    /// <summary>These settings with a tape's note laid on; none (an older tape) or <c>off</c> is <see cref="Off"/>.</summary>
    public PersonStartSettings WithNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note) || !note.Contains('=')) return Off;
        var s = this;
        foreach (var part in note.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            bool on = part[(eq + 1)..] == "1";
            s = part[..eq] switch { "player" => s with { Player = on }, "crowd" => s with { Crowd = on }, "wait" => s with { WaitForPhase = on }, _ => s };
        }
        return s;
    }
}

/// <summary>Where a person is in a start or a stop (<see cref="PersonStep.Mode"/>).</summary>
public enum StepMode
{
    /// <summary>Neither: the gaits and the stand as before.</summary>
    None,
    /// <summary>A start clip playing, from the stand to the loop.</summary>
    Start,
    /// <summary>A stop asked, the loop going on to the stop clip's phase.</summary>
    Waiting,
    /// <summary>A stop clip playing, from the loop to the stand.</summary>
    Stop,
}

/// <summary>
/// <b>a person's start or stop</b> — the simulation's state (digested): the animation writes it, the body's motor reads it the next tick. Only
/// people given one by the session have it (the player on the spring; the townsfolk on the models repository's crowd), and a world has the store only
/// with the starts on and people with the clips, so the earlier builds' digest as before.
/// </summary>
public struct PersonStep
{
    /// <summary>Where it is.</summary>
    public StepMode Mode;

    /// <summary>1: the player's, the capsule on the engine's spring (held at its speed while a stop waits); 2: everyone else's, the capsule moved by the clip's speed.</summary>
    public int Curve;

    /// <summary>The way the capsule goes (a unit vector in the world): the way asked starting, the way it went stopping.</summary>
    public Vector2 Way;

    /// <summary>The capsule's speed the next tick (m/s): the clip's (natural), or held while a stop waits.</summary>
    public float Speed;

    /// <summary>The clip's speeds by this to the body's (its pace over the clip's ground speed).</summary>
    public float Scale;

    /// <summary>The speed asked this tick (m/s: the intent's share of its gait's pace; 0 for none), and the way asked (unit, world).</summary>
    public float Wanted;
    public Vector2 WantWay;

    /// <summary>How long a stop has waited for its phase (s).</summary>
    public float Waited;

    /// <summary>A fade under way that takes this long (s) in place of the animation's own: a start from, or a stop into, the stand's own pose; 0 none.</summary>
    public float QuickFade;

    /// <summary>The way the body went last tick (unit, world; as it was when it last moved).</summary>
    public Vector2 Going;

    /// <summary>The body's speed the last tick it was asked to go (m/s): a stop holds it (the tick a stop is asked the motor has already begun to slow).</summary>
    public float Cruise;

    /// <summary>Whether the capsule follows the clip (the motor reads it).</summary>
    public readonly bool Moves => (Curve == 2 && Mode != StepMode.None) || (Curve == 3 && Mode == StepMode.Stop);   // the player's own stops by their clips

    /// <summary>Whether the player's capsule is held at <see cref="Speed"/> while its stop waits.</summary>
    public readonly bool Holds => Curve == 1 && Mode == StepMode.Waiting;
}

/// <summary>
/// <b>a person's start and stop clips</b> (the models repository): from either foot (<c>_l</c>: the left steps first, or closes
/// last), the player's on the engine's spring (half-life 0.2 s: <c>walk_start_l</c> … <c>run_stop_r</c>), everyone else's on the human curves
/// <c>walk_start_natural_l</c> 0.4 s after a 0.45 s prep, <c>run_start_natural_l</c> 0.3 s, <c>run_stop_natural_l</c> 0.35 s; the walking stops serve
/// everyone; every number read from their notes). Found by
/// name; −1 for one the set lacks. Null on <see cref="PersonMotion.Starts"/> for a set without them (an older set).
/// </summary>
public sealed class PersonStarts
{
    /// <summary>The clips by [gait (0 walk, 1 run), curve (0 spring, 1 natural), foot (0 left, 1 right)].</summary>
    public required int[] StartClips { get; init; }
    public required int[] StopClips { get; init; }

    static int At(bool run, bool natural, bool left) => (run ? 4 : 0) + (natural ? 2 : 0) + (left ? 0 : 1);

    /// <summary>The start for a gait, curve and foot (−1 for none).</summary>
    public int Start(bool run, bool natural, bool left) => StartClips[At(run, natural, left)];

    /// <summary>The stop for a gait, curve and foot (the walking stops serve both curves; −1 for none).</summary>
    public int Stop(bool run, bool natural, bool left) => StopClips[At(run, natural, left)];

    /// <summary>
    /// The stand's clip (the idle role's), and the starts that begin and the stops that end in the stand's own pose (the legs within
    /// <see cref="StandWithin"/> of its first frame: the stops; every round's starts), handed to and from it with the quick fade
    /// (<see cref="StandFade"/>) in place of the animation's.
    /// </summary>
    public int Stand { get; internal set; } = -1;
    public int[] FromStand { get; internal set; } = [];
    public int[] ToStand { get; internal set; } = [];

    /// <summary>How near the stand's first frame a clip's legs (the pelvis, thighs, calves, feet and balls) must be to count as in its pose (m), and the quick fade (s).</summary>
    public const float StandWithin = 0.035f, StandFade = 0.1f;

    public bool BeginsInStand(int clip) => clip >= 0 && Array.IndexOf(FromStand, clip) >= 0;
    public bool EndsInStand(int clip) => clip >= 0 && Array.IndexOf(ToStand, clip) >= 0;

    /// <summary>
    /// Measures which of the clips begin (starts) or end (stops) in <paramref name="stand"/>'s first frame (<see cref="StandWithin"/>) and keeps
    /// <paramref name="standClip"/> as the stand to hand to. Called once, when the kind is registered.
    /// </summary>
    public void Measure(CharacterAssets assets, Skeleton skeleton, IAnimationClip stand, int standClip)
    {
        Stand = standClip;
        (FromStand, ToStand) = InStand(assets, skeleton, stand, StartClips, StopClips);
    }

    /// <summary>The earlier build's rule for any starts and stops (the player's own too): which begin and which end in <paramref name="stand"/>'s first frame.</summary>
    internal static (int[] From, int[] To) InStand(CharacterAssets assets, Skeleton skeleton, IAnimationClip stand, int[] startClips, int[] stopClips)
    {
        string[] legs = ["pelvis", "thigh_l", "thigh_r", "calf_l", "calf_r", "foot_l", "foot_r", "ball_l", "ball_r"];
        var bones = legs.Select(skeleton.Find).Where(b => b >= 0).ToArray();
        int n = skeleton.Count;
        var local = new BoneTransform[n];
        var at = new Matrix4x4[n];
        var model = new Matrix4x4[n];
        stand.Sample(0f, skeleton, local);
        PoseEvaluator.ToModel(skeleton, local, at);
        bool In(int clip, bool end)
        {
            var c = assets.Clip(clip);
            c.Sample(end ? c.Duration : 0f, skeleton, local);
            PoseEvaluator.ToModel(skeleton, local, model);
            return bones.All(b => Vector3.Distance(model[b].Translation, at[b].Translation) <= StandWithin);
        }
        return ([.. startClips.Where(c => c >= 0 && In(c, false))], [.. stopClips.Where(c => c >= 0 && In(c, true))]);
    }

    /// <summary>Whether <paramref name="clip"/> is one of them.</summary>
    public bool Holds(int clip) => clip >= 0 && (Array.IndexOf(StartClips, clip) >= 0 || Array.IndexOf(StopClips, clip) >= 0);

    /// <summary>The clips by name from a set (null when it has none).</summary>
    public static PersonStarts? From(Func<string, INotedClip?> named, Func<INotedClip, int> indexOf)
    {
        int Of(string name, bool start) => named(name) is { Loops: false, Notes.StartStop: { } ss } c && ss.Start == start && ss.Speeds.Length > 1 ? indexOf(c) : -1;
        var starts = new int[8];
        var stops = new int[8];
        foreach (bool run in new[] { false, true })
            foreach (bool natural in new[] { false, true })
                foreach (bool left in new[] { true, false })
                {
                    string g = run ? "run" : "walk", n = natural ? "_natural" : "", f = left ? "l" : "r";
                    starts[At(run, natural, left)] = Of($"{g}_start{n}_{f}", true);
                    // The walking stops serve everyone (a planned walking stop is the spring's own).
                    int stop = Of($"{g}_stop{n}_{f}", false);
                    if (stop < 0 && natural) stop = Of($"{g}_stop_{f}", false);
                    stops[At(run, natural, left)] = stop;
                }
        return starts.Any(c => c >= 0) || stops.Any(c => c >= 0) ? new PersonStarts { StartClips = starts, StopClips = stops } : null;
    }
}

/// <summary>
/// <b>playing the starts and stops</b> (<see cref="AnimationSystem"/> calls it for a person with a <see cref="PersonStep"/>), the models repository's
/// note: a start from the stand at once (the player's capsule on the spring started then, so the clip is entered where its speed is the capsule's;
/// anyone else's capsule moved by the clip's speed); at its last frame the loop takes over at its <c>exit_phase</c>, with no fade. A stop when the
/// loop reaches its <c>enter_phase</c> (whatever share of the stride each foot's stop gives: whichever of <c>_l</c> and <c>_r</c> comes next), the spring's goal nought
/// then (the player's capsule held at its speed till then); it ends in the stand. Ours (no pops: each with the animation's fade):
/// <list type="bullet">
/// <item>Asked to go on mid-stop, or to stop mid-start, or the way turning more than <see cref="PersonStartSettings.Ahead"/> from the facing
/// mid-start: a stop asked mid-start goes to the stop of the same foot entered where its speed is the capsule's; anything else fades to the gaits as
/// before (a stop is left for the loop, a start for the blend of the way).</item>
/// <item>Not on the ground, in a move, crouched or dodging: none (as before).</item>
/// <item>Only the walk and the run going ahead: a start sideways or back, the sprint, the crouch, a townsperson's styled walk start and stop as
/// before.</item>
/// </list>
/// </summary>
public static class PeopleStarts
{
    /// <summary>
    /// One tick of a person's start or stop: true when it chose the clip (<paramref name="a"/> set up, its rate set; the caller only advances it),
    /// false to let the gaits choose as before (with <paramref name="fade"/> when it is leaving a start or a stop for them).
    /// </summary>
    public static bool Choose(CharacterAssets assets, PersonMotion person, ref CharacterAnimation a, ref PersonStep st, in MotionInput input, MotionRole want,
                              PersonStartSettings rules, float runFrom, float seconds, out bool fade)
    {
        fade = false;
        var starts = person.Starts!;
        bool natural = st.Curve == 2;
        bool free = input.Grounded && input.Traversal == TraversalKind.None && !input.Crouched && !input.Dodging;
        float speed = input.GroundSpeed;
        float off = speed > 0.05f ? MathF.Abs(DetMath.Atan2(input.SideSpeed, input.ForwardSpeed)) * DetMath.RadToDeg : 0f;
        bool asked = st.Wanted >= rules.Asked;
        switch (st.Mode)
        {
            case StepMode.Start:
            {
                var notes = (assets.Clip(a.Clip) as INotedClip)?.Notes.StartStop;
                bool run = notes?.Gait == "run";
                if (!free || notes is null || !starts.Holds(a.Clip)) { Leave(ref st); fade = true; return false; }
                if (!asked)
                {
                    // Asked to stop mid-start: the stop of the same foot, entered where its speed is the capsule's.
                    int stop = starts.Stop(run, natural, notes.Left);
                    if (stop >= 0 && Enter(assets, ref a, ref st, stop, speed, run)) return true;
                    Leave(ref st); fade = true; return false;
                }
                if (off > rules.Ahead || (st.Wanted >= runFrom) != run) { Leave(ref st); fade = true; return false; }   // turned aside, or another gait asked
                var clip = assets.Clip(a.Clip);
                if (a.Time + seconds >= clip.Duration)
                {
                    // Its last frame: the loop takes over at its exit_phase, no fade.
                    var gait = run ? person.Run : person.Walk;
                    if (gait is null) { Leave(ref st); return false; }
                    int loop = gait.Clip(input.ForwardSpeed, input.SideSpeed, gait.First);
                    float over = a.Time + seconds - clip.Duration;
                    a.Clip = loop;
                    a.Role = run ? MotionRole.Run : MotionRole.Walk;
                    a.Time = notes.Phase * assets.Clip(loop).Duration + over;
                    a.PreviousClip = -1;
                    a.Blend = 1f;
                    a.Rate = 0f;   // the caller sets the loop's rate this tick (it falls through to the gaits)
                    Leave(ref st);
                    return false;
                }
                a.Rate = 1f;
                Follow(ref st, notes, a.Time + seconds, st.WantWay);
                return true;
            }
            case StepMode.Waiting:
            {
                if (asked || !free) { Leave(ref st); return false; }
                st.Waited += seconds;
                var gait = person.GaitOf(a.Role);
                if (gait is null || !gait.Holds(a.Clip) || speed < 0.5f * st.Speed || st.Waited > 1.5f) { Leave(ref st); return false; }
                // The loop goes on (the gaits move it); the crossing of a stop's phase is looked for after (Crossed).
                return false;
            }
            case StepMode.Stop:
            {
                var notes = (assets.Clip(a.Clip) as INotedClip)?.Notes.StartStop;
                if (!free || notes is null || !starts.Holds(a.Clip)) { Leave(ref st); fade = true; return false; }
                if (asked) { Leave(ref st); fade = true; return false; }   // going on: to the gaits, faded
                var clip = assets.Clip(a.Clip);
                if (a.Time >= clip.Duration - 1e-4f)
                {
                    // Its end: the stand, feet side by side. a stop that ends in the stand's pose hands to it at once, with only
                    // the quick fade; any other, to the gaits faded as before.
                    Leave(ref st);
                    if (starts.EndsInStand(a.Clip) && starts.Stand >= 0)
                    {
                        Begin(ref a, starts.Stand, MotionRole.Idle, 0f, fade: true);
                        a.Rate = 1f;
                        st.QuickFade = PersonStarts.StandFade;
                        return true;
                    }
                    fade = true;
                    return false;
                }
                a.Rate = 1f;
                Follow(ref st, notes, a.Time + seconds, st.Way);
                return true;
            }
            default:
            {
                if (!free) return false;
                // A start: from the stand (or a turn on the spot), going ahead.
                bool standing = a.Role is MotionRole.Idle or MotionRole.Turn;
                if (standing && asked && (want is MotionRole.Walk or MotionRole.Run || speed > 0f) && off <= rules.Ahead && speed < 0.5f * MathF.Max(st.Wanted, 0.4f))
                {
                    bool run = st.Wanted >= runFrom;
                    if ((run ? person.Run : person.Walk) is null) return false;   // a styled walk of its own: as before
                    // The foot: toward the way asked's side (the left for straight ahead).
                    bool left = input.SideSpeed >= -1e-3f;
                    int clip = starts.Start(run, natural, left);
                    if (clip < 0 || assets.Clip(clip) is not INotedClip { Notes.StartStop: { } notes } made) return false;
                    float pace = made.Notes.GroundSpeed > 0f ? made.Notes.GroundSpeed : notes.Speeds[^1];
                    st.Scale = pace > 0f ? st.Wanted / pace : 1f;
                    // The player's capsule set off on the spring this tick: entered where the clip's speed is the capsule's. Anyone else's from rest.
                    float time = natural ? 0f : notes.TimeAt(speed / MathF.Max(1e-3f, st.Scale));
                    Begin(ref a, clip, run ? MotionRole.Run : MotionRole.Walk, time, fade: true);
                    if (starts.BeginsInStand(clip)) st.QuickFade = PersonStarts.StandFade;   // from the stand's own pose, only the quick fade
                    st.Mode = StepMode.Start;
                    st.Way = st.WantWay;
                    a.Rate = 1f;
                    Follow(ref st, notes, time + seconds, st.WantWay);
                    return true;
                }
                // A stop: the walk or the run going ahead, nothing asked.
                if (!asked && a.Role is MotionRole.Walk or MotionRole.Run && person.GaitOf(a.Role) is { } g && g.Holds(a.Clip) && off <= rules.Ahead && speed >= rules.Asked)
                {
                    bool run = a.Role == MotionRole.Run;
                    st.Way = speed > 1e-3f ? Way(input, st) : st.WantWay;
                    st.Speed = MathF.Max(speed, st.Cruise);
                    st.Waited = 0f;
                    if (rules.WaitForPhase) { st.Mode = StepMode.Waiting; return false; }
                    // At once, the stop whose phase is nearer; the feet's IK takes the difference.
                    float d = assets.Clip(a.Clip).Duration, p = d > 0f ? a.Time / d : 0f;
                    p -= MathF.Floor(p);
                    int stop = StopAt(assets, starts, run, natural, p);   // the stop whose enter_phase is nearer
                    if (stop < 0 || assets.Clip(stop) is not INotedClip { Notes.StartStop: { } notes } made) { st.Mode = StepMode.None; return false; }
                    float pace = notes.Speeds.Length > 0 && notes.Speeds[0] > 0f ? notes.Speeds[0] : made.Notes.GroundSpeed;
                    st.Scale = pace > 0f ? speed / pace : 1f;
                    Begin(ref a, stop, a.Role, 0f, fade: true);
                    st.Mode = StepMode.Stop;
                    a.Rate = 1f;
                    Follow(ref st, notes, seconds, st.Way);
                    return true;
                }
                return false;
            }
        }
    }

    /// <summary>
    /// One tick of <b>the player's own start or stop</b> (the models repository; <see cref="PlayerGaits"/>), as <see cref="Choose"/> but on the
    /// player's three gaits (the walk, the run, the sprint) and with: a stop is taken <b>at once</b> by the nearest of its four ways in a stride (its
    /// notes' <c>enter_phase</c>), from its start, the loop's pose (at most an eighth of a stride either side of the way in) blended into it over
    /// <see cref="PersonGaitSettings.StopBlend"/> of the stride; never waiting. The capsule during a stop is driven by the clip's own
    /// <c>speed_m_s</c> (the run's on its 0.16 s half-life; <see cref="PersonStep.Moves"/>), by the capsule's speed over the clip's at the way in; a start sets
    /// off on the spring as before (the clips are made on it), entered where its speed is the capsule's, and hands its loop over at its <c>exit_phase</c>.
    /// Ours, as before: asked to stop mid-start, the stop of its foot entered where its speed is the capsule's; asked to go on mid-stop, another gait mid-start
    /// or turned more than <see cref="PersonStartSettings.Ahead"/>, the gaits (faded). The gait a start is at: the walk below the player's
    /// <see cref="PersonGaitSettings.RunFrom"/>, the sprint from <see cref="PersonGaitSettings.SprintFrom"/> (the speed asked).
    /// </summary>
    public static bool ChoosePlayer(CharacterAssets assets, PersonMotion person, PlayerGaits own, ref CharacterAnimation a, ref PersonStep st, in MotionInput input,
                                    MotionRole want, PersonStartSettings rules, PersonGaitSettings gaits, float seconds)
    {
        bool free = input.Grounded && input.Traversal == TraversalKind.None && !input.Dodging && !input.Crouched;
        float speed = input.GroundSpeed;
        float off = speed > 0.05f ? MathF.Abs(DetMath.Atan2(input.SideSpeed, input.ForwardSpeed)) * DetMath.RadToDeg : 0f;
        bool asked = st.Wanted >= rules.Asked;
        int gaitAsked = own.GaitFor(st.Wanted, gaits.RunFrom, gaits.SprintFrom);
        switch (st.Mode)
        {
            case StepMode.Start:
            {
                int g = own.GaitOfStart(a.Clip);
                var notes = (assets.Clip(a.Clip) as INotedClip)?.Notes.StartStop;
                if (!free || notes is null || g < 0) { Leave(ref st); return false; }
                if (!asked)
                {
                    // Asked to stop mid-start: a stop of the same gait and foot, entered where its speed is the capsule's.
                    int stop = -1;
                    for (int k = 0; k < 4 && stop < 0; k++)
                        if (own.Stops[g * 4 + k] is int c and >= 0 && assets.Clip(c) is INotedClip { Notes.StartStop: { } sn } && sn.Left == notes.Left) stop = c;
                    if (stop >= 0 && Enter(assets, ref a, ref st, stop, speed, g > 0)) return true;
                    Leave(ref st); return false;
                }
                if (off > rules.Ahead || gaitAsked != g) { Leave(ref st); return false; }   // turned aside, or another gait asked: the gaits, faded
                var clip = assets.Clip(a.Clip);
                if (a.Time + seconds >= clip.Duration)
                {
                    // Its last frame: the loop takes over at its exit_phase, no fade.
                    int loop = own.Loops[g];
                    if (loop < 0) { Leave(ref st); return false; }
                    float over = a.Time + seconds - clip.Duration;
                    a.Clip = loop;
                    a.Role = g == 0 ? MotionRole.Walk : MotionRole.Run;
                    a.Time = notes.Phase * assets.Clip(loop).Duration + over;
                    a.PreviousClip = -1;
                    a.Blend = 1f;
                    a.Rate = 0f;   // the caller sets the loop's rate this tick (it falls through to the gaits)
                    Leave(ref st);
                    return false;
                }
                a.Rate = 1f;
                Follow(ref st, notes, a.Time + seconds, st.WantWay);
                return true;
            }
            case StepMode.Stop:
            {
                var notes = (assets.Clip(a.Clip) as INotedClip)?.Notes.StartStop;
                if (!free || notes is null || own.GaitOfStop(a.Clip) < 0 || asked) { Leave(ref st); return false; }   // going on: to the gaits, faded
                var clip = assets.Clip(a.Clip);
                if (a.Time >= clip.Duration - 1e-4f)
                {
                    // Its end: the stand (only the quick fade when it ends in the stand's own pose).
                    Leave(ref st);
                    if (Array.IndexOf(own.ToStand, a.Clip) >= 0 && person.Starts?.Stand is >= 0 and int stand)
                    {
                        Begin(ref a, stand, MotionRole.Idle, 0f, fade: true);
                        a.Rate = 1f;
                        st.QuickFade = PersonStarts.StandFade;
                        return true;
                    }
                    return false;
                }
                a.Rate = 1f;
                Follow(ref st, notes, a.Time + seconds, st.Way);
                return true;
            }
            case StepMode.Waiting:
                Leave(ref st);   // (the earlier build's wait; the player's own gaits never wait)
                return false;
            default:
            {
                if (!free) return false;
                // A start: from the stand (or a turn on the spot), going ahead.
                bool standing = a.Role is MotionRole.Idle or MotionRole.Turn;
                if (standing && asked && (want is MotionRole.Walk or MotionRole.Run || speed > 0f) && off <= rules.Ahead && speed < 0.5f * MathF.Max(st.Wanted, 0.4f))
                {
                    bool left = input.SideSpeed >= -1e-3f;   // the foot toward the way asked's side (the left for straight ahead)
                    int clip = own.Start(gaitAsked, left);
                    if (clip < 0 || assets.Clip(clip) is not INotedClip { Notes.StartStop: { } notes } made) return false;
                    float pace = made.Notes.GroundSpeed > 0f ? made.Notes.GroundSpeed : notes.Speeds[^1];
                    st.Scale = pace > 0f ? st.Wanted / pace : 1f;
                    // The capsule set off on the spring this tick: entered where the clip's speed is the capsule's.
                    float time = notes.TimeAt(speed / MathF.Max(1e-3f, st.Scale));
                    Begin(ref a, clip, gaitAsked == 0 ? MotionRole.Walk : MotionRole.Run, time, fade: true);
                    if (Array.IndexOf(own.FromStand, clip) >= 0) st.QuickFade = PersonStarts.StandFade;
                    st.Mode = StepMode.Start;
                    st.Way = st.WantWay;
                    a.Rate = 1f;
                    Follow(ref st, notes, time + seconds, st.WantWay);
                    return true;
                }
                // A stop: one of the three loops going ahead, nothing asked — at once, by the nearest of its four ways in.
                int gait = own.GaitOfLoop(a.Clip);
                if (!asked && gait >= 0 && off <= rules.Ahead && speed >= rules.Asked)
                {
                    float d = assets.Clip(a.Clip).Duration, p = d > 0f ? a.Time / d : 0f;
                    p -= MathF.Floor(p);
                    int stop = own.StopAt(assets, gait, p, out _);
                    if (stop < 0 || assets.Clip(stop) is not INotedClip { Notes.StartStop: { } notes } made) return false;
                    st.Way = speed > 1e-3f ? Way(input, st) : st.WantWay;
                    st.Waited = 0f;
                    float pace = notes.Speeds.Length > 0 && notes.Speeds[0] > 0f ? notes.Speeds[0] : made.Notes.GroundSpeed;
                    float at = MathF.Max(speed, st.Cruise);
                    st.Scale = pace > 0f ? at / pace : 1f;
                    // From its start (its first frame is the loop's at its way in), the loop's pose up to an eighth of a stride either side of it blended into it:
                    // so the capsule goes on from the speed it had along the clip's own curve, and the stop takes the clip's own distance and time. (Entered as far into
                    // the clip as the loop was past its way in, the capsule would lose the speed of those first frames in one tick and stop up to a third shorter.)
                    const float time = 0f;
                    Begin(ref a, stop, a.Role, time, fade: true);
                    st.QuickFade = MathF.Max(World.TickSeconds, gaits.StopBlend);
                    st.Mode = StepMode.Stop;
                    a.Rate = 1f;
                    Follow(ref st, notes, time + seconds, st.Way);
                    return true;
                }
                return false;
            }
        }
    }

    /// <summary>
    /// After the loop has moved on (<paramref name="before"/> its time before, in a stop's wait): when it crossed a stop clip's
    /// <c>enter_phase</c>, that stop begins, at what it had run past it (no fade: it begins on the loop's pose).
    /// </summary>
    public static void Crossed(CharacterAssets assets, PersonMotion person, ref CharacterAnimation a, ref PersonStep st, float before, float seconds)
    {
        if (st.Mode != StepMode.Waiting || a.Rate <= 0f) return;
        var starts = person.Starts!;
        float d = assets.Clip(a.Clip).Duration;
        if (d <= 0f) return;
        float p0 = before / d, moved = seconds * a.Rate / d;
        p0 -= MathF.Floor(p0);
        // The stop whose enter_phase (any share of the stride its notes give, each foot its own) the loop reached first this tick.
        bool run = a.Role == MotionRole.Run;
        int stop = -1;
        float reached = float.MaxValue;
        foreach (bool left in new[] { true, false })
        {
            int c = starts.Stop(run, st.Curve == 2, left);
            if (c < 0 || assets.Clip(c) is not INotedClip { Notes.StartStop: { } candidate }) continue;
            float ahead = candidate.Phase - p0;
            ahead -= MathF.Floor(ahead);   // how far ahead in the stride its phase is, [0, 1)
            if (ahead <= moved && ahead < reached) (reached, stop) = (ahead, c);
        }
        if (stop < 0) return;
        if (assets.Clip(stop) is not INotedClip { Notes.StartStop: { } notes } made) { Leave(ref st); return; }
        float pace = notes.Speeds.Length > 0 && notes.Speeds[0] > 0f ? notes.Speeds[0] : made.Notes.GroundSpeed;
        st.Scale = pace > 0f ? st.Speed / pace : 1f;
        float past = (moved - reached) * d / MathF.Max(a.Rate, 1e-3f);   // seconds of real time past the phase
        Begin(ref a, stop, a.Role, past, fade: false);
        a.Rate = 1f;
        st.Mode = StepMode.Stop;
        Follow(ref st, notes, past + 0.5f * seconds, st.Way);
    }

    /// <summary>
    /// Of a gait's two stops (<c>_l</c>, <c>_r</c>), the one whose <c>enter_phase</c> is nearest <paramref name="phase"/> (a share of the
    /// stride, round the cycle): read from its notes, not its name (one set entered the running stops' <c>_l</c> at half the stride, the next at its start).
    /// </summary>
    static int StopAt(CharacterAssets assets, PersonStarts starts, bool run, bool natural, float phase)
    {
        int best = -1;
        float nearest = float.MaxValue;
        foreach (bool left in new[] { true, false })
        {
            int c = starts.Stop(run, natural, left);
            if (c < 0 || assets.Clip(c) is not INotedClip { Notes.StartStop: { } notes }) continue;
            float d = phase - notes.Phase;
            d -= MathF.Round(d);
            if (MathF.Abs(d) < nearest) (nearest, best) = (MathF.Abs(d), c);
        }
        return best;
    }

    /// <summary>Into a stop from a start, at the stop's frame where its speed is the capsule's, faded.</summary>
    static bool Enter(CharacterAssets assets, ref CharacterAnimation a, ref PersonStep st, int stop, float speed, bool run)
    {
        if (assets.Clip(stop) is not INotedClip { Notes.StartStop: { } notes }) return false;
        float pace = notes.Speeds.Length > 0 && notes.Speeds[0] > 0f ? notes.Speeds[0] : 1f;
        float scale = st.Scale > 0f ? st.Scale : 1f;
        float time = speed >= pace * scale ? 0f : notes.TimeAt(speed / scale);
        Begin(ref a, stop, run ? MotionRole.Run : MotionRole.Walk, time, fade: true);
        st.Mode = StepMode.Stop;
        a.Rate = 1f;
        Follow(ref st, notes, time, st.Way);
        return true;
    }

    /// <summary>The capsule's speed and way the next tick (the natural curve's: the clip's speed at <paramref name="time"/>, by the scale).</summary>
    static void Follow(ref PersonStep st, ClipStartStop notes, float time, Vector2 way)
    {
        st.Speed = notes.SpeedAt(time) * (st.Scale > 0f ? st.Scale : 1f);
        if (way.LengthSquared() > 1e-6f) st.Way = Vector2.Normalize(way);
    }

    /// <summary>The way the body moves (the world's), from its speeds along and across its facing and the way asked when still.</summary>
    static Vector2 Way(in MotionInput input, in PersonStep st) => st.Going.LengthSquared() > 1e-6f ? st.Going : st.WantWay;

    /// <summary>A start or a stop given up (the air's clips have the body: <see cref="PeopleAir"/>): none, as <see cref="StepMode.None"/>.</summary>
    public static void Clear(ref PersonStep st)
    {
        if (st.Mode != StepMode.None || st.Speed != 0f || st.Waited != 0f || st.QuickFade != 0f) Leave(ref st);
    }

    static void Leave(ref PersonStep st)
    {
        st.Mode = StepMode.None;
        st.Speed = 0f;
        st.Waited = 0f;
        st.QuickFade = 0f;
    }

    static void Begin(ref CharacterAnimation a, int clip, MotionRole role, float time, bool fade)
    {
        if (fade && a.Clip >= 0)
        {
            a.PreviousClip = a.Clip;
            a.PreviousTime = a.Time;
            a.PreviousRate = a.Rate;
            a.Blend = 0f;
        }
        else { a.PreviousClip = -1; a.Blend = 1f; }
        a.Clip = clip;
        a.Role = role;
        a.Time = time;
        a.RateScale = 1f;
    }
}
