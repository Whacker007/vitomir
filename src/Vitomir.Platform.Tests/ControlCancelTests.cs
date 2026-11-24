// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Powers;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The input side (Control/PlayerControl.cs, Bodies/Dodge.cs, Bodies/DirectionPace.cs), on a
/// floor built in memory, with a stand-in for the swing's side (<see cref="SwingCancelStandIn"/>; the guard's
/// <see cref="SwingCancel"/> once answered false): the cancel key with a swing to cancel cancels it and does
/// nothing else (crouch's press swallowed till let go), and without one crouches; Dodge mid-swing asks, and dodges when allowed
/// or is refused as before; double tap to dodge, off by default, dodges each way within its window and not outside it, and a
/// tape of it replays; free look turns the camera and not the body and eases back; a body facing one way moves at its
/// shares of its pace the other ways.
/// </summary>
public sealed class ControlCancelTests
{
    const int T = World.TicksPerSecond;

    sealed record Rig(World W, EntityId P, List<(EntityId E, SwingCancelCause Cause)> Asked, List<(EntityId E, SwingCancelCause Cause)> Done);

    static LevelSpace Floor() =>
        new("floor", new TestLevel().Floor(-20, -20, 20, 20, cell: 4f).Build("floor").Collision) { Places = LevelPlaces.None };

