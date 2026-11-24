// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Formats;
using Vitomir.Platform.Animation;

namespace Vitomir.Platform.Assets.OneOne;

/// <summary>
/// A stitched blow made for one kind, before it is handed to the simulation (<see cref="StitchedMotion"/> without the clip's index):
/// The earlier build's <see cref="StitchedClip"/>, or the earlier build's blow with its wind-up (<see cref="WindUpClip"/>, <see cref="OneOneWindUps"/>).
/// </summary>
public sealed record OneOneStitchedBlow(string Direction, IAnimationClip Clip, float Rate, float StrikeSeconds, float BusySeconds, float ReadySeconds, string From);

/// <summary>
/// The overhead and the thrust for a one-handed blade, stitched from 1.1's own clips (no new
/// motions are made).
/// In short: 1.1 has no one-handed overhead and no one-handed thrust for a sword on any skeleton (its one-handed
/// swords swing only the forehand, the backhand and the whirl; Serena's, Samuel's and Erik's blades are side cuts or the
/// same two-handed blows on other skeletons), but Christof's own skeleton — 1.1's skeleton 0, which every sword-bearing human
/// the platform plays shares, bone for bone (Christof, <c>rudolfGuard</c>, <c>KnightOfStJohn_torch</c>,
/// <c>viennaGuard_sword</c>: the same 40 bones, the same parents, the same rest translations) — has a clean
/// <b>two-handed overhead</b> (the war hammer's <c>chrisAttackWarHammer</c>: the weapon cocked over the right shoulder, raised
/// over the head, brought straight down in the body's own vertical plane) and a clean <b>thrust</b> (the halberd's
/// <c>chrisAttackHalberd</c>: drawn back to the hip, driven forward 1.5 m with a lunge, the point leading). Each is played on
/// the kind as it is (the same skeleton: the bone map is the identity) with:
/// <list type="bullet">
/// <item><b>The off hand taken off the grip</b>: the left arm (the left shoulder and every bone under it: the hand, and what
/// the left hand or forearm holds — a shield, a torch) from the kind's own stand, so a shield or a torch stays where the
/// stance carries it and the arm matches the stance the blow begins and ends in.</item>
/// <item>The overhead's <b>bow kept shallower</b>: the hammer's smash bows the body 47° at its strike and 62° after it, the
/// hips 0.31 m lower, the head down at the waist; everything but the arms is blended halfway (<see cref="OverheadBowKept"/>)
/// back toward the stand from the top of the swing to its recovery, so the sword's chop ends at the belly, not the ground.</item>
/// </list>
/// Each plays as 1.1 plays the carried weapon's quick blow (the rate that makes the clip last its first attack's
/// <c>STRIKETIME</c>, busy for its <c>RELOADTIME</c>), with the source clip's own <c>Strike</c> mark. Kinds of another
/// skeleton, two-handed weapons and bare hands get none (say so: <see cref="Why"/>).
/// </summary>
public static class OneOneStitched
{
    /// <summary>The 1.1 clip the overhead is stitched from (the war hammer's two-handed overhead; <c>Christof.nms</c> mode 71 <c>bluntbash2</c>, 34 frames, <c>Strike</c> at 15).</summary>
    public const string OverheadFrom = "chrisAttackWarHammer";

    /// <summary>The 1.1 clip the thrust is stitched from (the halberd's thrust; <c>Christof.nms</c> mode 84 <c>polethrust</c>, 29 frames, <c>Strike</c> at 16).</summary>
    public const string ThrustFrom = "chrisAttackHalberd";

    /// <summary>
    /// How much of the hammer's bow the overhead keeps after the top of its swing (SANDBOX, ours): 0.5 — everything but the
    /// arms blended halfway toward the stand from frame 11 (the top) to frame 15 (the strike), held to 24, back by 32.
    /// </summary>
    public const float OverheadBowKept = 0.5f;

    /// <summary>The overhead's bow layer's keys (frames of <see cref="OverheadFrom"/>, SANDBOX): the blend toward the stand rises from the top of the swing to the strike, holds through the follow-through, falls through the recovery.</summary>
    public static readonly float[] OverheadBowFrames = [11f, 15f, 24f, 32f];

