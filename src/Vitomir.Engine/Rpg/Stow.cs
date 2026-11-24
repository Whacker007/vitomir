// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Putting an item away without being told where: the placement search, the repack, and the cascade
// that decides between them. All three are the original's.
//
// **This closes a deferred item and confirms a guess.** "The engine's own placement search" has
// been on the list for a long time, with ours marked as row-by-row from the top left and labelled a
// choice. The free-cell search *is* row-by-row from the top left — x fastest, then y, restarting the overlap
// walk after every step — so ours was right, and it is now a reading rather than a preference.
//
// **And it is what the base game does exactly**: an item goes straight into
// the character's inventory; if the inventory needs to be rearranged to make space for it then it will do that
// automatically. If there is no inventory space left it goes to the cursor buffer, so it is held.
// If there is no space there either, the transfer is refused. That is the stow's
// cascade for a character who is **not** the one you are steering, in that order. For the one you
// *are* steering the hand comes first and the repack second, because the stow asks
// `pActor == GetCurrentControl()` before it decides.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>What became of an item that was put away without a cell being named.</summary>
public enum StowResult
{
    /// <summary>It found a cell in a container.</summary>
    Stowed,
    /// <summary>Nothing would take it, so it went onto the cursor.</summary>
    ToHand,
    /// <summary>It was money: added to the purse and gone.</summary>
    Cash,
    /// <summary>
    /// A quest item: **destroyed**, never carried. The item is marked
    /// <see cref="Item.Destroyed"/> here; the caller, who has the place, takes its thing away.
    /// </summary>
    Quest,
    /// <summary>No room anywhere and the hand was full. The cursor flashes.</summary>
    Refused,
}

/// <summary>The engine's own "put this somewhere" — the free-cell search, the repack and the stow.</summary>
public static class Stow
{
    /// <summary>
    /// How close a character has to be to take something off the floor or to put something on it:
    /// **50 units**. It is handed to the approach in the take order and
    /// used in the drop order — the same number twice, from opposite directions.
    /// </summary>
    public const float Reach = 50f;

    /// <summary>
    /// Under **300 units** an order walks rather than runs (tested in the drop, the use
    /// and their siblings). Over it, the run speed is used unless a modifier says not to.
    /// </summary>
    public const float WalkWithin = 300f;

    /// <summary>
    /// The repack only re-places icons up to **3 x 3 cells**, because its two loops count down from
    /// three. An item bigger than that in either direction is emptied out of the pack and never put
    /// back — it ends up owned but in no container. No shipped icon is that big, so the case does
    /// not arise in the base game; a mod's would.
    /// </summary>
    public const int RepackCells = 3;

    /// <summary>
    /// **The placement search**. Scans for the first cell whose footprint overlaps
    /// nothing, **x fastest and then y, from (0, 0)** — the engine restarts its walk of the other
    /// items after every step, which is the same answer as a taken-cell map and is what this does.
    /// Returns false when the container will not take it at all or has no room.
    /// </summary>
    public static bool Place(Actor actor, Item item, int container)
    {
        if (FreeCell(actor, item, container) is not { } cell) return false;
        item.Location = container;
        item.Cell = cell;
        return true;
    }

    /// <summary>
    /// <see cref="Place"/>'s search without the move: the first free cell of <paramref name="container"/>
    /// for <paramref name="item"/>, or null when the container will not take it or has no room. What a
    /// caller asks before it takes something off that has to go somewhere (ours: 1.1 acts on
    /// the free-cell search's answer itself).
    /// </summary>
    public static (int X, int Y)? FreeCell(Actor actor, Item item, int container)
    {
        var (width, height) = Slot.Size(container);
        if (width == 0) return null;
        // The search's own first test: the icon's grid mask has to include this container. The icon's
        // maker puts every item's mask in the pack and the vault (0x4400) but on the
        // quick-use belt (0x200) **only when the icon is one cell** — so the 5 x 1 belt refuses the
        // one shipped 2 x 1 item, `launch_grenades`, although it would fit, and a pick-up sends it
        // to the pack (the pane already knew, `InventoryPane.Fits`; a comparison with 1.1).
        if ((item.Slots & container) == 0 && (container & Slot.Containers) == 0) return null;
        if (container == Slot.QuickUse && item.IconCells != (1, 1)) return null;
        var (across, down) = item.IconCells;
        if (across > width || down > height) return null;

        var taken = new bool[width, height];
        foreach (var other in actor.Inventory.Items)
        {
            if (ReferenceEquals(other, item) || other.Location != container || other.Destroyed) continue;
            var (oa, od) = other.IconCells;
            for (int x = other.Cell.X; x < other.Cell.X + oa && x < width; x++)
                for (int y = other.Cell.Y; y < other.Cell.Y + od && y < height; y++)
                    if (x >= 0 && y >= 0) taken[x, y] = true;
        }

        for (int y = 0; y + down <= height; y++)
            for (int x = 0; x + across <= width; x++)
            {
                bool free = true;
                for (int dx = 0; dx < across && free; dx++)
                    for (int dy = 0; dy < down && free; dy++)
                        if (taken[x + dx, y + dy]) free = false;
                if (free) return (x, y);
            }
        return null;
    }

