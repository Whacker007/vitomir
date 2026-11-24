// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using System.Text;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Combat;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The strike's path without the move into it (*"notice that extraneous upward hook on the sword
/// path in the drawn arc. That shouldn't be an actual sword damage path, that's just the wind up. The arc across the body should be
/// the actual swing and damage."*). Every test skips without the 1.1 install
/// (VTMR_GAME_DIR). With <c>VITOMIR_STRIKE_SURVEY</c> set to a folder, the survey writes each blow's tip by frame there.
/// </summary>
public sealed class StrikeWindowTests(ITestOutputHelper output)
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);

    static OneOneLibrary Lib()
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        return Library.Value!;
    }

    static readonly Dictionary<(string, string?), OneOneCharacter> Loaded = [];

    static OneOneCharacter Character(string name, string? weapon = null)
    {
        var lib = Lib();
        lock (Loaded)
        {
            if (!Loaded.TryGetValue((name, weapon), out var c))
                Loaded[(name, weapon)] = c = lib.Character(name, weapon) ?? throw new Xunit.Sdk.XunitException($"{name} did not load");
            return c;
        }
    }

    public static readonly TheoryData<string, string?> SwordKinds = new()
    {
        { "Christof", "BroadSword" }, { "Christof", "Rapier" }, { "rudolfGuard", null }, { "KnightOfStJohn_torch", null }, { "viennaGuard_sword", null },
    };

    static (CharacterAssets Assets, int Kind, ActionSet Set) Registered(string name, string? weapon, bool flat)
    {
        var assets = new CharacterAssets();
        int kind = OneOneLibrary.Register(assets, Character(name, weapon), null, flat);
        return (assets, kind, assets.Kind(kind).Actions!);
    }

    static HeldThing Blade(OneOneCharacter c) => c.Held.SelectMany(h => h.Things).First(t => t.Kind == HeldKind.Blade);

    static (Vector3 Tip, Vector3 Guard) Pose(OneOneCharacter c, IAnimationClip clip, float s)
    {
        var sk = c.Model.Skeleton;
        var local = new BoneTransform[sk.Count];
        var model = new Matrix4x4[sk.Count];
        PoseEvaluator.Rest(sk, local);
        clip.Sample(s, sk, local);
        PoseEvaluator.ToModel(sk, local, model);
        var b = Blade(c);
        return (Vector3.Transform(b.Tip, model[b.Bone]), Vector3.Transform(b.Guard, model[b.Bone]));
    }

    const float Tempo = 0.70f;   // The earlier build's global combat tempo (pace.json's tempo.global), for the times in real seconds

    /// <summary>
    /// Each wind-up's window (the earlier build's <c>windup:*</c>, as the session registers them) begins where its strike proper begins and ends
    /// where it did: the side blows where the blade is first level (the tip within 0.1 m of the guard's height: their sweep, in front
    /// of the body), not as it comes down out of the ready pose; the overhead at its top, not as it rises from behind the head; the
    /// thrust where it starts forward, as it already did. Without the rows (<c>window-from-windup</c>) every window is the earlier build's.
    /// </summary>
    [Theory]
    [MemberData(nameof(SwordKinds))]
    public void Each_wind_up_s_window_begins_where_its_strike_proper_begins_and_ends_where_it_did(string name, string? weapon)
    {
        var c = Character(name, weapon);
        var (assets, kind, set) = Registered(name, weapon, flat: true);
        var rule = new ActiveWindowRule();
        var was = rule with { StrikeStarts = [] };
        var ic = CultureInfo.InvariantCulture;
        Assert.NotEmpty(set.Stitched);
        foreach (var s in set.Stitched)
        {
            var clip = (WindUpClip)assets.Clip(s.Motion.Clip);
            var w = ActiveWindows.Measure(assets, kind, s.Motion.Clip, s.Motion.StrikeSeconds, rule);
            var o = ActiveWindows.Measure(assets, kind, s.Motion.Clip, s.Motion.StrikeSeconds, was);
            Assert.Equal(0f, o.Cut);
            Assert.Equal(o.From, w.MotionFrom, 1e-5f);
            Assert.True(w.To - w.From >= rule.LeastSeconds - 1e-5f, $"{s.Direction}: at least the least span");
            var (tip, guard) = Pose(c, clip, w.From);
            var (tipBefore, guardBefore) = Pose(c, clip, w.From - rule.SampleSeconds);
            float heading = MathF.Atan2(tip.X, tip.Y) * 180f / MathF.PI;
            float ms = w.Cut / s.Motion.Rate * 1000f;
            output.WriteLine(string.Create(ic, $"{name} {weapon} {s.Direction}: the window {o.From * 30f:0.00}-{o.To * 30f:0.00} -> {w.From * 30f:0.00}-{w.To * 30f:0.00} of the wind-up (the blow's {clip.BaseFrame(o.From * 30f):0.00} -> {clip.BaseFrame(w.From * 30f):0.00}); {w.Cut * 30f:0.00} frames, {ms:0} ms at the weapon's rate {s.Motion.Rate:0.00}, {ms / Tempo:0} ms at the tempo {Tempo:0.00}; the tip there {tip.Z:0.00} m (the guard {guard.Z:0.00}), heading {heading:0}°"));
            switch (s.Direction)
            {
                case "left" or "right":
                    Assert.True(w.Cut > 0f, $"{s.Direction}: the descent from the ready pose cut");
                    Assert.Equal(o.To, w.To, 1e-5f);
                    Assert.True(MathF.Abs(tip.Z - guard.Z) <= 0.1f + 1e-4f, $"{s.Direction}: level at its start ({tip.Z - guard.Z:0.000})");
                    Assert.True(MathF.Abs(tipBefore.Z - guardBefore.Z) > 0.1f, $"{s.Direction}: not level a sample before");
                    Assert.True(MathF.Abs(heading) < 100f, $"{s.Direction}: the tip in front ({heading:0}°)");
                    Assert.True(MathF.Abs(tip.Z - OneOneStitched.SwingHeight) < 0.1f, $"{s.Direction}: at the sweep's height ({tip.Z:0.00})");
                    break;
                case "overhead":
                    Assert.True(w.Cut > 0f, "overhead: the raise behind the head cut");
                    Assert.True(w.To >= o.To);
                    // At its top: no place of the earlier build's window higher than where it now begins (a sample's rise allowed).
                    for (float t = o.From; t <= o.To; t += rule.SampleSeconds)
                        Assert.True(Pose(c, clip, t).Tip.Z <= tip.Z + 0.002f, $"overhead: {Pose(c, clip, t).Tip.Z:0.000} at {t * 30f:0.00} above the start's {tip.Z:0.000}");
                    Assert.True(tip.Z > 2.0f, $"overhead: begins above the head ({tip.Z:0.00})");
                    break;
                case "thrust":
                    // The earlier build's window was the least span stood round the mark, from a moment before the tip starts forward: it now
                    // begins where the tip starts forward (an eighth of a frame later), as long as before.
                    Assert.True(w.Cut * 30f < 0.25f, $"thrust: {w.Cut * 30f:0.00} frames cut");
                    Assert.Equal(o.To - o.From, w.To - w.From, 1e-4f);
                    Assert.True(Pose(c, clip, w.From + 2f * rule.SampleSeconds).Tip.Y > tip.Y + 1e-3f, "thrust: the tip goes forward from its start");
                    break;
            }
        }
    }

    /// <summary>1.1's own blows (<c>swings-oneone</c>: 1.1's side blows, the earlier build's stitched overhead and thrust) and the earlier build's flattened quick blows (<c>attack-spin</c>) keep their windows: no row names them.</summary>
    [Theory]
    [MemberData(nameof(SwordKinds))]
    public void One_one_s_own_blows_and_the_flattened_quick_blows_keep_their_windows(string name, string? weapon)
    {
        foreach (bool flat in new[] { false, true })
        {
            var (assets, kind, set) = Registered(name, weapon, flat);
            var rule = new ActiveWindowRule();
            var was = rule with { StrikeStarts = [] };
            var blows = set.Quick.Append(set.Strong).Where(m => m.Has).ToList();
            if (!flat) blows.AddRange(set.Stitched.Select(s => s.Motion));
            foreach (var m in blows)
            {
                var w = ActiveWindows.Measure(assets, kind, m.Clip, m.StrikeSeconds, rule);
                Assert.Equal(ActiveWindows.Measure(assets, kind, m.Clip, m.StrikeSeconds, was), w);
                Assert.Equal(0f, w.Cut);
            }
        }
    }

    /// <summary>The tip with the clip's lower body (the bones not in <see cref="ActionSet.UpperBody"/>) held at the clip's first frame: the blow as a moving body plays it (the action on the upper body over the walk's hips), roughly.</summary>
    static Vector3 TipMoving(OneOneCharacter c, ActionSet set, IAnimationClip clip, float s)
    {
        var sk = c.Model.Skeleton;
        var local = new BoneTransform[sk.Count];
        var stance = new BoneTransform[sk.Count];
        var model = new Matrix4x4[sk.Count];
        PoseEvaluator.Rest(sk, local);
        PoseEvaluator.Rest(sk, stance);
        clip.Sample(s, sk, local);
        clip.Sample(0f, sk, stance);
        for (int b = 0; b < sk.Count; b++) if (b >= set.UpperBody.Length || !set.UpperBody[b]) local[b] = stance[b];
        PoseEvaluator.ToModel(sk, local, model);
        var bl = Blade(c);
        return Vector3.Transform(bl.Tip, model[bl.Bone]);
    }

    /// <summary>Every blow as the session plays it (the wind-ups, and with <c>swings-oneone</c> 1.1's own and the earlier build's stitched): the tip by quarter frame, to a CSV when asked.</summary>
    [Theory]
    [MemberData(nameof(SwordKinds))]
    public void Survey_the_tip_by_frame(string name, string? weapon)
    {
        string? folder = Environment.GetEnvironmentVariable("VITOMIR_STRIKE_SURVEY");
        var c = Character(name, weapon);
        var ic = CultureInfo.InvariantCulture;
        var sb = new StringBuilder("kind,weapon,set,clip,direction,clip_frame,blow_frame,tip_x,tip_y,tip_z,guard_x,guard_y,guard_z,speed,heading,slope,in_window,window_from,window_to,strike,in_window_windup,window_from_windup,moving_tip_x,moving_tip_y,moving_tip_z\n");
        foreach (bool flat in new[] { true, false })
        {
            var (assets, kind, set) = Registered(name, weapon, flat);
            var rule = new ActiveWindowRule();
            var ruleWindUp = rule with { StrikeStarts = [] };
            var blows = new List<(string Direction, ActionMotion Motion)>();
            foreach (var s in set.Stitched) blows.Add((s.Direction, s.Motion));
            foreach (var q in set.Quick) blows.Add(("quick", q));
            foreach (var (direction, m) in blows)
            {
                var clip = assets.Clip(m.Clip);
                var w = ActiveWindows.Measure(assets, kind, m.Clip, m.StrikeSeconds, rule);
                var was = ActiveWindows.Measure(assets, kind, m.Clip, m.StrikeSeconds, ruleWindUp);
                var wind = clip as WindUpClip;
                var last = Pose(c, clip, 0f).Tip;
                const float step = 1f / 120f;
                for (float t = step; t <= clip.Duration + 1e-5f; t += step)
                {
                    var (tip, guard) = Pose(c, clip, t);
                    float speed = Vector3.Distance(tip, last) / step;
                    last = tip;
                    float heading = MathF.Atan2(tip.X, tip.Y) * 180f / MathF.PI;
                    var d = tip - guard;
                    float slope = MathF.Atan2(d.Z, MathF.Sqrt(d.X * d.X + d.Y * d.Y)) * 180f / MathF.PI;
                    float frame = t * 30f, blowFrame = wind?.BaseFrame(frame) ?? frame;
                    var mv = TipMoving(c, set, clip, t);
                    bool inside = w.Has && t >= w.From && t <= w.To;
                    sb.Append(string.Create(ic, $"{name},{weapon ?? "-"},{(flat ? "session" : "oneone")},{clip.Name},{direction},{frame:0.00},{blowFrame:0.00},{tip.X:0.000},{tip.Y:0.000},{tip.Z:0.000},{guard.X:0.000},{guard.Y:0.000},{guard.Z:0.000},{speed:0.0},{heading:0.0},{slope:0.0},{(inside ? 1 : 0)},{w.From * 30f:0.00},{w.To * 30f:0.00},{m.StrikeSeconds * 30f:0.00},{(was.Has && t >= was.From && t <= was.To ? 1 : 0)},{was.From * 30f:0.00},{mv.X:0.000},{mv.Y:0.000},{mv.Z:0.000}\n"));
                }
                output.WriteLine(string.Create(ic, $"{name} {(flat ? "session" : "oneone")} {clip.Name} ({direction}): window {w.From * 30f:0.00}-{w.To * 30f:0.00} of {clip.Duration * 30f:0.0}"));
            }
        }
        if (folder is not null)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, $"{name}-{weapon ?? "own"}.csv"), sb.ToString());
        }
    }
}
