// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Core;
using Vitomir.Platform.Powers;

namespace Vitomir.Platform.Control;

/// <summary>
/// The body the player drives in direct mode (a tag), and the buttons a cancel swallowed: those that went down
/// with a press of cancel that cancelled — the other action on cancel's key — are not seen until they are let go.
/// </summary>
public struct Controlled
{
    public Buttons Swallowed;
    /// <summary>The sprint (<see cref="Pace"/>): kept by a tap (1), and supernatural speed (1) from a double tap.</summary>
    public int SprintKept, Supernatural;
    /// <summary>How many ticks sprint has been down in this press (0: up), and how old the last press of it is (ticks, 1 on its own tick; 0: none yet).</summary>
    public int SprintDown, PressAge;
    /// <summary>What the press now down was: plain (0), made while the sprint was kept (1: a tap of it ends the sprint), a double tap (2).</summary>
    public int PressKind;
    /// <summary>
    /// The latches of the two-state actions (<see cref="HoldAndToggle"/>): <see cref="Buttons.Crouch"/>,
    /// <see cref="Buttons.Walk"/> and <see cref="Buttons.FreePointer"/>, each set and cleared by a press of its toggle.
    /// </summary>
    public Buttons Latched;
    /// <summary>
    /// Double tap to dodge (<see cref="DoubleTap"/>): for each move key — forward, back, left, right — the ticks since
    /// its last press began (1 on its own tick; 0: none, or used by a double tap).
    /// </summary>
    public int TapForward, TapBack, TapLeft, TapRight;
}

/// <summary>First or third person.</summary>
public enum ViewKind
{
    Third = 0,
    First = 1,
}

/// <summary>
/// Which way the player's body faces in third person (ours): turned to the way it moves, the camera free
/// (as most third-person action games), or always the view's way (it then runs sideways and backwards as it faces
/// forwards). First person always faces the view.
/// </summary>
public enum ThirdPersonFacing
{
    Move = 0,
    View = 1,
}

/// <summary>
/// Where the player's view looks from the controlled body: its yaw and
/// pitch (radians; pitch kept within ±89°), first or third person, which shoulder the third-person camera is
/// over (+1 the right, −1 the left), and whether the pointer is free (the view then stops turning).
/// </summary>
public struct CameraRig
{
    public float Yaw, Pitch;
    public ViewKind View;
    public int Shoulder;
    public bool PointerFree;
    /// <summary>
    /// <b>Free look</b>: how far the third-person camera is swung round the body from its facing
    /// (radians, counter-clockwise), and how fast it eases back to 0 once free look is let go (radians a second, set as it
    /// is let go so the return takes <see cref="FreeLookSettings.ReturnSeconds"/>). <see cref="Yaw"/> stays the body's facing
    /// and aim; the camera (<see cref="Cameras.CameraSystem"/>) looks along <see cref="Yaw"/> + <see cref="Orbit"/>.
    /// </summary>
    public float Orbit, OrbitReturn;

    /// <summary>The steepest the view looks up or down.</summary>
    public const float MaxPitch = 89f * DetMath.DegToRad;

    public static CameraRig Facing(float yaw) => new() { Yaw = yaw, Pitch = 0f, View = ViewKind.Third, Shoulder = 1 };

    /// <summary>
    /// The view after a press of switch view (<i>"all views bound to 1 key and it cycles through
    /// them. So v is first person, third person right shoulder, third person left shoulder, repeat."</i>): first
    /// person, then third over the right shoulder (always the right, whichever it was before), then over the left, then
    /// first again. The controls work: <i>"just the "change camera" key would swap between first person, right
    /// shoulder, then left shoulder, then back through again"</i> — it is the only way to the other shoulder now (swap shoulder is
    /// retired; <see cref="Buttons.SwapShoulder"/>). A press goes on from the view the player is in.
    /// </summary>
    public static CameraRig NextView(CameraRig rig) =>
        rig.View == ViewKind.First ? rig with { View = ViewKind.Third, Shoulder = 1 }
        : rig.Shoulder >= 0 ? rig with { Shoulder = -1 }
        : rig with { View = ViewKind.First };

