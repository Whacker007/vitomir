// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A console for a run that is all text, on a windowed program: both programs' one implementation (the old program's WinConsole,
// Folded in). a game in a window attaches to nothing (its lines kept in its log), the dedicated host's window
// in UTF-8 with a prompt and held open at the end, and a game's log window (a viewer of its own, LogWindow.cs, LogViewer.cs).
using System.Runtime.InteropServices;
using System.Text;

namespace Vitomir.Platform.Render.Windowing;

/// <summary>
/// **Somewhere for a run that is all text to print and read**. Both programs are windowed programs on Windows
/// (<c>WinExe</c>), so they start with no console. What a run wants (<see cref="Wanted"/>, from its command line:
/// <see cref="TextRuns"/>) and what the program's standard output is (<see cref="Kind"/>, read from the handle itself, never from
/// what .NET cached) decide what is done (<see cref="Decide"/>, a pure function, tested on every OS). Each program asks once, first
/// thing:
///
/// - output **redirected** (a pipe or a file: the tests, <c>Tee-Object</c>, <c>&gt; file</c>): left as it is, nothing opened;
/// - **<c>--headless-server</c>**: **a console window of its own, always** (<c>AllocConsole</c>, any console the
///   program had let go first), never the terminal it was started from, its title naming the game and its port, its code pages
///   UTF-8. The host types after a prompt, <c>&gt; </c> (<see cref="ConsolePrompt"/>; the keys read one by one with
///   <c>ReadConsoleInputW</c>, the host's lines put above what is half typed); closing it by its X (or Ctrl+C, Ctrl+Break in it)
///   asks the run to stop as <c>/quit</c> does (<see cref="CloseAsked"/>), and the program is given the few seconds Windows allows
///   to end. **When the host ends** (<see cref="Ending"/>, every end, an unhandled error too), the window stays open on the line
///   <see cref="ConsolePrompt.Ended"/> until a key is pressed or its X closes it;
/// - <c>vitomir-tools</c>' <c>--text-window</c>: the terminal it was started from, or a console window of its own when there is none;
/// - **a run whose whole job is text** (<see cref="TextRuns"/>: the reports, the tools, the checks, <c>--net-bot</c>): the
///   terminal it was started from, when there is one; else nothing;
/// - **a game in a window**: **nothing**, however it was started; what it prints goes to its log
///   (<see cref="Lines"/>, a <see cref="ConsoleLines"/>), and to its **log window** when one is opened (<see cref="OpenLogWindow"/>,
///   <c>--log-window</c>, <c>logwindow</c>: a viewer of its own, <see cref="LogWindow"/>). A program started with a console of its
///   own (the platform's <c>-cli</c> build) keeps it;
/// - **the log window's viewer** (<c>--log-viewer</c>, <see cref="LogViewer"/>): a console window of its own, UTF-8, its title,
///   no prompt, and Windows' own handling of its events (its X, Ctrl+C or its tab ends the viewer alone).
///
/// A console found or made is then used **explicitly**: <c>CONOUT$</c> and <c>CONIN$</c> opened (<c>CreateFileW</c>), made the
/// standard handles (<c>SetStdHandle</c>), and <see cref="Console"/>'s out, error and in opened on them afresh, in the console's own
/// code pages. What was done is said in one line (<see cref="Said"/>), which each program writes to its logs. Done once a process:
/// a later ask answers the first's line (and names a window of its own). Nothing elsewhere than Windows.
/// </summary>
public static unsafe partial class TextConsole
{
    /// <summary>What a run asks for.</summary>
    public enum Wanted
    {
        /// <summary>The terminal it was started from, when there is one; nothing otherwise (a run that is all text).</summary>
        Terminal,
        /// <summary>The terminal it was started from, or a console window of its own when there is none (<c>--text-window</c>).</summary>
        TerminalElseOwnWindow,
        /// <summary>A console window of its own, never the terminal (<c>--headless-server</c>).</summary>
        OwnWindow,
        /// <summary>No console at all, its lines kept in its log (a game in a window).</summary>
        Nothing,
        /// <summary>A console window of its own with no prompt, Windows' own handling of its events left in place (the log window's viewer).</summary>
        LogViewer,
    }