    /// <summary>
    /// **The repack**. Empties the **pack only** — worn items and the quick-use belt are
    /// left where they are — and then re-places everything largest first: the two loops count
    /// height 3 down to 1 and, inside each, width 3 down to 1. A 1 x 1 item that will not fit the
    /// pack is offered the belt; nothing else is.
    /// </summary>
    public static void Repack(Actor actor)
    {
        var loose = new List<Item>();
        foreach (var item in actor.Inventory.Items)
        {
            if (item.Location != Slot.Inventory || item.Destroyed) continue;
            item.Location = Slot.None;
            item.Cell = (-1, -1);
            loose.Add(item);
        }

        for (int h = Rules.GameRules.Current.Items.RepackMaxCells; h >= 1; h--)
            for (int w = Rules.GameRules.Current.Items.RepackMaxCells; w >= 1; w--)
                foreach (var item in loose)
                {
                    if (item.Location != Slot.None) continue;
                    var (across, down) = item.IconCells;
                    if (across != w || down != h) continue;
                    if (Place(actor, item, Slot.Inventory)) continue;
                    // Only the single cell gets the belt as a second chance.
                    if (w == 1 && h == 1) Place(actor, item, Slot.QuickUse);
                }
    }

    /// <summary>
    /// **The cascade**: where an item goes when nobody named a cell — a pick-up off the
    /// floor, a script's grant, an item handed to a companion.
    ///
    /// <list type="number">
    /// <item>**Money** goes straight into the purse and the item is gone.</item>
    /// <item>A **quest item is destroyed** (type 7 is destroyed, and the cascade
    /// answers 1, taken). It never enters the inventory at all: its `pickup` script has already had
    /// its say (MON3_Main's skull key and Garinol's journal set their chronicle flags
    /// there), and what the player sees of it afterwards is the quest pane, whose quest shows the
    /// object's own picture once the script completes it (`Ui.QuestPane`). The
    /// early one is the goblet of Ekaterina's blood: that item never
    /// goes into the inventory and just shows up as an image in the quest pane, and so it is here.
    /// Earlier, Vitomir kept it in the inventory with no cell.</item>
    /// <item>Otherwise the two containers are tried, and **the order depends on the item**: one
    /// that can be worn tries the pack first and the belt second, and one that cannot tries the
    /// belt first — and **a weapon whose current attack is THROWN counts as one that cannot**
    /// the attack record's <c>0x20</c>, the SDK's QUICKUSE), so holy water,
    /// greek fire, shuriken, throwing knives and the grenades go to the belt first. (Read in the
    /// A comparison with 1.1; Vitomir put them in the pack first.)</item>
    /// <item>If neither takes it, **who the character is decides what happens next.** For the one
    /// you are steering, an empty hand gets it immediately and the repack is the fallback; for
    /// anybody else the repack comes first and the hand after it.</item>
    /// <item>No room after a repack and a full hand is a **refusal**, which is what makes the
    /// cursor flash — and the character says `vox_inventoryfull` when free to speak
    /// (a comparison with 1.1).</item>
    /// </list>
    /// </summary>
    public static StowResult Put(WorldThing thing, Item item, bool steering)
    {
        if (thing.Actor is not { } actor) return StowResult.Refused;
        if (item.Flags.HasFlag(ItemFlags.InstantCash)) return StowResult.Cash;
        if (item.Type == ItemType.Quest)
        {
            item.Destroyed = true;
            return StowResult.Quest;
        }

        bool wearable = (item.Slots & Slot.Worn) != 0
                        && item.Weapon?.Current.Is(WeaponFlag.Thrown) != true;
        int first = wearable ? Slot.Inventory : Slot.QuickUse;
        int second = wearable ? Slot.QuickUse : Slot.Inventory;

        bool repacked = false;
        while (true)
        {
            // Linked once (the link runs inside the search, on success). Until then this added it
            // again on the second pass after a repack, so a stowed item was listed twice and a later
            // lift or drop, which unlinks once, left a copy behind.
            if (!actor.Inventory.Items.Contains(item)) actor.Inventory.Add(item);
            if (Place(actor, item, first) || Place(actor, item, second))
            {
                // **The link writes the owner** (the item's owner is the actor, asserting it was empty,
                // which the release leaves it). Until then the pick-up wrote it after this and nothing
                // else did, so an item handed over on a portrait was in the new
                // character's pack with no owner — and a shop will buy only what the seller owns
                // (`Shops.Sell`): in play, everything Wilhem gave
                // Christof was refused at Jiri's and Unorna's until it had been picked up and put down.
                if (item.Thing is { } linked) linked.Owner = thing;
                return StowResult.Stowed;
            }

            if (repacked || steering)
            {
                // **Through `Ground.Hand`, not by writing `Held` here.** The hand is outside the
                // inventory list — the hold removes the item from its owner before it writes the
                // link — and this was the one place left that put an item on the cursor and in the
                // pack at once.
                if (actor.Held is null && Ground.Hand(thing, item)) return StowResult.ToHand;
                if (repacked)
                {
                    if (Audio.Noise.CanSpeak(thing)) Audio.Noise.Foley(thing, Audio.FoleyEvent.VoxInventoryFull);
                    return StowResult.Refused;
                }
            }
            Repack(actor);
            repacked = true;
        }
    }
}

