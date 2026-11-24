// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A host's picture drawn by the host's renderer from what the module says (what vitomir.exe's HostPicture did;
// the module's).
using Vitomir.Engine.World;

namespace Vitomir.Classic;

/// <summary>
/// **The frame controller's frames into a host's picture**: the game's frame from a camera, or a screen with no world
/// (the front end, a film), said by the module and drawn by the host's renderer into the <see cref="GpuPicture"/> the host
/// acquired. The controller calls these and names no GPU handle.
/// </summary>
public static class PictureDraws
{
    /// <summary>The game's frame drawn into it from the camera.</summary>
    public static void Draw(this in GpuPicture picture, PlaceFrame frame, FreeCamera camera) => frame.Render(picture, camera);

    /// <summary>A screen (the front end, a film) drawn into it.</summary>
    public static void Draw(this in GpuPicture picture, ScreenFrame screen) => screen.Render(picture);
}
