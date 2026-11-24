// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using SDL;
using static SDL.SDL3;

namespace Vitomir.Platform.Render.Windowing;

/// <summary>
/// **Vitomir's icon on a window** (the launcher's and the game's): the 256-pixel picture embedded here (<c>Art/vitomir_icon_256.png</c>,
/// the art's), decoded once and given to SDL, which scales it for the title bar and the task bar. The programs carry the same
/// icon (<c>Art/vitomir.ico</c>, each program's <c>ApplicationIcon</c>). It changes nothing that is drawn.
/// </summary>
public static unsafe class WindowIcon
{
    static readonly Lazy<(byte[] Rgba, int Width, int Height)?> picture = new(() =>
    {
        using var s = typeof(WindowIcon).Assembly.GetManifestResourceStream("Art/vitomir_icon_256.png");
        if (s is null) return null;
        var bytes = new byte[s.Length];
        s.ReadExactly(bytes);
        return Png.Decode(bytes, out int w, out int h) is { } rgba ? (rgba, w, h) : null;
    });

    /// <summary>Sets the icon on <paramref name="window"/>; answers whether SDL took it (a window system without icons does not).</summary>
    public static bool Set(SDL_Window* window)
    {
        if (window == null || picture.Value is not { } icon) return false;
        // RGBA bytes in memory are SDL's ABGR8888 on a little-endian machine (every one Vitomir runs on).
        fixed (byte* pixels = icon.Rgba)
        {
            var surface = SDL_CreateSurfaceFrom(icon.Width, icon.Height, SDL_PixelFormat.SDL_PIXELFORMAT_ABGR8888, (nint)pixels, icon.Width * 4);
            if (surface == null) return false;
            bool set = SDL_SetWindowIcon(window, surface);
            SDL_DestroySurface(surface);
            return set;
        }
    }
}
