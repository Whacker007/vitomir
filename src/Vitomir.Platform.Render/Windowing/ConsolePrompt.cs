// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The dedicated host's prompt and its typed line ("I do want there to be an actual
// identifiable command prompt in the headless terminal. A simple > would suffice."). The line handling is here, a pure state
// machine tested on every OS; TextConsole feeds it the console's keys (ReadConsoleInputW) and writes what it answers.
using System.Text;

namespace Vitomir.Platform.Render.Windowing;

/// <summary>
/// **The host's prompt**: <c>&gt; </c> where the host types, after the opening lines and again after every line the host
/// prints, with whatever the host had typed so far kept and put back after it. The console's own line input is off (the keys
/// are read one by one), so this keeps the line: a printable key goes on its end and is echoed, Backspace takes the last off,
/// Escape clears it, Enter hands it in and starts a fresh prompt. A line the host prints while something is half typed goes
/// above it: back to the line's start, the printed line over the prompt (padded with spaces over what was there), a new line,
/// then the prompt and the typed part again. Plain characters only, no terminal escapes (a console window of the program's
/// own, which may not have them on).
/// </summary>
public sealed class ConsolePrompt
{
    /// <summary>The prompt.</summary>
    public const string Prompt = "> ";

    /// <summary>The last line, which the console's keys hold at the end of every window.</summary>
    public const string Ended = "The host has ended. Press a key to close this window.";

    readonly StringBuilder typed = new();
    bool shown;

    /// <summary>What has been typed on the prompt's line and not yet handed in.</summary>
    public string Typed => typed.ToString();

    /// <summary>What to write to show the prompt the first time (nothing once it is up).</summary>
    public string Show()
    {
        if (shown) return "";
        shown = true;
        return Prompt + typed;
    }

    /// <summary>
    /// What to write for a line the host prints: the prompt's line overwritten by it (padded over the prompt and what was typed),
    /// a new line, the prompt and the typed part again.
    /// </summary>
    public string Print(string line)
    {
        var o = new StringBuilder();
        if (shown)
        {
            int was = Prompt.Length + typed.Length;
            o.Append('\r').Append(line);
            if (line.Length < was) o.Append(' ', was - line.Length);
        }
        else o.Append(line);
        o.Append("\r\n").Append(Prompt).Append(typed);
        shown = true;
        return o.ToString();
    }

    /// <summary>The keys a console gives that the prompt reads (virtual-key codes).</summary>
    public const ushort Enter = 0x0D, Back = 0x08, Escape = 0x1B;

    /// <summary>
    /// One key pressed: what to write (the echo), and the line handed in when it was Enter (null otherwise; an empty line is
    /// handed in as empty). <paramref name="character"/> is the key's character (0 for none), <paramref name="key"/> its
    /// virtual-key code.
    /// </summary>
    public (string Echo, string? Line) Key(char character, ushort key)
    {
        if (key == Enter || character == '\r')
        {
            string line = typed.ToString();
            typed.Clear();
            shown = true;
            return ("\r\n" + Prompt, line);
        }
        if (key == Back || character == '\b')
        {
            if (typed.Length == 0) return ("", null);
            typed.Length--;
            return ("\b \b", null);
        }
        if (key == Escape)
        {
            int n = typed.Length;
            typed.Clear();
            return (n == 0 ? "" : new string('\b', n) + new string(' ', n) + new string('\b', n), null);
        }
        if (character >= ' ' && character != '\x7f' && typed.Length < 1000)
        {
            typed.Append(character);
            return (character.ToString(), null);
        }
        return ("", null);
    }

    /// <summary>What to write for the end's last line (on a line of its own, the prompt overwritten).</summary>
    public string End()
    {
        var o = new StringBuilder();
        if (shown)
        {
            int was = Prompt.Length + typed.Length;
            o.Append('\r').Append(Ended);
            if (Ended.Length < was) o.Append(' ', was - Ended.Length);
        }
        else o.Append(Ended);
        shown = false;
        typed.Clear();
        return o.Append("\r\n").ToString();
    }
}
