// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The frame controller's host on SDL: the window, the GPU, the events, the clock, the pointer (once in the old program, as
// `SdlClassicHost`; the platform's, under both programs' hosts, with every window mode).
using SDL;
using Vitomir.Platform.Audio;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Render.Classic;
using static SDL.SDL3;

namespace Vitomir.Platform.Render.Windowing;

/// <summary>
/// **The frame controller's host on SDL** (once in the old program; now the platform's,:
/// <c>vitomir-tools</c>' <c>SdlClassicHost</c> and <c>vitomir-platform</c>'s <c>ClassicHost</c> are this, so the two programs
/// have one window code; <see cref="IClassicHost"/>): what outlives one run of the program
///  — the next run's arguments, a message for the next main menu, the runs so far, SDL, the window
/// and the GPU (made by the first run that needs them and kept by the rest), whether the keyboard's text is on — and SDL's
/// side of every frame: the picture acquired and presented (<see cref="PictureHost"/>: the bars of borderless full screen,
/// a replay's shrunk window), the events pumped (made the platform's neutral ones, <see cref="SdlInput"/>, which is also
/// the tape's live side), the window's modes and keeping (<see cref="FullScreenHost"/>, <see cref="WindowPlaceHost"/>,
/// <see cref="WindowFrameHost"/>, <see cref="BorderlessHost"/>, <see cref="OnTopHost"/>, <see cref="WindowReportHost"/>),
/// the clock, the pointer and the focus. What differs between the programs is theirs: a harness's scripted input
/// (<see cref="Script"/>) and the sound device (<see cref="Speakers"/>).
/// </summary>
/// <para><b>The frames</b> (the pushed frame): <see cref="Frames"/> is the platform's frame loop for a game played in its
/// window, both programs': it waits as the last frame asked, gathers the events since the last frame (through the picture: the
/// pointer in its pixels, a press on a bar dropped, the window's news in the log), times the frame on SDL's clock, hands it in
/// and goes on until the game quits. It is also the services a module's loop is handed (<see cref="IServiceProvider"/>: this host,
/// as <see cref="IClassicHost"/>).</para>
public abstract unsafe class SdlHost : IClassicHost, IServiceProvider
{
    nint keptWindow;
    Gpu? keptGpu;
    ClassicRenderer? keptRenderer;
    bool sdlUp;
    readonly SdlInput input;
    readonly SdlWindowModes modes;

    protected SdlHost()
    {
        input = new SdlInput(() => keptWindow);
        modes = new SdlWindowModes(() => keptWindow);
    }

    /// <summary>
    /// **The frames with no window** (the headless loop's copy retired): a host made so opens no window
    /// for a run that asks for one (<see cref="OpenWindow"/> answers false, as for a still), and its picture each frame is a target of
    /// the run's size on the GPU, drawn and never shown (<see cref="Acquire"/>), so 1.1's frame — whose world steps only on a frame
    /// whose picture was had — runs as in a window, every frame through the one frame (<c>vitomir-platform --game classic
    /// --headless --frames n</c>). SDL's video starts with the driver the environment names, as a still's does.
    /// </summary>
    protected bool Windowless { get; init; }

    /// <summary>The windowless picture's size: the run's (its <c>--size</c>), as its window would have been asked.</summary>
    (int Width, int Height) windowlessSize;
    SDL_GPUTexture* windowlessTarget;
    (int Width, int Height) windowlessMade;

    /// <summary>The input's live side (<see cref="SdlInput"/>), the tape's.</summary>
    public IInputHost Input => input;
    /// <summary>The window's modes for the Options screen (<see cref="FullScreenHost"/>, <see cref="SdlWindowModes"/>), null with no window.</summary>
    public IWindowModes? Modes => keptWindow != 0 ? modes : null;

    /// <summary>Where the window keeps its keys when this host keeps them (the platform's Classic host, per game); none here, so the game keeps its own.</summary>
    public virtual Vitomir.Platform.Windowing.IWindowSettings? WindowSettings => null;

    /// <summary>
    /// A run that is all text: a console window of its own, or the terminal it was started from
    /// (<see cref="Windowing.TextConsole"/>, both programs' one implementation); the line saying what was done said once, in this
    /// program's log (<see cref="SayConsole"/>), and answered.
    /// </summary>
    public string? TextConsole(bool ownWindow, string? title = null)
    {
        var line = Windowing.TextConsole.Open(ownWindow ? Windowing.TextConsole.Wanted.OwnWindow : Windowing.TextConsole.Wanted.Terminal, title);
        if (line is not null && !consoleSaid) { consoleSaid = true; SayConsole(line); }
        // The dedicated host's run has begun (its logger hooked): an unhandled error from here holds its window too.
        if (ownWindow) Windowing.TextConsole.HoldOnCrash();
        return line;
    }

