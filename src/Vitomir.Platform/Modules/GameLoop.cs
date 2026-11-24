// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A module's own frame loop.
using Vitomir.Platform.Assets;

namespace Vitomir.Platform.Modules;

/// <summary>
/// **A game that keeps its own time** (ours): a module that offers one (<see cref="IModule.Loop"/>) is run by the
/// program frame by frame instead of through the platform's world and its fixed 120 Hz ticks. The Classic module is the
/// first: 1.1 steps each frame by that frame's own wall-clock time (1 ms to 1 s), so the program hands it the
/// time since the last frame, never ticks. The enhanced game offers none and keeps its own path (<c>Session</c>, the
/// accumulator) unchanged. Everything is called on the program's main thread, in order: <see cref="Start"/> once, then
/// <see cref="Frame"/> once per shown frame until it asks to stop, then <see cref="IDisposable.Dispose"/>.
/// <para><b>The pushed frame</b> (platform API 1.5.0): the program's host runs the frames, the loop answers them. Before
/// each frame the host waits as the last answer asked (<see cref="LoopResult.NextFrameNs"/>), gathers the input since the last
/// frame and hands it in with the frame's time, the picture's size and the focus (<see cref="HostFrame"/>); the loop steps its
/// game once and answers (<see cref="LoopResult"/>): go on, quit, or its run ended and the next begins (a restart, which a host
/// that plays it in a window goes on through and a headless one stops at). A loop played in a window is handed the host's own
/// services at its start (<see cref="LoopContext.Host"/>: the Classic module asks it for the platform renderer's host contract,
/// through which its frame says what to draw and when its picture is done). Nothing here names a GPU: a headless loop draws
/// nothing.</para>
/// </summary>
public interface IGameLoop : IDisposable
{
    /// <summary>Starts the game: what it reads, where it keeps its things, what it was started with (<see cref="LoopContext"/>).</summary>
    void Start(LoopContext context);

    /// <summary>One shown frame: the wall time since the last, the input since then, the picture and the focus; what the host does next.</summary>
    LoopResult Frame(in HostFrame frame);

    /// <summary>
    /// Whether the loop plays in a window when the program's host is handed to it (<see cref="LoopContext.Host"/>; platform API
    /// 1.5.0); a loop that does not runs headless only (<see cref="HeadlessLoop"/>).
    /// </summary>
    bool Windowed => false;
}

/// <summary>
/// What the program gives a module's loop when it starts. The loop finds its own things through it and never
/// through <see cref="AppContext.BaseDirectory"/>: under the platform that is the program's folder, and a module's folder
/// is read only.
/// </summary>
public sealed class LoopContext
{
    /// <summary>The module's own folder (<c>Modules\Classic\</c>), read only: its manifest, <c>bin\</c>, its data and its installed settings.</summary>
    public required string ModuleFolder { get; init; }

    /// <summary>
    /// The module's own user folder, writable (<c>&lt;the platform's user data&gt;\Modules\&lt;id&gt;\</c>, or a test's): its
    /// settings file, its saves and logs later. Made by the program before <see cref="IGameLoop.Start"/>.
    /// </summary>
    public required string UserFolder { get; init; }

    /// <summary>Where the module may keep what it can make again (the scripts' compiled cache): <c>&lt;user folder&gt;\cache\</c> unless given.</summary>
    public string CacheFolder { get => cacheFolder ?? Path.Combine(UserFolder, "cache"); init => cacheFolder = value; }
    string? cacheFolder;

    /// <summary>The player's 1.1 install (<c>oneone:</c>), opened by the program, or null when none was found.</summary>
    public GameInstall? Install { get; init; }

    /// <summary>The 1.1 install's folder (<see cref="GameInstall.Root"/>), or null.</summary>
    public string? GameFolder => Install?.Root;

    /// <summary>The whole command line as given; the module reads the switches it understands from it.</summary>
    public IReadOnlyList<string> Args { get; init; } = [];

