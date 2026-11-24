// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Fix `shapetorch` (ours, off by default): the off hand's utility item out of the hand while a shape
// is on, still in its slot, and back in the hand, lit, when the shape ends.
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **Fix `shapetorch`** (ours, off by default). 1.1's shape change never touches the utility item
/// (the change does not read the utility item), so a torch held before Shape of the Beast is held by
/// the wolf, on its tail, and puts it in the utility stance, whose `util_punch` the wolf's set lacks:
/// the wolf swings nothing and bites nobody. With the fix the item **leaves
/// the hand but not its slot**: as the shape goes on it is let go and taken out of the world exactly
/// as <see cref="Utility.Set"/> with nothing does (the null branch), so nothing is drawn, no
/// flame burns, no light shines and the stance is gone — and it stays equipped, in its worn slot, so
/// a full pack is no obstacle (the first version of the fix put it in the pack, which a full one
/// refuses). The shape's record remembers it (<see cref="ShapeChange.Pocketed"/>), and the shape
/// ended, it goes back in the hand through <see cref="Utility.Set"/>.
///
/// **The flame.** The three shipped utility items with a script — `ItemUtilityTorch`,
/// `ItemUtilityFlashlight` and `ItemGangrelEye` — hang their flame on `worn` and at a scene start
/// when `ITEM_FLAG_BEINGWORN` is set, and take it away on `unworn` and at a scene end. So the item's
/// script is told `unworn` before it leaves the hand (its flame removed while it is still in the
/// world) and `worn` once it is back — one flame out, one in — and while it is out of the hand its
/// `BEINGWORN` bit is off, so a scene start (a journey, a load) does not hang a flame on a torch
/// that is out of the world, which would burn in mid-air where it was let go (the kind of lone
/// flame). Its slot, which is what makes it equipped, is untouched.
///
/// **A save.** The record writes only the shape's name and flags, so the item is not
/// named in it — but the item is saved in its worn slot with its `BEINGWORN` bit off, which nothing
/// else ever writes (the equip sets the bit, and nothing else puts a utility item in a
/// hand). The load puts it back in the hand from its slot (the load's re-equips), and the post-load
/// shape (<see cref="ShapeShift.Remake"/>) finds it there without its bit and takes it out of the
/// hand again, whether or not the fix is on in the run that loads — so the shape ended after a
/// load gives it back.
/// </summary>
public static class ShapeTorchFix
{
    static ILogger Log => Diag.For("Vitomir.Rpg");

    /// <summary>
    /// Before a shape goes on: the utility item out of the hand, if the fix is on — or, in the
    /// post-load shape (<paramref name="loading"/>), if it was out of the hand when the save was
    /// taken. Returns the item taken, or null.
    /// </summary>
    public static Item? LetGo(WorldThing holder, bool fix, bool loading)
    {
        if (holder.Actor is not { Utility: { } held }) return null;
        if (!fix && !(loading && WasPocketed(held)))
        {
            // Said in the log either way, so a play-test's log shows which it was.
            Log.LogInformation("shapetorch off: {Who} takes the shape with {Item} in the hand (1.1)", Watch.Name(holder), held.Name);
            return null;
        }
        Log.LogInformation("shapetorch: {Item} out of {Who}'s hand, still worn, while the shape is on", held.Name, Watch.Name(holder));
        if (held.Thing is { InWorld: true } body) holder.Place?.ItemScripts?.Unworn?.Invoke(body, holder);   // its answer is moot: it stays on
        Utility.Set(holder, null);
        held.Flags &= ~(ItemFlags)Equipment.EquippedBit;
        return held;
    }

    /// <summary>
    /// The shape ended: the item back in the hand and lit, if it is still in a worn slot of this
    /// character's (nothing can take it out while the shape is on — the take-off refuses — but a death
    /// may have dropped the lot).
    /// </summary>
    public static void GiveBack(WorldThing holder, Item? item)
    {
        if (item is null || holder.Actor is not { } actor) return;
        if ((item.Location & Slot.Worn) == 0 || !actor.Inventory.Items.Contains(item)) return;
        item.Flags |= (ItemFlags)Equipment.EquippedBit;
        Log.LogInformation("shapetorch: {Item} back in {Who}'s hand", item.Name, Watch.Name(holder));
        Utility.Set(holder, item);
        if (item.Thing is { } body) holder.Place?.ItemScripts?.Worn?.Invoke(body, holder);
    }

    /// <summary>A utility item in a worn slot without its `BEINGWORN` bit: only the fix leaves one so.</summary>
    public static bool WasPocketed(Item item) =>
        (item.Location & Slot.Worn) != 0 && ((int)item.Flags & Equipment.EquippedBit) == 0;
}
