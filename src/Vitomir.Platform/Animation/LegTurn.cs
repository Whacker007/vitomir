// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours ("For Third Person mode, I want the player to always be turning to face the direction
// the camera is going. So WASD would be moving forward, sidestepping left and right, or backpedalling."). The shape is the
// third-person shooters' and the melee games' with the body on the camera (recalled, not looked up): the lower body turned
// toward the movement within about ±45 to ±70°, the spine turned back so the chest stays on the aim; a backpedal as the
// forward clip played backwards; standing, the feet planted until the twist passes a limit, then a turn in place.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Animation;

/// <summary>
/// The legs' turn's numbers (<b>ENGINE_SANDBOX</b>, ours, looked at by eye on Christof until the models repository gives side
/// and back motions). Angles in radians, rates in radians a second.
/// </summary>
public sealed record LegTurnSettings
{
    /// <summary>
    /// Whether the legs turn at all (on by default; the setting <c>legTurn</c> and <c>--no-leg-turn</c> turn it off, for the
    /// arena's "unchanged": off, every pose is master's).
    /// </summary>
    public bool On { get; init; } = true;

    /// <summary>
    /// The most the hips and legs are turned toward the way the body moves (60°): a sidestep at a right angle to the facing
    /// has the legs 60° round and the walk or run 30° off its line, the chest still on the view. Games keep it within about
    /// 45° to 70° (a spine twisted further reads as broken).
    /// </summary>
    public float MostTurn { get; init; } = 60f * DetMath.DegToRad;

    /// <summary>How fast the legs follow the way the body moves (360° a second: a change of the move keys swings them in a sixth of a second or so, never a pop).</summary>
    public float Rate { get; init; } = 360f * DetMath.DegToRad;

    /// <summary>
    /// Standing: the feet stay planted while the chest follows the view, until the twist between them passes this (50°);
    /// then the legs turn to catch the chest up (<see cref="CatchUpRate"/>), all the way round.
    /// </summary>
    public float StandTwist { get; init; } = 50f * DetMath.DegToRad;

    /// <summary>How fast a standing body's legs catch up once the twist passed <see cref="StandTwist"/> (270° a second: 50° in about a fifth of a second, a short shuffle's length).</summary>
    public float CatchUpRate { get; init; } = 270f * DetMath.DegToRad;

    /// <summary>The most the spine is ever twisted against the legs (90°): a view turned faster than the legs follow drags them round with it.</summary>
    public float MostTwist { get; init; } = 90f * DetMath.DegToRad;

    /// <summary>
    /// A walk or run is drawn backwards — and the legs turned toward the reverse of the movement — when the body moves more
    /// than about 105° from its facing: its speed along the facing below this share of its speed over the ground, negated
    /// (the animation's own rule, <see cref="AnimationSettings.BackwardsShare"/>, so the clip and the legs agree).
    /// </summary>
    public float BackwardsShare { get; init; } = 0.25f;

    /// <summary>
    /// <b>SANDBOX</b>: a person standing (<see cref="People.PeopleTurn"/>) whose legs the view has left behind by more
    /// than this turns on the spot with the models repository's turn clips, rather than catching up at <see cref="CatchUpRate"/>.
    /// <i>"I'd suggest lowering it to 30"</i>: 30°; the earlier build's was 45° (<see cref="NoChain"/>). The
    /// setting <c>turnFrom</c>, <c>--turn-from</c>.
    /// </summary>
    public float PersonTurnFrom { get; init; } = 30f * DetMath.DegToRad;

    /// <summary>
    /// <b>SANDBOX</b>: at the end of a person's turn on the spot, a twist still beyond this (20°: the view moved on during
    /// the turn, or the turn needed more than one) plays the next turn; less is left planted. The setting <c>turnOn</c>, <c>--turn-on</c>.
    /// </summary>
    public float PersonTurnOn { get; init; } = 20f * DetMath.DegToRad;

    // ---- the turn as a chain that catches up (People.PeopleTurn) ----

