// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Combat;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The four directions' wind-ups and ready poses (*"Let's build the wind ups for all 4 directions"*):
/// registered with the flattened side blows only (the switch <c>swings-oneone</c> and the
/// library's plain registration keep the earlier build's stitched blows and 1.1's side blows exactly), each at its blow's rate with its ready point
/// at its wind-up's end; each ready pose where its shape puts it and as asked (the blade cocked over the left shoulder, at the right
/// shoulder, raised over the head, drawn back at the hip with the point forward), its blade (the overhead's hand) at the edge of the
/// first-person picture; the
/// blade's path from the join on the blow's own, its window's end where it was; every side blow peaking near the forehand's; the same
/// every time; nothing allocated. Every test skips without the 1.1 install (VTMR_GAME_DIR).
/// </summary>
public sealed class WindUpTests(ITestOutputHelper output)
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

    static (Vector3 Tip, Vector3 Guard, Matrix4x4[] Model) Pose(OneOneCharacter c, IAnimationClip clip, float s)
    {
        var sk = c.Model.Skeleton;
        var local = new BoneTransform[sk.Count];
        var model = new Matrix4x4[sk.Count];
        PoseEvaluator.Rest(sk, local);
        clip.Sample(s, sk, local);
        PoseEvaluator.ToModel(sk, local, model);
        var b = Blade(c);
        return (Vector3.Transform(b.Tip, model[b.Bone]), Vector3.Transform(b.Guard, model[b.Bone]), model);
    }

    static WindUpShape ShapeOf(string direction) => OneOneWindUps.Defaults.Single(s => s.Direction == direction);

    /// <summary>Every kind has all four — the guards and the watchmen their left lent (Christof's backhand).</summary>
    static string[] Expected(string name) => ["left", "right", "overhead", "thrust"];

    [Theory]
    [MemberData(nameof(SwordKinds))]
    public void The_wind_ups_come_with_the_flattened_blows_only_each_at_its_blows_rate_with_its_ready_point_at_its_end(string name, string? weapon)
    {
        var c = Character(name, weapon);
        var (oneAssets, _, one) = Registered(name, weapon, flat: false);
        var plain = new CharacterAssets();
        OneOneLibrary.Register(plain, c);
        Assert.Equal(plain.ClipCount, oneAssets.ClipCount);                                  // swings-oneone: the earlier build's stitched blows and 1.1's
        Assert.Equal(["overhead", "thrust"], one.Stitched.Select(s => s.Direction));
        Assert.Equal(["stitched:overhead", "stitched:thrust"], one.Stitched.Select(s => oneAssets.Clip(s.Motion.Clip).Name));

        var (assets, _, set) = Registered(name, weapon, flat: true);
        Assert.Equal(Expected(name), set.Stitched.Select(s => s.Direction));
        foreach (var s in set.Stitched)
        {
            var clip = Assert.IsType<WindUpClip>(assets.Clip(s.Motion.Clip));
            Assert.Equal(OneOneWindUps.ClipName(s.Direction), clip.Name);
            var shape = ShapeOf(s.Direction);
            Assert.Equal(shape.WindUpFrames / 30f, s.ReadySeconds, 1e-5f);
            Assert.Equal(s.ReadySeconds, set.StitchedReady(s.Direction));
            Assert.Equal(clip.ReadySeconds, s.ReadySeconds);
            // The blow's own rate and busy time (the weapon's), its Strike mark carried into the strike.
            int own = Array.FindIndex(set.Quick, q => OneOneWindUps.SideOf.TryGetValue(assets.Clip(q.Clip).Name["flat:".Length..], out var side) && side == s.Direction);
            var blow = s.Direction is "left" or "right" ? (own >= 0 ? set.Quick[own] : ActionMotion.None) : one.Stitched.Single(o => o.Direction == s.Direction).Motion;
            if (own < 0 && s.Direction == "left")
            {
                // The left lent (Christof's backhand, 29 frames as the kind's forehand): at its forehand's rate and busy time, the
                // backhand's Strike mark (frame 15).
                Assert.Equal(OneOneWindUps.LentFrom(OneOneStitched.LentBackhand), s.From);
                blow = set.Quick[0] with { StrikeSeconds = 15f / 30f };
            }
            var (rate, busy, strikeFrame) = (blow.Rate, blow.BusySeconds, blow.StrikeSeconds * 30f);
            Assert.Equal(rate, s.Motion.Rate);
            Assert.Equal(busy, s.Motion.BusySeconds);
            Assert.Equal(clip.FrameOf(strikeFrame) / 30f, s.Motion.StrikeSeconds, 1e-5f);
            Assert.True(s.Motion.StrikeSeconds > s.ReadySeconds, $"{s.Direction}: the Strike mark after the ready point");
            Assert.True(s.Motion.Composed);
            output.WriteLine($"{name}: {s.Direction} {clip.Name} from {s.From}: {clip.Duration * 30f:0.##} frames, ready {s.ReadySeconds * 30f:0.##}, strike {s.Motion.StrikeSeconds * 30f:0.##}, rate {s.Motion.Rate:0.###}");
        }
        // The quick blows stay the flattened ones (1.1's tap-and-hold, attack-spin, plays them).
        Assert.All(set.Quick, q => Assert.StartsWith("flat:", assets.Clip(q.Clip).Name));
    }

    [Theory]
    [MemberData(nameof(SwordKinds))]
    public void Each_ready_pose_holds_the_blade_where_its_shape_puts_it_and_as_asked(string name, string? weapon)
    {
        var c = Character(name, weapon);
        var (assets, kind, set) = Registered(name, weapon, flat: true);
        var head = c.HeadBone;
        foreach (var s in set.Stitched)
        {
            var shape = ShapeOf(s.Direction);
            var (tip, guard, model) = Pose(c, assets.Clip(s.Motion.Clip), s.ReadySeconds);
            var way = Vector3.Normalize(tip - guard);
            float off = MathF.Acos(Math.Clamp(Vector3.Dot(way, Vector3.Normalize(shape.Blade)), -1f, 1f)) * 180f / MathF.PI;
            output.WriteLine($"{name} {s.Direction}: guard {guard.X:0.00} {guard.Y:0.00} {guard.Z:0.00} (asked {shape.Guard.X:0.00} {shape.Guard.Y:0.00} {shape.Guard.Z:0.00}), tip {tip.X:0.00} {tip.Y:0.00} {tip.Z:0.00}, blade {off:0.0}° off");
            Assert.True(Vector3.Distance(guard, shape.Guard) < 0.02f, $"{s.Direction}: the guard {Vector3.Distance(guard, shape.Guard):0.000} m from the shape's");
            Assert.True(off < 3f, $"{s.Direction}: the blade {off:0.0}° off the shape's");
            float headZ = model[head].Translation.Z;
            switch (s.Direction)
            {
                case "left":     // cocked over the left shoulder: the hand across the body, the blade up and back over the left
                    Assert.True(guard.X < -0.1f && guard.Z > 1.35f && tip.X < guard.X && tip.Y < guard.Y && tip.Z > headZ, "left: over the left shoulder");
                    break;
                case "right":    // cocked at the right shoulder: the hand right of the centre, the blade up, out to the right and back; not looped over the head
                    Assert.True(guard.X > 0.2f && guard.Z > 1.35f && tip.Y < guard.Y && tip.Z > headZ && tip.X > guard.X + 0.2f, "right: at the right shoulder");
                    break;
                case "overhead": // raised above the head: the hand at the crown, in front, the blade up and back over the head
                    Assert.True(guard.Z > headZ + 0.15f && tip.Z > headZ + 0.8f && tip.Y < guard.Y, "overhead: raised above the head");
                    break;
                case "thrust":   // drawn back at the hip, the point forward
                    Assert.True(guard.Z < 1.2f && guard.Y < 0f && way.Y > 0.9f, "thrust: drawn back at the hip, the point forward");
                    break;
            }
        }
    }

    /// <summary>
    /// The share of the blade from the guard (0) to the tip (1) the first-person picture shows at a ready pose, seen from the eyes along
    /// the body's facing, 80° up the picture, 16:9 (the defaults); <paramref name="hand"/> whether the weapon hand (the wrist) shows.
    /// </summary>
    static float SeenInFirstPerson(OneOneCharacter c, IAnimationClip clip, float s, out string what, out bool hand)
    {
        var sk = c.Model.Skeleton;
        var (tip, guard, model) = Pose(c, clip, s);
        var skin = new Matrix4x4[sk.Count];
        PoseEvaluator.ToSkin(sk, model, skin);
        var collapse = HeadCollapse.For(sk, c.HeadBone, c.Model.Vertices, OneOneModels.FacingYaw)!;
        var eye = collapse.EyesIn(skin);
        float halfUp = 40f * MathF.PI / 180f, halfAcross = MathF.Atan(MathF.Tan(halfUp) * 16f / 9f);
        bool Seen(Vector3 p)
        {
            var d = p - eye;
            return d.Y > 0.05f && MathF.Abs(MathF.Atan2(d.X, d.Y)) < halfAcross && MathF.Abs(MathF.Atan2(d.Z, d.Y)) < halfUp;
        }
        int seen = 0;
        for (int i = 0; i <= 20; i++) if (Seen(Vector3.Lerp(guard, tip, i / 20f))) seen++;
        hand = Seen(model[c.Limbs!.RightArm.End].Translation);
        what = $"eye {eye.X:0.00} {eye.Y:0.00} {eye.Z:0.00}";
        return seen / 21f;
    }

    [Theory]
    [MemberData(nameof(SwordKinds))]
    public void Each_ready_pose_shows_its_blade_at_the_edge_of_the_first_person_picture(string name, string? weapon)
    {
        var c = Character(name, weapon);
        var (assets, _, set) = Registered(name, weapon, flat: true);
        foreach (var s in set.Stitched)
        {
            float share = SeenInFirstPerson(c, assets.Clip(s.Motion.Clip), s.ReadySeconds, out var what, out bool hand);
            output.WriteLine($"{name} {s.Direction}: {share:P0} of the blade in the first-person picture, the hand {(hand ? "too" : "not")} ({what})");
            // The sides and the thrust show a fifth of the blade or more at the picture's edge; the overhead, raised above the eyes, its
            // hand at the top (its blade goes up out of the picture: no pose with it raised over the head shows more).
            if (s.Direction == "overhead") Assert.True(hand, "overhead: the hand at the top of the first-person picture");
            else Assert.True(share >= 0.2f, $"{s.Direction}: {share:P0} of the blade seen in first person");
        }
    }

    [Theory]
    [MemberData(nameof(SwordKinds))]
    public void From_the_join_on_the_blade_follows_its_blows_own_path_and_the_window_ends_where_it_did(string name, string? weapon)
    {
        var c = Character(name, weapon);
        var (assets, kind, set) = Registered(name, weapon, flat: true);
        var rule = new ActiveWindowRule();
        foreach (var s in set.Stitched)
        {
            var clip = (WindUpClip)assets.Clip(s.Motion.Clip);
            var shape = clip.Shape;
            float end = clip.Strike.Duration * 30f;
            float worst = 0f;
            for (float b = shape.JoinAt; b <= end; b += 0.25f)
            {
                var mine = Pose(c, clip, clip.FrameOf(b) / 30f).Tip;
                var its = Pose(c, clip.Strike, b / 30f).Tip;
                worst = MathF.Max(worst, Vector3.Distance(mine, its));
            }
            Assert.True(worst < 1e-3f, $"{s.Direction}: the tip {worst * 1000f:0.0} mm off the blow's own path after the join");
            // The window, measured on the wind-up, in the blow's frames.
            var w = ActiveWindows.Measure(assets, kind, s.Motion.Clip, s.Motion.StrikeSeconds, rule);
            float from = clip.BaseFrame(w.From * 30f), to = clip.BaseFrame(w.To * 30f);
            output.WriteLine($"{name} {s.Direction}: window {w.From * 30f:0.00}-{w.To * 30f:0.00} of the wind-up (the blow's {from:0.00}-{to:0.00}), peak {w.PeakSpeed:0.0} m/s; the join at the blow's {shape.JoinAt:0.##}; worst {worst * 1000f:0.000} mm");
            Assert.True(w.Has && to >= shape.JoinAt, $"{s.Direction}: the window runs past the join");
        }
    }

    [Theory]
    [MemberData(nameof(SwordKinds))]
    public void Every_directional_blow_peaks_near_the_forehand(string name, string? weapon)
    {
        var (assets, kind, set) = Registered(name, weapon, flat: true);
        var rule = new ActiveWindowRule();
        var right = set.Stitched.Single(s => s.Direction == "right").Motion;
        float forehand = ActiveWindows.Measure(assets, kind, right.Clip, right.StrikeSeconds, rule).PeakSpeed;
        foreach (var s in set.Stitched)
        {
            float peak = ActiveWindows.Measure(assets, kind, s.Motion.Clip, s.Motion.StrikeSeconds, rule).PeakSpeed;
            output.WriteLine($"{name} {s.Direction}: peaks at {peak:0.0} m/s a clip second (the forehand {forehand:0.0})");
            Assert.True(peak <= forehand * 1.1f, $"{s.Direction}: {peak:0.0} against the forehand's {forehand:0.0}");
            if (s.Direction == "left") Assert.True(MathF.Abs(peak - forehand) < forehand * 0.1f, $"the backhand {peak:0.0} against the forehand's {forehand:0.0}");
        }
        foreach (var q in set.Quick)
        {
            float peak = ActiveWindows.Measure(assets, kind, q.Clip, q.StrikeSeconds, rule).PeakSpeed;
            output.WriteLine($"{name} quick {assets.Clip(q.Clip).Name}: peaks at {peak:0.0}");
            Assert.True(peak <= forehand * 1.1f, $"{assets.Clip(q.Clip).Name}: {peak:0.0} against the forehand's {forehand:0.0}");
        }
    }

    [Theory]
    [MemberData(nameof(SwordKinds))]
    public void The_wind_ups_are_the_same_every_time_and_allocate_nothing(string name, string? weapon)
    {
        var c = Character(name, weapon);
        var (assets, _, set) = Registered(name, weapon, flat: true);
        var sk = c.Model.Skeleton;
        var a = new BoneTransform[sk.Count];
        var b = new BoneTransform[sk.Count];
        foreach (var s in set.Stitched)
        {
            var clip = assets.Clip(s.Motion.Clip);
            for (float t = 0; t <= clip.Duration; t += 0.05f)
            {
                clip.Sample(t, sk, a);
                clip.Sample(t, sk, b);
                Assert.True(a.AsSpan().SequenceEqual(b), $"{clip.Name} at {t}");
            }
            for (int i = 0; i < 100; i++) clip.Sample(i * 0.011f, sk, a);
            Allocations.None($"{clip.Name} sampled 2,000 times", () =>
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 2000; i++) clip.Sample(i * 0.0007f % clip.Duration, sk, a);
                return GC.GetAllocatedBytesForCurrentThread() - before;
            });
        }
    }
}
