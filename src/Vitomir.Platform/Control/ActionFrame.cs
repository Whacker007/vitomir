// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Platform.Control;

/// <summary>The buttons a player can hold, as actions rather than keys (bindings are data).</summary>
[Flags]
public enum Buttons : uint
{
    None = 0,
    Jump = 1 << 0,
    Sprint = 1 << 1,
    Crouch = 1 << 2,
    Use = 1 << 3,
    /// <summary>Switches between first and third person (pressed, not held).</summary>
    SwitchView = 1 << 4,
    /// <summary>
    /// <b>Retired</b> (<i>"As for swap shoulders, we were going to get rid of that, and just
    /// the "change camera" key would swap between first person, right shoulder, then left shoulder"</i>): no binding, no Controls
    /// page row, no scenario button; the program never sends it. The bit stays reserved: a tape made earlier that pressed it
    /// replays as it was made (the press still puts the third-person camera over the other shoulder, <see cref="PlayerControlSystem"/>),
    /// so its digests agree. Never give this bit to another action.
    /// </summary>
    SwapShoulder = 1 << 5,
    /// <summary>Frees the pointer while held (a press of <see cref="FreePointerToggle"/> latches it; <see cref="HoldAndToggle"/>).</summary>
    FreePointer = 1 << 6,
    /// <summary>The other pace while held: walk, or run when always run is off (<see cref="WalkToggle"/> latches it).</summary>
    Walk = 1 << 7,
    /// <summary>
    /// Switches between direct mode and click mode (pressed). Click mode is not in the enhanced game (DI): it
    /// is the 1.1 module's, later; the action has no default key and no row on the Controls page and nothing reads it.
    /// </summary>
    SwitchMode = 1 << 8,
    Attack = 1 << 9,
    Block = 1 << 10,
    /// <summary>Switches between the masquerade (moving as a human) and unbound (the body's own powers) (pressed).</summary>
    Masquerade = 1 << 11,
    /// <summary>
    /// Cancels what can be cancelled (pressed): a hang let go, a move in its first moments undone. It may
    /// share a key with one other action; a press that cancels does nothing else (<see cref="PlayerControlSystem"/>).
    /// </summary>
    Cancel = 1 << 12,
    /// <summary>
    /// A click of the freed pointer (pressed): it acts on what is under the pointer only where a verb applies
    /// (<see cref="Cameras.PointerSystem"/>). Its own action, by default on attack's Mouse Left: the program sends the key
    /// to this while the pointer is free and to the other action while it is captured, so the two never mix.
    /// </summary>
    PointerClick = 1 << 13,
    /// <summary>
    /// <b>The toggled halves</b> of the two-state actions (pressed; <i>"toggle and hold keys"</i>):
    /// a press flips the body's latch for crouch, the other pace and the freed pointer, which is on with its held button
    /// or its latch (<see cref="HoldAndToggle"/>). Unbound by default.
    /// </summary>
    CrouchToggle = 1 << 14,
    /// <inheritdoc cref="CrouchToggle"/>
    WalkToggle = 1 << 15,
    /// <inheritdoc cref="CrouchToggle"/>
    FreePointerToggle = 1 << 16,
    /// <summary>
    /// <b>The quick slots</b> (pressed; <i>"bindable quick slots that can have keys bound to them"</i>):
    /// a press of a slot's key uses the slot at once (<see cref="PlayerControlSystem"/> writes
    /// <see cref="Powers.PowerIntent"/>), keys 1 to 8 by default. A older tape has none of these bits, so it reads
    /// and replays as it was. <see cref="QuickSlotButtons"/> maps them to the slots' numbers.
    /// </summary>
    Slot1 = 1 << 17,
    /// <inheritdoc cref="Slot1"/>
    Slot2 = 1 << 18,
    /// <inheritdoc cref="Slot1"/>
    Slot3 = 1 << 19,
    /// <inheritdoc cref="Slot1"/>
    Slot4 = 1 << 20,
    /// <inheritdoc cref="Slot1"/>
    Slot5 = 1 << 21,
    /// <inheritdoc cref="Slot1"/>
    Slot6 = 1 << 22,
    /// <inheritdoc cref="Slot1"/>
    Slot7 = 1 << 23,
    /// <inheritdoc cref="Slot1"/>
    Slot8 = 1 << 24,
    /// <summary>
    /// <b>Dodge</b> (pressed): a quick evasive step along the way the move keys point (straight back when none), the
    /// body really moving (<see cref="Bodies.DodgeSystem"/>, <see cref="Bodies.BodyMotor.DodgeStep"/>). A tape from before
    /// An older tape has none of this bit, so it reads and replays as it was.
    /// </summary>
    Dodge = 1 << 25,
    /// <summary>
    /// <b>Free look</b> (held; <i>"a free-look key in third person (held: the camera swings round
    /// Christof without turning him), unbound until given a key"</i>): in third person, while it is on, the look orbits the
    /// camera round the body and the body keeps its facing (<see cref="PlayerControlSystem"/>, <see cref="CameraRig.Orbit"/>).
    /// A two-state action (<see cref="HoldAndToggle"/>): held, or latched by <see cref="FreeLookToggle"/>. Unbound by default.
    /// A older tape has none of these bits, so it reads as it was.
    /// </summary>
    FreeLook = 1 << 26,
    /// <inheritdoc cref="FreeLook"/>
    FreeLookToggle = 1 << 27,
    /// <summary>
    /// <b>Swap weapon</b> (pressed): the melee weapon and the bow swap (<see cref="PlayerBow"/>), F by default. A tape
    /// from earlier has none of this bit.
    /// </summary>
    SwapWeapon = 1 << 28,
    /// <summary>
    /// <b>Aim</b> (held; the aim mode): with the bow in hand, the bow raised into the ready
    /// pose and an arrow nocked, undrawn; attack held while aiming draws (attack without aim does nothing with the bow); let go, a
    /// draw let down and the bow back low. on Q by default (<i>"Q is aim"</i>; an earlier build left it unbound while Q was
    /// Dodge's); latched by <see cref="AimToggle"/>. A older tape
    /// has none of this bit.
    /// </summary>
    Aim = 1 << 29,
    /// <summary>
    /// <b>Aim (toggle)</b> (pressed; <i>"let's make it so there are toggle and hold keys. Default
    /// is map Q to hold, toggle is empty."</i>): a press latches aim mode on or off (<see cref="HoldAndToggle"/>). Unbound by default. A
    /// older tape has none of this bit.
    /// </summary>
    AimToggle = 1u << 30,
    /// <summary>
    /// <b>Stealth</b> (pressed): a press toggles stealth (a careful, slower, lower mode; in it the back is put to a wall behind as cover), C by
    /// default where nothing else has it. A older tape has none of this bit. The last bit of the word.
    /// </summary>
    Stealth = 1u << 31,
}