    /// <summary>The switches its manifest declares that were given (as the loader gave them to <see cref="IModule.Configure"/>).</summary>
    public IReadOnlySet<string> Switches { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Whether it runs with no window (<see cref="HeadlessLoop"/>): nothing is shown and no picture can be drawn.</summary>
    public bool Headless { get; init; }

    /// <summary>The program's log.</summary>
    public Action<string> Say { get; init; } = _ => { };

    /// <summary>
    /// **Where the module's sound goes** (platform API 1.2.0): the platform's audio output, which the loop pumps from
    /// its source once a frame on this thread (<see cref="Audio.AudioOutput.Pump"/>, with its tape's amount hook when it
    /// has one). The program gives the host's device in a window; headless (and unless given) it has none, and mixes only
    /// what a hook asks, as a muted run does. The program disposes it after the loop.
    /// </summary>
    public Audio.AudioOutput Sound { get; init; } = new();

    /// <summary>
    /// **What the program's host offers a loop it plays in a window** (platform API 1.5.0), by type, or null headless: the
    /// host's own services beside the frames it pushes (the Classic module asks it for the platform renderer's host contract,
    /// <c>IClassicHost</c>: the window, its renderer, the frame's picture, the pointer, the sound device). The platform names no
    /// renderer, so it is handed over untyped and the module asks for what it knows.
    /// </summary>
    public IServiceProvider? Host { get; init; }
}

/// <summary>
/// One shown frame as the program gives it to a module's loop. <see cref="ElapsedNs"/> is the wall-clock time since
/// the last frame (since <see cref="IGameLoop.Start"/> for the first), as measured or as a harness fixes it; the module
/// bounds it as its own rules say. Nothing in it is SDL's: the input is the platform's neutral events.
/// </summary>
public readonly record struct HostFrame
{
    /// <summary>The frame's number, 1 for the first.</summary>
    public long Index { get; init; }

    /// <summary>Nanoseconds of wall-clock time since the last frame.</summary>
    public long ElapsedNs { get; init; }

    /// <summary>
    /// The host's clock when the frame began, nanoseconds (platform API 1.5.0): the clock <see cref="LoopInput.TimeNs"/> is on,
    /// and the one a loop's <see cref="LoopResult.NextFrameNs"/> names; 0 from a host that keeps none (<see cref="HeadlessLoop"/>).
    /// </summary>
    public long TimeNs { get; init; }

    /// <summary>The input since the last frame, oldest first.</summary>
    public IReadOnlyList<LoopInput> Input { get; init; }

    /// <summary>The picture's size in pixels (the window's drawable size; a headless run's notional one).</summary>
    public int PictureWidth { get; init; }

    public int PictureHeight { get; init; }

    /// <summary>Pixels per point (1 on a plain screen).</summary>
    public float Density { get; init; }

    /// <summary>Whether the window has the focus (a headless run always has).</summary>
    public bool Focused { get; init; }

    /// <summary>Whether a picture can be drawn this frame (false headless, or while minimised).</summary>
    public bool CanDraw { get; init; }

    /// <summary>The elapsed time in seconds.</summary>
    public double ElapsedSeconds => ElapsedNs / 1e9;
}

/// <summary>
/// What one neutral input event is (platform API 1.3.0,: the window's events a game
/// reads, and the pointer entering and leaving it). The numbers of the first eleven are kept: they are written on tapes.
/// </summary>
public enum LoopInputKind
{
    KeyDown,
    KeyUp,
    /// <summary>Text typed (<see cref="LoopInput.Text"/>), apart from the keys that made it.</summary>
    Text,
    PointerMove,
    ButtonDown,
    ButtonUp,
    Wheel,
    FocusGained,
    FocusLost,
    /// <summary>The picture's size changed (<see cref="LoopInput.X"/> by <see cref="LoopInput.Y"/>).</summary>
    Resized,
    /// <summary>The player asked the program to end (the last window closed, or the system's own request).</summary>
    Quit,
    /// <summary>The pointer came onto the window.</summary>
    PointerEnter,
    /// <summary>The pointer left the window.</summary>
    PointerLeave,
    /// <summary>The window was moved (<see cref="LoopInput.X"/>, <see cref="LoopInput.Y"/>: its new place).</summary>
    Moved,
    /// <summary>The window was minimised.</summary>
    Minimized,
    /// <summary>The window was maximised.</summary>
    Maximized,
    /// <summary>The window was restored from minimised or maximised.</summary>
    Restored,
    /// <summary>The window went onto another display.</summary>
    DisplayChanged,
    /// <summary>The player asked this window to close (its close box).</summary>
    CloseRequested,
}

/// <summary>
/// The modifier keys held with a key (platform API 1.3.0): a bit each, the left and right of each pair apart, and
/// the lock keys' states. The bits are SDL 3's own (<c>SDL_Keymod</c>), so a host on SDL converts nothing and a tape
/// written by one reads the same; nothing of SDL's is named.
/// </summary>
[Flags]
public enum LoopMods : ushort
{
    None = 0,
    LeftShift = 0x0001,
    RightShift = 0x0002,
    /// <summary>The fifth level's shift (some layouts).</summary>
    Level5 = 0x0004,
    LeftCtrl = 0x0040,
    RightCtrl = 0x0080,
    LeftAlt = 0x0100,
    RightAlt = 0x0200,
    LeftGui = 0x0400,
    RightGui = 0x0800,
    /// <summary>Num Lock on.</summary>
    Num = 0x1000,
    /// <summary>Caps Lock on.</summary>
    Caps = 0x2000,
    /// <summary>AltGr's mode key.</summary>
    Mode = 0x4000,
    /// <summary>Scroll Lock on.</summary>
    Scroll = 0x8000,
    Shift = LeftShift | RightShift,
    Ctrl = LeftCtrl | RightCtrl,
    Alt = LeftAlt | RightAlt,
    Gui = LeftGui | RightGui,
}

/// <summary>
/// **A key's position** (platform API 1.3.0): <see cref="LoopInput.Code"/> of a key event, the USB HID usage number
/// of the key wherever it is on the player's layout (the numbers SDL calls scan codes, and <c>InputSource.Key</c>'s).
/// The ones a game names; any other is its usage number all the same.
/// </summary>
public static class LoopScancode
{
    public const int A = 4, B = 5, C = 6, D = 7, E = 8, F = 9, G = 10, H = 11, I = 12, J = 13, K = 14, L = 15, M = 16, N = 17,
                     O = 18, P = 19, Q = 20, R = 21, S = 22, T = 23, U = 24, V = 25, W = 26, X = 27, Y = 28, Z = 29,
                     Digit1 = 30, Digit2 = 31, Digit3 = 32, Digit4 = 33, Digit5 = 34, Digit6 = 35, Digit7 = 36, Digit8 = 37,
                     Digit9 = 38, Digit0 = 39,
                     Return = 40, Escape = 41, Backspace = 42, Tab = 43, Space = 44, Minus = 45, EqualsKey = 46, LeftBracket = 47,
                     RightBracket = 48, Backslash = 49, Semicolon = 51, Apostrophe = 52, Grave = 53, Comma = 54, Period = 55,
                     Slash = 56, CapsLock = 57,
                     F1 = 58, F2 = 59, F3 = 60, F4 = 61, F5 = 62, F6 = 63, F7 = 64, F8 = 65, F9 = 66, F10 = 67, F11 = 68, F12 = 69,
                     PrintScreen = 70, ScrollLock = 71, Pause = 72, Insert = 73, Home = 74, PageUp = 75, Delete = 76, End = 77,
                     PageDown = 78, Right = 79, Left = 80, Down = 81, Up = 82, NumLock = 83,
                     KeypadEnter = 88, Keypad1 = 89, Keypad2 = 90, Keypad3 = 91, Keypad4 = 92, Keypad5 = 93, Keypad6 = 94,
                     Keypad7 = 95, Keypad8 = 96, Keypad9 = 97, Keypad0 = 98,
                     LeftCtrl = 224, LeftShift = 225, LeftAlt = 226, LeftGui = 227, RightCtrl = 228, RightShift = 229,
                     RightAlt = 230, RightGui = 231;
}

/// <summary>
/// **What a key types on the player's layout** (platform API 1.3.0): <see cref="LoopInput.Key"/> of a key event. A
/// key that types a character is that character (a letter in lower case); a key that types none is its position
/// (<see cref="LoopScancode"/>) with <see cref="NoCharacter"/> added. These are SDL 3's key codes, so a host on SDL
/// converts nothing; the ones a game names are here.
/// </summary>
public static class LoopKey
{
    /// <summary>Added to a key's position when the key types no character.</summary>
    public const int NoCharacter = 0x40000000;