    /// <summary>
    /// The view's name — <c>first</c> (first
    /// person), <c>right</c> or <c>left</c> (third person over that shoulder), the three the view key cycles through.
    /// </summary>
    public static string ViewName(CameraRig rig) => rig.View == ViewKind.First ? "first" : rig.Shoulder >= 0 ? "right" : "left";

    /// <summary>The rig in the named view (<see cref="ViewName"/>), its look kept; false for a name that is none of the three.</summary>
    public static bool TryInView(CameraRig rig, string? name, out CameraRig inView)
    {
        inView = name switch
        {
            "first" => rig with { View = ViewKind.First, Shoulder = 1 },
            "right" => rig with { View = ViewKind.Third, Shoulder = 1 },
            "left" => rig with { View = ViewKind.Third, Shoulder = -1 },
            _ => rig,
        };
        return name is "first" or "right" or "left";
    }
}

/// <summary>
/// This tick's and the last tick's input for the simulation (the tape's frames): the program — or a
/// replay — calls <see cref="Set"/> before each <see cref="World.Step"/>. Also the player's third-person facing setting
/// and always run, each carried in a tape's head. (The pointer key's setting, held or toggled, gave way to
/// a held and a toggled action, <see cref="HoldAndToggle"/>: the latch is the simulation's, so the tape needs no note.)
/// </summary>
public sealed class TickInput
{
    public ActionFrame Current { get; private set; }
    public ActionFrame Previous { get; private set; }
    /// <summary>The player's setting for which way the body faces in third person.</summary>
    public ThirdPersonFacing ThirdPersonFacing { get; set; } = ThirdPersonFacing.Move;
    /// <summary>
    /// The player's <b>always run</b> (on by default): plain movement runs and walk held walks; off, plain
    /// movement walks and walk held runs (<see cref="Pace"/>). On the tape (<see cref="AlwaysRunNote"/>).
    /// </summary>
    public bool AlwaysRun { get; set; } = true;

    /// <summary>The tape head's note for <see cref="AlwaysRun"/> (<c>true</c> or <c>false</c>).</summary>
    public const string AlwaysRunNote = "alwaysRun";

    /// <summary>
    /// <b>Double tap to dodge</b> (<i>"I think double tap to dodge is a good feature but should default off,
    /// I tend not to use them"</i>): the window in seconds within which a move key pressed, let go and pressed again dodges
    /// that way (<see cref="DoubleTap"/>); 0 (the default) for off. The settings <c>doubleTapDodge</c> and
    /// <c>doubleTapSeconds</c>; on the tape (<see cref="DoubleTapNote"/>).
    /// </summary>
    public float DoubleTapDodge { get; set; }

    /// <summary>The tape head's note for <see cref="DoubleTapDodge"/> (its window in seconds, <c>0</c> for off).</summary>
    public const string DoubleTapNote = "doubleTapDodge";

    /// <summary>Double tap to dodge as a tape's head records it: off unless it gives a window (an older tape had none).</summary>
    public static float DoubleTapFrom(IReadOnlyDictionary<string, string> notes) =>
        notes.TryGetValue(DoubleTapNote, out var v) && float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float s) && s > 0f ? s : 0f;

    /// <summary>Always run as a tape's head records it: on unless it says <c>false</c> (an older tape ran so).</summary>
    public static bool AlwaysRunFrom(IReadOnlyDictionary<string, string> notes) =>
        !notes.TryGetValue(AlwaysRunNote, out var v) || !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>The frame for the tick about to be stepped; the one before becomes <see cref="Previous"/>.</summary>
    public void Set(in ActionFrame frame)
    {
        Previous = Current;
        Current = frame;
    }

    /// <summary>The buttons that went down on this tick.</summary>
    public Buttons Pressed => ButtonEdges.Pressed(Current.Held, Previous.Held);
}