    /// <summary>
    /// A person's head, chest and hips each chase the view in turn at limited rates (<see cref="People.PeopleTurn"/>, the chain);
    /// false is the earlier build's turn exactly (the chest on the view at once, the legs held by <see cref="MostTwist"/>; with
    /// <see cref="NoChain"/>'s 45° start): <c>--turn-chain-off</c>, the setting <c>turnChainOff</c>. 1.1's bodies are not people: theirs is unchanged.
    /// </summary>
    public bool Chain { get; init; } = true;

    /// <summary><b>SANDBOX</b>: the fastest the head turns after the view (300° a second: the studies' fast turns peak at 124–272°/s). <c>turnHeadRate</c>.</summary>
    public float HeadRate { get; init; } = 300f * DetMath.DegToRad;

    /// <summary><b>SANDBOX</b>: how far behind a view turned steadily the head trails, the chase's lag (0.03 s: the eyes lead, the head straight after). <c>turnHeadLag</c>.</summary>
    public float HeadLag { get; init; } = 0.03f;

    /// <summary><b>SANDBOX</b>: the most the head is ever turned ahead of the chest (25°: the head leads the thorax by about 20° at the peak of a 90° turn, Khobkhun et al. 2022; an earlier build lane A had 45°); further, it waits there for the chest. <c>turnNeckMost</c>.</summary>
    public float NeckMost { get; init; } = 25f * DetMath.DegToRad;

    /// <summary><b>SANDBOX</b>: the fastest the chest turns after the head (200° a second). <c>turnChestRate</c>.</summary>
    public float ChestRate { get; init; } = 200f * DetMath.DegToRad;

    /// <summary><b>SANDBOX</b>: how far the chest trails the head turned steadily (0.06 s: the studies' thorax 50–60 ms after the head). <c>turnChestLag</c>.</summary>
    public float ChestLag { get; init; } = 0.06f;

    /// <summary><b>SANDBOX</b>: the most the chest is ever turned ahead of the hips (30°); further, it waits there for the hips. <c>turnSpineMost</c>.</summary>
    public float SpineMost { get; init; } = 30f * DetMath.DegToRad;

    /// <summary>
    /// <b>SANDBOX</b>: a turn on the spot plays faster the further the hips are behind the view: at its own rate with them
    /// <see cref="PersonTurnFrom"/> behind, up to this many times it (2) with them <see cref="PivotFrom"/> behind or more. <c>turnMostSpeedup</c>.
    /// </summary>
    public float TurnMostSpeedup { get; init; } = 2f;

    /// <summary>
    /// <b>SANDBOX</b>: past what the turn clips can follow — the hips more than this behind the view (75°) — the legs pivot on
    /// their own as well, at <see cref="PivotRate"/>, so the body is never left far behind. <c>turnPivotFrom</c>.
    /// </summary>
    public float PivotFrom { get; init; } = 75f * DetMath.DegToRad;

    /// <summary><b>SANDBOX</b>: how fast the legs pivot on their own past <see cref="PivotFrom"/> (240° a second). <c>turnPivotRate</c>.</summary>
    public float PivotRate { get; init; } = 240f * DetMath.DegToRad;

    // ---- the waist that came loose, and the leash ("a is good") ----

    /// <summary>
    /// <b>SANDBOX</b>: the most the hips are ever behind the view (90°): past it the whole body is carried round
    /// with the view, the legs held at the leash (their feet let go of their locks and turn with them). Normal turns never reach it (the
    /// legs pivot from <see cref="PivotFrom"/>); it holds the body square-on at worst when the view is spun for seconds. 0 or 180° (or
    /// more) is off, the earlier build's (the hips up to half a turn behind). <c>turnLeash</c>, <c>--turn-leash</c>; in the tape's note
    /// (<c>leash=90</c>; a note without it is the earlier build's, off).
    /// </summary>
    public float Leash { get; init; } = 90f * DetMath.DegToRad;

