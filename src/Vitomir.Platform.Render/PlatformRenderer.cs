// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using SDL;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Render.Characters;
using Vitomir.Platform.Render.Level;
using Vitomir.Platform.Render.Modern;
using Vitomir.Platform.Render.Overlay;
using static SDL.SDL3;

namespace Vitomir.Platform.Render;

/// <summary>
/// How the renderer is made: the GPU driver (<c>vulkan</c>, <c>direct3d12</c>; empty for the platform's
/// default, as the old program's <c>--gpu</c>), the graphics quality preset (<c>low</c>, <c>medium</c>,
/// <c>high</c>, <c>ultra</c>, <c>auto</c>; empty for the GPU's), whether to wait for the screen, and the
/// picture's size: with <see cref="PictureWidth"/> and <see cref="PictureHeight"/> set the world is drawn at
/// that size and shown as large as fits the window, centred, black around it (a borderless window
/// over the whole desktop with the picture at a size he picks); 0 draws at the window's own size.
/// </summary>
/// <param name="Look">The modern path's look (<c>morning</c>, <c>dusk</c>, …; <c>dusk</c> for a night), or empty for the default.</param>
/// <param name="CharacterTextureLimit">an earlier build: the largest side a character's texture is uploaded at, 1024, 2048 or 4096
/// (<see cref="Characters.CharacterTextureSize"/>; the player's <c>characterTextureLimit</c>); 0 for the default, 2048.</param>
public sealed record RendererOptions(string Driver = "", string Quality = "", bool VSync = true, int PictureWidth = 0, int PictureHeight = 0, string Look = "",
                                     int CharacterTextureLimit = 0, Characters.PeopleSkinning? PeopleSkinning = null);

/// <summary>
/// The platform's renderer: the old program's modern renderer brought over
/// whole. The program makes the window and owns its events; this draws into it.
/// A level package is given once (<see cref="LoadLevel"/>) and streamed round the eye as the old program
/// streams it; each frame's <see cref="FrameView"/> gives the view, the characters posed, the reticle, the
/// time of day and two lines of text. It decides nothing about the world.
/// </summary>
public sealed unsafe class PlatformRenderer : IDisposable
{
    readonly Gpu gpu;
    readonly SDL_Window* window;
    readonly RendererOptions options;
    readonly Presenter presenter;
    readonly CharacterPass characters;
    readonly OverlayPass overlay;
    readonly ReachPass reach;
    readonly AwarenessPass awareness;   // the developer's awareness overlay
    readonly HitVolumePass hitVolumes;  // the developer's hit volumes overlay
    readonly CuePass cues;              // the flurry's cue
    readonly SwingTrailPass swingTrail; // the developer's swing trail
    readonly SwingArcPass swingArc;     // the swing's arc
    readonly CellLooks looks;
    readonly bool ownsSdl;
    LevelPass? level;
    SDL_GPUTexture* headless;
    readonly int headlessWidth, headlessHeight;
    bool drawn;

    PlatformRenderer(Gpu gpu, SDL_Window* window, RendererOptions options, int headlessWidth, int headlessHeight, bool ownsSdl)
    {
        this.gpu = gpu;
        this.window = window;
        this.options = options;
        this.ownsSdl = ownsSdl;
        (this.headlessWidth, this.headlessHeight) = (headlessWidth, headlessHeight);
        looks = new CellLooks { Quality = string.IsNullOrWhiteSpace(options.Quality) ? null : options.Quality, Look = string.IsNullOrWhiteSpace(options.Look) ? null : options.Look };
        if (!ModernQualities.Check(looks.Quality)) looks.Quality = null;
        presenter = new Presenter(gpu);
        characters = new CharacterPass(gpu) { TextureLimit = CharacterTextureSize.Limit(options.CharacterTextureLimit), Skinning = options.PeopleSkinning ?? PeopleSkinning.Default };
        overlay = new OverlayPass(gpu);
        reach = new ReachPass(gpu);
        awareness = new AwarenessPass(gpu);
        hitVolumes = new HitVolumePass(gpu);
        cues = new CuePass(gpu);
        swingTrail = new SwingTrailPass(gpu);
        swingArc = new SwingArcPass(gpu);
        if (window == null)
            headless = gpu.CreateTarget(headlessWidth, headlessHeight, gpu.ColorFormat,
                                        SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);
        else if (!options.VSync) gpu.Unthrottle();
    }

    /// <summary>A renderer drawing into the program's window (an <c>SDL_Window*</c>; SDL's video already initialised by the program).</summary>
    public static PlatformRenderer Create(nint sdlWindow, RendererOptions options)
    {
        if (sdlWindow == 0) throw new ArgumentException("no window", nameof(sdlWindow));
        var gpu = GpuDrivers.Open((SDL_Window*)sdlWindow, Asked(options.Driver), debug: false, Console.WriteLine);
        try { return new PlatformRenderer(gpu, (SDL_Window*)sdlWindow, options, 0, 0, ownsSdl: false); }
        catch { gpu.Dispose(); throw; }
    }