/// <summary>
/// The Input phase's direct mode (ours): for each controlled body with a
/// <see cref="CameraRig"/> and a <see cref="MoveIntent"/>, the latches of crouch, the other pace and the freed pointer
/// are kept (<see cref="HoldAndToggle"/>, each on with its held button or its latch, which a press of its toggle
/// flips), the look turns the rig (not while the pointer is free), a press of switch view takes effect (and a press of the
/// retired swap shoulder, which only an older tape holds: <see cref="Buttons.SwapShoulder"/>), and the
/// move becomes the intent relative to the rig's yaw — its pace from the keys and the always-run setting
/// (<see cref="Pace"/>, sprint held, tapped and kept, or double-tapped for supernatural speed) — with jump as
/// pressed and held (a charge) and crouch as held or latched. The body faces the view's way in first
/// person, and in third person too when the player's setting says so; otherwise (the default) it turns to the
/// way it moves (<see cref="Bodies.Locomotion.TurnDegreesPerSecond"/>) and keeps its facing when it stands.
/// At a walk the intent asks for the edge guard of the safe fall (<see cref="EdgeGuard.SafeFall"/>). A press of
/// cancel when the body has something to cancel (<see cref="BodyMotor.CanCancel"/>) becomes the intent's
/// <see cref="MoveIntent.Cancel"/> and swallows the buttons that went down with it (<see cref="Controlled.Swallowed"/>),
/// so a key that is both crouch and cancel cancels a hang without crouching, and crouches when there is nothing to cancel
/// (and a key that is both crouch toggle and cancel likewise cancels without flipping the latch).
/// <para>
/// The quick slots: a press of a slot's key (an edge, after the cancel's swallow) writes the body's
/// <see cref="PowerIntent"/> for that tick — the lowest slot when two go down together, 0 on a tick with none — when the
/// world has the powers' contract (<see cref="PowerContract.Register"/>, which the module that reads the intent calls);
/// a world without it is left as it was, digest for digest. A held key makes no second use. The freed pointer does not
/// stop a slot (it is a key, not a click); the pause menu and the pages do, as they stop everything: they take the
/// window's input and the simulation does not step while they are open.
/// </para>
/// </summary>
public sealed class PlayerControlSystem : ISystem
{
    // And (when the world has fighting) attack and block into the body's CombatIntent, a block held making a walk.
    public void Run(World world)
    {
        var input = world.Service<TickInput>();
        world.TryService<PointerState>(out var pointer);   // may be absent (a world without the pointer)
        var frame = input.Current;
        var pressed = input.Pressed;
        var tags = world.Store<Controlled>();
        var rigs = world.Store<CameraRig>();
        var intents = world.Store<MoveIntent>();
        var bodies = world.Store<Body>();
        var moves = world.Store<Traversal>();
        var fights = Animation.PoseRigSystem.Optional<CombatIntent>(world, ref combatKnown);   // may be absent
        var powers = world.TryStore<PowerIntent>(out var intentsOfPowers) ? intentsOfPowers : null;   // the powers' contract, when a module registers it
        var ents = tags.Entities;
        var all = frame;
        var allPressed = pressed;
        for (int i = 0; i < ents.Length; i++)
        {
            var e = ents[i];
            if (!rigs.Has(e) || !intents.Has(e)) continue;
            ref var rig = ref rigs.Get(e);
            ref var intent = ref intents.Get(e);
            ref var fight = ref fights is null ? ref System.Runtime.CompilerServices.Unsafe.NullRef<CombatIntent>() : ref fights.Find(e);
            ref var tag = ref tags.Data[i];

            // Cancel: a press with something to cancel cancels and does nothing else — the buttons that went
            // down with it (the other action on its key) are swallowed until let go; with nothing to cancel the other
            // action works as ever.
            tag.Swallowed &= all.Held;
            bool cancels = (allPressed & Buttons.Cancel) != 0 && bodies.Has(e) && moves.Has(e) && BodyMotor.CanCancel(bodies.Get(e), moves.Get(e));
            // A swing the rules let be cancelled now (before its strike: a feint) is something to cancel too.
            bool feint = !cancels && (allPressed & Buttons.Cancel) != 0 && SwingCancelCalls.Can(world, e, SwingCancelCause.Cancel)
                         && SwingCancelCalls.Do(world, e, SwingCancelCause.Cancel);
            if (feint) tag.Swallowed |= allPressed & ~Buttons.Cancel;
            if (cancels) tag.Swallowed |= allPressed & ~Buttons.Cancel;
            // A drawn bow is something to cancel too — the press lets it down and does nothing else.
            if (!cancels && !feint && (allPressed & Buttons.Cancel) != 0 && PlayerBow.CanLetDown(world, e))
            {
                PlayerBow.LetDown(world, e);
                tag.Swallowed |= allPressed & ~Buttons.Cancel;
            }
            // Cancelling a charge swallows the jump held for it too, so letting it go jumps nothing.
            if (cancels && BodyMotor.IsCharging(bodies.Get(e))) tag.Swallowed |= all.Held & Buttons.Jump;
            frame = all with { Held = all.Held & ~tag.Swallowed };
            pressed = allPressed & ~tag.Swallowed;
            intent.Cancel = cancels;

            // Hold and toggle — each two-state action on with its held button or its latch; a press of its toggle
            // flips the latch, and a jump, a sprint, the hands taking hold or the held key let go clear it (HoldAndToggle).
            bool hangs = bodies.Has(e) && bodies.Get(e).State is BodyState.Hanging or BodyState.Ladder;
            var latches = HoldAndToggle.Step(tag.Latched, frame.Held, pressed, ButtonEdges.Pressed(input.Previous.Held, all.Held), hangs);
            tag.Latched = latches.Latched;
            frame = frame with { Held = (frame.Held & ~HoldAndToggle.Latchable) | latches.On };
            pressed = latches.Pressed;

            rig.PointerFree = frame.Holds(Buttons.FreePointer);
            if (!rig.PointerFree)
            {
                // Free look: in third person, while it is on, the look swings the camera round the body
                // (the orbit) and the body keeps its facing; let go, the orbit eases back to 0 (FreeLook.Step).
                bool looking = frame.Holds(Buttons.FreeLook) && rig.View == ViewKind.Third;
                if (looking || rig.Orbit != 0f) FreeLook.Step(ref rig, looking, frame.Look.X, freeLook ??= world.TryService<FreeLookSettings>(out var fl) ? fl : FreeLookSettings.Default);
                else rig.Yaw = DetMath.WrapAngle(rig.Yaw + frame.Look.X);
                rig.Pitch = Math.Clamp(rig.Pitch + frame.Look.Y, -CameraRig.MaxPitch, CameraRig.MaxPitch);
            }
            if ((pressed & Buttons.SwitchView) != 0) rig = CameraRig.NextView(rig);
            if ((pressed & Buttons.SwapShoulder) != 0) rig.Shoulder = rig.Shoulder >= 0 ? -1 : 1;   // an older tape's press only (retired)

            var (s, c) = DetMath.SinCos(rig.Yaw);
            var forward = new Vector2(c, s);
            var right = new Vector2(s, -c);
            var wish = forward * frame.Move.Y + right * frame.Move.X;
            float l = wish.Length();
            if (l > 1f) wish /= l;
            intent.Direction = wish;
            intent.Gait = Pace.Of(world, e, ref tag, frame, pressed, input.AlwaysRun, wish);
            intent.Jump = (pressed & Buttons.Jump) != 0;
            intent.JumpHeld = frame.Holds(Buttons.Jump);
            intent.Crouch = frame.Holds(Buttons.Crouch) || latches.CrouchLetGo;
            // Onto a ladder at its foot or top — unless the press talks: one press of use does one thing, and
            // talking to the person the reticle is on (the pointer's verb from last tick) comes before a ladder.
            intent.Use = (pressed & Buttons.Use) != 0 && !(pointer is { Free: false, Verb: PointerVerb.Talk });
            // Use held catches a lip at the commit point in the air — only with the commit on (a world without it keeps the intent the earlier build's).
            intent.UseHeld = frame.Holds(Buttons.Use) && world.TryService<Animation.People.PersonCommitSettings>(out var commitOn) && commitOn.On;
            // At a walk the body stops at an edge whose drop is more than its safe fall.
            intent.Guard = intent.Gait == Gait.Walk ? EdgeGuard.SafeFall : EdgeGuard.None;
            intent.GuardCorner = default;
            intent.Facing = rig.Yaw;
            intent.Faces = rig.View == ViewKind.First || input.ThirdPersonFacing == ThirdPersonFacing.View;

            // Attack and block held (the freed pointer keeps Mouse Left for its click, no attack while it is
            // free); moving while blocking is a walk.
            if (!System.Runtime.CompilerServices.Unsafe.IsNullRef(ref fight))
            {
                fight.Attack = frame.Holds(Buttons.Attack) && !rig.PointerFree;
                fight.Block = frame.Holds(Buttons.Block) && !rig.PointerFree;
                if (fight.Block && intent.Gait > Gait.Walk) { intent.Gait = Gait.Walk; intent.Guard = EdgeGuard.SafeFall; }
            }
            // The player's bow — the swap, the draw and loose, the let-down, the aim at the crosshair (a world with a bow only).
            PlayerBow.Step(world, e, frame, pressed, ref fight, in rig, ref intent);

            // A press of Dodge asks for a dodge along the move keys' way (straight back from the facing when none held);
            // the intent is put on the body at its first press, so a world whose player never dodges is unchanged. Like a
            // slot's key, the freed pointer does not stop it.
            if ((pressed & Buttons.Dodge) != 0) Dodges.Ask(world, e, wish);
            // Double tap to dodge (off by default): a move key pressed, let go and pressed again within the window.
            if (input.DoubleTapDodge > 0f && DoubleTap.Step(ref tag, all.Move, input.Previous.Move, input.DoubleTapDodge)) Dodges.Ask(world, e, wish);

            // A slot's key pressed uses its slot this tick (the lowest of two together); the component is put on the
            // body at its first press, so a world whose player never presses one is unchanged.
            if (powers is not null)
            {
                int slot = QuickSlotButtons.Lowest(pressed);
                ref var use = ref powers.Find(e);
                if (!System.Runtime.CompilerServices.Unsafe.IsNullRef(ref use)) use.Slot = slot;
                else if (slot != 0) world.Set(e, new PowerIntent { Slot = slot });
            }
        }
    }