    /// <summary>Whether <see cref="Leash"/> holds the hips (between 0 and half a turn).</summary>
    public bool Leashed => Leash > 0f && Leash < DetMath.Pi;

    /// <summary>
    /// The chain's waist as before (<see cref="People.TurnChain"/>): the chest's turn over the hips split evenly over the spine and
    /// the spine then moved at its first bone so its top stayed where the unturned pose had it, which slid the trunk off the hips at the
    /// belt by up to 21 cm on a play-test's tape (the upper torso looked disconnected from the lower body, at the waist). False, the earlier build's: the turn
    /// spread over the spine mostly in the chest, the spine left on the pelvis. <c>turnWaistEven</c>, <c>--turn-waist-even</c>; in the
    /// tape's note (<c>waist=1</c> is the earlier build's; a note without it is the earlier build's).
    /// </summary>
    public bool WaistEven { get; init; }

    /// <summary>The earlier build's turn of a person, exactly (<c>--turn-chain-off</c>): no chain, the turn on the spot from 45°.</summary>
    public static LegTurnSettings NoChain => new() { Chain = false, PersonTurnFrom = 45f * DetMath.DegToRad };

    /// <summary>
    /// The turn's numbers as a tape's note (<c>turn</c>): <c>an earlier build …</c> for the earlier build's turn, else <c>chain</c> and the chain's numbers
    /// in degrees, degrees a second and seconds (<c>chain head=300 headLag=0.03 neck=45 …</c>); then <c>leash=90</c> (0 off) and
    /// <c>waist=1</c> (the earlier build's waist). A tape without the note was made on the earlier build's turn (<see cref="WithNote"/>).
    /// </summary>
    public string Note()
    {
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        if (!Chain) return string.Create(ic, $"nochain from={D(PersonTurnFrom)} on={D(PersonTurnOn)}");
        return string.Create(ic,
            $"chain head={D(HeadRate)} headLag={HeadLag:0.####} neck={D(NeckMost)} chest={D(ChestRate)} chestLag={ChestLag:0.####} spine={D(SpineMost)} from={D(PersonTurnFrom)} on={D(PersonTurnOn)} speedup={TurnMostSpeedup:0.####} pivotFrom={D(PivotFrom)} pivotRate={D(PivotRate)} leash={D(Leashed ? Leash : 0f)} waist={(WaistEven ? 0 : 1)}");

        static string D(float radians) => (radians * DetMath.RadToDeg).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// These settings with a tape's note <c>turn</c> (<see cref="Note"/>) laid on; no note is the earlier build's turn (a tape made earlier:
    /// <see cref="NoChain"/>'s). A number the note lacks keeps this one's. The degrees go through the note's text, so a replay's numbers
    /// are the note's rounded to 1e-4°, as a recording's own run had them (a session takes its numbers through the note too). a note
    /// without <c>leash</c> or <c>waist</c> is the leash off and the earlier build's waist, so a tape replays as it was made.
    /// </summary>
    public LegTurnSettings WithNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note)) return this with { Chain = false, PersonTurnFrom = NoChain.PersonTurnFrom, PersonTurnOn = NoChain.PersonTurnOn, Leash = 0f, WaistEven = true };
        var parts = note.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var s = this with { Chain = parts[0] == "chain", Leash = 0f, WaistEven = true };
        foreach (var part in parts.Skip(1))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0 || !float.TryParse(part[(eq + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) continue;
            float r = v * DetMath.DegToRad;
            s = part[..eq] switch
            {
                "head" => s with { HeadRate = r }, "headLag" => s with { HeadLag = v }, "neck" => s with { NeckMost = r },
                "chest" => s with { ChestRate = r }, "chestLag" => s with { ChestLag = v }, "spine" => s with { SpineMost = r },
                "from" => s with { PersonTurnFrom = r }, "on" => s with { PersonTurnOn = r }, "speedup" => s with { TurnMostSpeedup = v },
                "pivotFrom" => s with { PivotFrom = r }, "pivotRate" => s with { PivotRate = r },
                "leash" => s with { Leash = r }, "waist" => s with { WaistEven = v == 0f },
                _ => s,
            };
        }
        return s;
    }
}