    /// <summary>
    /// A renderer with no window, drawing into a <paramref name="width"/> × <paramref name="height"/> target
    /// (tests, stills). Initialises SDL's video when nothing has (set <c>SDL_VIDEO_DRIVER=offscreen</c> on a
    /// machine with no screen). Throws when no GPU device can be made.
    /// </summary>
    public static PlatformRenderer CreateHeadless(int width, int height, RendererOptions options)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "a headless picture needs a size");
        bool init = (SDL_WasInit(SDL_InitFlags.SDL_INIT_VIDEO) & SDL_InitFlags.SDL_INIT_VIDEO) == 0;
        if (init && !SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO)) throw new SdlException("SDL_Init");
        Gpu? gpu = null;
        try
        {
            gpu = GpuDrivers.Open(null, Asked(options.Driver), debug: false, Console.WriteLine);
            return new PlatformRenderer(gpu, null, options, width, height, ownsSdl: init);
        }
        catch
        {
            gpu?.Dispose();
            if (init) SDL_Quit();
            throw;
        }
    }

    static string? Asked(string driver) => string.IsNullOrWhiteSpace(driver) ? null : driver.Trim();

    /// <summary>A character part's texture by name (the program's assets); a part whose texture is not there yet is drawn white and asked for again next frame.</summary>
    public Func<string, TextureImage?>? CharacterTextures
    {
        get => characters.Textures;
        set => characters.Textures = value;
    }

    /// <summary>
    /// The GPU memory the characters' textures take so far, bytes (RGBA8 with their mips, each no larger than
    /// <see cref="RendererOptions.CharacterTextureLimit"/>), and that limit.
    /// </summary>
    public long CharacterTextureBytes => characters.TextureBytes;
    /// <inheritdoc cref="CharacterTextureBytes"/>
    public int CharacterTextureLimit => characters.TextureLimit;

    /// <summary>The last frame's characters' cost on the CPU (the skinning, the upload; the people skinned, reused, out of view).</summary>
    public CharacterCost CharacterCost => characters.LastCost;

    /// <summary>How the people are skinned when there are several (<see cref="RendererOptions.PeopleSkinning"/>; can change between frames).</summary>
    public PeopleSkinning PeopleSkinning { get => characters.Skinning; set => characters.Skinning = value; }

    /// <summary>First person's cut (<see cref="CharacterBatch.FirstPersonCut"/>), a tuning aid for the stills.</summary>
    internal float FirstPersonCut { get => characters.FirstPersonCut; set => characters.FirstPersonCut = value; }

    /// <summary>Coverage-keeping mips for people's cut-outs (on), a comparison aid for the stills.</summary>
    internal bool KeepCoverage { get => characters.KeepCoverage; set => characters.KeepCoverage = value; }

    /// <summary>1.1's art for a cursor (the program's, read from the install), or null: the freed pointer is then a plain arrow.</summary>
    public Func<CursorId, TextureImage?>? CursorArt
    {
        get => overlay.CursorArt;
        set => overlay.CursorArt = value;
    }

    /// <summary>The GPU driver in use (<c>vulkan</c>, <c>direct3d12</c>, …).</summary>
    public string Driver => gpu.Driver;

    /// <summary>
    /// Opens a cooked level package for drawing, in place of any before: a streamed
    /// package's cells and far versions are read round the eye from the first <see cref="Draw"/> on, as the
    /// old program reads them; a version-1 package's glTF is read whole.
    /// </summary>
    public void LoadLevel(string packageFolder)
    {
        SDL_WaitForGPUIdle(gpu.Device);
        level?.Dispose();
        level = null;
        level = new LevelPass(gpu, RenderLevel.Load(Path.GetFullPath(packageFolder.TrimEnd('/', '\\'))), looks);
    }

    /// <summary>
    /// How many <see cref="Draw"/>s of one unchanged view a still needs before <see cref="SaveScreenshot"/>: two
    /// when the screen's reflections are on (they read the frame before), else one, as the old program's stills.
    /// </summary>
    public int StillFrames => level?.StillFrames ?? 1;

    /// <summary>The picture's size in pixels: <see cref="RendererOptions.PictureWidth"/> × <see cref="RendererOptions.PictureHeight"/>, else the window's (or the headless target's).</summary>
    public (int Width, int Height) PixelSize => Picture(WindowPixels());

    /// <summary>Where the picture lands in the window's pixels (all of it, unless the picture's size was given and differs from the window's shape).</summary>
    public (int X, int Y, int Width, int Height) PictureRect
    {
        get
        {
            var (ww, wh) = WindowPixels();
            var (pw, ph) = Picture((ww, wh));
            return Presenter.Fit(pw, ph, ww, wh);
        }
    }

    (int Width, int Height) WindowPixels()
    {
        if (window == null) return (headlessWidth, headlessHeight);
        int w, h;
        return SDL_GetWindowSizeInPixels(window, &w, &h) ? (w, h) : (0, 0);
    }

    (int Width, int Height) Picture((int Width, int Height) target) =>
        options.PictureWidth > 0 && options.PictureHeight > 0 ? (options.PictureWidth, options.PictureHeight) : target;

    /// <summary>
    /// Draws a frame: the level from <see cref="FrameView.View"/> with <see cref="FrameView.Characters"/> skinned
    /// and lit in it, the sun at <see cref="FrameView.TimeOfDay"/> (else the level's own), the reticle, the
    /// status line and the banner; then <paramref name="screen"/> over it all when one is open (an interface screen's
    /// draw list, laid out for <see cref="PixelSize"/>); then shows it. Nothing is drawn but black (and the screen)
    /// before a level is loaded.
    /// </summary>
    public void Draw(FrameView frame, Interface.DrawList? screen = null)
    {
        var cmd = SDL_AcquireGPUCommandBuffer(gpu.Device);
        if (cmd == null) throw new SdlException("SDL_AcquireGPUCommandBuffer");
        SDL_GPUTexture* target;
        int tw, th;
        if (window != null)
        {
            SDL_GPUTexture* swap;
            uint sw, sh;
            if (!SDL_WaitAndAcquireGPUSwapchainTexture(cmd, window, &swap, &sw, &sh) || swap == null || sw == 0 || sh == 0)
            {
                SDL_SubmitGPUCommandBuffer(cmd);   // minimised, or the swapchain is being made again
                return;
            }
            target = swap;
            (tw, th) = ((int)sw, (int)sh);
        }
        else
        {
            target = headless;
            (tw, th) = (headlessWidth, headlessHeight);
        }

        var (pw, ph) = Picture((tw, th));
        var picture = presenter.SceneTarget(pw, ph);
        if (level is not null)
        {
            characters.Prepare(cmd, frame.Characters, new SkinView(frame.View, ph > 0 ? pw / (float)ph : 16f / 9f));   // the people in view
            level.TimeOfDay(frame.TimeOfDay);
            level.Draw(cmd, picture, pw, ph, FreeCamera.From(frame.View), frame.Seconds, characters.Extra);
            // The reach highlight: in the world, against the scene's depth, under the overlay.
            reach.Draw(cmd, picture, level.Depth, pw, ph, frame);
            awareness.Draw(cmd, picture, level.Depth, pw, ph, frame);
            hitVolumes.Draw(cmd, picture, level.Depth, pw, ph, frame);
            cues.Draw(cmd, picture, level.Depth, pw, ph, frame);
            swingArc.Draw(cmd, picture, level.Depth, pw, ph, frame);   // under the developer's trail
            swingTrail.Draw(cmd, picture, level.Depth, pw, ph, frame);
        }
        else Clear(cmd, picture);
        overlay.Lay(frame, pw, ph);
        if (screen is not null) overlay.LayScreen(screen);
        overlay.Draw(cmd, picture, pw, ph);
        presenter.Present(cmd, target, tw, th);
        SDL_SubmitGPUCommandBuffer(cmd);
        drawn = true;
    }

    static void Clear(SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* target)
    {
        var colour = new SDL_GPUColorTargetInfo
        {
            texture = target, load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
            clear_color = new SDL_FColor { r = 0, g = 0, b = 0, a = 1 },
        };
        SDL_EndGPURenderPass(SDL_BeginGPURenderPass(cmd, &colour, 1, null));
    }

    /// <summary>The last frame's picture (without the bars), as a PNG. Waits for the GPU.</summary>
    public void SaveScreenshot(string pngPath)
    {
        if (!drawn) throw new InvalidOperationException("nothing drawn yet");
        var rgba = Pixels(out int w, out int h);
        for (int i = 3; i < rgba.Length; i += 4) rgba[i] = 255;
        var folder = Path.GetDirectoryName(Path.GetFullPath(pngPath));
        if (folder is not null) Directory.CreateDirectory(folder);
        Png.Write(pngPath, w, h, rgba);
    }

    /// <summary>The last frame's picture as RGBA bytes, top row first (tests).</summary>
    internal byte[] Pixels(out int width, out int height)
    {
        (width, height) = presenter.SceneSize;
        var rgba = gpu.Download(presenter.SceneTarget(width, height), width, height);
        if (gpu.ColorFormat is SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_B8G8R8A8_UNORM or SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_B8G8R8A8_UNORM_SRGB)
            for (int i = 0; i < rgba.Length; i += 4) (rgba[i], rgba[i + 2]) = (rgba[i + 2], rgba[i]);
        return rgba;
    }

    public void Dispose()
    {
        SDL_WaitForGPUIdle(gpu.Device);
        level?.Dispose();
        level = null;
        overlay.Dispose();
        reach.Dispose();
        awareness.Dispose();
        hitVolumes.Dispose();
        cues.Dispose();
        swingTrail.Dispose();
        swingArc.Dispose();
        characters.Dispose();
        presenter.Dispose();
        if (headless != null) SDL_ReleaseGPUTexture(gpu.Device, headless);
        headless = null;
        gpu.Dispose();
        if (ownsSdl) SDL_Quit();
    }
}
