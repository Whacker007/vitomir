// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// `--ui-click` and `--ui-type`: a windowed run's clicks and typing, pushed into SDL's own queue (a harness). the old program's until
// Then the platform's SDL host's (both programs'), word for word but for the key names, which are the game's (ScriptKeys).
using System.Globalization;
using System.Runtime.InteropServices;
using SDL;
using Vitomir.Platform.Render.Classic;
using static SDL.SDL3;

namespace Vitomir.Platform.Render.Windowing;

/// <summary>
/// A harness for the game's own input path (ours). <c>--ui-click x,y[;x,y...]</c> is a left click at
/// each window pixel in turn, and <c>--ui-type text</c> is that text typed and Enter pressed after the
/// last of them. A step of <c>--ui-click</c> may also be <c>r:x,y</c> (a right click there — the
/// world's cast), <c>key:Ctrl+V</c> (a key pressed and let go, in the key page's names, with the
/// modifiers on the event's own <c>mod</c>), <c>down:Keypad 4</c> and <c>up:Keypad 4</c> (a key held
/// between two steps — or a button, <c>down:Middle Click</c>), <c>drag:dx,dy</c> (the mouse
/// moved by that much), <c>shot:path.png</c> (the frame written out), <c>winshot:path.png</c> (the whole
/// window as last presented, with borderless full screen's bars — every other pixel a step names is the
/// window's too, so a click on the picture names the picture's pixel plus the bar's offset), <c>text:words</c> (typed, no Enter), <c>clip:words</c>
/// (the clipboard set to them, <c>\r\n</c> written as such), <c>move:x,y</c> (the pointer moved there,
/// no button), <c>focus:out</c> and <c>focus:in</c> (the window losing and gaining the focus) or <c>wait:n</c> (n frames with nothing). In place of <c>x,y</c> a click, a right click
/// or a move may name a template — <c>r:@cappadocian</c> — and lands on the nearest living one where it
/// is drawn on the frame the step goes in (<see cref="HarnessAim"/>); <c>size:WxH</c> makes the window that size (ours, a resize). Every step goes in through <c>SDL_PushEvent</c>, so it comes out of the same poll a
/// hand's click comes out of and meets every handler a real one meets, in the same order — which is
/// the point: <c>--shop-click</c> and <c>--menu-click</c> call a handler directly, and a click some
/// Earlier handler swallows would pass them and fail the player. One step a frame from the tenth
/// frame (the pointer moved there, then pressed; the release on the next frame), in a windowed run —
/// with <c>SDL_VIDEO_DRIVER=offscreen</c> that is a run with no screen, and <c>--frames</c> ends it.
/// <para>The platform's SDL host's, so <c>vitomir-platform --game classic --developer</c> runs it as <c>vitomir-tools</c>
/// does; the keys' names, the US keycode of a position, the keys the harness holds and the mouse's base are the game's
/// (<see cref="ScriptKeys"/>, which the controller gives), so nothing here names the engine.</para>
/// </summary>
public sealed unsafe class ScriptedInput : IScriptedInput
{
    Func<nint> window = () => 0;
    ScriptKeys keys = null!;
    readonly Queue<SDL_Event[]> steps = new();
    /// <summary>The clipboard each <c>clip:</c> step sets, by the step's number.</summary>
    readonly Dictionary<int, string> clips = [];
    int pumped;
    /// <summary>The steps aimed at a thing rather than a pixel (<c>@template</c>), by the step's number.</summary>
    readonly Dictionary<int, string> aims = [];
    (float X, float Y)? lastAim;
    /// <summary>The picture each <c>shot:</c> step writes, by the step's number.</summary>
    readonly Dictionary<int, string> shots = [];
    /// <summary>The whole-window picture each <c>winshot:</c> step writes, by the step's number.</summary>
    readonly Dictionary<int, string> windowShots = [];
    /// <summary>The window's size each <c>size:</c> step sets, by the step's number.</summary>
    readonly Dictionary<int, (int W, int H)> sizes = [];

    /// <summary>Writes the frame as it was last drawn to a PNG, for <c>shot:path</c> steps.</summary>
    public Action<string>? Shoot { get; set; }
    int waited;

    /// <summary>How many frames an <c>@template</c> step waits for its thing to come on the screen before it gives up.</summary>
    public const int AimWait = 600;

    /// <summary>Where on the window a template's nearest living thing is drawn, for <c>@template</c> steps (<see cref="HarnessAim"/>).</summary>
    public Func<string, (float X, float Y)?>? Aim { get; set; }

