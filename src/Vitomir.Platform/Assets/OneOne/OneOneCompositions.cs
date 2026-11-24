// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Formats;
using Vitomir.Platform.Animation;

namespace Vitomir.Platform.Assets.OneOne;

/// <summary>
/// One key of a composition: where along the move (0 to 1), a mode of the character's motion set and the frame of its
/// motion (at 1.1's 30 a second), the hips' height to give it (a frame of a fall keeps its pose but not where the fall
/// had got to), a lean forwards (radians), and whether the legs are the stand's (a falling frame's legs are drawn up).
/// </summary>
public sealed record CompositionKey(float At, string Mode, float Frame, float? RootHeight = null, float Lean = 0f, bool StandingLegs = false);

/// <summary>A composition for one slot: its keys, and whether it loops (a crouch) or is played by a move's progress.</summary>
public sealed record Composition(MotionRole Role, bool Loops, IReadOnlyList<CompositionKey> Keys);

/// <summary>
/// The stand-ins for the moves' motion slots, made of 1.1's own poses (ours):
/// 1.1 never jumped, crouched or climbed, so for each slot a few frames of Christof's own motions — his
/// stand, run, his kneel and his pick-up, the perch at the window, the drop through the floor — are blended along
/// the move, and the <see cref="PoseRig"/> then puts the hands on the ledge and the feet on the top by IK. Chosen by
/// eye ("the movements don't need to be pretty but they need to roughly match what's going on"); a real motion in a
/// slot (a real motion) replaces its composition with no change to the moves. A mode the character's own
/// motion set lacks is its stand's first frame.
/// </summary>
public static class OneOneCompositions
{
    /// <summary>The compositions, by slot. Modes are Christof's (<c>Christof.nms</c>); the townsfolk's sets have few of them.</summary>
    public static readonly IReadOnlyList<Composition> Slots =
    [
        // Crouched and still: his perch on the window sill (special2, Christof_WindowPerch), hips at 0.6 m.
        new(MotionRole.Crouch, true, [new(0f, "special2", 30f)]),
        // A hop up: a dip of the knees (his pick-up's first bend), up with the knees drawn (the kneel's bend), down.
        new(MotionRole.HopUp, false, [new(0f, "stand", 0f), new(0.3f, "action5", 16f), new(0.6f, "action3", 24f, 1.0f), new(1f, "stand", 0f)]),
        // A hurdle: the run's longest stride, held over the top.
        new(MotionRole.Hurdle, false, [new(0f, "run", 4f), new(0.5f, "run", 8f), new(1f, "run", 15f)]),
        // A vault: crouched over the top, the hands on it (IK).
        new(MotionRole.Vault, false, [new(0f, "stand", 0f), new(0.45f, "action5", 16f), new(1f, "stand", 0f)]),
        // Climbing onto: arms up to the edge, bent over it with a knee up (the kneel), standing on the top.
        new(MotionRole.ClimbOnto, false, [new(0f, "action9", 12f, 1.04f), new(0.45f, "action3", 32f, 0.9f), new(0.8f, "action5", 16f), new(1f, "stand", 0f)]),
        // A mantle: arms up (the drop's reach), pulled up and over (the kneel's bend), standing on the top.
        new(MotionRole.Mantle, false, [new(0f, "action9", 24f, 1.04f), new(0.4f, "action9", 24f, 1.04f), new(0.65f, "action3", 32f, 0.9f), new(1f, "stand", 0f)]),
        // The grab: from the air, arms up to the ledge.
        new(MotionRole.Grab, false, [new(0f, "action9", 12f, 1.04f), new(0.4f, "action9", 24f, 1.04f), new(1f, "action9", 24f, 1.04f, StandingLegs: true)]),
        // Hanging: the drop through the floor's straight body and raised arms, the legs hanging as he stands.
        new(MotionRole.Hang, false, [new(0f, "action9", 24f, 1.04f, StandingLegs: true)]),
        // The pull-up: hanging, up over the edge with a knee up, standing on the top.
        new(MotionRole.PullUp, false, [new(0f, "action9", 24f, 1.04f, StandingLegs: true), new(0.45f, "action3", 32f, 0.9f), new(0.8f, "action5", 16f), new(1f, "stand", 0f)]),
        // Letting go: hanging, then the arms coming down as he falls.
        new(MotionRole.Drop, false, [new(0f, "action9", 24f, 1.04f, StandingLegs: true), new(0.5f, "action9", 16f, 1.0f), new(1f, "stand", 0f)]),
    ];

    /// <summary>
    /// The composed clips for a character with this motion set and skeleton (one per slot of <see cref="Slots"/>),
    /// <paramref name="motion"/> finding a mode's clip (<see cref="OneOneLibrary.Motion"/>), <paramref name="stand"/>
    /// standing in for a mode the set lacks. Only the body is taken from each frame — the hips, the limbs, the spine
    /// and the head (every bone from a hand, a foot or the head up to the hips, and those below them); the rest (the
    /// cape, the tunic's flaps, a scabbard) keeps the stand's first frame, so a frame of a fall or a spin does not
    /// throw the cape about.
    /// </summary>
    public static IEnumerable<(MotionRole Role, ComposedClip Clip)> For(string character, NmsMotionSet set, Skeleton skeleton, LimbMap limbs, int head,
                                                                      Func<NmsMotionSet, string, IAnimationClip?> motion, IAnimationClip stand)
    {
        int n = skeleton.Count;
        var body = new bool[n];
        void Up(int b) { for (; b >= 0 && b < n; b = skeleton.Bones[b].Parent is int p && p < b ? p : -1) body[b] = true; }
        foreach (var limb in new[] { limbs.LeftLeg, limbs.RightLeg, limbs.LeftArm, limbs.RightArm })
            Up(limb.Tip >= 0 ? limb.Tip : limb.End);
        Up(head);
        Up(limbs.Root);
        var below = new bool[n];         // below a hand, a foot or the head: the fingers, a held weapon, the jaw
        int[] ends = [head, limbs.LeftArm.End, limbs.RightArm.End, limbs.LeftLeg.End, limbs.RightLeg.End];
        for (int b = 0; b < n; b++)
        {
            int p = skeleton.Bones[b].Parent;
            if (p >= 0 && p < b && (Array.IndexOf(ends, p) >= 0 || below[p])) below[b] = true;
        }
        for (int b = 0; b < n; b++) body[b] |= below[b];

        var still = new BoneTransform[n];
        stand.Sample(0f, skeleton, still);
        foreach (var slot in Slots)
        {
            var keys = new List<ComposedKey>(slot.Keys.Count);
            foreach (var k in slot.Keys)
            {
                var clip = motion(set, k.Mode);
                float seconds = clip is null ? 0f : MathF.Min(k.Frame / NadClip.FramesPerSecond, clip.Duration);
                var key = ComposedClip.Key(k.At, clip ?? stand, seconds, skeleton, limbs.Root, clip is null ? null : k.RootHeight, k.Lean);
                for (int b = 0; b < n; b++)
                    if (!body[b]) key.Pose[b] = still[b];
                if (k.StandingLegs)
                    foreach (var leg in new[] { limbs.LeftLeg, limbs.RightLeg })
                        foreach (var b in new[] { leg.Upper, leg.Lower, leg.End, leg.Tip })
                            if (b >= 0) key.Pose[b] = still[b];
                keys.Add(key);
            }
            yield return (slot.Role, new ComposedClip($"{character}:{slot.Role}", 1f, slot.Loops, keys));
        }
    }
}
