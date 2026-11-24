// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The body acts: the action channel's roles found in 1.1's sets by 1.1's rules for Christof with his broadsword,
/// rudolfGuard, the Teutonic Knight and the Knight of St John; attack tapped a quick blow and held a strong one made on
/// release, a press during a swing buffered once, the masquerade's catch, the block held (a walk while it is); the aim
/// offset bending the blow high, level and low with the blade's tip at the <c>Strike</c> moved by a measured amount; the
/// first-person eye on the unbent head; held things on the bones their vertices ride, placed where they are drawn; replays
/// that agree; nothing allocated per tick. Every test skips without the 1.1 install (VTMR_GAME_DIR).
/// </summary>
public sealed class ActionTests
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

    /// <summary>A world as the program makes it (movement, the masquerade, animation, the body's motion, fighting) on a floor.</summary>
    sealed class Arena
    {
        public required World World { get; init; }
        public required EntityId Player { get; init; }
        public EntityId Other { get; init; } = EntityId.None;
        public CharacterAssets Assets => World.Service<CharacterAssets>();
        public readonly List<AttackBegun> Begun = [];
        public readonly List<StrikeMoment> Strikes = [];
        public readonly List<AttackRefused> Refused = [];
        public readonly List<BlockRaised> Raised = [];
        public readonly List<BlockLowered> Lowered = [];

        public void Step(ActionFrame frame, int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                MovementServices.Step(World, frame);
                Begun.AddRange(World.Events<AttackBegun>().ThisTick);
                Strikes.AddRange(World.Events<StrikeMoment>().ThisTick);
                Refused.AddRange(World.Events<AttackRefused>().ThisTick);
                Raised.AddRange(World.Events<BlockRaised>().ThisTick);
                Lowered.AddRange(World.Events<BlockLowered>().ThisTick);
            }
        }

        public void Idle(int ticks) => Step(ActionFrame.Idle, ticks);

        public string ClipName(int clip) => Assets.Clip(clip).Name;
    }

    static ActionFrame Held(Buttons b) => new(Vector2.Zero, Vector2.Zero, b, default);

    static Arena Make(string weapon = "BroadSword", string? other = null, ulong seed = 7, PoseRigSettings? rig = null, bool firstPerson = false, float pitch = 0f)
    {
        var christof = Character("Christof", weapon);
        var assets = new CharacterAssets();
        int kind = OneOneLibrary.Register(assets, christof);
        int otherKind = other is null ? -1 : OneOneLibrary.Register(assets, Character(other));
        var level = new TestLevel().Floor(-60, -60, 60, 60).Build();
        var b = new WorldBuilder(seed);
        MovementServices.Register(b, level);
        MasqueradeServices.Register(b);
        b.Animation(assets, rig: rig);
        BodyMotionSystem.Register(b);
        CombatServices.Register(b);
        var w = b.Build();
        var e = MovementServices.SpawnControlled(w, new(0, 0, 0.01f), DetMath.HalfPi, Locomotion.Sandbox);
        w.Set(e, CharacterAnimation.For(assets, kind));
        w.Set(e, MotionInput.Standing);
        w.Set(e, new MasqueradeState { Mode = MasqueradeMode.Unbound });   // the catch off unless a test puts it on
        CombatServices.Arm(w, e);
        var o = EntityId.None;
        if (otherKind >= 0)
        {
            o = MovementServices.SpawnBody(w, new(0, 1.8f, 0.01f), -DetMath.HalfPi, Locomotion.Sandbox);
            w.Set(o, CharacterAnimation.For(assets, otherKind));
            w.Set(o, MotionInput.Standing);
            CombatServices.Arm(w, o);
        }
        var arena = new Arena { World = w, Player = e, Other = o };
        if (firstPerson)
            foreach (var held in new[] { Buttons.SwitchView, Buttons.None, Buttons.SwitchView }) arena.Step(Held(held));
        if (pitch != 0f) arena.Step(new ActionFrame(Vector2.Zero, new Vector2(0f, pitch), Buttons.None, default));
        arena.Idle(30);
        return arena;
    }

    static int Ticks(float seconds) => (int)MathF.Round(seconds * World.TicksPerSecond);

    // ---- the roles ----

    [Fact]
    public void Christofs_broadsword_finds_1_1s_two_swings_the_whirl_the_shield_block_the_hurt_and_the_deaths()
    {
        var c = Character("Christof", "BroadSword");
        Assert.Equal("BroadSword", c.Weapon!.Template);
        Assert.Equal(1, c.Weapon.Carry);
        Assert.Equal("ChrisStandSword", c.Role(MotionRole.Idle).Clip!.Name);          // stand_onehand: the carry class's stand
        Assert.Equal("walk_onehand", c.Role(MotionRole.Walk).Mode);
        Assert.Equal("run_onehand", c.Role(MotionRole.Run).Mode);
        var a = c.Actions!;
        Assert.Equal(["chrisAttackSword", "chrisAttack2Sword"], a.Quick.Select(q => q.Clip.Name));
        Assert.Equal("ChrisAttackSSword", a.Strong!.Clip.Name);
        Assert.All(a.Quick, q => Assert.Equal(15f / 30f, q.StrikeSeconds, 3));
        Assert.Equal(25f / 30f, a.Strong.StrikeSeconds, 3);
        Assert.All(a.Quick, q => Assert.Equal(q.Clip.Duration / 1.1f, q.Rate, 3));     // STRIKETIME 1.1 s
        Assert.Equal(a.Strong.Clip.Duration / 1.5f, a.Strong.Rate, 3);                  // STRIKETIME2 1.5 s
        Assert.Equal(1.1f, a.Quick[0].BusySeconds, 3);                                  // RELOADTIME 1.1 s
        Assert.Equal("chrisShieldBlock", a.Block!.Clip.Name);
        Assert.False(a.Block.Composed);
        Assert.InRange(a.Block.HoldSeconds, 0.05f, a.Block.Clip.Duration);
        Assert.Equal("ChrisDamageSword", a.Hurt!.Clip.Name);
        Assert.Equal("christofQuickdeath", a.DeathQuick!.Clip.Name);
        Assert.Equal("christofDeath", a.DeathSlow!.Clip.Name);
        Assert.Equal([14, 15, 16], a.Spine);
        for (int b = 0; b < 40; b++) Assert.Equal(b is >= 14 and <= 38, a.UpperBody[b]);
        // Bare hands (his template's Punch): the fists and the kick, his own stand.
        var fists = Character("Christof").Actions!;
        Assert.Equal("chrisAttackFists", fists.Quick.Single().Clip.Name);
        Assert.Equal("ChrisAttackSKick", fists.Strong!.Clip.Name);
        Assert.Empty(Character("Christof").Held);
    }

    [Fact]
    public void The_guard_the_Teutonic_Knight_and_the_Knight_of_St_John_find_their_own_sets_and_a_composed_guard()
    {
        var guard = Character("rudolfGuard");
        Assert.Equal("weapRapier_shield", guard.Weapon!.Template);
        Assert.Equal("chrisAttackSword", guard.Actions!.Quick.Single().Clip.Name);
        Assert.Equal("chrisAttackSword", guard.Actions.Strong!.Clip.Name);             // no special in his set: the swing, at STRIKETIME2
        Assert.Equal(guard.Actions.Strong.Clip.Duration / 1.5f, guard.Actions.Strong.Rate, 3);
        Assert.True(guard.Actions.Block!.Composed);                                     // no shield mode: SANDBOX guard
        Assert.Equal("ChrisDamageSword", guard.Actions.Hurt!.Clip.Name);

        var knight = Character("TeutonicKnight");
        Assert.Equal("GreatSword", knight.Weapon!.Template);
        Assert.Equal(3, knight.Weapon.Carry);
        Assert.Equal("TK_Stand2HSword", knight.Role(MotionRole.Idle).Clip!.Name);
        Assert.Equal(["Erik_Attack2HSword", "erikAttack22HSword"], knight.Actions!.Quick.Select(q => q.Clip.Name));
        Assert.Equal("erikAttackS2HSword", knight.Actions.Strong!.Clip.Name);
        Assert.Equal([9, 10, 11], knight.Actions.Spine);                                // skeleton 1's spine
        Assert.Equal(26, knight.Actions.WeaponHand);                                     // no RFingers: the Weapon tag
        Assert.True(knight.Actions.Block!.Composed);
        Assert.Equal("TK_Damage2HSword", knight.Actions.Hurt!.Clip.Name);

        var stJohn = Character("KnightOfStJohn");
        Assert.Equal("Halberd", stJohn.Weapon!.Template);
        Assert.Equal(["chrisAttackHalberd", "chrisAttack2Halberd"], stJohn.Actions!.Quick.Select(q => q.Clip.Name));
        Assert.Equal("ChrisStandHalberd", stJohn.Role(MotionRole.Idle).Clip!.Name);
        Assert.Equal("ChrisDamageHalberd", stJohn.Actions.Hurt!.Clip.Name);
    }

    [Fact]
    public void Held_models_are_found_on_the_bones_their_vertices_ride()
    {
        var sword = Assert.Single(Character("Christof", "BroadSword").Held);
        Assert.Equal("broadSword_0", sword.Model.Name, ignoreCase: true);
        var blade = Assert.Single(sword.Things);
        Assert.Equal((HeldKind.Blade, 25), (blade.Kind, blade.Bone));
        Assert.InRange(blade.Length, 0.85f, 1.05f);                                      // guard to tip
        Assert.InRange(blade.Tip.Length(), 0.98f, 1.06f);                                // the tip 1.02 m from the hand (the survey)

        var rapier = Assert.Single(Character("rudolfGuard").Held);
        Assert.Equal("rapier_shield_0", rapier.Model.Name, ignoreCase: true);
        Assert.Contains(rapier.Things, t => t is { Kind: HeldKind.Blade, Bone: 25 });
        Assert.Contains(rapier.Things, t => t is { Kind: HeldKind.Shield, Bone: 30 });
        // The kite's own box: thin across, not as deep as its tilt in the bone's frame.
        var kite = rapier.Things.First(t => t.Kind == HeldKind.Shield);
        float[] own = [kite.BoxHalf.X, kite.BoxHalf.Y, kite.BoxHalf.Z];
        Array.Sort(own);
        Assert.InRange(own[0], 0.03f, 0.06f);                                              // 0.043: half its thickness (the bone's units; the world's are twice)
        Assert.InRange(own[2], 0.40f, 0.48f);                                              // 0.437: half its height
        var bone = (kite.Max - kite.Min) * 0.5f;
        Assert.True(own[0] < MathF.Min(bone.X, MathF.Min(bone.Y, bone.Z)) * 0.5f, $"own box {kite.BoxHalf}, the bone's {bone}");

        var great = Assert.Single(Character("TeutonicKnight").Held);
        Assert.Equal("crusader_1", great.Model.Name, ignoreCase: true);
        Assert.Equal(26, Assert.Single(great.Things).Bone);
        Assert.InRange(great.Things[0].Tip.Length(), 1.35f, 1.5f);                       // the tip 1.43 m from bone 26
    }

    [Fact]
    public void A_tilted_box_s_own_box_is_found_whatever_its_tilt()
    {
        var turn = Matrix4x4.CreateRotationX(0.6f) * Matrix4x4.CreateRotationZ(-1.1f);
        var points = new List<Vector3>();
        for (int i = -4; i <= 4; i++) for (int j = -8; j <= 8; j++) for (int k = -1; k <= 1; k += 2)
            points.Add(Vector3.Transform(new Vector3(i * 0.45f / 4, j * 0.85f / 8, k * 0.08f), turn) + new Vector3(0.1f, -0.2f, 0.3f));
        var (centre, x, y, z, half) = HeldItems.OwnBox(points);
        Assert.True(Vector3.Distance(centre, new Vector3(0.1f, -0.2f, 0.3f)) < 1e-4f, $"centre {centre}");
        float[] got = [half.X, half.Y, half.Z];
        Array.Sort(got);
        Assert.Equal(0.08f, got[0], 3);
        Assert.Equal(0.45f, got[1], 3);
        Assert.Equal(0.85f, got[2], 3);
        Assert.Equal(1f, Vector3.Dot(Vector3.Cross(x, y), z), 4);                          // right-handed and square
        Assert.Equal(0f, Vector3.Dot(x, y), 4);
        var a = HeldItems.OwnBox(points);
        Assert.Equal((centre, x, y, z, half), a);                                          // the same answer every time
    }

    // ---- the input ----

    [Fact]
    public void Attack_tapped_is_a_quick_blow_made_on_release_and_held_past_the_threshold_a_strong_one()
    {
        var a = Make();
        a.Step(Held(Buttons.Attack));
        Assert.Empty(a.Begun);                                                           // made as it is let go
        a.Idle(1);
        var first = Assert.Single(a.Begun);
        Assert.Equal((AttackStrength.Quick, ActionRole.AttackQuick, "chrisAttackSword"), (first.Strength, first.Role, a.ClipName(first.Clip)));
        Assert.Equal(ActionRole.AttackQuick, a.World.Get<CharacterAnimation>(a.Player).Action);
        a.Idle(Ticks(1.2f));
        // The variants are taken in turn: the second tap swings the other.
        a.Step(Held(Buttons.Attack)); a.Idle(1);
        Assert.Equal("chrisAttack2Sword", a.ClipName(a.Begun[1].Clip));
        a.Idle(Ticks(1.2f));
        // Held 0.4 s: nothing while held, the whirl on release.
        a.Step(Held(Buttons.Attack), Ticks(0.4f));
        Assert.Equal(2, a.Begun.Count);
        a.Idle(1);
        Assert.Equal((AttackStrength.Strong, ActionRole.AttackStrong, "ChrisAttackSSword"), (a.Begun[2].Strength, a.Begun[2].Role, a.ClipName(a.Begun[2].Clip)));
        a.Idle(Ticks(1.7f));
        // Held 0.2 s (under the 0.3 s threshold): quick.
        a.Step(Held(Buttons.Attack), Ticks(0.2f)); a.Idle(1);
        Assert.Equal(AttackStrength.Quick, a.Begun[3].Strength);
        // Each blow's Strike moment comes once, at its mark (15 of 29 frames at 1.1's stretched rate).
        a.Idle(Ticks(1.5f));
        Assert.Equal(4, a.Strikes.Count);
        long delay = a.Strikes[0].Tick - a.Begun[0].Tick;
        Assert.InRange(delay, Ticks(0.5f / (0.967f / 1.1f)) - 2, Ticks(0.5f / (0.967f / 1.1f)) + 2);
        Assert.All(a.Strikes, s => Assert.InRange(s.TipSpeed, 5f, 80f));
    }

    [Fact]
    public void A_press_during_a_swing_is_buffered_once_and_a_third_is_refused_mid_swing()
    {
        var a = Make();
        a.Step(Held(Buttons.Attack)); a.Idle(Ticks(0.3f));
        a.Step(Held(Buttons.Attack)); a.Idle(Ticks(0.2f));                               // buffered
        Assert.Single(a.Begun);
        a.Step(Held(Buttons.Attack)); a.Idle(1);                                         // a third: refused
        Assert.Equal(AttackRefusal.MidSwing, Assert.Single(a.Refused).Why);
        a.Idle(Ticks(1.0f));
        Assert.Equal(2, a.Begun.Count);
        // The buffered blow begins as the first one's busy time (RELOADTIME 1.1 s) ends.
        Assert.InRange(a.Begun[1].Tick - a.Begun[0].Tick, Ticks(1.1f) - 1, Ticks(1.1f) + 1);
        Assert.Equal("chrisAttack2Sword", a.ClipName(a.Begun[1].Clip));
    }

    [Fact]
    public void The_masquerades_catch_refuses_an_attack_once_a_press_and_lets_the_block_through()
    {
        var a = Make();
        a.World.Get<MasqueradeState>(a.Player).Mode = MasqueradeMode.Masquerade;
        a.Step(Held(Buttons.Attack), Ticks(0.5f)); a.Idle(Ticks(1.5f));
        Assert.Empty(a.Begun);
        Assert.Equal(AttackRefusal.Catch, Assert.Single(a.Refused).Why);                // once for the press, not each tick
        a.Step(Held(Buttons.Block), Ticks(0.5f));
        Assert.Single(a.Raised);
        a.Idle(Ticks(0.5f));
        // The catch let go (the mode unbound): the next press swings.
        a.World.Get<MasqueradeState>(a.Player).Mode = MasqueradeMode.Unbound;
        a.Step(Held(Buttons.Attack)); a.Idle(1);
        Assert.Single(a.Begun);
    }

    [Fact]
    public void Block_held_raises_the_block_at_its_raised_frame_moving_walks_and_attack_is_refused_until_it_is_lowered()
    {
        var a = Make();
        a.World.Service<Vitomir.Platform.Combat.GuardSettings>().Enabled = false;   // the guard off is the earlier build's (on: block and swing together, GuardTests)
        var hold = a.Assets.Kind(a.World.Get<CharacterAnimation>(a.Player).Kind).Actions!.Block.HoldSeconds;
        var forward = new ActionFrame(new Vector2(0, 1), Vector2.Zero, Buttons.Block, default);
        a.Step(forward, Ticks(1.0f));
        Assert.Single(a.Raised);
        var anim = a.World.Get<CharacterAnimation>(a.Player);
        Assert.Equal((ActionRole.Block, true), (anim.Action, anim.ActionHolds));
        Assert.Equal(hold, anim.ActionTime, 4);
        Assert.Equal(Gait.Walk, a.World.Get<MoveIntent>(a.Player).Gait);
        var v = a.World.Get<Body>(a.Player).Velocity;
        Assert.InRange(MathF.Sqrt(v.X * v.X + v.Y * v.Y), 0.5f, a.World.Get<Locomotion>(a.Player).WalkSpeed + 0.05f);
        a.Step(new ActionFrame(Vector2.Zero, Vector2.Zero, Buttons.Block | Buttons.Attack, default));
        Assert.Equal(AttackRefusal.Blocking, Assert.Single(a.Refused).Why);
        a.Idle(Ticks(1.0f));
        Assert.Single(a.Lowered);
        Assert.InRange(a.Lowered[0].HeldSeconds, 0.95f, 1.05f);
        Assert.Equal(ActionRole.None, a.World.Get<CharacterAnimation>(a.Player).Action);
    }

    [Fact]
    public void A_hurt_and_a_death_play_when_asked_and_the_dead_do_not_attack()
    {
        var a = Make();
        a.World.Get<CombatIntent>(a.Player).Play = ActionRole.Hurt;
        a.Idle(2);
        Assert.Equal("ChrisDamageSword", a.ClipName(a.World.Get<CharacterAnimation>(a.Player).ActionClip));
        a.Idle(Ticks(1.6f));
        a.World.Get<CombatIntent>(a.Player).Play = ActionRole.DeathSlow;
        a.Idle(Ticks(3f));
        var anim = a.World.Get<CharacterAnimation>(a.Player);
        Assert.Equal((ActionRole.DeathSlow, "christofDeath", 1f), (anim.Action, a.ClipName(anim.ActionClip), anim.ActionWeight));   // held at its end
        a.Step(Held(Buttons.Attack)); a.Idle(1);
        Assert.Equal(AttackRefusal.Dead, Assert.Single(a.Refused).Why);
    }

    // ---- the aim offset ----

    static StrikeMoment StrikeAimed(float pitchDegrees, PoseRigSettings? rig = null)
    {
        var a = Make(firstPerson: true, pitch: pitchDegrees * DetMath.DegToRad, rig: rig);
        a.Step(Held(Buttons.Attack)); a.Idle(Ticks(1.0f));
        Assert.Equal(pitchDegrees * DetMath.DegToRad, a.World.Get<CameraRig>(a.Player).Pitch, 4);
        return Assert.Single(a.Strikes);
    }

    [Fact]
    public void The_aim_offset_bends_a_blow_high_level_and_low_and_the_tip_at_the_strike_follows()
    {
        var level = StrikeAimed(0f);
        var high = StrikeAimed(25f);
        var low = StrikeAimed(-35f);
        // Measured (Christof's chrisAttackSword with the broadsword, the bend split over spine 14 to 16): level 1.29 m,
        // aimed 25° up about 0.6 m higher, 35° down about 0.8 m lower; the bend is clamped at 25° up and 40° down.
        Assert.InRange(level.Tip.Z, 1.15f, 1.45f);
        Assert.InRange(high.Tip.Z - level.Tip.Z, 0.4f, 0.9f);
        Assert.InRange(level.Tip.Z - low.Tip.Z, 0.5f, 1.1f);
        // Beyond the reach it is clamped: 60° up bends no further than 25°.
        var steep = StrikeAimed(60f);
        Assert.InRange(steep.Tip.Z - high.Tip.Z, -0.02f, 0.02f);
    }

    [Fact]
    public void The_first_person_eye_stays_on_the_unbent_head_while_the_swing_bends_the_body()
    {
        var flat = new PoseRigSettings { Aim = new AimOffsetSettings { MostUp = 0f, MostDown = 0f } };
        var bent = Make(firstPerson: true, pitch: -35f * DetMath.DegToRad);
        var straight = Make(firstPerson: true, pitch: -35f * DetMath.DegToRad, rig: flat);
        float most = 0f;
        for (int t = 0; t < Ticks(1.2f); t++)
        {
            var frame = t == 0 ? Held(Buttons.Attack) : ActionFrame.Idle;
            bent.Step(frame);
            straight.Step(frame);
            Assert.Equal(straight.World.Service<ViewState>().Eyes, bent.World.Service<ViewState>().Eyes);
            Assert.Equal(straight.World.Service<ViewState>().View.Eye, bent.World.Service<ViewState>().View.Eye);
            var hb = bent.World.Service<CharacterPoses>().Bones(bent.World, bent.Player)[18].Translation;
            var hs = straight.World.Service<CharacterPoses>().Bones(straight.World, straight.Player)[18].Translation;
            most = MathF.Max(most, Vector3.Distance(hb, hs));
        }
        Assert.True(most > 0.15f, $"the bent head moved only {most:0.000} m");
        Assert.True(bent.World.Get<PoseRig>(bent.Player).AimPitch == 0f || bent.World.Get<CharacterAnimation>(bent.Player).Action != ActionRole.None);
    }

    // ---- held things placed and drawn ----

    [Fact]
    public void The_blade_and_the_shield_are_placed_on_their_bones_where_they_are_drawn()
    {
        var a = Make(other: "rudolfGuard");
        a.Step(Held(Buttons.Attack));
        a.World.Get<CombatIntent>(a.Other).Attack = true;
        a.Idle(1);
        a.World.Get<CombatIntent>(a.Other).Attack = false;
        var poses = a.World.Service<CharacterPoses>();
        for (int t = 0; t < Ticks(1.0f); t += 7)
        {
            a.Idle(7);
            foreach (var e in new[] { a.Player, a.Other })
            {
                Assert.True(HeldItems.Blade(a.World, e, out var blade));
                Assert.True(HeldItems.BoneInWorld(a.World, e, 25, out var hand));
                Assert.InRange(Vector3.Distance(blade.Guard, hand.Translation), 0f, 0.12f);
                Assert.InRange(blade.Length, 0.85f, 1.05f);
                // The tip is where the drawn blade's furthest vertex is drawn: the blade you see is the blade that hits.
                var held = HeldItems.Of(a.World, e)[0];
                Assert.True(HeldItems.Placement(a.World, e, out var place));
                var drawn = poses.DrawHeld(a.World, e, 0, place);
                Assert.Same(held.Model, drawn.Model);
                var thing = held.Things.First(x => x.Kind == HeldKind.Blade);
                var tipVertex = held.Model.Vertices.Where(v => v.Bone(0) == thing.Bone)
                    .MaxBy(v => PoseMath.Transform(v.Position, held.Model.Skeleton.InverseBind[thing.Bone]).LengthSquared());
                var drawnTip = PoseMath.Transform(PoseEvaluator.SkinPosition(tipVertex, drawn.Skin), drawn.World);
                Assert.InRange(Vector3.Distance(drawnTip, blade.Tip), 0f, 0.002f);
            }
            Assert.True(HeldItems.Shield(a.World, a.Other, out var shield));
            Assert.True(HeldItems.BoneInWorld(a.World, a.Other, 30, out var arm));
            Assert.InRange(Vector3.Distance(shield.Centre, arm.Translation), 0f, 0.45f);
            Assert.InRange(shield.HalfSize.X + shield.HalfSize.Y + shield.HalfSize.Z, 0.3f, 1.2f);
        }
        Assert.False(HeldItems.Shield(a.World, a.Player, out _));
    }

    // ---- replays ----

    static ulong[] Fight(ulong seed)
    {
        var a = Make(other: "rudolfGuard", seed: seed);
        var script = a.World.Service<ActionScript>();
        Assert.True(ActionScript.TryParse("g:attack@0.5^20,g:strong@2.0^-25,g:block@4+1,g:hurt@6,g:death@8", a.World.Service<CombatSettings>(), out var lines, out var error), error);
        foreach (var line in lines) script.Add(a.Other, line);
        var digests = new ulong[Ticks(10f)];
        for (int t = 0; t < digests.Length; t++)
        {
            var k = t % Ticks(3f);
            var held = (k < 2 || (k >= Ticks(0.4f) && k < Ticks(0.42f)) ? Buttons.Attack : Buttons.None)
                       | (k >= Ticks(1.5f) && k < Ticks(2.5f) ? Buttons.Attack : Buttons.None)
                       | (t >= Ticks(7f) && t < Ticks(8f) ? Buttons.Block : Buttons.None);
            a.Step(new ActionFrame(new Vector2(0, t > Ticks(5f) ? 1 : 0), new Vector2(0.002f, -0.001f), held, default));
            digests[t] = a.World.Digest();
        }
        Assert.True(a.Begun.Count >= 6 && a.Strikes.Count >= 5 && a.Raised.Count >= 1, $"{a.Begun.Count} begun, {a.Strikes.Count} strikes, {a.Raised.Count} blocks");
        return digests;
    }

    [Fact]
    public void A_fight_replays_to_the_same_digests_every_tick()
    {
        var first = Fight(11);
        var again = Fight(11);
        Assert.Equal(first, again);
    }

    [Fact]
    public void Fighting_allocates_nothing_per_tick_once_going()
    {
        var a = Make(other: "rudolfGuard");
        void Round(int t)
        {
            var held = t % 150 < 2 ? Buttons.Attack : t % 150 is >= 60 and < 100 ? Buttons.Block : Buttons.None;
            a.World.Get<CombatIntent>(a.Other).Attack = t % 120 < 3;
            MovementServices.Step(a.World, new ActionFrame(Vector2.Zero, Vector2.Zero, held, default));
        }
        for (int t = 0; t < 400; t++) Round(t);
        Allocations.None("300 ticks of fighting", () =>
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int t = 400; t < 700; t++) Round(t);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        });
    }
}
