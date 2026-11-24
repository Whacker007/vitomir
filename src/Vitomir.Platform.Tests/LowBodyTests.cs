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
/// Blows that meet a low body: Christof's four
/// directional blows (the earlier build's wind-ups) aimed as the duelling brain aims them (<see cref="SwingAim"/>) meet 1.1's szlachta (hunched: his top
/// 1.01 m) and a standing townsman in front at the brain's range; struck walking backward with the szlachta following, the side blows meet
/// him too once the blows' hips are held upright (<see cref="StitchHips"/>; with the earlier build's hips the forehand passed over him); holding the hips
/// upright moves no other bone in the model, allocates nothing. Every test skips without the 1.1 install (VTMR_GAME_DIR).
/// </summary>
public sealed class LowBodyTests(ITestOutputHelper output)
{
    /// <summary>The composed blows with their hips held upright (the switch <c>blow-hips-upright</c>).</summary>
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) { HipsUpright = true } : null);

    static OneOneLibrary Lib()
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        return Library.Value!;
    }

    /// <summary>The composed blows as by default: their hips their own.</summary>
    static readonly Lazy<OneOneLibrary?> LibraryMovingHips = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) { HipsUpright = false } : null);

    static readonly Dictionary<(string, string?, bool), OneOneCharacter> Loaded = [];

    static OneOneCharacter Character(string name, string? weapon = null, bool movingHips = false)
    {
        var lib = Lib();
        if (movingHips) lib = LibraryMovingHips.Value!;
        lock (Loaded)
        {
            if (!Loaded.TryGetValue((name, weapon, movingHips), out var c))
                Loaded[(name, weapon, movingHips)] = c = lib.Character(name, weapon) ?? throw new Xunit.Sdk.XunitException($"{name} did not load");
            return c;
        }
    }

    internal sealed class Ring
    {
        public required World World { get; init; }
        public required EntityId A { get; init; }
        public required EntityId T { get; init; }
        public readonly List<ContactMade> Contacts = [];
        public readonly List<(long Tick, BladePlacement Blade)> Path = [];
        public readonly List<string> Clips = [];

        public void Step(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                MovementServices.Step(World, ActionFrame.Idle);
                Contacts.AddRange(World.Events<ContactMade>().ThisTick);
                if (HeldItems.Blade(World, A, out var blade)) Path.Add((World.Tick, blade));
                int clip = World.Get<CharacterAnimation>(A).ActionClip;
                if (clip >= 0 && World.Service<CharacterAssets>().Clip(clip).Name is { } name && (Clips.Count == 0 || Clips[^1] != name)) Clips.Add(name);
            }
        }
    }

    /// <summary>
    /// <paramref name="attacker"/> (with <paramref name="weapon"/>; flattened side blows and the wind-ups) at the origin facing +Y, the
    /// <paramref name="foe"/> <paramref name="d"/> m in front facing him; directional attacks on, the brain's aim registered.
    /// </summary>
    internal static Ring Make(string foe, float d, string attacker = "Christof", string? weapon = "Rapier", SwingAim? aim = null, bool movingHips = false)
    {
        var assets = new CharacterAssets();
        int a = OneOneLibrary.Register(assets, Character(attacker, weapon, movingHips), null, true);
        int f = OneOneLibrary.Register(assets, Character(foe));
        var b = new WorldBuilder(7);
        MovementServices.Register(b, new TestLevel().Floor(-60, -60, 60, 60).Build());
        MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = false });
        b.Animation(assets);
        b.HitVolumes();
        BodyMotionSystem.Register(b);
        CombatServices.Register(b);
        ContactServices.Register(b, new ContactConfig());
        b.Service(new DirectionalAttacks { Enabled = true, Settings = new DirectionSettings(), Rows = Rows });
        b.Service(aim ?? new SwingAim());
        var w = b.Build();
        w.Service<HitVolumes>().Prepare(assets);
        ContactServices.Prepare(w);
        EntityId Put(int kind, float x, float y, float yaw)
        {
            var e = MovementServices.SpawnBody(w, new(x, y, 0.01f), yaw * DetMath.DegToRad, Locomotion.Sandbox);
            w.Set(e, CharacterAnimation.For(assets, kind));
            w.Set(e, MotionInput.Standing);
            CombatServices.Arm(w, e);
            return e;
        }
        var r = new Ring { World = w, A = Put(a, 0, 0, 90), T = Put(f, 0, d, -90) };
        r.Step(60);
        r.Contacts.Clear();
        r.Path.Clear();
        return r;
    }

    /// <summary>The enhanced module's direction table (<c>directions.json</c>) as it stands: each direction's blow with its wind-up first.</summary>
    internal static readonly DirectionRow[] Rows =
    [
        new(null, null, "stitched:left|quick:1", "stitched:right|quick:0", null, null),
        new(null, 1, null, null, "stitched:overhead", "stitched:thrust"),
    ];

    /// <summary>The pitch the duelling brain gives a blow of <paramref name="direction"/> with the draw's <paramref name="choice"/> (DuelBrain's attack choice).</summary>
    internal static float BrainPitch(Ring r, AttackDirection direction, int choice)
    {
        var aim = r.World.Service<SwingAim>();
        return direction == AttackDirection.Thrust
            ? aim.ThrustPitchAt(r.World, r.A, in r.World.Get<Body>(r.A), r.T, in r.World.Get<Body>(r.T))
            : aim.PitchAt(r.World, in r.World.Get<Body>(r.A), r.T, in r.World.Get<Body>(r.T), choice);
    }

    /// <summary>
    /// A tap of attack from <paramref name="direction"/> aimed at <paramref name="pitch"/> (the brain's: pressed one tick, let go), played out;
    /// with <paramref name="walk"/> the attacker walks backward all the while (as the brain keeping its range does), facing his foe.
    /// </summary>
    internal static void Swing(Ring r, AttackDirection direction, float pitch, bool walk = false)
    {
        void Move()
        {
            if (!walk) return;
            r.World.Set(r.A, new MoveIntent { Direction = new(0f, -1f), Gait = Gait.Walk, Facing = DetMath.HalfPi, Faces = true });
            r.World.Set(r.T, new MoveIntent { Direction = new(0f, -1f), Gait = Gait.Walk, Facing = -DetMath.HalfPi, Faces = true });   // the foe follows
        }
        if (walk) for (int i = 0; i < 40; i++) { Move(); r.Step(1); }
        r.Contacts.Clear(); r.Path.Clear(); r.Clips.Clear();
        ref var intent = ref r.World.Get<CombatIntent>(r.A);
        intent.Aims = true; intent.AimPitch = pitch; intent.Attack = true; intent.Direction = direction;
        Move();
        r.Step(1);
        r.World.Get<CombatIntent>(r.A).Attack = false;
        for (int i = 0; i < (int)MathF.Round(1.6f * World.TicksPerSecond); i++) { Move(); r.Step(1); }
    }

    static readonly AttackDirection[] Directions = [AttackDirection.Left, AttackDirection.Right, AttackDirection.Overhead, AttackDirection.Thrust];

    static string Met(Ring r) => r.Contacts.Count == 0 ? "nothing"
        : FormattableString.Invariant($"{(r.Contacts[0].Met == ContactMet.Body ? r.Contacts[0].Site : r.Contacts[0].Met.ToString())} at {r.Contacts[0].Point.Z:0.00} m, along {r.Contacts[0].AlongBlade:0.0}");

    static void MeetsHisBody(Ring r, string what)
    {
        Assert.True(r.Contacts.Count > 0, $"{what}: met nothing");
        Assert.Equal(ContactMet.Body, r.Contacts[0].Met);
        Assert.Equal(r.T, r.Contacts[0].Target);
    }

    /// <summary>
    /// Standing, each direction aimed as the duelling brain aims it (each draw's height; the thrust at his chest) meets the szlachta (his
    /// top 1.01 m, hunched) standing in front from 0.8 to 1.4 m (feet to feet: the brain's range against him, its contacts' 0.9–1.1 m), and a
    /// standing townsman as before.
    /// </summary>
    [Theory]
    [InlineData("Szlachta")]
    [InlineData("Civ12male")]
    public void Standing_each_direction_aimed_as_the_brain_aims_meets_a_szlachta_or_a_townsman_in_front_at_the_brain_s_range(string foe)
    {
        foreach (var dir in Directions)
            foreach (int choice in dir == AttackDirection.Thrust ? new[] { 0 } : new[] { 1, 0, -1 })
            {
                var line = new System.Text.StringBuilder(FormattableString.Invariant($"{foe} {dir,-8} {(choice > 0 ? "high" : choice < 0 ? "low" : "level"),-5}"));
                foreach (float d in new[] { 0.8f, 1.0f, 1.2f, 1.4f })
                {
                    var r = Make(foe, d);
                    float pitch = BrainPitch(r, dir, choice);
                    Swing(r, dir, pitch);
                    line.Append(FormattableString.Invariant($" | {d:0.0} m {pitch * 57.2958f:0}°: {Met(r)}"));
                    MeetsHisBody(r, FormattableString.Invariant($"{foe} {dir} {choice} at {d} m"));
                }
                output.WriteLine(line.ToString());
            }
    }

    /// <summary>
    /// The earlier build's cause and fix: Christof walks backward as he strikes (as the brain keeping its range does) and the szlachta follows, 0.8 to
    /// 1.4 m away. The action channel plays a blow on the upper body alone while the body moves, so the blow's own hips (its turn and bow)
    /// are the walk's: with their own hips (the default) the forehand's sweep came round late and high; with the hips held
    /// upright (<see cref="StitchHips"/>) both side blows meet his body at every distance, and the overhead close in.
    /// </summary>
    [Fact]
    public void Walking_backward_the_side_blows_still_meet_a_szlachta_who_follows()
    {
        foreach (var dir in Directions)
            foreach (bool old in new[] { true, false })
            {
                var line = new System.Text.StringBuilder(FormattableString.Invariant($"{dir,-8} walking, {(old ? "the moving hips" : "hips upright")}"));
                foreach (float d in new[] { 0.8f, 1.0f, 1.2f, 1.4f })
                {
                    var r = Make("Szlachta", d, movingHips: old);
                    float pitch = BrainPitch(r, dir, 0);
                    Swing(r, dir, pitch, walk: true);
                    line.Append(FormattableString.Invariant($" | {d:0.0} m {pitch * 57.2958f:0}°: {Met(r)}"));
                    if (!old && (dir is AttackDirection.Left or AttackDirection.Right || dir == AttackDirection.Overhead && d <= 1.0f))
                        MeetsHisBody(r, FormattableString.Invariant($"{dir} walking at {d} m"));
                }
                output.WriteLine(line.ToString());
            }
    }

    /// <summary>
    /// Holding the hips upright is a pure re-expression: standing (the whole body the blow's), every bone but the hips' own is where it
    /// was in the model, for every composed blow through its whole clip; the same every time; nothing allocated.
    /// </summary>
    [Fact]
    public void Holding_the_hips_upright_moves_no_bone_in_the_model_but_the_hips_own()
    {
        var upright = Character("Christof", "Rapier");
        var own = Character("Christof", "Rapier", movingHips: true);
        var sk = upright.Model.Skeleton;
        var hips = StitchHips.Of(sk, upright.Role(MotionRole.Idle).Clip!)!;
        Matrix4x4[] Pose(IAnimationClip clip, float t)
        {
            var local = new BoneTransform[sk.Count]; var model = new Matrix4x4[sk.Count];
            PoseEvaluator.Rest(sk, local); clip.Sample(t, sk, local); PoseEvaluator.ToModel(sk, local, model); return model;
        }
        foreach (var (a, b) in upright.WindUps.Zip(own.WindUps))
        {
            float worst = 0f;
            for (float t = 0f; t <= a.Clip.Duration; t += 1f / 30f)
            {
                var x = Pose(a.Clip, t); var y = Pose(b.Clip, t);
                for (int k = 0; k < sk.Count; k++)
                    if (k != hips.Bone) worst = MathF.Max(worst, Vector3.Distance(x[k].Translation, y[k].Translation));
            }
            output.WriteLine(FormattableString.Invariant($"{a.Direction}: every bone but the hips within {worst * 1000f:0.000} mm"));
            Assert.True(worst < 1e-3f, $"{a.Direction}: {worst * 1000f:0.0} mm");
        }
        var l1 = new BoneTransform[sk.Count]; var l2 = new BoneTransform[sk.Count];
        var clip = upright.WindUps[0].Clip;
        clip.Sample(0.3f, sk, l1); clip.Sample(0.3f, sk, l2);
        Assert.True(l1.AsSpan().SequenceEqual(l2));
        Allocations.None("the hips held upright 2,000 times", () =>
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2000; i++) hips.Apply(sk, l1);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        });
    }
}
