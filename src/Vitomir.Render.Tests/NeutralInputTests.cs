// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The platform's neutral input against SDL's: the same numbers, and the old tapes read as the host reads SDL.
using System.Reflection;
using System.Runtime.InteropServices;
using SDL;
using Vitomir.Engine.Diagnostics;
using Vitomir.Platform.Modules;
using static SDL.SDL3;

namespace Vitomir.Render.Tests;

/// <summary>
/// **The neutral input is SDL's, renamed**: the platform's modifier bits, key
/// positions, key codes and button numbers are SDL 3's own, so a host converts nothing; and **an old
/// tape reads as the live host would have heard it**: every <c>ev</c> line of the baseline tapes (and one of each kind
/// the first format wrote), built back into the SDL event that format's replay built of it, made a neutral event by the host's own
/// conversion (<c>SdlInput.From</c> in the built <c>Vitomir.Platform.Render.dll</c> beside vitomir-tools), is the event <see cref="TapeInput"/> reads from the
/// line. That is the promise that lets the baselines replay unchanged (a comparison with 1.1's risk 2). No GPU needed; the program's
/// half is skipped when it is not built beside the tests.
/// </summary>
public unsafe class NeutralInputTests
{
    static string SdlName(string ours, string prefix)
    {
        if (ours.StartsWith("Digit", StringComparison.Ordinal)) return prefix + ours[5..];
        if (ours.StartsWith("Keypad", StringComparison.Ordinal)) return prefix + "KP_" + ours[6..].ToUpperInvariant();
        string upper = ours switch
        {
            "EqualsKey" => "EQUALS", "NumLock" => "NUMLOCKCLEAR",
            "LeftCtrl" => "LCTRL", "LeftShift" => "LSHIFT", "LeftAlt" => "LALT", "LeftGui" => "LGUI",
            "RightCtrl" => "RCTRL", "RightShift" => "RSHIFT", "RightAlt" => "RALT", "RightGui" => "RGUI",
            _ => ours.ToUpperInvariant(),
        };
        return prefix + upper;
    }

