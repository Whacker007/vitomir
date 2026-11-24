// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The console's `aitrace`: every mode change of every mind, in the message box.
namespace Vitomir.Engine.Ai;

/// <summary>
/// **The AI trace**. While the AI's main flags carry <see cref="AiMainFlag.Trace"/> — the
/// developer console's <c>aitrace 1</c> — the mode change and the return to the
/// home mode — and the mind's set-up when it starts a character asleep, flying or frenzied
/// — each put one line in the message box, in the box's own grey:
/// <c>AI '%s' [%d] changed mode: '%s' to '%s'.</c> — the thing's template name,
/// its GUID, and the old and new modes' names from the engine's table, which
/// is one name short (<see cref="AiModes.Logged"/>: mode 1 prints "pet", and so on). It is written on
/// every call, a mode set to itself included. With the trace off (the default) nothing is written.
/// </summary>
public static class AiTrace
{
    /// <summary>The line for one change.</summary>
    public static string Line(Mind mind, AiMode from, AiMode to) =>
        $"AI '{mind.Thing.Template}' [{mind.Thing.Guid}] changed mode: '{AiModes.Logged(from)}' to '{AiModes.Logged(to)}'.";

    /// <summary>The mode change's trace (the end of the mode change): the line, when the trace bit is up.</summary>
    public static void Changed(Mind mind, AiMode from, AiMode to)
    {
        var place = mind.Thing.Place;
        if (place?.Minds is not { } minds || (minds.MainFlags & AiMainFlag.Trace) == 0) return;
        place.Session?.Messages.Add(Line(mind, from, to));
    }
}
