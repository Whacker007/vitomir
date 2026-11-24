// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;

namespace Vitomir.Platform.Combat;

/// <summary>
/// A blow's active window (SANDBOX, <see cref="ActiveWindowRule"/>): the span of its clip, in clip seconds, in
/// which its blade is swept for contact — <see cref="From"/> to <see cref="To"/> round the <c>Strike</c> mark — with the
/// mark, the fastest the striking point moves in the clip (m/s at the clip's own rate) and the share of it the window
/// was cut at. <see cref="FromMotion"/> is false when the motion gave no span (a clip with no mark, or the point slow
/// at the mark) and the window is the rule's least span round the mark. <see cref="Cut"/>, the clip seconds the strike's
/// start (<see cref="StrikeStart"/>) took off the motion's run — its move out of a wind-up — 0 when none did.
/// </summary>
public readonly record struct ActiveWindow(float From, float To, float Strike, float PeakSpeed, float Share, bool FromMotion, float Cut = 0f)
{
    /// <summary>No window: the clip is never swept.</summary>
    public static ActiveWindow None => new(-1f, -1f, -1f, 0f, 0f, false);

    /// <summary>Where the motion's fast run began: <see cref="From"/> less <see cref="Cut"/> — the earlier build's window's start.</summary>
    public float MotionFrom => From - Cut;

    public bool Has => To > From;

    /// <summary>Whether the clip time from <paramref name="last"/> to <paramref name="now"/> (one tick's advance) overlaps the window.</summary>
    public bool Overlaps(float last, float now) => Has && now >= From && last <= To;
}

/// <summary>
/// The active windows of every attack clip of every kind of character (a service, ours, not digested: derived
/// from the clips, the held models and the rule). Worked out <b>once per clip at load</b> (<see cref="Prepare"/>; a
/// clip first asked about later is worked out then): the clip sampled at the rule's step on the kind's skeleton alone
/// (no IK, no aim offset: the motion as made), its striking point — the first blade's tip, or bare-handed the weapon
/// hand's joint — carried by its bone, its speed between samples measured, and the window is the run of samples round
/// the <c>Strike</c> mark where the speed is at least <see cref="ActiveWindowRule.ShareOfPeak"/> of the clip's
/// fastest (a slow moment no longer than <see cref="ActiveWindowRule.BridgeSeconds"/> between fast ones bridged). Kept by (kind, clip), since the same clip moves a longer blade faster.
/// </summary>
public sealed class ActiveWindows
{
    ActiveWindow[][] byKind = [];
    bool[][] known = [];

    /// <summary>Works out the window of every attack clip of every kind <paramref name="assets"/> holds now.</summary>
    public void Prepare(CharacterAssets assets, ActiveWindowRule rule)
    {
        for (int k = 0; k < assets.KindCount; k++)
        {
            if (assets.Kind(k).Actions is not { } set) continue;
            foreach (var m in set.Quick) For(assets, k, m.Clip, m.StrikeSeconds, rule);
            For(assets, k, set.Strong.Clip, set.Strong.StrikeSeconds, rule);
            foreach (var s in set.Stitched) For(assets, k, s.Motion.Clip, s.Motion.StrikeSeconds, rule);   // the stitched blows
        }
    }

    /// <summary>The window of <paramref name="clip"/> as kind <paramref name="kind"/> plays it (worked out now if it was not).</summary>
    public ActiveWindow For(CharacterAssets assets, int kind, int clip, float strikeSeconds, ActiveWindowRule rule)
    {
        if (clip < 0 || kind < 0) return ActiveWindow.None;
        if (kind >= byKind.Length)
        {
            int size = Math.Max(kind + 1, Math.Max(8, byKind.Length * 2));
            Array.Resize(ref byKind, size);
            Array.Resize(ref known, size);
        }
        if (byKind[kind] is null || byKind[kind].Length <= clip)
        {
            int size = Math.Max(clip + 1, assets.ClipCount);
            var w = byKind[kind] ?? []; var k = known[kind] ?? [];
            Array.Resize(ref w, size); Array.Resize(ref k, size);
            byKind[kind] = w; known[kind] = k;
        }
        if (!known[kind][clip])
        {
            byKind[kind][clip] = Measure(assets, kind, clip, strikeSeconds, rule);
            known[kind][clip] = true;
        }
        return byKind[kind][clip];
    }