    int combatKnown;   // 0 not looked for, 1 registered, 2 not (a world without fighting)
    FreeLookSettings? freeLook;
}

/// <summary>
/// <b>Double tap to dodge</b> (ours; off by default: <see cref="TickInput.DoubleTapDodge"/>): a move key —
/// forward, back, left or right — pressed, let go and pressed again within the window asks for a dodge the way the keys
/// point on the second press (as Dodge pressed then would). Read from the tick's move keys and the tick before's (their
/// edges), never from the window system's key repeat, so a tape replays it; the keys' ages are the body's
/// (<see cref="Controlled.TapForward"/> and the rest, digested). A double tap uses up its keys' ages, so a third tap starts
/// afresh. Allocates nothing.
/// </summary>
public static class DoubleTap
{
    /// <summary>A move key counts as down when the move's part along it is more than this (a diagonal's 0.71 counts).</summary>
    public const float Down = 0.5f;

    /// <summary>
    /// One tick of <paramref name="tag"/>'s move keys' ages from the move now (<paramref name="move"/>: x right, y forward)
    /// and the tick before's; whether a key was double-tapped within <paramref name="window"/> seconds this tick.
    /// </summary>
    public static bool Step(ref Controlled tag, System.Numerics.Vector2 move, System.Numerics.Vector2 before, float window)
    {
        int most = Math.Max(1, (int)MathF.Round(window * World.TicksPerSecond));
        bool tapped = false;
        tapped |= Key(ref tag.TapForward, move.Y > Down, before.Y > Down, most);
        tapped |= Key(ref tag.TapBack, move.Y < -Down, before.Y < -Down, most);
        tapped |= Key(ref tag.TapRight, move.X > Down, before.X > Down, most);
        tapped |= Key(ref tag.TapLeft, move.X < -Down, before.X < -Down, most);
        return tapped;
    }

