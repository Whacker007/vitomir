// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The developer console against 1.1's console: the
/// line it runs and its key hook: a run closes it and keeps the line in a
/// 32-line history unless it repeats the newest, the first word is the command, [ and ] walk the
/// history round, Tab completes a name, the grave key closes, and an unknown word says so.
/// </summary>
public class DevConsoleTests
{
    static (FrontEnd Menus, DevConsole Console, List<string> Said) Open(GameFiles files)
    {
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find) { State = FrontEnd.StateGame };
        var console = new DevConsole(menus);
        menus.DevConsole = console;
        var said = new List<string>();
        console.Posted += said.Add;
        return (menus, console, said);
    }

    static void Type(FrontEnd menus, string s)
    {
        foreach (char c in s) menus.Type(c);
    }

    [Fact]
    public void Enter_runs_the_first_word_with_the_rest_as_arguments_and_closes_the_console()
    {
        using var files = Install.Open();
        var (menus, console, said) = Open(files);
        string[]? got = null;
        console.Commands["cash"] = a => { got = a; return "Current Cash: 7"; };
        console.Open();
        Assert.True(console.IsOpen);
        Assert.True(console.Pane.Dialog);              // `dialog=1`: it has the keyboard
        Type(menus, "CASH 500 x");
        menus.Drawn();
        menus.Key(MenuKey.Enter);
        Assert.Equal(["500", "x"], got!);
        Assert.Equal(["Current Cash: 7"], said);
        Assert.False(console.IsOpen);
        Assert.Equal(["CASH 500 x"], console.History);
    }

    [Fact]
    public void The_history_keeps_32_lines_skips_a_repeat_and_brackets_walk_it_round()
    {
        using var files = Install.Open();
        var (menus, console, _) = Open(files);
        for (int i = 0; i < 40; i++) console.Run($"echo {i}");
        console.Run("echo 39");
        Assert.Equal(DevConsole.HistoryLines, console.History.Count);
        Assert.Equal("echo 39", console.History[0]);
        console.Open();
        menus.Type('[');
        Assert.Equal("echo 39", console.Pane.Edits[DevConsole.Edit].Text);
        menus.Type('[');
        Assert.Equal("echo 38", console.Pane.Edits[DevConsole.Edit].Text);
        menus.Type(']');
        menus.Type(']');
        Assert.Equal("echo 8", console.Pane.Edits[DevConsole.Edit].Text);   // wrapped round to the oldest
    }

    [Fact]
    public void Tab_completes_a_command_name_and_the_grave_key_closes()
    {
        using var files = Install.Open();
        var (menus, console, _) = Open(files);
        console.Open();
        Type(menus, "getch");
        menus.Type('\t');
        Assert.Equal("getchronflag", console.Pane.Edits[DevConsole.Edit].Text);
        menus.Type('`');
        Assert.False(console.IsOpen);
    }

    /// <summary>
    /// **The command word ends at a comma as well as a space**, and the rest of the line
    /// after it is the argument as typed — which is what `echo` prints and what a
    /// discipline's name with spaces in it is looked up by.
    /// </summary>
    [Fact]
    public void The_word_ends_at_a_comma_and_the_rest_is_kept_as_typed()
    {
        using var files = Install.Open();
        var (_, console, _) = Open(files);
        string[]? got = null;
        console.Commands["echo"] = a => { got = a; return null; };
        console.Run("ECHO,two  spaces\tand a tab");
        Assert.Equal("two  spaces\tand a tab", console.Rest);
        Assert.Equal(["two", "spaces", "and", "a", "tab"], got!);
    }

    [Fact]
    public void An_unknown_word_is_unrecognized_and_one_of_1_1s_is_named_as_not_built()
    {
        using var files = Install.Open();
        var (_, console, said) = Open(files);
        Assert.False(console.Run("frobnicate"));
        Assert.False(console.Run("heads"));
        Assert.Equal(DevConsole.Unrecognized, said[0]);
        Assert.Contains("heads", said[1]);
        Assert.Equal(94, DevConsole.OriginalCommands.Length);   // 98 registrations, four of them twice
    }
}