/// <summary>Where a body's legs' turn is (<see cref="PoseRig.LegMode"/>).</summary>
public enum LegMode : byte
{
    /// <summary>Not yet seen: the legs start on the body's facing.</summary>
    Unset = 0,
    /// <summary>Following: the way the body moves, or planted while it stands.</summary>
    Follow = 1,
    /// <summary>Standing, the twist passed its limit: turning to catch the chest up.</summary>
    CatchUp = 2,
}

/// <summary>
/// <b>The legs' turn</b> (ours). A body whose facing is not the way it moves (the player in third person facing the
/// view, as in first person; a duelling brain circling its foe) has its <b>hips and legs turned toward the way it moves</b>
/// by up to <see cref="LegTurnSettings.MostTurn"/>, and its <b>spine turned back</b> by the same, split over the spine bones
/// as the aim offset splits its pitch (<see cref="AimOffset"/>), so the chest — the attacks, the block, the reticle's aim —
/// stays on the facing. Moving backwards (past the animation's backwards rule) the legs are turned toward the reverse of the
/// movement and the walk or run plays backwards, as before. The turn is eased (<see cref="LegTurnSettings.Rate"/>), so a
/// change of the move keys never pops. <b>Standing</b>, the chest follows the facing at once while the legs keep their place
/// in the world until the twist passes <see cref="LegTurnSettings.StandTwist"/>; then they turn to catch it up (a turn rate,
/// till the models repository's turn-in-place motion). Off the ground, in a move or a dodge, the legs ease back to the facing.
/// <para>
/// The body's simulated yaw is unchanged (what faces, attacks and is measured); only the drawn pose turns — and since the hit
/// volumes ride on the drawn pose (<see cref="CharacterPoses"/>), they turn with it. The turn is kept in the
/// <see cref="PoseRig"/> (<see cref="PoseRig.LegYaw"/>, digested) by <see cref="LegTurnSystem"/> on the simulation's tick and
/// laid on the pose by <see cref="PoseEvaluator"/> (<see cref="Apply"/>) before the limbs are placed, so the feet's IK and
/// ground keep to the turned legs.
/// </para>
/// </summary>
public static class LegTurn
{
    /// <summary>
    /// One tick of a body's legs' turn: the rig's <see cref="PoseRig.LegYaw"/> moved on from how the body moves
    /// (<paramref name="input"/>: its speeds along and across its facing), the motion it plays (<paramref name="role"/>)
    /// and its yaw now (<paramref name="yaw"/>; the legs keep their place in the world as it turns).
    /// </summary>
    public static void Step(ref PoseRig rig, MotionRole role, in MotionInput input, float yaw, LegTurnSettings s, float seconds)
    {
        if (!s.On)
        {
            rig.LegYaw = 0f;
            rig.LegFrom = 0f;
            rig.LegMode = LegMode.Unset;
            return;
        }
        if (rig.LegMode == LegMode.Unset)
        {
            rig.LegYaw = 0f;
            rig.LegFrom = yaw;
            rig.LegMode = LegMode.Follow;
        }
        // The legs keep their place in the world as the body turns: the turn of the body this tick taken off them.
        float rel = DetMath.WrapAngle(rig.LegYaw - DetMath.WrapAngle(yaw - rig.LegFrom));
        rig.LegFrom = yaw;

        bool grounded = input.Grounded && input.Traversal == TraversalKind.None && !input.Dodging;
        bool moving = grounded && role is MotionRole.Walk or MotionRole.Run or MotionRole.CrouchWalk;
        float target, rate = s.Rate;
        if (!grounded) { target = 0f; rig.LegMode = LegMode.Follow; }
        else if (moving)
        {
            rig.LegMode = LegMode.Follow;
            target = Toward(input.ForwardSpeed, input.SideSpeed, input.GroundSpeed, s);
        }
        else if (rig.LegMode == LegMode.CatchUp || MathF.Abs(rel) > s.StandTwist)
        {
            rig.LegMode = LegMode.CatchUp;
            target = 0f;
            rate = s.CatchUpRate;
        }
        else target = rel;      // planted

        rel = Towards(rel, target, rate * seconds);
        rel = Math.Clamp(rel, -s.MostTwist, s.MostTwist);
        if (rig.LegMode == LegMode.CatchUp && rel == 0f) rig.LegMode = LegMode.Follow;
        rig.LegYaw = rel;
    }

