// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the models repository's people as kinds of character.
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Animation.People;

/// <summary>
/// What a kind on the models repository's bodies has beyond a 1.1 kind (<see cref="CharacterKind.Person"/>): the bones it
/// is posed by by name, its walk's and run's eight directions (<see cref="Gait"/>), its turns on the spot
/// (<see cref="TurnSet"/>), and the twist bones' rule (<see cref="TwistRule"/>). Made once by <see cref="PeopleKinds.Register"/>;
/// read-only after.
/// </summary>
public sealed class PersonMotion
{
    /// <summary>The bones by name (<see cref="PersonRig.From"/>).</summary>
    public required Assets.People.PersonRig Rig { get; init; }

    /// <summary>The first bone, <c>root</c>: between the feet on the ground, the bone a turn on the spot turns; the legs' turn turns the body about it.</summary>
    public int Root => Rig.Root;

    /// <summary><c>spine_01</c> to <c>_03</c>: the spine the legs' turn turns back so the chest stays on the facing (<see cref="LegTurn.Apply"/>).</summary>
    public int[] Spine => Rig.Spine;

    /// <summary>The walk's eight directions (also the crouched walk's), or null when the set has only the walk ahead.</summary>
    public Gait? Walk { get; init; }

    /// <summary>The run's eight directions (also the sprint's sideways and back), or null when the set has only the run ahead.</summary>
    public Gait? Run { get; init; }

    /// <summary>
    /// The <c>sprint</c>, the run's rule at 5.5 m/s straight ahead (a loop), its place in the kind's
    /// clips, or −1 for a set without it; played for a run ahead at a sprint (<see cref="AnimationSettings.PersonSprintFrom"/>).
    /// </summary>
    public int Sprint { get; init; } = -1;

    /// <summary>The ground speed the sprint is drawn for (m/s: its notes' <c>ground_speed_m_s</c>, by the kind's scale).</summary>
    public float SprintSpeed { get; init; }

    /// <summary>
    /// The models repository's <c>walk_sneak</c> (a loop, 0.7 m/s straight ahead), its place in the kind's clips, or −1 for a set without it; played for a
    /// walk ahead in stealth (<see cref="PersonStealthSettings.Sneak"/>).
    /// </summary>
    public int Sneak { get; init; } = -1;

    /// <summary>The ground speed the sneak is drawn for (m/s: its notes' <c>ground_speed_m_s</c>, by the kind's scale).</summary>
    public float SneakSpeed { get; init; }

    /// <summary>Whether <paramref name="clip"/> is the sprint.</summary>
    public bool Sprints(int clip) => clip >= 0 && clip == Sprint;

    /// <summary>The player's own walk, run and sprint and their starts and stops (the models repository's clips), or null for a set without them.</summary>
    public PlayerGaits? PlayerGaits { get; init; }

    /// <summary>Whether <paramref name="clip"/> is drawn straight ahead only (the sprint, the player's loops, starts and stops): the legs turned toward the way.</summary>
    public bool AheadOnly(int clip) => Sprints(clip) || PlayerGaits?.Holds(clip) == true;

    /// <summary>The turns on the spot, or null when the set has none.</summary>
    public TurnSet? Turns { get; init; }

    /// <summary>The walk's and the run's uphill and downhill clips, or null for a set without them.</summary>
    public PersonSlopes? Slopes { get; init; }

    /// <summary>The stair clips when a set has them (the hook for a later round: found by name, not played yet), or null.</summary>
    public PersonStairs? Stairs { get; init; }

    /// <summary>The starts and stops when a set has them (<see cref="PeopleStarts"/>), or null.</summary>
    public PersonStarts? Starts { get; init; }

    /// <summary>The jumps, the fall and the landings when a set has them (<see cref="PeopleAir"/>), or null.</summary>
    public PersonAirClips? Air { get; init; }

    /// <summary>The moves over and onto things and off edges when a set has them (<see cref="PeopleOver"/>, <see cref="PeopleOff"/>), or null.</summary>
    public PersonOverClips? Over { get; init; }

    /// <summary>The ledge (hang, shimmy, braces, pull-up, drop, grabs) when a set has it (<see cref="PeopleLedge"/>), or null.</summary>
    public PersonLedgeClips? Ledge { get; init; }

