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
/// The side blows flattened (*"can we somewhat flatten the horizontal swings so they're more even side to
/// side?"*): registered in 1.1's blows' places only when asked (the session's default; the
/// switch <c>swings-oneone</c> and the library's plain registration keep 1.1's exactly); through the sweep across the front each
/// blade is nearly level, both at the same height; contact meets a standing townsman's chest or shoulders from either side; the
/// same every time; nothing allocated. Every test skips without the 1.1 install (VTMR_GAME_DIR).
/// </summary>
public sealed class FlatSwingTests(ITestOutputHelper output)
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
        clip.Sample(s, sk, local);
        PoseEvaluator.ToModel(sk, local, model);
        var b = Blade(c);
        return (Vector3.Transform(b.Tip, model[b.Bone]), Vector3.Transform(b.Guard, model[b.Bone]));
    }

    /// <summary>A blow's sweep across the front through its window: the tip's height where it crosses the centre line, its steepest slope and its height span while in front (within 75° of forward), its peak speed and its reach.</summary>
    public readonly record struct Sweep(float CentreHeight, float Slope, float Low, float High, float Peak, float Reach, float From, float To, float EndHeading);

    static Sweep Measure(OneOneCharacter c, CharacterAssets assets, int kind, ActionMotion m)
    {
        var clip = assets.Clip(m.Clip);
        var w = ActiveWindows.Measure(assets, kind, m.Clip, m.StrikeSeconds, new ActiveWindowRule());
        float centre = float.NaN, slope = 0f, low = float.MaxValue, high = float.MinValue, reach = 0f;
        var last = Pose(c, clip, w.From).Tip;
        for (float t = w.From + 1f / 240f; t <= w.To; t += 1f / 240f)
        {
            var tip = Pose(c, clip, t).Tip;
            reach = MathF.Max(reach, MathF.Sqrt(tip.X * tip.X + tip.Y * tip.Y));
            bool front = tip.Y > 0.3f && MathF.Abs(MathF.Atan2(tip.X, tip.Y)) < 75f * MathF.PI / 180f;
            if (front)
            {
                low = MathF.Min(low, tip.Z); high = MathF.Max(high, tip.Z);
                var d = tip - last;
                float across = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
                if (across > 0.01f) slope = MathF.Max(slope, MathF.Atan2(MathF.Abs(d.Z), across) * 57.2958f);
                if (float.IsNaN(centre) && MathF.Sign(tip.X) != MathF.Sign(last.X)) centre = last.Z + (tip.Z - last.Z) * (last.X / (last.X - tip.X));
            }
            last = tip;
        }
        var endTip = Pose(c, clip, w.To).Tip;
        return new Sweep(centre, slope, low, high, w.PeakSpeed, reach, w.From, w.To, MathF.Atan2(endTip.X, endTip.Y) * 57.2958f);
    }

    [Theory]
    [MemberData(nameof(SwordKinds))]
    public void The_flattened_side_blows_take_1_1_s_places_only_when_asked_with_1_1_s_timing(string name, string? weapon)
    {
        var (oneAssets, _, one) = Registered(name, weapon, flat: false);
        var (flatAssets, _, flat) = Registered(name, weapon, flat: true);
        var plain = new CharacterAssets();
        OneOneLibrary.Register(plain, Character(name, weapon));
        Assert.Equal(plain.ClipCount, oneAssets.ClipCount);                       // asked not to, nothing is added: 1.1's clip numbers
        Assert.Equal(plain.Kind(0).Actions!.Quick.Select(q => plain.Clip(q.Clip).Name), one.Quick.Select(q => oneAssets.Clip(q.Clip).Name));
        Assert.Equal(one.Quick.Length, flat.Quick.Length);
        for (int i = 0; i < one.Quick.Length; i++)
        {
            string was = oneAssets.Clip(one.Quick[i].Clip).Name, now = flatAssets.Clip(flat.Quick[i].Clip).Name;
            Assert.Equal(OneOneStitched.FlatName(was), now);
            Assert.Equal((one.Quick[i].Rate, one.Quick[i].StrikeSeconds, one.Quick[i].BusySeconds), (flat.Quick[i].Rate, flat.Quick[i].StrikeSeconds, flat.Quick[i].BusySeconds));
            Assert.Equal(oneAssets.Clip(one.Quick[i].Clip).Duration, flatAssets.Clip(flat.Quick[i].Clip).Duration);
            output.WriteLine($"{name}: quick {i} {was} -> {now}");
        }
        Assert.Equal(oneAssets.Clip(one.Strong.Clip).Name, flatAssets.Clip(flat.Strong.Clip).Name);   // the strong blow is 1.1's
        // With the flattened blows the stitched are the four directions' wind-ups (WindUpTests); the overhead and thrust among them.
        Assert.Equal(one.Stitched.Select(s => s.Direction), flat.Stitched.Select(s => s.Direction).Where(d => d is "overhead" or "thrust"));
    }

    [Fact]
    public void Bare_hands_two_handed_weapons_and_other_skeletons_keep_1_1_s()
    {
        foreach (var (name, weapon) in new[] { ("Christof", (string?)null), ("TeutonicKnight", null), ("Civ12male", null) })
            Assert.Empty(Character(name, weapon).Flat);
    }

    [Theory]
    [MemberData(nameof(SwordKinds))]
    public void Through_the_sweep_across_the_front_each_side_blow_is_nearly_level_and_both_at_the_same_height(string name, string? weapon)
    {
        var c = Character(name, weapon);
        var (oneAssets, oneKind, one) = Registered(name, weapon, flat: false);
        var (flatAssets, flatKind, flat) = Registered(name, weapon, flat: true);
        var heights = new List<float>();
        for (int i = 0; i < flat.Quick.Length; i++)
        {
            var before = Measure(c, oneAssets, oneKind, one.Quick[i]);
            var after = Measure(c, flatAssets, flatKind, flat.Quick[i]);
            output.WriteLine($"{name} {oneAssets.Clip(one.Quick[i].Clip).Name}: 1.1 {Line(before)}");
            output.WriteLine($"{name} {flatAssets.Clip(flat.Quick[i].Clip).Name}: flattened {Line(after)}");
            Assert.True(after.Slope < 15f, $"nearly level across the front: at most {after.Slope:0.0}° (1.1's {before.Slope:0.0}°)");
            Assert.InRange(after.Low, OneOneStitched.SwingHeight - 0.15f, OneOneStitched.SwingHeight + 0.15f);
            Assert.InRange(after.High, OneOneStitched.SwingHeight - 0.15f, OneOneStitched.SwingHeight + 0.15f);
            Assert.False(float.IsNaN(after.CentreHeight), "it crosses the centre line in front");
            Assert.InRange(after.Peak, 15f, 90f);
            // The window runs past the centre line onto the far side, as 1.1's does: the whole front of the sweep can meet.
            Assert.True(MathF.Abs(after.EndHeading) > 45f, $"the window ends with the tip at {after.EndHeading:0}° (1.1's {before.EndHeading:0}°)");
            heights.Add(after.CentreHeight);
        }
        if (heights.Count == 2) Assert.True(MathF.Abs(heights[0] - heights[1]) < 0.1f, $"the two sides at {heights[0]:0.00} and {heights[1]:0.00} m");
    }

    static string Line(Sweep s) => FormattableString.Invariant(
        $"window {s.From * 30:0.0}-{s.To * 30:0.0} frames, the tip at the centre line {s.CentreHeight:0.00} m, in front {s.Low:0.00}-{s.High:0.00} m, steepest {s.Slope:0}° from level, peak {s.Peak:0.0} m/s, reach {s.Reach:0.00} m, the window ending with the tip at {s.EndHeading:0}° (0 ahead, + right)");

    // ---- contact ----

    sealed class Ring
    {
        public required World World { get; init; }
        public required Dictionary<string, int> Kinds { get; init; }
        public CharacterAssets Assets => World.Service<CharacterAssets>();
        public readonly List<ContactMade> Contacts = [];

        public void Idle(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                MovementServices.Step(World, ActionFrame.Idle);
                Contacts.AddRange(World.Events<ContactMade>().ThisTick);
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
    }

    (Ring R, EntityId A) Blow(string name, string? weapon, bool flat, int variant, float d = 1.4f)
    {
        var assets = new CharacterAssets();
        var kinds = new Dictionary<string, int>
        {
            [name] = OneOneLibrary.Register(assets, Character(name, weapon), null, flat),
            ["Civ12male"] = OneOneLibrary.Register(assets, Character("Civ12male")),
        };
        var b = new WorldBuilder(7);
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
        var r = new Ring { World = w, Kinds = kinds };
        var a = r.Put(name, 0, 0, 90);
        r.Put("Civ12male", 0, d, -90);
        r.Idle(30);
        w.Get<Combatant>(a).NextVariant = variant;
        ref var intent = ref w.Get<CombatIntent>(a);
        intent.Aims = true; intent.AimPitch = 0f; intent.Attack = true;
        r.Idle(1);
        w.Get<CombatIntent>(a).Attack = false;
        r.Idle((int)MathF.Round(1.3f * World.TicksPerSecond));
        return (r, a);
    }

    [Theory]
    [InlineData("Christof", "BroadSword", 0)]
    [InlineData("Christof", "BroadSword", 1)]
    [InlineData("Christof", "Rapier", 0)]
    [InlineData("Christof", "Rapier", 1)]
    [InlineData("rudolfGuard", null, 0)]
    [InlineData("KnightOfStJohn_torch", null, 0)]
    public void Aimed_level_either_side_meets_a_standing_townsman_s_chest_or_shoulders(string name, string? weapon, int variant)
    {
        var (before, _) = Blow(name, weapon, flat: false, variant);
        var (after, a) = Blow(name, weapon, flat: true, variant);
        string Say(Ring r) => string.Join("; ", r.Contacts.Select(c => FormattableString.Invariant($"{c.Site} at {c.Point.Z:0.00} m, {c.ImpactSpeed:0.0} m/s")));
        output.WriteLine($"{name} quick {variant}: 1.1 {Say(before)} | flattened {Say(after)}");
        var c = Assert.Single(after.Contacts);
        Assert.Equal(a, c.Attacker);
        Assert.Contains(c.Site, new[] { "chest", "neck", "left upper arm", "right upper arm" });
        Assert.InRange(c.Point.Z, 1.2f, 1.65f);
    }

    // ---- the second pass: the brain's aim at its foe's body (SwingAim) ----

    /// <summary>
    /// Christof (flattened side blows) swings quick blow <paramref name="variant"/> at a <paramref name="foe"/> 1.4 m away (crouched
    /// when asked), the blow pitched by <see cref="SwingAim"/> at the draw's <paramref name="choice"/> (1 high, 0 level, −1 low) on
    /// the foe's body, as the duelling brain does when the session registers it.
    /// </summary>
    (Ring R, float Pitch, float Top) AimedBlow(string foe, int variant, int choice, bool crouch = false, string weapon = "BroadSword", float d = 1.4f)
    {
        var assets = new CharacterAssets();
        var kinds = new Dictionary<string, int>
        {
            ["Christof"] = OneOneLibrary.Register(assets, Character("Christof", weapon), null, true),
            [foe] = OneOneLibrary.Register(assets, Character(foe)),
        };
        var b = new WorldBuilder(7);
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
        var r = new Ring { World = w, Kinds = kinds };
        var a = r.Put("Christof", 0, 0, 90);
        var t = r.Put(foe, 0, d, -90);
        for (int i = 0; i < 60; i++) { if (crouch) w.Set(t, new MoveIntent { Crouch = true, Facing = -90f * DetMath.DegToRad, Faces = true }); r.Idle(1); }
        float pitch = new SwingAim().PitchAt(w, in w.Get<Body>(a), t, in w.Get<Body>(t), choice);
        float top = SwingAim.Top(w, t, in w.Get<Body>(t));
        output.WriteLine(FormattableString.Invariant($"  {foe}: crouched {w.Get<Body>(t).Crouched}, capsule {w.Get<Body>(t).CapsuleHeight(w.Get<Locomotion>(t)):0.00} m, role {w.Get<CharacterAnimation>(t).Role}"));
        w.Get<Combatant>(a).NextVariant = variant;
        ref var intent = ref w.Get<CombatIntent>(a);
        intent.Aims = true; intent.AimPitch = pitch; intent.Attack = true;
        if (crouch) w.Set(t, new MoveIntent { Crouch = true, Facing = -90f * DetMath.DegToRad, Faces = true });
        r.Idle(1);
        w.Get<CombatIntent>(a).Attack = false;
        for (int i = 0; i < (int)MathF.Round(1.3f * World.TicksPerSecond); i++) { if (crouch) w.Set(t, new MoveIntent { Crouch = true, Facing = -90f * DetMath.DegToRad, Faces = true }); r.Idle(1); }
        return (r, pitch, top);
    }

    [Theory]
    [InlineData("Szlachta", false, 0)]
    [InlineData("Szlachta", false, 1)]
    [InlineData("Merchant", true, 0)]
    [InlineData("Merchant", true, 1)]
    public void Aimed_at_his_body_a_flattened_side_blow_meets_a_szlachta_and_a_crouched_townsman(string foe, bool crouch, int variant)
    {
        foreach (int choice in new[] { 1, 0, -1 })
        {
            var (r, pitch, top) = AimedBlow(foe, variant, choice, crouch);
            output.WriteLine(FormattableString.Invariant($"{foe}{(crouch ? " crouched" : "")} (top {top:0.00} m), quick {variant}, aim {(choice > 0 ? "high" : choice < 0 ? "low" : "level")} {pitch * 57.2958f:0.0}°: {string.Join("; ", r.Contacts.Select(c => $"{c.Met} {c.Site} at {c.Point.Z:0.00} m, {c.ImpactSpeed:0.0} m/s"))}"));
            var c = Assert.Single(r.Contacts);
            Assert.Equal(ContactMet.Body, c.Met);
            Assert.True(c.Point.Z < top + 0.05f, $"on him: {c.Point.Z:0.00} against his top {top:0.00}");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Aimed_level_at_a_standing_man_s_body_it_still_meets_his_chest_or_shoulders(int variant)
    {
        var (r, pitch, top) = AimedBlow("Civ12male", variant, 0);
        output.WriteLine(FormattableString.Invariant($"standing (top {top:0.00} m), quick {variant}, aim level {pitch * 57.2958f:0.0}°: {string.Join("; ", r.Contacts.Select(c => $"{c.Site} at {c.Point.Z:0.00} m"))}"));
        var c = Assert.Single(r.Contacts);
        Assert.Contains(c.Site, new[] { "chest", "neck", "left upper arm", "right upper arm" });
        Assert.InRange(c.Point.Z, 1.1f, 1.6f);
    }

    [Fact]
    public void The_aim_s_heights_follow_the_foe_s_own_height_and_stay_in_their_band()
    {
        var (_, high, top) = AimedBlow("Szlachta", 0, 1);
        var (_, low, _) = AimedBlow("Szlachta", 0, -1);
        var (_, standLevel, standTop) = AimedBlow("Civ12male", 0, 0);
        var (_, crouchLevel, crouchTop) = AimedBlow("Merchant", 0, 0, crouch: true);   // a townsman with the moves' composed stand-ins (the crouch posed)
        output.WriteLine(FormattableString.Invariant($"szlachta top {top:0.00}: high {high * 57.2958f:0.0}°, low {low * 57.2958f:0.0}°; townsman standing {standTop:0.00}: level {standLevel * 57.2958f:0.0}°; crouched {crouchTop:0.00}: level {crouchLevel * 57.2958f:0.0}°"));
        Assert.True(high > low, "high above low");
        Assert.True(top < standTop - 0.5f, "the szlachta is much lower than a standing man");
        Assert.True(high < standLevel, "aimed high at the szlachta is still lower than level at a standing man");
        Assert.InRange(standLevel * 57.2958f, -10f, 0f);                   // a standing man's chest: about level
        Assert.True(crouchLevel <= standLevel + 1e-4f, "a crouched man is aimed no higher");   // (his crouch pose barely lowers his volumes: top 1.79 m against 1.82)
    }

    // ---- the same every time, nothing allocated ----

    [Fact]
    public void A_flattened_clip_samples_the_same_every_time_and_allocates_nothing()
    {
        var c = Character("Christof", "BroadSword");
        var sk = c.Model.Skeleton;
        var a = new BoneTransform[sk.Count];
        var b = new BoneTransform[sk.Count];
        foreach (var (_, clip) in c.Flat)
        {
            for (float t = 0; t <= clip.Duration; t += 0.03f)
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
