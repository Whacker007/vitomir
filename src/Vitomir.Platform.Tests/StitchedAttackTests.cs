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
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The overhead and the thrust stitched from 1.1's own clips: every
/// sword-bearing human kind the platform plays has both and nothing else does; each loads, plays and has its mark, its window
/// and its ready point; the overhead's tip comes down from above the head in front, the thrust's goes forward along the aim; the
/// off hand is off the grip and keeps what it holds; contact meets a standing townsman's head with the overhead and his belly
/// with the thrust; the same every time; nothing allocated; and nothing plays them unless asked. Every test skips without
/// the 1.1 install (VTMR_GAME_DIR).
/// </summary>
public sealed class StitchedAttackTests(ITestOutputHelper output)
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

    /// <summary>The sword-bearing human kinds the platform plays (Christof with his broadsword or a rapier; the guard; the night watchman; Vienna's guard).</summary>
    public static readonly TheoryData<string, string?> SwordKinds = new()
    {
        { "Christof", "BroadSword" }, { "Christof", "Rapier" }, { "rudolfGuard", null }, { "KnightOfStJohn_torch", null }, { "viennaGuard_sword", null },
    };

    static (CharacterAssets Assets, int Kind, ActionSet Set) Registered(string name, string? weapon, string? asQuick = null)
    {
        var assets = new CharacterAssets();
        int kind = OneOneLibrary.Register(assets, Character(name, weapon), asQuick);
        return (assets, kind, assets.Kind(kind).Actions!);
    }

    static HeldThing Blade(OneOneCharacter c) => c.Held.SelectMany(h => h.Things).First(t => t.Kind == HeldKind.Blade);

    /// <summary>The blade's tip and guard (the kind's model space) at clip seconds <paramref name="s"/>, and the bones.</summary>
    static (Vector3 Tip, Vector3 Guard, Matrix4x4[] Model) Pose(OneOneCharacter c, IAnimationClip clip, float s)
    {
        var sk = c.Model.Skeleton;
        var local = new BoneTransform[sk.Count];
        var model = new Matrix4x4[sk.Count];
        clip.Sample(s, sk, local);
        PoseEvaluator.ToModel(sk, local, model);
        var b = Blade(c);
        return (Vector3.Transform(b.Tip, model[b.Bone]), Vector3.Transform(b.Guard, model[b.Bone]), model);
    }

    // ---- who has them ----

    [Theory]
    [MemberData(nameof(SwordKinds))]
    public void Every_sword_bearing_human_kind_has_an_overhead_and_a_thrust_played_as_its_quick_blow_is(string name, string? weapon)
    {
        var c = Character(name, weapon);
        var (assets, _, set) = Registered(name, weapon);
        Assert.Equal([StitchedAttacks.Overhead, StitchedAttacks.Thrust], set.Stitched.Select(s => s.Direction));
        Assert.Equal([OneOneStitched.OverheadFrom, OneOneStitched.ThrustFrom], set.Stitched.Select(s => s.From));
        var quick = set.Quick[0];
        foreach (var s in set.Stitched)
        {
            var clip = assets.Clip(s.Motion.Clip);
            Assert.Equal(StitchedAttacks.ClipName(s.Direction), clip.Name);
            Assert.True(s.Motion.Composed, "a stand-in composed by the platform (SANDBOX)");
            Assert.False(clip.Loops);
            // As 1.1 plays the weapon's quick blow: the whole clip lasts its STRIKETIME, busy its RELOADTIME, the source's Strike mark.
            Assert.Equal(c.Weapon!.StrikeTime[0], clip.Duration / s.Motion.Rate, 4);
            Assert.Equal(quick.BusySeconds, s.Motion.BusySeconds);
            var stitched = (StitchedClip)clip;
            var source = (NadClip)stitched.Base;
            float mark = source.Tags.First(t => t.Type == OneOneActions.StrikeTag).Frame;   // the thrust's time is warped: its mark where the source's plays
            Assert.Equal((stitched.Warp is { } warp ? warp.FrameOf(mark) : mark) / 30f, s.Motion.StrikeSeconds, 4);
            Assert.Equal(s.Motion, set.Stitched(s.Direction));
            output.WriteLine($"{name} ({weapon ?? c.Weapon.Template}): {clip.Name} from {s.From}, rate {s.Motion.Rate:0.000}, strike {s.Motion.StrikeSeconds * 30:0.#} of {clip.Duration * 30:0} frames, ready {s.ReadySeconds * 30:0.#}, busy {s.Motion.BusySeconds:0.00} s");
        }
    }

    [Theory]
    [InlineData("Christof", null, "not a one-handed weapon")]          // his template's Punch: bare hands
    [InlineData("TeutonicKnight", null, "not a one-handed weapon")]    // the great sword, two-handed, skeleton 1
    [InlineData("Civ12male", null, "not a one-handed weapon")]         // a townsman carries nothing
    [InlineData("Civ02beggar", null, "")]                               // the beggar's torch is one-handed and of Christof's skeleton, but he has no blow
    public void Bare_hands_two_handed_weapons_and_those_with_no_blow_get_none(string name, string? weapon, string why)
    {
        var c = Character(name, weapon);
        Assert.Empty(c.Stitched);
        var (_, _, set) = Registered(name, weapon);
        Assert.Empty(set.Stitched);
        Assert.False(set.Stitched(StitchedAttacks.Overhead).Has);
        Assert.Equal(-1f, set.StitchedReady(StitchedAttacks.Thrust));
        if (why.Length > 0) Assert.Equal(why, OneOneStitched.Why(c.Model.Skeleton, c.Limbs, c.Weapon, c.Role(MotionRole.Idle).Clip));
        else Assert.Empty(c.Actions!.Quick);
    }

    [Fact]
    public void The_family_is_Christof_s_skeleton_and_his_people_share_it_bone_for_bone()
    {
        foreach (var name in new[] { "Christof", "rudolfGuard", "KnightOfStJohn_torch", "viennaGuard_sword" })
            Assert.True(OneOneStitched.OfFamily(Character(name).Model.Skeleton), name);
        Assert.False(OneOneStitched.OfFamily(Character("TeutonicKnight").Model.Skeleton));   // 35 bones
        Assert.False(OneOneStitched.OfFamily(Character("Civ11female").Model.Skeleton));      // 1.1's women: 48 bones
    }

    // ---- the motions ----

    [Theory]
    [MemberData(nameof(SwordKinds))]
    public void Each_loads_and_plays_with_its_window_round_its_mark_and_its_ready_point_before_it(string name, string? weapon)
    {
        var c = Character(name, weapon);
        var (assets, kind, set) = Registered(name, weapon);
        var windows = new ActiveWindows();
        windows.Prepare(assets, new ActiveWindowRule());
        foreach (var s in set.Stitched)
        {
            var clip = assets.Clip(s.Motion.Clip);
            for (float t = 0; t <= clip.Duration; t += 1f / 60f)
            {
                var (tip, guard, _) = Pose(c, clip, t);
                Assert.True(float.IsFinite(tip.X + tip.Y + tip.Z + guard.X + guard.Y + guard.Z), $"{clip.Name} at {t}");
                Assert.InRange((tip - guard).Length(), Blade(c).Length * 0.99f, Blade(c).Length * 1.01f);   // the blade stays a blade
            }
            var w = windows.For(assets, kind, s.Motion.Clip, s.Motion.StrikeSeconds, new ActiveWindowRule());
            output.WriteLine($"{name}: {clip.Name} window {w.From * 30:0.0}-{w.To * 30:0.0} frames, strike {w.Strike * 30:0.#}, peak {w.PeakSpeed:0.0} m/s, ready {s.ReadySeconds * 30:0.#}");
            Assert.True(w.Has && w.FromMotion, $"{clip.Name}: {w}");
            // 1.1's Strike mark in the window, or (the overhead, its bow kept shallower: the tip slows below 40 % of its peak at 14.8)
            // within a frame after it.
            Assert.InRange(s.Motion.StrikeSeconds, w.From, w.To + 1f / 30f);
            Assert.InRange(s.ReadySeconds, w.From - 0.1f, w.From);             // the wind-up done, just before the window
            Assert.InRange(w.PeakSpeed, 12f, 60f);
        }
    }

    [Theory]
    [MemberData(nameof(SwordKinds))]
    public void The_overhead_s_tip_comes_down_from_above_the_head_in_front_on_the_centre_line(string name, string? weapon)
    {
        var c = Character(name, weapon);
        var (assets, kind, set) = Registered(name, weapon);
        var m = set.Stitched(StitchedAttacks.Overhead);
        var clip = assets.Clip(m.Clip);
        var w = ActiveWindows.Measure(assets, kind, m.Clip, m.StrikeSeconds, new ActiveWindowRule());
        var (start, _, model) = Pose(c, clip, w.From);
        float head = model[c.HeadBone].Translation.Z;
        var (strike, _, _) = Pose(c, clip, m.StrikeSeconds);
        var (end, _, _) = Pose(c, clip, w.To);
        float highest = float.MinValue;
        for (float t = w.From; t <= w.To; t += 1f / 240f) highest = MathF.Max(highest, Pose(c, clip, t).Tip.Z);
        output.WriteLine($"{name}: the tip from {start} (the head at {head:0.00}) through {strike} at the strike to {end}; highest {highest:0.00}");
        Assert.True(highest > head + 0.5f, $"above the head: {highest:0.00} against {head:0.00}");
        Assert.True(strike.Y > 1.2f && end.Y > 1.2f, "in front at the strike and after");
        Assert.True(highest - strike.Z > 1f, "down by more than a metre");
        Assert.InRange(strike.X, -0.12f, 0.12f);                           // on the centre line, where the aim is
        // A vertical swing: across the window the tip moves less sideways than a tenth of its way down and forward.
        Assert.True(MathF.Abs(end.X - start.X) < 0.25f * MathF.Max(highest - end.Z, end.Y - start.Y), $"{start} to {end}");
    }

    [Theory]
    [MemberData(nameof(SwordKinds))]
    public void The_thrust_s_tip_goes_forward_along_the_aim_point_first(string name, string? weapon)
    {
        var c = Character(name, weapon);
        var (assets, kind, set) = Registered(name, weapon);
        var m = set.Stitched(StitchedAttacks.Thrust);
        var clip = assets.Clip(m.Clip);
        var w = ActiveWindows.Measure(assets, kind, m.Clip, m.StrikeSeconds, new ActiveWindowRule());
        var (start, _, _) = Pose(c, clip, w.From);
        var (strike, guard, _) = Pose(c, clip, m.StrikeSeconds);
        var (end, endGuard, _) = Pose(c, clip, w.To);
        var d = end - start;
        output.WriteLine($"{name}: the tip from {start} through {strike} to {end}; the blade at the strike {Vector3.Normalize(strike - guard)}");
        Assert.True(d.Y > 1f, $"forward more than a metre: {d}");
        Assert.True(MathF.Abs(d.X) < 0.4f * d.Y && MathF.Abs(d.Z) < 0.3f * d.Y, $"along the aim: {d}");
        Assert.True(Vector3.Normalize(strike - guard).Y > 0.9f && Vector3.Normalize(end - endGuard).Y > 0.9f, "the point leads");
        Assert.InRange(end.X, -0.25f, 0.25f);
        Assert.InRange(strike.Z, 1.2f, 1.7f);                              // aimed level, toward the eyes' height in front (the crosshair)
    }

    [Theory]
    [MemberData(nameof(SwordKinds))]
    public void Through_its_window_the_thrust_s_tip_moves_straight_along_the_aim_and_the_blade_points_along_it(string name, string? weapon)
    {
        var c = Character(name, weapon);
        var (assets, kind, set) = Registered(name, weapon);
        var m = set.Stitched(StitchedAttacks.Thrust);
        var clip = (StitchedClip)assets.Clip(m.Clip);
        var reach = clip.Reach!;
        var w = ActiveWindows.Measure(assets, kind, m.Clip, m.StrikeSeconds, new ActiveWindowRule());
        // The aim: the line through the ready guard toward the centre line's point at the eyes' height ThrustAimDistance ahead.
        var sk = c.Model.Skeleton;
        var local = new BoneTransform[sk.Count]; var model = new Matrix4x4[sk.Count];
        c.Role(MotionRole.Idle).Clip!.Sample(0f, sk, local);
        PoseEvaluator.ToModel(sk, local, model);
        var aimPoint = new Vector3(0f, OneOneStitched.ThrustAimDistance, model[c.HeadBone].Translation.Z + OneOneStitched.ThrustAimOverHead);
        var toAim = Vector3.Normalize(aimPoint - reach.From);
        Assert.True(Vector3.Dot(toAim, reach.Direction) > 0.9999f, "the line is aimed at the aim point");
        float worstMove = 0f, worstBlade = 0f, moved = 0f;
        var last = Pose(c, clip, w.From).Tip;
        for (float t = w.From + 1f / 240f; t <= w.To; t += 1f / 240f)
        {
            var (tip, guard, _) = Pose(c, clip, t);
            var step = tip - last;
            if (step.Length() > 0.004f)                                    // the tip moving (more than about 1 m/s at 240 a second)
            {
                worstMove = MathF.Max(worstMove, MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(step), toAim), -1f, 1f)) * 57.2958f);
                moved += step.Length();
            }
            worstBlade = MathF.Max(worstBlade, MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(tip - guard), toAim), -1f, 1f)) * 57.2958f);
            last = tip;
        }
        output.WriteLine($"{name}: through the window ({w.From * 30:0.0}-{w.To * 30:0.0}) the tip moved {moved:0.00} m, at most {worstMove:0.0}° off the aim; the blade at most {worstBlade:0.0}° off it; the aim {toAim}");
        Assert.True(moved > 1.2f, $"a drive, not a jab: {moved:0.00} m");
        Assert.True(worstMove < 10f, $"the tip moves along the aim: at most {worstMove:0.0}° off it");
        Assert.True(worstBlade < 5f, $"the point leads along the aim: the blade at most {worstBlade:0.0}° off it");
    }

    [Theory]
    [InlineData("Christof", "BroadSword")]
    [InlineData("rudolfGuard", null)]
    [InlineData("KnightOfStJohn_torch", null)]
    public void The_off_hand_is_off_the_grip_and_keeps_what_it_holds_as_the_stance_does(string name, string? weapon)
    {
        var c = Character(name, weapon);
        var sk = c.Model.Skeleton;
        var stand = c.Role(MotionRole.Idle).Clip!;
        var offHand = OneOneStitched.Subtree(sk, c.Limbs!.LeftArm.Upper);
        Assert.True(offHand[c.Limbs.LeftArm.End] && offHand[30], "the hand and bone 30 (where a shield is held) are the off hand's");
        foreach (var b in c.Stitched)
        {
            var source = (NadClip)((StitchedClip)b.Clip).Base;
            float strike = b.StrikeSeconds;
            var a = new BoneTransform[sk.Count]; var s = new BoneTransform[sk.Count];
            var model = new Matrix4x4[sk.Count];
            source.Sample(strike, sk, a);
            PoseEvaluator.ToModel(sk, a, model);
            float gripBefore = (model[c.Limbs.LeftArm.End].Translation - model[c.Limbs.RightArm.End].Translation).Length();
            b.Clip.Sample(strike, sk, a);
            PoseEvaluator.ToModel(sk, a, model);
            float gripAfter = (model[c.Limbs.LeftArm.End].Translation - model[c.Limbs.RightArm.End].Translation).Length();
            stand.Sample(strike - MathF.Floor(strike / stand.Duration) * stand.Duration, sk, s);
            output.WriteLine($"{name} {b.Direction}: the hands {gripBefore:0.00} m apart in {source.Name} at its strike, {gripAfter:0.00} m stitched");
            Assert.True(gripAfter > gripBefore + 0.1f || gripBefore > 0.3f && gripAfter > 0.3f, "the off hand is off the grip");
            for (int i = 0; i < sk.Count; i++)
                if (offHand[i] && i != c.Limbs.LeftArm.Upper)
                    Assert.Equal(s[i], a[i]);                                  // below the shoulder, the stance's arm exactly
        }
    }

    // ---- contact ----

    sealed class Ring
    {
        public required World World { get; init; }
        public required Dictionary<string, int> Kinds { get; init; }
        public CharacterAssets Assets => World.Service<CharacterAssets>();
        public readonly List<ContactMade> Contacts = [];
        public readonly List<AttackBegun> Begun = [];

        public void Idle(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                MovementServices.Step(World, ActionFrame.Idle);
                Contacts.AddRange(World.Events<ContactMade>().ThisTick);
                Begun.AddRange(World.Events<AttackBegun>().ThisTick);
            }
        }

        public EntityId Put(string kind, float x, float y, float yawDegrees)
        {
            var e = MovementServices.SpawnBody(World, new(x, y, 0.01f), yawDegrees * DetMath.DegToRad, Locomotion.Sandbox);
            World.Set(e, CharacterAnimation.For(Assets, Kinds[kind]));
            World.Set(e, MotionInput.Standing);
            CombatServices.Arm(World, e);
            return e;
        }

        public void Swing(EntityId who, float aimDegrees = 0f)
        {
            ref var intent = ref World.Get<CombatIntent>(who);
            intent.Aims = true;
            intent.AimPitch = aimDegrees * DetMath.DegToRad;
            intent.Attack = true;
            Idle(1);
            World.Get<CombatIntent>(who).Attack = false;
        }
    }

    /// <summary>A world with contact on a floor: <paramref name="name"/> (its stitched blows as its quick blows, <paramref name="asQuick"/>) and a townsman.</summary>
    static Ring Make(string name, string? weapon, string? asQuick, ulong seed = 7)
    {
        var assets = new CharacterAssets();
        var kinds = new Dictionary<string, int>
        {
            [name] = OneOneLibrary.Register(assets, Character(name, weapon), asQuick),
            ["Civ12male"] = OneOneLibrary.Register(assets, Character("Civ12male")),
        };
        var b = new WorldBuilder(seed);
        MovementServices.Register(b, new TestLevel().Floor(-60, -60, 60, 60).Build());
        MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = false });
        b.Animation(assets);
        b.HitVolumes();
        BodyMotionSystem.Register(b);
        CombatServices.Register(b);
        ContactServices.Register(b, new ContactConfig());
        var w = b.Build();
        w.Service<HitVolumes>().Prepare(assets);
        ContactServices.Prepare(w);
        return new Ring { World = w, Kinds = kinds };
    }

    /// <summary><paramref name="name"/> swings its stitched <paramref name="direction"/> once, level, at a townsman <paramref name="d"/> m before it.</summary>
    (Ring R, EntityId A, EntityId T) Blow(string name, string? weapon, string direction, float d = 1.4f, float aim = 0f)
    {
        var r = Make(name, weapon, direction);
        var a = r.Put(name, 0, 0, 90);
        var t = r.Put("Civ12male", 0, d, -90);
        r.Idle(30);
        r.Swing(a, aim);
        r.Idle((int)MathF.Round(1.3f * World.TicksPerSecond));
        output.WriteLine($"{name} {direction} at {d} m, aim {aim}°: {string.Join("; ", r.Contacts.Select(c => $"{c.Met} {c.Site} at {c.Point.Z:0.00} m, {c.ImpactSpeed:0.0} m/s (point {c.PointSpeed:0.0}, peak {c.PeakSpeed:0.0})"))}");
        return (r, a, t);
    }

    [Theory]
    [InlineData("Christof", "BroadSword", "overhead", "head,neck,left upper arm,right upper arm,chest")]
    [InlineData("Christof", "BroadSword", "thrust", "chest,belly,neck")]
    [InlineData("Christof", "Rapier", "overhead", "head,neck,left upper arm,right upper arm,chest")]
    [InlineData("Christof", "Rapier", "thrust", "chest,belly,neck")]
    [InlineData("rudolfGuard", null, "overhead", "head,neck,left upper arm,right upper arm,chest")]
    [InlineData("rudolfGuard", null, "thrust", "chest,belly,neck")]
    [InlineData("KnightOfStJohn_torch", null, "overhead", "head,neck,left upper arm,right upper arm,chest")]
    [InlineData("KnightOfStJohn_torch", null, "thrust", "chest,belly,neck")]
    public void Contact_meets_a_standing_townsman_s_head_with_the_overhead_and_his_trunk_with_the_thrust(string name, string? weapon, string direction, string sites)
    {
        var (r, a, t) = Blow(name, weapon, direction);
        var begun = Assert.Single(r.Begun);
        Assert.Equal(StitchedAttacks.ClipName(direction), r.Assets.Clip(begun.Clip).Name);
        var c = Assert.Single(r.Contacts);
        Assert.Equal(ContactMet.Body, c.Met);
        Assert.Equal(a, c.Attacker);
        Assert.Equal(t, c.Target);
        Assert.Contains(c.Site, sites.Split(','));
        Assert.InRange(c.ImpactSpeed, 3f, 60f);
        Assert.True(c.AlongBlade > 0.3f, $"met with the blade, not at the hilt: {c.AlongBlade:0.00}");
        if (direction == StitchedAttacks.Overhead) Assert.True(c.Point.Z > 1.35f, $"high: {c.Point.Z:0.00}");
        else
        {
            Assert.InRange(c.Point.Z, 1.2f, 1.6f);                      // aimed level: the chest, toward the crosshair
            Assert.True(c.ImpactSpeed > 12f, $"a strike, not a touch: {c.ImpactSpeed:0.0} m/s into the body");
            Assert.True(c.AlongBlade > 0.9f, $"the point: {c.AlongBlade:0.00}");
        }
    }

    [Fact]
    public void Aimed_down_at_the_belly_the_thrust_meets_it_and_aimed_down_the_overhead_still_falls_on_the_head_first()
    {
        ContactMade First(string dir, float aim) => Blow("Christof", "BroadSword", dir, aim: aim).R.Contacts[0];
        // The crosshair on the townsman's belly (1.1 m) from the eyes (1.59 m) 1.4 m away: 19° down. The thrust follows the aim.
        var level = First("thrust", 0f); var low = First("thrust", -19f);
        Assert.True(low.Point.Z < level.Point.Z - 0.15f, $"thrust level {level.Point.Z:0.00}, aimed at the belly {low.Point.Z:0.00}");
        Assert.Contains(low.Site, new[] { "belly", "hips" });
        Assert.True(low.ImpactSpeed > 10f, $"{low.ImpactSpeed:0.0} m/s");
        // The overhead comes down from above: bent down by the aim, it still meets the top of the man first (the aim offset moves
        // where it ends, not what it falls on).
        Assert.Equal("head", First("overhead", -20f).Site);
    }

    // ---- the same every time, nothing allocated, nothing played unless asked ----

    [Fact]
    public void A_stitched_clip_samples_the_same_every_time_and_allocates_nothing()
    {
        var c = Character("rudolfGuard");
        var sk = c.Model.Skeleton;
        var a = new BoneTransform[sk.Count];
        var b = new BoneTransform[sk.Count];
        foreach (var s in c.Stitched)
        {
            for (float t = 0; t <= s.Clip.Duration; t += 0.05f)
            {
                s.Clip.Sample(t, sk, a);
                s.Clip.Sample(t, sk, b);
                Assert.True(a.AsSpan().SequenceEqual(b), $"{s.Clip.Name} at {t}");
            }
            for (int i = 0; i < 100; i++) s.Clip.Sample(i * 0.011f, sk, a);
            Allocations.None($"{s.Clip.Name} sampled 2,000 times", () =>
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 2000; i++) s.Clip.Sample(i * 0.0007f % s.Clip.Duration, sk, a);
                return GC.GetAllocatedBytesForCurrentThread() - before;
            });
        }
    }

    [Fact]
    public void Two_worlds_swinging_the_overhead_and_the_thrust_agree_digest_for_digest()
    {
        ulong[] Run(string dir)
        {
            var r = Make("Christof", "BroadSword", dir, seed: 11);
            var a = r.Put("Christof", 0, 0, 90);
            r.Put("Civ12male", 0, 1.4f, -90);
            r.Idle(30);
            r.Swing(a);
            var digests = new ulong[12];
            for (int i = 0; i < digests.Length; i++) { r.Idle(15); digests[i] = r.World.Digest(); }
            return digests;
        }
        foreach (var dir in new[] { "overhead", "thrust" }) Assert.Equal(Run(dir), Run(dir));
        Assert.NotEqual(Run("overhead"), Run("thrust"));
    }

    [Fact]
    public void Unless_asked_the_quick_blows_are_1_1_s_and_1_1_s_clips_keep_their_places()
    {
        var (assets, _, set) = Registered("Christof", "BroadSword");
        Assert.Equal(["chrisAttackSword", "chrisAttack2Sword"], set.Quick.Select(q => assets.Clip(q.Clip).Name));
        Assert.Equal("ChrisAttackSSword", assets.Clip(set.Strong.Clip).Name);
        // The stitched clips come after every one of 1.1's the kind registers.
        int last1_1 = new[] { set.Strong.Clip, set.Block.Clip, set.Hurt.Clip, set.DeathQuick.Clip, set.DeathSlow.Clip }.Concat(set.Quick.Select(q => q.Clip)).Max();
        Assert.All(set.Stitched, s => Assert.True(s.Motion.Clip > last1_1));
        Assert.Equal(assets.ClipCount - 2, set.Stitched[0].Motion.Clip);
        // Asked: the quick blows are the stitched ones (for the films and the references), the rest as it was.
        var (a2, _, both) = Registered("Christof", "BroadSword", "both");
        Assert.Equal(["stitched:overhead", "stitched:thrust"], both.Quick.Select(q => a2.Clip(q.Clip).Name));
        Assert.Equal("ChrisAttackSSword", a2.Clip(both.Strong.Clip).Name);
        var (a3, _, one) = Registered("rudolfGuard", null, "thrust");
        Assert.Equal(["stitched:thrust"], one.Quick.Select(q => a3.Clip(q.Clip).Name));
    }
}
