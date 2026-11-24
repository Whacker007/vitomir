// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Engine.Ui;

/// <summary>
/// Two small rules of the ui library's edit field (`edit`) that a comparison with 1.1
/// library found missing from <see cref="MenuEdit"/>:
/// <list type="bullet">
/// <item><b>A password field shows stars.</b> The field's redraw (its first virtual)
/// tests its style's bit 0 and, when it is set, writes a `*` for every character of the text
/// (`0x2a2a2a2a` a word at a time) and draws that instead, the caret after it as usual. Only the
/// create-game screen's two password boxes set the bit (`mpcgPW`, `mpcgPWConfirm`; the WON
/// panes' two are left out with WON). What is measured to slide a long line left is still the real
/// text (measure the shown text), so a starred line slides by its letters' widths.</item>
/// <item><b>A text set outright keeps one fewer than the limit.</b> The set copies up to
/// the limit (`strncpy(text)`) and then writes the terminator at `limit - 1` of the text
/// — the limit's last character, since the buffer starts one byte before the text. Typing
/// takes a character while the text is shorter than the limit, so a typed field holds the limit and
/// a set one one fewer. A limit of 0, which would write over the caret character, is never set.</item>
/// </list>
/// </summary>
public static class EditMask
{
    /// <summary>What a password field draws in place of each character.</summary>
    public const char Star = '*';

    /// <summary>The text as the field draws it: stars for a password field, else as it is.</summary>
    public static string Shown(string text, bool password) => password ? new string(Star, text.Length) : text;

    /// <summary>A text set outright into a field that takes <paramref name="max"/>: at most <c>max − 1</c> characters.</summary>
    public static string SetCut(string text, int max)
    {
        int keep = Math.Max(0, max - 1);
        return text.Length > keep ? text[..keep] : text;
    }
}
