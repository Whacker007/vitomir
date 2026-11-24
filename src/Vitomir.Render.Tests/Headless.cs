// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using SDL;
using Vitomir.Render;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using static SDL.SDL3;

namespace Vitomir.Render.Tests;

/// <summary>
/// One headless GPU device and one open install, shared by every rendering test: SDL is initialised
/// without a video window, so these run over a remote session and in CI. If there is no install, or
/// SDL can find no GPU at all, the tests skip with a reason instead of failing.
/// </summary>
public sealed unsafe class Headless : IDisposable
{
    public Gpu? Gpu { get; }
    public GameFiles? Files { get; }
    public string? Skip { get; }

    public Headless()
    {
        // **These tests measure 1.1's pictures, so they are taken in 1.1's layout**: the interface's sheet
        // stretched to the picture, the world drawn above the status bar and no further (the program's own
        // default is the 16:9 stretch, the fitted sheet before it: `Engine.Ui.UiLayout`; on a 16:9
        // picture that is this to the float, and on a narrower one a shorter sheet). A test that looks
        // at another layout puts it in force and puts this back;
        // every test that draws is in this collection, one at a time.
        Vitomir.Engine.Ui.UiLayout.Mode = Vitomir.Engine.Ui.UiLayoutMode.Stretch;
        string? dir = GameFiles.Locate();
        if (dir is null) { Skip = "no game install (set VTMR_GAME_DIR to the folder holding Resource.nob)"; return; }
        try
        {
            SDL_SetHint(SDL_HINT_VIDEO_DRIVER, Environment.GetEnvironmentVariable("SDL_VIDEO_DRIVER") ?? "");
            if (!SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO)) { Skip = "SDL_Init failed: " + SDL_GetError(); return; }
            Gpu = new Gpu(null);
            Files = new GameFiles(dir);
        }
        catch (Exception e)
        {
            Skip = "no GPU for headless rendering: " + e.Message;
        }
    }

    /// <summary>The frame a level shows at its player start, as RGBA pixels, top row first.</summary>
    public byte[] Frame(string level, int width = 320, int height = 180, float seconds = 0f,
                        Action<Location>? prepare = null, Action<FrameRenderer>? settings = null,
                        string? scene = null, FreeCamera? camera = null)
    {
        var location = Location.Load(Files!, level, scene: scene);
        prepare?.Invoke(location);
        using var frame = new FrameRenderer(Gpu!, location);
        settings?.Invoke(frame);
        if (seconds > 0) frame.Game.Advance(seconds);
        return Shoot(frame, width, height, camera ?? location.Camera());
    }

    /// <summary>
    /// A still of <paramref name="frame"/>: the frame's game side first (its walk, sky and poses, a step of
    /// <paramref name="seconds"/>; <c>WorldFrame.Frame</c>, as the program runs it), then the picture — what
    /// <c>FrameRenderer.RenderToImage</c> did on its own until then, when the renderer stopped writing the game.
    /// </summary>
    public static byte[] Shoot(FrameRenderer frame, int width, int height, FreeCamera camera, float seconds = 0f)
    {
        frame.Game.Frame(camera, width, height, seconds);
        return frame.RenderToImage(width, height, camera);
    }

    /// <summary>How bright the frame is on average, 0 to 1 — enough to tell "drew something" from "black".</summary>
    public static float Mean(byte[] rgba)
    {
        long sum = 0;
        for (int i = 0; i < rgba.Length; i += 4) sum += rgba[i] + rgba[i + 1] + rgba[i + 2];
        return (float)sum / (rgba.Length / 4 * 3) / 255f;
    }

    /// <summary>The share of pixels where two frames differ at all.</summary>
    public static float Differing(byte[] a, byte[] b)
    {
        int differ = 0;
        for (int i = 0; i < a.Length; i += 4)
            if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2]) differ++;
        return (float)differ / (a.Length / 4);
    }

    public void Dispose()
    {
        Files?.Dispose();
        Gpu?.Dispose();
        if (Gpu is not null) SDL_Quit();
    }
}

/// <summary>Shares one device and install across the whole run.</summary>
[CollectionDefinition("headless")]
public sealed class HeadlessCollection : ICollectionFixture<Headless>;
