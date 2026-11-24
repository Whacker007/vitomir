// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Combat;

namespace Vitomir.Platform.Tests;

/// <summary>
/// A blow struck while moving (<see cref="ActionHips"/>, <see cref="CharacterAssets.CarryActionHips"/>; the switch
/// <c>blows-on-moving-hips</c> turns it off; on by default). Found in the arc's pictures: walking, each wind-up's blade path tilts or turns, because
/// the action plays on the upper body over the walk's hips and loses the turn of its own. With the hips' turn carried, each blow's tip
/// through its window, walking and running, stays near where it is standing; without, it does not (the defect, measured); a standing
/// body's pose is the same bit for bit either way. Skips without the 1.1 install (VTMR_GAME_DIR).
/// </summary>
public sealed class ActionHipsTests(ITestOutputHelper output)
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);

    static (CharacterAssets Assets, int Kind, OneOneCharacter Character) Christof()
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        var c = Library.Value!.Character("Christof", "BroadSword") ?? throw new Xunit.Sdk.XunitException("Christof did not load");
        var assets = new CharacterAssets();
        int kind = OneOneLibrary.Register(assets, c, null, true);   // the session's: the flattened blows and the wind-ups
        return (assets, kind, c);
    }

    static Vector3 Tip(PoseEvaluator evaluator, CharacterAssets assets, OneOneCharacter c, in CharacterAnimation state)
    {
        int n = c.Model.Skeleton.Count;
        var model = new Matrix4x4[n];
        var skin = new Matrix4x4[n];
        evaluator.Evaluate(assets, state, model, skin);
        var blade = c.Held.SelectMany(h => h.Things).First(t => t.Kind == HeldKind.Blade);
        return Vector3.Transform(blade.Tip, model[blade.Bone]);
    }

    static CharacterAnimation State(CharacterAssets assets, int kind, MotionRole role, float time, int clip, float at)
    {
        var s = CharacterAnimation.For(assets, kind);
        var m = assets.Kind(kind).Motion(role);
        s.Role = role;
        s.Clip = m.Clip;
        s.Time = time;
        s.PreviousClip = -1;
        s.Blend = 1f;
        s.Action = ActionRole.AttackQuick;
        s.ActionClip = clip;
        s.ActionTime = at;
        s.ActionWeight = 1f;
        s.ActionLegs = role == MotionRole.Idle ? 1f : 0f;
        return s;
    }

    [Fact]
    public void Walking_and_running_each_blow_keeps_its_standing_path_with_the_hips_turn_carried_and_not_without()
    {
        var (assets, kind, c) = Christof();
        var set = assets.Kind(kind).Actions!;
        var evaluator = new PoseEvaluator();
        var rule = new ActiveWindowRule();
        var ic = CultureInfo.InvariantCulture;
        var results = new List<(string Direction, MotionRole Role, float Off, float On)>();
        foreach (var s in set.Stitched)
        {
            var w = ActiveWindows.Measure(assets, kind, s.Motion.Clip, s.Motion.StrikeSeconds, rule);
            foreach (var role in new[] { MotionRole.Walk, MotionRole.Run })
            {
                float off = 0f, on = 0f, low = float.MaxValue, high = float.MinValue, standLow = float.MaxValue, standHigh = float.MinValue;
                int i = 0;
                for (float t = w.From; t <= w.To; t += 1f / 120f, i++)
                {
                    float gait = 0.05f + i * 0.011f;    // the walk or run going on through the blow
                    assets.CarryActionHips = false;
                    var standing = Tip(evaluator, assets, c, State(assets, kind, MotionRole.Idle, 0f, s.Motion.Clip, t));
                    var before = Tip(evaluator, assets, c, State(assets, kind, role, gait, s.Motion.Clip, t));
                    assets.CarryActionHips = true;
                    var after = Tip(evaluator, assets, c, State(assets, kind, role, gait, s.Motion.Clip, t));
                    Assert.Equal(standing, Tip(evaluator, assets, c, State(assets, kind, MotionRole.Idle, 0f, s.Motion.Clip, t)));   // standing: the same bits
                    off = MathF.Max(off, Vector3.Distance(before, standing));
                    on = MathF.Max(on, Vector3.Distance(after, standing));
                    (low, high) = (MathF.Min(low, after.Z), MathF.Max(high, after.Z));
                    (standLow, standHigh) = (MathF.Min(standLow, standing.Z), MathF.Max(standHigh, standing.Z));
                }
                assets.CarryActionHips = false;
                output.WriteLine(string.Create(ic, $"{s.Direction} {role}: the tip from its standing place at most {off:0.00} m (on moving hips), {on:0.00} m with the hips' turn carried; its height {low:0.00}-{high:0.00} m (standing {standLow:0.00}-{standHigh:0.00})"));
                results.Add((s.Direction, role, off, on));
            }
        }
        foreach (var (direction, role, off, on) in results)
        {
            // The thrust is the halberd's lunge: its hips go forward under it, which is not carried (the upper body stays on the
            // moving hips), so its point falls up to 0.45 m short of its standing place; the rest within 0.3 m.
            Assert.True(on < (direction == "thrust" ? 0.5f : 0.3f), $"{direction} {role}: {on:0.00} m from the standing path with the hips' turn carried");
            Assert.True(off > 2f * on, $"{direction} {role}: without it {off:0.00} m, with it {on:0.00} m");
        }
    }
}
