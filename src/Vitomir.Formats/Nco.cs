// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A conversation, as the original stores one.
//
// The format as the game's loader and line parser take it, not as the SDK's prose has it, checked against all 310 shipped files. Two things about it are worth
// knowing before the grammar:
//
//   * **an `.nco` holds no dialogue.** Every line is an *identifier* — `Kazi_10_1_103` — and the
//     words come out of the localised strings (`.nls`). The generator that wrote these files put the
//     English text in a trailing `//` comment for the reader's benefit, and the engine never sees it.
//     So a conversation is a graph, not a script.
//   * **the vocabulary is seven keywords and nothing else.** The parser is a chain of
//     case-insensitive compares against `LINE:`, `ANSWERGOTO:`, `ANSWERRETURN:`, `EVENT:`, `JUMP:`,
//     `DELAY:` and `CONVERSATION:`; anything else is silently ignored, which is also how the comment
//     blocks survive being read.
using System.Globalization;

namespace Vitomir.Formats;

/// <summary>
/// What one step of a conversation is, by the value the parser writes to the record's step field.
/// </summary>
public enum NcoStep
{
    /// <summary>Somebody says a line (kind 0).</summary>
    Line = 0,
    /// <summary>A player reply that moves to another event (kind 1).</summary>
    AnswerGoto = 1,
    /// <summary>A player reply that ends the conversation with a code (kind 2).</summary>
    AnswerReturn = 2,
    /// <summary>Go to another event with no reply (kind 3).</summary>
    Jump = 3,
    /// <summary>Wait (kind 4).</summary>
    Delay = 4,
}

/// <summary>
/// One step. Two of its fields mean different things by
/// <see cref="Kind"/>, so this names each meaning:
///
/// | engine field | `LINE:` | `ANSWERGOTO:` | `ANSWERRETURN:` | `JUMP:` | `DELAY:` |
/// |---|---|---|---|---|---|
/// | flags | <see cref="Flags"/> | <see cref="Flags"/> | <see cref="Flags"/> | — | — |
/// | extra time | <see cref="ExtraMs"/> | <see cref="ExtraMs"/> | <see cref="ExtraMs"/> | — | — |
/// | first text, 32 bytes | <see cref="Id"/> | <see cref="Id"/> | <see cref="Id"/> | <see cref="Event"/> | <see cref="Pause"/>, as an **int** |
/// | second text, 32 bytes | <see cref="Speaker"/> | <see cref="Event"/> | <see cref="Code"/>, as an **int** | *the resolved event index* | — |
/// | resolved target | the speaker's **thing GUID** | *the resolved event index* | — | — | — |
/// | display name | the speaker's **display name** | — | — | — | — |
///
/// The last two rows are filled in after the file is read, not by the parser: a pass over the file resolves
/// every `JUMP:`'s and `ANSWERGOTO:`'s target name to an **event index** once the whole file is in —
/// a `JUMP:` over its own second text and an `ANSWERGOTO:` into its resolved target — and the start fills a
/// `LINE:`'s last two when a conversation starts. A target that matches no event is
/// **left alone rather than reported**, so an `ANSWERGOTO:` that named nothing would silently go to
/// event 0. Every shipped target resolves.
///
/// Every string the engine keeps is <b>32 bytes, truncated without complaint</b>, and so is every
/// token: the tokeniser stops copying a token at 32 characters. No shipped file comes
/// near it — the longest token in all 310 is well under — so the cap is recorded rather than
/// exercised.
/// </summary>
public sealed record NcoLine
{
    public NcoStep Kind { get; init; }

    /// <summary>The line's identifier, which is a key into the localised strings, not the words.</summary>
    public string Id { get; init; } = "";

    /// <summary>Who says it — a cast name like `Christof`. 95 distinct across the shipped files.</summary>
    public string Speaker { get; init; } = "";

    /// <summary>The event a <see cref="NcoStep.Jump"/> or an <see cref="NcoStep.AnswerGoto"/> goes to.</summary>
    public string Event { get; init; } = "";

    /// <summary>An <see cref="NcoStep.AnswerReturn"/>'s code. Only 1, 2 and 3 occur, 20 times in all.</summary>
    public int Code { get; init; }