    /// <summary>The ladders (the leaning and the upright sets, the side landings, the ladder to a braced hang) when a set has them (<see cref="PeopleLadder"/>), or null.</summary>
    public PersonLadderClips? Ladder { get; init; }

    /// <summary>The twist bones' rule, or null for a skeleton without them.</summary>
    public TwistRule? Twist { get; init; }

    /// <summary>The bend helpers' rule (the knees and hips), or null for a skeleton without them.</summary>
    public BendRule? Bend { get; init; }

    /// <summary>A skirt hung from the two thighs carried by both (<see cref="SkirtRule"/>), or null for a body without one.</summary>
    public SkirtRule? Skirt { get; init; }

    /// <summary>The swing bones' simulator for what the person wears (<see cref="SwingRule"/>), or null when nothing worn has swing chains.</summary>
    public SwingRule? Swing { get; init; }

    /// <summary>The crouch of the models repository (<see cref="PeopleCrouch"/>), or null for a set without it (the stand-ins).</summary>
    public PersonCrouch? Crouch { get; init; }

    /// <summary>
    /// The gait a locomotion role plays (the walk for a walk, the run for a run; for a crouched walk the crouch's own when the set
    /// has it, else the walk, lowered by the rig), or null.
    /// </summary>
    public Gait? GaitOf(MotionRole role) => role switch
    {
        MotionRole.Walk => Walk,
        MotionRole.CrouchWalk => Crouch is { } crouch ? crouch.Walk : Walk,
        MotionRole.Run => Run,
        _ => null,
    };

    /// <summary>The turns a clip belongs to (the standing ones or the crouched ones), or null.</summary>
    public TurnSet? TurnsOf(int clip) => Turns?.Holds(clip) == true ? Turns : Crouch?.Turns?.Holds(clip) == true ? Crouch.Turns : null;

    /// <summary>
    /// The clips whose tables (the rig's ankles, <see cref="PoseRigSystem"/>; the eyes' bob, <see cref="EyeTracks"/>) stand for the
    /// kind's own clips — the gaits' direction clips and one turn a source played in place — made when the kind is first seen.
    /// </summary>
    public int[] PoseClips { get; init; } = [];

    /// <summary>
    /// For a clip of the platform's own (a gait's blend, a turn played for some degrees): the clips of <see cref="PoseClips"/> whose
    /// tables stand for it at <paramref name="seconds"/> — the blend's two at the same share of their cycles, the second's weight
    /// <paramref name="weight"/>; a turn's one in place — so the tables are made for twenty clips, not a thousand, and nothing is
    /// made when the body first goes some new way. False for any other clip.
    /// </summary>
    public bool Sources(IAnimationClip clip, float seconds, out int a, out float atA, out int b, out float atB, out float weight)
    {
        (a, atA, b, atB, weight) = (-1, 0f, -1, 0f, 0f);
        if (clip is GaitBlendClip blend && blend.FromClip >= 0 && blend.ToClip >= 0)
        {
            float d = blend.Duration, p = d > 0f ? seconds / d : 0f;
            p -= MathF.Floor(p);
            (a, atA, b, atB, weight) = (blend.FromClip, p * blend.From.Duration, blend.ToClip, p * blend.To.Duration, blend.Weight);
            return true;
        }
        if (clip is TurnClip turn && turn.PoseClip >= 0)
        {
            (a, atA, b, atB) = (turn.PoseClip, seconds, turn.PoseClip, seconds);
            return true;
        }
        return false;
    }
}

