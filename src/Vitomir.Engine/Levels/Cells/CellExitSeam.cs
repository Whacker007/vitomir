// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Levels.Cells;

/// <summary>
/// **Ours: where a cell level's exits are taken** — the seam between the scene (this round's
/// "scene" work: 1.1's click-exit signs stand in the new town, say what 1.1's say and run 1.1's
/// <c>ClickExit</c> script) and what taking an exit does in a cell level (the "exits" work). 1.1's script
/// ends in <c>CodexSequence.TakeExit(exit, actor)</c> (into the exit request, which checks the exit and
/// sets the campaign going). In a cell level that native asks here first: <see cref="Taken"/> is told the
/// place, who and which exit, and hands it to <see cref="Take"/> when someone has set it. True from
/// <see cref="Take"/> means the exit was dealt with — a real door walked through, a move inside the hub —
/// and the native does nothing more; false, or nobody listening, and 1.1's journey goes ahead as ever (the
/// chronicle's exit, a level change). In a 1.1 level none of this is asked.
/// </summary>
public static class CellExitSeam
{
    /// <summary>
    /// The exits work's function: the place (a cell level), the character that took the exit, the exit's
    /// number in the chronicle's list for this place. True when it has dealt with it. Null until that work sets it.
    /// </summary>
    public static Func<Location, WorldThing, int, bool>? Take { get; set; }

    /// <summary>The exits taken in cell levels this run, newest last (place, exit) — for logs and tests.</summary>
    public static List<(string Place, int Exit)> Log { get; } = [];

    /// <summary>
    /// A character took an exit by 1.1's script. False at once in a 1.1 level. In a cell level it is
    /// logged and offered to <see cref="Take"/>; the answer is whether the exit has been dealt with.
    /// </summary>
    public static bool Taken(Location? place, WorldThing who, int exit)
    {
        if (place is null || CellLevel.Of(place.Level) is not { } cells) return false;
        lock (Log) Log.Add((place.Name, exit));
        bool dealt = Take?.Invoke(place, who, exit) ?? false;
        string line = $"cell exit: {who.Template} took exit {exit} of {place.Name} (the cell level {cells.Name}): " +
                      (dealt ? "dealt with in the level" : "left to 1.1's journey");
        Diag.For(typeof(CellExitSeam).FullName!).LogInformation("{Line}", line);
        Console.WriteLine(line);
        return dealt;
    }
}