    /// <summary>
    /// The ready points (frames of the source clip, SANDBOX, ours): where a held blow waits (the direction work's ready and release)
    /// — the wind-up done, just before the active window: the overhead's sword raised over the head (frame 11;
    /// the window opens at 11.4 with the broadsword), the thrust's hand drawn back to the hip with the point forward (frame 13;
    /// the window opens at 13.9).
    /// </summary>
    public const float OverheadReadyFrame = 11f, ThrustReadyFrame = 13f;

    /// <summary>Christof's skeleton's parents (1.1's skeleton 0), the family these blows are made for: its bone map is the identity.</summary>
    public static readonly int[] FamilyParents = [-1, 0, 1, 2, 3, 0, 5, 6, 7, 0, 9, 10, 0, 12, 0, 14, 15, 16, 17, 18, 18, 16, 21, 22, 23, 23, 16, 26, 27, 28, 27, 16, 31, 32, 33, 32, 35, 32, 37, 0];

    /// <summary>Whether a skeleton is of Christof's family (its bones and parents his, in order).</summary>
    public static bool OfFamily(Skeleton skeleton)
    {
        if (skeleton.Count != FamilyParents.Length) return false;
        for (int b = 0; b < FamilyParents.Length; b++)
            if (skeleton.Bones[b].Parent != FamilyParents[b]) return false;
        return true;
    }

    /// <summary>Why a kind gets no stitched blows (empty when it gets them).</summary>
    public static string Why(Skeleton skeleton, LimbMap? limbs, OneOneWeapon? weapon, IAnimationClip? stand)
    {
        if (weapon is null || weapon.Carry != 1) return "not a one-handed weapon";
        if (!OfFamily(skeleton)) return "not of Christof's skeleton";
        if (limbs is null || !limbs.LeftArm.Has || !limbs.RightArm.Has) return "no arms found";
        if (stand is null) return "no stand";
        return "";
    }

    /// <summary>
    /// The stitched blows for a kind with this skeleton, limbs, stand and weapon (<paramref name="clip"/> loading a 1.1 clip by
    /// name, not looping), or none (<see cref="Why"/>).
    /// </summary>
    public static IReadOnlyList<OneOneStitchedBlow> For(Skeleton skeleton, LimbMap? limbs, OneOneWeapon? weapon, IAnimationClip? stand, Func<string, NadClip?> clip, int spineFirst = 14,
                                                         HeldThing? blade = null, int head = -1)
    {
        if (Why(skeleton, limbs, weapon, stand).Length > 0) return [];
        var offHand = Subtree(skeleton, limbs!.LeftArm.Upper);
        var weaponArm = Subtree(skeleton, limbs.RightArm.Upper);
        var body = new bool[skeleton.Count];
        for (int b = 0; b < body.Length; b++) body[b] = !offHand[b] && !weaponArm[b];
        var blows = new List<OneOneStitchedBlow>(2);
        if (clip(OverheadFrom) is { } overhead)
        {
            var layers = new StitchLayer[]
            {
                new("off hand", offHand, stand!, [0f], [1f]),
                new("bow", body, stand!, OverheadBowFrames, [0f, OverheadBowKept, OverheadBowKept, 0f]),
            };
            var turn = new StitchTurn("to the centre", spineFirst, System.Numerics.Vector3.UnitZ, OverheadTurnDegrees * DegreesToRadians, TurnFrames(overhead), [0f, 1f, 1f, 0f]);
            blows.Add(Blow(StitchedAttacks.Overhead, overhead, layers, [turn], OverheadReadyFrame, weapon!));
        }
        if (clip(ThrustFrom) is { } thrust)
        {
            var frames = TurnFrames(thrust);
            var turn = new StitchTurn("to the centre", spineFirst, System.Numerics.Vector3.UnitZ, ThrustTurnDegrees * DegreesToRadians, frames, [0f, 1f, 1f, 0f]);
            // The weapon arm drawn back straight from the stance: the halberd's first frames raise the pole diagonally across the
            // body (the tip 2 m high, crossing the face in first person) before drawing it back; the sword arm goes from the stand's
            // carry to the drawn-back pose instead, the clip's own from frame 12 (before the ready point and the window).
            var drawBack = new StitchLayer("weapon arm drawn back from the stance", weaponArm, stand!, ThrustDrawBackFrames, [1f, 1f, 0f]);
            StitchLayer[] layers = [new("off hand", offHand, stand!, [0f], [1f]), drawBack];
            // The second pass (the look at the films: "a low diagonal sweep from the lower right upward, not a stab"):
            // the halberd's lunge played twice as fast (its frames 13 to 20 in 3.5), and the sword driven straight along the aim by
            // the arm's IK through it, the point leading (Thrust's summary). Without a held blade, the first pass (the halberd's arm).
            var warp = new StitchWarp([0f, ThrustDriveFrom, ThrustDriveTo, thrust.Frames], [0f, ThrustDriveFrom, ThrustLungeEndsAt, thrust.Frames]);
            var plain = new StitchedClip(StitchedAttacks.ClipName(StitchedAttacks.Thrust), thrust, layers, [turn]) { Warp = warp };
            var reach = blade is { Kind: HeldKind.Blade } b && b.Length > 0f ? Drive(skeleton, limbs, b, plain, head, stand!) : null;
            var stitched = new StitchedClip(StitchedAttacks.ClipName(StitchedAttacks.Thrust), thrust, layers, [turn]) { Warp = reach is null ? null : warp, Reach = reach };
            float strikeFrame = reach is null ? -1f : warp.FrameOf(StrikeFrame(thrust));
            blows.Add(Blow(StitchedAttacks.Thrust, thrust, stitched, ThrustReadyFrame, weapon!, strikeFrame));
        }
        return blows;
    }

