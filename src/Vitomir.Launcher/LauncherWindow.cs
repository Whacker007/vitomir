// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The launcher's window: SDL's window, the platform's renderer drawing the interface's draw list, the events into the screen.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using SDL;
using Vitomir.Platform.Interface;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Render;
using Vitomir.Platform.Render.Windowing;
using static SDL.SDL3;

namespace Vitomir.Launcher;

/// <summary>
/// **The launcher's window**: a window of SDL's at a fixed comfortable size
/// (<see cref="Size"/>, resizable, no smaller than <see cref="Least"/>), drawn by the platform's own renderer
/// (<see cref="PlatformRenderer"/>: no level, only the interface's draw list over black) from the launcher's screen
/// (<see cref="Launcher.Screen"/>, the interface's pieces laid out at <see cref="Metrics"/>), and its events given to the screen: the
/// pointer, the wheel, the keys the interface answers (Up, Down, Left, Right, Tab, Space, Enter, Escape) and, while the folder box is typed
/// in, the text, Backspace and Ctrl+V. While the game runs the window is hidden and nothing is drawn; it comes back, raised, when the
/// game ends. The folder picker is SDL's own (Windows' folder dialog). No other UI framework.
/// </summary>
public static unsafe class LauncherWindow
{
    /// <summary>The window's size when it opens (window units), and the least it may be made.</summary>
    public static readonly (int Width, int Height) Size = (1280, 800), Least = (1024, 680);

    /// <summary>
    /// The interface's measures for a picture <paramref name="width"/> x <paramref name="height"/> pixels on a display scaled by
    /// <paramref name="scale"/> (Windows' 150 % is 1.5): a unit kept near 0.78 pixels times the scale (so the words are the same size
    /// however tall the window is, and the same size to the eye on a scaled display, drawn from the art's own resolution), within the
    /// interface's own scale's bounds.
    /// </summary>
    public static UiMetrics Metrics(int width, int height, float scale = 1f) =>
        new(width, height, height > 0 ? Math.Clamp(0.78f * MathF.Max(1f, scale) * UiMetrics.DesignHeight / height, UiMetrics.MinScale, UiMetrics.MaxScale) : 1f);