    /// <summary>
    /// The window of a clip as a kind plays it (the class's summary): pure, from the clip, the kind's skeleton and its
    /// held blade (or weapon hand). Samples at <see cref="ActiveWindowRule.SampleSeconds"/>; the speed of sample
    /// <c>i</c> is the point's move from sample <c>i − 1</c> over the step, and the window runs from the start of the
    /// first fast step to the end of the last.
    /// </summary>
    public static ActiveWindow Measure(CharacterAssets assets, int kind, int clip, float strikeSeconds, ActiveWindowRule rule)
    {
        if (clip < 0 || clip >= assets.ClipCount || strikeSeconds < 0f) return ActiveWindow.None;
        var c = assets.Kind(kind);
        var skeleton = assets.ModelOf(kind).Skeleton;
        int bone = -1;
        Vector3 local = Vector3.Zero, hilt = Vector3.Zero;
        bool blade = false;
        foreach (var held in c.Held)
        {
            foreach (var t in held.Things)
                if (t.Kind == HeldKind.Blade) { bone = t.Bone; local = t.Tip; hilt = t.Guard; blade = true; break; }
            if (bone >= 0) break;
        }
        if (bone < 0 && c.Actions is { WeaponHand: >= 0 } set) bone = set.WeaponHand;
        if ((uint)bone >= (uint)skeleton.Count) return Least(strikeSeconds, rule, 0f);

        var motion = assets.Clip(clip);
        // The strike's start, for a blade whose clip the rule names (its tip and guard kept by sample to find it).
        var start = blade ? StrikeStart.For(rule.StrikeStarts, motion.Name) : null;
        float step = MathF.Max(1e-3f, rule.SampleSeconds);
        int samples = Math.Max(2, (int)MathF.Ceiling(motion.Duration / step) + 1);
        var pose = new BoneTransform[skeleton.Count];
        var model = new Matrix4x4[skeleton.Count];
        var speeds = new float[samples];
        var tips = start is null ? null : new Vector3[samples];
        var guards = start is null ? null : new Vector3[samples];
        var last = Vector3.Zero;
        float peak = 0f;
        for (int i = 0; i < samples; i++)
        {
            float t = MathF.Min(i * step, motion.Duration);
            PoseEvaluator.Rest(skeleton, pose);
            motion.Sample(t, skeleton, pose);
            PoseEvaluator.ToModel(skeleton, pose, model);
            var p = PoseMath.Transform(local, model[bone]);
            if (i > 0) { speeds[i] = Geometry.Length(p - last) / step; peak = MathF.Max(peak, speeds[i]); }
            last = p;
            if (tips is not null) { tips[i] = p; guards![i] = PoseMath.Transform(hilt, model[bone]); }
        }
        float threshold = rule.ShareOfPeak * peak;
        int mark = Math.Clamp((int)MathF.Round(strikeSeconds / step), 1, samples - 1);
        // The fast samples round the mark: from the mark (or the nearest fast sample within a tenth of the clip) out both ways.
        int at = -1;
        int within = Math.Max(1, (int)MathF.Ceiling(0.1f * motion.Duration / step));
        for (int d = 0; d <= within && at < 0; d++)
        {
            if (mark - d >= 1 && speeds[mark - d] >= threshold && peak > 0f) at = mark - d;
            else if (mark + d < samples && speeds[mark + d] >= threshold && peak > 0f) at = mark + d;
        }
        if (at < 0) return Least(strikeSeconds, rule, peak);
        // Out both ways while the point is fast; a slow stretch no longer than the bridge between fast ones is bridged (a
        // motion's key can brake the point for a moment mid-swing: chrisAttackSword's at frame 16).
        int bridge = Math.Max(0, (int)MathF.Floor(rule.BridgeSeconds / step));
        int first = at, end = at;
        while (true)
        {
            int next = -1;
            for (int j = first - 1; j >= 1 && j >= first - 1 - bridge; j--) if (speeds[j] >= threshold) { next = j; break; }
            if (next < 0) break;
            first = next;
        }
        while (true)
        {
            int next = -1;
            for (int j = end + 1; j < samples && j <= end + 1 + bridge; j++) if (speeds[j] >= threshold) { next = j; break; }
            if (next < 0) break;
            end = next;
        }
        float from = (first - 1) * step, to = MathF.Min(end * step, motion.Duration);
        if (to - from < rule.LeastSeconds)
        {
            float grow = (rule.LeastSeconds - (to - from)) * 0.5f;
            from = MathF.Max(0f, from - grow);
            to = MathF.Min(motion.Duration, to + grow);
        }
        // The window begins where the strike proper begins, not with the blade's move out of the ready pose into it. Searched
        // over the fast run; found nowhere in it, the run's start is kept. A window left shorter than the least span is grown at its
        // end, toward the Strike mark (the overhead's: the earlier build's window closed 0.25 frames before its mark), never into the wind-up;
        // where the clip ends first, the run's start is kept.
        float cut = 0f;
        if (start is not null)
        {
            int s = StrikeBegins(start, tips!, guards!, Math.Max(1, first - 1), end);
            float begins = s * step;
            float until = MathF.Max(to, MathF.Min(motion.Duration, begins + rule.LeastSeconds));
            if (s >= 0 && begins > from && until - begins >= rule.LeastSeconds - 1e-6f) { cut = begins - from; from = begins; to = until; }
        }
        return new ActiveWindow(from, to, strikeSeconds, peak, rule.ShareOfPeak, true, cut);
    }

    /// <summary>
    /// The first sample in [<paramref name="first"/>, <paramref name="end"/>] at which <paramref name="start"/>'s rule holds, or
    /// −1: <see cref="StrikeStartRule.Level"/> the tip within the row's metres of the guard's height; <see cref="StrikeStartRule.Top"/>
    /// the tip's highest before it starts down (the sample before the first that is lower than the one before it);
    /// <see cref="StrikeStartRule.Forward"/> the tip's last sample before it moves forward (+Y, the body's front).
    /// </summary>
    static int StrikeBegins(StrikeStart start, Vector3[] tips, Vector3[] guards, int first, int end)
    {
        for (int i = first; i <= end && i < tips.Length; i++)
        {
            switch (start.Rule)
            {
                case StrikeStartRule.Level:
                    if (MathF.Abs(tips[i].Z - guards[i].Z) <= start.Metres) return i;
                    break;
                case StrikeStartRule.Top:
                    if (i > first && tips[i].Z < tips[i - 1].Z) return i - 1;
                    break;
                case StrikeStartRule.Forward:
                    if (i > first && tips[i].Y > tips[i - 1].Y) return i - 1;
                    break;
            }
        }
        return -1;
    }

    static ActiveWindow Least(float strike, ActiveWindowRule rule, float peak) =>
        new(MathF.Max(0f, strike - rule.LeastSeconds * 0.5f), strike + rule.LeastSeconds * 0.5f, strike, peak, rule.ShareOfPeak, false);
}