    /// <summary>A controlled human on a floor; <paramref name="allow"/> answers the stand-in's Can (null: no stand-in, SwingCancel's own).</summary>
    static Rig Make(Func<SwingCancelCause, bool>? allow = null, float doubleTap = 0f, DirectionPace? pace = null)
    {
        var asked = new List<(EntityId, SwingCancelCause)>();
        var done = new List<(EntityId, SwingCancelCause)>();
        var b = new WorldBuilder(3);
        MovementServices.Register(b, Floor());
        b.Component<Combatant>().Component<CharacterAnimation>().Component<Dead>();
        b.Event<BlockLowered>();
        PowerContract.Register(b);
        if (pace is not null) b.Service(pace);
        if (allow is not null)
            b.Service(new SwingCancelStandIn((w, e, c) => { asked.Add((e, c)); return allow(c) && Swings(w, e); }, (w, e, c) =>
            {
                if (!allow(c) || !Swings(w, e)) return false;
                done.Add((e, c));
                // As the swing's side would: the blow ends (here, the action and the busy time).
                w.Get<CharacterAnimation>(e).Action = ActionRole.None;
                return true;
            }));
        var w = b.Build();
        w.Service<TickInput>().DoubleTapDodge = doubleTap;
        var p = MovementServices.SpawnControlled(w, new Vector3(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        for (int i = 0; i < 12; i++) MovementServices.Step(w, ActionFrame.Idle);
        return new Rig(w, p, asked, done);
    }

    /// <summary>Whether a blow plays (the stand-in's "a swing to cancel").</summary>
    static bool Swings(World w, EntityId e) => w.Store<CharacterAnimation>().Has(e) && ActionRoles.IsAttack(w.Get<CharacterAnimation>(e).Action);

    static void Swinging(Rig r)
    {
        r.W.Set(r.P, Combatant.Ready with { Busy = 0.8f });
        r.W.Set(r.P, new CharacterAnimation { Action = ActionRole.AttackQuick });
    }

    static ActionFrame Keys(Buttons held, Vector2 move = default) => new(move, default, held, default);

    [Fact]
    public void The_cancel_key_with_a_swing_the_rules_let_be_cancelled_cancels_it_and_does_not_crouch()
    {
        var r = Make(allow: c => c == SwingCancelCause.Cancel);
        Swinging(r);
        var both = Buttons.Cancel | Buttons.Crouch;     // one key, crouch's and cancel's (the default C)
        MovementServices.Step(r.W, Keys(both));
        Assert.Equal([(r.P, SwingCancelCause.Cancel)], r.Done);
        Assert.False(r.W.Get<MoveIntent>(r.P).Crouch);
        Assert.False(r.W.Get<MoveIntent>(r.P).Cancel);          // the body's own cancel (a move, a hang) is not asked for
        // Held on, still no crouch (swallowed till let go); let go and pressed again with nothing to cancel, it crouches.
        for (int i = 0; i < 10; i++) MovementServices.Step(r.W, Keys(both));
        Assert.False(r.W.Get<MoveIntent>(r.P).Crouch);
        Assert.Single(r.Done);
        MovementServices.Step(r.W, ActionFrame.Idle);
        MovementServices.Step(r.W, Keys(both));
        Assert.True(r.W.Get<MoveIntent>(r.P).Crouch);
        Assert.Single(r.Done);
    }

    [Fact]
    public void The_cancel_key_with_nothing_to_cancel_crouches_as_ever()
    {
        // No stand-in: SwingCancel's own answer — a swing playing is not something to cancel.
        var r = Make();
        Swinging(r);
        MovementServices.Step(r.W, Keys(Buttons.Cancel | Buttons.Crouch));
        Assert.True(r.W.Get<MoveIntent>(r.P).Crouch);
        // A stand-in that refuses (after the strike: the guard's rule): crouches too, and nothing was cancelled.
        var after = Make(allow: _ => false);
        Swinging(after);
        MovementServices.Step(after.W, Keys(Buttons.Cancel | Buttons.Crouch));
        Assert.True(after.W.Get<MoveIntent>(after.P).Crouch);
        Assert.Equal([(after.P, SwingCancelCause.Cancel)], after.Asked);
        Assert.Empty(after.Done);
    }

    [Fact]
    public void Dodge_during_a_swing_asks_and_dodges_the_move_keys_way_when_allowed_else_is_refused_as_before()
    {
        var r = Make(allow: c => c == SwingCancelCause.Dodge);
        Swinging(r);
        var begun = new List<DodgeBegun>();
        MovementServices.Step(r.W, Keys(Buttons.Dodge, new Vector2(-1, 0)));       // left held
        begun.AddRange(r.W.Events<DodgeBegun>().ThisTick);
        Assert.Equal([(r.P, SwingCancelCause.Dodge)], r.Done);
        var b = Assert.Single(begun);
        Assert.True(b.Way.Y > 0.99f, $"{b.Way}");      // facing +X, left is +Y
        // The fighter stays busy for the later of the swing's and the dodge's ends: no blow starts in the dodge.
        Assert.True(r.W.Get<Combatant>(r.P).Busy >= r.W.Get<Dodging>(r.P).Total - 1e-4f);

        // The swing's side refusing (a stand-in: the real SwingCancel allows a dodge at any point of a
        // swing; GuardTests.Lead_a_dodge_asked_mid_swing_cancels_the_swing_and_the_body_dodges is the whole path).
        foreach (var refuse in new Func<SwingCancelCause, bool>?[] { _ => false })
        {
            var no = Make(allow: refuse);
            Swinging(no);
            MovementServices.Step(no.W, Keys(Buttons.Dodge, new Vector2(-1, 0)));
            Assert.Equal(DodgeRefusal.MidSwing, Assert.Single(no.W.Events<DodgeRefused>().ThisTick).Why);
            Assert.Empty(no.W.Events<DodgeBegun>().ThisTick);
            Assert.Empty(no.Done);
        }
        // In the air mid-swing: refused as mid-swing, and the swing not asked to cancel (nothing else would let it dodge).
        var air = Make(allow: _ => true);
        Swinging(air);
        air.W.Get<Body>(air.P).State = BodyState.Air;
        air.W.Get<Body>(air.P).Feet += new Vector3(0, 0, 2f);
        MovementServices.Step(air.W, Keys(Buttons.Dodge));
        Assert.Equal(DodgeRefusal.MidSwing, Assert.Single(air.W.Events<DodgeRefused>().ThisTick).Why);
        Assert.Empty(air.Done);
    }

    /// <summary>Steps the move keys given tick by tick (x right, y forward), gathering the dodges begun.</summary>
    static List<DodgeBegun> Taps(Rig r, IEnumerable<Vector2> keys)
    {
        var begun = new List<DodgeBegun>();
        foreach (var k in keys)
        {
            MovementServices.Step(r.W, Keys(Buttons.None, k));
            begun.AddRange(r.W.Events<DodgeBegun>().ThisTick);
        }
        return begun;
    }

    /// <summary>A key down for <paramref name="down"/> ticks, up for <paramref name="gap"/>, down again for <paramref name="again"/>, then up a second.</summary>
    static IEnumerable<Vector2> DoubleTapOf(Vector2 key, int down, int gap, int again = 6)
    {
        for (int i = 0; i < down; i++) yield return key;
        for (int i = 0; i < gap; i++) yield return Vector2.Zero;
        for (int i = 0; i < again; i++) yield return key;
        for (int i = 0; i < T; i++) yield return Vector2.Zero;
    }

    [Fact]
    public void Double_tap_is_off_by_default_and_on_dodges_each_way_within_its_window_and_not_outside_it()
    {
        Assert.Empty(Taps(Make(), DoubleTapOf(new Vector2(-1, 0), 6, 6)));          // off (the default): nothing
        Assert.Equal(0f, new TickInput().DoubleTapDodge);
        foreach (var (key, way) in new[]
                 {
                     (new Vector2(0, 1), new Vector2(1, 0)), (new Vector2(0, -1), new Vector2(-1, 0)),
                     (new Vector2(-1, 0), new Vector2(0, 1)), (new Vector2(1, 0), new Vector2(0, -1)),
                 })
        {
            var r = Make(doubleTap: 0.25f);
            var begun = Taps(r, DoubleTapOf(key, 6, 6));                              // second press 12 ticks (0.1 s) after the first
            var b = Assert.Single(begun);
            Assert.True(Vector2.Distance(way, b.Way) < 1e-3f, $"{key}: {b.Way}");
        }
        // Outside the window (the second press 0.3 s after the first): no dodge; and a key held long and pressed again: none.
        Assert.Empty(Taps(Make(doubleTap: 0.25f), DoubleTapOf(new Vector2(-1, 0), 6, 30)));
        Assert.Empty(Taps(Make(doubleTap: 0.25f), DoubleTapOf(new Vector2(-1, 0), 40, 4)));
        // Two different keys: no dodge.
        var two = Make(doubleTap: 0.25f);
        Assert.Empty(Taps(two, [.. Enumerable.Repeat(new Vector2(-1, 0), 5), .. Enumerable.Repeat(Vector2.Zero, 5), .. Enumerable.Repeat(new Vector2(1, 0), 5)]));
        // A third tap right after a double tap does not dodge again (its age was used).
        var three = Make(doubleTap: 0.25f);
        Assert.Single(Taps(three, [.. Enumerable.Repeat(new Vector2(-1, 0), 4), .. Enumerable.Repeat(Vector2.Zero, 4), .. Enumerable.Repeat(new Vector2(-1, 0), 4),
                                   .. Enumerable.Repeat(Vector2.Zero, 4), .. Enumerable.Repeat(new Vector2(-1, 0), 4), .. Enumerable.Repeat(Vector2.Zero, T)]));
    }

    [Fact]
    public void A_tape_of_a_double_tap_replays_digest_for_digest()
    {
        var frames = DoubleTapOf(new Vector2(-1, 0), 6, 6).Select(k => Keys(Buttons.None, k)).ToList();
        var a = Make(doubleTap: 0.25f);
        var b = Make(doubleTap: 0.25f);
        foreach (var f in frames)
        {
            MovementServices.Step(a.W, f);
            MovementServices.Step(b.W, f);
            Assert.Equal(a.W.Digest(), b.W.Digest());
        }
        Assert.Equal(1, a.W.Get<Dodging>(a.P).Count);
        // Nothing allocated per tick by the taps' keys (after the dodge's components were put on at the first).
        var warm = DoubleTapOf(new Vector2(1, 0), 6, 6).Select(k => Keys(Buttons.None, k)).ToList();
        foreach (var f in warm) MovementServices.Step(a.W, f);
        long before = GC.GetAllocatedBytesForCurrentThread();
        foreach (var f in warm) MovementServices.Step(a.W, f);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 1024, $"{allocated} bytes in {warm.Count} ticks");
        // The setting is the tape head's: written as its window, read back; an older tape reads as off.
        Assert.Equal(0.25f, TickInput.DoubleTapFrom(new Dictionary<string, string> { [TickInput.DoubleTapNote] = "0.25" }));
        Assert.Equal(0f, TickInput.DoubleTapFrom(new Dictionary<string, string>()));
    }

    [Fact]
    public void Free_look_turns_the_camera_round_the_body_and_not_the_body_and_eases_back_when_let_go()
    {
        var r = Make();
        r.W.Get<CameraRig>(r.P).View = ViewKind.Third;
        float yaw = r.W.Get<Body>(r.P).Yaw;
        // Held, the look turns the orbit; the body's facing and the move keys' way stay.
        for (int i = 0; i < 30; i++) MovementServices.Step(r.W, new ActionFrame(new Vector2(0, 1), new Vector2(0.05f, 0f), Buttons.FreeLook, default));
        Assert.Equal(1.5f, r.W.Get<CameraRig>(r.P).Orbit, 1e-4f);
        Assert.Equal(yaw, r.W.Get<CameraRig>(r.P).Yaw, 1e-6f);
        Assert.Equal(yaw, r.W.Get<Body>(r.P).Yaw, 1e-5f);
        var dir = r.W.Get<MoveIntent>(r.P).Direction;
        Assert.True(dir.X > 0.99f, $"forward is still the body's way: {dir}");
        // Let go: the orbit eases back to 0 in a quarter second, evenly.
        int ticks = 0;
        while (r.W.Get<CameraRig>(r.P).Orbit != 0f && ticks < 200) { MovementServices.Step(r.W, ActionFrame.Idle); ticks++; }
        Assert.InRange(ticks, 29, 31);
        // Toggled: on until its toggle is pressed again; in first person it does nothing.
        MovementServices.Step(r.W, Keys(Buttons.FreeLookToggle));
        MovementServices.Step(r.W, new ActionFrame(default, new Vector2(0.2f, 0f), Buttons.None, default));
        Assert.Equal(0.2f, r.W.Get<CameraRig>(r.P).Orbit, 1e-5f);
        Assert.Equal(yaw, r.W.Get<CameraRig>(r.P).Yaw, 1e-6f);
        MovementServices.Step(r.W, Keys(Buttons.FreeLookToggle));
        var first = Make();
        first.W.Get<CameraRig>(first.P).View = ViewKind.First;
        MovementServices.Step(first.W, new ActionFrame(default, new Vector2(0.2f, 0f), Buttons.FreeLook, default));
        Assert.Equal(0f, first.W.Get<CameraRig>(first.P).Orbit);
        Assert.Equal(DetMath.WrapAngle(yaw + 0.2f), first.W.Get<CameraRig>(first.P).Yaw, 1e-5f);
    }

    /// <summary>The level speed a body reaches holding <paramref name="keys"/> with its facing on the view (m/s).</summary>
    static float SpeedHolding(Vector2 keys, DirectionPace? pace, Buttons pace2 = Buttons.None)
    {
        var r = Make(pace: pace);
        r.W.Service<TickInput>().ThirdPersonFacing = ThirdPersonFacing.View;
        for (int i = 0; i < T; i++) MovementServices.Step(r.W, Keys(pace2, keys));
        var v = r.W.Get<Body>(r.P).Velocity;
        return new Vector2(v.X, v.Y).Length();
    }

    [Fact]
    public void A_body_facing_the_view_backpedals_at_seven_tenths_and_sidesteps_at_nine_tenths_of_its_pace()
    {
        var dp = new DirectionPace();
        float run = Locomotion.Sandbox.RunSpeed;
        Assert.Equal(run, SpeedHolding(new Vector2(0, 1), dp), 0.01f);
        Assert.Equal(0.9f * run, SpeedHolding(new Vector2(1, 0), dp), 0.01f);
        Assert.Equal(0.7f * run, SpeedHolding(new Vector2(0, -1), dp), 0.01f);
        Assert.Equal(0.7f * Locomotion.Sandbox.WalkSpeed, SpeedHolding(new Vector2(0, -1), dp, Buttons.Walk), 0.01f);
        // A diagonal between its neighbours (cos² and sin² blend): ahead 0.95, behind 0.80.
        var d = Vector2.Normalize(new Vector2(1, 1));
        Assert.Equal(0.95f * run, SpeedHolding(d, dp), 0.01f);
        Assert.Equal(0.80f * run, SpeedHolding(new Vector2(d.X, -d.Y), dp), 0.01f);
        // Off, or a world without it: the pace every way.
        Assert.Equal(run, SpeedHolding(new Vector2(0, -1), dp with { On = false }), 0.01f);
        Assert.Equal(run, SpeedHolding(new Vector2(0, -1), null), 0.01f);
        Assert.Equal(1f, dp.Factor(Vector2.Zero, 0f));
    }
}
