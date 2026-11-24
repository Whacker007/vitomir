// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A frame with no world in it, as the module says it (the game half of the screen renderer; the black is
// the host's renderer's, Vitomir.Platform.Render.Classic.ClassicScreenRenderer). The front end is drawn this way — state 2 runs
// the interface and not the world (flags 0x19) — and so are the title screen and the loading
// plaque between a menu and a level.
using Vitomir.Engine.World;

namespace Vitomir.Classic;

/// <summary>Black, and <see cref="After"/> emitted over it for the host's renderer to draw (<see cref="IClassicScreen"/>).</summary>
public sealed class ScreenFrame(IClassicScreen screen)
{
    readonly FreeCamera camera = new();
    readonly ClassicOver over = new();

    /// <summary>What goes on the black: the menus, the floating texts, the pointer.</summary>
    public IFrameLayer? After { get; set; }

    ClassicOver Say(int width, int height)
    {
        over.Clear();
        // Nothing in the interface's passes reads the simulation; there is none to give them.
        After?.Emit(over, width, height, null, camera, null!);
        return over;
    }

    /// <summary>The screen drawn into a host's picture.</summary>
    public void Render(in GpuPicture picture) => screen.Draw(picture, Say(picture.Width, picture.Height));

    /// <summary>One frame with no window, as RGBA with the top row first.</summary>
    public byte[] RenderToImage(int width, int height) => screen.Still(width, height, Say(width, height));
}
