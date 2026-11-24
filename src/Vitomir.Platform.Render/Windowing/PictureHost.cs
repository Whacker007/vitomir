// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The picture's rectangle in borderless full screen (ours; the rules are
// `Platform.Windowing.Picture`). This file is the part that asks SDL, and it is the one place the loops go through
// for the three things that must agree about where the picture is:
//   * the window's size and every pointer coordinate — the picture's, so that everything the game lays
//     out, picks and hovers behaves as if the window were the picture;
//   * the frame — drawn into a target the picture's size and copied to the middle of the window, black
//     around it;
//   * the pointer — kept inside the picture while the window has the keyboard.
// With no bars (any other window mode, a size picked of the desktop's own shape, a replay) every call
// here is SDL's own and the frame is drawn straight to the window, as it always was.
using SDL;
using Vitomir.Platform.Windowing;
using static SDL.SDL3;

namespace Vitomir.Platform.Render.Windowing;

/// <summary>
/// The picture, where the loops meet SDL (ours):
/// <list type="bullet">
/// <item><see cref="Size"/> in place of <c>SDL_GetWindowSize</c>, <see cref="Poll"/> in place of
/// <c>SDL_PollEvent</c>, <see cref="MouseState"/> in place of <c>SDL_GetMouseState</c> and
/// <see cref="Warp"/> in place of <c>SDL_WarpMouseInWindow</c>: the picture's size and the picture's
/// pixels. A press or a wheel turn on a bar is dropped, with the press's release; a movement there
/// is held to the picture's edge. The translation is made before the input tape writes an event, so a
/// recording made with bars replays in a plain window the picture's size.</item>
/// <item><see cref="Acquire"/> in place of <c>SDL_WaitAndAcquireGPUSwapchainTexture</c> and
/// <see cref="Submit"/> in place of <c>SDL_SubmitGPUCommandBuffer</c>: with bars, the frame's target is
/// a texture the picture's size and <see cref="Submit"/> first clears the window to black and copies it
/// to the middle, pixel for pixel.</item>
/// <item><see cref="Frame"/> once a frame: the pointer kept inside the picture
/// (<c>SDL_SetWindowMouseRect</c>) while the window has the keyboard and there are bars, and let go
/// when it loses the focus, when the mode is left and when there are none.</item>
/// <item><see cref="WindowShot"/> (a harness's <c>winshot:</c> step): the whole window as it was last
/// presented, bars and all — a screenshot is the picture alone.</item>
/// </list>
/// </summary>
public static unsafe class PictureHost
{
    /// <summary>The game's window: the one <see cref="FullScreenHost"/> has.</summary>
    static SDL_Window* Window => FullScreenHost.Window;

    /// <summary>
    /// **A replay's picture, drawn at its recording's size and shown smaller** (ours):
    /// the size the game is to believe its window has — the recording's, which every click and pick is placed by —
    /// while the window itself is small and out of the way. Everything the game reads goes through
    /// <see cref="Size"/> and a resize's numbers, so it sees this size; the frame is drawn into a target this size
    /// and copied shrunk to the window. Null: no shrinking (not a replay, or one being watched).
    /// </summary>
    public static (int Width, int Height)? Shrunk { get; set; }

    /// <summary>The window a shrunk replay's picture is shown in (<see cref="Picture.ShrunkWindow"/>).</summary>
    public static (int Width, int Height) ShrunkWindow(int w, int h) => Picture.ShrunkWindow(w, h);

    /// <summary>The picture on a window of <paramref name="w"/> x <paramref name="h"/> (window units or pixels), or null when it is the whole of it.</summary>
    static PictureRect? RectOf(int w, int h)
    {
        if (FullScreenHost.Picked() is not { } picked) return null;
        var size = new ScreenMode(w, h);
        var r = Picture.RectOn(size, picked);
        return Picture.HasBars(size, r) ? r : null;
    }

    /// <summary>The picture's rectangle in the window's own units when there are bars; null when the picture is the whole window.</summary>
    public static PictureRect? Bars()
    {
        if (Window == null) return null;
        int w = 0, h = 0;
        return SDL_GetWindowSize(Window, &w, &h) ? RectOf(w, h) : null;
    }

