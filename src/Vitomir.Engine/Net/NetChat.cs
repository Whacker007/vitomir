// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Multiplayer chat: what a typed line means (on the server), and how a line is shown (on the
// client).
using System.Globalization;

namespace Vitomir.Engine.Net;

/// <summary>The kinds of line the host sends and a client shows.</summary>
public enum ChatKind : byte
{
    /// <summary>0: plain speech, `MP_SAY`.</summary>
    Say = 0,
    /// <summary>1: `!text`, `MP_SHOUT`.</summary>
    Shout = 1,
    /// <summary>2: `/name text` or `.text` (to player 0), `MP_WHISPER`.</summary>
    Whisper = 2,
    /// <summary>3: `:text`, an emote — "%s %s"; bow, menace, taunt and wave play modes 0xbf–0xc2.</summary>
    Emote = 3,
    /// <summary>5: `;text`, the server's own only. Its showing is unread (the client's case 5 shows nothing we have found).</summary>
    Narrate = 5,
    /// <summary>6: `%n`, n ten-sided dice, `MP_ROLLS`.</summary>
    Roll = 6,
    /// <summary>7: `,text`, `MP_TEAMSAY`.</summary>
    Team = 7,
    /// <summary>8: said in the lobby rather than in a game, `MP_LOBBYSAY`.</summary>
    Lobby = 8,
}

/// <summary>One line as the host decided it: who said it, to whom (−1 everybody), and what.</summary>
public readonly record struct ChatLine(ChatKind Kind, int From, int To, string Text);

public static class NetChat
{
    /// <summary>The longest line (cut at 0x78; the edit fields' limit is the same).</summary>
    public const int MaxLength = 120;
    /// <summary>The most dice one `%` rolls (`if (0x13 < n) n = 0x14`).</summary>
    public const int MaxDice = 20;

    /// <summary>The colours each kind is written in.</summary>
    public static uint Colour(ChatKind kind) => kind switch
    {
        ChatKind.Say or ChatKind.Lobby => 0xffddddffu,
        ChatKind.Shout => 0xffd04040u,
        ChatKind.Whisper => 0xff40d040u,
        ChatKind.Emote => 0xff40ddddu,
        ChatKind.Roll => 0xffd0d040u,
        ChatKind.Team => 0xffdd80ffu,
        _ => 0xffffffffu,
    };

    /// <summary>The four emotes that play an animation, and the mode each plays.</summary>
    public static readonly (string Key, int Mode)[] Emotes =
        [("MP_EMOTE_BOW", 0xbf), ("MP_EMOTE_MENACE", 0xc0), ("MP_EMOTE_TAUNT", 0xc1), ("MP_EMOTE_WAVE", 0xc2)];

    /// <summary>
    /// What a typed line means, the host's way: the first character picks the kind. A
    /// whisper names its player by the start of the line, case-insensitively, against every name in
    /// <paramref name="names"/> (slot order); a whisper to nobody comes back as <c>To = -2</c>, which
    /// the host answers with `UI_NET_WHISPERNOTFOUND`. <paramref name="inGame"/> false is the lobby,
    /// where every line is <see cref="ChatKind.Lobby"/>.
    /// </summary>
    public static ChatLine Parse(string typed, int from, IReadOnlyList<string?> names, bool inGame, Func<int, int> roll)
    {
        string text = typed.Length > MaxLength ? typed[..MaxLength] : typed;
        if (!inGame) return new ChatLine(ChatKind.Lobby, from, -1, text);
        if (text.Length == 0) return new ChatLine(ChatKind.Say, from, -1, text);
        switch (text[0])
        {
            case '!': return new ChatLine(ChatKind.Shout, from, -1, text[1..]);
            case ':': return new ChatLine(ChatKind.Emote, from, -1, text[1..]);
            case '.': return new ChatLine(ChatKind.Whisper, from, 0, text[1..]);
            case ';' when from == 0: return new ChatLine(ChatKind.Narrate, from, -1, text[1..]);
            case ',': return new ChatLine(ChatKind.Team, from, -1, text[1..]);
            case '%':
            {
                int dice = int.TryParse(text.AsSpan(1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
                dice = Math.Clamp(dice, 1, Rules.GameRules.Current.Party.ChatMaxDice);
                var rolls = new List<int>();
                for (int i = 0; i < dice; i++) rolls.Add(roll(10) + 1);
                return new ChatLine(ChatKind.Roll, from, -1, string.Join(", ", rolls));
            }
            case '/':
            {
                string rest = text[1..];
                for (int slot = 0; slot < names.Count; slot++)
                {
                    if (names[slot] is not { Length: > 0 } name) continue;
                    if (!rest.StartsWith(name, StringComparison.OrdinalIgnoreCase)) continue;
                    string said = rest[name.Length..];
                    if (said.Length > 0 && !char.IsWhiteSpace(said[0])) continue;
                    return new ChatLine(ChatKind.Whisper, from, slot, said.TrimStart());
                }
                return new ChatLine(ChatKind.Whisper, from, -2, rest);
            }
        }
        return new ChatLine(ChatKind.Say, from, -1, text);
    }

    /// <summary>
    /// A line as the chat box shows it: the kind's string with the speaker as `%S`, the
    /// text as `%T` and the whisper's recipient as `%R`; an emote is "name text".
    /// </summary>
    public static string Show(ChatKind kind, string speaker, string text, string? recipient, Func<string, string?> find)
    {
        string Fmt(string key) => Ui.Strings.Format(find, key, $"%S{speaker}%T{text}" + (recipient is null ? "" : $"%R{recipient}"));
        return kind switch
        {
            ChatKind.Say => Fmt("MP_SAY"),
            ChatKind.Shout => Fmt("MP_SHOUT"),
            ChatKind.Whisper => Fmt(recipient is null ? "MP_WHISPER" : "MP_WHISPERALT"),
            ChatKind.Emote => $"{speaker} {text}",
            ChatKind.Roll => Fmt("MP_ROLLS"),
            ChatKind.Team => Fmt("MP_TEAMSAY"),
            ChatKind.Lobby => Fmt("MP_LOBBYSAY"),
            _ => $"{speaker}: {text}",
        };
    }

    /// <summary>
    /// The emote's animation mode when the text begins with one of the four (each compared, case aside, over its
    /// string's length, in the order bow, menace, taunt, wave), else −1.
    /// </summary>
    public static int EmoteMode(string text, Func<string, string?> find)
    {
        // The compare is `_strnicmp` over the word's own length: the line need only begin with it (until then
        // Vitomir asked for the whole line, trimmed).
        foreach (var (key, mode) in Emotes)
        {
            string word = find(key) ?? key;
            if (text.Length >= word.Length && string.Compare(text, 0, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0)
                return mode;
        }
        return -1;
    }
}
