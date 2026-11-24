// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Render.Windowing;
using static Vitomir.Platform.Render.Windowing.TextConsole;

namespace Vitomir.Platform.Render.Tests;

/// <summary>
/// **The console's decision** (<c>Windowing/TextConsole.cs</c>), a pure function over the standard output's kind and what the run
/// wants, tested on every OS (the Windows calls themselves run only on Windows): a pipe or a file is redirected and left as it is; the
/// dedicated host (<c>--headless-server</c>) opens a console window of its own whatever else it has, never the
/// terminal; <c>--text-window</c> the terminal or a window of its own; anything else the terminal alone. And the one line each case says,
/// in plain words (a redirected program's output is in the console's code page on Windows: no dash, no letter outside ASCII).
/// </summary>
public sealed class TextConsoleTests
{
    [Theory]
    [InlineData(Kind.Pipe, Wanted.OwnWindow, Choice.LeaveRedirected)]
    [InlineData(Kind.File, Wanted.OwnWindow, Choice.LeaveRedirected)]
    [InlineData(Kind.Pipe, Wanted.Terminal, Choice.LeaveRedirected)]
    [InlineData(Kind.File, Wanted.TerminalElseOwnWindow, Choice.LeaveRedirected)]
    [InlineData(Kind.None, Wanted.OwnWindow, Choice.OwnWindow)]
    [InlineData(Kind.Console, Wanted.OwnWindow, Choice.OwnWindow)]
    [InlineData(Kind.Unknown, Wanted.OwnWindow, Choice.OwnWindow)]
    [InlineData(Kind.None, Wanted.TerminalElseOwnWindow, Choice.AttachElseOwnWindow)]
    [InlineData(Kind.Console, Wanted.TerminalElseOwnWindow, Choice.AttachElseOwnWindow)]
    [InlineData(Kind.None, Wanted.Terminal, Choice.Attach)]
    [InlineData(Kind.Console, Wanted.Terminal, Choice.Attach)]
    [InlineData(Kind.Unknown, Wanted.Terminal, Choice.Attach)]
    [InlineData(Kind.None, Wanted.Nothing, Choice.Nothing)]
    [InlineData(Kind.Unknown, Wanted.Nothing, Choice.Nothing)]
    [InlineData(Kind.Console, Wanted.Nothing, Choice.Nothing)]
    [InlineData(Kind.Pipe, Wanted.Nothing, Choice.LeaveRedirected)]
    [InlineData(Kind.File, Wanted.Nothing, Choice.LeaveRedirected)]
    [InlineData(Kind.None, Wanted.LogViewer, Choice.OwnWindow)]
    [InlineData(Kind.Console, Wanted.LogViewer, Choice.OwnWindow)]
    [InlineData(Kind.Pipe, Wanted.LogViewer, Choice.LeaveRedirected)]
    [InlineData(Kind.File, Wanted.LogViewer, Choice.LeaveRedirected)]
    public void The_decision_is_the_handle_s_kind_and_the_run_s_wish(Kind output, Wanted wanted, Choice expected) =>
        Assert.Equal(expected, Decide(output, wanted));

    [Theory]
    [InlineData(0L, 2u, Kind.None)]
    [InlineData(-1L, 3u, Kind.None)]
    [InlineData(84L, 1u, Kind.File)]
    [InlineData(84L, 2u, Kind.Console)]
    [InlineData(84L, 3u, Kind.Pipe)]
    [InlineData(84L, 0u, Kind.Unknown)]
    [InlineData(84L, 0x8000u, Kind.Unknown)]
    public void A_handle_s_kind_is_GetFileType_s_answer_and_a_null_handle_is_none(long handle, uint fileType, Kind expected) =>
        Assert.Equal(expected, KindOf((nint)handle, fileType));

