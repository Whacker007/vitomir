// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Tab in a network game: the key hook's Tab, whose single-player arm steers the party's
// next member and whose seat arm walks the players. Vitomir's Tab did the single player's in a network game.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>
/// **Tab**: the interface's click (sound 12), then the Tab arms, then the one-in-five `vox_newplayer`
/// of whoever is steered (<see cref="Location.SteerTab"/>). It has two arms and a network game reaches
/// only the first:
/// <list type="bullet">
/// <item>**in the Storyteller's seat** the target moves on to **the next player's character**
/// (<see cref="Next"/>) — made the target directly, so the camera goes to it and the ghost leaves
/// the world when it is neither the control nor the new target — and returns;</item>
/// <item>the party's arm — a steered possession ended, else the next member  — only
/// **outside a network game**. So a network game's Tab outside the seat steers nobody
/// else and ends no possession: the click, and the one steered may still say `vox_newplayer`.</item>
/// </list>
/// </summary>
public static class StoryTab
{
    /// <summary>The slot table's length (0x50 bytes a slot).</summary>
    public const int Slots = 8;

    /// <summary>
    /// The seat's next target (the seat arm): when the target is a player's character (type 7) found in
    /// the slot table (<paramref name="slots"/>, the characters by slot), the search starts at
    /// the slot after it, else at slot 0; the first slot from there to the last whose character is **in a place**
    /// (not out of the world) is the answer; with none, **slot 0's character** whatever it is (the
    /// search does not wrap). Null when slot 0 has none.
    /// </summary>
    public static WorldThing? Next(IReadOnlyList<WorldThing?> slots, WorldThing? target)
    {
        int from = 0;
        if (target is { Type: ThingType.Player })
            for (int i = 0; i < Math.Min(slots.Count, Slots); i++)
                if (ReferenceEquals(slots[i], target)) { from = i + 1; break; }
        for (int i = from; i < Math.Min(slots.Count, Slots); i++)
            if (slots[i] is { InWorld: true, Place: not null } next) return next;
        return slots.Count > 0 ? slots[0] : null;
    }
}
