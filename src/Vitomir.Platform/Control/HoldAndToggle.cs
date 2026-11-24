// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Platform.Control;

/// <summary>What one tick of <see cref="HoldAndToggle.Step"/> gives: the latches kept, the states on, and the presses the rest of the input sees.</summary>
/// <param name="Latched">The latches after this tick (bits of <see cref="HoldAndToggle.Latchable"/>), kept in <see cref="Controlled.Latched"/>.</param>
/// <param name="On">Each two-state action's state this tick: its held button or its latch (bits of <see cref="HoldAndToggle.Latchable"/>).</param>
/// <param name="Pressed">The presses with a latch turned on counted as a press of its held button (so a kept sprint ends on a walk latched as on walk pressed).</param>
/// <param name="CrouchLetGo">A press of crouch toggle while the hands hold a lip or a ladder: crouch for this one tick (the motor's press, which lets go), never latched.</param>
public readonly record struct Latches(Buttons Latched, Buttons On, Buttons Pressed, bool CrouchLetGo);

/// <summary>
/// <b>Hold and toggle</b> (ours; on his aim answer at 13:11: <i>"let's make it
/// so there are toggle and hold keys. Default is map Q to hold, toggle is empty."</i>): each two-state action has a
/// <b>held</b> action and a <b>toggled</b> action, either of which may be unbound (<see cref="Pairs"/>). The state is on
/// while the held button is down <b>or</b> the body's latch is set; a press of the toggled action flips the latch. The
/// latch is the simulation's (<see cref="Controlled.Latched"/>, digested), so a tape of the presses replays it exactly.
/// Sprint keeps its own scheme (held, tapped and kept, double-tapped; <see cref="Pace"/>).
/// <para>
/// What else lets a latch go — common practice in games with toggled crouch and walk, and the freed pointer's held key:
/// </para>
/// <list type="bullet">
/// <item><b>Crouch</b>: a press of <b>jump</b> (a jump stands the body up, as a toggled crouch does in most shooters) or
/// of <b>sprint</b> (a sprint begun stands it up and runs). And when the hands take a lip or a ladder (the body
/// <see cref="Bodies.BodyState.Hanging"/> or on a <see cref="Bodies.BodyState.Ladder"/>) the latch lets go, as a held
/// crouch is let go of to hang; there a press of crouch toggle is crouch for one tick — crouch's own press, which
/// lets go of the lip or the ladder (<see cref="Bodies.BodyMotor"/>) — and the body is not left crouched when it lands.</item>
/// <item><b>The other pace</b> (walk; run when always run is off): a press of <b>sprint</b>. A latch turned on counts
/// as a press of walk, so it ends a kept sprint as walk pressed does.</item>
/// <item><b>The freed pointer</b>: letting go of the <b>held</b> key gives the view back, as it does when nothing is
/// latched (the held key's release always captures the pointer).</item>
/// <item><b>Free look</b>: nothing else lets its latch go; a press of its toggle ends it.</item>
/// </list>
/// A press of a toggle that a cancel swallowed (a key that is both cancel and a toggle, pressed with something to
/// cancel) is not seen, so it flips nothing (<see cref="PlayerControlSystem"/>).
/// </summary>
public static class HoldAndToggle
{
    /// <summary>
    /// The pairs of actions, held and toggled (the bindings' names), with their buttons. the bow's aim joins
    /// them, <c>aim</c> held (Q) and <c>aimToggle</c> unbound.
    /// </summary>
    public static readonly IReadOnlyList<(string Held, string Toggled, Buttons HeldButton, Buttons ToggleButton)> Pairs =
    [
        ("crouch", "crouchToggle", Buttons.Crouch, Buttons.CrouchToggle),
        ("walk", "walkToggle", Buttons.Walk, Buttons.WalkToggle),
        ("freePointer", "freePointerToggle", Buttons.FreePointer, Buttons.FreePointerToggle),
        ("freeLook", "freeLookToggle", Buttons.FreeLook, Buttons.FreeLookToggle),
        ("aim", "aimToggle", Buttons.Aim, Buttons.AimToggle),                         // the bow's aim mode
    ];

    /// <summary>The held buttons that have a latch.</summary>
    public const Buttons Latchable = Buttons.Crouch | Buttons.Walk | Buttons.FreePointer | Buttons.FreeLook | Buttons.Aim;   // Aim

    /// <summary>
    /// One tick: the latches from <paramref name="latched"/> (last tick's), the buttons <paramref name="held"/> and
    /// <paramref name="pressed"/> this tick (a cancel's swallowed already taken out), those <paramref name="released"/>
    /// since the last, and whether the hands hold a lip or a ladder (<paramref name="hangs"/>).
    /// </summary>
    public static Latches Step(Buttons latched, Buttons held, Buttons pressed, Buttons released, bool hangs)
    {
        var was = latched & Latchable;
        var l = was;
        if ((pressed & Buttons.CrouchToggle) != 0) l ^= Buttons.Crouch;
        if ((pressed & Buttons.WalkToggle) != 0) l ^= Buttons.Walk;
        if ((pressed & Buttons.FreePointerToggle) != 0) l ^= Buttons.FreePointer;
        if ((pressed & Buttons.FreeLookToggle) != 0) l ^= Buttons.FreeLook;     // free look
        if ((pressed & Buttons.AimToggle) != 0) l ^= Buttons.Aim;               // aim mode latched (nothing else lets it go)
        // Crouch lets go on a jump or a sprint, and when the hands take hold; a press while they hold lets go for one tick.
        bool letGo = hangs && (pressed & Buttons.CrouchToggle) != 0;
        if (hangs || (pressed & (Buttons.Jump | Buttons.Sprint)) != 0) l &= ~Buttons.Crouch;
        // The other pace lets go on a sprint.
        if ((pressed & Buttons.Sprint) != 0) l &= ~Buttons.Walk;
        // The freed pointer lets go when its held key is let go.
        if ((released & Buttons.FreePointer) != 0) l &= ~Buttons.FreePointer;
        var on = (held & Latchable) | l;
        var morePressed = l & ~was & ~held;     // a latch turned on with its held button up: that state's press
        return new Latches(l, on, pressed | morePressed, letGo);
    }

    /// <summary>
    /// A settings file whose free-pointer key toggled (the setting <c>pointerKey: toggle</c>) brought to the
    /// pair: <c>freePointer</c>'s keys move to <c>freePointerToggle</c> (after any it has), leaving <c>freePointer</c>
    /// unbound. Answers the keys moved (none when it had none). The program does it once (the settings' layout version).
    /// </summary>
    public static IReadOnlyList<string> MovePointerKeysToToggle(Dictionary<string, string[]> bindings)
    {
        if (!bindings.TryGetValue("freePointer", out var keys) || keys.Length == 0) return [];
        var moved = keys.Where(k => !string.IsNullOrWhiteSpace(k)).ToArray();
        var had = bindings.TryGetValue("freePointerToggle", out var t) ? t : [];
        bindings["freePointerToggle"] = [.. had, .. moved.Where(k => !had.Contains(k))];
        bindings["freePointer"] = [];
        return moved;
    }
}