    /// <summary>Runs the window until Quit or its close; answers the exit code.</summary>
    public static int Run(Launcher launcher, Action<string> say)
    {
        if (!SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO)) { say($"launcher: SDL could not start its video: {SDL_GetError()}"); return 1; }
        SDL_Window* window = null;
        PlatformRenderer? renderer = null;
        try
        {
            window = SDL_CreateWindow("Vitomir", Size.Width, Size.Height, SDL_WindowFlags.SDL_WINDOW_RESIZABLE | SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY);
            if (window == null) { say($"launcher: the window could not be made: {SDL_GetError()}"); return 1; }
            SDL_SetWindowMinimumSize(window, Least.Width, Least.Height);
            Scaled(window, say);
            WindowIcon.Set(window);
            renderer = PlatformRenderer.Create((nint)window, new RendererOptions(VSync: true));
            say($"launcher: a window {Size.Width}x{Size.Height}, drawn by {renderer.Driver}");
            SDL_ShowWindow(window);
            return Loop(launcher, window, renderer, say);
        }
        catch (Exception e) when (e is SdlException or InvalidOperationException)
        {
            say($"launcher: the window could not be drawn: {e.Message}");
            SDL_ShowSimpleMessageBox(SDL_MessageBoxFlags.SDL_MESSAGEBOX_ERROR, "Vitomir", $"The launcher's window could not be drawn: {e.Message}", window);
            return 1;
        }
        finally
        {
            // Films being made or tools fetched: stopped and waited for, so no program is left running after the launcher.
            launcher.Films.Shutdown();
            if (window != null) SDL_StopTextInput(window);
            renderer?.Dispose();
            if (window != null) SDL_DestroyWindow(window);
            SDL_Quit();
        }
    }

    /// <summary>
    /// On a display Windows scales (150 %, 200 %) while SDL gives the window its size in the display's own pixels, the window made as
    /// large as the scale asks (1280 x 800 at 150 % is 1920 x 1200 pixels, its least size with it), kept inside the display's usable
    /// room; elsewhere (SDL scaling the window itself, or no scale) nothing changes.
    /// </summary>
    static void Scaled(SDL_Window* window, Action<string> say)
    {
        float content = SDL_GetDisplayContentScale(SDL_GetDisplayForWindow(window));
        float density = SDL_GetWindowPixelDensity(window);
        if (content <= 1.01f || density > 1.01f) return;
        SDL_Rect usable;
        int w = (int)(Size.Width * content), h = (int)(Size.Height * content);
        if (SDL_GetDisplayUsableBounds(SDL_GetDisplayForWindow(window), &usable))
            (w, h) = (Math.Min(w, Math.Max(Least.Width, usable.w - 40)), Math.Min(h, Math.Max(Least.Height, usable.h - 60)));
        SDL_SetWindowMinimumSize(window, Math.Min(w, (int)(Least.Width * content)), Math.Min(h, (int)(Least.Height * content)));
        SDL_SetWindowSize(window, w, h);
        SDL_SetWindowPosition(window, (int)SDL_WINDOWPOS_CENTERED_MASK, (int)SDL_WINDOWPOS_CENTERED_MASK);
        say($"launcher: the display is scaled {content * 100:0} %: the window {w}x{h}");
    }

    /// <summary>The window's display scale (its pixel density times the display's content scale; 1 when SDL cannot say).</summary>
    static float Scale(SDL_Window* window) => SDL_GetWindowDisplayScale(window) is var s && s > 0f ? s : 1f;

    static int Loop(Launcher launcher, SDL_Window* window, PlatformRenderer renderer, Action<string> say)
    {
        var list = new DrawList();
        var view = new FrameView { ShowReticle = false };
        var screen = launcher.Screen;
        bool hidden = false, typing = false, drawn = false;
        while (!launcher.QuitAsked)
        {
            // Drawn again only when something may have changed: an event, a step's end or its progress. An idle launcher draws nothing
            // (it drew four pictures a second, which a slow laptop felt).
            bool changed = launcher.Poll() | !drawn;
            // The game runs: the window hidden, the events still taken (nothing to do but wait for its end).
            if (launcher.Running is not null)
            {
                if (!hidden) { SDL_HideWindow(window); hidden = true; say("launcher: hidden while the game runs"); }
                SDL_Event idle;
                if (SDL_WaitEventTimeout(&idle, 200) && (SDL_EventType)idle.type == SDL_EventType.SDL_EVENT_QUIT) { }
                while (SDL_PollEvent(&idle)) { }
                continue;
            }
            if (hidden)
            {
                SDL_ShowWindow(window);
                SDL_RaiseWindow(window);
                hidden = false;
                changed = true;
                say("launcher: back, the game has ended");
            }
            if (launcher.Editing is not null != typing)
            {
                typing = launcher.Editing is not null;
                if (typing) SDL_StartTextInput(window); else SDL_StopTextInput(window);
            }
            float density = SDL_GetWindowPixelDensity(window) is var d && d > 0f ? d : 1f;
            SDL_Event e;
            // A step runs (the progress moves): about 30 pictures a second; else woken by the events, four a second at most.
            bool moving = launcher.Setup.Busy || launcher.Films.Working || launcher.Films.Fetching || launcher.UserFolder.Moving
                          || launcher.Modules.Importing || launcher.Modules.Checking || launcher.Modules.Verifying is not null;
            if (SDL_WaitEventTimeout(&e, moving ? 33 : 250))
            {
                do Take(launcher, screen, window, in e, density);
                while (SDL_PollEvent(&e));
                changed = true;
            }
            if (screen.Actions.Count > 0) changed = true;
            launcher.TakeActions();
            if (launcher.TakePick() is { } pickFor) Browse(window, pickFor, pickFor == PickFor.SetupFolder ? launcher.Setup.Path : "");
            if (TakePicked() is { } picked) launcher.Picked(picked.Length > 0 ? picked : null);
            if (launcher.TakeClipboard() is { } copied && !SDL_SetClipboardText(copied)) say($"launcher: the clipboard was not set: {SDL_GetError()}");
            if (launcher.QuitAsked) break;

            if (typing || moving) changed = true;
            if (!changed) continue;
            int w = 0, h = 0;
            if (!SDL_GetWindowSizeInPixels(window, &w, &h) || w <= 0 || h <= 0) continue;
            drawn = true;
            launcher.Fill();
            float scale = Scale(window);
            screen.Layout(Metrics(w, h, scale));
            if (launcher.TakeFocus() is { } wanted && screen.Find(wanted) is { } piece)
            {
                screen.SetFocus(piece);
                screen.Layout(Metrics(w, h, scale));
            }
            screen.Draw(list);
            renderer.Draw(view, list);
        }
        if (launcher.Setup.Busy)
        {
            // Closed while a step runs: it is stopped (an import carries on from there the next time).
            launcher.Setup.Cancel();
            launcher.Setup.Wait(TimeSpan.FromSeconds(30));
        }
        say("launcher: closed");
        return 0;
    }

    static void Take(Launcher launcher, Screen screen, SDL_Window* window, in SDL_Event e, float density)
    {
        switch ((SDL_EventType)e.type)
        {
            case SDL_EventType.SDL_EVENT_QUIT:
            case SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED:
                launcher.Act("quit");
                break;
            case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
                screen.PointerMove(e.motion.x * density, e.motion.y * density);
                break;
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN when e.button.button == SDL_BUTTON_LEFT:
                screen.PointerMove(e.button.x * density, e.button.y * density);
                // A click anywhere but on the box ends its typing (the text kept).
                if (launcher.Editing is not null && screen.Hover is not TextBox) launcher.EndEdit(commit: false);
                screen.PointerDown();
                break;
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP when e.button.button == SDL_BUTTON_LEFT:
                screen.PointerUp();
                break;
            case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
                {
                    float turn = e.wheel.direction == SDL_MouseWheelDirection.SDL_MOUSEWHEEL_FLIPPED ? -e.wheel.y : e.wheel.y;
                    if (turn != 0f) screen.Wheel(turn);
                    break;
                }
            case SDL_EventType.SDL_EVENT_TEXT_INPUT when launcher.Editing is not null:
                launcher.Type(Utf8(e.text.text));
                break;
            case SDL_EventType.SDL_EVENT_KEY_DOWN:
                Key(launcher, screen, e.key.scancode, e.key.mod, e.key.repeat);
                break;
        }
    }

    static void Key(Launcher launcher, Screen screen, SDL_Scancode code, SDL_Keymod mod, bool repeat)
    {
        bool ctrl = (mod & SDL_Keymod.SDL_KMOD_CTRL) != 0, shift = (mod & SDL_Keymod.SDL_KMOD_SHIFT) != 0, alt = (mod & SDL_Keymod.SDL_KMOD_ALT) != 0;
        if (launcher.Editing is not null)
        {
            switch (code)
            {
                case SDL_Scancode.SDL_SCANCODE_BACKSPACE: launcher.Backspace(word: ctrl); return;
                case SDL_Scancode.SDL_SCANCODE_RETURN or SDL_Scancode.SDL_SCANCODE_KP_ENTER: if (!repeat) launcher.EndEdit(commit: true); return;
                case SDL_Scancode.SDL_SCANCODE_ESCAPE: if (!repeat) launcher.EndEdit(commit: false, putBack: true); return;
                case SDL_Scancode.SDL_SCANCODE_V when ctrl:
                    if (SDL_HasClipboardText() && SDL_GetClipboardText() is { } pasted) launcher.Type(pasted);
                    return;
                case SDL_Scancode.SDL_SCANCODE_TAB: launcher.EndEdit(commit: false); break;   // and the focus moves on, below
                default: return;   // the text comes as text input
            }
        }
        UiKey? key = code switch
        {
            SDL_Scancode.SDL_SCANCODE_UP => alt ? UiKey.AltUp : UiKey.Up,             // Alt+Up and Alt+Down move a module's row
            SDL_Scancode.SDL_SCANCODE_DOWN => alt ? UiKey.AltDown : UiKey.Down,
            SDL_Scancode.SDL_SCANCODE_LEFT => UiKey.Left,
            SDL_Scancode.SDL_SCANCODE_RIGHT => UiKey.Right,
            SDL_Scancode.SDL_SCANCODE_TAB => shift ? UiKey.BackTab : UiKey.Tab,
            SDL_Scancode.SDL_SCANCODE_RETURN or SDL_Scancode.SDL_SCANCODE_KP_ENTER => UiKey.Enter,
            SDL_Scancode.SDL_SCANCODE_SPACE => UiKey.Space,
            SDL_Scancode.SDL_SCANCODE_ESCAPE => UiKey.Escape,
            _ => null,
        };
        if (key is { } k && (!repeat || k is not (UiKey.Escape or UiKey.Enter or UiKey.Space))) screen.Key(k);
        // Page Up and Page Down move the page's scroll by the wheel.
        if (code is SDL_Scancode.SDL_SCANCODE_PAGEUP or SDL_Scancode.SDL_SCANCODE_PAGEDOWN) screen.Wheel(code == SDL_Scancode.SDL_SCANCODE_PAGEUP ? 6f : -6f);
    }

    static string Utf8(byte* text)
    {
        if (text == null) return "";
        int n = 0;
        while (text[n] != 0) n++;
        return Encoding.UTF8.GetString(text, n);
    }

    // ---- the pickers: Windows' folder dialog, or its file dialog for a zip or a .nob; SDL answers through a callback, maybe on
    // another thread -------------------------------------------------------------------------------------------------------------

    static readonly object pickGate = new();
    static string? pickedFolder;
    static bool picking;

    // The file dialog's filters, kept for the dialog's life (SDL reads them while it is up): zips and .nobs, and every file.
    static readonly IntPtr filterNames = Marshal.StringToCoTaskMemUTF8("A module's pack (zip, nob)"), filterPatterns = Marshal.StringToCoTaskMemUTF8("zip;nob"),
                           allName = Marshal.StringToCoTaskMemUTF8("Every file"), allPattern = Marshal.StringToCoTaskMemUTF8("*");
    static readonly SDL_DialogFileFilter* filters = MakeFilters();

    static SDL_DialogFileFilter* MakeFilters()
    {
        var f = (SDL_DialogFileFilter*)NativeMemory.Alloc(2, (nuint)sizeof(SDL_DialogFileFilter));
        f[0].name = (byte*)filterNames;
        f[0].pattern = (byte*)filterPatterns;
        f[1].name = (byte*)allName;
        f[1].pattern = (byte*)allPattern;
        return f;
    }

    static void Browse(SDL_Window* window, PickFor wanted, string start)
    {
        lock (pickGate)
        {
            if (picking) return;
            picking = true;
        }
        string? from = Directory.Exists(start) ? start : null;
        if (wanted == PickFor.ModuleFile) SDL_ShowOpenFileDialog(&Picked, IntPtr.Zero, window, filters, 2, from, false);
        else SDL_ShowOpenFolderDialog(&Picked, IntPtr.Zero, window, from, false);
    }

    /// <summary>The folder or file selected since the last call (empty: the dialog was closed with none), or null while none was answered.</summary>
    static string? TakePicked()
    {
        lock (pickGate)
        {
            var p = pickedFolder;
            pickedFolder = null;
            return p;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void Picked(IntPtr userdata, byte** files, int filter)
    {
        string answer = files is not null && files[0] is not null ? Utf8(files[0]) : "";
        lock (pickGate)
        {
            pickedFolder = answer;
            picking = false;
        }
    }
}
