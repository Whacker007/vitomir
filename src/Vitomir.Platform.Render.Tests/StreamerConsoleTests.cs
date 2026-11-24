// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Render.Windowing;

namespace Vitomir.Platform.Render.Tests;

/// <summary>
/// **Streamer mode in the windows of text**: the log window is on the screen and a stream may show it, so
/// its lines are hidden with it on and shown with it off, while the log the same lines go to keeps them as they are (the player's
/// own to send); and a console window or a terminal (the dedicated host's, a text run's) hides each whole line with it on and
/// writes straight through with it off. The setting is process-wide: these run alone.
/// </summary>
[Collection(nameof(StreamerConsoleCollection))]
public sealed class StreamerConsoleTests : IDisposable
{
    readonly bool was = StreamerMode.On;

    public void Dispose() => StreamerMode.On = was;

    [Fact]
    public void The_log_window_hides_addresses_on_a_stream_and_the_log_keeps_them()
    {
        var logged = new List<string>();
        var lines = new ConsoleLines(line => { logged.Add(line); return true; });
        var window = new StringWriter();
        lines.Show(window);
        StreamerMode.On = true;
        lines.Out.WriteLine("net: connection from 198.51.100.2:4994");
        StreamerMode.On = false;
        lines.Out.WriteLine("net: connection from 198.51.100.3:4994");
        string shown = window.ToString();
        Assert.Contains($"net: connection from {StreamerMode.Hidden}", shown);
        Assert.DoesNotContain("198.51.100.2", shown);
        Assert.Contains("198.51.100.3:4994", shown);
        Assert.Equal(["net: connection from 198.51.100.2:4994", "net: connection from 198.51.100.3:4994"], logged);

        // A log window opened later shows the lines kept, as streamer mode stands then.
        StreamerMode.On = true;
        var later = new StringWriter();
        lines.Show(later);
        Assert.DoesNotContain("198.51.100", later.ToString());
    }

    [Fact]
    public void A_console_window_hides_each_whole_line_on_a_stream_and_writes_straight_through_without()
    {
        var screen = new StringWriter();
        var console = new TextConsole.Lasting(screen, masked: true);
        StreamerMode.On = true;
        console.Write("  1: Anna (MP_Serena), ");
        console.Write("10.0.0.");
        console.WriteLine("7:4994, 40 ms");
        Assert.Equal($"  1: Anna (MP_Serena), {StreamerMode.Hidden}, 40 ms{Environment.NewLine}", screen.ToString());
        StreamerMode.On = false;
        console.WriteLine("  2: Bob, 10.0.0.8:4994");
        Assert.EndsWith($"  2: Bob, 10.0.0.8:4994{Environment.NewLine}", screen.ToString());
        // The prompt's own writer is never held for a line (its lines are hidden before the prompt is drawn under them).
        var raw = new StringWriter();
        StreamerMode.On = true;
        new TextConsole.Lasting(raw).Write("> 10.0.0.9");
        Assert.Equal("> 10.0.0.9", raw.ToString());
    }

    /// <summary>
    /// The dedicated host's own window, its prompt under its lines (the second part): each whole line through streamer mode as it is
    /// put above the prompt, a player's address hidden with it on and shown with it off; what is typed at the prompt stays as typed.
    /// </summary>
    [Fact]
    public void The_dedicated_hosts_prompt_window_hides_addresses_on_a_stream()
    {
        var screen = new StringWriter();
        var window = new TextConsole.Prompted(() => screen, new ConsolePrompt(), new object());
        StreamerMode.On = true;
        window.Write("net: Anna joined from ");
        window.WriteLine("10.0.0.7:4994");
        string shown = screen.ToString();
        Assert.Contains($"net: Anna joined from {StreamerMode.Hidden}", shown);
        Assert.DoesNotContain("10.0.0.7", shown);
        Assert.EndsWith(ConsolePrompt.Prompt, shown);
        StreamerMode.On = false;
        window.WriteLine("net: Bob joined from 10.0.0.8:4994");
        Assert.Contains("net: Bob joined from 10.0.0.8:4994", screen.ToString());
    }
}

/// <summary>The streamer mode tests, which set a process-wide setting, alone.</summary>
[CollectionDefinition(nameof(StreamerConsoleCollection), DisableParallelization = true)]
public sealed class StreamerConsoleCollection;