    /// <summary>What a standard handle is (<c>GetStdHandle</c>, <c>GetFileType</c>).</summary>
    public enum Kind
    {
        /// <summary>No handle (null or invalid): a windowed program started with none.</summary>
        None,
        /// <summary>A character device: a console (or the NUL device).</summary>
        Console,
        /// <summary>A pipe: redirected.</summary>
        Pipe,
        /// <summary>A disk file: redirected.</summary>
        File,
        /// <summary>A handle of no type Windows names.</summary>
        Unknown,
    }

    /// <summary>What is done.</summary>
    public enum Choice
    {
        /// <summary>Output redirected (a pipe or a file): left as it is, nothing opened.</summary>
        LeaveRedirected,
        /// <summary>Attach to the terminal it was started from; nothing when there is none.</summary>
        Attach,
        /// <summary>Attach to the terminal it was started from, or open a console window of its own.</summary>
        AttachElseOwnWindow,
        /// <summary>A console window of its own (any console the program had let go first).</summary>
        OwnWindow,
        /// <summary>No console; the lines kept in the log, and in a log window when one is opened (a game in a window).</summary>
        Nothing,
        /// <summary>The console the program was started with, left as it is (the platform's <c>-cli</c> build, a game in a window).</summary>
        LeaveConsole,
    }

    /// <summary>
    /// **The decision**, from the standard output's kind, what the run wants and whether the process was started with a console of
    /// its own (<paramref name="hasConsole"/>: <c>GetConsoleWindow</c>): a pipe or a file is redirected and left.
    /// </summary>
    public static Choice Decide(Kind output, Wanted wanted, bool hasConsole = false) =>
        output is Kind.Pipe or Kind.File ? Choice.LeaveRedirected
        : wanted switch
        {
            Wanted.OwnWindow or Wanted.LogViewer => Choice.OwnWindow,
            Wanted.TerminalElseOwnWindow => Choice.AttachElseOwnWindow,
            Wanted.Nothing => hasConsole ? Choice.LeaveConsole : Choice.Nothing,
            _ => Choice.Attach,
        };

    /// <summary>The kind of a handle from <c>GetFileType</c>'s answer (1 disk, 2 character, 3 pipe, else unknown); a null or invalid handle is none.</summary>
    public static Kind KindOf(nint handle, uint fileType) =>
        handle == 0 || handle == -1 ? Kind.None
        : fileType switch { 1 => Kind.File, 2 => Kind.Console, 3 => Kind.Pipe, _ => Kind.Unknown };

    /// <summary>
    /// **The one line that says what was done** (both programs' logs): attached to the terminal it was started from / opened a
    /// console window of its own / output redirected, left as it is / no console, with the Windows error codes. Plain words only.
    /// </summary>
    public static string Line(Choice choice, Kind output, bool attached, bool opened, uint attachError, uint allocError) =>
        choice == Choice.LeaveRedirected ? $"console: output redirected ({(output == Kind.Pipe ? "a pipe" : "a file")}), left as it is"
        : choice == Choice.Nothing ? "console: none, a game in a window attaches to nothing (its lines are in its log; --log-window shows them)"
        : choice == Choice.LeaveConsole ? "console: the console the program was started with, left as it is"
        : attached ? "console: attached to the terminal it was started from"
        : opened ? "console: opened a console window of its own (UTF-8)"
        : choice switch
        {
            Choice.Attach => $"console: none (no terminal to attach to, AttachConsole error {attachError}; the log has the lines)",
            Choice.AttachElseOwnWindow => $"console: none (AttachConsole error {attachError}, AllocConsole error {allocError}; the log has the lines)",
            _ => $"console: none (AllocConsole error {allocError}; the log has the lines)",
        };

    /// <summary>
    /// **Whether a window's end is held** on <see cref="ConsolePrompt.Ended"/> for a key: only the dedicated host's console
    /// window of its own, and not when that window is already closing (its X): a redirect, a terminal it attached to, a
    /// <c>--text-window</c> or a log window's viewer never (its own end is <see cref="WaitForKey"/>).
    /// </summary>
    public static bool Holds(Choice choice, Wanted wanted, bool opened, bool closing) =>
        choice == Choice.OwnWindow && wanted == Wanted.OwnWindow && opened && !closing;

    /// <summary>
    /// **Whether a failure is shown in a message box**: a game in a window that ended with an error (no terminal to see it
    /// in), never a run with a console, a redirect, or one that has shown its own box already.
    /// </summary>
    public static bool BoxesFailure(Choice? choice, int exitCode, bool boxShown) => choice == Choice.Nothing && exitCode != 0 && !boxShown;

