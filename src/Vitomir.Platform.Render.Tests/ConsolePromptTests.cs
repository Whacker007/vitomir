// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Render.Windowing;

namespace Vitomir.Platform.Render.Tests;

/// <summary>
/// **The dedicated host's prompt and its last line** (<c>Windowing/ConsolePrompt.cs</c>), and **a game in a
/// window's lines** (<c>Windowing/ConsoleLines.cs</c>): the pure parts, on every OS; the console's keys themselves
/// (<c>ReadConsoleInputW</c>) are read only on Windows. What each answers is replayed on a model of a console's line (a carriage
/// return goes back to the line's start, a backspace one place back, a line feed to a fresh line) and the screen it leaves checked.
/// </summary>
public sealed class ConsolePromptTests
{
    /// <summary>A console's screen as plain lines: what the prompt's writes leave.</summary>
    sealed class Screen
    {
        readonly List<System.Text.StringBuilder> rows = [new()];
        int column;

        public void Write(string text)
        {
            foreach (char c in text)
            {
                var row = rows[^1];
                switch (c)
                {
                    case '\r': column = 0; break;
                    case '\n': rows.Add(new()); column = 0; break;
                    case '\b': if (column > 0) column--; break;
                    default:
                        if (column < row.Length) row[column] = c;
                        else row.Append(c);
                        column++;
                        break;
                }
            }
        }

        public string[] Rows => [.. rows.Select(r => r.ToString().TrimEnd())];

        public string Last => Rows[^1];
    }

    static (ConsolePrompt Prompt, Screen Screen) Start() => (new ConsolePrompt(), new Screen());

    static string? Type(ConsolePrompt prompt, Screen screen, string keys)
    {
        string? handed = null;
        foreach (char c in keys)
        {
            var (echo, line) = c switch
            {
                '\r' => prompt.Key('\r', ConsolePrompt.Enter),
                '\b' => prompt.Key('\b', ConsolePrompt.Back),
                '\x1b' => prompt.Key('\0', ConsolePrompt.Escape),
                _ => prompt.Key(c, 0),
            };
            screen.Write(echo);
            handed ??= line;
        }
        return handed;
    }

    [Fact]
    public void The_prompt_follows_every_line_the_host_prints()
    {
        var (prompt, screen) = Start();
        screen.Write(prompt.Print("console: opened a console window of its own (UTF-8)"));
        screen.Write(prompt.Print("net: hosting \"Server's game\" on UDP 4994"));
        Assert.Equal(["console: opened a console window of its own (UTF-8)", "net: hosting \"Server's game\" on UDP 4994", ">"], screen.Rows);
        Assert.Equal(ConsolePrompt.Prompt, "> ");
    }

    [Fact]
    public void A_line_printed_while_something_is_half_typed_goes_above_it_and_the_typing_is_kept()
    {
        var (prompt, screen) = Start();
        screen.Write(prompt.Print("net: hosting"));
        Assert.Null(Type(prompt, screen, "/pla"));
        Assert.Equal("> /pla", screen.Last);
        screen.Write(prompt.Print("  room: Bob has joined the game."));
        screen.Write(prompt.Print("x"));
        Assert.Equal(["net: hosting", "  room: Bob has joined the game.", "x", "> /pla"], screen.Rows);
        Assert.Equal("/players", Type(prompt, screen, "yers\r"));
        Assert.Equal(["net: hosting", "  room: Bob has joined the game.", "x", "> /players", ">"], screen.Rows);
        Assert.Equal("", prompt.Typed);
    }

    [Fact]
    public void Backspace_takes_the_last_off_escape_clears_and_enter_hands_in_the_line()
    {
        var (prompt, screen) = Start();
        screen.Write(prompt.Print("ready"));
        Assert.Null(Type(prompt, screen, "/quiz\b"));
        Assert.Equal("> /qui", screen.Last);
        Assert.Null(Type(prompt, screen, "\x1b"));
        Assert.Equal(">", screen.Last);
        Assert.Equal("", prompt.Typed);
        Assert.Null(Type(prompt, screen, "\b\b"));
        Assert.Equal(">", screen.Last);
        Assert.Equal("/quit", Type(prompt, screen, "/quit\r"));
        Assert.Equal("", Type(prompt, screen, "\r"));
        Assert.Equal(["ready", "> /quit", ">", ">"], screen.Rows);
    }