    public const int Return = '\r', Escape = 0x1b, Backspace = '\b', Tab = '\t', Space = ' ', Grave = '`',
                     A = 'a', B = 'b', C = 'c', D = 'd', E = 'e', F = 'f', G = 'g', H = 'h', I = 'i', J = 'j', K = 'k', L = 'l',
                     M = 'm', N = 'n', O = 'o', P = 'p', Q = 'q', R = 'r', S = 's', T = 't', U = 'u', V = 'v', W = 'w', X = 'x',
                     Y = 'y', Z = 'z',
                     Digit0 = '0', Digit1 = '1', Digit2 = '2', Digit3 = '3', Digit4 = '4', Digit5 = '5', Digit6 = '6',
                     Digit7 = '7', Digit8 = '8', Digit9 = '9',
                     F1 = NoCharacter | LoopScancode.F1, F2 = NoCharacter | LoopScancode.F2, F3 = NoCharacter | LoopScancode.F3,
                     F4 = NoCharacter | LoopScancode.F4, F5 = NoCharacter | LoopScancode.F5, F6 = NoCharacter | LoopScancode.F6,
                     F7 = NoCharacter | LoopScancode.F7, F8 = NoCharacter | LoopScancode.F8, F9 = NoCharacter | LoopScancode.F9,
                     F10 = NoCharacter | LoopScancode.F10, F11 = NoCharacter | LoopScancode.F11, F12 = NoCharacter | LoopScancode.F12,
                     Pause = NoCharacter | LoopScancode.Pause, PageUp = NoCharacter | LoopScancode.PageUp,
                     PageDown = NoCharacter | LoopScancode.PageDown, Right = NoCharacter | LoopScancode.Right,
                     Left = NoCharacter | LoopScancode.Left, Down = NoCharacter | LoopScancode.Down, Up = NoCharacter | LoopScancode.Up,
                     KeypadEnter = NoCharacter | LoopScancode.KeypadEnter;
}

/// <summary>The mouse's buttons by the platform's numbers (<see cref="LoopInput.Code"/> of a button event).</summary>
public static class LoopButton
{
    public const int Left = 1, Middle = 2, Right = 3, X1 = 4, X2 = 5;

