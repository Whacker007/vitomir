// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What 1.1's frame controller asks of its host (once in the old program; now here, public, so the platform's program can be
// one): the host contract.
using Vitomir.Platform.Audio;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Render.Classic;

/// <summary>What a run asks of the window: the switches that shape it.</summary>
/// <param name="Headless">no window at all (a still)</param>
/// <param name="Width">the picture's width</param>
/// <param name="Height">the picture's height</param>
/// <param name="SizeGiven">whether <c>--size</c> named it</param>
/// <param name="Windowed"><c>--window</c></param>
/// <param name="FullScreen"><c>--fullscreen</c></param>
/// <param name="Harness">a harness's run (<c>--ui-click</c>, <c>--frames</c>, <c>--frame-ms</c>, <c>--shot</c>)</param>
/// <param name="Borderless"><c>--borderless</c>'s value</param>
/// <remarks>
///: the window code is the host's, in the platform's renderer, for both programs; what it
/// asked of the game itself until then — the tape, the settings' file, the log — the game now says here.
/// </remarks>
public readonly record struct WindowAsk(bool Headless, int Width, int Height, bool SizeGiven, bool Windowed, bool FullScreen, bool Harness, string? Borderless)
{
    /// <summary>The run replays a tape: its window is a plain one, at its tape's size, never kept and never full screen.</summary>
    public bool Replaying { get; init; }
    /// <summary>A replay nobody watches (a check, or <c>--replay-fast</c>): drawn at its recording's size and shown small.</summary>
    public bool Unwatched { get; init; }
    /// <summary><c>--replay-full</c>: an unwatched replay shown at its own size.</summary>
    public bool ReplayFull { get; init; }
    /// <summary><c>--replay-slot</c>: where among several unwatched replays (<c>--replay-folder</c>) this one's window stands.</summary>
    public int ReplaySlot { get; init; }
    /// <summary>Where the window keeps what it keeps between starts, <c>[window]</c> (null: nothing is kept).</summary>
    public Vitomir.Platform.Windowing.IWindowSettings? Settings { get; init; }
    /// <summary>The game's log for the window's lines (they are said on the console too).</summary>
    public Action<string>? Log { get; init; }
}

/// <summary>What a run asks of the GPU: <c>--gpu</c>'s driver, <c>--gpu-debug</c> (with the log on), <c>--no-vsync</c>, and
/// whether it is a replay nobody watches, which runs on its tape's steps and is drawn as fast as the machine can:
/// <paramref name="Unwatched"/> the log's word for it (<c>replay-check</c> or <c>replay</c>), null for any other run.</summary>
public readonly record struct GpuAsk(string? Driver, bool Debug, bool NoVsync, string? Unwatched = null);