/// <summary>
/// The quick slots' buttons by number: slot <c>n</c> (1 to <see cref="Powers.QuickSlots.Count"/>) is
/// <see cref="Buttons.Slot1"/> shifted by <c>n − 1</c>, and its binding's action is <c>slot</c><i>n</i>.
/// </summary>
public static class QuickSlotButtons
{
    /// <summary>Every slot's button.</summary>
    public const Buttons All = Buttons.Slot1 | Buttons.Slot2 | Buttons.Slot3 | Buttons.Slot4 | Buttons.Slot5 | Buttons.Slot6 | Buttons.Slot7 | Buttons.Slot8;

    const int FirstBit = 17;

    /// <summary>The bindings' action names, slot 1 first (<c>slot1</c> … <c>slot8</c>).</summary>
    public static readonly IReadOnlyList<string> Actions = ["slot1", "slot2", "slot3", "slot4", "slot5", "slot6", "slot7", "slot8"];

    /// <summary>Slot <paramref name="slot"/>'s button (1 to 8); none for another number.</summary>
    public static Buttons Of(int slot) => slot is >= 1 and <= Powers.QuickSlots.Count ? (Buttons)(1u << (FirstBit + slot - 1)) : Buttons.None;

    /// <summary>The lowest slot whose button is in <paramref name="buttons"/> (two pressed together: the lower), or 0 for none.</summary>
    public static int Lowest(Buttons buttons)
    {
        uint bits = (uint)(buttons & All) >> FirstBit;
        return bits == 0 ? 0 : System.Numerics.BitOperations.TrailingZeroCount(bits) + 1;
    }
}

/// <summary>
/// One tick's input, as the simulation sees it and as the tape records it: the move
/// wanted (x to the right, y forward, each −1 to 1), the look turned this tick (radians: yaw to the left,
/// pitch up), the buttons held, where the pointer is when it is free — its place <b>on the picture</b>, 0 to 1
/// across and down (the picture fitted in the window, so the bars beside it are outside 0 to 1; the centre
/// while the pointer is captured) — and the picture's shape (<see cref="Aspect"/>, its width over its height; 0 when
/// not known, read as <see cref="DefaultAspect"/>). With the view's field (the cameras' settings) these two give the
/// ray through the pointer (<see cref="Cameras.PointerPick"/>), so a replay picks the same thing. The program builds
/// these from the window's events; a replay reads them back, so neither needs the other. An earlier build:
/// <see cref="AttackMode"/>, the player's way of choosing a blow's direction changed on this tick (the Controls page's
/// <i>Attack Direction</i> set mid-run), else null: the session applies it before the tick and a tape records it at the tick.
/// </summary>
public readonly record struct ActionFrame(Vector2 Move, Vector2 Look, Buttons Held, Vector2 Pointer, float Aspect = 0f, AttackDirectionMode? AttackMode = null)
{
    public static ActionFrame Idle => default;

    /// <summary>The picture's shape taken when a frame does not say (an older tape, a test): 16 by 9.</summary>
    public const float DefaultAspect = 16f / 9f;

    /// <summary>The picture's width over its height for this frame.</summary>
    public float PictureAspect => Aspect > 0.01f ? Aspect : DefaultAspect;

    public bool Holds(Buttons b) => (Held & b) != 0;
}

/// <summary>Which buttons went down on this tick: held now and not on the last.</summary>
public static class ButtonEdges
{
    public static Buttons Pressed(Buttons now, Buttons before) => now & ~before;
}