    /// <summary>A button's bit in <see cref="LoopInput.Buttons"/> and in a held-buttons mask.</summary>
    public static uint Mask(int button) => button is > 0 and <= 32 ? 1u << (button - 1) : 0u;
}

/// <summary>
/// One input event in the platform's own terms (platform API 1.3.0): a key by its position
/// (<see cref="LoopScancode"/>) and what it types (<see cref="LoopKey"/>) with the modifiers held (<see cref="LoopMods"/>),
/// a mouse button by the platform's number (<see cref="LoopButton"/>), the pointer's place on the picture in pixels and its
/// movement (relative: what it moved by, which is all there is while the pointer is held for a swivel), the wheel's turn,
/// text, and the window's own events (<see cref="LoopInputKind"/>).
/// </summary>
public readonly record struct LoopInput(LoopInputKind Kind)
{
    /// <summary>A key's position (<see cref="LoopScancode"/>) or a mouse button's number (<see cref="LoopButton"/>).</summary>
    public int Code { get; init; }

    /// <summary>What a key types on the player's layout (<see cref="LoopKey"/>).</summary>
    public int Key { get; init; }

    /// <summary>The modifiers held with a key (<see cref="LoopMods"/>).</summary>
    public LoopMods Mods { get; init; }

    /// <summary>A key held down and repeating.</summary>
    public bool Repeat { get; init; }

    /// <summary>
    /// The pointer's place (a move's, a button's, the wheel's), a resize's width and height, a move's new place, or a
    /// window event's two numbers as the host had them.
    /// </summary>
    public float X { get; init; }

    public float Y { get; init; }

    /// <summary>The pointer's movement, or the wheel's turn (up positive in <see cref="DY"/>, as the system reports it).</summary>
    public float DX { get; init; }

    public float DY { get; init; }

    /// <summary>The mouse buttons held during a move, a bit each (<see cref="LoopButton.Mask"/>).</summary>
    public uint Buttons { get; init; }

    /// <summary>The text typed, for <see cref="LoopInputKind.Text"/>.</summary>
    public string Text { get; init; } = "";

    /// <summary>When it happened, nanoseconds on the program's clock.</summary>
    public long TimeNs { get; init; }

    /// <summary>A key event (down or up).</summary>
    public bool IsKey => Kind is LoopInputKind.KeyDown or LoopInputKind.KeyUp;

    /// <summary>A mouse button event (down or up).</summary>
    public bool IsButton => Kind is LoopInputKind.ButtonDown or LoopInputKind.ButtonUp;
}

/// <summary>What a loop asks after a frame.</summary>
public enum LoopVerdict
{
    /// <summary>Go on to the next frame.</summary>
    Continue,
    /// <summary>The game is over: stop, with <see cref="LoopResult.ExitCode"/>.</summary>
    Quit,
    /// <summary>Start the program's run again with <see cref="LoopResult.Args"/> (1.1's new place in a network game, a new game).</summary>
    Restart,
}

/// <summary>A loop's answer to a frame (and when the next frame may begin).</summary>
public readonly record struct LoopResult(LoopVerdict Verdict, int ExitCode = 0, IReadOnlyList<string>? Args = null, string Reason = "")
{
    /// <summary>
    /// **The host's wait** (platform API 1.5.0): the host's clock (<see cref="HostFrame.TimeNs"/>) the next frame begins no
    /// sooner than, or 0 for at once. A game that holds its frames to a length (1.1's console <c>maxfps</c>) or idles behind a
    /// window without the focus (1.1 runs no frame there) says so here, and the host waits; the loop itself never waits.
    /// </summary>
    public long NextFrameNs { get; init; }

    public static LoopResult Continue => new(LoopVerdict.Continue);

    /// <summary>Go on, the next frame no sooner than <paramref name="timeNs"/> on the host's clock.</summary>
    public static LoopResult ContinueAt(long timeNs) => new(LoopVerdict.Continue) { NextFrameNs = timeNs };

    public static LoopResult Quit(int exitCode = 0, string reason = "") => new(LoopVerdict.Quit, exitCode, null, reason);

    public static LoopResult Restart(IReadOnlyList<string> args, string reason = "") => new(LoopVerdict.Restart, 0, args, reason);
}

/// <summary>
/// **A module's loop run with no window**: for tests and for the program's <c>--headless</c> with a game whose
/// module offers a loop. Each frame is given its wall time from <paramref name="frameNs"/> (a harness's fixed step, or a
/// recording's), no input, a notional picture that cannot be drawn, and the focus. It stops at the end of the times or at
/// the first frame that does not ask to go on, and disposes the loop.
/// </summary>
public static class HeadlessLoop
{
    /// <summary>The picture a headless frame says it has unless told: 1.1's own 640x480 sheet (nothing is drawn).</summary>
    public const int PictureWidth = 640, PictureHeight = 480;