    [Fact]
    public void Each_case_says_one_line_in_plain_words()
    {
        string[] lines =
        [
            Line(Choice.LeaveRedirected, Kind.Pipe, false, false, 0, 0),
            Line(Choice.LeaveRedirected, Kind.File, false, false, 0, 0),
            Line(Choice.Attach, Kind.None, true, false, 0, 0),
            Line(Choice.OwnWindow, Kind.None, false, true, 0, 0),
            Line(Choice.AttachElseOwnWindow, Kind.None, false, true, 6, 0),
            Line(Choice.Attach, Kind.None, false, false, 6, 0),
            Line(Choice.AttachElseOwnWindow, Kind.None, false, false, 6, 5),
            Line(Choice.OwnWindow, Kind.None, false, false, 0, 5),
            Line(Choice.Nothing, Kind.None, false, false, 0, 0),
            Line(Choice.LeaveConsole, Kind.Console, false, false, 0, 0),
        ];
        Assert.Equal("console: output redirected (a pipe), left as it is", lines[0]);
        Assert.Equal("console: output redirected (a file), left as it is", lines[1]);
        Assert.Equal("console: attached to the terminal it was started from", lines[2]);
        Assert.Equal("console: opened a console window of its own (UTF-8)", lines[3]);
        Assert.Equal("console: opened a console window of its own (UTF-8)", lines[4]);
        Assert.Equal("console: none, a game in a window attaches to nothing (its lines are in its log; --log-window shows them)", lines[8]);
        Assert.Equal("console: the console the program was started with, left as it is", lines[9]);
        Assert.Contains("AttachConsole error 6", lines[5]);
        Assert.Contains("AttachConsole error 6, AllocConsole error 5", lines[6]);
        Assert.Contains("AllocConsole error 5", lines[7]);
        foreach (var line in lines)
        {
            Assert.StartsWith("console: ", line);
            Assert.All(line, c => Assert.True(c is >= ' ' and <= '~', $"not a plain character in \"{line}\""));
        }
    }