/// <summary>
/// **What 1.1's frame controller asks of its host** (public and the platform's, so
/// that <c>vitomir-platform --game classic</c> and <c>vitomir-tools</c> are each one): the runs (quit, or restart with another
/// run's arguments), the window and its GPU, the picture's size, a frame's picture acquired and presented, the input's live
/// side (the platform's neutral events), the clock, the pointer and the keyboard's focus, the window's modes for the
/// Options screen, a harness's scripted input, the sound device. Every type in it is the platform's: the controller
/// (<c>Vitomir.Classic</c>, the Classic module's library) reaches SDL through this and nothing else.
/// <para><b>The pushed frame</b> (platform API 1.5.0): the host runs the frames (the platform's
/// <c>IGameLoop.Frame</c>) and hands each in with its input and its time; this is what a frame asks of the host besides.
/// What the controller pulled until then went: the films' raw poll (the frame's input is handed in, read in order by the
/// films and the tape alike), the waits (the host's, before the frame a loop's answer names) and the windowed game's door
/// (<c>IClassicGame.Play</c>: the module's loop is handed this host at its start, <c>LoopContext.Host</c>). What a frame still
/// asks for here is the picture, where it draws: 1.1 steps its world only on a frame whose picture can be had (the tape's
/// <c>drawn</c>), shows the loading plaque over the last frame in the middle of a frame and does the frame's
/// last work after its picture is shown, so the frame says when its picture is wanted (<see cref="Acquire"/>) and when it is
/// done (<see cref="Submit"/>), and the host's renderer draws what it says in between.</para>
/// </summary>
public unsafe interface IClassicHost
{
    // ---- the runs ------------------------------------------------------------------------------------
    /// <summary>The next run's arguments: set, the program goes on to that run when this one ends (a new game, a
    /// load, the main menu); left null, it ends with this one.</summary>
    string[]? NextRun { get; set; }
    /// <summary>A message the next run's main menu shows (a load that failed).</summary>
    string? MenuNotice { get; set; }
    /// <summary>The runs before this one (the opening films play only in the first); the program's loop of runs counts them.</summary>
    int Runs { get; set; }
    /// <summary>Whether the keyboard's text is on for a field of the menus (kept across the runs).</summary>
    bool Typing { get; set; }
    /// <summary>
    /// **A run that is all text, with no window** (multiplayer's windowless runs, <c>--headless-server</c> and
    /// <c>--net-bot</c>): the host gives it somewhere to print and, for a host's console, to read typed lines from — on Windows,
    /// with <paramref name="ownWindow"/> (the dedicated host) a console window of its own, named
    /// <paramref name="title"/>, never the terminal it was started from; without it the terminal it was started from, if any. Nothing
    /// when its output is redirected (a pipe, a file), and nothing elsewhere than Windows. Answers the one line saying what was done
    /// (<c>Windowing.TextConsole</c>; the host has said it in its own log), which the run writes to the game's log; null when there
    /// is nothing to say. A host with no such thing does nothing.
    /// </summary>
    string? TextConsole(bool ownWindow, string? title = null) => null;

    /// <summary>
    /// Whether the console window of its own (<see cref="TextConsole"/>) was closed by its X, or Ctrl+C or Ctrl+Break pressed in it:
    /// the run stops as its <c>/quit</c> does, within the few seconds Windows allows. Read every frame.
    /// </summary>
    bool TextConsoleClosed => false;

    /// <summary>
    /// **The game's log window** (a developer's: <c>--log-window</c>, <c>[log] window = on</c> in the game's
    /// <c>vitomir.ini</c>, or the console's <c>logwindow</c>): on Windows, for a game in a window, a viewer of its own (the same
    /// program started with <c>--log-viewer</c>, a process of its own whose console window is titled <c>Vitomir log: &lt;game&gt;</c>)
    /// showing the game's lines (those from before it opened first) and whose X ends only it
    /// (<c>Windowing.TextConsole.LogWindowSet</c>). <paramref name="on"/>: true opens it, false closes it, null toggles. Answers the
    /// line saying what was done (said in the host's log too), null when there is nothing to say. A host with no such thing does nothing.
    /// </summary>
    string? LogWindow(string game, bool? on = true) => null;