    /// <summary>
    /// The game's log window (a viewer of its own: <see cref="Windowing.TextConsole.LogWindowSet"/>), opened, closed or
    /// toggled (<paramref name="on"/> null), said in this program's log.
    /// </summary>
    public string? LogWindow(string game, bool? on = true)
    {
        var line = Windowing.TextConsole.LogWindowSet($"Vitomir log: {game}", on);
        SayConsole(line);
        return line;
    }

    bool consoleSaid;

    /// <summary>The console window of its own closed by its X, or Ctrl+C or Ctrl+Break in it: the run stops.</summary>
    public bool TextConsoleClosed => Windowing.TextConsole.CloseAsked;

    /// <summary>Where this program says what was done with the console: the console itself, which the platform's host makes its log.</summary>
    protected virtual void SayConsole(string line) => Console.WriteLine(line);

    public string[]? NextRun { get; set; }
    public string? MenuNotice { get; set; }
    public int Runs { get; set; }
    public bool Typing { get; set; }

    /// <summary>The window (null headless or before it is made): the host's own, never the controller's.</summary>
    public SDL_Window* Window => (SDL_Window*)keptWindow;
    /// <summary>The window's handle, for what the host's own program pushes into SDL's queue (a harness's scripted input).</summary>
    public nint Handle => keptWindow;
    public bool HasWindow => keptWindow != 0;
    public string GpuDriver => keptGpu?.Driver ?? "?";

