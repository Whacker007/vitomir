// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The guards' left ("Yes go ahead"):
/// 1.1's guards and watchmen swing only the forehand, so each kind of Christof's skeleton with a one-handed blade and no backhand is
/// lent Christof's — flattened as his, the off hand from the kind's own stand, with the left's wind-up — registered first among its
/// wind-ups (the switch <c>guards-one-blow</c> keeps the earlier build's three); its blade sweeps from the guard's left to his right, level at the
/// chest, the shield or torch kept where the stance holds it; the direction table finds it for the duelling brain; it meets a standing
/// townsman. Every test skips without the 1.1 install (VTMR_GAME_DIR). (WindUpTests covers its ready pose, its join, its window, its
/// peak, its determinism and its allocations with the other wind-ups, on every kind.)
/// </summary>
public sealed class GuardLeftTests(ITestOutputHelper output)
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

    public static readonly TheoryData<string> GuardKinds = new() { "rudolfGuard", "KnightOfStJohn_torch", "viennaGuard_sword" };

    static HeldThing Blade(OneOneCharacter c) => c.Held.SelectMany(h => h.Things).First(t => t.Kind == HeldKind.Blade);

    static Matrix4x4[] Model(OneOneCharacter c, IAnimationClip clip, float s)
    {
        var sk = c.Model.Skeleton;
        var local = new BoneTransform[sk.Count];
        var model = new Matrix4x4[sk.Count];
        PoseEvaluator.Rest(sk, local);
        clip.Sample(s, sk, local);
        PoseEvaluator.ToModel(sk, local, model);
        return model;
    }

    [Theory]
    [MemberData(nameof(GuardKinds))]
    public void Each_guard_kind_is_lent_Christof_s_backhand_as_its_left_and_the_switch_keeps_the_three(string name)
    {
        var c = Character(name);
        Assert.NotNull(c.LentLeft);
        Assert.Equal("left", c.LentLeft!.Direction);
        Assert.Equal("lend:chrisAttack2Sword", c.LentLeft.From);
        Assert.DoesNotContain(c.Actions!.Quick, q => q.Clip.Name == OneOneStitched.LentBackhand);   // 1.1 gives it none of its own

        var with = new CharacterAssets();
        var set = with.Kind(OneOneLibrary.Register(with, c, null, true)).Actions!;
        Assert.Equal(["left", "right", "overhead", "thrust"], set.Stitched.Select(s => s.Direction));
        Assert.Equal("windup:left", with.Clip(set.Stitched[0].Motion.Clip).Name);

        var without = new CharacterAssets();
        var kept = without.Kind(OneOneLibrary.Register(without, c, null, true, lentLeft: false)).Actions!;
        Assert.Equal(["right", "overhead", "thrust"], kept.Stitched.Select(s => s.Direction));
        Assert.Equal(with.ClipCount - 1, without.ClipCount);
        // swings-oneone: nothing lent (the earlier build's stitched blows and 1.1's own).
        var oneone = new CharacterAssets();
        Assert.Equal(["overhead", "thrust"], oneone.Kind(OneOneLibrary.Register(oneone, c, null, false)).Actions!.Stitched.Select(s => s.Direction));
        // Christof has his own backhand: nothing lent.
        Assert.Null(Character("Christof", "Rapier").LentLeft);
        output.WriteLine($"{name}: left from {c.LentLeft.From}, rate {c.LentLeft.Rate:0.###}, strike {c.LentLeft.StrikeSeconds * 30f:0.##}, busy {c.LentLeft.BusySeconds:0.###} s; quick {c.Actions.Quick[0].Clip.Name} rate {c.Actions.Quick[0].Rate:0.###}, busy {c.Actions.Quick[0].BusySeconds:0.###} s");
        Assert.Equal(c.Actions.Quick[0].Rate, c.LentLeft.Rate, 1e-4f);
        Assert.Equal(c.Actions.Quick[0].BusySeconds, c.LentLeft.BusySeconds, 1e-4f);
    }

    [Theory]
    [MemberData(nameof(GuardKinds))]
    public void Through_its_window_the_lent_left_sweeps_from_his_left_to_his_right_level_at_the_chest_the_off_hand_kept(string name)
    {
        var c = Character(name);
        var assets = new CharacterAssets();
        int kind = OneOneLibrary.Register(assets, c, null, true);
        var left = assets.Kind(kind).Actions!.Stitched[0].Motion;
        var clip = (WindUpClip)assets.Clip(left.Clip);
        var w = ActiveWindows.Measure(assets, kind, left.Clip, left.StrikeSeconds, new ActiveWindowRule());
        Assert.True(w.Has);
        var b = Blade(c);
        float first = float.NaN, last = float.NaN, low = float.MaxValue, high = float.MinValue;
        for (float t = w.From; t <= w.To + 1e-4f; t += 1f / 120f)
        {
            var m = Model(c, clip, t);
            var tip = Vector3.Transform(b.Tip, m[b.Bone]);
            float heading = MathF.Atan2(tip.X, tip.Y) * 180f / MathF.PI;   // 0 ahead, + to his right
            if (float.IsNaN(first)) first = heading;
            last = heading;
            if (MathF.Abs(heading) < 75f) { low = MathF.Min(low, tip.Z); high = MathF.Max(high, tip.Z); }
        }
        // The off hand: the left arm as the kind's stand holds it, all through the clip — the hand where the stand has it from the
        // shoulder (the body turns and bows with the blow; the arm on it is the stand's).
        var stand = c.Role(MotionRole.Idle).Clip!;
        int hand = c.Limbs!.LeftArm.End, shoulder = c.Model.Skeleton.Bones[c.Limbs.LeftArm.Upper].Parent;
        Vector3 FromShoulder(Matrix4x4[] m) => Matrix4x4.Invert(m[shoulder], out var inv) ? Vector3.Transform(m[hand].Translation, inv) : Vector3.Zero;
        var standHand = FromShoulder(Model(c, stand, 0f));
        float drift = 0f;
        for (float t = 0f; t <= clip.Duration; t += 0.05f) drift = MathF.Max(drift, Vector3.Distance(FromShoulder(Model(c, clip, t)), standHand));
        output.WriteLine(FormattableString.Invariant($"{name}: window {w.From * 30f:0.0}-{w.To * 30f:0.0} frames, the tip from {first:0}° to {last:0}° (0 ahead, + right), in front {low:0.00}-{high:0.00} m, peak {w.PeakSpeed:0.0} m/s; the off hand at most {drift * 100f:0.0} cm from where the stand holds it from the shoulder"));
        Assert.True(first < -30f && last > 30f, $"from his left ({first:0}°) to his right ({last:0}°)");
        Assert.InRange(low, OneOneStitched.SwingHeight - 0.15f, OneOneStitched.SwingHeight + 0.15f);
        Assert.InRange(high, OneOneStitched.SwingHeight - 0.15f, OneOneStitched.SwingHeight + 0.15f);
        Assert.True(drift < 0.03f, $"the off hand {drift:0.00} m from where the stand holds it");
    }

    [Theory]
    [MemberData(nameof(GuardKinds))]
    public void The_direction_table_gives_the_brain_a_left_for_him_and_it_meets_a_standing_townsman(string name)
    {
        var assets = new CharacterAssets();
        int g = OneOneLibrary.Register(assets, Character(name), null, true);
        int t = OneOneLibrary.Register(assets, Character("Civ12male"));
        var b = new WorldBuilder(7);
        MovementServices.Register(b, new TestLevel().Floor(-60, -60, 60, 60).Build());
        MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = false });
        b.Animation(assets);
        b.HitVolumes();
        BodyMotionSystem.Register(b);
        CombatServices.Register(b);
        ContactServices.Register(b, new ContactConfig());
        var dirs = new DirectionalAttacks { Enabled = true, Settings = new DirectionSettings(), Rows = LowBodyTests.Rows };
        b.Service(dirs);
        var w = b.Build();
        w.Service<HitVolumes>().Prepare(assets);
        ContactServices.Prepare(w);
        Assert.True(dirs.Has(w, assets, g, AttackDirection.Left), "the brain's draw finds a left for him");
        var m = dirs.Motion(w, assets, g, AttackDirection.Left);
        Assert.Equal("windup:left", assets.Clip(m.Clip).Name);

        EntityId Put(int kind, float x, float y, float yaw)
        {
            var e = MovementServices.SpawnBody(w, new(x, y, 0.01f), yaw * DetMath.DegToRad, Locomotion.Sandbox);
            w.Set(e, CharacterAnimation.For(assets, kind));
            w.Set(e, MotionInput.Standing);
            CombatServices.Arm(w, e);
            return e;
        }
        var a = Put(g, 0, 0, 90);
        var foe = Put(t, 0, 1.4f, -90);
        var contacts = new List<ContactMade>();
        void Step(int n) { for (int i = 0; i < n; i++) { MovementServices.Step(w, ActionFrame.Idle); contacts.AddRange(w.Events<ContactMade>().ThisTick); } }
        Step(60);
        ref var intent = ref w.Get<CombatIntent>(a);
        intent.Aims = true; intent.AimPitch = 0f; intent.Attack = true; intent.Direction = AttackDirection.Left;
        Step(1);
        w.Get<CombatIntent>(a).Attack = false;
        Step(200);
        output.WriteLine($"{name}: {string.Join("; ", contacts.Select(c => FormattableString.Invariant($"{c.Met} {c.Site} at {c.Point.Z:0.00} m ({c.Point.X:0.00} across), {c.ImpactSpeed:0.0} m/s")))}");
        var hit = Assert.Single(contacts);
        Assert.Equal(foe, hit.Target);
        Assert.Equal(ContactMet.Body, hit.Met);
        Assert.InRange(hit.Point.Z, 1.15f, 1.65f);
    }
}
