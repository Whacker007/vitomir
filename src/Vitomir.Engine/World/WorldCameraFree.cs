// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The free camera's way in and its flown eye.
namespace Vitomir.Engine.World;

public sealed partial class WorldCamera
{
    /// <summary>
    /// **The developer's free camera** (Ctrl+P, behind the developer switch — options bit 0x80,
    /// not a network client): the update becomes the free camera's, the steered character's
    /// first-person bit is cleared (render flag <c>0x80</c>), the pointer is shown and the mode
    /// is 3. 1.1 reads it from the game camera's keys, from a cutscene's and
    /// from the free camera's own (where it changes nothing); the eye and the angles stay as
    /// they stand, so the free camera starts from the last picture. False when the mode is none of those.
    /// </summary>
    public bool EnterFree(WorldThing? steered)
    {
        if (Mode is not (CameraMode.Game or CameraMode.Cutscene)) return false;
        if (steered is not null) steered.RenderFlags &= ~RenderFirstPerson;
        CursorShown = true;
        Mode = CameraMode.Free;
        return true;
    }

    /// <summary>
    /// Where the free camera's keys have flown it (the keys write the eye into the camera
    /// and the angles through the angle set): Vitomir flies the picture's own camera
    /// (<see cref="FreeCamera"/>, which the frame is drawn from while this is free) and copies it here, so
    /// what reads the camera — the way back to the game camera keeps the yaw — reads where it
    /// is. Only in the free camera.
    /// </summary>
    public void FlownTo(FreeCamera view)
    {
        if (Mode != CameraMode.Free) return;
        Eye = view.Position;
        Angles = new(Wrap(view.Pitch), Wrap(view.Roll), Wrap(view.Yaw - 90f));
    }
}