    /// <summary>One key: its age moved on; on a press, a double tap when the last press began within the window (ticks).</summary>
    static bool Key(ref int age, bool now, bool was, int most)
    {
        if (age > 0 && age < int.MaxValue) age++;
        if (!now || was) return false;
        if (age > 0 && age - 1 <= most) { age = 0; return true; }
        age = 1;
        return false;
    }
}

/// <summary>
/// How the player's control and the dodge reach <see cref="SwingCancel"/>: its own answers, unless the world has a
/// <see cref="SwingCancelStandIn"/> (a test's, standing in for the swing's side, or to test the input
/// alone after it).
/// </summary>
public static class SwingCancelCalls
{
    /// <summary><see cref="SwingCancel.Can"/>, or the stand-in's.</summary>
    public static bool Can(World world, EntityId e, SwingCancelCause cause) =>
        world.TryService<SwingCancelStandIn>(out var s) ? s.Can(world, e, cause) : SwingCancel.Can(world, e, cause);

    /// <summary><see cref="SwingCancel.Do"/>, or the stand-in's.</summary>
    public static bool Do(World world, EntityId e, SwingCancelCause cause) =>
        world.TryService<SwingCancelStandIn>(out var s) ? s.Do(world, e, cause) : SwingCancel.Do(world, e, cause);
}

