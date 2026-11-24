// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Copy and paste in the in-game text lines** (<see cref="TypedLine"/>; always
/// on, with no switch): Ctrl+V pastes the clipboard's first line with its control characters
/// dropped, on the end, by 1.1's own rule for an edit field (its 0x16 case) — into the
/// developer console's field and into the chat line alike — and Ctrl+C, ours, copies the line being
/// typed, or from an empty line the last output: the console's own last answer, the message box's
/// newest line for the chat.
/// </summary>
public class TypedLineTests
{
    [Fact]
    public void A_paste_is_the_clipboard_s_first_line_on_the_end_with_its_control_characters_dropped()
    {
        Assert.Equal("/wear 0 broadsword", TypedLine.Paste("/wear ", "0 broad\tsword\r\nsethide 3", 80));
        Assert.Equal("abc", TypedLine.Paste("abc", null, 80));
        Assert.Equal("abc", TypedLine.Paste("abc", "", 80));
    }

    [Fact]
    public void A_paste_stops_where_the_line_is_full()
    {
        Assert.Equal("abcde", TypedLine.Paste("abc", "defgh", 5));
        Assert.Equal("abc", TypedLine.Paste("abc", "defgh", 3));
    }

    [Fact]
    public void A_copy_is_the_line_being_typed_or_from_an_empty_line_the_last_output()
    {
        Assert.Equal("sethide 3", TypedLine.Copied("sethide 3", "Current Cash: 7"));
        Assert.Equal("Current Cash: 7", TypedLine.Copied("", "Current Cash: 7"));
        Assert.Null(TypedLine.Copied("", null));
        Assert.Null(TypedLine.Copied(null, ""));
    }

    [Fact]
    public void The_chat_line_s_last_output_is_the_message_box_s_newest_line()
    {
        var log = new MessageLog();
        Assert.Null(TypedLine.Newest(log));
        log.Add("first");
        log.Add("wear: broadsword on Anna — Equipped");
        Assert.Equal("wear: broadsword on Anna — Equipped", TypedLine.Newest(log));
    }

    [Fact]
    public void In_the_developer_console_ctrl_v_pastes_and_ctrl_c_copies_the_line_or_the_last_answer()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find) { State = FrontEnd.StateGame };
        var console = new DevConsole(menus);
        menus.DevConsole = console;
        console.Commands["echo"] = a => string.Join(' ', a);

        console.Open();
        Assert.Null(menus.CopyText);                                    // nothing typed, nothing answered yet
        menus.Paste("echo pasted\r\nline two");
        Assert.Equal("echo pasted", menus.FocusedText);
        Assert.Equal("echo pasted", menus.CopyText);
        menus.Drawn();
        menus.Key(MenuKey.Enter);
        Assert.Equal("pasted", console.LastOutput);

        console.Open();
        Assert.Equal("", menus.FocusedText);
        Assert.Equal("pasted", menus.CopyText);                         // the empty line copies the last answer
        console.Close();
        Assert.Null(menus.CopyText);                                    // no field has the keyboard
    }
}