    // ---- the window's size and the pointer -------------------------------------------------------

    /// <summary>In place of <c>SDL_GetWindowSize</c>: the picture's size, which is the window's when there are no bars.</summary>
    public static bool Size(SDL_Window* on, int* w, int* h)
    {
        if (on == Window && on != null && Shrunk is { } shrunk) { (*w, *h) = shrunk; return true; }
        if (!SDL_GetWindowSize(on, w, h)) return false;
        if (on == Window && on != null && RectOf(*w, *h) is { } r) (*w, *h) = (r.Width, r.Height);
        return true;
    }

    /// <summary>The buttons whose press on a bar was dropped, a bit each: their release is dropped too.</summary>
    static uint dropped;

    /// <summary>The mouse id the harness's own pointer events carry (<see cref="ScriptedInput"/>): "HRNS".</summary>
    public const uint HarnessMouse = 0x48524E53;

    /// <summary>
    /// **Only the harness has the pointer** (ours): set for a run a harness drives (<c>--ui-click</c>, <c>--ui-type</c>), whose
    /// pointer is the harness's alone. The system's own pointer events — the real mouse moving over the window, its buttons
    /// and wheel, the pointer leaving the window — are not heard then: a test's run sharing the desktop with others had the
    /// real pointer move off and leave the window between a harness press and its release, and the click was lost.
    /// </summary>
    public static bool HarnessPointer { get; set; }

    /// <summary>Where the game last put the pointer (<see cref="Warp"/>), in window pixels: the system's motion there is heard.</summary>
    static (float X, float Y)? warpedTo;

    /// <summary>
    /// Whether an event is the system's pointer and not heard in a harness's run (<see cref="HarnessPointer"/>): not the
    /// harness's own, and not the motion that the game's own warp of the pointer makes.
    /// </summary>
    static bool Unheard(in SDL_Event e)
    {
        if (!SystemPointer(e)) return false;
        if (e.Type == SDL_EventType.SDL_EVENT_MOUSE_MOTION && warpedTo is { } w
            && MathF.Abs(e.motion.x - w.X) < 1f && MathF.Abs(e.motion.y - w.Y) < 1f)
        {
            warpedTo = null;
            return false;
        }
        return true;
    }

    /// <summary>Whether an event is the system's pointer, not the harness's (<see cref="HarnessPointer"/>).</summary>
    public static bool SystemPointer(in SDL_Event e) => e.Type switch
    {
        SDL_EventType.SDL_EVENT_MOUSE_MOTION => e.motion.which != (SDL_MouseID)HarnessMouse,
        SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN or SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP => e.button.which != (SDL_MouseID)HarnessMouse,
        SDL_EventType.SDL_EVENT_MOUSE_WHEEL => e.wheel.which != (SDL_MouseID)HarnessMouse,
        SDL_EventType.SDL_EVENT_WINDOW_MOUSE_LEAVE => true,
        _ => false,
    };

    /// <summary>In place of <c>SDL_PollEvent</c>: the next event the game is to hear, its pointer in the picture's pixels.</summary>
    public static bool Poll(SDL_Event* e)
    {
        while (SDL_PollEvent(e))
        {
            WindowReportHost.Hear(*e);          // SDL's news of the displays and of full screen, into the log
            if (Hear(e)) return true;
        }
        return false;
    }