    /// <summary>
    /// The legs' turn wanted for a body moving at <paramref name="forward"/> along its facing and <paramref name="left"/>
    /// across it (m/s, to its left positive), <paramref name="ground"/> over the ground: toward the movement, within
    /// <see cref="LegTurnSettings.MostTurn"/>; toward its reverse when it moves backwards (the clip then plays backwards).
    /// </summary>
    public static float Toward(float forward, float left, float ground, LegTurnSettings s)
    {
        if (ground <= 1e-4f) return 0f;
        float way = DetMath.Atan2(left, forward);
        if (forward < -s.BackwardsShare * ground) way = DetMath.WrapAngle(way + DetMath.Pi);
        return Math.Clamp(way, -s.MostTurn, s.MostTurn);
    }

    static float Towards(float value, float target, float step) =>
        value < target ? MathF.Min(target, value + step) : MathF.Max(target, value - step);

    /// <summary>
    /// Lays a turn of <paramref name="yaw"/> radians (counter-clockwise seen from above, toward the body's left) on
    /// <paramref name="local"/>: the hips (<paramref name="root"/>, and every bone under them) turned about the model's up, and
    /// the spine turned back by the same, split evenly over <paramref name="spine"/>, each bone about the model's up as its
    /// parent sees it, so the last spine bone is turned as it was; and the spine moved (at its first bone) so the last one's
    /// root is where it was — on 1.1's skeleton the spine springs from off the hips' pivot and leans, so the turned hips and the
    /// split turn would carry the chest some 7 cm aside at 60° — so the legs point the turned way and the chest, the arms and
    /// the head are exactly where they were (the waist takes up the difference). <paramref name="scratch"/> (at least the
    /// skeleton's bone count) is worked in.
    /// </summary>
    public static void Apply(Skeleton skeleton, int root, ReadOnlySpan<int> spine, float yaw, Span<BoneTransform> local, Span<Matrix4x4> scratch)
    {
        if (yaw == 0f || spine.Length == 0 || (uint)root >= (uint)local.Length) return;
        for (int i = 0; i < spine.Length; i++) if ((uint)spine[i] >= (uint)local.Length) return;
        int n = skeleton.Count;
        int first = spine[0], last = spine[^1];
        PoseEvaluator.ToModel(skeleton, local[..n], scratch[..n]);
        var was = scratch[last].Translation;
        Turn(skeleton, root, yaw, local);
        float share = -yaw / spine.Length;
        for (int i = 0; i < spine.Length; i++) Turn(skeleton, spine[i], share, local);
        PoseEvaluator.ToModel(skeleton, local[..n], scratch[..n]);
        var moved = was - scratch[last].Translation;
        int parent = skeleton.Bones[first].Parent;
        if (parent >= 0 && parent < first)
            moved = LimbIK.Rotate(LimbIK.Conjugate(LimbIK.ModelRotation(skeleton, local, parent)), moved);
        local[first] = local[first] with { Translation = local[first].Translation + moved };
    }

