// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The game's message log: the status bar's `sbTextbox`, which every subsystem writes its one-line
// news into — "Christof dies.", "Quest log has been updated.", "You pickup 30 gold."
namespace Vitomir.Engine.Ui;

/// <summary>One line of the log and the colour it was written in (<c>0xAARRGGBB</c>).</summary>
/// <param name="Colour">0 for the box's own colour, which is what an item added without one gets.</param>
public readonly record struct Message(string Text, uint Colour);

/// <summary>
/// The status bar's message box (`sbTextbox`, `maxItems=100`). Things are
/// **appended** to it and the box drops its oldest line when a hundred-and-first
/// arrives. What goes in is almost always a string-table line with its blanks filled in
/// (<see cref="Format"/>), in one of three colours: the box's own, <b>red</b> for a death and
/// <b>yellow</b> for good news.
/// </summary>
public sealed class MessageLog
{
    /// <summary>The box's `maxItems`.</summary>
    public const int MaxItems = 100;

    /// <summary>The colours the engine writes with: a death and good news.</summary>
    public const uint Own = 0, Red = 0xFFFF0000u, Yellow = 0xFFFFFF00u;

    readonly List<Message> lines = [];

    /// <summary>The string table the keys are looked up in. Null answers every key with itself.</summary>
    public Strings? Text { get; set; }

    /// <summary>What is in the box, oldest first.</summary>
    public IReadOnlyList<Message> Lines => lines;

    /// <summary>Goes up by one every time a line is added or the box emptied, so a drawer can tell it has changed.</summary>
    public int Version { get; private set; }

    /// <summary>How many lines have ever been added — which tells a view an add from an emptying (<see cref="MessageBox"/>).</summary>
    public long Added { get; private set; }

    /// <summary>Also told about every line — the console, the journal.</summary>
    public event Action<Message>? Posted;

    /// <summary>Appends one line, dropping the oldest at <see cref="MaxItems"/>.</summary>
    public void Add(string text, uint colour = Own)
    {
        if (lines.Count >= MaxItems) lines.RemoveAt(0);
        var line = new Message(text, colour);
        lines.Add(line);
        Version++;
        Added++;
        Posted?.Invoke(line);
    }

    /// <summary>Empties the box (its Clear) — the advancement screen does, on opening and on every revert.</summary>
    public void Clear()
    {
        lines.Clear();
        Version++;
    }

    /// <summary>A string-table line with its blanks filled (<see cref="Format"/>), appended.</summary>
    public void Post(string key, string? args = null, uint colour = Own) => Add(Format(key, args), colour);

    /// <summary>
    /// The engine's line builder. <paramref name="args"/> is a run of <c>%X value</c>
    /// pairs — <c>"%PChristof"</c>, <c>"%D30"</c> — and every <c>%X</c> in the looked-up line takes
    /// the value of the pair with the same letter. <b>An upper-case letter is put in as it is; a
    /// lower-case one is a key</b> and is looked up in turn, which is how
    /// <c>RPG_DISCIPLINELEARNED</c> names the discipline in the player's language. A blank with no
    /// pair is dropped, <c>%</c> and letter both. With no <paramref name="args"/> at all the line
    /// comes back as it stands, blanks and all — which is the other entry to the same table.
    /// </summary>
    public string Format(string key, string? args = null) =>
        args is null ? Say(key) : Strings.Format(k => Text?.Find(k), key, args);

    string Say(string key) => Text is null ? key : Text[key];
}