    /// <summary>
    /// One event made the picture's: a pointer coordinate less the bar's offset, a new window size as
    /// the picture's. False for an event the game is not to hear: a press or a wheel turn on a bar,
    /// and the release of such a press.
    /// </summary>
    static bool Hear(SDL_Event* e)
    {
        if (HarnessPointer && Unheard(*e)) return false;
        switch (e->Type)
        {
            case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
                if (Bars() is { } m) (e->motion.x, e->motion.y) = Picture.FromWindow(m, e->motion.x, e->motion.y);
                return true;
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
            {
                uint bit = 1u << (e->button.button & 31);
                dropped &= ~bit;
                if (Bars() is not { } b) return true;
                if (!Picture.On(b, e->button.x, e->button.y)) { dropped |= bit; return false; }
                (e->button.x, e->button.y) = Picture.FromWindow(b, e->button.x, e->button.y);
                return true;
            }
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
            {
                uint bit = 1u << (e->button.button & 31);
                if ((dropped & bit) != 0) { dropped &= ~bit; return false; }
                if (Bars() is { } b) (e->button.x, e->button.y) = Picture.FromWindow(b, e->button.x, e->button.y);
                return true;
            }
            case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
                if (Bars() is not { } wheel) return true;
                if (!Picture.On(wheel, e->wheel.mouse_x, e->wheel.mouse_y)) return false;
                (e->wheel.mouse_x, e->wheel.mouse_y) = Picture.FromWindow(wheel, e->wheel.mouse_x, e->wheel.mouse_y);
                return true;
            case SDL_EventType.SDL_EVENT_WINDOW_RESIZED or SDL_EventType.SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED:
                // The size a recording keeps and a replay's window takes: the picture's.
                if (SDL_GetWindowFromID(e->window.windowID) == Window && Shrunk is { } shrunk)
                    (e->window.data1, e->window.data2) = shrunk;
                else if (SDL_GetWindowFromID(e->window.windowID) == Window && RectOf(e->window.data1, e->window.data2) is { } r)
                    (e->window.data1, e->window.data2) = (r.Width, r.Height);
                return true;
            default:
                return true;
        }
    }

    /// <summary>In place of <c>SDL_GetMouseState</c>: the pointer in the picture's pixels, held to the picture while it is on a bar.</summary>
    public static SDL_MouseButtonFlags MouseState(float* x, float* y)
    {
        float mx = 0, my = 0;
        var buttons = SDL_GetMouseState(&mx, &my);
        if (Bars() is { } r) (mx, my) = Picture.FromWindow(r, mx, my);
        if (x != null) *x = mx;
        if (y != null) *y = my;
        return buttons;
    }

    /// <summary>In place of <c>SDL_WarpMouseInWindow</c>: the pointer put at a picture pixel.</summary>
    public static void Warp(SDL_Window* on, float x, float y)
    {
        if (on == Window && Bars() is { } r) (x, y) = Picture.ToWindow(r, x, y);
        warpedTo = (x, y);                              // the motion the warp makes is the game's own, heard in a harness's run too
        SDL_WarpMouseInWindow(on, x, y);
    }

    /// <summary>A picture pixel as the window's (a harness's <c>@template</c> aim, which is worked out on the picture and pushed as a window pixel).</summary>
    public static (float X, float Y) ToWindow(float x, float y) => Bars() is { } r ? Picture.ToWindow(r, x, y) : (x, y);

    /// <summary>
    /// The picture changed with the window's size as it was (the Display screen picked another size in
    /// borderless full screen): a resize is put in SDL's queue, so the loops and a recording hear the
    /// picture's new size as they hear any window's.
    /// </summary>
    public static void Changed()
    {
        if (Window == null) return;
        int w = 0, h = 0;
        if (!SDL_GetWindowSize(Window, &w, &h)) return;
        SDL_Event e = default;
        e.window.type = SDL_EventType.SDL_EVENT_WINDOW_RESIZED;
        e.window.windowID = SDL_GetWindowID(Window);
        e.window.data1 = w;
        e.window.data2 = h;
        SDL_PushEvent(&e);
    }

    // ---- the pointer kept inside -----------------------------------------------------------

    static PictureRect? kept;