    /// <summary>
    /// The thrust's drive (SANDBOX, ours; the second pass): the stitched frames through which the blade is driven (13, the ready
    /// point, to 16.5) and the halberd's frame the lunge has reached by then (20: the hips 0.6 m forward), so the lunge plays twice
    /// as fast as 1.1's and the recovery (the halberd's 20 to 29) a little slower (12.5 frames); the clip lasts what it lasted.
    /// </summary>
    public const float ThrustDriveFrom = 13f, ThrustDriveTo = 16.5f, ThrustLungeEndsAt = 20f;

    /// <summary>
    /// Where the thrust is aimed (SANDBOX, ours): the point on the body's centre line this far in front (m) at its eyes' height
    /// (the head bone's height in its stand and <see cref="ThrustAimOverHead"/>): where a level crosshair looks at a foe in reach,
    /// so in first person the point comes from the lower right toward the crosshair's centre; the aim offset carries the line up
    /// and down with the look as it carries every blow.
    /// </summary>
    public const float ThrustAimDistance = 1.8f, ThrustAimOverHead = 0.05f;

    /// <summary>The thrust's IK weight (stitched frames, SANDBOX): in from 9 to the ready point (13: the point turned onto the line while it is drawn back), held through the drive and the hold to 19, out by 26 (the halberd's own recovery back to the stance).</summary>
    public static readonly float[] ThrustReachFrames = [9f, 13f, 19f, 26f];

    /// <summary>How far the arm may be stretched along the line (a share of its two bones' length).</summary>
    public const float ThrustArmStretch = 0.97f;

    /// <summary>
    /// The thrust's drive for a kind: the guard where the ready pose holds it (the first pass's frame 13), the line from it to the
    /// aim point, and how far along it the arm can carry the guard by the lunge's end (the shoulder there, the arm stretched to
    /// <see cref="ThrustArmStretch"/>).
    /// </summary>
    static StitchReach Drive(Skeleton skeleton, LimbMap limbs, HeldThing blade, StitchedClip plain, int head, IAnimationClip stand)
    {
        int n = skeleton.Count;
        var local = new BoneTransform[n];
        var model = new System.Numerics.Matrix4x4[n];
        stand.Sample(0f, skeleton, local);
        PoseEvaluator.ToModel(skeleton, local, model);
        float eyes = ((uint)head < (uint)n ? model[head].Translation.Z : 1.55f) + ThrustAimOverHead;
        plain.Sample(ThrustDriveFrom / NadClip.FramesPerSecond, skeleton, local);
        PoseEvaluator.ToModel(skeleton, local, model);
        var arm = limbs.RightArm;
        var guard = System.Numerics.Vector3.Transform(blade.Guard, model[blade.Bone]);
        var wrist = model[arm.End].Translation;
        float reach = LimbIK.Reach(model, arm) * ThrustArmStretch;
        float back = LimbIK.Length(guard - wrist);
        var d = LimbIK.Unit(new System.Numerics.Vector3(0f, ThrustAimDistance, eyes) - guard);
        plain.Sample(ThrustDriveTo / NadClip.FramesPerSecond, skeleton, local);
        PoseEvaluator.ToModel(skeleton, local, model);
        var shoulder = model[arm.Upper].Translation;
        // |guard + d (L − back) − shoulder| = reach: the farther root.
        var q = guard - d * back - shoulder;
        float qd = System.Numerics.Vector3.Dot(q, d), disc = qd * qd - System.Numerics.Vector3.Dot(q, q) + reach * reach;
        float length = MathF.Max(0.3f, -qd + MathF.Sqrt(MathF.Max(0f, disc)));
        return new StitchReach("the sword driven along the aim", arm, blade.Bone, blade.Guard, blade.Tip, guard, d, length, ThrustDriveFrom, ThrustDriveTo,
                               new System.Numerics.Vector3(1f, 0f, -1f), ThrustReachFrames, [0f, 1f, 1f, 0f]);
    }