    // ---- the window and the GPU ------------------------------------------------------------------------
    /// <summary>The window, made by the first run that asks and kept by the rest; its title set. False headless (no window).</summary>
    bool OpenWindow(string title, WindowAsk ask);
    /// <summary>
    /// The GPU, made by the first run that asks and kept by the rest, as **the host's renderer for 1.1's frames**.
    /// The module never draws): the controller asks it for its drawers and hands it each frame to draw
    /// (<see cref="IClassicRenderer"/>).
    /// </summary>
    IClassicRenderer OpenGpu(GpuAsk ask);
    /// <summary>
    /// Why the host's renderer stopped the run, or null (ours: a cell level Direct3D 12 could not draw, which only
    /// <c>vitomir-tools</c> draw): the game's loop ends on it, the program's loop of runs too, and the process answers
    /// <see cref="StoppedExitCode"/>.
    /// </summary>
    string? Stopped { get; }
    /// <summary>The process's exit code when the host's renderer stopped a run (<see cref="Stopped"/>).</summary>
    const int StoppedExitCode = 3;
    /// <summary>Whether there is a window (none headless or before it is made).</summary>
    bool HasWindow { get; }
    /// <summary>The GPU's driver's name, or "?" before there is one.</summary>
    string GpuDriver { get; }
    /// <summary>The picture's size, which is the window's when there are no bars.</summary>
    bool PictureSize(int* w, int* h);
    /// <summary>A frame's picture, asked where the frame draws: a command buffer and the window's texture, when one can be had.</summary>
    GpuPicture Acquire();
    /// <summary>The picture done: the frame's commands sent and its picture shown (the host's).</summary>
    void Submit(in GpuPicture picture);
    /// <summary>
    /// Every frame's picture kept where it can be read back (ours, a frame dump's: <c>--frame-dump</c> in a window's loop): the
    /// picture drawn into a target of its own and copied to the window, as it is with bars.
    /// </summary>
    void KeepPictures() { }
    /// <summary>
    /// The picture as last shown, RGBA at its own size (ours, a frame dump's), read back from the frame the screen got so
    /// nothing is drawn again for it; null when none can be had (before <see cref="KeepPictures"/>, or no window).
    /// </summary>
    byte[]? LastPicture(out int width, out int height) { width = height = 0; return null; }
    /// <summary>The window's own frame (the grip, a dragged size put back; ours).</summary>
    void KeepFrame();
    /// <summary>The window's place, written once it stays put (ours).</summary>
    void KeepPlace();
    /// <summary>A borderless window on top while it has the focus (ours).</summary>
    void KeepOnTop();
    /// <summary>The size the world is drawn at this frame when it is not the picture's own (borderless full screen's).</summary>
    (int Width, int Height)? WorldSize();
    /// <summary>The window's title.</summary>
    void Title(string title);
    /// <summary>The window's modes and sizes for the Options screen; null when there is no window.</summary>
    IWindowModes? Modes { get; }
    /// <summary>
    /// Where the window keeps what it keeps between starts when the host keeps it (the window's settings
    /// are the platform's, kept per game: <c>vitomir-platform</c>'s <c>Games\&lt;id&gt;\window.json</c>); null when the game keeps
    /// them itself (<c>vitomir-tools</c>: its <c>vitomir.ini</c>'s <c>[window]</c>). The game asks it for its window's ask
    /// (<see cref="WindowAsk.Settings"/>) and never writes it in a replay.
    /// </summary>
    Vitomir.Platform.Windowing.IWindowSettings? WindowSettings { get; }
    /// <summary>The end of the program: the GPU released, the window's place written, the window gone.</summary>
    void Close();

    // ---- the input, neutral ------------------------------------------------------------------
    /// <summary>
    /// The input's live side: what the binding layer asks of the window (the keys and buttons held, the modifiers, the picture's
    /// size, the clipboard). Its events are the host's to gather: the frame is handed them, and reads them there.
    /// </summary>
    IInputHost Input { get; }
    /// <summary>An event the window's own keeping hears (moved or resized: kept).</summary>
    void Heard(in LoopInput e);
    /// <summary>Whether a key is held now (<see cref="LoopScancode"/>), straight from the system (the portrait's turn, never on a tape).</summary>
    bool KeyDown(int scancode);
    /// <summary>A harness's scripted input (<c>--ui-click</c>'s steps, <c>--ui-type</c>'s words), pushed into the host's own queue a
    /// frame at a time, its keys named as <paramref name="keys"/> names them (the game's key names); null for none.</summary>
    IScriptedInput? Script(string? steps, string? typed, ScriptKeys keys);