    /// <summary>How a headless run ended: the frames run and the last answer (<see cref="LoopResult.Continue"/> when the times ran out).</summary>
    public readonly record struct Ran(long Frames, LoopResult Last);

    /// <summary>
    /// Starts <paramref name="loop"/> with <paramref name="context"/> and runs a frame for each time given (nanoseconds), each
    /// saying its picture is <paramref name="width"/> by <paramref name="height"/> (what a window of that size would show:
    /// a game that works out its view from the picture, as 1.1's portal walk does, sees the same; nothing is drawn).
    /// </summary>
    public static Ran Run(IGameLoop loop, LoopContext context, IEnumerable<long> frameNs, int width = PictureWidth, int height = PictureHeight)
    {
        ArgumentNullException.ThrowIfNull(loop);
        Directory.CreateDirectory(context.UserFolder);
        using (loop)
        {
            loop.Start(context);
            long index = 0;
            var last = LoopResult.Continue;
            foreach (long ns in frameNs)
            {
                var frame = new HostFrame
                {
                    Index = ++index, ElapsedNs = ns, Input = [], PictureWidth = width, PictureHeight = height,
                    Density = 1f, Focused = true, CanDraw = false,
                };
                last = loop.Frame(in frame);
                if (last.Verdict != LoopVerdict.Continue) return new Ran(index, last);
            }
            return new Ran(index, last);
        }
    }

    /// <summary><paramref name="count"/> frames of <paramref name="milliseconds"/> each (a harness's fixed step).</summary>
    public static IEnumerable<long> Fixed(int count, double milliseconds)
    {
        long ns = (long)Math.Round(milliseconds * 1_000_000.0);
        for (int i = 0; i < count; i++) yield return ns;
    }
}