    /// <summary>
    /// Once a frame: the pointer is kept inside the picture while there are bars and the window has
    /// the keyboard (its input focus), and let go otherwise — the focus lost (Alt+Tab), the mode left,
    /// no bars. Asked of SDL only when that changes, and said in the log.
    /// </summary>
    public static void Frame()
    {
        if (Window == null) return;
        bool focus = (SDL_GetWindowFlags(Window) & SDL_WindowFlags.SDL_WINDOW_INPUT_FOCUS) != 0;
        var bars = Bars();
        var want = focus ? bars : null;
        if (want == kept) return;
        if (want is { } r)
        {
            var rect = new SDL_Rect { x = r.X, y = r.Y, w = r.Width, h = r.Height };
            if (SDL_SetWindowMouseRect(Window, &rect)) FullScreenHost.Say($"window: the pointer is kept inside the picture, {r}");
            else FullScreenHost.Say($"window: the pointer could not be kept inside the picture ({SDL_GetError()})");
        }
        else
        {
            SDL_SetWindowMouseRect(Window, null);
            FullScreenHost.Say($"window: the pointer is let go ({(bars is null ? "the picture is the whole window" : "the window lost the focus")})");
        }
        kept = want;
    }

    // ---- the frame ---------------------------------------------------------------------------------

    static Gpu? device;
    static SDL_GPUTexture* picture;
    static int pictureWidth, pictureHeight;
    /// <summary>This frame's swapchain texture and the picture's place on it, between <see cref="Acquire"/> and <see cref="Submit"/>.</summary>
    static SDL_GPUTexture* swapNow;
    static PictureRect placeNow;
    /// <summary>The window's size in pixels and the picture's place on it when a frame was last presented through the picture.</summary>
    static (int Width, int Height, PictureRect Place)? last;

    /// <summary>A harness's whole-window picture is wanted (<c>winshot:</c>): every frame goes through the picture's target, bars or none.</summary>
    public static bool KeepWhole { get; set; }

    /// <summary>The swapchain's size and the picture's place on it as last drawn, for the log (<see cref="WindowReportHost"/>).</summary>
    public static string Drawn { get; private set; } = "nothing drawn yet";

    /// <summary>
    /// In place of <c>SDL_WaitAndAcquireGPUSwapchainTexture</c>: the frame's target and its size. With
    /// bars that is a texture the picture's size, and <see cref="Submit"/> copies it to the window;
    /// with none it is the swapchain's own texture.
    /// </summary>
    public static bool Acquire(Gpu gpu, SDL_GPUCommandBuffer* cmd, SDL_Window* on, SDL_GPUTexture** target, uint* width, uint* height)
    {
        swapNow = null;
        SDL_GPUTexture* swap; uint sw, sh;
        if (!SDL_WaitAndAcquireGPUSwapchainTexture(cmd, on, &swap, &sw, &sh)) { *target = null; return false; }
        *target = swap; *width = sw; *height = sh;
        if (swap == null || on != Window) return true;
        var whole = new PictureRect(0, 0, (int)sw, (int)sh);
        if (Shrunk is { } shrunk)
        {
            // A shrunk replay's: the picture its recording's size, copied to the window fitted and centred.
            double s = Math.Min(sw / (double)shrunk.Width, sh / (double)shrunk.Height);
            int fw = Math.Max(1, (int)Math.Round(shrunk.Width * s)), fh = Math.Max(1, (int)Math.Round(shrunk.Height * s));
            var fitted = new PictureRect(((int)sw - fw) / 2, ((int)sh - fh) / 2, fw, fh);
            Drawn = $"a swapchain of {sw}x{sh}, a replay's picture of {shrunk.Width}x{shrunk.Height} shown at {fitted}";
            Target(gpu, shrunk.Width, shrunk.Height);
            swapNow = swap; placeNow = fitted;
            last = ((int)sw, (int)sh, fitted);
            *target = picture; *width = (uint)shrunk.Width; *height = (uint)shrunk.Height;
            return true;
        }
        var place = RectOf((int)sw, (int)sh);
        Drawn = place is { } p ? $"a swapchain of {sw}x{sh}, the picture {p}" : $"a swapchain of {sw}x{sh}, the picture all of it";
        if (place is null && !KeepWhole) { last = null; return true; }
        var r = place ?? whole;
        Target(gpu, r.Width, r.Height);
        swapNow = swap; placeNow = r;
        last = ((int)sw, (int)sh, r);
        *target = picture; *width = (uint)r.Width; *height = (uint)r.Height;
        return true;
    }

