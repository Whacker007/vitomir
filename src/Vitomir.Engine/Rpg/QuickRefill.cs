// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The quick slot's refill after a use — the Controls screen's *auto-load quick slot from backpack*
// (`BackpackAutoLoad`, bit 0x20 of the options word) — the refill and its two callers,
// (a quick-slot use: the strip's click and the number keys, `Rpg.QuickSlots`) and the network's
// (the network handler for a player's use).
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **The refill.** When an item used from the quick-use belt is gone from it — used up and destroyed,
/// or taken into the hand — and the options word's bit 0x20 is set, the first item in the pack
/// (its belt flag `0x400`, in the character's own item order) whose
/// **template name** (compared byte for byte) is the used one's is moved into the same
/// belt cell (slot `0x200`, row 0), with the item's own `wear` foley (event
/// 0x14 on the item), and the pack is redrawn for the character being steered.
///
/// The callers take the refill only when the use answered **1**, which it does for three
/// things and no others: a consumable whose last charge destroyed it (`item_flags` 1), a **thrown**
/// weapon put in the hand (its attack's flag 0x20), and a scroll made ready (type
/// 11). A charge spent that leaves the item, spent or not, in its cell answers 0, and so does
/// an ordinary weapon.
/// </summary>
public static class QuickRefill
{
    /// <summary>What the refill needs to know before the use: the belt cell and the template name, or (−1, "") off the belt.</summary>
    public static (int Cell, string Template) Before(Item item) =>
        item.Location == Slot.QuickUse ? (item.Cell.X, item.Name) : (-1, "");

    /// <summary>Whether the use answered 1 — what the refill waits for.</summary>
    public static bool Emptied(Item item, UseResult result) => result switch
    {
        UseResult.UsedUp => item.Destroyed,
        UseResult.Held => item.Type == ItemType.Scroll
                          || item.Type == ItemType.Weapon && item.Weapon?.Current.Is(WeaponFlag.Thrown) == true,
        _ => false,
    };

    /// <summary>
    /// The same answer read off the item after the use, for a caller that has only the item: gone
    /// (destroyed at its last charge), or out of the belt as a scroll or a thrown weapon.
    /// </summary>
    public static bool Left(Item item) =>
        item.Destroyed
        || item.Location != Slot.QuickUse
           && (item.Type == ItemType.Scroll
               || item.Type == ItemType.Weapon && item.Weapon?.Current.Is(WeaponFlag.Thrown) == true);

    /// <summary>
    /// The refill itself: with <paramref name="option"/> on, the first pack item of <paramref name="template"/>
    /// goes into belt cell <paramref name="cell"/>. Answers it, or null when nothing moved.
    /// </summary>
    public static Item? From(Actor actor, int cell, string template, bool option)
    {
        if (!option || cell < 0 || template.Length == 0) return null;
        foreach (var item in actor.Inventory.Items)
        {
            if (item.Location != Slot.Inventory || item.Destroyed) continue;
            if (!string.Equals(item.Name, template, StringComparison.Ordinal)) continue;
            item.Location = Slot.QuickUse;
            item.Cell = (cell, 0);
            return item;
        }
        return null;
    }
}