    static float StrikeFrame(NadClip source)
    {
        int i = Array.FindIndex(source.Tags, t => t.Type == OneOneActions.StrikeTag);
        return i >= 0 ? source.Tags[i].Frame : -1f;
    }

    const float DegreesToRadians = MathF.PI / 180f;

    /// <summary>
    /// How far each blow's upper body is turned to its left (SANDBOX, ours; degrees about the vertical at the spine's first bone):
    /// the war hammer's overhead comes down 0.15 m right of the body's centre line and the halberd's thrust drives 0.35–0.45 m
    /// right of it (both weapons are held at the right side with two hands), so a blow aimed with the crosshair on a man's head or
    /// belly 1.4 m away grazed the side of his head and met his hanging forearm; turned, each meets the man's middle. 6° and 12°.
    /// </summary>
    public const float OverheadTurnDegrees = 6f, ThrustTurnDegrees = 12f;


    /// <summary>
    /// The thrust's draw-back (frames of <see cref="ThrustFrom"/>, SANDBOX, ours): the weapon arm is the stand's to frame 7 and the
    /// halberd's own by frame 12, blended between — so the sword is drawn back from where the stance carries it, not raised
    /// across the face first as the pole is (its tip 2 m high in front of the eyes at frame 6, seen in the first-person film).
    /// </summary>
    public static readonly float[] ThrustDrawBackFrames = [0f, 7f, 12f];

    /// <summary>The turn's keys (frames of the source clip): in over the first third of the wind-up, held to two thirds of the clip's rest after the strike, out by its end.</summary>
    static float[] TurnFrames(NadClip source)
    {
        float end = source.Frames;
        int i = Array.FindIndex(source.Tags, t => t.Type == OneOneActions.StrikeTag);
        float strike = i >= 0 ? source.Tags[i].Frame : end * 0.45f;
        return [0f, strike / 3f, strike + (end - strike) * 2f / 3f, end];
    }

    static OneOneStitchedBlow Blow(string direction, NadClip source, StitchLayer[] layers, StitchTurn[] turns, float readyFrame, OneOneWeapon weapon) =>
        Blow(direction, source, new StitchedClip(StitchedAttacks.ClipName(direction), source, layers, turns), readyFrame, weapon, -1f);

    /// <summary>The blow as 1.1 plays an attack, its Strike mark the source's (or <paramref name="strikeFrame"/>, a stitched frame, when the clip's time is warped).</summary>
    static OneOneStitchedBlow Blow(string direction, NadClip source, StitchedClip stitched, float readyFrame, OneOneWeapon weapon, float strikeFrame)
    {
        // As OneOneActions plays 1.1's attacks: the whole clip lasts the first attack's STRIKETIME; busy its RELOADTIME.
        float strikeTime = weapon.StrikeTime[0], reload = weapon.ReloadTime[0];
        float rate = strikeTime > 0.01f && source.Duration > 0f ? source.Duration / strikeTime : 1f;
        float mark = strikeFrame >= 0f ? strikeFrame : StrikeFrame(source);
        float strike = mark >= 0f ? mark / NadClip.FramesPerSecond : -1f;
        float busy = reload > 0.01f ? reload : source.Duration / rate;
        return new OneOneStitchedBlow(direction, stitched, rate, strike, busy, readyFrame / NadClip.FramesPerSecond, source.Name);
    }