    [Fact]
    public void Keys_with_no_character_are_ignored_and_letters_outside_ascii_are_kept()
    {
        var (prompt, screen) = Start();
        screen.Write(prompt.Print("ready"));
        Assert.Equal(("", (string?)null), prompt.Key('\0', 0x25));
        Assert.Equal(("", (string?)null), prompt.Key('\t', 0x09));
        Assert.Equal("Šárka", Type(prompt, screen, "Šárka\r"));
    }

    [Fact]
    public void The_end_replaces_the_prompt_with_its_last_line()
    {
        var (prompt, screen) = Start();
        screen.Write(prompt.Print("net: the host has stopped after 12 s"));
        Type(prompt, screen, "/play");
        screen.Write(prompt.End());
        Assert.Equal(["net: the host has stopped after 12 s", ConsolePrompt.Ended, ""], screen.Rows);
        Assert.Equal("The host has ended. Press a key to close this window.", ConsolePrompt.Ended);
        Assert.All(ConsolePrompt.Ended, c => Assert.True(c is >= ' ' and <= '~'));
    }

    [Fact]
    public void A_game_in_a_window_s_lines_go_to_its_log_and_wait_while_it_cannot_take_them()
    {
        var logged = new List<string>();
        bool open = false;
        var lines = new ConsoleLines(line => { if (!open) return false; logged.Add(line); return true; });
        lines.Out.WriteLine("random seed: 1");
        lines.Out.Write("features: ");
        lines.Out.Write("faithful rules\r\n");
        Assert.Empty(logged);
        open = true;
        lines.Error.WriteLine("net: UDP port 4994 is already held by another program");
        Assert.Equal(["random seed: 1", "features: faithful rules", "net: UDP port 4994 is already held by another program"], logged);
        Assert.Equal(["net: UDP port 4994 is already held by another program"], lines.LastErrors);
        Assert.Equal(3, lines.Recent.Count);
    }

    [Fact]
    public void A_line_the_log_has_already_is_not_given_to_it_again()
    {
        var logged = new List<string>();
        bool echo = false;
        var lines = new ConsoleLines(line => { logged.Add(line); return true; }) { AlreadyLogged = () => echo };
        echo = true;
        lines.Error.WriteLine("warn   Net  an echo of a log line");
        echo = false;
        lines.Out.WriteLine("net: hosting");
        Assert.Equal(["net: hosting"], logged);
        Assert.Equal(["warn   Net  an echo of a log line", "net: hosting"], lines.Recent);
    }

    [Fact]
    public void A_log_window_opened_later_shows_the_lines_from_before_then_each_as_it_comes_and_none_after_it_closes()
    {
        var lines = new ConsoleLines();
        lines.Out.WriteLine("one");
        lines.Out.WriteLine("two");
        var window = new StringWriter();
        lines.Show(window);
        Assert.True(lines.Showing);
        lines.Out.WriteLine("three");
        lines.Show(null);
        lines.Out.WriteLine("four");
        Assert.Equal("one\ntwo\nthree\n", window.ToString().Replace("\r\n", "\n"));
        Assert.False(lines.Showing);
    }

    [Fact]
    public void Only_so_many_lines_are_kept()
    {
        var lines = new ConsoleLines(_ => false);
        for (int i = 0; i < ConsoleLines.Kept + 10; i++) lines.Out.WriteLine($"line {i}");
        Assert.Equal(ConsoleLines.Kept, lines.Recent.Count);
        Assert.Equal("line 10", lines.Recent[0]);
        for (int i = 0; i < ConsoleLines.ErrorsKept + 3; i++) lines.Error.WriteLine($"error {i}");
        Assert.Equal(ConsoleLines.ErrorsKept, lines.LastErrors.Count);
    }
}