/// <summary>
/// One gait's eight directions: the walk (or run) ahead, back, left, right and the
/// four diagonals, each made going its own way (<see cref="ClipNotes.HeadingDeg"/>) at its own ground speed with the body
/// facing ahead, the hips turned toward the way (the clips turn the hips themselves). <b>A body going any way plays the two
/// either side of its way blended by angle</b>, phase-synchronised (by the share of each one's own cycle: their lengths differ,
/// and each has its left foot down at its start and its right at its middle), at the rate that keeps the blend's stride on the
/// ground. A blend is a clip of its own (<see cref="GaitBlendClip"/>), one for every whole degree round
/// (<see cref="Bins"/> of them, from <see cref="First"/> in the kind's clips), so the pose stays a pure function of the
/// animation's state (its clip and time): a degree is a fiftieth of the 45° between two clips, a change too small to see,
/// and the body's way changes smoothly (its velocity is on a spring).
/// <para>
/// <b>Where the gait turns from stepping ahead to backpedalling</b> (between a clip and its neighbour whose
/// <c>played_backwards</c> differs: left and back-left, right and back-right, the hips turned +60° and −30°) the two are
/// <b>not blended</b> — their legs swing in opposite senses, and a blend's stride cancels (measured: the feet slide at the
/// body's whole speed, about 1 m/s walking, halfway between). The nearer one plays alone and <b>the legs are turned by the
/// rest of the angle</b> (<see cref="GaitBlendClip.Turn"/>, at most half the 45° between: the legs' turn, as 1.1's kinds' legs
/// turn toward the way, <see cref="PeopleTurn"/>), and the change from one to the other fades as any change of motion. Within
/// <see cref="Band"/>° of the halfway line the one playing is kept (<see cref="Alternates"/>), so a way held on the line does
/// not flicker.
/// </para>
/// </summary>
public sealed class Gait
{
    /// <summary>One blend a degree, all the way round.</summary>
    public const int Bins = 360;

    /// <summary>How far past the halfway line between a forward clip and a backpedal the one playing is kept (degrees).</summary>
    public const int Band = 6;

    /// <summary>The gait's name (<c>walk</c>, <c>run</c>).</summary>
    public required string Name { get; init; }

    /// <summary>The clip of the blend at 0° (straight ahead); the one at <c>d</c>° (to the body's left, 0 to 359) is <c>First + d</c>; then the alternates.</summary>
    public required int First { get; init; }

    /// <summary>The ground speed each degree's clip is drawn for (m/s; <see cref="GaitBlendClip.NaturalSpeed"/>).</summary>
    public required float[] Speeds { get; init; }

    /// <summary>The ways the gait's clips go (degrees, 0 to 359, to the body's left), in order round.</summary>
    public required float[] Headings { get; init; }

    /// <summary>
    /// For each degree near a halfway line between a forward clip and a backpedal, the other one's clip (its index after the
    /// <see cref="Bins"/>, from <see cref="First"/>), kept while it is the one playing; −1 elsewhere.
    /// </summary>
    public required int[] Alternates { get; init; }

    /// <summary>For each of the gait's clips from <see cref="First"/>, whether it backpedals (its <c>played_backwards</c>).</summary>
    public required bool[] Backwards { get; init; }

    /// <summary>How many clips the gait has (<see cref="Bins"/> and the alternates).</summary>
    public int Count => Backwards.Length;

    /// <summary>Whether <paramref name="clip"/> is one of this gait's.</summary>
    public bool Holds(int clip) => clip >= First && clip < First + Count;

    /// <summary>Whether two of this gait's clips step the same way (both ahead or both backpedalling): one goes to the other with no fade.</summary>
    public bool Alike(int a, int b) => Holds(a) && Holds(b) && Backwards[a - First] == Backwards[b - First];

    /// <summary>
    /// The clip for a body moving <paramref name="forward"/> along its facing and <paramref name="left"/> across it (m/s, its
    /// left positive): the way's whole degree, 0 to 359 (<paramref name="current"/>, a clip of this gait, when the body is not
    /// moving enough to have a way; and near a halfway line, the current one's side kept).
    /// </summary>
    public int Clip(float forward, float left, int current)
    {
        if (forward * forward + left * left < 1e-8f) return Holds(current) ? current : First;
        int d = (int)MathF.Round(DetMath.Atan2(left, forward) * DetMath.RadToDeg);
        d %= Bins;
        if (d < 0) d += Bins;
        int other = Alternates[d];
        if (other >= 0 && Holds(current) && Backwards[current - First] != Backwards[d] && Backwards[current - First] == Backwards[other])
            return First + other;
        return First + d;
    }