    // SDL, the window and the GPU: made by the first run that needs them and kept by the rest.
    public bool OpenWindow(string title, WindowAsk ask)
    {
        WindowRun.From(ask);      // the run's facts for the window code: a replay, the settings, the log
        if (Windowless) windowlessSize = (Math.Max(1, ask.Width), Math.Max(1, ask.Height));
        if (!sdlUp)
        {
            if (ask.Headless || Windowless) SDL_SetHint(SDL_HINT_VIDEO_DRIVER, Environment.GetEnvironmentVariable("SDL_VIDEO_DRIVER") ?? "");
            if (!SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO)) throw new SdlException("SDL_Init");
            sdlUp = true;
        }
        var made = (SDL_Window*)keptWindow;
        if (!ask.Headless && !Windowless && made == null)
        {
            int width = ask.Width, height = ask.Height;
            // Where it was left, and full screen as 1.1, unless the run says otherwise (FullScreen.Start, ours).
            made = WindowPlaceHost.Open(title, width, height, FullScreenHost.Plan(ask.SizeGiven, width, height, ask.Windowed, ask.FullScreen,
                harness: ask.Harness,
                borderless: ask.Borderless));   // ours: `--borderless WxH`, `[window] borderless` (Borderless)
            if (made == null) throw new SdlException("SDL_CreateWindow");
            // A replay's window has no title bar and stands at the display's corner (ours), so a recording
            // made full screen at the display's own size fits: with a title bar its inside would be cut short.
            if (ask.Replaying) { SDL_SetWindowBordered(made, false); SDL_SetWindowPosition(made, 0, 0); SDL_SyncWindow(made); }
            // A replay nobody watches (a check, or `--replay-fast`) is drawn at its recording's size and shown in
            // a small window out of the way (ours): `PictureHost.Shrunk`. Several
            // at once (`--replay-folder`) are laid side by side by their `--replay-slot`. `--replay-full` shows
            // it at its own size.
            if (ask.Unwatched && !ask.ReplayFull)
            {
                PictureHost.Shrunk = (width, height);
                var (sw, sh) = PictureHost.ShrunkWindow(width, height);
                SDL_SetWindowSize(made, sw, sh);
                int slot = ask.ReplaySlot;
                SDL_SetWindowPosition(made, (slot % 3) * (sw + 8), (slot / 3) * (sh + 8));
                SDL_SyncWindow(made);
                Console.WriteLine($"replay: drawn at {width}x{height}, the recording's size, and shown in a window of {sw}x{sh}");
            }
            keptWindow = (nint)made;
        }
        else if (made != null) SDL_SetWindowTitle(made, title);
        return made != null;
    }

    /// <summary>The GPU, made by the first run that asks and kept by the rest, and the host's renderer for 1.1's frames on it.</summary>
    public IClassicRenderer OpenGpu(GpuAsk ask)
    {
        if (keptRenderer is not null) return keptRenderer;
        // Vulkan first on Windows, SDL's own choice when there is none; `--gpu` names one (ours: GpuDrivers).
        keptGpu = GpuDrivers.Open(Window, ask.Driver, ask.Debug, Console.WriteLine);
        // A replay check runs on its tape's steps, not the wall clock: drawn as fast as the machine can.
        if (ask.Unwatched is { } what && keptGpu.Unthrottle()) Console.WriteLine($"{what}: not waiting for the screen");
        // `--no-vsync` (a harness knob, ours): the same for any run, so a measured one shows what a frame
        // costs and not the screen's refresh. With `--frame-ms` the steps are fixed and the run is the same at any pace.
        else if (ask.NoVsync && keptGpu.Unthrottle()) Console.WriteLine("window: not waiting for the screen (--no-vsync)");
        keptRenderer = new ClassicRenderer(keptGpu);
        Opened(keptGpu);
        return keptRenderer;
    }

    /// <summary>
    /// The GPU just opened: a host that draws more than 1.1's own frames with it hears of it here (<c>vitomir-tools</c>', the
    /// levels generator's cell world for its tools); the platform's hears nothing.
    /// </summary>
    protected virtual void Opened(Gpu gpu) { }

    /// <summary>Why the host's renderer stopped the run, or null (none of the platform's own drawers stops one).</summary>
    public virtual string? Stopped => null;

    /// <summary>The end of the program: the GPU released, the window's place written, the window and SDL gone.</summary>
    public void Close()
    {
        if (windowlessTarget != null && keptGpu is not null) SDL_ReleaseGPUTexture(keptGpu.Device, windowlessTarget);
        windowlessTarget = null;
        keptGpu?.Dispose();
        WindowPlaceHost.Save();                                   // where the window was left (ours)
        if (keptWindow != 0) SDL_DestroyWindow((SDL_Window*)keptWindow);
        if (sdlUp) SDL_Quit();
    }

    public bool PictureSize(int* w, int* h) => PictureHost.Size(Window, w, h);

    public GpuPicture Acquire()
    {
        var cmd = SDL_AcquireGPUCommandBuffer(keptGpu!.Device);
        if (Windowless && keptWindow == 0)
        {
            // The run's picture drawn into a target of its own, never shown; had every frame (no window to be minimised).
            var (w, h) = windowlessSize;
            if (windowlessTarget == null || windowlessMade != (w, h))
            {
                if (windowlessTarget != null) SDL_ReleaseGPUTexture(keptGpu.Device, windowlessTarget);
                windowlessTarget = keptGpu.CreateTarget(w, h, keptGpu.ColorFormat,
                    SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);
                windowlessMade = (w, h);
            }
            return new GpuPicture(cmd, windowlessTarget, w, h, windowlessTarget != null);
        }
        SDL_GPUTexture* swap; uint sw, sh;
        bool acquired = PictureHost.Acquire(keptGpu, cmd, Window, &swap, &sw, &sh) && swap != null;
        return new GpuPicture(cmd, swap, (int)sw, (int)sh, acquired);
    }

    public void Submit(in GpuPicture picture)
    {
        PictureHost.Submit(picture.Commands);
        // With no swapchain nothing holds the frames back, so each windowless frame waits for its picture to be done.
        if (Windowless && keptWindow == 0 && keptGpu is not null) SDL_WaitForGPUIdle(keptGpu.Device);
    }

    public void KeepPictures() => PictureHost.KeepWhole = true;
    public byte[]? LastPicture(out int width, out int height) => PictureHost.PicturePixels(out width, out height);
    public void KeepFrame() => WindowFrameHost.Frame();
    public void KeepPlace() => WindowPlaceHost.Tick();
    public void KeepOnTop() => BorderlessHost.Tick();
    public (int Width, int Height)? WorldSize() => FullScreenHost.WorldSize();
    public void Title(string title) => SDL_SetWindowTitle(Window, title);

    public void Heard(in LoopInput e) => WindowPlaceHost.Hear(e);

    public bool KeyDown(int scancode)
    {
        int n;
        var keys = SDL_GetKeyboardState(&n);
        return keys != null && scancode >= 0 && scancode < n && keys[scancode];
    }

    /// <summary>A harness's scripted input (<c>--ui-click</c>, <c>--ui-type</c>, <c>--menu-steps</c>) pushed into SDL's queue
    /// (<see cref="ScriptedInput"/>; this host's, both programs', the old program's own until then).</summary>
    public virtual IScriptedInput? Script(string? steps, string? typed, ScriptKeys keys) => ScriptedInput.From(steps, typed, () => Handle, keys);

    public ulong Now() => SDL_GetTicksNS();
    public ulong Ticks() => SDL_GetTicks();

    /// <summary>This host as the services a module's loop played in its window is handed (<c>LoopContext.Host</c>).</summary>
    public object? GetService(Type serviceType) => serviceType.IsInstanceOfType(this) ? this : null;

    /// <summary>
    /// **The platform's frame loop for a game played in this host's window** (the pushed frame; both programs'): before each
    /// frame the wait the last one asked for (<see cref="LoopResult.NextFrameNs"/>: 1.1's idle behind a window without the focus,
    /// its console's <c>maxfps</c>), then the events since the last frame gathered (<see cref="Gather"/>), the frame timed on SDL's
    /// clock and handed to <paramref name="frame"/>, until it answers anything but go on or a restart (the game's next run, which
    /// it starts itself on the next frame, the window and the GPU kept). Answers the last answer; the caller closes the host.
    /// </summary>
    public LoopResult Frames(Func<HostFrame, LoopResult> frame)
    {
        long index = 0, last = (long)SDL_GetTicksNS(), next = 0;
        while (true)
        {
            Wait(next);
            long now = (long)SDL_GetTicksNS();
            int w = 0, h = 0;
            bool shown = keptWindow != 0 && PictureSize(&w, &h);
            var flags = keptWindow != 0 ? SDL_GetWindowFlags(Window) : 0;
            var answer = frame(new HostFrame
            {
                Index = ++index, TimeNs = now, ElapsedNs = now - last, Input = Gather(),
                PictureWidth = w, PictureHeight = h, Density = keptWindow != 0 ? SDL_GetWindowPixelDensity(Window) : 1f,
                Focused = (flags & SDL_WindowFlags.SDL_WINDOW_INPUT_FOCUS) != 0,
                CanDraw = shown && (flags & (SDL_WindowFlags.SDL_WINDOW_MINIMIZED | SDL_WindowFlags.SDL_WINDOW_HIDDEN)) == 0,
            });
            last = now;
            if (answer.Verdict == LoopVerdict.Quit) return answer;
            next = answer.NextFrameNs;
        }
    }

    /// <summary>The events since the last frame, as the game hears them (the picture's, <see cref="PictureHost.Poll"/>); none before SDL is up.</summary>
    List<LoopInput> Gather()
    {
        var events = new List<LoopInput>();
        if (!sdlUp) return events;
        SDL_Event raw;
        while (PictureHost.Poll(&raw))
            if (SdlInput.From(raw) is { } made) events.Add(made);
        return events;
    }

    /// <summary>
    /// The host's wait: until SDL's clock reaches <paramref name="until"/> (0: none). A wait of whole milliseconds is slept at
    /// once (the idle behind a window without the focus, which was <c>SDL_Delay(10)</c>), the rest by 0.1 ms as 1.1's <c>maxfps</c> was.
    /// </summary>
    static void Wait(long until)
    {
        if (until <= 0) return;
        long left = until - (long)SDL_GetTicksNS();
        if (left >= 2_000_000L) SDL_DelayNS((ulong)(left - left % 1_000_000L));
        while ((long)SDL_GetTicksNS() < until) SDL_DelayNS(100_000);
    }

    public void ShowCursor(bool shown) { if (shown) SDL_ShowCursor(); else SDL_HideCursor(); }
    public void RelativeMouse(bool on) => SDL_SetWindowRelativeMouseMode(Window, on);
    public void Warp(float x, float y) => PictureHost.Warp(Window, x, y);
    public void ConfineMouse(bool on) => SDL_SetWindowMouseGrab(Window, on);

    public void SystemKeys(bool winKey, bool altTab)
    {
        SDL_SetHint(SDL_HINT_ALLOW_ALT_TAB_WHILE_GRABBED, altTab ? "1" : "0");
        SDL_SetWindowKeyboardGrab(Window, winKey);  // SDL holds it only while the window has the focus
    }

    public bool TextInputActive => SDL_TextInputActive(Window);
    public void TextInput(bool on) { if (on) SDL_StartTextInput(Window); else SDL_StopTextInput(Window); }
    public string? Clipboard() => SDL_HasClipboardText() ? SDL_GetClipboardText() : null;

    /// <summary>The sound device, the program's own.</summary>
    public abstract AudioOutput Speakers(AudioFormat format);
}