    /// <summary>The line said by the first ask, or null before it (and always elsewhere than Windows).</summary>
    public static string? Said { get; private set; }

    /// <summary>What the first ask chose (null before it, and elsewhere than Windows).</summary>
    public static Choice? Chosen { get; private set; }

    /// <summary>Whether this process opened a console window of its own.</summary>
    public static bool OwnWindowOpened { get; private set; }

    /// <summary>A game in a window's lines, kept in its log and shown in its log window; null for any other run.</summary>
    public static ConsoleLines? Lines { get; private set; }

    /// <summary>Whether the console window of its own was closed (its X), or Ctrl+C or Ctrl+Break pressed in it: the run stops as <c>/quit</c> does.</summary>
    public static bool CloseAsked => Volatile.Read(ref closeAsked) != 0;

    static int closeAsked, closing, holding, closeNow, endingDone;
    static readonly ManualResetEventSlim ended = new(false);
    static Wanted asked;
    static nint conIn = -1;

    /// <summary>
    /// **The console for this process** (the first ask decides; a later one answers its line): see the class. <paramref name="title"/>
    /// names a window of its own (set whenever it is given). <paramref name="log"/> takes a game in a window's lines into its log
    /// (it can be given later: <see cref="Lines"/>). Answers the line to say, or null elsewhere than Windows.
    /// </summary>
    public static string? Open(Wanted wanted, string? title = null, Func<string, bool>? log = null)
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (Said is { } before)
        {
            if (title is not null && OwnWindowOpened) SetConsoleTitleW(title);
            return before;
        }
        asked = wanted;
        try { Said = OpenOnWindows(wanted, title, log); }
        catch (Exception e) when (e is not OutOfMemoryException) { Said = $"console: none ({e.GetType().Name}: {e.Message})"; }
        return Said;
    }

    static string OpenOnWindows(Wanted wanted, string? title, Func<string, bool>? log)
    {
        var outHandle = GetStdHandle(StdOutput);
        var output = KindOf(outHandle, outHandle is 0 or -1 ? 0u : GetFileType(outHandle));
        var choice = Decide(output, wanted, GetConsoleWindow() != 0);
        Chosen = choice;
        bool attached = false, opened = false;
        uint attachError = 0, allocError = 0;
        if (choice == Choice.Nothing)
        {
            // A game in a window: no console; its lines kept for its log and its log window.
            Lines = new ConsoleLines(log);
            Console.SetOut(Lines.Out);
            Console.SetError(Lines.Error);
            return Line(choice, output, false, false, 0, 0);
        }
        if (choice is Choice.Attach or Choice.AttachElseOwnWindow)
        {
            attached = AttachConsole(AttachParentProcess) != 0;
            if (!attached) attachError = (uint)Marshal.GetLastPInvokeError();
            // Attached already (a console the process was started with): the terminal's.
            if (!attached && attachError == ErrorAccessDenied && GetConsoleWindow() != 0) attached = true;
        }
        if (choice == Choice.OwnWindow || choice == Choice.AttachElseOwnWindow && !attached)
        {
            // Never the terminal it was started from: a console the process has is let go first.
            if (GetConsoleWindow() != 0) FreeConsole();
            opened = AllocConsole() != 0;
            if (!opened) allocError = (uint)Marshal.GetLastPInvokeError();
            else
            {
                OwnWindowOpened = true;
                if (title is not null) SetConsoleTitleW(title);
                // A window of its own starts in the system's OEM code page, where a dash or an accented
                // name shows "?": UTF-8 both ways. A terminal it attached to is left as it is.
                SetConsoleOutputCP(Utf8);
                SetConsoleCP(Utf8);
                // The log window's viewer keeps Windows' own handling: its X, Ctrl+C or its tab ends it, and only it.
                if (wanted != Wanted.LogViewer) SetConsoleCtrlHandler(&Handler, 1);
                AppDomain.CurrentDomain.ProcessExit += (_, _) => ended.Set();
            }
        }
        if (attached || opened) UseConsole(prompted: opened && wanted == Wanted.OwnWindow);
        return Line(choice, output, attached, opened, attachError, allocError);
    }

    /// <summary>
    /// CONOUT$ and CONIN$ opened and made the standard handles, then <see cref="Console"/>'s out, error and in opened on them afresh
    /// (<see cref="Console.OpenStandardOutput()"/> reads the handle as it is now), in the console's own code pages. With
    /// <paramref name="prompted"/> (the dedicated host's window) the out and error go through the prompt and the in is the
    /// lines handed in at it (<see cref="StartKeys"/>).
    /// </summary>
    static void UseConsole(bool prompted)
    {
        var conOut = CreateFileW("CONOUT$", GenericRead | GenericWrite, FileShareRead | FileShareWrite, 0, OpenExisting, 0, 0);
        conIn = CreateFileW("CONIN$", GenericRead | GenericWrite, FileShareRead | FileShareWrite, 0, OpenExisting, 0, 0);
        if (conOut != -1)
        {
            SetStdHandle(StdOutput, conOut);
            SetStdHandle(StdError, conOut);
            var encoding = CodePage(GetConsoleOutputCP());
            if (prompted)
            {
                raw = new Lasting(new StreamWriter(Console.OpenStandardOutput(), encoding) { AutoFlush = true });
                var through = new Prompted(() => raw, prompt, gate);
                Console.SetOut(through);
                Console.SetError(through);
            }
            else
            {
                Console.SetOut(new Lasting(new StreamWriter(Console.OpenStandardOutput(), encoding) { AutoFlush = true }, masked: true));
                Console.SetError(new Lasting(new StreamWriter(Console.OpenStandardError(), encoding) { AutoFlush = true }, masked: true));
            }
        }
        if (conIn != -1)
        {
            SetStdHandle(StdInput, conIn);
            if (prompted && raw is not null) StartKeys();
            else Console.SetIn(new StreamReader(Console.OpenStandardInput(), CodePage(GetConsoleCP())));
        }
    }

    // ---- the dedicated host's prompt and its end ----------------------------------------------------------------------------

    static readonly object gate = new();
    static readonly ConsolePrompt prompt = new();
    static readonly System.Collections.Concurrent.BlockingCollection<string> handedIn = new();
    static TextWriter? raw;
    static Thread? keys;
    static volatile bool stopKeys;

    /// <summary>
    /// The host's lines through the prompt: each whole line put above what is half typed, the prompt after it. Its screen, its prompt
    /// and its lock are given (the second part: so a test reaches the window's streamer mode); the dedicated host's are this file's.
    /// </summary>
    internal sealed class Prompted(Func<TextWriter?> screen, ConsolePrompt prompt, object gate) : TextWriter
    {
        readonly StringBuilder partial = new();

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            lock (gate)
            {
                if (value == '\n')
                {
                    if (partial.Length > 0 && partial[^1] == '\r') partial.Length--;
                    // The host's window is on the screen: its lines go through streamer mode (/players' addresses among them).
                    screen()?.Write(prompt.Print(Vitomir.Platform.Presentation.StreamerMode.Shown(partial.ToString())));
                    partial.Clear();
                }
                else partial.Append(value);
            }
        }

        public override void Write(string? value)
        {
            if (value is null) return;
            lock (gate) foreach (char c in value) Write(c);
        }

        public override void WriteLine(string? value)
        {
            lock (gate)
            {
                Write(value);
                Write('\n');
            }
        }

        public override void WriteLine() => Write('\n');
    }

    /// <summary>The lines handed in at the prompt, as <see cref="Console.ReadLine"/> reads them (null once the host has ended).</summary>
    sealed class HandedIn : TextReader
    {
        public override string? ReadLine()
        {
            try { return handedIn.Take(); }
            catch (InvalidOperationException) { return null; }
        }

        public override int Read() => -1;
    }

    /// <summary>
    /// The console's line input and echo off (its keys read one by one, Ctrl+C still the console's), and a thread reading them:
    /// each key given to the prompt, its echo written, an Enter's line handed in (<see cref="HandedIn"/>).
    /// </summary>
    static void StartKeys()
    {
        if (GetConsoleMode(conIn, out uint mode) != 0) SetConsoleMode(conIn, mode & ~(EnableLineInput | EnableEchoInput));
        Console.SetIn(new HandedIn());
        keys = new Thread(() =>
        {
            int failures = 0;
            while (!stopKeys)
            {
                if (WaitForSingleObject(conIn, 100) != 0) continue;
                InputRecord record;
                if (ReadConsoleInputW(conIn, &record, 1, out uint read) == 0)
                {
                    if (++failures > 50) return;
                    continue;
                }
                if (read == 0 || record.EventType != KeyEvent || record.KeyDown == 0) continue;
                lock (gate)
                {
                    for (int n = Math.Max((int)record.RepeatCount, 1); n > 0; n--)
                    {
                        var (echo, line) = prompt.Key(record.UnicodeChar, record.VirtualKeyCode);
                        if (echo.Length > 0) raw?.Write(echo);
                        if (line is not null && !handedIn.IsAddingCompleted) handedIn.Add(line);
                    }
                }
            }
        }) { IsBackground = true, Name = "host console keys" };
        keys.Start();
    }

    /// <summary>
    /// An unhandled error in the dedicated host's run holds its window too: said in it, then <see cref="Ending"/>. Asked by the
    /// host's run once it has begun, after the program's own logger has hooked the same event, so the log has the error first.
    /// </summary>
    public static void HoldOnCrash()
    {
        if (!OperatingSystem.IsWindows() || !OwnWindowOpened || asked != Wanted.OwnWindow || Interlocked.Exchange(ref crashHooked, 1) != 0) return;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var error = e.ExceptionObject as Exception;
            Console.Error.WriteLine($"error: the host stopped on an unhandled error: {error?.GetType().Name}: {error?.Message}");
            Ending();
        };
    }

    static int crashHooked;

    /// <summary>
    /// **The host has ended**: in the dedicated host's own window, the key reading stopped, the
    /// prompt's line replaced by <see cref="ConsolePrompt.Ended"/>, and the program waits for one key (any key, Ctrl+C too) or the
    /// window's X, which then ends it at once. Called by each program as it ends, every end; does nothing for any other console
    /// (a redirect, a terminal, a log window) or when the window is already closing. Once a process.
    /// </summary>
    public static void Ending()
    {
        // A game's log window: its lines written and its pipe closed, so its viewer says the game has ended.
        FinishLogWindow();
        if (!OperatingSystem.IsWindows() || Interlocked.Exchange(ref endingDone, 1) != 0) return;
        try
        {
            if (!Holds(Chosen ?? Choice.Nothing, asked, OwnWindowOpened, Volatile.Read(ref closing) != 0)) return;
            stopKeys = true;
            keys?.Join(1000);
            handedIn.CompleteAdding();
            lock (gate) raw?.Write(prompt.End());
            Volatile.Write(ref holding, 1);
            KeyDown();
        }
        catch (Exception e) when (e is not OutOfMemoryException) { }
        finally { ended.Set(); }
    }

    /// <summary>The console's input emptied, then one key-down waited for (or the handler's let-go, <see cref="Handler"/>).</summary>
    static void KeyDown()
    {
        FlushConsoleInputBuffer(conIn);
        while (Volatile.Read(ref closeNow) == 0)
        {
            if (WaitForSingleObject(conIn, 100) != 0) continue;
            InputRecord record;
            if (ReadConsoleInputW(conIn, &record, 1, out uint read) == 0) break;
            if (read == 1 && record.EventType == KeyEvent && record.KeyDown != 0) break;
        }
    }

    /// <summary>
    /// **One key waited for** in a console window this process opened (the log window's viewer at its end): any key; Ctrl+C or
    /// the window's X end the process as Windows does. Nothing for any other console, or elsewhere than Windows.
    /// </summary>
    public static void WaitForKey()
    {
        if (!OperatingSystem.IsWindows() || !OwnWindowOpened || conIn == -1) return;
        try { KeyDown(); }
        catch (Exception e) when (e is not OutOfMemoryException) { }
    }

    // ---- a game's log window (a viewer of its own) ------------------------------------------------------------------------

    static readonly object windowGate = new();
    static LogWindow? logWindow;
    static int crashFinishHooked;

    /// <summary>What a log window answers elsewhere than Windows.</summary>
    public const string WindowsAlone = "log window: not opened (Windows alone; the log has the lines)";

    /// <summary>Whether this process has a log window open (its viewer not closed).</summary>
    public static bool LogWindowOpen { get { lock (windowGate) return logWindow is { Open: true }; } }

    /// <summary>
    /// **A game's log window** (<i>"Have the game with it's own log window. It could be like a developer
    /// option or toggle. Other games have this"</i>): a viewer of its own (<see cref="LogWindow"/>: the same program started with
    /// <c>--log-viewer</c>, a process of its own, its console window titled <paramref name="title"/>, <c>Vitomir log: &lt;game&gt;</c>),
    /// fed the game's lines (<see cref="Lines"/>: those from before it opened first, then each as it comes) over a named pipe.
    /// **Closing it (its X, Ctrl+C, its Windows Terminal tab) ends only the viewer**: the game says <c>log window: closed</c> in its
    /// log and can open another. Only for a game in a window (<see cref="Choice.Nothing"/>). Answers the line to say.
    /// </summary>
    public static string OpenLogWindow(string title)
    {
        if (!OperatingSystem.IsWindows()) return WindowsAlone;
        lock (windowGate)
        {
            if (logWindow is { Open: true }) return "log window: open already";
            if (Lines is not { } lines)
                return Chosen switch
                {
                    Choice.OwnWindow => "log window: not opened (the dedicated host shows its lines in its own window)",
                    Choice.LeaveRedirected => "log window: not opened (the output is redirected)",
                    Choice.LeaveConsole => "log window: not opened (the program's own console shows its lines)",
                    _ => "log window: not opened (this run's lines go to the terminal it was started from)",
                };
            var (window, line) = LogWindow.Start(lines, title, LogWindow.StartProcess, said => Console.WriteLine(said));
            logWindow = window;
            // A crash: the lines queued reach the viewer before the process ends (the program's own handlers run first).
            if (window is not null && Interlocked.Exchange(ref crashFinishHooked, 1) == 0)
                AppDomain.CurrentDomain.UnhandledException += (_, _) => FinishLogWindow();
            return line;
        }
    }

    /// <summary>The log window closed by the game (<c>logwindow off</c>): its viewer told to end; the lines go on into the log.</summary>
    public static string CloseLogWindow()
    {
        LogWindow? closing;
        lock (windowGate)
        {
            closing = logWindow is { Open: true } open ? open : null;
            logWindow = null;
        }
        if (closing is not null) return closing.Close();
        return OperatingSystem.IsWindows() || Lines is not null ? "log window: not open" : WindowsAlone;
    }

    /// <summary>
    /// **The log window set** (<c>--log-window</c> at the start, the console's <c>logwindow</c>): <paramref name="on"/> true
    /// opens it, false closes it, null toggles. Answers the line to say.
    /// </summary>
    public static string LogWindowSet(string title, bool? on) =>
        on ?? !LogWindowOpen ? OpenLogWindow(title) : CloseLogWindow();

    /// <summary>The game ending (or crashing): the log window's lines written and its pipe closed, so its viewer says so.</summary>
    public static void FinishLogWindow()
    {
        LogWindow? ending;
        lock (windowGate)
        {
            ending = logWindow;
            logWindow = null;
        }
        ending?.GameEnding();
    }

    // ---- the plumbing ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A console's writer that outlives its console: a window closed by its X takes CONOUT$ with it while the run is still saying its
    /// last lines, and a line that cannot be written is lost rather than the run (the log file has it).
    /// <para>
    /// <paramref name="masked"/>: a console window or a terminal is on the screen, so while streamer mode is on its lines go
    /// through it (<see cref="Vitomir.Platform.Presentation.StreamerMode.Shown"/>), each whole line at its end; while it is off, as
    /// written. The prompt's own writer is not masked here (its lines are, before the prompt is drawn under them).
    /// </para>
    /// </summary>
    internal sealed class Lasting(TextWriter inner, bool masked = false) : TextWriter
    {
        readonly StringBuilder held = new();

        public override Encoding Encoding => inner.Encoding;
        bool Straight => !masked || held.Length == 0 && !Vitomir.Platform.Presentation.StreamerMode.On;

        public override void Write(char value)
        {
            if (Straight) { Safe(() => inner.Write(value)); return; }
            lock (held)
            {
                if (value != '\n') { held.Append(value); return; }
                if (held.Length > 0 && held[^1] == '\r') held.Length--;
                string line = Vitomir.Platform.Presentation.StreamerMode.Shown(held.ToString());
                held.Clear();
                Safe(() => inner.WriteLine(line));
            }
        }

        public override void Write(string? value)
        {
            if (value is null) return;
            if (Straight) { Safe(() => inner.Write(value)); return; }
            lock (held) foreach (char c in value) Write(c);
        }

        public override void WriteLine(string? value)
        {
            if (Straight) { Safe(() => inner.WriteLine(value)); return; }
            lock (held)
            {
                Write(value);
                Write('\n');
            }
        }

        public override void Flush() => Safe(inner.Flush);

        static void Safe(Action write)
        {
            try { write(); } catch (IOException) { } catch (ObjectDisposedException) { }
        }
    }

    /// <summary>The encoding of a console code page (UTF-8 without its mark for 65001), or UTF-8 when .NET has none for it.</summary>
    static Encoding CodePage(uint page)
    {
        if (page is 0 or 65001) return new UTF8Encoding(false);
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding((int)page);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException) { return new UTF8Encoding(false); }
    }

    /// <summary>
    /// The console's control events (<c>SetConsoleCtrlHandler</c>), for a window of its own alone: Ctrl+C, Ctrl+Break and the
    /// window closed by its X ask the run to stop (<see cref="CloseAsked"/>, read every frame); for the close, Windows ends the
    /// process when this answers, so it waits — at most four and a half of the five seconds Windows allows — for the program to end.
    /// While the window is held at the end (<see cref="Ending"/>), any of them lets it go at once.
    /// </summary>
    [UnmanagedCallersOnly]
    static int Handler(uint ctrlType)
    {
        if (Volatile.Read(ref holding) != 0)
        {
            Interlocked.Exchange(ref closeNow, 1);
            return 1;
        }
        Interlocked.Exchange(ref closeAsked, 1);
        if (ctrlType == CtrlClose)
        {
            Interlocked.Exchange(ref closing, 1);
            ended.Wait(TimeSpan.FromSeconds(4.5));
        }
        return 1;
    }

    /// <summary>INPUT_RECORD with its KEY_EVENT_RECORD (wincon.h): 20 bytes.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 20)]
    struct InputRecord
    {
        [FieldOffset(0)] public ushort EventType;
        [FieldOffset(4)] public int KeyDown;
        [FieldOffset(8)] public ushort RepeatCount;
        [FieldOffset(10)] public ushort VirtualKeyCode;
        [FieldOffset(12)] public ushort VirtualScanCode;
        [FieldOffset(14)] public char UnicodeChar;
        [FieldOffset(16)] public uint ControlKeyState;
    }

    const uint AttachParentProcess = 0xFFFFFFFF, ErrorAccessDenied = 5, CtrlClose = 2, Utf8 = 65001;
    const int StdInput = -10, StdOutput = -11, StdError = -12;
    const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, FileShareRead = 1, FileShareWrite = 2, OpenExisting = 3;
    const ushort KeyEvent = 1;
    const uint EnableLineInput = 0x2, EnableEchoInput = 0x4;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int AttachConsole(uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int AllocConsole();

    [LibraryImport("kernel32.dll")]
    private static partial int FreeConsole();

    [LibraryImport("kernel32.dll")]
    private static partial nint GetConsoleWindow();

    [LibraryImport("kernel32.dll")]
    private static partial nint GetStdHandle(int which);

    [LibraryImport("kernel32.dll")]
    private static partial int SetStdHandle(int which, nint handle);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetFileType(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateFileW(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetConsoleTitleW(string title);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetConsoleOutputCP();

    [LibraryImport("kernel32.dll")]
    private static partial uint GetConsoleCP();

    [LibraryImport("kernel32.dll")]
    private static partial int SetConsoleOutputCP(uint page);

    [LibraryImport("kernel32.dll")]
    private static partial int SetConsoleCP(uint page);

    [LibraryImport("kernel32.dll")]
    private static partial int GetConsoleMode(nint handle, out uint mode);

    [LibraryImport("kernel32.dll")]
    private static partial int SetConsoleMode(nint handle, uint mode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int ReadConsoleInputW(nint handle, InputRecord* buffer, uint length, out uint read);

    [LibraryImport("kernel32.dll")]
    private static partial int FlushConsoleInputBuffer(nint handle);

    [LibraryImport("kernel32.dll")]
    private static partial uint WaitForSingleObject(nint handle, uint milliseconds);

    [LibraryImport("kernel32.dll")]
    private static partial int SetConsoleCtrlHandler(delegate* unmanaged<uint, int> handler, int add);
}