    /// <summary>
    /// The gait's clips for its direction clips (at least two ways): a degree round, named <c>&lt;kind&gt;:&lt;gait&gt;@&lt;degrees&gt;</c>,
    /// then the alternates (<c>…@&lt;degrees&gt;'</c>); null with fewer than two ways.
    /// </summary>
    public static (float[] Headings, GaitBlendClip[] Clips, int[] Alternates)? Blends(string kind, string gait, IReadOnlyList<(INotedClip Clip, float Heading)> ways)
    {
        var sorted = ways.Select(w => (w.Clip, Heading: Round(w.Heading))).GroupBy(w => w.Heading).Select(g => g.First()).OrderBy(w => w.Heading).ToArray();
        if (sorted.Length < 2) return null;
        var clips = new List<GaitBlendClip>(Bins + 32);
        var alternates = new int[Bins];
        Array.Fill(alternates, -1);
        var later = new List<(int Degrees, GaitBlendClip Clip)>();
        for (int d = 0; d < Bins; d++)
        {
            int i = sorted.Length - 1;
            while (i > 0 && sorted[i].Heading > d) i--;
            if (sorted[i].Heading > d) i = sorted.Length - 1;          // before the first way: between the last and the first, round 360
            int j = (i + 1) % sorted.Length;
            float lo = sorted[i].Heading, hi = sorted[j].Heading;
            float at = d;
            if (hi <= lo) { hi += 360f; if (at < lo) at += 360f; }
            float w = (at - lo) / (hi - lo);
            var (a, b) = (sorted[i].Clip, sorted[j].Clip);
            if (w <= 0f || w >= 1f || a.Notes.PlayedBackwards == b.Notes.PlayedBackwards)
            {
                clips.Add(new GaitBlendClip($"{kind}:{gait}@{d}", a, b, w));
                continue;
            }
            // Ahead on one side, backpedalling on the other: the nearer alone, the legs turned by the rest; near halfway, the other too.
            float half = (lo + hi) * 0.5f;
            bool nearer = at < half;
            clips.Add(Alone($"{kind}:{gait}@{d}", nearer ? a : b, (at - (nearer ? lo : hi)) * DetMath.DegToRad));
            if (MathF.Abs(at - half) <= Band)
                later.Add((d, Alone($"{kind}:{gait}@{d}'", nearer ? b : a, (at - (nearer ? hi : lo)) * DetMath.DegToRad)));
        }
        foreach (var (d, clip) in later)
        {
            alternates[d] = clips.Count;
            clips.Add(clip);
        }
        return ([.. sorted.Select(s => s.Heading)], [.. clips], alternates);

        static float Round(float heading)
        {
            float h = heading % 360f;
            return h < 0f ? h + 360f : h;
        }

        static GaitBlendClip Alone(string name, INotedClip clip, float turn) => new(name, clip, clip, 0f) { Turn = turn };
    }
}

/// <summary>
/// Two of a gait's direction clips blended at one weight (<see cref="Gait"/>): phase-synchronised — at a share of its
/// cycle each is sampled at that share of its own — each bone by <see cref="DetMath.Slerp"/> and a linear blend of its
/// translation (<see cref="PoseEvaluator.Crossfade"/>); its cycle the two cycles blended. At weight 0 it is the first clip
/// alone, bit for bit. Allocates nothing (the second pose is on the stack), so it is safe from any thread.
/// </summary>
public sealed class GaitBlendClip : IAnimationClip
{
    readonly INotedClip from, to;
    readonly float weight;

    public GaitBlendClip(string name, INotedClip from, INotedClip to, float weight)
    {
        Name = name;
        this.from = from;
        this.to = to;
        this.weight = Math.Clamp(weight, 0f, 1f);
        Duration = this.weight <= 0f ? from.Duration : from.Duration + (to.Duration - from.Duration) * this.weight;
        float strideFrom = from.Notes.GroundSpeed * from.Duration, strideTo = to.Notes.GroundSpeed * to.Duration;
        float stride = this.weight <= 0f ? strideFrom : strideFrom + (strideTo - strideFrom) * this.weight;
        NaturalSpeed = Duration > 0f ? stride / Duration : 0f;
    }

    public string Name { get; }
    public float Duration { get; }
    public bool Loops => true;

    /// <summary>The clip on the near side and the one on the far side, and the far one's share (0 to 1).</summary>
    public INotedClip From => from;
    public INotedClip To => to;
    public float Weight => weight;

    /// <summary>The ground speed the blend is drawn for (m/s): the two strides (ground speed × cycle) blended, over the cycle.</summary>
    public float NaturalSpeed { get; }

    /// <summary>
    /// How far the legs are turned from the facing while it plays (radians, to the left positive; <see cref="PeopleTurn"/>): 0 for a
    /// blend; for a clip played alone near the halfway line between a forward clip and a backpedal, the rest of the angle
    /// (<see cref="Gait"/>).
    /// </summary>
    public float Turn { get; init; }