    /// <summary>The picture's target, made again when its size or the device changes.</summary>
    static void Target(Gpu gpu, int w, int h)
    {
        if (picture != null && (pictureWidth, pictureHeight) == (w, h) && device == gpu) return;
        if (picture != null && device is not null) SDL_ReleaseGPUTexture(device.Device, picture);
        device = gpu;
        picture = gpu.CreateTarget(w, h, gpu.ColorFormat,
            SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);
        (pictureWidth, pictureHeight) = (w, h);
    }

    /// <summary>In place of <c>SDL_SubmitGPUCommandBuffer</c>: with bars, the picture is first copied to its place on the window and the rest of the window made black.</summary>
    public static void Submit(SDL_GPUCommandBuffer* cmd)
    {
        if (swapNow != null) Copy(cmd, swapNow, placeNow);
        swapNow = null;
        SDL_SubmitGPUCommandBuffer(cmd);
    }

    /// <summary>The picture's texture onto <paramref name="onto"/> at <paramref name="place"/>, one pixel for one, everything else cleared to black.</summary>
    static void Copy(SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* onto, PictureRect place)
    {
        var blit = new SDL_GPUBlitInfo
        {
            source = new SDL_GPUBlitRegion { texture = picture, w = (uint)pictureWidth, h = (uint)pictureHeight },
            destination = new SDL_GPUBlitRegion { texture = onto, x = (uint)place.X, y = (uint)place.Y, w = (uint)place.Width, h = (uint)place.Height },
            load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
            clear_color = new SDL_FColor { r = 0, g = 0, b = 0, a = 1 },
            // One pixel for one with bars; a shrunk replay's picture is smaller on the window, and smoothed.
            filter = place.Width == pictureWidth && place.Height == pictureHeight ? SDL_GPUFilter.SDL_GPU_FILTER_NEAREST : SDL_GPUFilter.SDL_GPU_FILTER_LINEAR,
        };
        SDL_BlitGPUTexture(cmd, &blit);
    }

    /// <summary>A download as RGBA: red and blue exchanged when the GPU's colour is BGRA (as the controller's <c>Screenshots.Rgba</c>).</summary>
    static byte[] Rgba(byte[] pixels, bool bgra)
    {
        if (!bgra) return pixels;
        for (int i = 0; i + 3 < pixels.Length; i += 4) (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
        return pixels;
    }

    /// <summary>
    /// The picture as last drawn, RGBA at its own size (ours, a frame dump's: <c>--frame-dump</c> in a window's loop), read
    /// back from the frame the screen got, so nothing of the game is drawn again for it. Null when no frame has gone through
    /// the picture's target: a dump sets <see cref="KeepWhole"/> so that every frame does.
    /// </summary>
    public static byte[]? PicturePixels(out int width, out int height)
    {
        (width, height) = (pictureWidth, pictureHeight);
        if (device is not { } gpu || picture == null || last is null) return null;
        return Rgba(gpu.Download(picture, pictureWidth, pictureHeight), gpu.ColourIsBgra);
    }

    /// <summary>
    /// A harness's <c>winshot:path.png</c>: the whole window as it was last presented — the picture at
    /// its place and the bars — put together by the same copy the screen gets. False, and said, when
    /// no frame has gone through the picture yet.
    /// </summary>
    public static bool WindowShot(string path)
    {
        if (device is not { } gpu || picture == null || last is not { } was)
        {
            Console.WriteLine($"  harness: no whole-window picture for {path} (nothing presented yet)");
            return false;
        }
        var whole = gpu.CreateTarget(was.Width, was.Height, gpu.ColorFormat, SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET);
        var cmd = SDL_AcquireGPUCommandBuffer(gpu.Device);
        Copy(cmd, whole, was.Place);
        SDL_SubmitGPUCommandBuffer(cmd);
        var pixels = gpu.Download(whole, was.Width, was.Height);
        SDL_ReleaseGPUTexture(gpu.Device, whole);
        Png.Write(path, was.Width, was.Height, Rgba(pixels, gpu.ColourIsBgra));
        Console.WriteLine($"  harness: the whole window, {was.Width}x{was.Height} with the picture {was.Place}, written to {path}");
        return true;
    }
}