    /// <summary>
    /// A <see cref="NcoStep.Delay"/>'s wait, **in milliseconds**, from a file that writes seconds.
    /// The parser reads the token with `atof`, **multiplies it by 1000.0**, makes it positive and
    /// truncates; the runtime then adds that to the engine's millisecond clock. The 58 shipped delays read **0.5 to 8** and mean
    /// exactly what they look like: half a second to eight seconds.
    ///
    /// **Read wrongly once**, when the multiply by 1000 was
    /// missed and the field was recorded as raw milliseconds. Nothing about this field is a bug.
    /// </summary>
    public int Pause { get; init; }

    /// <summary>
    /// The optional fourth token — and it is not spare, it is **flags on the line**,
    /// read by the runtime:
    ///
    /// <list type="bullet">
    /// <item>bits <c>1</c>, <c>2</c>, <c>4</c> and <c>8</c> pick the voice's **volume** — 50%, 63%,
    /// 87%, 100%, tested in that order, and <b>75% when none of them is set</b>;</item>
    /// <item>bit <c>0x10</c> means **do not wait for the line**: the step's own length becomes just
    /// <see cref="ExtraMs"/>.</item>
    /// </list>
    ///
    /// **No shipped line has a fourth token**, so every line in the game plays at 75% and waits.
    /// </summary>
    public int Flags { get; init; }

    /// <summary>
    /// The optional fifth token: **milliseconds added to however long the step takes**
    /// (a step lasts <c>length + 500 + this</c>). Never present in the shipped data.
    /// </summary>
    public int ExtraMs { get; init; }
}

/// <summary>
/// One event: a name and the steps under it. The engine's record is **40 bytes** — a 32-byte name,
/// a count and a pointer — and the steps are copied out of a staging buffer when the next `EVENT:`
/// or `CONVERSATION:` arrives.
///
/// <c>ev_0</c> is where a conversation starts, and every one of the 310 shipped files has it.
/// </summary>
public sealed record NcoEvent(string Name)
{
    public List<NcoLine> Steps { get; } = [];
}

/// <summary>One conversation: a name, the number after it, and its events in file order.</summary>
public sealed class NcoConversation(string name, int number)
{
    public string Name { get; } = name;

    /// <summary>
    /// The second token of the `CONVERSATION:` line. **Zero in all 310 shipped files**, so what it
    /// would do is unknown; recorded because it is parsed.
    /// </summary>
    public int Number { get; } = number;

    public List<NcoEvent> Events { get; } = [];

    /// <summary>The event of that name, case-insensitively, or null.</summary>
    public NcoEvent? Event(string name) =>
        Events.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Where a conversation begins.</summary>
    public const string Start = "ev_0";

    /// <inheritdoc cref="Start"/>
    public NcoEvent? First => Event(Start);
}

/// <summary>
/// A conversation file. It can hold more than one `CONVERSATION:` block — the loader keeps them all
/// and hands back the one whose name was asked for — though every shipped file holds exactly one,
/// named after itself.
/// </summary>
public sealed class NcoFile
{
    public string Name { get; init; } = "";
    public List<NcoConversation> Conversations { get; } = [];

    /// <summary>
    /// Lines the engine would ignore that were not blank and not a comment — so a check over the
    /// shipped files can say the vocabulary is complete. It is: all 310 files together produce
    /// **2,518 content lines and nothing dropped**.
    /// </summary>
    public List<string> Dropped { get; } = [];

    /// <summary>The conversation of that name, case-insensitively, or null.</summary>
    public NcoConversation? Conversation(string name) =>
        Conversations.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>How long a line may be before the engine reads the rest of it and throws it away.</summary>
    public const int LineLength = 1024;
    /// <summary>How much of a token the engine keeps.</summary>
    public const int TokenLength = 32;