    /// <summary>Whether it backpedals (the <c>played_backwards</c> of the clip it mostly is).</summary>
    public bool Backwards => (weight > 0.5f ? to : from).Notes.PlayedBackwards;

    /// <summary><see cref="From"/>'s and <see cref="To"/>'s places in the kind's clips (set when it is registered; −1 before).</summary>
    public int FromClip { get; internal set; } = -1;
    public int ToClip { get; internal set; } = -1;

    public void Sample(float seconds, Skeleton skeleton, Span<BoneTransform> local)
    {
        if (weight <= 0f) { from.Sample(seconds, skeleton, local); return; }
        float d = Duration;
        float p = d > 0f ? seconds / d : 0f;
        p -= MathF.Floor(p);
        if (weight >= 1f) { to.Sample(p * to.Duration, skeleton, local); return; }
        int n = Math.Min(local.Length, skeleton.Count);
        from.Sample(p * from.Duration, skeleton, local[..n]);
        Span<BoneTransform> other = n <= 512 ? stackalloc BoneTransform[n] : new BoneTransform[n];
        to.Sample(p * to.Duration, skeleton, other);
        PoseEvaluator.Crossfade(local[..n], other, weight, local[..n]);
    }
}

/// <summary>
/// The turns on the spot of a kind: <c>turn_left_45</c>, <c>_90</c>,
/// <c>turn_right_45</c>, <c>_90</c>, each played from the stand to the stand, the root bone turning from none to its
/// <c>turn_deg</c> about the vertical (<c>root_turns</c>) while the feet step round. The platform plays them <b>in place</b>
/// (<see cref="TurnClip"/>: the root's turn taken out of the pose) and gives the root's turn to the legs' turn instead, tick by
/// tick (<see cref="PeopleTurn"/>), so the body is never turned twice. To end exactly on the facing, a turn is played for the
/// whole degrees it must make, <see cref="Least"/> to <see cref="Most"/>: up to 67° the 45's steps, from 68° the 90's, its
/// root's turn scaled to that many degrees (a 45 played for 56°: its feet slide the 11° between) — one clip a degree a side
/// (<see cref="Clip"/>), so the pose stays a pure function of the animation's state. A larger turn plays the 90 as made, then
/// what is left.
/// </summary>
public sealed class TurnSet
{
    /// <summary>The fewest and most degrees one turn is played for.</summary>
    public const int Least = 23, Most = 112;

    /// <summary>From how many degrees the 90's steps are played rather than the 45's (halfway between).</summary>
    public const int NinetyFrom = 68;

    /// <summary>Clips a side.</summary>
    public const int PerSide = Most - Least + 1;

    /// <summary>The first turn clip: to the left <see cref="Least"/>° … <see cref="Most"/>°, then to the right the same.</summary>
    public required int First { get; init; }

    /// <summary>Whether the kind has the 45s and the 90s it was made from on each side (a set lacking one plays the other for its degrees).</summary>
    public required bool Complete { get; init; }

    /// <summary>Whether <paramref name="clip"/> is one of these turns.</summary>
    public bool Holds(int clip) => clip >= First && clip < First + 2 * PerSide;

    /// <summary>
    /// The turn for legs that must turn <paramref name="need"/> radians (to the left positive) to face ahead: its whole degrees
    /// (<see cref="Least"/> to <see cref="Most"/>; beyond <see cref="Most"/> the 90 as made, the rest after it).
    /// </summary>
    public int Clip(float need)
    {
        bool left = need > 0f;
        int degrees = (int)MathF.Round(MathF.Abs(need) * DetMath.RadToDeg);
        if (degrees > Most) degrees = 90;
        degrees = Math.Clamp(degrees, Least, Most);
        return First + (left ? 0 : PerSide) + (degrees - Least);
    }

    /// <summary>The fewest degrees a re-aimed turn must differ by to be taken (so a view held still does not flick it between two).</summary>
    public const int RetargetBy = 2;

