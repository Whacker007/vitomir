// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Tests;

/// <summary>
/// Directional attacks: each way of choosing picks the direction it should — automatic by
/// the crosshair on each region of a standing townsman (and the view with no one in front), the mouse by each flick, the keys by
/// each key; a direction Christof has no blow for falls back to the side; the press readies the blow and holds it at its ready
/// point (the busy time not running), the release strikes, a tap is a whole blow struck as soon as before; the cancel key feints a
/// held blow; <c>attack-spin</c> (the service off) is the earlier build's tap-and-hold exactly; the forehand comes from the right and the
/// backhand from the left (the tip's sweep); the duelling brain's choice; a recording replays; nothing allocated per tick. Every
/// test skips without the 1.1 install (VTMR_GAME_DIR).
/// </summary>
public sealed class DirectionTests(ITestOutputHelper output)
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);
    static readonly Dictionary<(string, string?), OneOneCharacter> Loaded = [];

    static OneOneCharacter Character(string name, string? weapon = null)
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        lock (Loaded)
        {
            if (!Loaded.TryGetValue((name, weapon), out var c))
                Loaded[(name, weapon)] = c = Library.Value!.Character(name, weapon) ?? throw new Xunit.Sdk.XunitException($"{name} did not load");
            return c;
        }
    }

    internal sealed class Ring
    {
        public required World World { get; init; }
        public required EntityId Player { get; init; }
        public EntityId Foe { get; init; } = EntityId.None;
        public CharacterAssets Assets => World.Service<CharacterAssets>();
        public readonly List<AttackBegun> Begun = [];
        public readonly List<AttackReleased> Released = [];
        public readonly List<StrikeMoment> Strikes = [];
        public readonly List<SwingCancelled> Cancelled = [];
        public readonly List<AttackSwitched> Switched = [];

        public void Step(ActionFrame frame, int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                MovementServices.Step(World, frame);
                Begun.AddRange(World.Events<AttackBegun>().ThisTick);
                Released.AddRange(World.Events<AttackReleased>().ThisTick);
                Strikes.AddRange(World.Events<StrikeMoment>().ThisTick);
                Cancelled.AddRange(World.Events<SwingCancelled>().ThisTick);
                Switched.AddRange(World.Events<AttackSwitched>().ThisTick);
            }
        }

        public void Idle(int ticks) => Step(ActionFrame.Idle, ticks);

        public string ClipName(int clip) => Assets.Clip(clip).Name;

        /// <summary>The camera turned so the view's line runs through <paramref name="point"/> (world), and a tick stepped so the view is there.</summary>
        public void Look(Vector3 point)
        {
            Idle(1);
            var eye = World.Service<ViewState>().View.Eye;
            var d = point - eye;
            ref var rig = ref World.Get<CameraRig>(Player);
            rig.Yaw = DetMath.Atan2(d.Y, d.X);
            rig.Pitch = DetMath.Atan2(d.Z, MathF.Sqrt(d.X * d.X + d.Y * d.Y));
            Idle(1);
            for (int k = 0; k < 3; k++)   // the eye moves a little as the body turns: aim again from where it is now
            {
                eye = World.Service<ViewState>().View.Eye;
                d = point - eye;
                rig = ref World.Get<CameraRig>(Player);
                rig.Yaw = DetMath.Atan2(d.Y, d.X);
                rig.Pitch = DetMath.Atan2(d.Z, MathF.Sqrt(d.X * d.X + d.Y * d.Y));
                Idle(1);
            }
        }
    }

    static ActionFrame Held(Buttons b, Vector2 move = default, Vector2 look = default) => new(move, look, b, default);
    static int Ticks(float seconds) => (int)MathF.Round(seconds * World.TicksPerSecond);

    /// <summary>
    /// Christof with his broadsword in first person at the origin facing +Y, a townsman (<paramref name="foe"/>) <paramref name="distance"/>
    /// m in front facing him, contact on; directional attacks on unless <paramref name="spin"/> (the service registered and off:
    /// <c>attack-spin</c>), the player's <paramref name="mode"/>.
    /// </summary>
    internal static Ring Make(AttackDirectionMode mode = AttackDirectionMode.Automatic, bool spin = false, string? foe = "Civ12male", float distance = 1.8f,
        bool directions = true, ulong seed = 7, DirectionSettings? settings = null, bool brains = false)
    {
        var assets = new CharacterAssets();
        int kind = OneOneLibrary.Register(assets, Character("Christof", "BroadSword"));
        int foeKind = foe is null ? -1 : OneOneLibrary.Register(assets, Character(foe));
        var level = new TestLevel().Floor(-60, -60, 60, 60).Build();
        var b = new WorldBuilder(seed);
        MovementServices.Register(b, level);
        MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = false });
        b.Animation(assets);
        b.HitVolumes();
        BodyMotionSystem.Register(b);
        CombatServices.Register(b);
        ContactServices.Register(b, new ContactConfig());
        if (brains) { DuelBrains.Register(b); GuardSkills.Register(b); }   // a brain on the foe, and its guard skill
        if (directions)
        {
            b.Service(new DirectionalAttacks { Enabled = !spin, Settings = settings ?? new DirectionSettings() });
            b.Service(new AttackDirectionInput { Mode = mode });
        }
        var w = b.Build();
        w.Service<HitVolumes>().Prepare(assets);
        ContactServices.Prepare(w);
        var e = MovementServices.SpawnControlled(w, new(0, 0, 0.01f), DetMath.HalfPi, Locomotion.Sandbox);
        w.Set(e, CharacterAnimation.For(assets, kind));
        w.Set(e, MotionInput.Standing);
        w.Set(e, new MasqueradeState { Mode = MasqueradeMode.Unbound });
        CombatServices.Arm(w, e);
        var o = EntityId.None;
        if (foeKind >= 0)
        {
            o = MovementServices.SpawnBody(w, new(0, distance, 0.01f), -DetMath.HalfPi, Locomotion.Sandbox);
            w.Set(o, CharacterAnimation.For(assets, foeKind));
            w.Set(o, MotionInput.Standing);
            CombatServices.Arm(w, o);
        }
        var r = new Ring { World = w, Player = e, Foe = o };
        foreach (var held in new[] { Buttons.SwitchView, Buttons.None, Buttons.SwitchView }) r.Step(Held(held));   // first person
        r.Idle(30);
        return r;
    }

    // ---- a held blow switched to another direction ----

    /// <summary>As <see cref="Ring.Look"/>, attack held all the while: the view turned so its line runs through <paramref name="point"/>.</summary>
    static void LookHeld(Ring r, Vector3 point)
    {
        for (int k = 0; k < 4; k++)
        {
            var eye = r.World.Service<ViewState>().View.Eye;
            var d = point - eye;
            ref var rig = ref r.World.Get<CameraRig>(r.Player);
            rig.Yaw = DetMath.Atan2(d.Y, d.X);
            rig.Pitch = DetMath.Atan2(d.Z, MathF.Sqrt(d.X * d.X + d.Y * d.Y));
            r.Step(Held(Buttons.Attack));
        }
    }

    /// <summary>The backhand's (chrisAttack2Sword's) ready point and its clip.</summary>
    static (int Clip, float Ready, float Rate, float Strike) Backhand(Ring r)
    {
        var m = r.World.Service<DirectionalAttacks>().Motion(r.World, r.Assets, r.World.Get<CharacterAnimation>(r.Player).Kind, AttackDirection.Left);
        return (m.Clip, m.HoldSeconds, m.Rate, m.StrikeSeconds);
    }

    /// <summary>Released after a switch to the backhand: it strikes the backhand, its release-to-strike after the release.</summary>
    void StrikesTheBackhand(Ring r)
    {
        var (clip, ready, rate, strike) = Backhand(r);
        r.Idle(1);
        long releasedAt = r.World.Tick;
        Assert.Equal(AttackDirection.Left, r.Released[^1].Direction);
        Assert.Equal(clip, r.World.Get<CharacterAnimation>(r.Player).ActionClip);
        r.Idle(Ticks(1f));
        long want = (long)MathF.Round((strike - ready) / rate * World.TicksPerSecond);
        long struck = r.Strikes[^1].Tick - releasedAt;
        output.WriteLine($"released, the backhand struck {struck} ticks later (its release to strike {want})");
        Assert.InRange(struck, want - 2, want + 2);
    }

    [Fact]
    public void Keys_a_blow_held_1_s_switches_to_the_move_key_held_now_and_the_release_strikes_the_new_one()
    {
        var r = Make(AttackDirectionMode.Keys);
        r.Look(On(r, 0.15f, 1.2f));
        r.Step(Held(Buttons.Attack));                                                   // no key at the press: the crosshair chooses, right
        Assert.Equal(AttackDirection.Right, r.Begun[^1].Direction);
        r.Step(Held(Buttons.Attack), Ticks(1f));
        Assert.True(ActionChannel.AtReady(r.World.Get<CharacterAnimation>(r.Player)));
        int swing = r.World.Get<Combatant>(r.Player).Swing;
        r.Step(Held(Buttons.Attack, move: new Vector2(-1f, 0f)), 3);                    // A held, attack still held
        var x = Assert.Single(r.Switched);
        Assert.Equal((AttackDirection.Right, AttackDirection.Left, true, swing), (x.From, x.To, x.AtReady, x.Swing));
        var (clip, ready, _, _) = Backhand(r);
        ref var a = ref r.World.Get<CharacterAnimation>(r.Player);
        Assert.Equal(clip, a.ActionClip);
        Assert.Equal(0.25f, a.ActionCrossSeconds, 4);                                   // the switch's cross-fade
        Assert.Equal(ready, a.ActionTime, 4);                                           // straight to its ready point, held there
        Assert.True(Attacks.NextBlow(r.World, r.Player, out var next) && next.Held && next.Direction == AttackDirection.Left);   // the arc shows it
        Assert.Single(r.Begun);                                                         // the same blow: no new swing
        r.Step(Held(Buttons.Attack), Ticks(0.5f));                                      // no key now: it keeps its new direction
        Assert.Single(r.Switched);
        Assert.Empty(r.Strikes);
        StrikesTheBackhand(r);
        Assert.Equal(swing, r.Released[^1].Swing);
    }

    [Fact]
    public void Mouse_a_flick_while_held_switches_it_and_less_than_the_dead_zone_does_not()
    {
        float deg = DetMath.DegToRad;
        var r = Make(AttackDirectionMode.Mouse);
        r.Look(On(r, 0.15f, 1.2f));
        r.Idle(Ticks(0.5f));
        r.Step(Held(Buttons.Attack));                                                   // no flick: automatic, right
        Assert.Equal(AttackDirection.Right, r.Begun[^1].Direction);
        r.Step(Held(Buttons.Attack), Ticks(1f));
        r.Step(Held(Buttons.Attack, look: new Vector2(0.05f * deg, 0f)), 6);            // 0.3° in all: under the dead zone
        Assert.Empty(r.Switched);
        r.Step(Held(Buttons.Attack, look: new Vector2(0.6f * deg, 0f)), 6);             // 3.6° to the left over 0.05 s
        var x = Assert.Single(r.Switched);
        Assert.Equal((AttackDirection.Right, AttackDirection.Left), (x.From, x.To));
        r.Step(Held(Buttons.Attack), Ticks(0.5f));                                      // the flick's sum dies away: it keeps the left
        Assert.Single(r.Switched);
        StrikesTheBackhand(r);
    }

    [Fact]
    public void Automatic_the_crosshair_moved_across_his_centre_line_while_held_switches_it()
    {
        var r = Make(AttackDirectionMode.Automatic);
        r.Look(On(r, 0.15f, 1.2f));
        r.Step(Held(Buttons.Attack));
        Assert.Equal(AttackDirection.Right, r.Begun[^1].Direction);
        r.Step(Held(Buttons.Attack), Ticks(1f));
        Assert.Empty(r.Switched);                                                       // held still: the same side
        LookHeld(r, On(r, -0.15f, 1.2f));                                               // the crosshair to the left of his line
        var x = Assert.Single(r.Switched);
        Assert.Equal((AttackDirection.Right, AttackDirection.Left), (x.From, x.To));
        r.Step(Held(Buttons.Attack), Ticks(0.3f));
        StrikesTheBackhand(r);
    }

    [Fact]
    public void A_switch_before_the_ready_point_goes_into_the_new_blow_s_wind_up_at_the_same_share_and_holds_at_its_ready_point()
    {
        var r = Make(AttackDirectionMode.Keys);
        r.Look(On(r, 0.15f, 1.2f));
        r.Step(Held(Buttons.Attack));
        r.Step(Held(Buttons.Attack), 8);                                                // a little way into the forehand's wind-up
        ref var a0 = ref r.World.Get<CharacterAnimation>(r.Player);
        Assert.False(ActionChannel.AtReady(a0));
        float share = a0.ActionTime / a0.ActionHoldAt;
        r.Step(Held(Buttons.Attack, move: new Vector2(-1f, 0f)));
        var x = Assert.Single(r.Switched);
        Assert.False(x.AtReady);
        var (clip, ready, rate, _) = Backhand(r);
        ref var a = ref r.World.Get<CharacterAnimation>(r.Player);
        Assert.Equal(clip, a.ActionClip);
        output.WriteLine($"switched at {share:0.00} of the forehand's wind-up: the backhand at {a.ActionTime:0.000} s of its ready {ready:0.000}");
        Assert.InRange(a.ActionTime, ready * share - 0.01f, ready * share + rate * World.TickSeconds + 0.01f);   // the same share (and the switch tick's own step)
        r.Step(Held(Buttons.Attack), Ticks(0.6f));
        Assert.True(ActionChannel.AtReady(r.World.Get<CharacterAnimation>(r.Player)));
        Assert.Equal(ready, r.World.Get<CharacterAnimation>(r.Player).ActionTime, 4);
        StrikesTheBackhand(r);
    }

    [Fact]
    public void Without_the_switch_in_the_data_a_held_blow_keeps_its_direction_and_the_same_input_steps_to_the_same_digests()
    {
        ulong[] Run(DirectionSettings? settings)
        {
            var r = Make(AttackDirectionMode.Keys, settings: settings);
            r.Look(On(r, 0.15f, 1.2f));
            r.Step(Held(Buttons.Attack), Ticks(1f));
            r.Step(Held(Buttons.Attack, move: new Vector2(-1f, 0f)), 3);
            r.Step(Held(Buttons.Attack), Ticks(0.3f));
            r.Idle(Ticks(1.2f));
            return [r.World.Digest(), (ulong)r.Switched.Count, (ulong)r.Released[^1].Direction];
        }
        var off = Run(new DirectionSettings { SwitchWhileHeld = false });
        Assert.Equal(0UL, off[1]);
        Assert.Equal((ulong)AttackDirection.Right, off[2]);
        var on = Run(null);
        Assert.Equal(on, Run(null));                                                    // deterministic from the tick's input
        Assert.Equal((1UL, (ulong)AttackDirection.Left), (on[1], on[2]));
    }

    // ---- automatic ----

    /// <summary>The townsman's point <paramref name="across"/> m to the player's right of his centre line (the player faces +Y: right is +X) at <paramref name="height"/> m.</summary>
    static Vector3 On(Ring r, float across, float height)
    {
        var f = r.World.Get<Body>(r.Foe).Feet;
        return new Vector3(f.X + across, f.Y, f.Z + height);
    }

    [Fact]
    public void Automatic_chooses_by_where_the_crosshair_is_on_the_foe_left_right_above_the_shoulders_below_the_waist()
    {
        var r = Make();
        var dirs = r.World.Service<DirectionalAttacks>();
        (float Across, float Height, AttackDirection Want, AttackDirection Lean)[] cases =
        [
            (-0.15f, 1.20f, AttackDirection.Left, AttackDirection.Left),     // his chest, left of his centre line
            (0.15f, 1.20f, AttackDirection.Right, AttackDirection.Right),    // right of it
            (-0.05f, 1.65f, AttackDirection.Overhead, AttackDirection.Left), // his head (above 0.80 of 1.8 m)
            (0.10f, 0.55f, AttackDirection.Thrust, AttackDirection.Right),   // his thigh (below 0.52)
        ];
        foreach (var (across, height, want, lean) in cases)
        {
            r.Look(On(r, across, height));
            var intent = r.World.Get<CombatIntent>(r.Player);
            var choice = dirs.Automatic(r.World, r.Player, true, intent, out var foe, out float got, out float share);
            output.WriteLine($"crosshair {across:+0.00;-0.00} m at {height:0.00} m: {choice.Direction} (lean {choice.Lean}), across {got:+0.000;-0.000} m, height {share:0.00} of his");
            Assert.Equal(r.Foe, foe);
            Assert.Equal((want, lean, DirectionSource.Foe), (choice.Direction, choice.Lean, choice.Source));
            Assert.Equal(across, got, 0.03f);
        }
    }

    [Fact]
    public void A_press_strikes_from_the_side_the_crosshair_is_on_and_an_overhead_or_thrust_falls_back_to_that_side()
    {
        // Christof has 1.1's two quick blows only (when the stitched overhead and stab are not in the table): his head left of centre
        // swings the backhand from the left, his thigh right of centre the forehand from the right.
        (float Across, float Height, AttackDirection Want, string Clip)[] cases =
        [
            (-0.15f, 1.20f, AttackDirection.Left, "chrisAttack2Sword"),
            (0.15f, 1.20f, AttackDirection.Right, "chrisAttackSword"),
            (-0.05f, 1.65f, AttackDirection.Left, "chrisAttack2Sword"),
            (0.10f, 0.55f, AttackDirection.Right, "chrisAttackSword"),
            (0.15f, 1.20f, AttackDirection.Right, "chrisAttackSword"),     // the same side twice: no alternation
        ];
        var r = Make();
        foreach (var (across, height, want, clip) in cases)
        {
            r.Look(On(r, across, height));
            int before = r.Begun.Count;
            r.Step(Held(Buttons.Attack));
            r.Idle(1);
            var b = r.Begun[before];
            Assert.Equal((want, clip), (b.Direction, r.ClipName(b.Clip)));
            r.Idle(Ticks(1.3f));
        }
    }

    [Fact]
    public void With_no_one_in_front_the_view_against_the_body_chooses_and_the_forehand_by_default()
    {
        var r = Make(foe: null);
        var dirs = r.World.Service<DirectionalAttacks>();
        var feet = r.World.Get<Body>(r.Player).Feet;
        (Vector3 At, AttackDirection Want)[] cases =
        [
            (feet + new Vector3(0f, 3f, 1.6f), AttackDirection.Right),            // straight ahead, level: the default side
            (feet + new Vector3(0f, 3f, 3.0f), AttackDirection.Overhead),         // looking up 25°
            (feet + new Vector3(0f, 3f, -0.2f), AttackDirection.Thrust),          // looking down 31°
        ];
        foreach (var (at, want) in cases)
        {
            r.Look(at);
            var choice = dirs.Automatic(r.World, r.Player, true, r.World.Get<CombatIntent>(r.Player));
            Assert.Equal((want, DirectionSource.NoFoe), (choice.Direction, choice.Source));
        }
        // An overhead with no blow for it and no side: the forehand.
        var d = dirs.Resolve(r.World, r.Assets, r.World.Get<CharacterAnimation>(r.Player).Kind, new DirectionChoice(AttackDirection.Overhead, AttackDirection.None, DirectionSource.NoFoe),
            AttackDirection.None, out var motion);
        Assert.Equal((AttackDirection.Right, "chrisAttackSword"), (d, r.ClipName(motion.Clip)));
    }

    // ---- the mouse and the keys ----

    [Fact]
    public void The_mouse_chooses_by_its_flick_and_under_the_dead_zone_leaves_it_to_automatic()
    {
        var s = new DirectionSettings();
        float deg = DetMath.DegToRad;
        Assert.Equal(AttackDirection.Left, DirectionalAttacks.Mouse(new Vector2(3f * deg, 0.5f * deg), s)!.Value.Direction);   // turned left
        Assert.Equal(AttackDirection.Right, DirectionalAttacks.Mouse(new Vector2(-3f * deg, -1f * deg), s)!.Value.Direction);
        Assert.Equal(AttackDirection.Overhead, DirectionalAttacks.Mouse(new Vector2(0.5f * deg, 3f * deg), s)!.Value.Direction);
        Assert.Equal(AttackDirection.Thrust, DirectionalAttacks.Mouse(new Vector2(-1f * deg, -3f * deg), s)!.Value.Direction);
        Assert.Equal(AttackDirection.Right, DirectionalAttacks.Mouse(new Vector2(-1f * deg, -3f * deg), s)!.Value.Lean);
        Assert.Null(DirectionalAttacks.Mouse(new Vector2(0.5f * deg, 0.5f * deg), s));                                      // under 1°
        Assert.Equal(AttackDirection.Right, DirectionalAttacks.Mouse(new Vector2(3f * deg, 0f), s with { MouseInvert = true })!.Value.Direction);

        // Through the attacks: a flick of the view to the left in the tenth of a second before the press, then the press.
        var r = Make(AttackDirectionMode.Mouse);
        r.Look(On(r, 0.15f, 1.2f));                                                         // the crosshair right of his centre line
        r.Step(Held(Buttons.None, look: new Vector2(0.6f * deg, 0f)), 6);                   // 3.6° to the left over 0.05 s
        r.Step(Held(Buttons.Attack));
        Assert.Equal(AttackDirection.Left, r.Begun[^1].Direction);
        r.Idle(Ticks(1.3f));
        r.Step(Held(Buttons.None, look: new Vector2(-0.6f * deg, 0f)), 6);
        r.Step(Held(Buttons.Attack));
        Assert.Equal(AttackDirection.Right, r.Begun[^1].Direction);
        r.Idle(Ticks(1.3f));
        // No flick (the leaky sum long gone): automatic, by where the crosshair is.
        r.Look(On(r, -0.15f, 1.2f));
        r.Idle(Ticks(1f));
        r.Step(Held(Buttons.Attack));
        Assert.Equal(AttackDirection.Left, r.Begun[^1].Direction);
    }

    [Fact]
    public void The_keys_choose_by_the_movement_key_held_at_the_press_and_with_none_leave_it_to_automatic()
    {
        var s = new DirectionSettings();
        Assert.Equal(AttackDirection.Left, DirectionalAttacks.Keys(new Vector2(-1f, 0f), s)!.Value.Direction);
        Assert.Equal(AttackDirection.Right, DirectionalAttacks.Keys(new Vector2(1f, 0f), s)!.Value.Direction);
        Assert.Equal(AttackDirection.Overhead, DirectionalAttacks.Keys(new Vector2(0f, 1f), s)!.Value.Direction);
        Assert.Equal(AttackDirection.Thrust, DirectionalAttacks.Keys(new Vector2(0f, -1f), s)!.Value.Direction);
        Assert.Equal(AttackDirection.Left, DirectionalAttacks.Keys(new Vector2(-1f, 1f), s)!.Value.Direction);   // A with W: the side first
        Assert.Null(DirectionalAttacks.Keys(Vector2.Zero, s));

        var r = Make(AttackDirectionMode.Keys);
        r.Look(On(r, 0.15f, 1.2f));                                     // the crosshair says right
        r.Step(Held(Buttons.Attack, move: new Vector2(-1f, 0f)));      // A held: from the left
        r.Step(Held(Buttons.None, move: new Vector2(-1f, 0f)));
        Assert.Equal(AttackDirection.Left, r.Begun[^1].Direction);
        r.Idle(Ticks(1.5f));
        r.Look(On(r, -0.15f, 1.2f));                                    // the crosshair says left
        r.Step(Held(Buttons.Attack, move: new Vector2(1f, 0f)));       // D held: from the right
        Assert.Equal(AttackDirection.Right, r.Begun[^1].Direction);
        r.Idle(Ticks(1.5f));
        r.Look(On(r, -0.15f, 1.2f));
        r.Step(Held(Buttons.Attack));                                   // none held: automatic
        Assert.Equal(AttackDirection.Left, r.Begun[^1].Direction);
    }

    // ---- ready and release (no one in front: nothing stops the blade, every blow reaches its strike) ----

    [Fact]
    public void The_press_readies_the_blow_and_holds_it_at_its_ready_point_and_the_release_strikes()
    {
        var r = Make(foe: null);
        r.Step(Held(Buttons.Attack));
        var begun = Assert.Single(r.Begun);
        Assert.Equal((AttackDirection.Right, "chrisAttackSword"), (begun.Direction, r.ClipName(begun.Clip)));   // no one in front: the forehand
        ref var c0 = ref r.World.Get<Combatant>(r.Player);
        Assert.True(c0.Readied);
        float ready = c0.ReadyAt;
        int kind = r.World.Get<CharacterAnimation>(r.Player).Kind;
        var set = r.Assets.Kind(kind).Actions!;
        var window = r.World.Service<ActiveWindows>().For(r.Assets, kind, begun.Clip, set.Quick[0].StrikeSeconds, r.World.Service<ContactConfig>().Window);
        output.WriteLine($"chrisAttackSword: ready at {ready:0.000} s of the clip (frame {ready * 30:0.0}), the window {window.From:0.000} to {window.To:0.000}, the strike {set.Quick[0].StrikeSeconds:0.000}");
        Assert.InRange(ready, 0.05f, window.From);
        // Held two seconds: the clip waits at the ready point, the busy time does not run, nothing is struck.
        r.Step(Held(Buttons.Attack), Ticks(2f));
        Assert.Equal(ready, r.World.Get<CharacterAnimation>(r.Player).ActionTime, 4);
        Assert.Empty(r.Strikes);
        Assert.Empty(r.Released);
        float busyHeld = r.World.Get<Combatant>(r.Player).Busy;
        r.Step(Held(Buttons.Attack), 30);
        Assert.Equal(busyHeld, r.World.Get<Combatant>(r.Player).Busy);
        // Released: it strikes, (strike − ready) ÷ rate later, and is busy the rest of its own time.
        r.Idle(1);
        var rel = Assert.Single(r.Released);
        Assert.InRange(rel.HeldSeconds, 1.85f, 2.0f);   // 2.25 s pressed less the wind-up (0.29 s of clip at 0.88): 1.93 s at the ready point
        long releasedAt = r.World.Tick;
        float rate = set.Quick[0].Rate;
        float rest = set.Quick[0].BusySeconds - ready / rate;
        Assert.Equal(rest, r.World.Get<Combatant>(r.Player).Busy, 3);   // the whole blow's busy time less the clip played (the hold not counted)
        r.Idle(Ticks(1.2f));
        var strike = Assert.Single(r.Strikes);
        long want = (long)MathF.Round((set.Quick[0].StrikeSeconds - ready) / rate * World.TicksPerSecond);
        output.WriteLine($"released at tick {releasedAt}: struck {strike.Tick - releasedAt} ticks later (want {want}); busy after the release {rest:0.000} s");
        Assert.InRange(strike.Tick - releasedAt, want - 2, want + 2);
        // A second press is a new blow once this one's busy time is over.
        r.Step(Held(Buttons.Attack)); r.Idle(1);
        Assert.Equal(2, r.Begun.Count);
    }

    [Fact]
    public void A_tap_is_a_whole_blow_struck_as_soon_after_its_press_as_attack_spin_s_after_its_release()
    {
        var on = Make(foe: null);
        on.Step(Held(Buttons.Attack)); on.Idle(Ticks(1.2f));
        var spin = Make(spin: true, foe: null);
        spin.Step(Held(Buttons.Attack)); spin.Idle(Ticks(1.2f));
        long tapOn = on.Strikes[0].Tick - on.Begun[0].Tick, tapSpin = spin.Strikes[0].Tick - spin.Begun[0].Tick;
        output.WriteLine($"a tap: struck {tapOn} ticks after its press (directional) and {tapSpin} after its release (1.1's)");
        Assert.Equal(on.ClipName(on.Begun[0].Clip), spin.ClipName(spin.Begun[0].Clip));
        Assert.InRange(tapOn, tapSpin - 1, tapSpin + 1);
        Assert.Equal(0f, Assert.Single(on.Released).HeldSeconds);
        // Busy the clip's own time from its press (the earlier build's from its release, a tick later).
        Assert.True(on.World.Get<Combatant>(on.Player).Busy <= 0f);
        on.Step(Held(Buttons.Attack)); on.Idle(1);
        Assert.Equal(2, on.Begun.Count);
    }

    [Fact]
    public void The_cancel_key_feints_a_held_blow_and_its_release_strikes_nothing()
    {
        var r = Make(foe: null);
        r.Step(Held(Buttons.Attack), Ticks(0.8f));
        Assert.True(r.World.Get<Combatant>(r.Player).Readied);
        Assert.True(SwingCancel.Can(r.World, r.Player, SwingCancelCause.Cancel));
        Assert.True(SwingCancel.Do(r.World, r.Player, SwingCancelCause.Cancel));
        var x = Assert.Single(r.World.Events<SwingCancelled>().ThisTick);
        Assert.True(x.BeforeStrike);
        r.Step(Held(Buttons.Attack), 2);
        Assert.False(r.World.Get<Combatant>(r.Player).Readied);
        r.Idle(Ticks(1.2f));
        Assert.Empty(r.Strikes);
        Assert.Empty(r.Released);
        // The body is free again after the feint's return: the next press readies a new blow.
        r.Step(Held(Buttons.Attack)); r.Idle(1);
        Assert.Equal(2, r.Begun.Count);
    }

    [Fact]
    public void A_press_during_a_swing_is_buffered_with_its_direction_and_begins_held_ready_if_still_held()
    {
        var r = Make(foe: null);
        r.Step(Held(Buttons.Attack)); r.Idle(Ticks(0.3f));          // the forehand under way
        r.World.Get<CombatIntent>(r.Player).Direction = AttackDirection.Left;   // as the brain or a script chooses
        r.Step(Held(Buttons.Attack), Ticks(1.5f));                  // pressed mid-swing, held past its end
        Assert.Equal(2, r.Begun.Count);
        Assert.Equal((AttackDirection.Left, "chrisAttack2Sword"), (r.Begun[1].Direction, r.ClipName(r.Begun[1].Clip)));
        long gap = r.Begun[1].Tick - r.Begun[0].Tick;
        Assert.InRange(gap, Ticks(1.1f) - 1, Ticks(1.1f) + 1);       // when the first's busy time (1.1 s) is over
        Assert.True(r.World.Get<Combatant>(r.Player).Readied);     // held ready, not struck
        Assert.Single(r.Released);
        Assert.Single(r.Strikes);
        r.Idle(Ticks(1.2f));
        Assert.Equal(2, r.Released.Count);
        Assert.Equal(2, r.Strikes.Count);
    }

    [Fact]
    public void Every_kind_s_blows_have_a_ready_point_before_their_active_window()
    {
        var assets = new CharacterAssets();
        (string Name, string? Weapon)[] kinds = [("Christof", "BroadSword"), ("Christof", null), ("rudolfGuard", null), ("TeutonicKnight", null), ("KnightOfStJohn", null)];
        var ids = kinds.Select(k => OneOneLibrary.Register(assets, Character(k.Name, k.Weapon))).ToArray();
        var b = new WorldBuilder(1);
        MovementServices.Register(b, new TestLevel().Floor(-10, -10, 10, 10).Build());
        b.Animation(assets);
        CombatServices.Register(b);
        ContactServices.Register(b, new ContactConfig());
        foreach (var rule in new[] { ReadyRule.Turn, ReadyRule.Window })
        {
            var dirs = new DirectionalAttacks { Settings = new DirectionSettings { Ready = rule } };
            b.Service(dirs);
            var w = b.Build();
            ContactServices.Prepare(w);
            for (int i = 0; i < kinds.Length; i++)
                foreach (var d in new[] { AttackDirection.Left, AttackDirection.Right })
                {
                    var m = dirs.Motion(w, assets, ids[i], d);
                    if (!m.Has) { output.WriteLine($"{rule}: {kinds[i].Name} {kinds[i].Weapon ?? "(own)"} {d}: none (falls back)"); continue; }
                    var win = w.Service<ActiveWindows>().For(assets, ids[i], m.Clip, m.StrikeSeconds, w.Service<ContactConfig>().Window);
                    output.WriteLine($"{rule}: {kinds[i].Name} {kinds[i].Weapon ?? "(own)"} {d}: {assets.Clip(m.Clip).Name} ready {m.HoldSeconds:0.000} s (frame {m.HoldSeconds * 30:0.0}), window {win.From:0.000}-{win.To:0.000}, strike {m.StrikeSeconds:0.000}; release to strike {(m.StrikeSeconds - m.HoldSeconds) / m.Rate:0.000} s, press to ready {m.HoldSeconds / m.Rate:0.000} s");
                    Assert.InRange(m.HoldSeconds, 0f, win.Has ? win.From : m.StrikeSeconds);
                }
            b = new WorldBuilder(1);
            MovementServices.Register(b, new TestLevel().Floor(-10, -10, 10, 10).Build());
            b.Animation(assets);
            CombatServices.Register(b);
            ContactServices.Register(b, new ContactConfig());
        }
    }

    [Fact]
    public void A_kind_on_Christof_s_skeleton_borrows_his_backhand_and_one_on_another_does_not()
    {
        var assets = new CharacterAssets();
        int christof = OneOneLibrary.Register(assets, Character("Christof", "BroadSword"));
        int guard = OneOneLibrary.Register(assets, Character("rudolfGuard"));
        int knight = OneOneLibrary.Register(assets, Character("TeutonicKnight"));
        var b = new WorldBuilder(1);
        MovementServices.Register(b, new TestLevel().Floor(-10, -10, 10, 10).Build());
        b.Animation(assets);
        CombatServices.Register(b);
        var dirs = new DirectionalAttacks { Rows = [.. DirectionalAttacks.DefaultRows, new DirectionRow(null, null, "quick:1|lend:chrisAttack2Sword", null, null, null)] };
        b.Service(dirs);
        var w = b.Build();
        var lent = dirs.Motion(w, assets, guard, AttackDirection.Left);
        var own = assets.Kind(guard).Actions!.Quick[0];
        Assert.Equal("chrisAttack2Sword", assets.Clip(lent.Clip).Name);
        Assert.Equal(assets.Clip(own.Clip).Duration / own.Rate, assets.Clip(lent.Clip).Duration / lent.Rate, 4);   // the guard's own STRIKETIME
        Assert.Equal(own.BusySeconds, lent.BusySeconds);
        Assert.Equal(assets.Kind(christof).Actions!.Quick[1].StrikeSeconds, lent.StrikeSeconds);
        Assert.Equal("erikAttack22HSword", assets.Clip(dirs.Motion(w, assets, knight, AttackDirection.Left).Clip).Name);   // his own second
        Assert.Equal("chrisAttack2Sword", assets.Clip(dirs.Motion(w, assets, christof, AttackDirection.Left).Clip).Name);
        Assert.False(dirs.Has(w, assets, guard, AttackDirection.Overhead));
    }

    /// <summary>
    /// The module's row naming the stitched blows (<c>stitched:overhead</c>, <c>stitched:thrust</c>, for
    /// every one-handed bearer) resolves them from the kind's own action set, each held ready at its own ready point; a kind of
    /// another skeleton (the Teutonic Knight) has none and falls back.
    /// </summary>
    [Fact]
    public void The_table_s_stitched_names_give_the_overhead_and_the_thrust_held_at_their_own_ready_points()
    {
        var assets = new CharacterAssets();
        int christof = OneOneLibrary.Register(assets, Character("Christof", "BroadSword"));
        int guard = OneOneLibrary.Register(assets, Character("rudolfGuard"));
        int knight = OneOneLibrary.Register(assets, Character("TeutonicKnight"));
        var b = new WorldBuilder(1);
        MovementServices.Register(b, new TestLevel().Floor(-10, -10, 10, 10).Build());
        b.Animation(assets);
        CombatServices.Register(b);
        var dirs = new DirectionalAttacks { Rows = [.. DirectionalAttacks.DefaultRows, new DirectionRow(null, 1, null, null, "stitched:overhead", "stitched:thrust")] };
        b.Service(dirs);
        var w = b.Build();
        foreach (int kind in new[] { christof, guard })
        {
            var set = assets.Kind(kind).Actions!;
            foreach (var (d, name) in new[] { (AttackDirection.Overhead, StitchedAttacks.Overhead), (AttackDirection.Thrust, StitchedAttacks.Thrust) })
            {
                var m = dirs.Motion(w, assets, kind, d);
                Assert.Equal(StitchedAttacks.ClipName(name), assets.Clip(m.Clip).Name);
                Assert.Equal(set.StitchedReady(name), m.HoldSeconds);
                Assert.InRange(m.HoldSeconds, 0f, m.StrikeSeconds);
            }
            Assert.Equal("chrisAttackSword", assets.Clip(dirs.Motion(w, assets, kind, AttackDirection.Right).Clip).Name);   // the sides as before
        }
        Assert.False(dirs.Has(w, assets, knight, AttackDirection.Overhead));
        Assert.False(dirs.Has(w, assets, knight, AttackDirection.Thrust));
    }

    // ---- the duelling brain against a blow held ready (the parry waits for the release) ----

    /// <summary>
    /// rudolfGuard 1.5 m before Christof with a brain that parries every blow it can (ParryChance 1, no block, no dodge, no blows of its
    /// own); Christof presses with the crosshair right of the guard's centre line, holds <paramref name="hold"/> s, lets go. Returns the
    /// guard's parry windows opened (their ticks), the press and release ticks, the swing's strike tick (by the clip: its strike mark less
    /// where it was let go, at its rate) and the guard's brain.
    /// </summary>
    (List<long> Parries, long Press, long Release, long Strike, DuelBrain Brain, Ring R) HeldAgainstAParry(float hold, bool spin = false, bool directions = true, float ownBusy = 0f)
    {
        var r = Make(foe: "rudolfGuard", distance: 1.5f, spin: spin, directions: directions, brains: true);
        var w = r.World;
        var book = w.Service<DuelBrainBook>();
        float reach = DuelBrains.ReachOf(r.Assets, w.Get<CharacterAnimation>(r.Foe).Kind);
        float christofReach = DuelBrains.ReachOf(r.Assets, w.Get<CharacterAnimation>(r.Player).Kind);
        w.Set(r.Foe, DuelBrain.For(BrainKind.Duel, r.Player, book.Add(new DuelBrainSettings { ParryChance = 1f, BlockChance = 0f, StrikeShare = 0.1f, CloseShare = 3f, TooCloseShare = 0f, BackOffChance = 0f }),
            reach, christofReach));
        r.Look(On(r, 0.15f, 1.2f));
        r.Idle(10);
        var parries = new List<long>();
        void Tick(Buttons held)
        {
            r.Step(Held(held));
            foreach (var p in w.Events<ParryBegun>().ThisTick) if (p.Entity == r.Foe) parries.Add(p.Tick);
        }
        if (ownBusy > 0f) w.Get<Combatant>(r.Foe).Busy = ownBusy;   // as if its own swing ran on that long
        long press = w.Tick + 1;
        int held = Math.Max(1, Ticks(hold));
        for (int t = 0; t < held; t++) Tick(Buttons.Attack);
        long release = w.Tick + 1;
        var c = w.Get<Combatant>(r.Player);
        var anim = w.Get<CharacterAnimation>(r.Player);
        long strike = spin ? -1 : release + (long)MathF.Round((c.StrikeAt - anim.ActionTime) / anim.ActionRate * World.TicksPerSecond);
        for (int t = 0; t < Ticks(1.6f); t++) Tick(Buttons.None);
        return (parries, press, release, strike, w.Get<DuelBrain>(r.Foe), r);
    }

    /// <summary>What the guard's brain did, tick by tick, while Christof held a blow ready and let it go (<see cref="HoldScene"/>).</summary>
    internal sealed record HoldTrace(long Press, long Release, long Strike, List<long> Parries, List<long> Attacks, List<(long Tick, bool Blocking, bool ParryPending)> Ticks, DuelBrain Brain);

    /// <summary>
    /// The guard (rudolfGuard, 1.5 m away) with a brain of <paramref name="numbers"/> and a guard skill whose reaction is <paramref name="react"/>
    /// s (its factor over the first module's 0.15 s; the module's react_seconds: 0.30, 0.24, 0.15, 0.11, 0.09 along the technique, so Melee 0 about 0.23 and
    /// Melee 5 about 0.11 for the combat rules' newly Embraced), Christof pressing with the crosshair right of the guard's centre line, holding the
    /// blow <paramref name="hold"/> s at its ready point (after its wind-up to it, 0.33 s for Christof's forehand),
    /// letting go; <paramref name="after"/> (when given) the brain's numbers from the press on (a second entry in its book).
    /// </summary>
    internal HoldTrace HoldScene(float hold, DuelBrainSettings numbers, float react = 0.15f, DuelBrainSettings? after = null, bool spin = false, float pause = 0f)
    {
        var r = Make(foe: "rudolfGuard", distance: 1.5f, spin: spin, brains: true);
        var w = r.World;
        var book = w.Service<DuelBrainBook>();
        float reach = DuelBrains.ReachOf(r.Assets, w.Get<CharacterAnimation>(r.Foe).Kind);
        float christofReach = DuelBrains.ReachOf(r.Assets, w.Get<CharacterAnimation>(r.Player).Kind);
        w.Set(r.Foe, DuelBrain.For(BrainKind.Duel, r.Player, book.Add(numbers), reach, christofReach));
        int second = after is null ? -1 : book.Add(after);
        w.Set(r.Foe, GuardSkill.Normal with { React = react / 0.15f });
        if (pause > 0f) w.Get<DuelBrain>(r.Foe).PauseLeft = pause;   // the guard in a pause from the start
        r.Look(On(r, 0.15f, 1.2f));
        r.Idle(10);
        var parries = new List<long>();
        var attacks = new List<long>();
        var ticks = new List<(long, bool, bool)>();
        void Tick(Buttons held)
        {
            r.Step(Held(held));
            foreach (var p in w.Events<ParryBegun>().ThisTick) if (p.Entity == r.Foe) parries.Add(p.Tick);
            foreach (var x in w.Events<AttackBegun>().ThisTick) if (x.Entity == r.Foe) attacks.Add(x.Tick);
            ticks.Add((w.Tick - 1, w.Get<Combatant>(r.Foe).Blocking, w.Get<DuelBrain>(r.Foe).ParryPending));
        }
        long press = w.Tick + 1;
        Tick(Buttons.Attack);
        if (second >= 0) w.Get<DuelBrain>(r.Foe).Book = second;
        if (!spin) for (int t = 0; t < Ticks(1f) && !ActionChannel.AtReady(w.Get<CharacterAnimation>(r.Player)); t++) Tick(Buttons.Attack);
        for (int t = 0; t < Ticks(hold); t++) Tick(Buttons.Attack);
        long release = w.Tick + 1;
        var c = w.Get<Combatant>(r.Player);
        var anim = w.Get<CharacterAnimation>(r.Player);
        long strike = spin ? -1 : release + (long)MathF.Round((c.StrikeAt - anim.ActionTime) / anim.ActionRate * World.TicksPerSecond);
        for (int t = 0; t < Ticks(1.6f); t++) Tick(Buttons.None);
        return new HoldTrace(press, release, strike, parries, attacks, ticks, w.Get<DuelBrain>(r.Foe));
    }

    static readonly DuelBrainSettings Still = new() { StrikeShare = 0.1f, CloseShare = 3f, TooCloseShare = 0f, BackOffChance = 0f };

    [Theory]
    [InlineData(0.23f)]   // about Melee 0's reaction
    [InlineData(0.11f)]   // about Melee 5's
    public void After_a_held_blow_s_release_the_brain_waits_its_own_reaction_before_its_parry(float react)
    {
        var g = new GuardSettings();
        // The parry not given up here (a long patience), so the one chosen at the press waits the hold out.
        var t = HoldScene(1f, Still with { ParryChance = 1f, BlockChance = 0f, HeldParryGiveUpSeconds = 10f }, react);
        long window = (long)MathF.Round(g.ParryWindowSeconds * World.TicksPerSecond);
        output.WriteLine($"reaction {react} s: let go at {t.Release}, strikes at {t.Strike} ({t.Strike - t.Release} ticks later); parries at [{string.Join(", ", t.Parries.Select(p => $"{p} (+{p - t.Release})"))}], each window {window} ticks");
        Assert.DoesNotContain(t.Parries, p => p - t.Release < Ticks(react) - 1);   // never before its reaction from the release
        if (react > 0.2f)
            Assert.DoesNotContain(t.Parries, p => p <= t.Strike - 2);          // a slow brain is late: no window before the blade strikes (its reaction ends at the strike mark, and a parry is given up past it)
        else Assert.InRange(t.Strike, Assert.Single(t.Parries) + 4, Assert.Single(t.Parries) + window);   // a quick one is in time, the window open at the strike
    }

    [Theory]
    [InlineData(0.23f)]
    [InlineData(0.11f)]
    public void A_block_raised_against_a_held_blow_stays_up_through_the_hold_and_its_usual_time_after_the_release(float react)
    {
        var numbers = Still with { ParryChance = 0f, BlockChance = 1f };
        var t = HoldScene(1f, numbers, react);
        bool Up(long tick) => t.Ticks.First(x => x.Tick == tick).Blocking;
        long raised = t.Ticks.First(x => x.Blocking).Tick;
        long lowered = t.Ticks.First(x => x.Tick > raised && !x.Blocking).Tick;
        output.WriteLine($"reaction {react} s: the block raised at {raised} ({raised - t.Press} ticks after the press), let go at {t.Release}, lowered at {lowered} ({lowered - t.Release} ticks after the release)");
        Assert.True(raised < t.Release);
        Assert.True(Up(t.Release - 1) && Up(t.Release + 10));                   // up through the whole hold (1.33 s from the press, past 0.9 s from the reaction)
        long blockTicks = Ticks(numbers.BlockSeconds);
        Assert.InRange(lowered - t.Release, blockTicks - 2, blockTicks + 4);    // and its usual time from the release
    }

    [Theory]
    [InlineData(0.23f)]
    [InlineData(0.11f)]
    public void A_parry_waiting_on_a_long_hold_is_given_up_and_the_brain_is_free_then_parries_again_on_the_release(float react)
    {
        // From the press on, the brain would strike whenever it is free and in reach (a second entry in its book).
        var numbers = Still with { ParryChance = 1f, BlockChance = 0f };
        var t = HoldScene(1f, numbers, react, after: numbers with { StrikeShare = 2f, CloseShare = 0.5f });
        output.WriteLine($"reaction {react} s: pressed at {t.Press}, let go at {t.Release}; parries pending to {t.Ticks.Last(x => x.ParryPending && x.Tick < t.Release).Tick}; its own blows at [{string.Join(", ", t.Attacks)}]; parries at [{string.Join(", ", t.Parries)}]; given up {t.Brain.ParriesGivenUp}, chosen again {t.Brain.ParriesOnRelease}");
        Assert.Equal(1, t.Brain.ParriesGivenUp);
        long gaveUp = t.Ticks.Last(x => x.ParryPending && x.Tick < t.Release).Tick;
        Assert.True(gaveUp < t.Release - Ticks(0.3f), "given up well before the release (0.6 s into the 1 s hold)");
        Assert.Contains(t.Attacks, a => a > gaveUp && a < t.Release);          // free: it strikes during the hold
        Assert.DoesNotContain(t.Parries, p => p < t.Release);
    }

    // A brain in a pause does not strike, but its guard and parry answer a blow held ready as ever.
    [Theory]
    [InlineData(0.23f)]
    [InlineData(0.11f)]
    public void A_brain_in_a_pause_still_blocks_a_held_blow_through_the_hold_and_its_usual_time_after_and_strikes_not(float react)
    {
        var numbers = Still with { ParryChance = 0f, BlockChance = 1f, StrikeShare = 2f, CloseShare = 0.5f };   // out of a pause it would strike at once
        var t = HoldScene(1f, numbers, react, pause: 30f);
        long raised = t.Ticks.First(x => x.Blocking).Tick;
        long lowered = t.Ticks.First(x => x.Tick > raised && !x.Blocking).Tick;
        output.WriteLine($"reaction {react} s, paused: the block raised at {raised} ({raised - t.Press} after the press), let go at {t.Release}, lowered at {lowered}; its own blows at [{string.Join(", ", t.Attacks)}]; pause cut {t.Brain.PausesCut}");
        Assert.True(raised < t.Press + Ticks(react) + 3);
        Assert.True(lowered - t.Release >= Ticks(numbers.BlockSeconds) - 2);
        Assert.DoesNotContain(t.Attacks, a => a < t.Release);
    }

    [Fact]
    public void A_brain_in_a_pause_parries_a_held_blow_on_its_release_at_its_reaction()
    {
        var t = HoldScene(1f, Still with { ParryChance = 1f, BlockChance = 0f, HeldParryGiveUpSeconds = 10f, StrikeShare = 2f, CloseShare = 0.5f }, 0.11f, pause: 30f);
        output.WriteLine($"paused: let go at {t.Release}, strikes at {t.Strike}; parries at [{string.Join(", ", t.Parries)}]; its own blows at [{string.Join(", ", t.Attacks)}]");
        long at = Assert.Single(t.Parries);
        Assert.True(at - t.Release >= Ticks(0.11f) - 1);
        Assert.InRange(t.Strike, at, at + (long)MathF.Round(new GuardSettings().ParryWindowSeconds * World.TicksPerSecond));
        Assert.DoesNotContain(t.Attacks, a => a < t.Release);
    }

    [Fact]
    public void A_parry_given_up_is_chosen_again_on_the_release_at_its_reaction()
    {
        var t = HoldScene(1f, Still with { ParryChance = 1f, BlockChance = 0f }, 0.11f);
        output.WriteLine($"let go at {t.Release}, strikes at {t.Strike}; parries at [{string.Join(", ", t.Parries)}]; given up {t.Brain.ParriesGivenUp}, chosen again {t.Brain.ParriesOnRelease}");
        Assert.Equal((1, 1), (t.Brain.ParriesGivenUp, t.Brain.ParriesOnRelease));
        long at = Assert.Single(t.Parries);
        Assert.True(at - t.Release >= Ticks(0.11f) - 1);
        Assert.InRange(t.Strike, at, at + (long)MathF.Round(new GuardSettings().ParryWindowSeconds * World.TicksPerSecond));
    }

    [Fact]
    public void A_parrying_brain_waits_out_a_held_blow_and_parries_it_in_time_after_its_release()
    {
        var g = new GuardSettings();
        var (parries, press, release, strike, brain, _) = HeldAgainstAParry(1f);
        output.WriteLine($"held 1 s: pressed at {press}, let go at {release}, strikes at {strike}; the guard's parries at [{string.Join(", ", parries)}] (window {g.ParryWindowSeconds} s); chosen {brain.ParriesChosen}, not free {brain.ParriesNotFree}");
        Assert.DoesNotContain(parries, p => p < release);                       // nothing pressed during the hold
        long at = Assert.Single(parries);
        Assert.InRange(strike, at, at + (long)MathF.Round(g.ParryWindowSeconds * World.TicksPerSecond));   // the window open at the strike
        // A tap: the same, the parry's window open at the strike.
        var (tapParries, _, tapRelease, tapStrike, _, _) = HeldAgainstAParry(0f);
        output.WriteLine($"a tap: let go at {tapRelease}, strikes at {tapStrike}; the guard's parries at [{string.Join(", ", tapParries)}]");
        long tapAt = Assert.Single(tapParries);
        Assert.InRange(tapStrike, tapAt, tapAt + (long)MathF.Round(g.ParryWindowSeconds * World.TicksPerSecond));
    }

    [Fact]
    public void A_brain_still_busy_with_its_own_swing_does_not_choose_a_parry_it_cannot_press_in_time_even_for_a_blow_pressed_as_a_tap()
    {
        // The brain's own swing (stood in for by its busy time) runs 2 s past the press: a parry pressed then would raise no window
        // (the finding). A blow is readied at its press — a tap is let go on the next tick, after the brain has judged it.
        var (parries, _, _, _, brain, _) = HeldAgainstAParry(0f, ownBusy: 2f);
        output.WriteLine($"own swing busy 2 s, a tap: parries [{string.Join(", ", parries)}], chosen {brain.ParriesChosen}, not free {brain.ParriesNotFree}");
        Assert.Equal(1, brain.ParriesNotFree);
        Assert.Empty(parries);
    }

    [Fact]
    public void Under_attack_spin_a_parrying_brain_against_a_held_press_is_as_before_directions()
    {
        // attack-spin (the service off) and a world without the service step to the same digests (a hold of 1 s: the whirl on release).
        static List<ulong> Run(DirectionTests t, bool directions)
        {
            var (_, _, _, _, _, r) = t.HeldAgainstAParry(1f, spin: true, directions: directions);
            return [r.World.Digest(), (ulong)r.World.Tick];
        }
        Assert.Equal(Run(this, false), Run(this, true));
        var (parries, _, release, _, brain, r2) = HeldAgainstAParry(1f, spin: true);
        output.WriteLine($"attack-spin, held 1 s: the whirl begun at release {release}; parries [{string.Join(", ", parries)}], chosen {brain.ParriesChosen}");
        Assert.DoesNotContain(parries, p => p < release);                        // the blow begins at the release, the brain answers it then
        Assert.Single(parries);
    }

    // ---- attack-spin: an earlier build exactly ----

    [Fact]
    public void Attack_spin_is_1_1_s_tap_and_hold_quick_blows_in_turn_and_the_whirl_on_release()
    {
        var r = Make(spin: true);
        r.Step(Held(Buttons.Attack));
        Assert.Empty(r.Begun);                                         // made as it is let go
        r.Idle(1);
        Assert.Equal(("chrisAttackSword", AttackDirection.None, AttackStrength.Quick), (r.ClipName(r.Begun[0].Clip), r.Begun[0].Direction, r.Begun[0].Strength));
        r.Idle(Ticks(1.2f));
        r.Step(Held(Buttons.Attack)); r.Idle(1);
        Assert.Equal("chrisAttack2Sword", r.ClipName(r.Begun[1].Clip));
        r.Idle(Ticks(1.2f));
        r.Step(Held(Buttons.Attack), Ticks(0.4f)); r.Idle(1);
        Assert.Equal(("ChrisAttackSSword", AttackStrength.Strong), (r.ClipName(r.Begun[2].Clip), r.Begun[2].Strength));
        Assert.Empty(r.Released);
        Assert.False(r.World.Get<Combatant>(r.Player).Readied);
    }

    [Fact]
    public void Attack_spin_and_a_world_without_directions_step_to_the_same_digests()
    {
        static List<ulong> Run(bool directions)
        {
            var r = Make(spin: true, directions: directions);
            var digests = new List<ulong>();
            for (int t = 0; t < 600; t++)
            {
                var held = t % 200 < (t < 200 ? 2 : 45) ? Buttons.Attack : t % 200 is >= 120 and < 150 ? Buttons.Block : Buttons.None;
                r.Step(Held(held));
                digests.Add(r.World.Digest());
            }
            return digests;
        }
        Assert.Equal(Run(false), Run(true));
    }

    // ---- the blows' sides, measured ----

    [Fact]
    public void The_forehand_sweeps_from_his_right_and_the_backhand_from_his_left()
    {
        foreach (var (side, clip) in new[] { (AttackDirection.Right, "chrisAttackSword"), (AttackDirection.Left, "chrisAttack2Sword") })
        {
            var r = Make(foe: null);
            var kind = r.World.Get<CharacterAnimation>(r.Player).Kind;
            var dirs = r.World.Service<DirectionalAttacks>();
            var m = dirs.Motion(r.World, r.Assets, kind, side);
            Assert.Equal(clip, r.ClipName(m.Clip));
            var window = r.World.Service<ActiveWindows>().For(r.Assets, kind, m.Clip, m.StrikeSeconds, r.World.Service<ContactConfig>().Window);
            r.World.Get<CombatIntent>(r.Player).Direction = side;   // as a brain would
            r.Step(Held(Buttons.Attack)); r.Idle(1);
            float first = float.NaN, last = float.NaN;
            for (int t = 0; t < Ticks(1.1f); t++)
            {
                r.Idle(1);
                var a = r.World.Get<CharacterAnimation>(r.Player);
                if (a.ActionTime < window.From || a.ActionTime > window.To || !HeldItems.Blade(r.World, r.Player, out var blade)) continue;
                var body = r.World.Get<Body>(r.Player);
                var (sy, cy) = DetMath.SinCos(body.Yaw);
                float x = (blade.Tip.X - body.Feet.X) * sy - (blade.Tip.Y - body.Feet.Y) * cy;   // across his front, his right (sin, −cos) positive
                float fwd = (blade.Tip.X - body.Feet.X) * cy + (blade.Tip.Y - body.Feet.Y) * sy;
                if (fwd < 0.3f) continue;   // the window's part in front of him (the forehand's begins behind his head)
                if (float.IsNaN(first)) first = x;
                last = x;
            }
            output.WriteLine($"{clip} ({side}): in front of him the tip crosses from {first:+0.00;-0.00} to {last:+0.00;-0.00} m (his right +)");
            if (side == AttackDirection.Right) Assert.True(first > 0.5f && last < -0.5f, "the forehand comes from his right across to his left");
            else Assert.True(first < 0f && last > 0.5f, "the backhand comes from his left across to his right");
        }
    }

    // ---- the duelling brain ----

    [Fact]
    public void The_duelling_brain_s_thrust_is_aimed_at_the_centre_of_the_foe_s_chest_whatever_its_draw()
    {
        // Christof's brain thrusting only (the stitched thrust), the guard standing 1.5 m away; every
        // thrust pitched at 0.72 of the guard's top from Christof's eyes (SwingAim.ThrustPitchAt), not at the draw's high, level or low.
        var assets = new CharacterAssets();
        int christof = OneOneLibrary.Register(assets, Character("Christof", "BroadSword"));
        int guard = OneOneLibrary.Register(assets, Character("rudolfGuard"));
        var b = new WorldBuilder(9);
        MovementServices.Register(b, new TestLevel().Floor(-60, -60, 60, 60).Build());
        MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = false });
        b.Animation(assets);
        b.HitVolumes();
        BodyMotionSystem.Register(b);
        CombatServices.Register(b);
        DuelBrains.Register(b);
        b.Service(new DirectionalAttacks { Rows = [.. DirectionalAttacks.DefaultRows, new DirectionRow(null, 1, null, null, "stitched:overhead", "stitched:thrust")] });
        var aim = new SwingAim();
        b.Service(aim);
        var w = b.Build();
        w.Service<HitVolumes>().Prepare(assets);
        var ea = MovementServices.SpawnBody(w, new(0f, 0f, 0.02f), DetMath.HalfPi, Locomotion.Sandbox);
        var eb = MovementServices.SpawnBody(w, new(0f, 1.5f, 0.02f), -DetMath.HalfPi, Locomotion.Sandbox);
        foreach (var (e, k) in new[] { (ea, christof), (eb, guard) })
        {
            w.Set(e, CharacterAnimation.For(assets, k));
            w.Set(e, MotionInput.Standing);
            CombatServices.Arm(w, e);
        }
        var book = w.Service<DuelBrainBook>();
        float ra = DuelBrains.ReachOf(assets, christof), rb = DuelBrains.ReachOf(assets, guard);
        w.Set(ea, DuelBrain.For(BrainKind.Duel, eb, book.Add(new DuelBrainSettings
            { BackOffChance = 0f, BlockChance = 0f, LeftShare = 0f, RightShare = 0f, OverheadShare = 0f, ThrustShare = 1f, AwayFromGuardShare = 0f }), ra, rb));
        w.Set(eb, DuelBrain.For(BrainKind.Stand, ea, book.Add(new DuelBrainSettings()), rb, ra));
        var pitches = new List<(float Got, float Want, float Draws)>();
        for (int t = 0; t < 8 * World.TicksPerSecond; t++)
        {
            MovementServices.Step(w, ActionFrame.Idle);
            foreach (var x in w.Events<AttackBegun>().ThisTick)
            {
                Assert.Equal(AttackDirection.Thrust, x.Direction);
                var a = w.Get<Body>(ea);
                var f = w.Get<Body>(eb);
                pitches.Add((x.AimPitch, aim.ThrustPitchAt(w, ea, in a, eb, in f), aim.PitchAt(w, in a, eb, in f, 1) - aim.PitchAt(w, in a, eb, in f, -1)));
            }
        }
        output.WriteLine($"thrusts: {string.Join(", ", pitches.Select(p => $"{p.Got * DetMath.RadToDeg:0.0}° (chest {p.Want * DetMath.RadToDeg:0.0}°)"))}");
        Assert.True(pitches.Count >= 4);
        Assert.All(pitches, p => Assert.Equal(p.Want, p.Got, 0.02f));            // within about a degree (the bodies a tick on)
        Assert.All(pitches, p => Assert.True(p.Draws > 0.1f));                    // where the draw's high and low would differ by more than 6°
        // The aim itself: a foe of 1.8 m at 1.4 m, from a man of 1.8 m: atan2(0.72 × 1.8 − 0.92 × 1.8, 1.4), about −14°.
        Assert.Equal(DetMath.Atan2(0.72f * 1.8f - 0.92f * 1.8f, 1.4f), DetMath.Atan2(1.296f - 1.656f, 1.4f), 5);
    }

    [Fact]
    public void The_duelling_brain_strikes_from_each_side_it_has_and_away_from_a_raised_guard()
    {
        // Christof's brain against rudolfGuard standing still (no guard), then holding his shield up (his left arm: Christof's right as
        // they face each other): by the weights, both sides; with every blow away from the guard, all from the left.
        foreach (var (dummy, away) in new[] { (BrainKind.Stand, 0f), (BrainKind.Block, 1f) })
        {
            var assets = new CharacterAssets();
            int christof = OneOneLibrary.Register(assets, Character("Christof", "BroadSword"));
            int guard = OneOneLibrary.Register(assets, Character("rudolfGuard"));
            var b = new WorldBuilder(5);
            MovementServices.Register(b, new TestLevel().Floor(-60, -60, 60, 60).Build());
            MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = false });
            b.Animation(assets);
            b.HitVolumes();
            BodyMotionSystem.Register(b);
            CombatServices.Register(b);
            DuelBrains.Register(b);
            b.Service(new DirectionalAttacks());
            var w = b.Build();
            w.Service<HitVolumes>().Prepare(assets);
            var ea = MovementServices.SpawnBody(w, new(0f, 0f, 0.02f), DetMath.HalfPi, Locomotion.Sandbox);
            var eb = MovementServices.SpawnBody(w, new(0f, 1.5f, 0.02f), -DetMath.HalfPi, Locomotion.Sandbox);
            foreach (var (e, k) in new[] { (ea, christof), (eb, guard) })
            {
                w.Set(e, CharacterAnimation.For(assets, k));
                w.Set(e, MotionInput.Standing);
                CombatServices.Arm(w, e);
            }
            var book = w.Service<DuelBrainBook>();
            float ra = DuelBrains.ReachOf(assets, christof), rb = DuelBrains.ReachOf(assets, guard);
            w.Set(ea, DuelBrain.For(BrainKind.Duel, eb, book.Add(new DuelBrainSettings { BackOffChance = 0f, BlockChance = 0f, AwayFromGuardShare = away }), ra, rb));
            w.Set(eb, DuelBrain.For(dummy, ea, book.Add(new DuelBrainSettings()), rb, ra));
            var picked = new int[5];
            for (int t = 0; t < Ticks(30f); t++)
            {
                MovementServices.Step(w, ActionFrame.Idle);
                foreach (var x in w.Events<AttackBegun>().ThisTick) if (x.Entity == ea) picked[(int)x.Direction]++;
            }
            output.WriteLine($"against a dummy that {(dummy == BrainKind.Block ? "blocks" : "stands")}: left {picked[1]}, right {picked[2]}, overhead {picked[3]}, thrust {picked[4]}, none {picked[0]}; away from the guard {w.Get<DuelBrain>(ea).AwayFromGuard}");
            Assert.Equal(0, picked[0]);
            Assert.True(picked[1] + picked[2] >= 15, "it fought");
            if (dummy == BrainKind.Stand) Assert.True(picked[1] >= 4 && picked[2] >= 4, "both sides by the weights");
            else Assert.Equal(picked[1] + picked[2], picked[1]);
        }
    }
}

