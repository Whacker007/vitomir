// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A game's log window as a process of its own: the viewer's side. Each program, first thing, before any log, setting,
// archive or user-data work, runs this instead of itself when started with --log-viewer (LogWindow.cs starts it so).
using System.IO.Pipes;
using System.Text;

namespace Vitomir.Platform.Render.Windowing;

/// <summary>
/// **The log window's viewer**: the same program started by a game as
/// <c>&lt;program&gt; --log-viewer &lt;pipe&gt; --title "&lt;title&gt;"</c> (<see cref="Command"/>). It is a run that is all text:
/// a console window of its own (<see cref="TextConsole.Wanted.LogViewer"/>: UTF-8, its title, no prompt), which writes each line
/// that comes down the pipe (<see cref="LogWire"/>, <see cref="Pump"/>). Each program asks for it **first thing**
/// (<see cref="Asked"/>), before any log, settings, archive or user-data work: a viewer never archives or rotates the game's
/// session nor writes a log of its own. Windows' own handling of its console's events is left in place, so its X, Ctrl+C or its
/// Windows Terminal tab ends the viewer alone, at once. When the pipe ends without the game's close record (the game ended or
/// crashed) it says <see cref="GameEnded"/> and waits for a key, as the dedicated host's window does; when the game closed it
/// (<c>logwindow off</c>) it ends at once.
/// </summary>
public static class LogViewer
{
    /// <summary>The viewer's switch, first on its command line; its value is the pipe's name.</summary>
    public const string Switch = "--log-viewer";

    /// <summary>The window's title switch.</summary>
    public const string TitleSwitch = "--title";

    /// <summary>The viewer's last line when the game has ended (or crashed).</summary>
    public const string GameEnded = "The game has ended. Press a key to close this window.";

    /// <summary>How long the viewer waits for the game's pipe.</summary>
    public const int ConnectMs = 60_000;

    /// <summary>How the pipe ended.</summary>
    public enum End
    {
        /// <summary>The pipe ended without the close record: the game ended or crashed.</summary>
        GameEnded,
        /// <summary>The game closed the window.</summary>
        ClosedByGame,
    }

    /// <summary>Whether a command line is the viewer's: <see cref="Switch"/> first.</summary>
    public static bool Asked(IReadOnlyList<string> args) => args.Count > 0 && args[0] == Switch;

    /// <summary>
    /// **The viewer's command** for a program at <paramref name="processPath"/> (<c>Environment.ProcessPath</c>): the program itself
    /// with the viewer's switches, or, when the program is the <c>dotnet</c> host running an assembly (a developer's
    /// <c>dotnet vitomir.dll</c>), the host with that assembly first.
    /// </summary>
    public static (string File, string[] Args) Command(string processPath, string? entryAssembly, string pipe, string title)
    {
        string name = Path.GetFileNameWithoutExtension(processPath);
        bool host = name.Equals("dotnet", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(entryAssembly);
        string[] viewer = [Switch, pipe, TitleSwitch, title];
        return host ? (processPath, [entryAssembly!, .. viewer]) : (processPath, viewer);
    }

    /// <summary>The pipe's name from the switch's value (<c>\\.\pipe\</c> before it taken off), or null when it is not there.</summary>
    public static string? PipeOf(IReadOnlyList<string> args)
    {
        int at = IndexOf(args, Switch);
        if (at < 0 || at + 1 >= args.Count || args[at + 1].Length == 0) return null;
        string pipe = args[at + 1];
        const string prefix = @"\\.\pipe\";
        return pipe.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? pipe[prefix.Length..] : pipe;
    }

    /// <summary>The window's title from <see cref="TitleSwitch"/>, or <c>Vitomir log</c>.</summary>
    public static string TitleOf(IReadOnlyList<string> args)
    {
        int at = IndexOf(args, TitleSwitch);
        return at >= 0 && at + 1 < args.Count && args[at + 1].Length > 0 ? args[at + 1] : "Vitomir log";
    }

    static int IndexOf(IReadOnlyList<string> args, string name)
    {
        for (int i = 0; i < args.Count; i++)
            if (args[i] == name) return i;
        return -1;
    }

    /// <summary>
    /// **The viewer's run**: its console window (on Windows, when <paramref name="to"/> is not given; elsewhere, or redirected, the
    /// standard output as it is), the game's pipe connected, its lines written until it ends, then <see cref="GameEnded"/> and a
    /// key waited for, unless the game closed it. Answers 0, or 2 for a command line with no pipe and 1 when the pipe could not
    /// be reached. Touches no file.
    /// </summary>
    public static int Run(IReadOnlyList<string> args, TextWriter? to = null, int connectMs = ConnectMs)
    {
        string title = TitleOf(args);
        if (to is null) TextConsole.Open(TextConsole.Wanted.LogViewer, title);
        to ??= Console.Out;
        if (PipeOf(args) is not { } pipe)
        {
            to.WriteLine("log window: no pipe named (--log-viewer <pipe> --title <title>, as the game starts it)");
            return 2;
        }
        using var client = new NamedPipeClientStream(".", pipe, PipeDirection.In, PipeOptions.CurrentUserOnly);
        try { client.Connect(connectMs); }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            to.WriteLine($"log window: the game's pipe {pipe} could not be reached ({e.GetType().Name}: {e.Message})");
            to.WriteLine("Press a key to close this window.");
            to.Flush();
            TextConsole.WaitForKey();
            return 1;
        }
        if (Pump(client, to) == End.ClosedByGame) return 0;
        to.WriteLine();
        to.WriteLine(GameEnded);
        to.Flush();
        TextConsole.WaitForKey();
        return 0;
    }

    /// <summary>
    /// **What comes down the pipe written**: each line of the log as it is, the game's own records (<see cref="LogWire.Ping"/>)
    /// showing nothing; answers <see cref="End.ClosedByGame"/> at the close record, <see cref="End.GameEnded"/> when the stream
    /// ends or breaks without it.
    /// </summary>
    public static End Pump(Stream from, TextWriter to)
    {
        using var reader = new StreamReader(from, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, 16 * 1024, leaveOpen: true);
        try
        {
            while (reader.ReadLine() is { } record)
            {
                switch (LogWire.Read(record))
                {
                    case LogWire.Kind.Close:
                        to.Flush();
                        return End.ClosedByGame;
                    case LogWire.Kind.Line:
                        to.WriteLine(record);
                        to.Flush();
                        break;
                }
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException) { }
        to.Flush();
        return End.GameEnded;
    }
}