    /// <summary>
    /// The chain, <see cref="PeopleTurn.StepChain"/>: the turn of the same steps as <paramref name="clip"/> (the same source: the
    /// 45's degrees, <see cref="Least"/> to 67, or the 90's, <see cref="NinetyFrom"/> to <see cref="Most"/>; the same way) whose rest from
    /// <paramref name="seconds"/> on brings legs <paramref name="rel"/> radians off the facing onto it — so a turn begun for the view as
    /// it was ends on the view as it is. Its pose is the same at every time (only the root's turn given is scaled), so it goes on from
    /// the same time with nothing to fade. <paramref name="clip"/> itself when it is nearly over, the legs need the other way, or the
    /// steps cannot be stretched further, or the change is under <see cref="RetargetBy"/>°.
    /// </summary>
    public int Retarget(int clip, TurnClip turning, float seconds, float rel)
    {
        if (!Holds(clip) || MathF.Abs(turning.Turn) < 1e-4f) return clip;
        float done = turning.Yaw(seconds) / turning.Turn;      // the share of its turn given so far
        if (done >= 0.95f || -rel * turning.Turn <= 0f) return clip;
        int index = clip - First, side = index / PerSide, degrees = index % PerSide + Least;
        var (lo, hi) = degrees >= NinetyFrom ? (NinetyFrom, Most) : (Least, NinetyFrom - 1);
        int want = Math.Clamp((int)MathF.Round(MathF.Abs(rel) / (1f - done) * DetMath.RadToDeg), lo, hi);
        return Math.Abs(want - degrees) < RetargetBy ? clip : First + side * PerSide + want - Least;
    }

    /// <summary>
    /// The turn clips for a kind (<see cref="PerSide"/> a side, left first) from its four turns (any may be null; a side with
    /// neither has none, and the set is null), the root bone <paramref name="root"/>.
    /// </summary>
    public static TurnClip[]? Clips(string kind, Skeleton skeleton, int root, INotedClip? left45, INotedClip? left90, INotedClip? right45, INotedClip? right90)
    {
        if ((left45 ?? left90) is null || (right45 ?? right90) is null || (uint)root >= (uint)skeleton.Count) return null;
        var tables = new Dictionary<INotedClip, float[]>(ReferenceEqualityComparer.Instance);
        float[] Table(INotedClip c) => tables.TryGetValue(c, out var t) ? t : tables[c] = TurnClip.RootYaws(c, skeleton, root);
        var clips = new TurnClip[2 * PerSide];
        for (int side = 0; side < 2; side++)
        {
            var (small, large) = side == 0 ? (left45 ?? left90!, left90 ?? left45!) : (right45 ?? right90!, right90 ?? right45!);
            for (int d = Least; d <= Most; d++)
            {
                var source = d >= NinetyFrom ? large : small;
                float want = (side == 0 ? d : -d) * DetMath.DegToRad;
                clips[side * PerSide + d - Least] = new TurnClip($"{kind}:{source.Name}@{(side == 0 ? d : -d)}", source, root, Table(source), want);
            }
        }
        return clips;
    }
}

/// <summary>
/// A turn on the spot played in place (<see cref="TurnSet"/>): the turn clip with its root bone's turn about the vertical
/// taken out of the pose (what is left of the root's rotation, none in the standard set, kept), and that turn, scaled so the
/// whole clip turns <see cref="Turn"/> radians, given by <see cref="Yaw"/> for the legs' turn to take. Played once.
/// </summary>
public sealed class TurnClip : IAnimationClip
{
    readonly INotedClip source;
    readonly int root;
    readonly float[] yaws;   // the root's turn about the vertical at 30 a second through the source (radians, unwrapped), the last at its end
    readonly float scale;

    public TurnClip(string name, INotedClip source, int root, float[] yaws, float turn)
    {
        Name = name;
        this.source = source;
        this.root = root;
        this.yaws = yaws;
        Turn = turn;
        float made = yaws.Length > 0 ? yaws[^1] : 0f;
        scale = MathF.Abs(made) > 1e-4f ? turn / made : 0f;
    }

    public string Name { get; }
    public float Duration => source.Duration;
    public bool Loops => false;

    /// <summary>The clip it plays in place.</summary>
    public INotedClip Source => source;

    /// <summary>How far it turns the legs over its whole length (radians, to the left positive).</summary>
    public float Turn { get; }

    /// <summary>The place in the kind's clips of its source played in place as made (whose tables stand for every one of its degrees; −1 before it is registered).</summary>
    public int PoseClip { get; internal set; } = -1;

