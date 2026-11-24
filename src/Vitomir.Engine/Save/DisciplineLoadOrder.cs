// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Save;

/// <summary>
/// The order a load leaves a character's discipline list in. 1.1 writes the list head first
/// and reads it back one record at a time through the game's own way in:
/// each name is found or made — and a made one goes on the **front** — then its
/// level is set, which for any level but −1 opens the next discipline of its group at −1 on
/// the front when the character does not know it yet, and last the record's flags, quick slot and cast
/// count go on the instance. So **every load turns the list round**, a group's next discipline sitting
/// in front of the one that opened it when it comes later in the file — and since everything that walks
/// the list meets the newest first (the AI's ladder, among them), which discipline a
/// companion reaches for first can change across a save and a load. The first reading of version 5 saves
/// showed it: 1.1's own Monastery save lists Christof's disciplines from `drawingoutbeast` to
/// `celerity`, and the running game after a load from `celerity` on. It is done on the load
/// (<see cref="SaveApply"/>) and not on an import: an imported 1.1 save keeps the file's order in the save
/// it becomes, and loading that turns it round once, as 1.1's load of the file would.
/// </summary>
public static class DisciplineLoadOrder
{
    /// <summary>
    /// The list, head first, as 1.1's load builds it from <paramref name="saved"/> in the file's order: a
    /// saved instance is the one kept for its name; a group's next that no record names is made at −1,
    /// its "new" flag cleared, with no quick slot. With no definitions to ask for a
    /// group's next, the list is only turned round.
    /// </summary>
    public static List<Discipline> Rebuild(IReadOnlyList<Discipline> saved, DisciplineDefinitions? defs, WorldThing owner)
    {
        var list = new List<Discipline>();
        foreach (var d in saved)
        {
            int at = list.FindIndex(x => Same(x.Definition, d.Definition));
            if (at < 0) list.Insert(0, d);
            else list[at] = d;                                                  // found: its place kept, its record's fields put on
            if (d.Level != -1 && defs?.NextInGroup(d.Definition) is { } next
                && !list.Exists(x => Same(x.Definition, next)))
                list.Insert(0, new Discipline { Definition = next, Owner = owner, Level = -1 });
        }
        return list;
    }

    static bool Same(NddDiscipline a, NddDiscipline b) => string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
}