/// <summary>
/// The two orders that move an item between a character and the floor — the take
/// and the drop. Both are **approach orders**: they set an action mode, walk, and do
/// the thing on arrival, which is why the base game marches you over to where you clicked.
///
/// Only their *gates* and their *arithmetic* are here; the orders themselves are
/// <see cref="Actions.Fetch"/> and <see cref="Actions.Put"/>, and the transfer they arrive at is
/// <see cref="Ground"/>. An item on the floor is an ordinary world thing.
/// </summary>
public static class GroundOrders
{
    /// <summary>
    /// The take's gate. An item may be taken only when **nobody owns or holds it** and it is in the
    /// same world, and the taker must not be ethereal, busy or down.
    /// </summary>
    public static OrderResult MayTake(WorldThing taker, Item item, bool ownedOrHeld, int nowMs)
    {
        if (ownedOrHeld) return OrderResult.Refused;
        if (taker.Actor is not { } actor) return OrderResult.Refused;
        if (actor.Has(Actor.Flag.Ethereal)) return OrderResult.Refused;
        if (Down(actor, nowMs)) return OrderResult.Busy;
        return OrderResult.Started;
    }

    /// <summary>
    /// The drop's gate. You may only put down something **you** own or hold, and the same
    /// busy/down test applies. The engine checks the owner and falls back to
    /// the holder.
    /// </summary>
    public static OrderResult MayDrop(WorldThing dropper, Item item, bool mine, int nowMs)
    {
        if (!mine) return OrderResult.Refused;
        if (dropper.Actor is not { } actor) return OrderResult.Refused;
        if (actor.Has(Actor.Flag.Ethereal)) return OrderResult.Refused;
        if (Down(actor, nowMs)) return OrderResult.Busy;
        return OrderResult.Started;
    }

    /// <summary>
    /// Whether the drop happens **where you stand** rather than where you clicked: with the
    /// modifier bit `0x8` held, the drop skips the walk and puts the item down at the clicked
    /// point if it is within <see cref="Stow.Reach"/>, or at the point
    /// <see cref="Stow.Reach"/> along that direction if it is further. <paramref name="distance"/>
    /// is from the character to the click.
    /// </summary>
    public static bool Immediate(InputModifier mods) => mods.HasFlag(InputModifier.Space);