    /// <summary>
    /// The clip's own upper-body lead, 30 times a second and at its end (radians about the vertical: the chest's turn from its first
    /// frame, across the shoulders, and the head's from the chest's, across the eyes; <see cref="TurnChain.Lines"/>), shared by the turns
    /// from one source; taken out of the pose while the chain is on (<see cref="TurnChain"/>). Empty before it is registered.
    /// </summary>
    internal float[] ChestLeads { get; set; } = [];
    internal float[] HeadLeads { get; set; } = [];

    /// <summary>The clip's own lead at <paramref name="seconds"/> (<see cref="ChestLeads"/>, <see cref="HeadLeads"/>; radians), between its frames linearly.</summary>
    public (float Chest, float Head) Lead(float seconds)
    {
        if (ChestLeads.Length < 2) return (0f, 0f);
        float d = Duration, f = Math.Clamp(seconds, 0f, d) * 30f;
        int i = Math.Min((int)f, ChestLeads.Length - 2);
        float span = i == ChestLeads.Length - 2 ? MathF.Max(1e-6f, d * 30f - i) : 1f;
        float t = Math.Clamp((f - i) / span, 0f, 1f);
        return (ChestLeads[i] + (ChestLeads[i + 1] - ChestLeads[i]) * t, HeadLeads[i] + (HeadLeads[i + 1] - HeadLeads[i]) * t);
    }

    /// <summary>The root's turn table it reads (shared by the turns from one source).</summary>
    internal float[] Yaws => yaws;

    public void Sample(float seconds, Skeleton skeleton, Span<BoneTransform> local)
    {
        source.Sample(seconds, skeleton, local);
        if ((uint)root < (uint)local.Length) local[root] = local[root] with { Rotation = Swing(local[root].Rotation) };
    }

    /// <summary>The turn it has given the legs by <paramref name="seconds"/> (radians, to the left positive): 0 at its start, <see cref="Turn"/> at its end.</summary>
    public float Yaw(float seconds)
    {
        if (yaws.Length < 2) return 0f;
        float d = Duration;
        if (seconds >= d) return yaws[^1] * scale;
        float f = MathF.Max(0f, seconds) * 30f;
        int i = Math.Min((int)f, yaws.Length - 2);
        // The last step of the table is to the clip's end, which may be less than a thirtieth after the frame before it.
        float span = i == yaws.Length - 2 ? MathF.Max(1e-6f, d * 30f - i) : 1f;
        float t = Math.Clamp((f - i) / span, 0f, 1f);
        return (yaws[i] + (yaws[i + 1] - yaws[i]) * t) * scale;
    }

    /// <summary><paramref name="q"/> less its turn about the vertical (its twist about Z): what is left, its swing.</summary>
    internal static Quaternion Swing(Quaternion q)
    {
        float l = MathF.Sqrt(q.Z * q.Z + q.W * q.W);
        if (l < 1e-9f) return q;
        var twist = new Quaternion(0f, 0f, q.Z / l, q.W / l);
        return LimbIK.Normalize(PoseMath.Multiply(q, LimbIK.Conjugate(twist)));
    }

    /// <summary>The turn about the vertical of <paramref name="q"/> (radians, (−π, π]).</summary>
    internal static float YawOf(Quaternion q) => DetMath.WrapAngle(2f * DetMath.Atan2(q.Z, q.W));

    /// <summary>
    /// The root's turn through <paramref name="clip"/>, 30 times a second and at its end (radians, unwrapped, from 0 at its
    /// start), the table <see cref="Yaw"/> reads.
    /// </summary>
    internal static float[] RootYaws(INotedClip clip, Skeleton skeleton, int root)
    {
        int frames = Math.Max(1, (int)MathF.Ceiling(clip.Duration * 30f - 1e-3f));
        var table = new float[frames + 1];
        var local = new BoneTransform[skeleton.Count];
        float last = 0f, sum = 0f;
        for (int f = 0; f <= frames; f++)
        {
            clip.Sample(MathF.Min(f / 30f, clip.Duration), skeleton, local);
            float yaw = YawOf(local[root].Rotation);
            if (f == 0) last = yaw;
            sum += DetMath.WrapAngle(yaw - last);
            last = yaw;
            table[f] = sum;
        }
        return table;
    }
}
