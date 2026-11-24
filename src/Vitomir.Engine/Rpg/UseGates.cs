// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The three gates of using an item that a comparison with 1.1 found Vitomir lacked (the item's use,
// gap 3), built.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// What decides whether an item that is worn, a weapon or a scroll is used at all.
///
/// <b>A wearable item is never used unless it is a thrown weapon</b>. The test
/// is on the item's icon, whose grid mask is the set of grids that take it,
/// written from the item's type when the icon is made (a weapon the right weapon slot, a
/// shield or a utility the left, headgear the head, and so on) — <see cref="Item.SlotsFor"/>, the
/// same table. Any of the nine paperdoll bits (<c>0x1ff</c>) and the item is refused, wherever it
/// sits, unless it is a weapon thing (type 5) whose current attack is THROWN (attack record bit
/// <c>0x20</c>). So a ring, an amulet, a helmet, a shield, a torch and a sword right-clicked in the
/// pack do nothing, silently — a comparison with 1.1's first reading had it as "an item in a worn slot"; the
/// icon's maker says it is the kind of item.
///
/// Then, for the two kinds that go to the hand rather than being consumed:
/// <list type="bullet">
/// <item><b>a weapon</b> (type 1) only when its attack is THROWN <i>and</i> nothing is in the hand
/// — and anything else answers 0 with no sound;</item>
/// <item><b>a scroll</b> (type 11) only when it names a discipline and nothing is
/// in the hand.</item>
/// </list>
/// Either way its `use` noise (on the owner) and then it goes into the hand
/// (the hold — out of the world, out of the owner's keeping, taken off if worn, and onto the cursor),
/// and the use answers 1 — which is what lets the quick belt refill behind it.
/// </summary>
public static class UseGates
{
    /// <summary>The nine paperdoll bits the icon's grid mask is tested against (<c>&amp; 0x1ff</c>).</summary>
    public const int PaperdollMask = 0x1ff;

    /// <summary>Whether the weapon's current attack is thrown (the selected attack's record, bit 0x20).</summary>
    public static bool Thrown(Item item) => item.Weapon?.Current.Is(WeaponFlag.Thrown) == true;

    /// <summary>
    /// A wearable item — any type the paperdoll takes (<see cref="Item.SlotsFor"/>) — is not
    /// used unless it is a thrown weapon. Answers true when the use is refused here.
    /// </summary>
    public static bool Wearable(Item item) => (item.Slots & PaperdollMask) != 0 && !Thrown(item);

    /// <summary>
    /// The weapon and scroll arms: the item goes into <paramref name="user"/>'s
    /// hand when it may, with its `use` noise first. Answers whether it did; false is 1.1's silent 0.
    /// </summary>
    public static bool ToHand(WorldThing user, Item item)
    {
        if (user.Actor is not { } me) return false;
        bool may = item.Type switch
        {
            ItemType.Weapon => Thrown(item),
            ItemType.Scroll => item.ScrollDiscipline.Length > 0,
            _ => false,
        };
        if (!may || me.Held is not null) return false;
        Items.Heard(user, item, Audio.FoleyEvent.Use);
        // The hold → the release — out of its owner's keeping, taken off
        // first when it is worn (for an item with the equipped bit), and no `remove` noise.
        if ((item.Location & Slot.Worn) != 0) Equipment.Unequip(user, item);
        return Ground.Hand(user, item);
    }
}