    /// <summary>
    /// **Where an immediate drop actually lands — and it is not "clamped to the reach", which is
    /// what this page said for two batches on the strength of a reading that looked like a clamp.**
    ///
    /// The drop measures the vector from the character to the clicked point. Within
    /// <see cref="Stow.Reach"/> the item is dropped at the point exactly, with no further test.
    /// Beyond it, the original computes
    ///
    /// <code>delta * 50 + origin</code>
    ///
    /// — the **same constant** (`_DAT_004eb724`, 50.0) serving as both the threshold it was just
    /// compared against and the multiplier, with **no normalise anywhere**: three
    /// multiplications by 50 and no division. A click 100 units away throws the item 5,000
    /// units, and where that lands on walkable ground the item is gone for good.
    ///
    /// **This is a repair, not a Fix** (<see cref="World.Repairs.DropLanding"/>): the one division
    /// the original is missing is always applied and there is no switch to turn it off, because
    /// losing the player's property is not a behaviour worth being able to recognise.
    /// </summary>
    /// <param name="from">The character's own position.</param>
    /// <param name="at">Where the player clicked.</param>
    public static System.Numerics.Vector3 Landing(System.Numerics.Vector3 from,
                                                  System.Numerics.Vector3 at)
    {
        var delta = at - from;
        float far = delta.Length();
        if (far <= Rules.GameRules.Current.Movement.TakePutReach || far <= 0f) return at;
        return from + delta / far * Rules.GameRules.Current.Movement.TakePutReach;
    }

    /// <summary>
    /// Whether the approach walks rather than runs. The drop tests `(mods &amp; 0x11) == 0` and
    /// then either `mods &amp; 0x20` or the distance against <see cref="Stow.WalkWithin"/>. Only
    /// four modifier bits are ever built (the click packs them from the four keys), so `0x11` is
    /// **shift** and `0x20` can never be set: in play it walks unless you hold shift or the point
    /// is more than 300 units away.
    /// </summary>
    public static bool Walks(InputModifier mods, float distance) =>
        !mods.HasFlag(InputModifier.Shift) && distance < Rules.GameRules.Current.Movement.RunBeyond;

    /// <summary>
    /// Whether the take's and the use's approach walks (and the use's): **the distance
    /// alone** — within <see cref="Stow.WalkWithin"/> (<c>&lt;=</c>) it
    /// walks, beyond it runs, and **shift is not asked**. Read in a comparison with 1.1 and built;
    /// Vitomir let shift make a take run, as the put-down's own rule (<see cref="Walks"/>) does.
    /// </summary>
    public static bool ApproachWalks(float distance) => distance <= Rules.GameRules.Current.Movement.RunBeyond;

    /// <summary>
    /// Whether a pick-up goes **to the cursor** rather than into the pack. The take takes its
    /// straight-to-hand branch for the character you are steering when either of two things is
    /// true — **whatever is in the hand**: a full hand is asked about only inside the branch, and
    /// there it ends the take with nothing done (a comparison with 1.1's gap, built —
    /// Vitomir made an empty hand part of this test and sent the item to the pack) — and **the
    /// first of them was written down vaguely here as "an option bit" until play showed the
    /// rule from play**:
    ///
    /// <list type="bullet">
    /// <item>**the inventory pane is on screen** — its shown bit and the test that it is up,
    /// the shown flag and then the test that the menu is one of the panes actually up. In play,
    /// without the inventory window open, an item clicked on the ground to pick
    /// up goes to the inventory by itself if there is space — which is this condition
    /// read from the other side;</item>
    /// <item>or **alt** is held (bit 2 of the click's mask).</item>
    /// </list>
    ///
    /// Cash and quest items never take this branch. Neither does a companion: it is the selected
    /// character's cursor or nothing (the party's `+4`, which is the steered one except while a
    /// possession lasts — a possessed body picks up into its own pack).
    /// </summary>
    public static bool ToCursor(InputModifier mods, Item item, bool steering, bool paneOpen = false) =>
        steering && (paneOpen || mods.HasFlag(InputModifier.Alt))
        && !item.Flags.HasFlag(ItemFlags.InstantCash) && item.Type != ItemType.Quest;

    /// <summary>The busy/dead/incapacitated test every one of these orders opens with.</summary>
    static bool Down(Actor actor, int nowMs) =>
        actor.BusyUntil > nowMs || actor.Dead || actor.Has2(Actor.Flag2.Restrained);
}
