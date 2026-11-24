// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What becomes of an item whose last charge is spent — in the two spends
// (the use and the throw) and the item's removal, as play showed them:
// minor health potions turning up in odd cells, under other items, and back in the pack after a load.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **The last of a consumable is gone, not hidden.** When a spend takes an item that is
/// <see cref="ItemFlags.DestroyedAtZeroCharges"/> (<c>item_flags</c> bit 1) below one charge, 1.1 does four
/// things in a row (and the same block in the throw's spend):
///
/// <code>
///   RemoveFromWorld        — out of the world, if it is in one
///   the icon out of its grid
///   destroy the thing      — 0 lets its DESTROY_TEMPLATE fall; no consumable has one
///   delete                  — the destructor
/// </code>
///
/// and the destructor, for an item with an owner or a holder, calls
/// The release with no noise (<c>(0, 1)</c>): **out of the owner's inventory list**, or out of the hand. So
/// nothing is left anywhere — not in the pack, not on the belt, not in the save.
///
/// Until then Vitomir's player use (the pack's right click, the belt's click and keys, a network
/// player's use on the host) only marked the item <see cref="Item.Destroyed"/>: the pane and the strip
/// skip a destroyed item, so it looked gone, but it stayed in the character's list at its cell with
/// its amount at 0. The placement search skips destroyed items too, so the next pick-up could be put
/// on the same cell; the save wrote every listed item, and a load made the potion again, visible and
/// usable, **on top of or under** whatever had taken its cell since. Used again, it healed again and
/// went to −1, and so on (saves made then hold <c>potionminorhealth</c> at −1, −2, −4 and −5). Only the
/// AI's use (<c>Ai.Casting</c>) took the item out of the list. Now every spend that destroys goes
/// through <see cref="Gone"/>, and a save written before this is mended on load
/// (<see cref="Save.LoadedPack"/>).
/// </summary>
public static class UsedUp
{
    /// <summary>
    /// The destroy: marks <paramref name="item"/> <see cref="Item.Destroyed"/>, takes it out of its
    /// keeper's list or hand (the destructor's release — no noise; ours: <paramref name="user"/> when
    /// the item has no thing to name its owner) and destroys its thing (with 0, through
    /// <see cref="Location.Remove"/>). Safe to call twice.
    /// </summary>
    public static void Gone(WorldThing user, Item item)
    {
        item.Destroyed = true;
        var keeper = item.Thing?.Owner ?? item.Thing?.Holder ?? user;
        if (keeper.Actor is { } actor)
        {
            while (actor.Inventory.Remove(item)) { }
            if (ReferenceEquals(actor.Held, item)) Ground.ClearHand(keeper);
        }
        // The user may not be the keeper (a network player's use made on the host names both).
        if (!ReferenceEquals(keeper, user) && user.Actor is { } other)
        {
            while (other.Inventory.Remove(item)) { }
            if (ReferenceEquals(other.Held, item)) Ground.ClearHand(user);
        }
        if (item.Thing is { } thing)
        {
            thing.Owner = null;
            thing.Holder = null;
            (thing.Place ?? user.Place)?.Remove(thing, drop: true);
        }
    }
}