    [Fact]
    public void Elsewhere_than_Windows_nothing_is_done_and_nothing_said()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "on Windows the console is the test runner's own");
        Assert.Null(Open(Wanted.OwnWindow, "a title"));
        Assert.Null(Said);
        Assert.False(CloseAsked);
    }

    /// <summary>A game in a window started by a program that has a console of its own (the platform's -cli build) keeps it.</summary>
    [Theory]
    [InlineData(Kind.Console, Choice.LeaveConsole)]
    [InlineData(Kind.None, Choice.LeaveConsole)]
    [InlineData(Kind.Pipe, Choice.LeaveRedirected)]
    public void A_game_in_a_window_keeps_a_console_the_program_was_started_with(Kind output, Choice expected) =>
        Assert.Equal(expected, Decide(output, Wanted.Nothing, hasConsole: true));

    /// <summary>
    /// **What each run asks** (<c>Windowing/TextRuns.cs</c>): the dedicated host its own window, <c>--text-window</c>
    /// the terminal or a window, a run whose whole job is text the terminal, and a game in a window, however started, nothing.
    /// </summary>
    [Theory]
    [InlineData(false, "--headless-server --no-upnp", Wanted.OwnWindow)]
    [InlineData(true, "--game classic --headless-server --port 5000", Wanted.OwnWindow)]
    [InlineData(false, "--text-window --level P1_HAVN", Wanted.TerminalElseOwnWindow)]
    [InlineData(false, "--net-bot --join 127.0.0.1", Wanted.Terminal)]
    [InlineData(true, "--game classic --developer --net-bot", Wanted.Terminal)]
    [InlineData(false, "--diagnostics", Wanted.Terminal)]
    [InlineData(false, "--chronicles", Wanted.Terminal)]
    [InlineData(false, "--list-saves", Wanted.Terminal)]
    [InlineData(false, "--replay-check run.tape", Wanted.Terminal)]
    [InlineData(false, "--replay-folder replays", Wanted.Terminal)]
    [InlineData(false, "--headless still.png --settle 2", Wanted.Terminal)]
    [InlineData(false, "--sweep out", Wanted.Terminal)]
    [InlineData(false, "--install-data C:\\Games\\VTMR", Wanted.Terminal)]
    [InlineData(false, "--things", Wanted.Terminal)]
    [InlineData(true, "--check-modules", Wanted.Terminal)]
    [InlineData(true, "--replay-dump run.tape", Wanted.Terminal)]
    [InlineData(true, "--describe --json", Wanted.Terminal)]
    [InlineData(true, "--help", Wanted.Terminal)]
    [InlineData(false, "", Wanted.Nothing)]
    [InlineData(false, "--play", Wanted.Nothing)]
    [InlineData(false, "--host --no-upnp --name Hosta", Wanted.Nothing)]
    [InlineData(false, "--join 127.0.0.1 --name Anna", Wanted.Nothing)]
    [InlineData(false, "--scenario fight", Wanted.Nothing)]
    [InlineData(false, "--check-modules", Wanted.Nothing)]
    [InlineData(true, "--game classic", Wanted.Nothing)]
    [InlineData(true, "--game classic --log-window --streamer", Wanted.Nothing)]
    [InlineData(true, "--level C:\\Games\\Vitomir\\eastgate.l2", Wanted.Nothing)]
    [InlineData(false, "--log-viewer vitomir-log-1234-1 --title Vitomir_log:_classic", Wanted.LogViewer)]
    [InlineData(true, "--log-viewer vitomir-log-1234-2 --title Vitomir_log:_classic", Wanted.LogViewer)]
    [InlineData(false, "--game classic --log-viewer vitomir-log-1234-1", Wanted.Nothing)]
    public void Each_run_asks_for_its_console_from_its_command_line(bool platform, string line, Wanted expected) =>
        Assert.Equal(expected, TextRuns.WantedFor(line.Split(' ', StringSplitOptions.RemoveEmptyEntries), platform));

    /// <summary>The dedicated host's window alone is held at its end, and not while its X is closing it.</summary>
    [Theory]
    [InlineData(Choice.OwnWindow, Wanted.OwnWindow, true, false, true)]
    [InlineData(Choice.OwnWindow, Wanted.OwnWindow, true, true, false)]
    [InlineData(Choice.OwnWindow, Wanted.OwnWindow, false, false, false)]
    [InlineData(Choice.AttachElseOwnWindow, Wanted.TerminalElseOwnWindow, true, false, false)]
    [InlineData(Choice.LeaveRedirected, Wanted.OwnWindow, false, false, false)]
    [InlineData(Choice.Attach, Wanted.Terminal, false, false, false)]
    [InlineData(Choice.Nothing, Wanted.Nothing, false, false, false)]
    [InlineData(Choice.OwnWindow, Wanted.LogViewer, true, false, false)]
    public void Only_the_dedicated_host_s_own_window_is_held_at_its_end(Choice choice, Wanted wanted, bool opened, bool closing, bool held) =>
        Assert.Equal(held, Holds(choice, wanted, opened, closing));

    /// <summary>A failure is boxed for a game in a window alone, once, and never for a clean end.</summary>
    [Theory]
    [InlineData(Choice.Nothing, 2, false, true)]
    [InlineData(Choice.Nothing, 1, false, true)]
    [InlineData(Choice.Nothing, 0, false, false)]
    [InlineData(Choice.Nothing, 2, true, false)]
    [InlineData(Choice.LeaveRedirected, 2, false, false)]
    [InlineData(Choice.Attach, 2, false, false)]
    [InlineData(Choice.OwnWindow, 2, false, false)]
    [InlineData(Choice.LeaveConsole, 2, false, false)]
    public void A_failure_is_boxed_for_a_game_in_a_window_alone(Choice choice, int exit, bool shown, bool boxed) =>
        Assert.Equal(boxed, BoxesFailure(choice, exit, shown));

    [Fact]
    public void Elsewhere_than_Windows_no_log_window_and_no_hold()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "on Windows the console is the test runner's own");
        Assert.Equal(WindowsAlone, OpenLogWindow("Vitomir log: classic"));
        Assert.Equal(WindowsAlone, CloseLogWindow());
        Assert.Equal(WindowsAlone, LogWindowSet("Vitomir log: classic", null));
        Assert.False(LogWindowOpen);
        WaitForKey();
        Ending();
        Assert.Null(Lines);
        Assert.False(FailureBox.Show("Vitomir", 2, ["it failed"]));
    }

    [Fact]
    public void A_failure_s_box_says_what_went_wrong_and_names_the_log()
    {
        string text = FailureBox.Text(["", "net: UDP port 4994 is already held by another program (another Vitomir host?): close it, or choose another port with --port"],
                                      [@"C:\Users\Anna\AppData\Local\Vitomir\Logs\vitomir.log", null, @"C:\Users\Anna\AppData\Local\Vitomir\Logs\vitomir.log"]);
        Assert.StartsWith("net: UDP port 4994 is already held", text);
        Assert.EndsWith("The log has the whole run:\n" + @"C:\Users\Anna\AppData\Local\Vitomir\Logs\vitomir.log", text);
        Assert.Contains("It ended with an error.", FailureBox.Text([], []));
        Assert.EndsWith("The log folder has the whole run.", FailureBox.Text([], []));
    }
}
