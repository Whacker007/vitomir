// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The mouse's clicks in the free camera: 1.1's input frame runs the click dispatcher there too —
// twice — and it hands the orders the ground, never a thing.
namespace Vitomir.Engine.World;

/// <summary>
/// **What a click does in each camera mode** — the frame input's switch on the camera's mode, with no pane
/// active. The game camera (case 1) runs the click dispatcher once; **the free camera
/// (case 3) runs it twice**, after its own keys; the first-person
/// look (case 2), a camera path (case 4) and a cutscene (case 5) never run it. The dispatcher reads the
/// buttons by their frame stamps, which are the same for both calls, so **a click in the free camera
/// is dispatched twice in the one frame**: an order given twice (the walk re-planned to the same
/// spot, a second cast order finding the caster busy, an item put down at once and then walked to),
/// a refusal's red cross flashed twice.
///
/// <para>It is the same dispatcher, so the same three bindings (the left button, the right, and the
/// third — the flag click, 1.1's middle button) go to the same orders, for the steered character
/// (the dispatcher's <c>this</c>). But <b>the thing it hands them is always
/// none</b>: the dispatcher acts on the thing the rollover pass settled on, and
/// the hover clears that every frame and picks again only in the game camera (camera mode 1).
/// So in the free camera the left button is always the ground order, the right the cast
/// at the ground, and the flag click the ground order with its Shift bit
/// — each from the pointer's ray through the free camera's own picture (the pick casts from the one
/// camera). Nothing is highlighted, and a click on a character walks to the floor behind it.</para>
///
/// <para>And <b>the BLOCKSELECT swallow is the last game-camera frame's</b>: the flag the dispatcher
/// tests first is cleared and set only inside the hover's game-camera arm, so the
/// free camera keeps whatever it was when the camera went free — a Ctrl+P with the pointer on a raising
/// door refuses every click in the free camera until the game camera picks again.</para>
///
/// <para>Which button is a click at all is the key bindings': with <c>[qol] driving</c>
/// on, the middle button in the free camera is the drag look (ours), not the flag click; with it
/// off, it is 1.1's flag click. The left and right buttons are the dispatcher's either way.
/// With nobody steered 1.1 would call the orders on a null character; Vitomir's clicks then do
/// nothing (ours).</para>
/// </summary>
public static class FreeCameraClicks
{
    /// <summary>
    /// How many times a click is dispatched to the world in a frame with the camera in
    /// <paramref name="mode"/>: once in the game camera (case 1), twice in the free camera
    /// (case 3), and never in the first-person look, on a camera path or in a cutscene.
    /// </summary>
    public static int Dispatches(CameraMode mode) => mode switch
    {
        CameraMode.Game => 1,
        CameraMode.Free => 2,
        _ => 0,
    };

    /// <summary>
    /// Whether a click can act on a thing at all (the rollover pass's thing): in
    /// the game camera; in any other mode the hover does not pick, and the click is the ground's.
    /// </summary>
    public static bool Picks(CameraMode mode) => mode == CameraMode.Game;

    /// <summary>
    /// Whether a BLOCKSELECT thing swallows the click: in the game camera, what
    /// the pick under the pointer found now; in any other mode, what the last game-camera pick left.
    /// </summary>
    public static bool Swallowed(CameraMode mode, bool pickedNow, bool lastGameCameraPick) =>
        mode == CameraMode.Game ? pickedNow : lastGameCameraPick;
}