    static IEnumerable<(string Name, int Value)> Constants(Type t) =>
        t.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral && f.FieldType == typeof(int))
         .Select(f => (f.Name, (int)f.GetRawConstantValue()!));

    [Fact]
    public void The_platform_s_input_numbers_are_SDL_3_s()
    {
        foreach (var (name, value) in Constants(typeof(LoopScancode)))
            Assert.True(value == (int)Enum.Parse<SDL_Scancode>(SdlName(name, "SDL_SCANCODE_")), $"LoopScancode.{name}");
        foreach (var (name, value) in Constants(typeof(LoopKey)).Where(c => c.Name != nameof(LoopKey.NoCharacter)))
            Assert.True((uint)value == (uint)Enum.Parse<SDL_Keycode>(SdlName(name, "SDLK_")), $"LoopKey.{name}");
        Assert.Equal(0x40000000, LoopKey.NoCharacter);   // SDLK_SCANCODE_MASK
        var mods = new (LoopMods Ours, SDL_Keymod Sdl)[]
        {
            (LoopMods.LeftShift, SDL_Keymod.SDL_KMOD_LSHIFT), (LoopMods.RightShift, SDL_Keymod.SDL_KMOD_RSHIFT),
            (LoopMods.Level5, SDL_Keymod.SDL_KMOD_LEVEL5), (LoopMods.LeftCtrl, SDL_Keymod.SDL_KMOD_LCTRL),
            (LoopMods.RightCtrl, SDL_Keymod.SDL_KMOD_RCTRL), (LoopMods.LeftAlt, SDL_Keymod.SDL_KMOD_LALT),
            (LoopMods.RightAlt, SDL_Keymod.SDL_KMOD_RALT), (LoopMods.LeftGui, SDL_Keymod.SDL_KMOD_LGUI),
            (LoopMods.RightGui, SDL_Keymod.SDL_KMOD_RGUI), (LoopMods.Num, SDL_Keymod.SDL_KMOD_NUM),
            (LoopMods.Caps, SDL_Keymod.SDL_KMOD_CAPS), (LoopMods.Mode, SDL_Keymod.SDL_KMOD_MODE),
            (LoopMods.Scroll, SDL_Keymod.SDL_KMOD_SCROLL), (LoopMods.Shift, SDL_Keymod.SDL_KMOD_SHIFT),
            (LoopMods.Ctrl, SDL_Keymod.SDL_KMOD_CTRL), (LoopMods.Alt, SDL_Keymod.SDL_KMOD_ALT), (LoopMods.Gui, SDL_Keymod.SDL_KMOD_GUI),
        };
        foreach (var (ours, sdl) in mods) Assert.True((ushort)ours == (ushort)sdl, ours.ToString());
        Assert.Equal(Enum.GetValues<LoopMods>().Length - 1, mods.Length);       // every bit and pair named, None aside
        Assert.Equal([SDL_BUTTON_LEFT, SDL_BUTTON_MIDDLE, SDL_BUTTON_RIGHT, SDL_BUTTON_X1, SDL_BUTTON_X2],
                     new[] { LoopButton.Left, LoopButton.Middle, LoopButton.Right, LoopButton.X1, LoopButton.X2 });
        Assert.Equal((uint)SDL_MouseButtonFlags.SDL_BUTTON_LMASK, LoopButton.Mask(LoopButton.Left));
        Assert.Equal((uint)SDL_MouseButtonFlags.SDL_BUTTON_X2MASK, LoopButton.Mask(LoopButton.X2));
    }

    // ---- the old tapes, through the host's conversion --------------------------------------------------------------

    static string? Src()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Vitomir.slnx"))) return dir.FullName;
        return null;
    }

    /// <summary>
    /// The host's conversion, <c>SdlInput.From</c>, out of the built tools' program beside the tests; null when it is not built.
    /// It is the platform renderer's (<c>Vitomir.Platform.Render.Windowing.SdlInput</c>, every host's), taken from
    /// vitomir-tools' own folder.
    /// </summary>
    static Func<SDL_Event, LoopInput?>? HostConversion()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var dll = Src() is { } src ? Path.Combine(src, "Vitomir.Tools", "bin", dir.Parent!.Name, dir.Name, "Vitomir.Platform.Render.dll") : null;
        if (dll is null || !File.Exists(dll)) return null;
        var from = Assembly.LoadFrom(dll).GetType("Vitomir.Platform.Render.Windowing.SdlInput", throwOnError: true)!.GetMethod("From", BindingFlags.Public | BindingFlags.Static)!;
        return e => (LoopInput?)from.Invoke(null, [e]);
    }

    /// <summary>The SDL event the first format's replay built of an <c>ev</c> line (its <c>InputTape.Build</c>, the window's resize aside).</summary>
    static SDL_Event Built(TapeLine ev, List<nint> texts)
    {
        SDL_Event e = default;
        var f = ev.Fields;
        ulong t = (ulong)ev.Long("t");
        switch (f[0])
        {
            case "key":
                e.key.type = f[1] == "down" ? SDL_EventType.SDL_EVENT_KEY_DOWN : SDL_EventType.SDL_EVENT_KEY_UP;
                e.key.down = f[1] == "down";
                e.key.scancode = (SDL_Scancode)ev.Long("sc");
                e.key.key = (SDL_Keycode)(uint)ev.Long("key");
                e.key.mod = (SDL_Keymod)(ushort)ev.Long("mod");
                e.key.repeat = ev.Long("rep") != 0;
                e.key.raw = (ushort)ev.Long("raw");
                e.key.which = (SDL_KeyboardID)(uint)ev.Long("which");
                e.key.windowID = (SDL_WindowID)(uint)ev.Long("win");
                e.key.timestamp = t;
                break;
            case "button":
                e.button.type = f[1] == "down" ? SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN : SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP;
                e.button.down = f[1] == "down";
                e.button.button = (byte)ev.Long("b");
                e.button.x = ev.Float("x"); e.button.y = ev.Float("y");
                e.button.clicks = (byte)ev.Long("clicks");
                e.button.which = (SDL_MouseID)(uint)ev.Long("which");
                e.button.windowID = (SDL_WindowID)(uint)ev.Long("win");
                e.button.timestamp = t;
                break;
            case "motion":
                e.motion.type = SDL_EventType.SDL_EVENT_MOUSE_MOTION;
                e.motion.x = ev.Float("x"); e.motion.y = ev.Float("y");
                e.motion.xrel = ev.Float("dx"); e.motion.yrel = ev.Float("dy");
                e.motion.state = (SDL_MouseButtonFlags)(uint)ev.Long("state");
                e.motion.which = (SDL_MouseID)(uint)ev.Long("which");
                e.motion.windowID = (SDL_WindowID)(uint)ev.Long("win");
                e.motion.timestamp = t;
                break;
            case "wheel":
                e.wheel.type = SDL_EventType.SDL_EVENT_MOUSE_WHEEL;
                e.wheel.x = ev.Float("x"); e.wheel.y = ev.Float("y");
                e.wheel.direction = (SDL_MouseWheelDirection)ev.Long("dir");
                e.wheel.mouse_x = ev.Float("mx"); e.wheel.mouse_y = ev.Float("my");
                e.wheel.integer_x = (int)ev.Long("ix"); e.wheel.integer_y = (int)ev.Long("iy");
                e.wheel.which = (SDL_MouseID)(uint)ev.Long("which");
                e.wheel.windowID = (SDL_WindowID)(uint)ev.Long("win");
                e.wheel.timestamp = t;
                break;
            case "text":
                e.text.type = SDL_EventType.SDL_EVENT_TEXT_INPUT;
                var text = Marshal.StringToCoTaskMemUTF8(ReplayTape.Unescape(f[1]));
                texts.Add(text);
                e.text.text = (byte*)text;
                e.text.windowID = (SDL_WindowID)(uint)ev.Long("win");
                e.text.timestamp = t;
                break;
            case "quit":
                e.quit.type = SDL_EventType.SDL_EVENT_QUIT;
                e.quit.timestamp = t;
                break;
            case "window":
                e.window.type = (SDL_EventType)(uint)ev.Long("type");
                e.window.data1 = (int)ev.Long("d1"); e.window.data2 = (int)ev.Long("d2");
                e.window.windowID = (SDL_WindowID)(uint)ev.Long("win");
                e.window.timestamp = t;
                break;
            case "raw":
                var bytes = Convert.FromHexString(f[1]);
                bytes.AsSpan(0, Math.Min(bytes.Length, sizeof(SDL_Event))).CopyTo(new Span<byte>(&e, sizeof(SDL_Event)));
                break;
        }
        return e;
    }

    /// <summary>One line of each kind the first format wrote, and the window's events by every number SDL has for one.</summary>
    static IEnumerable<string> Made()
    {
        yield return "ev key down sc=4 key=0x61 mod=0x3041 rep=1 raw=30 which=7 win=2 t=99";
        yield return "ev key up sc=72 key=0x40000048 mod=0x0 rep=0 raw=0 which=0 win=2 t=100";
        yield return "ev button down b=5 x=-3.25 y=1e-05 clicks=3 which=1 win=2 t=1";
        yield return "ev motion x=640 y=0 dx=-0 dy=17.5 state=21 which=1 win=2 t=2";
        yield return "ev wheel x=-0.5 y=2 dir=1 mx=33 my=44 ix=0 iy=2 which=1 win=2 t=3";
        yield return "ev text G%C3%BCnther%20%3D%25 win=2 t=4";
        yield return "ev quit t=5";
        for (uint type = (uint)SDL_EventType.SDL_EVENT_WINDOW_FIRST; type <= (uint)SDL_EventType.SDL_EVENT_WINDOW_LAST; type++)
            yield return $"ev window type=0x{type:x} d1=12 d2=-34 win=2 t=6";
        yield return "ev raw " + "00110000" + new string('0', 2 * 124);
    }

    [Fact]
    public void An_old_tape_s_events_read_as_the_host_makes_them_of_SDL_s()
    {
        var from = HostConversion();
        Assert.SkipWhen(from is null, "vitomir-tools is not built beside the tests (build src/Vitomir.Tools/Vitomir.Tools.csproj in the same configuration)");
        var lines = new List<string>(Made());
        var tapes = Directory.GetFiles(Path.Combine(Src()!, "Vitomir.Engine.Tests", "Replays"), "*.tape");
        Assert.True(tapes.Length >= 17, $"only {tapes.Length} tapes");
        foreach (var tape in tapes)
        {
            using var reader = LogFile.OpenText(tape);
            while (reader.ReadLine() is { } text) if (text.StartsWith("ev ", StringComparison.Ordinal)) lines.Add(text);
        }
        Assert.True(lines.Count > 500, $"only {lines.Count} lines");
        var texts = new List<nint>();
        try
        {
            int events = 0;
            foreach (var text in lines)
            {
                var line = ReplayTape.Parse(text)!.Value;
                var host = from!(Built(line, texts));
                Assert.True(host == TapeInput.Read(line), $"{text}: the host makes {host?.ToString() ?? "none"}, the tape reads {TapeInput.Read(line)?.ToString() ?? "none"}");
                if (host is not null) events++;
            }
            Assert.True(events > 500, $"only {events} events");
        }
        finally { foreach (var t in texts) Marshal.FreeCoTaskMem(t); }
    }
}
