// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Copy and paste in the lines a player types commands and chat into: Ctrl+V as 1.1 pastes, Ctrl+C ours.

namespace Vitomir.Engine.Ui;

/// <summary>
/// **Ctrl+C and Ctrl+V in the in-game text lines**.
/// Text can be copied into and out of the in-game host and Storyteller console (reading commands
/// there no longer needs an Alt+Tab): one of the deliberate small changes to the game that is
/// simply useful.
///
/// **Ctrl+V is 1.1's.** Both lines are `edit` elements in 1.1 — the developer console's `consoleEdit`
/// and the multiplayer status bar's `sbChatEdit` (`statusBarMP.nui`), where the host, a player and the
/// Storyteller type chat, `;` narration and `/` commands — and an edit field pastes through
/// The edit's 0x16 case: the clipboard's **first line**, **control characters dropped**, on the end
/// (the caret of these fields is always at the end), while there is room
/// (<see cref="MenuEdit.Pasted"/>). The console's field is a <see cref="MenuEdit"/> and pasted
/// already; Vitomir's chat line keeps its own buffer (`NetPlay.Typing`) and did not, until then.
///
/// **Ctrl+C** is ours everywhere (the edit has no case for character 3): it copies the line being
/// typed, or, when that is empty, the last output — in the developer console the console's own last
/// answer (<see cref="DevConsole.LastOutput"/>), on the chat line the **message box's newest line**,
/// which is where the chat's replies (`/wear 0 broadsword`'s answer, a player's line) are written.
/// </summary>
public static class TypedLine
{
    /// <summary>
    /// <paramref name="line"/> with the clipboard's first line added on the end, by the edit's rule:
    /// characters up to the first line break, each control character dropped (a Windows `\r` among
    /// them), no more looked at than the field takes.
    /// </summary>
    public static string Paste(string line, string? clip, int max) =>
        string.IsNullOrEmpty(clip) ? line : MenuEdit.Pasted(line, clip, max);

    /// <summary>What Ctrl+C copies: the line being typed, or, when it is empty, the newest output line; null when neither has anything.</summary>
    public static string? Copied(string? typed, string? lastOutput) =>
        !string.IsNullOrEmpty(typed) ? typed : string.IsNullOrEmpty(lastOutput) ? null : lastOutput;

    /// <summary>The message box's newest line, or null when it is empty.</summary>
    public static string? Newest(MessageLog? log) => log is { Lines.Count: > 0 } l ? l.Lines[^1].Text : null;
}