/// <summary>Directional attacks allocate nothing per tick once going (measured as the platform's are: <see cref="Allocations"/>).</summary>
[Collection(AllocationCollection.Name)]
public sealed class DirectionAllocationTests
{
    [Fact]
    public void Directional_attacks_allocate_nothing_per_tick_once_going()
    {
        foreach (var mode in new[] { AttackDirectionMode.Automatic, AttackDirectionMode.Mouse, AttackDirectionMode.Keys })
        {
            var r = DirectionTests.Make(mode);
            void Round(int t)
            {
                var held = t % 160 < 50 ? Buttons.Attack : Buttons.None;
                var move = mode == AttackDirectionMode.Keys ? new Vector2(t % 320 < 160 ? -1f : 1f, 0f) * (t % 160 < 5 ? 1f : 0f) : Vector2.Zero;
                var look = new Vector2(((t / 160) % 2 == 0 ? 0.01f : -0.01f) * (t % 160 > 150 ? 1f : 0f), 0f);
                Arts.NextBlow(r.World, r.Player);
                MovementServices.Step(r.World, new ActionFrame(move, look, held, default));
            }
            for (int t = 0; t < 640; t++) Round(t);
            Allocations.Warm(r.World);
            Allocations.None($"640 ticks of directional attacks ({mode})", () =>
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int t = 640; t < 1280; t++) Round(t);
                return GC.GetAllocatedBytesForCurrentThread() - before;
            });
        }
    }

    static class Arts
    {
        public static void NextBlow(World w, EntityId e) => Attacks.NextBlow(w, e, out _);
    }
}
