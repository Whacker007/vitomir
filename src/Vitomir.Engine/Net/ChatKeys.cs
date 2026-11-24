// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// 1.1's keys for the chat line, read at the head of the game camera's key hook.
// Earlier, Vitomir said no key had been found and opened the line on Enter as its own.
namespace Vitomir.Engine.Net;

/// <summary>The three physical keys the key hook reads for the chat line (DirectInput's scan codes, as SDL's are).</summary>
public enum ChatKey
{
    None,
    /// <summary>The main Enter (`DIK_RETURN` 0x1C) — not the keypad's (0x9C).</summary>
    Enter,
    /// <summary>The; key (`DIK_SEMICOLON` 0x27) with either Shift held: the line opens with ":".</summary>
    Colon,
    /// <summary>The / key (`DIK_SLASH` 0x35), Shift or not: the line opens with "/".</summary>
    Slash,
}

/// <summary>
/// **The chat line's keys**. Each is a press (the key's state
/// stamped with this frame). Each plays the interface sound 0 and then, when the status bar is a network game's (the
/// multiplayer bar and the Storyteller's) and the line is not already being typed in:
/// the bar's current pane is raised (unless the advancement screen is up), the chat
/// edit `sbChatEdit` takes the keys, and **Shift+;** puts ":" in it and
/// **/** puts "/" — the emote and the whisper of the
/// chat's own grammar (`NetChat`). **Enter** opens it empty. The hook as a whole runs only in the
/// game camera with no dialog up, and returns at once while the vault, the
/// advancement screen or the chat line itself is up, and with a shop up
/// reads Escape alone. In the seat the edit taken is the multiplayer bar's, which is hidden
/// behind the Storyteller's (the edit is the multiplayer bar's from when that bar is built) — so 1.1's
/// Storyteller types unseen; Vitomir's line is drawn by the multiplayer bar alone and is not seen there either.
/// </summary>
public static class ChatKeys
{
    /// <summary>The interface sound each of the three keys plays.</summary>
    public const int Sound = 0;

    /// <summary>Which of the chat's keys a press is, from its physical key and whether a Shift is held.</summary>
    public static ChatKey Of(bool enter, bool semicolon, bool slash, bool shift) =>
        enter ? ChatKey.Enter : semicolon && shift ? ChatKey.Colon : slash ? ChatKey.Slash : ChatKey.None;

    /// <summary>What the line opens with: nothing for Enter, ":" and "/" for the other two; null for no key.</summary>
    public static string? Opens(ChatKey key) => key switch
    {
        ChatKey.Enter => "",
        ChatKey.Colon => ":",
        ChatKey.Slash => "/",
        _ => null,
    };

    /// <summary>
    /// Whether the key hook reads the chat's keys now (the hook's head): the game camera, and none
    /// of a dialog, the vault, the advancement screen, a shop or the line itself up.
    /// </summary>
    public static bool Read(bool gameCamera, bool dialog, bool vault, bool advancement, bool shop, bool typing) =>
        gameCamera && !dialog && !vault && !advancement && !shop && !typing;
}