    /// <summary>
    /// Reads one. The engine's own shape: `fgets` a line of at most <see cref="LineLength"/>, skip
    /// leading whitespace, ignore it when the first character left is `\0`, `\r`, `\n`, `#`, or the
    /// start of a `//`, and otherwise split it on whitespace and match the first token.
    ///
    /// Three of the seven keywords want an **exact** token count and the engine drops the line
    /// otherwise: `EVENT:`, `JUMP:` and `DELAY:` want 2. `LINE:`, `ANSWERGOTO:` and `ANSWERRETURN:`
    /// want **more than 2** and read a 4th and 5th if they are there. `CONVERSATION:` wants more
    /// than 1.
    /// </summary>
    public static NcoFile Read(ReadOnlySpan<byte> data, string fileName)
    {
        var file = new NcoFile { Name = Path.GetFileNameWithoutExtension(fileName) };
        NcoConversation? conversation = null;
        NcoEvent? into = null;

        foreach (var raw in System.Text.Encoding.Latin1.GetString(data).Split('\n'))
        {
            var text = raw.Length > LineLength ? raw[..LineLength] : raw;
            var line = text.TrimStart();
            if (line.Length == 0 || line[0] is '\r' or '#') continue;
            if (line.StartsWith("//", StringComparison.Ordinal)) continue;

            // The engine's own tokenizer steps one field at a time from a fixed table;
            // `Split` gets every field of the line at once.
            var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
                if (words[i].Length > TokenLength) words[i] = words[i][..TokenLength];

            // `CONVERSATION:` first, because it is the only one that may appear before there is one.
            if (Is(words[0], "CONVERSATION:"))
            {
                if (words.Length < 2) { file.Dropped.Add(line.Trim()); continue; }
                conversation = new NcoConversation(words[1], words.Length > 2 ? Int(words[2]) : 0);
                file.Conversations.Add(conversation);
                into = null;
                continue;
            }
            if (Is(words[0], "EVENT:"))
            {
                // The engine does not require a conversation for EVENT:, only for the rest.
                if (words.Length != 2) { file.Dropped.Add(line.Trim()); continue; }
                into = new NcoEvent(words[1]);
                conversation?.Events.Add(into);
                continue;
            }
            if (conversation is null) { file.Dropped.Add(line.Trim()); continue; }

            NcoLine? step = null;
            if (Is(words[0], "LINE:") && words.Length > 2)
                step = new NcoLine { Kind = NcoStep.Line, Id = words[1], Speaker = words[2] };
            else if (Is(words[0], "ANSWERGOTO:") && words.Length > 2)
                step = new NcoLine { Kind = NcoStep.AnswerGoto, Id = words[1], Event = words[2] };
            else if (Is(words[0], "ANSWERRETURN:") && words.Length > 2)
                step = new NcoLine { Kind = NcoStep.AnswerReturn, Id = words[1], Code = Int(words[2]) };
            else if (Is(words[0], "JUMP:") && words.Length == 2)
                step = new NcoLine { Kind = NcoStep.Jump, Event = words[1] };
            else if (Is(words[0], "DELAY:") && words.Length == 2)
                step = new NcoLine { Kind = NcoStep.Delay, Pause = Seconds(words[1]) };

            if (step is null) { file.Dropped.Add(line.Trim()); continue; }
            // The two optional tokens, which nothing ships.
            if (step.Kind is NcoStep.Line or NcoStep.AnswerGoto or NcoStep.AnswerReturn)
                step = step with
                {
                    Flags = words.Length > 3 ? Int(words[3]) : 0,
                    ExtraMs = words.Length > 4 ? Int(words[4]) : 0,
                };
            // A step before any EVENT: has nowhere to go, and the engine stages it into the event it
            // is about to close — so it belongs to whatever event comes next. No shipped file does it.
            if (into is null) { file.Dropped.Add(line.Trim()); continue; }
            into.Steps.Add(step);
        }
        return file;
    }

    static bool Is(string word, string keyword) =>
        string.Equals(word, keyword, StringComparison.OrdinalIgnoreCase);

    /// <summary>`atoi`: as much of a leading integer as there is, and 0 for anything else.</summary>
    static int Int(string word)
    {
        int i = 0, sign = 1;
        if (i < word.Length && (word[i] == '-' || word[i] == '+')) sign = word[i++] == '-' ? -1 : 1;
        long value = 0;
        for (; i < word.Length && char.IsAsciiDigit(word[i]); i++) value = value * 10 + (word[i] - '0');
        return (int)(sign * value);
    }

    /// <summary>
    /// A `DELAY:`'s wait: `atof`, **times a thousand**, made positive, then truncated — so the
    /// file's seconds become the engine's milliseconds (a float, times 1000.0,
    /// made positive, then truncated).
    ///
    /// **The multiply was missed the first time this was worked out**, and the conclusion drawn from that
    /// — that a delay is milliseconds and the longest pause in the game is eight of them — was
    /// wrong and is retracted. It cost the convent's collapse: a
    /// three-second beat became three milliseconds, so Christof's fall and the fade over it were
    /// gone before a frame had drawn (twice).
    /// </summary>
    static int Seconds(string word) =>
        double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
            ? (int)Math.Abs(d * 1000.0) : 0;
}