    // ---- the clock -----------------------------------------------------------------------------------
    /// <summary>
    /// The wall clock, nanoseconds: the clock a frame's time is on (<c>HostFrame.TimeNs</c>). A frame reads its time from the
    /// frame; this is for what 1.1 reads as it happens — the clock started again when a run or a scene has started (a load
    /// inside the frame), and how long a film was shown, for the log.
    /// </summary>
    ulong Now();
    /// <summary>The wall clock, milliseconds (the portrait's turn).</summary>
    ulong Ticks();

    // ---- the pointer and the keyboard's focus ------------------------------------------------------------
    /// <summary>The system's pointer shown or hidden.</summary>
    void ShowCursor(bool shown);
    /// <summary>The pointer held in the window and its motion relative (the swivel's), or let go.</summary>
    void RelativeMouse(bool on);
    /// <summary>The pointer put at a point of the picture.</summary>
    void Warp(float x, float y);
    /// <summary>The pointer kept inside the window, or let go.</summary>
    void ConfineMouse(bool on);
    /// <summary>The system keys held while the window has the focus, and Alt+Tab let through or not (QoL <c>winkey</c>, <c>alttab</c>).</summary>
    void SystemKeys(bool winKey, bool altTab);
    /// <summary>Whether the system's text input is on in the window.</summary>
    bool TextInputActive { get; }
    /// <summary>The system's text input on or off in the window.</summary>
    void TextInput(bool on);
    /// <summary>The clipboard's text, or null.</summary>
    string? Clipboard();

    // ---- the sound device ------------------------------------------------------------------------------
    /// <summary>The platform's audio output with the host's device behind it, at the source's format (or one with no device, said why).</summary>
    AudioOutput Speakers(AudioFormat format);
}

/// <summary>
/// **The live input, as the host has it** (public and the platform's): what the tape reads when it records or
/// runs with no tape, and what the binding layer asks of the window, in the platform's neutral terms. A replay reads none of the
/// input from here, only whether its window was closed, and puts its window to the recorded sizes through it.
/// </summary>
public interface IInputHost
{
    /// <summary>The next event the game is to hear, its pointer in the picture's pixels; false when there are no more now.</summary>
    bool Poll(out LoopInput e);

    /// <summary>A replay's turn: the host's own events thrown away; true when the player closed the window among them.</summary>
    bool Closed();

    /// <summary>The mouse buttons held (a bit each, <see cref="LoopButton.Mask"/>) and the pointer's place on the picture.</summary>
    uint Buttons(out float x, out float y);

    /// <summary>The modifiers held, as the host's keyboard has them; set, the host's keyboard is told.</summary>
    LoopMods Mods { get; set; }

    /// <summary>The keys held, by position (<see cref="LoopScancode"/>).</summary>
    ReadOnlySpan<bool> Keyboard();

    /// <summary>Every key the host's keyboard holds let go (a focus change).</summary>
    void ResetKeyboard();

    /// <summary>Whether a text field has the keyboard's text (the host's text input on in the window).</summary>
    bool TextInputActive { get; }

    /// <summary>The window and full screen swapped (Alt+Enter).</summary>
    void ToggleFullScreen();

    /// <summary>Whether there is a window (none headless, or before the first is made).</summary>
    bool HasWindow { get; }

    /// <summary>The picture's size (the window's, when there are no bars), or 0 by 0 with no window.</summary>
    (int Width, int Height) PictureSize();

    /// <summary>A replay's window put to the size its recording's was; said when it will not go.</summary>
    void Resize(int width, int height);

    /// <summary>The system's clipboard text, or null (Ctrl+V in the in-game text lines).</summary>
    string? Clipboard();

    /// <summary>Text put on the system's clipboard (Ctrl+C in the in-game text lines).</summary>
    void SetClipboard(string text);
}

