// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A frame's picture as a host acquired it (was the old program's HostPicture).
using SDL;

namespace Vitomir.Platform.Render;

/// <summary>
/// **A frame's picture, as the host acquired it** (until then the old program's <c>HostPicture</c>): the command
/// buffer, the window's texture and its size. A frame controller hands it to what draws (the Classic glue's <c>Draw</c>s)
/// and back to its host to present; it never touches the GPU's handles itself, which is why they are here, on the
/// platform's renderer, and not in the controller.
/// </summary>
public unsafe readonly struct GpuPicture
{
    readonly SDL_GPUCommandBuffer* commands;
    readonly SDL_GPUTexture* target;

    /// <summary>The picture's size (what the acquire reported).</summary>
    public readonly int Width, Height;
    /// <summary>Whether there is a texture to draw into this frame.</summary>
    public readonly bool Acquired;

    /// <summary>A picture: <paramref name="commands"/> the frame's command buffer, <paramref name="target"/> the texture to draw into (may be null when none was had).</summary>
    public GpuPicture(SDL_GPUCommandBuffer* commands, SDL_GPUTexture* target, int width, int height, bool acquired)
    {
        this.commands = commands;
        this.target = target;
        Width = width;
        Height = height;
        Acquired = acquired;
    }

    /// <summary>The frame's command buffer (the host submits it).</summary>
    public SDL_GPUCommandBuffer* Commands => commands;
    /// <summary>The texture the frame is drawn into.</summary>
    public SDL_GPUTexture* Target => target;
}