    /// <summary>Turns bone <paramref name="b"/> by <paramref name="angle"/> about the model's up as its parent sees it.</summary>
    internal static void Turn(Skeleton skeleton, int b, float angle, Span<BoneTransform> local)
    {
        int parent = skeleton.Bones[b].Parent;
        var seen = parent >= 0 && parent < b ? LimbIK.Rotate(LimbIK.Conjugate(LimbIK.ModelRotation(skeleton, local, parent)), Vector3.UnitZ) : Vector3.UnitZ;
        seen = LimbIK.Unit(seen);
        var (s, c) = DetMath.SinCos(angle * 0.5f);
        var turn = new Quaternion(seen.X * s, seen.Y * s, seen.Z * s, c);
        local[b] = local[b] with { Rotation = LimbIK.Normalize(PoseMath.Multiply(turn, local[b].Rotation)) };
    }

    /// <summary>The knees' forward hint for the legs' IK (model space, the model facing +Y) turned by the legs' turn.</summary>
    public static Vector3 Knee(float yaw)
    {
        if (yaw == 0f) return new Vector3(0f, 1f, 0f);
        var (s, c) = DetMath.SinCos(yaw);
        return new Vector3(-s, c, 0f);
    }
}

/// <summary>
/// Moves every posed body's legs' turn on by one tick (<see cref="LegTurn.Step"/>): after the animation chose its motion
/// and before the rig places the limbs (<see cref="AnimationRegistration.AnimationOrder"/> + <see cref="Order"/>), for each
/// character with a body, a rig and a kind with hips and a spine. Allocates nothing.
/// </summary>
public sealed class LegTurnSystem(CharacterAssets assets, LegTurnSettings settings, float fade = 0.2f) : ISystem
{
    // Fade — the animation's fade (AnimationSettings.FadeSeconds), for a person's turn on the spot begun or ended here.
    /// <summary>The name it is registered under, and its place after the animation (before the rig's <see cref="PoseRigSystem.Order"/>).</summary>
    public const string Name = "platform.leg-turn";
    public const int Order = 5;

    int bodiesKnown, chainsKnown;

    public LegTurnSettings Settings => settings;

    public void Run(World world)
    {
        var rigs = world.Store<PoseRig>();
        var animations = world.Store<CharacterAnimation>();
        var inputs = world.Store<MotionInput>();
        var bodies = PoseRigSystem.Optional<Body>(world, ref bodiesKnown);
        if (bodies is null) return;
        // The people's turn as a chain, when the world has its store (people in it and the chain on).
        var chains = settings.Chain ? PoseRigSystem.Optional<People.PersonTurn>(world, ref chainsKnown) : null;
        var data = rigs.Data;
        var ents = rigs.Entities;
        for (int i = 0; i < data.Length; i++)
        {
            var e = ents[i];
            ref var anim = ref animations.Find(e);
            ref var body = ref bodies.Find(e);
            if (Unsafe.IsNullRef(ref anim) || Unsafe.IsNullRef(ref body)) continue;
            var kind = assets.Kind(anim.Kind);
            if (kind.Person is { } person && kind.Limbs is not null)
            {
                // A person's legs (their turns on the spot; the eight directions' clips turn the hips while moving).
                ref var personInput = ref inputs.Find(e);
                var personMotion = Unsafe.IsNullRef(ref personInput) ? MotionInput.Standing : personInput;
                if (chains is not null)
                {
                    if (!chains.Has(e)) world.Set(e, new People.PersonTurn());
                    People.PeopleTurn.StepChain(ref data[i], ref anim, ref chains.Get(e), assets, kind, person, in personMotion, body.Yaw, settings, World.TickSeconds, fade);
                }
                else People.PeopleTurn.Step(ref data[i], ref anim, assets, kind, person, in personMotion, body.Yaw, settings, World.TickSeconds, fade);
                continue;
            }
            if (kind.Limbs is null || kind.Actions is not { Spine.Length: > 0 }) continue;
            ref var input = ref inputs.Find(e);
            var motion = Unsafe.IsNullRef(ref input) ? MotionInput.Standing : input;
            LegTurn.Step(ref data[i], anim.Role, in motion, body.Yaw, settings, World.TickSeconds);
        }
    }
}