/// <summary>
/// **A harness's scripted input** (<c>--ui-click</c>, <c>--ui-type</c>, <c>--menu-steps</c>; the host's, behind this): the
/// steps pushed into the host's own queue, one a frame from <c>ScriptedInput.FirstFrame</c>, so they take a hand's path.
/// The platform's SDL host's (<c>Vitomir.Platform.Render.Windowing.ScriptedInput</c>), both programs'.
/// </summary>
public interface IScriptedInput
{
    /// <summary>Where a step's <c>@template</c> is on the picture, or null while it is not on the screen.</summary>
    Func<string, (float X, float Y)?>? Aim { get; set; }
    /// <summary>A <c>shot:</c> step's picture written to the file named.</summary>
    Action<string>? Shoot { get; set; }
    /// <summary>Whether steps are still to come.</summary>
    bool Pending { get; }
    /// <summary>This frame's step pushed.</summary>
    void Pump(int frame);
}

/// <summary>
/// **A key as a harness's step names it**: its input — a key's position (<see cref="LoopScancode"/>), or
/// <see cref="ScriptKeys.MouseBase"/> plus a mouse button's number — and the modifiers held with it, a bit each
/// (<see cref="Shift"/>, <see cref="Ctrl"/>, <see cref="Alt"/>: the key page's own bits).
/// </summary>
public readonly record struct ScriptKey(int Input, int Mods)
{
    public const int Shift = 1, Ctrl = 2, Alt = 4;
}

/// <summary>
/// **What a harness's scripted input asks of the game's keys** (the scripted input moved from the old program to the
/// platform's SDL host, which names no engine type): a key's name read as the key page reads it, the base above which an
/// input is a mouse button, the US keycode of a key's position, the keys the harness holds (which the binding layer reads
/// as held, since SDL's own state does not see a pushed key), and a key's name for the log.
/// </summary>
public sealed class ScriptKeys
{
    /// <summary>A key's or a button's name (<c>Ctrl+V</c>, <c>Keypad 4</c>, <c>Middle Click</c>), or null when it names none.</summary>
    public required Func<string, ScriptKey?> Parse { get; init; }
    /// <summary>An input above this is a mouse button (the input less this its number).</summary>
    public required int MouseBase { get; init; }
    /// <summary>The US layout's keycode of a key's position.</summary>
    public required Func<int, int> UsKey { get; init; }
    /// <summary>The keys and buttons the harness holds down now.</summary>
    public required ISet<int> Held { get; init; }
    /// <summary>A key's name, as the log says it.</summary>
    public required Func<ScriptKey, string> Name { get; init; }
}

/// <summary>
/// **The window's modes and sizes, for the Options screen's Display page** (what <c>OptionsHost</c> asked of
/// the old program's window directly). The rows are 1.1's interface's (<c>FullScreen.WindowModes</c>: the window, the
/// borderless window, borderless full screen, full screen); a mode is a width and a height.
/// </summary>
public interface IWindowModes
{
    /// <summary>The row in use.</summary>
    int Mode();
    /// <summary>The sizes a row offers.</summary>
    IReadOnlyList<(int Width, int Height)> ModesFor(int row);
    /// <summary>The size a row would take now.</summary>
    (int Width, int Height) CurrentFor(int row);
    /// <summary>The row and its size made the window's (OK on the Display page).</summary>
    bool SetDisplay(int row, int width, int height);
    /// <summary>Whether the window is full screen.</summary>
    bool IsFull { get; }
    /// <summary>The full screen's mode in use.</summary>
    (int Width, int Height) Current();
    /// <summary>The desktop's own size, when known.</summary>
    (int Width, int Height)? Desktop();
    /// <summary>The display's own modes while full screen; null when they are not to be had.</summary>
    IReadOnlyList<(int Width, int Height)>? FullModes();
    /// <summary>The full screen's mode changed.</summary>
    bool SetFullMode(int width, int height);
    /// <summary>The window's size now.</summary>
    (int Width, int Height) WindowSize();
    /// <summary>The window made that size (a window's mode from the Display screen), said in the log.</summary>
    bool SetWindowSize(int width, int height);
}
