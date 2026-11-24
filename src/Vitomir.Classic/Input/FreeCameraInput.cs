// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The free camera's keys, the host's half: the bindings read into `FreeKeys` once a frame while the
// free camera is up, and its way in and out; the drag look and level view.
using System.Numerics;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir;

/// <summary>
/// The free camera's keys as the host reads them (ours: the engine half is <see cref="FreeCameraKeys"/>).
/// <see cref="Up"/> says whether the free camera is up this frame, which is what makes its keys live —
/// <see cref="KeyInput"/> gives a chord one of them has to it alone while it is (1.1's camera mode 3 reads
/// nothing else, camera mode 3), and nothing at all while it is not.
/// </summary>
static class FreeCameraInput
{
    /// <summary>Whether the free camera is up — its keys are live — as of the last <see cref="Frame"/>.</summary>
    public static bool Up { get; private set; }

    /// <summary>
    /// Whether the drag look is held this frame (ours: <c>[qol] driving</c>, the free camera up and
    /// reading keys) — the host pins the pointer for it as it does for the swivel — and whether that
    /// changed this frame.
    /// </summary>
    public static bool Dragging { get; private set; }

    /// <inheritdoc cref="Dragging"/>
    public static bool DragChanged { get; private set; }

    static void Drag(bool now)
    {
        DragChanged = now != Dragging;
        Dragging = now;
    }

    static readonly FreeCameraKeys keys = new();
    static bool toggleWas = true;

    static readonly (KeyCommand Command, FreeKeys Key)[] Map =
    [
        (KeyCommand.FlyForward, FreeKeys.Forward), (KeyCommand.FlyBack, FreeKeys.Back),
        (KeyCommand.FlyLeft, FreeKeys.Left), (KeyCommand.FlyRight, FreeKeys.Right),
        (KeyCommand.FlyUp, FreeKeys.Up), (KeyCommand.FlyDown, FreeKeys.Down),
        (KeyCommand.FlyTurnLeft, FreeKeys.TurnLeft), (KeyCommand.FlyTurnRight, FreeKeys.TurnRight),
        (KeyCommand.FlyLookDown, FreeKeys.LookDown), (KeyCommand.FlyLookUp, FreeKeys.LookUp),
        (KeyCommand.FlyRollLeft, FreeKeys.RollLeft), (KeyCommand.FlyRollRight, FreeKeys.RollRight),
        (KeyCommand.FlyLevel, FreeKeys.Level),
        // Ours, read by the driving's handling alone.
        (KeyCommand.FlyLevelView, FreeKeys.LevelView), (KeyCommand.FlyDrag, FreeKeys.Drag),
        // 1.1's Shift; Vitomir's fast is the driving's run, as the driving camera's.
        (KeyCommand.Run, FreeKeys.Fast),
    ];

    /// <summary>
    /// One frame, before the world steps. <paramref name="up"/>: the free camera has the picture
    /// (<paramref name="view"/> is drawn from it). <paramref name="reading"/>: no menu and no text field has
    /// the keyboard (1.1 reads no key under a dialog, and none while the chat line is
    /// open). <paramref name="seconds"/>: the frame on the game's pausable clock.
    /// <paramref name="open"/>: whether Ctrl+P's way in is open — <c>[qol] freecamera</c> or <c>--devmode</c>, not on a
    /// player's machine, on a host only in a moderated game (<see cref="FreeCameraGate"/>).
    /// <paramref name="mouse"/>: the mouse's movement this frame, which the drag look turns by.
    /// Answers whether the camera went into or out of the free camera.
    /// </summary>
    public static bool Frame(FreeCamera view, WorldCamera camera, bool up, bool reading, float seconds, bool driving,
                             WorldThing? steered, bool open, Vector2 mouse = default)
    {
        Up = up;
        bool toggle = reading && KeyInput.Held(KeyCommand.FlyCamera);
        bool pressed = toggle && !toggleWas;
        toggleWas = toggle;
        if (pressed)
        {
            // Out: the free camera's own Ctrl+P, with or without the switch.
            if (up && camera.Mode == CameraMode.Free && camera.BackToGame(steered))
            {
                view.Roll = 0f;
                keys.Rest();
                Up = false;
                Drag(false);
                return true;
            }
            // In: the game camera's Ctrl+P, behind [qol] freecamera or --devmode, from the game camera or a cutscene.
            if (!up && open && camera.EnterFree(steered))
            {
                Up = true;
                Drag(false);
                return true;
            }
        }
        if (!up)
        {
            // The picture's camera is the game camera's (or pinned) and has no roll of its own.
            view.Roll = 0f;
            keys.Rest();
            Drag(false);
            return false;
        }
        var held = FreeKeys.None;
        if (reading)
            foreach (var (command, key) in Map)
                if (KeyInput.Held(command)) held |= key;
        Drag(driving && (held & FreeKeys.Drag) != 0);
        keys.Step(view, held, seconds, driving, mouse);
        camera.FlownTo(view);
        return false;
    }

    /// <summary>
    /// The window gained or lost the focus (ours; <c>KeyInput.LetGo</c>): the camera's held keys come
    /// to rest, as if every key had been let go — and a Ctrl+P held across it is not a press when the window
    /// comes back. The drag look ends on the next frame, when its button reads up and <see cref="DragChanged"/>
    /// tells the host to let the pointer go.
    /// </summary>
    public static void LetGo()
    {
        keys.Rest();
        toggleWas = true;
    }
}