    // ---- the side blows flattened ----

    /// <summary>
    /// The height the flattened side blows sweep at (SANDBOX, ours; the horizontal swings somewhat flattened so
    /// they are more even side to side): the blade level at 1.40 m over the feet, a standing man's chest to shoulders, from either
    /// side; the aim offset carries it up and down with the look as it carries every blow.
    /// </summary>
    public const float SwingHeight = 1.40f;

    /// <summary>The module's switch that keeps 1.1's own side blows: <c>swings-oneone</c>.</summary>
    public const string SwingsOneOneSwitch = "swings-oneone";

    /// <summary>
    /// Each 1.1 side blow flattened and its weight's keys (frames of the clip, SANDBOX): in as the blade comes round from its
    /// wind-up's loop over the head onto its side, held through the sweep across the front, out through the follow-through. The
    /// forehand (<c>chrisAttackSword</c>, and the night watchman's <c>Christof_attackSword_util</c>, the same motion with the torch
    /// held): in from 10 (the tip coming round behind the head) to 13 (on the right), out 17.5 to 21; the backhand
    /// (<c>chrisAttack2Sword</c>): in 10.5 to 12 (behind, then on the left), out 16 to 19.5.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, float[]> SwingFlattenFrames = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["chrisAttackSword"] = [10f, 13f, 17.5f, 21f],
        ["Christof_attackSword_util"] = [10f, 13f, 17.5f, 21f],
        ["chrisAttack2Sword"] = [10.5f, 12f, 16f, 19.5f],
    };

    /// <summary>
    /// the second pass (SANDBOX, ours): the flattened forehand's time warp. Level, its sweep slows after the body's centre line
    /// (1.1's motion spends those frames dropping the blade to the floor, which the flattening takes out), so its active window
    /// ended at the centre line (frame 15.5) and the left half of its sweep met nothing (the merged arena: 25 % of its swings met
    /// the watchman's body, 1.1's 60 %). 1.1's frames 15 to 18.5 (the centre to the left side) play in 2 (15 to 17) and its 18.5 to
    /// 29 (the recovery) in 12; the clip lasts what it lasted, its Strike mark (15) where it was.
    /// <para>
    /// From the pace survey: the flattened backhand's sweep capped to the forehand's speed. Level, its tip
    /// peaked at 77 m/s a clip second (the forehand's 55; a real one-handed cut 15 to 25 m/s): 1.1's frames 13.25 to 15.75 (the
    /// fast half of its sweep across the front) now play in 3.5 (12.55 to 16.05), 1.4 times as long, so it peaks at the forehand's;
    /// its wind-up (0 to 13.25) and its recovery (15.75 to 29) a few per cent faster to pay for it, the clip as long, its Strike mark
    /// (15) where it was.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (float[] Frames, float[] BaseFrames)> SwingFlattenWarp = new Dictionary<string, (float[], float[])>(StringComparer.OrdinalIgnoreCase)
    {
        ["chrisAttackSword"] = ([0f, 15f, 17f, 29f], [0f, 15f, 18.5f, 29f]),
        ["Christof_attackSword_util"] = ([0f, 15f, 17f, 29f], [0f, 15f, 18.5f, 29f]),
        ["chrisAttack2Sword"] = ([0f, 12.55f, 16.05f, 29f], [0f, 13.25f, 15.75f, 29f]),
    };

    /// <summary>The clip name a flattened side blow is registered under (<c>flat:chrisAttackSword</c>).</summary>
    public static string FlatName(string source) => "flat:" + source;

    /// <summary>
    /// The flattened side blows for a kind: its quick blows that are 1.1's side blows (<see cref="SwingFlattenFrames"/>), each
    /// played whole as 1.1 plays it with its blade's sweep brought level at <see cref="SwingHeight"/> by the weapon arm's IK; for a
    /// kind of Christof's skeleton carrying a one-handed blade (as the stitched blows), else none. Which are played is the session's
    /// (the switch <c>swings-oneone</c> keeps 1.1's).
    /// </summary>
    public static IReadOnlyList<(string From, StitchedClip Clip)> Flat(Skeleton skeleton, LimbMap? limbs, OneOneWeapon? weapon, IAnimationClip? stand,
                                                                      HeldThing? blade, IEnumerable<IAnimationClip> quick, StitchHips? hips = null)
    {
        if (Why(skeleton, limbs, weapon, stand).Length > 0 || blade is not { Kind: HeldKind.Blade } b || b.Length <= 0f) return [];
        var flat = new List<(string, StitchedClip)>(2);
        foreach (var q in quick)
        {
            if (q is not NadClip source || !SwingFlattenFrames.TryGetValue(source.Name, out var frames)) continue;
            var level = new StitchFlatten("the sweep brought level", limbs!.RightArm, b.Bone, b.Guard, b.Tip, SwingHeight, new System.Numerics.Vector3(1f, 0f, -1f),
                                          frames, [0f, 1f, 1f, 0f]);
            // The earlier build's second pass: the forehand's sweep across the left half of the front sped up (SwingFlattenWarp), so its active
            // window (contact's: the tip at 40 % of its peak or more) runs on to the left side, as 1.1's diagonal does; the
            // backhand's sweep slowed to the forehand's peak.
            var warp = SwingFlattenWarp.TryGetValue(source.Name, out var w) ? new StitchWarp(w.Frames, w.BaseFrames) : null;
            flat.Add((source.Name, new StitchedClip(FlatName(source.Name), source, []) { Flatten = level, Warp = warp, Hips = hips }));   // the hips held upright (hips: StitchHips)
        }
        return flat;
    }

    // ---- the guards' left ----

    /// <summary>
    /// The 1.1 clip lent to a one-handed blade of Christof's skeleton whose quick blows have no backhand (SANDBOX, ours): Christof's
    /// own backhand, <c>chrisAttack2Sword</c> (<c>Christof.nms</c>'s second <c>swordslash</c> entry, 29 frames, <c>Strike</c> at 15). 1.1's
    /// guards (<c>rudolfGuard</c>, <c>viennaGuard_sword</c>: <c>chrisAttackSword</c> alone) and the night watchman
    /// (<c>Christof_attackSword_util</c> alone) swing only the forehand; they stand on Christof's skeleton bone for bone (the bone map is
    /// the identity), so his backhand plays on them as it plays on him.
    /// </summary>
    public const string LentBackhand = "chrisAttack2Sword";

    /// <summary>
    /// <see cref="LentBackhand"/> flattened as Christof's is (<see cref="Flat"/>: the sweep level at <see cref="SwingHeight"/>, its fast half
    /// slowed to the forehand's peak) for a kind that lacks it, with <b>the off hand from the kind's own stand</b> (the left shoulder and every
    /// bone under it, as the stitched overhead and thrust take it): 1.1's backhand swings Christof's empty left arm for balance, which would
    /// swing a guard's shield or the watchman's torch about; from the stand they stay where his stance carries them. Null for a kind that gets
    /// no flattened blows (<see cref="Why"/>, no blade).
    /// </summary>
    public static StitchedClip? FlatLent(Skeleton skeleton, LimbMap? limbs, OneOneWeapon? weapon, IAnimationClip? stand, HeldThing? blade, NadClip source,
                                         StitchHips? hips = null)
    {
        var flat = Flat(skeleton, limbs, weapon, stand, blade, [source], hips);
        if (flat.Count == 0) return null;
        var f = flat[0].Clip;
        var offHand = Subtree(skeleton, limbs!.LeftArm.Upper);
        return new StitchedClip(FlatName(source.Name), source, [new StitchLayer("off hand", offHand, stand!, [0f], [1f])]) { Flatten = f.Flatten, Warp = f.Warp, Hips = hips };
    }

    /// <summary>A bone and every bone under it (parents precede children in 1.1's skeletons).</summary>
    public static bool[] Subtree(Skeleton skeleton, int top)
    {
        var mask = new bool[skeleton.Count];
        if ((uint)top >= (uint)mask.Length) return mask;
        mask[top] = true;
        for (int b = top + 1; b < mask.Length; b++)
        {
            int p = skeleton.Bones[b].Parent;
            if (p >= 0 && p < b && mask[p]) mask[b] = true;
        }
        return mask;
    }
}
