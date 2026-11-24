// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// Hold and toggle (each two-state action has a held action and a toggled one, either of which
/// may be unbound): each toggle latches and unlatches crouch, the other pace and the freed pointer; held and toggled
/// together the state is on while either is; what lets each latch go (a jump or a sprint the crouch, a sprint the walk,
/// the held key let go the pointer, the hands taking a lip the crouch — a press there letting go without crouching);
/// cancel sharing a key with crouch toggle (it only cancels when there is something to cancel); a recorded drive
/// with the toggles replaying digest-equal; and a settings file's <c>pointerKey: toggle</c> moved to the toggled action.
/// </summary>
public sealed class HoldAndToggleTests
{
    static World Flat(out EntityId player, bool alwaysRun = true)
    {
        var w = MovementServices.Register(new WorldBuilder(1), new TestLevel().Floor(-20, -20, 200, 20).Build()).Build();
        w.Service<TickInput>().AlwaysRun = alwaysRun;
        player = MovementServices.SpawnControlled(w, new(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        for (int i = 0; i < 10; i++) MovementServices.Step(w, ActionFrame.Idle);
        return w;
    }

    static ActionFrame F(Buttons held = Buttons.None, bool move = false, Vector2 look = default) =>
        new(move ? new Vector2(0, 1) : Vector2.Zero, look, held, new Vector2(0.5f, 0.5f));

    static void Run(World w, int ticks, ActionFrame f) { for (int i = 0; i < ticks; i++) MovementServices.Step(w, f); }

    /// <summary>A press: down three ticks (with <paramref name="also"/> held), then up for two.</summary>
    static void Press(World w, Buttons b, bool move = false, Buttons also = Buttons.None)
    {
        Run(w, 3, F(b | also, move));
        Run(w, 2, F(also, move));
    }

    static Buttons Latched(World w, EntityId p) => w.Get<Controlled>(p).Latched;

    // ---------------------------------------------------------------- each toggle latches and unlatches

    [Fact]
    public void Crouch_toggle_latches_the_crouch_and_a_second_press_lets_it_go()
    {
        var w = Flat(out var p);
        Press(w, Buttons.CrouchToggle);
        Run(w, 60, F());
        Assert.Equal(Buttons.Crouch, Latched(w, p));
        Assert.True(w.Get<MoveIntent>(p).Crouch);
        Assert.Equal(1, w.Get<Body>(p).Crouched);
        Press(w, Buttons.CrouchToggle);
        Run(w, 30, F());
        Assert.Equal(Buttons.None, Latched(w, p));
        Assert.False(w.Get<MoveIntent>(p).Crouch);
        Assert.Equal(0, w.Get<Body>(p).Crouched);
    }

    [Theory]
    [InlineData(true, Gait.Run, Gait.Walk)]
    [InlineData(false, Gait.Walk, Gait.Run)]
    public void Walk_toggle_latches_the_other_pace_whichever_always_run_says(bool alwaysRun, Gait plain, Gait other)
    {
        var w = Flat(out var p, alwaysRun);
        Run(w, 30, F(move: true));
        Assert.Equal(plain, w.Get<MoveIntent>(p).Gait);
        Press(w, Buttons.WalkToggle, move: true);
        Run(w, 60, F(move: true));
        Assert.Equal(other, w.Get<MoveIntent>(p).Gait);
        Assert.Equal(Buttons.Walk, Latched(w, p));
        Press(w, Buttons.WalkToggle, move: true);
        Run(w, 10, F(move: true));
        Assert.Equal(plain, w.Get<MoveIntent>(p).Gait);
        Assert.Equal(Buttons.None, Latched(w, p));
    }

    [Fact]
    public void Free_pointer_toggle_frees_the_pointer_and_the_view_stops_turning_until_it_is_pressed_again()
    {
        var w = Flat(out var p);
        Press(w, Buttons.FreePointerToggle);
        Assert.True(w.Get<CameraRig>(p).PointerFree);
        float yaw = w.Get<CameraRig>(p).Yaw;
        Run(w, 20, F(look: new Vector2(0.01f, 0f)));
        Assert.True(w.Get<CameraRig>(p).PointerFree);
        Assert.Equal(yaw, w.Get<CameraRig>(p).Yaw);
        Press(w, Buttons.FreePointerToggle);
        Assert.False(w.Get<CameraRig>(p).PointerFree);
        Run(w, 20, F(look: new Vector2(0.01f, 0f)));
        Assert.NotEqual(yaw, w.Get<CameraRig>(p).Yaw);
    }

    // ---------------------------------------------------------------- held and toggled together

    [Fact]
    public void Held_and_toggled_together_the_state_is_on_while_either_is()
    {
        var w = Flat(out var p);
        // Crouch held, then toggled on while held: letting the held key go leaves it crouched.
        Run(w, 10, F(Buttons.Crouch));
        Run(w, 3, F(Buttons.Crouch | Buttons.CrouchToggle));
        Run(w, 10, F(Buttons.Crouch));
        Run(w, 30, F());
        Assert.True(w.Get<MoveIntent>(p).Crouch);
        // Toggled off while the held key is down: crouched while held, standing when let go.
        Run(w, 10, F(Buttons.Crouch));
        Run(w, 3, F(Buttons.Crouch | Buttons.CrouchToggle));
        Run(w, 10, F(Buttons.Crouch));
        Assert.True(w.Get<MoveIntent>(p).Crouch);
        Assert.Equal(Buttons.None, Latched(w, p));
        Run(w, 30, F());
        Assert.False(w.Get<MoveIntent>(p).Crouch);
        Assert.Equal(0, w.Get<Body>(p).Crouched);

        // The other pace the same: latched, walk held and let go is still a walk; unlatched, it is a run when let go.
        Press(w, Buttons.WalkToggle, move: true);
        Run(w, 10, F(Buttons.Walk, move: true));
        Run(w, 10, F(move: true));
        Assert.Equal(Gait.Walk, w.Get<MoveIntent>(p).Gait);
        Press(w, Buttons.WalkToggle, move: true, also: Buttons.Walk);
        Assert.Equal(Gait.Walk, w.Get<MoveIntent>(p).Gait);
        Run(w, 5, F(move: true));
        Assert.Equal(Gait.Run, w.Get<MoveIntent>(p).Gait);
    }

    // ---------------------------------------------------------------- what lets a latch go

    [Fact]
    public void A_jump_or_a_sprint_lets_the_crouch_latch_go_and_a_sprint_the_walk_latch()
    {
        var w = Flat(out var p);
        Press(w, Buttons.CrouchToggle);
        Press(w, Buttons.Jump);
        Assert.Equal(Buttons.None, Latched(w, p));
        Run(w, 120, F());
        Assert.Equal(0, w.Get<Body>(p).Crouched);

        Press(w, Buttons.CrouchToggle, move: true);
        Run(w, 10, F(Buttons.Sprint, move: true));
        Assert.Equal(Buttons.None, Latched(w, p));
        Assert.Equal(Gait.Sprint, w.Get<MoveIntent>(p).Gait);
        Assert.False(w.Get<MoveIntent>(p).Crouch);
        Run(w, 10, F(move: true));

        // The walk latch: a tap of sprint lets it go (the kept sprint, then the run when it stops; never back to the walk).
        Press(w, Buttons.WalkToggle, move: true);
        Assert.Equal(Gait.Walk, w.Get<MoveIntent>(p).Gait);
        Press(w, Buttons.Sprint, move: true);
        Assert.Equal(Buttons.None, Latched(w, p));
        Assert.Equal(Gait.Sprint, w.Get<MoveIntent>(p).Gait);
        Run(w, 10, F());
        Run(w, 10, F(move: true));
        Assert.Equal(Gait.Run, w.Get<MoveIntent>(p).Gait);
    }

    [Fact]
    public void The_walk_latch_turned_on_ends_a_kept_sprint_as_a_press_of_walk_does()
    {
        var w = Flat(out var p);
        Press(w, Buttons.Sprint, move: true);           // a tap: the sprint is kept
        Run(w, 30, F(move: true));
        Assert.Equal(Gait.Sprint, w.Get<MoveIntent>(p).Gait);
        var kept = new List<SprintKept>();
        for (int i = 0; i < 5; i++)
        {
            MovementServices.Step(w, F(i < 3 ? Buttons.WalkToggle : Buttons.None, move: true));
            kept.AddRange(w.Events<SprintKept>().ThisTick);
        }
        Assert.Equal(SprintLetGo.Walk, Assert.Single(kept).Why);
        Assert.Equal(Gait.Walk, w.Get<MoveIntent>(p).Gait);
    }

    [Fact]
    public void Letting_the_held_pointer_key_go_lets_the_pointer_latch_go()
    {
        var w = Flat(out var p);
        Press(w, Buttons.FreePointerToggle);
        Run(w, 10, F(Buttons.FreePointer));
        Assert.True(w.Get<CameraRig>(p).PointerFree);
        Run(w, 1, F());
        Assert.False(w.Get<CameraRig>(p).PointerFree);
        Assert.Equal(Buttons.None, Latched(w, p));
        // A crouch or walk latch is not let go by its held key (only the pointer's is).
        Press(w, Buttons.CrouchToggle);
        Press(w, Buttons.Crouch);
        Assert.Equal(Buttons.Crouch, Latched(w, p));
    }

    /// <summary>A world with the player standing on a 3 m ledge facing +X (EdgeTests' ledge).</summary>
    static (World World, EntityId Player) OnLedge()
    {
        var level = new TestLevel().Floor(-8, -4, 8, 4).Box(new(-8, -4, 0), new(0, 4, 3f)).Build("ledge 3");
        var w = MovementServices.Register(new WorldBuilder(1), level).Build();
        var p = MovementServices.SpawnControlled(w, new(-1f, 0f, 3.01f), 0f, Locomotion.Sandbox);
        for (int i = 0; i < 30; i++) MovementServices.Step(w, ActionFrame.Idle);
        return (w, p);
    }

    [Fact]
    public void The_hands_taking_a_lip_let_the_crouch_latch_go_and_a_press_of_its_toggle_there_drops_and_lands_standing()
    {
        var (w, p) = OnLedge();
        // Crouch latched, walked off the edge: lowered to hang, as crouch held does — and hanging, the latch is let go.
        Press(w, Buttons.CrouchToggle);
        Assert.Equal(Buttons.Crouch, Latched(w, p));
        Run(w, 240, F(Buttons.Walk, move: true));
        Run(w, 10, F());
        Assert.Equal(BodyState.Hanging, w.Get<Body>(p).State);
        Assert.Equal(Buttons.None, Latched(w, p));
        // A press of crouch toggle lets go (crouch's own press for a tick) and latches nothing: it lands standing.
        Press(w, Buttons.CrouchToggle);
        Run(w, 120, F());
        Assert.NotEqual(BodyState.Hanging, w.Get<Body>(p).State);
        Assert.True(w.Get<Body>(p).Feet.Z < 0.05f);
        Assert.Equal(Buttons.None, Latched(w, p));
        Assert.Equal(0, w.Get<Body>(p).Crouched);
    }

    // ---------------------------------------------------------------- cancel on crouch toggle's key

    [Fact]
    public void Cancel_on_crouch_toggles_key_cancels_a_hang_without_latching_and_standing_it_latches()
    {
        // The key: X both crouch toggle and cancel, as the program's bindings make it (cancel may share one other action's key).
        var table = BindingTable.Build([new("crouchToggle", ["X"]), new("cancel", ["X"])], n => n == "X" ? 27 : null, _ => "X");
        Assert.Empty(table.Problems);
        Assert.True(table.TryGet(InputSource.Key(27), out var action) && action == "crouchToggle");
        Assert.True(table.Cancels(InputSource.Key(27)));
        var shared = Buttons.CrouchToggle | Buttons.Cancel;

        var (w, p) = OnLedge();
        Run(w, 240, F(Buttons.Walk | Buttons.Crouch, move: true));
        Run(w, 10, F());
        Assert.Equal(BodyState.Hanging, w.Get<Body>(p).State);
        // Pressed while hanging (something to cancel): it cancels only — the hang let go, no latch, never crouched.
        for (int i = 0; i < 60; i++)
        {
            MovementServices.Step(w, F(shared));
            if (i == 0) Assert.True(w.Get<MoveIntent>(p).Cancel);
            Assert.False(w.Get<MoveIntent>(p).Crouch);
            Assert.Equal(Buttons.None, Latched(w, p));
        }
        Run(w, 60, F());
        Assert.NotEqual(BodyState.Hanging, w.Get<Body>(p).State);
        Assert.Equal(0, w.Get<Body>(p).Crouched);
        // Pressed standing (nothing to cancel): the crouch latches.
        Press(w, shared);
        Run(w, 30, F());
        Assert.False(w.Get<MoveIntent>(p).Cancel);
        Assert.Equal(Buttons.Crouch, Latched(w, p));
        Assert.Equal(1, w.Get<Body>(p).Crouched);
    }

    // ---------------------------------------------------------------- replays

    /// <summary>A drive pressing every toggle and what lets them go: run, crouch latched, jump, walk latched, sprint, the pointer freed and the view turned.</summary>
    static ActionFrame ToggleDrive(int t)
    {
        static bool At(int t, int at) => t >= at && t < at + 3;
        var held = Buttons.None;
        if (At(t, 20) || At(t, 300) || At(t, 340)) held |= Buttons.CrouchToggle;
        if (At(t, 120)) held |= Buttons.Jump;
        if (At(t, 160)) held |= Buttons.WalkToggle;
        if (t is >= 220 and < 260) held |= Buttons.Sprint;
        if (At(t, 380) || At(t, 440)) held |= Buttons.FreePointerToggle;
        if (t is >= 460 and < 470) held |= Buttons.FreePointer;
        return new ActionFrame(new Vector2(0, t is > 30 and < 420 ? 1f : 0f), new Vector2(t % 50 < 25 ? 0.004f : -0.004f, 0f), held, new Vector2(0.5f, 0.5f));
    }

    [Fact]
    public void A_recorded_drive_with_the_toggles_replays_with_the_same_digest_at_every_tick()
    {
        var level = new TestLevel().Floor(-20, -20, 200, 20).Build();
        World Make(out EntityId p)
        {
            var w = MovementServices.Register(new WorldBuilder(7), level).Build();
            p = MovementServices.SpawnControlled(w, new(0, 0, 0.01f), 0f, Locomotion.Sandbox);
            return w;
        }
        var a = Make(out var pa);
        var tape = new Tape();
        var seen = Buttons.None;
        for (int t = 0; t < 520; t++)
        {
            var f = ToggleDrive(t);
            MovementServices.Step(a, f);
            tape.Frames.Add(f);
            tape.Digests[a.Tick] = a.Digest();
            seen |= Latched(a, pa);
        }
        Assert.Equal(HoldAndToggle.Latchable & ~Buttons.FreeLook & ~Buttons.Aim, seen);       // every latch of the drive was set at some tick (free look, and aim, not driven)

        var path = Path.Combine(Path.GetTempPath(), $"toggles-{Environment.ProcessId}.tape");
        try
        {
            tape.Write(path);
            var back = Tape.Read(path);
            Assert.Equal(tape.Frames, back.Frames);
            var b = Make(out _);
            for (int t = 0; t < back.Frames.Count; t++)
            {
                MovementServices.Step(b, back.Frames[t]);
                Assert.Equal(back.Digests[b.Tick], b.Digest());
            }
        }
        finally { File.Delete(path); }

        // The latch is digested: the same drive with one press of crouch toggle fewer parts from the recording there.
        var c = Make(out _);
        bool parted = false;
        for (int t = 0; t < 30 && !parted; t++)
        {
            var f = ToggleDrive(t);
            MovementServices.Step(c, t is >= 20 and < 23 ? f with { Held = f.Held & ~Buttons.CrouchToggle } : f);
            parted = tape.Digests[c.Tick] != c.Digest();
        }
        Assert.True(parted);
    }

    // ---------------------------------------------------------------- the settings' pointerKey: toggle

    [Fact]
    public void A_free_pointer_key_that_toggled_moves_to_the_toggled_action()
    {
        var bindings = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["freePointer"] = ["Mouse 4", "F"], ["freePointerToggle"] = ["F"], ["crouch"] = ["X"],
        };
        Assert.Equal(["Mouse 4", "F"], HoldAndToggle.MovePointerKeysToToggle(bindings));
        Assert.Empty(bindings["freePointer"]);
        Assert.Equal(["F", "Mouse 4"], bindings["freePointerToggle"]);
        Assert.Equal(["X"], bindings["crouch"]);
        // Done again (or for a file with no key on it), nothing moves.
        Assert.Empty(HoldAndToggle.MovePointerKeysToToggle(bindings));
        Assert.Equal(["F", "Mouse 4"], bindings["freePointerToggle"]);
        var none = new Dictionary<string, string[]>(StringComparer.Ordinal) { ["crouch"] = ["X"] };
        Assert.Empty(HoldAndToggle.MovePointerKeysToToggle(none));
        Assert.False(none.ContainsKey("freePointerToggle"));
    }

    [Fact]
    public void Every_pair_names_its_held_and_toggled_action_and_buttons()
    {
        Assert.Equal(["crouch", "walk", "freePointer", "freeLook", "aim"], HoldAndToggle.Pairs.Select(x => x.Held));   // aim
        Assert.Equal(["crouchToggle", "walkToggle", "freePointerToggle", "freeLookToggle", "aimToggle"], HoldAndToggle.Pairs.Select(x => x.Toggled));
        Assert.Equal(HoldAndToggle.Latchable, HoldAndToggle.Pairs.Aggregate(Buttons.None, (b, x) => b | x.HeldButton));
    }
}
