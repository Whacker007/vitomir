// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Combat;

/// <summary>
/// Where the rules' resolution traces go (ours; <c>--combat-trace &lt; file&gt; </c>): the rules write one JSON line per
/// contact they read (the enhanced module's staged harm: every stage's inputs and outputs, the dice drawn, the profile
/// and the Health before and after), and the program gives the writer. Not the world's state: never digested and never
/// read by the simulation, so a run with a trace is the same run as one without. Without a writer
/// (<see cref="Enabled"/> false) the rules build no line at all.
/// </summary>
public sealed class CombatTrace(TextWriter? writer)
{
    /// <summary>Whether lines are wanted.</summary>
    public bool Enabled => writer is not null;

    /// <summary>Lines written so far.</summary>
    public int Lines { get; private set; }

    /// <summary>The last line written (for a test or the run's summary), or null.</summary>
    public string? Last { get; private set; }

    public void Write(string line)
    {
        if (writer is null) return;
        Lines++;
        Last = line;
        writer.WriteLine(line);
    }

    /// <summary>Flushes the writer (the program does at the end of a run).</summary>
    public void Flush() => writer?.Flush();
}
