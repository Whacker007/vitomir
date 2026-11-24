// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The teleport table in a save. 1.1 writes all sixteen slots, seven words each, into `!main.nsg` as
// tag 8 and reads them back with an assert that there are sixteen.
// Vitomir writes the ones in use, by name.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Save;

/// <summary>
/// One slot, as the save keeps it: its place in the table, the two ends by location name and thing
/// id, the creator by its key, the flags and the expiry. The ends themselves are ordinary made
/// things in their places' records — a portal is a `havenPortal` prop like any other — so a save
/// with a pair open brings both back, and the slot finds them again by id.
/// </summary>
public sealed class TeleportSave
{
    public int Slot { get; set; }
    public string? InLocation { get; set; }
    public string? InThing { get; set; }
    public string? OutLocation { get; set; }
    public string? OutThing { get; set; }
    public uint Flags { get; set; }
    public string Creator { get; set; } = "";
    public long ExpireMs { get; set; }
}

public static class TeleportSaves
{
    /// <summary>
    /// The slots in use. **A free slot is left out**, where 1.1 writes all sixteen with whatever a
    /// closed pair left in them — nothing reads a free slot's ends, so nothing is lost.
    /// </summary>
    public static List<TeleportSave> Take(Teleports table, Chronicle? chronicle)
    {
        var list = new List<TeleportSave>();
        for (int i = 0; i < table.Slots.Count; i++)
        {
            var s = table.Slots[i];
            if (s.Free) continue;
            list.Add(new TeleportSave
            {
                Slot = i, InLocation = chronicle?.NameOf(s.InLocation), InThing = s.InThing,
                OutLocation = chronicle?.NameOf(s.OutLocation), OutThing = s.OutThing,
                Flags = s.Flags, Creator = s.Creator!, ExpireMs = s.ExpireMs,
            });
        }
        return list;
    }

    /// <summary>A load's table: every other slot free, each kept one where it was (1.1 empties the table first).</summary>
    public static void Apply(Teleports table, IReadOnlyList<TeleportSave> saved, Chronicle? chronicle)
    {
        var slots = Enumerable.Range(0, Teleports.Max).Select(_ => new TeleportSlot()).ToArray();
        foreach (var t in saved)
        {
            if ((uint)t.Slot >= Teleports.Max || t.Creator.Length == 0) continue;
            slots[t.Slot] = new TeleportSlot
            {
                InLocation = t.InLocation is { } inAt && chronicle is not null ? chronicle.Find(inAt) : -1,
                InThing = t.InThing,
                OutLocation = t.OutLocation is { } outAt && chronicle is not null ? chronicle.Find(outAt) : -1,
                OutThing = t.OutThing,
                Flags = t.Flags, Creator = t.Creator, ExpireMs = t.ExpireMs,
            };
        }
        table.Restore(slots);
    }
}