/// <summary>A stand-in for <see cref="SwingCancel"/> (tests only): a world service answering in its place.</summary>
public sealed class SwingCancelStandIn(Func<World, EntityId, SwingCancelCause, bool> can, Func<World, EntityId, SwingCancelCause, bool> @do)
{
    public bool Can(World world, EntityId e, SwingCancelCause cause) => can(world, e, cause);
    public bool Do(World world, EntityId e, SwingCancelCause cause) => @do(world, e, cause);
}

/// <summary>Free look's numbers (ENGINE_SANDBOX, ours): how long the camera takes to swing back behind the body once let go.</summary>
public sealed record FreeLookSettings
{
    /// <summary>The swing back's length (s): a quarter of a second (the), whatever the orbit was.</summary>
    public float ReturnSeconds { get; init; } = 0.25f;

    /// <summary>The numbers used when a world registers none.</summary>
    public static readonly FreeLookSettings Default = new();
}

/// <summary>
/// <b>Free look</b> (an earlier build: <i>"a free-look key in third person (held: the camera swings round Christof without
/// turning him), unbound until given a key"</i>; ours, as third-person games' free look). In third person, while
/// <see cref="Buttons.FreeLook"/> is on (held or latched), the look's turn goes to the camera's orbit round the body
/// (<see cref="CameraRig.Orbit"/>) and the rig's <see cref="CameraRig.Yaw"/> — the body's facing, the way the move keys,
/// attacks and blocks go — is kept; the pitch moves as ever. Let go, the orbit eases back to 0 at an even rate taking
/// <see cref="FreeLookSettings.ReturnSeconds"/>, while the look turns the body again at once. In first person it does
/// nothing. The orbit is the simulation's (digested), so a tape replays it.
/// </summary>
public static class FreeLook
{
    /// <summary>One tick of the rig: <paramref name="looking"/> (free look on, third person) and the look's turn this tick.</summary>
    public static void Step(ref CameraRig rig, bool looking, float lookYaw, FreeLookSettings s)
    {
        if (looking)
        {
            rig.Orbit = DetMath.WrapAngle(rig.Orbit + lookYaw);
            rig.OrbitReturn = 0f;
            return;
        }
        rig.Yaw = DetMath.WrapAngle(rig.Yaw + lookYaw);
        if (rig.Orbit == 0f) { rig.OrbitReturn = 0f; return; }
        if (rig.OrbitReturn <= 0f) rig.OrbitReturn = MathF.Abs(rig.Orbit) / MathF.Max(World.TickSeconds, s.ReturnSeconds);
        float step = rig.OrbitReturn * World.TickSeconds;
        rig.Orbit = rig.Orbit > 0f ? MathF.Max(0f, rig.Orbit - step) : MathF.Min(0f, rig.Orbit + step);
        if (rig.Orbit == 0f) rig.OrbitReturn = 0f;
    }
}