    /// <summary><c>\n</c>, <c>\r</c> and <c>\t</c> in a <c>clip:</c> step as the characters.</summary>
    static string Unescape(string s) => s.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t");

    /// <summary>The frame the first step goes in on: the run has settled and drawn by then.</summary>
    public const int FirstFrame = 10;

    /// <summary>Whether anything is left to push.</summary>
    public bool Pending => steps.Count > 0;

    /// <summary>The steps from the two arguments, or null when neither was given.</summary>
    public static ScriptedInput? From(string? clicks, string? typed, Func<nint> window, ScriptKeys keys)
    {
        if (clicks is null && typed is null) return null;
        var input = new ScriptedInput { window = window, keys = keys };
        PictureHost.HarnessPointer = true;              // the run's pointer is the harness's alone
        float lastX = 0f, lastY = 0f;                   // where the pointer was last put, for `down:` buttons and `drag:`
        foreach (var at in (clicks ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (at.StartsWith("key:", StringComparison.OrdinalIgnoreCase))
            {
                if (keys.Parse(at[4..]) is not { } chord || chord.Input > keys.MouseBase)
                { Console.WriteLine($"  harness: no key called {at[4..]}"); continue; }
                input.steps.Enqueue([Key(keys, chord, true), Key(keys, chord, false)]);
                continue;
            }
            // `down:` and `up:`: a key pressed on one step and let go on a later one — a held key,
            // such as the camera's turn (Keypad 4).
            // A button too: `down:Middle Click`, at the pointer's last place.
            if (at.StartsWith("down:", StringComparison.OrdinalIgnoreCase) || at.StartsWith("up:", StringComparison.OrdinalIgnoreCase))
            {
                bool down = at[0] is 'd' or 'D';
                string name = at[(down ? 5 : 3)..];
                if (keys.Parse(name) is not { } held)
                { Console.WriteLine($"  harness: no key called {name}"); continue; }
                input.steps.Enqueue([held.Input > keys.MouseBase ? Button(lastX, lastY, down, (byte)(held.Input - keys.MouseBase)) : Key(keys, held, down)]);
                continue;
            }
            // `drag:dx,dy`: the mouse moved by that much — what a held swivel reads.
            if (at.StartsWith("drag:", StringComparison.OrdinalIgnoreCase))
            {
                var d = at[5..].Split(',').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                var moved = Motion(lastX + d[0], lastY + d[1]);
                (moved.motion.xrel, moved.motion.yrel) = (d[0], d[1]);
                input.steps.Enqueue([moved]);
                continue;
            }
            // `focus:out` and `focus:in`: the window losing and gaining the focus, as an Alt+Tab does —
            // without the key-ups SDL sends for its own held keys, which pushed keys are not (so a held
            // `down:` key across it is the case of a key-up the binding layer never heard).
            if (at.StartsWith("focus:", StringComparison.OrdinalIgnoreCase))
            {
                SDL_Event f = default;
                f.window.type = at[6..].Equals("in", StringComparison.OrdinalIgnoreCase)
                    ? SDL_EventType.SDL_EVENT_WINDOW_FOCUS_GAINED : SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST;
                input.steps.Enqueue([f]);
                continue;
            }
            if (at.StartsWith("shot:", StringComparison.OrdinalIgnoreCase)) { input.shots[input.steps.Count] = at[5..]; input.steps.Enqueue([]); continue; }
            // `winshot:path.png` (ours): the whole window as it was last presented, with the bars a picture
            // in borderless full screen has beside it — `shot:` is the picture alone (PictureHost.WindowShot).
            if (at.StartsWith("winshot:", StringComparison.OrdinalIgnoreCase))
            {
                input.windowShots[input.steps.Count] = at[8..];
                input.steps.Enqueue([]);
                PictureHost.KeepWhole = true;
                continue;
            }
            // `quit` (ours): the window closed, as its close button or Alt+F4 does (SDL's quit event): a player's way
            // out of any screen, wherever its buttons lie on whatever size the window came up at.
            if (at.Equals("quit", StringComparison.OrdinalIgnoreCase)) { input.steps.Enqueue([Quit()]); continue; }
            // `size:WxH` (ours): the window made that size, as a hand dragging its corner does.
            if (at.StartsWith("size:", StringComparison.OrdinalIgnoreCase))
            {
                var wh = at[5..].Split('x').Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                input.sizes[input.steps.Count] = (wh[0], wh[1]);
                input.steps.Enqueue([]);
                continue;
            }
            if (at.StartsWith("text:", StringComparison.OrdinalIgnoreCase)) { input.steps.Enqueue([Text(at[5..])]); continue; }
            if (at.StartsWith("clip:", StringComparison.OrdinalIgnoreCase)) { input.clips[input.steps.Count] = Unescape(at[5..]); input.steps.Enqueue([]); continue; }
            if (at.StartsWith("wait:", StringComparison.OrdinalIgnoreCase))
            {
                for (int n = int.Parse(at[5..], CultureInfo.InvariantCulture); n > 0; n--) input.steps.Enqueue([]);
                continue;
            }
            if (at.StartsWith("move:", StringComparison.OrdinalIgnoreCase))
            {
                if (at[5..].StartsWith('@')) input.aims[input.steps.Count] = at[6..];
                var m = input.aims.ContainsKey(input.steps.Count) ? [0f, 0f] : at[5..].Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                (lastX, lastY) = (m[0], m[1]);
                input.steps.Enqueue([Motion(m[0], m[1])]);
                continue;
            }
            bool right = at.StartsWith("r:", StringComparison.OrdinalIgnoreCase);
            string where = right ? at[2..] : at;
            // `@template`: the thing's pixel, worked out when the step goes in (HarnessAim).
            if (where.StartsWith('@')) { input.aims[input.steps.Count] = where[1..]; input.aims[input.steps.Count + 1] = ""; }
            var p = where.StartsWith('@') ? [0f, 0f] : where.Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            (lastX, lastY) = (p[0], p[1]);
            byte button = (byte)(right ? SDL_BUTTON_RIGHT : SDL_BUTTON_LEFT);
            input.steps.Enqueue([Motion(p[0], p[1]), Button(p[0], p[1], pressed: true, button)]);
            input.steps.Enqueue([Button(p[0], p[1], pressed: false, button)]);
        }
        if (typed is not null)
        {
            if (typed.Length > 0) input.steps.Enqueue([Text(typed)]);
            input.steps.Enqueue([Key(SDL_Keycode.SDLK_RETURN, SDL_Scancode.SDL_SCANCODE_RETURN, true),
                                 Key(SDL_Keycode.SDLK_RETURN, SDL_Scancode.SDL_SCANCODE_RETURN, false)]);
        }
        return input;
    }

    /// <summary>Pushes this frame's step, if its frame has come; the next poll reads it.</summary>
    public void Pump(int frame)
    {
        var window = (SDL_Window*)this.window();
        if (frame < FirstFrame || steps.Count == 0) return;
        if (clips.Remove(pumped, out var clip))
        {
            SDL_SetClipboardText(clip);
            Console.WriteLine($"  harness: clipboard \"{clip.Replace("\r", "\\r").Replace("\n", "\\n")}\"");
        }
        if (sizes.Remove(pumped, out var size))
        {
            SDL_SetWindowSize(window, size.W, size.H);
            Console.WriteLine($"  harness: window {size.W}x{size.H}");
        }
        if (shots.Remove(pumped, out var shot))
        {
            Shoot?.Invoke(shot);
            Console.WriteLine($"  harness: shot {shot}");
        }
        if (windowShots.Remove(pumped, out var whole)) PictureHost.WindowShot(whole);
        (float X, float Y)? aimed = null;
        if (aims.TryGetValue(pumped, out var template))
        {
            if (template.Length == 0) aimed = lastAim;                 // a click's release: where it was pressed
            else
            {
                aimed = Aim?.Invoke(template);
                // Not drawn on the world's part of the window: the step waits for it, a frame at a time.
                if (aimed is null && ++waited < AimWait)
                {
                    if (waited == 1) Console.WriteLine($"  harness: waiting for {template} to be on the screen");
                    return;
                }
                waited = 0;
                // Worked out on the picture, pushed as a window pixel like every other step (borderless full screen's bars).
                if (aimed is { } onPicture) aimed = PictureHost.ToWindow(onPicture.X, onPicture.Y);
                lastAim = aimed;
                Console.WriteLine(aimed is { } px ? $"  harness: {template} is at {px.X:0},{px.Y:0}" : $"  harness: no {template} on the screen");
            }
            aims.Remove(pumped);
        }
        pumped++;
        var id = SDL_GetWindowID(window);
        foreach (var step in steps.Dequeue())
        {
            var e = step;
            if (aimed is { } a)
            {
                if (e.Type is SDL_EventType.SDL_EVENT_MOUSE_MOTION) (e.motion.x, e.motion.y) = a;
                else if (e.Type is SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN or SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP) (e.button.x, e.button.y) = a;
            }
            // The window's id in whichever member the event's type reads (they share the offset).
            e.key.windowID = id;
            // The pointer's events marked the harness's, the system's own not heard in its run (PictureHost.HarnessPointer).
            if (e.Type is SDL_EventType.SDL_EVENT_MOUSE_MOTION) e.motion.which = (SDL_MouseID)PictureHost.HarnessMouse;
            else if (e.Type is SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN or SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP) e.button.which = (SDL_MouseID)PictureHost.HarnessMouse;
            else if (e.Type is SDL_EventType.SDL_EVENT_MOUSE_WHEEL) e.wheel.which = (SDL_MouseID)PictureHost.HarnessMouse;
            SDL_PushEvent(&e);
            if (e.Type is SDL_EventType.SDL_EVENT_KEY_DOWN) keys.Held.Add((int)e.key.scancode);
            else if (e.Type is SDL_EventType.SDL_EVENT_KEY_UP) keys.Held.Remove((int)e.key.scancode);
            // A button too, which SDL's own state does not see pushed: the free camera's drag look polls the middle one.
            if (e.Type is SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN) keys.Held.Add(keys.MouseBase + e.button.button);
            else if (e.Type is SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP) keys.Held.Remove(keys.MouseBase + e.button.button);
            if (e.Type is SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP)
                Console.WriteLine($"  harness: {(e.button.button == SDL_BUTTON_RIGHT ? "right-" : "")}clicked {e.button.x:0},{e.button.y:0}");
            else if (e.Type is SDL_EventType.SDL_EVENT_KEY_UP)
                Console.WriteLine($"  harness: pressed {keys.Name(new ScriptKey((int)e.key.scancode, Mods(e.key.mod)))}");
        }
    }

    static SDL_Event Quit()
    {
        SDL_Event e = default;
        e.quit.type = SDL_EventType.SDL_EVENT_QUIT;
        return e;
    }

    static SDL_Event Motion(float x, float y)
    {
        SDL_Event e = default;
        e.motion.type = SDL_EventType.SDL_EVENT_MOUSE_MOTION;
        (e.motion.x, e.motion.y) = (x, y);
        return e;
    }

    static SDL_Event Button(float x, float y, bool pressed, byte button = (byte)SDL_BUTTON_LEFT)
    {
        SDL_Event e = default;
        e.button.type = pressed ? SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN : SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP;
        e.button.button = button;
        e.button.down = pressed;
        e.button.clicks = 1;
        (e.button.x, e.button.y) = (x, y);
        return e;
    }

    static SDL_Event Key(SDL_Keycode key, SDL_Scancode scancode, bool pressed)
    {
        SDL_Event e = default;
        e.key.type = pressed ? SDL_EventType.SDL_EVENT_KEY_DOWN : SDL_EventType.SDL_EVENT_KEY_UP;
        e.key.key = key;
        e.key.scancode = scancode;
        e.key.down = pressed;
        return e;
    }

    /// <summary>A key of the key page's naming, its US keycode, and its modifiers on the event.</summary>
    static SDL_Event Key(ScriptKeys keys, ScriptKey chord, bool pressed)
    {
        var e = Key((SDL_Keycode)keys.UsKey(chord.Input), (SDL_Scancode)chord.Input, pressed);
        if ((chord.Mods & ScriptKey.Ctrl) != 0) e.key.mod |= SDL_Keymod.SDL_KMOD_LCTRL;
        if ((chord.Mods & ScriptKey.Shift) != 0) e.key.mod |= SDL_Keymod.SDL_KMOD_LSHIFT;
        if ((chord.Mods & ScriptKey.Alt) != 0) e.key.mod |= SDL_Keymod.SDL_KMOD_LALT;
        return e;
    }

    static int Mods(SDL_Keymod m) =>
        ((m & SDL_Keymod.SDL_KMOD_CTRL) != 0 ? ScriptKey.Ctrl : 0)
        | ((m & SDL_Keymod.SDL_KMOD_SHIFT) != 0 ? ScriptKey.Shift : 0)
        | ((m & SDL_Keymod.SDL_KMOD_ALT) != 0 ? ScriptKey.Alt : 0);

    /// <summary>
    /// A text event. Its string is ours to keep alive for as long as SDL may read it, and a harness
    /// run is short, so it is simply never freed.
    /// </summary>
    static SDL_Event Text(string text)
    {
        SDL_Event e = default;
        e.text.type = SDL_EventType.SDL_EVENT_TEXT_INPUT;
        e.text.text = (byte*)Marshal.StringToCoTaskMemUTF8(text);
        return e;
    }
}
