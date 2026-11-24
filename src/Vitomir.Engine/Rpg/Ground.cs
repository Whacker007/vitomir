// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// An item on the floor, and the two ways it gets there and back.
//
// **This is the join the whole ground half was waiting on, and the surprise is that there is
// nothing to join.** It was once written down as "the conversion between an `Rpg.Item` record
// and a `WorldThing`", which is the wrong shape: in the original there is no conversion because
// there is no pair. `rpgItem` **is** a `simThing` of type ITEM, WEAPON or ARMOR, and a save says
// the same thing from the other side — every carried item in `!main.nsg` is a thing record with
// its own GUID and the item block appended. What separates a sword on the floor from a sword in a
// pack is one pointer: the world it is in. Picking something up unlinks it from the world and
// links it into an inventory; the object never changes and its GUID never changes, which is why
// a save experiment found a picked-up dagger carrying the GUID it was given when the
// barrel broke.
//
// The four functions:
//
//   * **Hold** — put an item in a hand. Unlink from the world, unlink from whoever owned it,
//     then the actor's hand is the item and the item's holder the actor. Refuses a full hand.
//   * **Unhold** — take it out again, clearing both ends, but only if this actor is really the
//     holder.
//   * **Take** — the pick-up, which chooses between the hand and the pack by a rule rather
//     than a preference, and ends by cancelling everybody else's walk to it.
//   * **Remove** (the script native) — the drop: unlink from the inventory, copy the **dropper's
//     whole transform** onto the item, find the sector that point is in, and link it into the
//     world. No offset, no fall: a sword dropped on a balcony stays at balcony height, because no
//     item template in the game sets `MOVETYPE` and the physics step never touches one.
//
// And two callbacks, because an item can have a script: **`pickup (III)Z`** (slot 1)
// and **`dropped (IFFFI)Z`** (slot 2). Both return a boolean and
// **false refuses the whole gesture**; both are asked of the item's own script first and then of
// every capture hanging off it.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>What became of a pick-up or a put-down.</summary>
public enum GroundResult
{
    /// <summary>It would not go: the gate refused, or a script said no.</summary>
    Refused,
    /// <summary>It went into a container on the taker.</summary>
    Stowed,
    /// <summary>It went onto the taker's cursor.</summary>
    ToHand,
    /// <summary>It was money, and went into the purse.</summary>
    Cash,
    /// <summary>It is on the floor.</summary>
    Dropped,
    /// <summary>A quest item, taken and destroyed: nobody carries it.</summary>
    Destroyed,
    /// <summary>
    /// Nothing happened, and the take is over all the same: the cursor's branch found the hand
    /// full. The pick-up noise has played and the item is still on the floor.
    /// </summary>
    Left,
}

/// <summary>Moving an item between an inventory and the world.</summary>
public static class Ground
{
    /// <summary>
    /// **Is anybody's script allowed to veto this?** The hook the caller supplies is asked the two
    /// questions the engine asks its Codex objects — `pickup` and `dropped` — and a false answer
    /// refuses the gesture outright. A caller with no script layer leaves it null and everything
    /// is allowed, which is what the engine does for an item with no script attached.
    /// </summary>
    /// <param name="Pickup">`pickup(thingGuid, pickerGuid, captureId)`.</param>
    /// <param name="Dropped">`dropped(thingGuid, x, y, z, captureId)`.</param>
    /// <param name="CanUse">
    /// `canuse(itemGuid, userGuid, captureId)`, the first word of whether a
    /// character may use or wear an item at all (<see cref="Equipment.CanUse"/>).
    /// </param>
    /// <param name="Worn">`worn(itemGuid, wearerGuid, captureId)`, once it is on.</param>
    /// <param name="Unworn">
    /// `unworn(itemGuid, wearerGuid, captureId)`, before it comes off —
    /// false keeps it on.
    /// </param>
    /// <param name="Used">
    /// `used(itemGuid, ownerGuid, captureId)`, before a potion, a tome or a
    /// book is used — false refuses the use (<see cref="Items.Use"/>).
    /// </param>
    /// <param name="Hit">
    /// `hit(wielderGuid, victimGuid, damage, captureId)`: a melee blow
    /// landed, with the weapon it was struck with and the damage it did.
    /// </param>
    /// <param name="Damaged">
    /// `damaged(thingGuid, attackerGuid, damage, damageType, captureId) → float`:
    /// a **prop** caught in an explosion — the thing, the
    /// explosion's owner, the damage and its type; each script's answer is the damage the next is
    /// told. What comes back is the damage after them all (the explosion throws it away). And a
    /// **character** taking any blow it does not refuse
    /// (<see cref="DamagedMessage"/>), where what comes back is the blow.
    /// </param>
    /// <param name="ActorCanUse">
    /// `actorcanuse(actorGuid, itemGuid, captureId)`: the **character's**
    /// scripts asked, after the item's `canuse` said yes — false refuses the use. No
    /// shipped class declares it; a mod's may.
    /// </param>
    /// <param name="ActorWorn">
    /// `actorworn(actorGuid, itemGuid, captureId)`: the wearer's scripts
    /// told, after the item's `worn`.
    /// </param>
    /// <param name="ActorUnworn">
    /// `actorunworn(actorGuid, itemGuid, captureId)`: the wearer's scripts
    /// asked, after the item's `unworn` said yes — false keeps it on.
    /// </param>
    /// <param name="BeginSwing">
    /// `beginswing(actorGuid, targetGuid, swingMs, captureId)`: a swing at
    /// a thing begins, with how long its motion runs.
    /// </param>
    /// <param name="KeyTag">
    /// `keytag(thingGuid, n, captureId)`: a thing's animation passed its
    /// `Codex1`..`Codex3` tag, `n` 0..2 (<see cref="Anim.CodexTags"/>).
    /// </param>
    /// <param name="Soaked">
    /// `soaked(thingGuid, attackerGuid, damage, damageType, soak, captureId) → float`:
    /// a character's soak roll came out other than 0 — the
    /// character, the attacker, the blow, its type and the soak; each script's answer is the soak the
    /// next is told, and what comes back is taken off the blow.
    /// </param>
    /// <remarks>
    /// The five before <c>Soaked</c> are the callbacks only a mod answers — each is on this record beside the item
    /// callbacks because this is the seam the engine half already asks the scripts through.
    /// </remarks>
    public sealed record Scripts(Func<WorldThing, WorldThing, bool>? Pickup = null,
                                 Func<WorldThing, Vector3, bool>? Dropped = null,
                                 Func<WorldThing, WorldThing, bool>? CanUse = null,
                                 Action<WorldThing, WorldThing>? Worn = null,
                                 Func<WorldThing, WorldThing, bool>? Unworn = null,
                                 Func<WorldThing, WorldThing, bool>? Used = null,
                                 Action<WorldThing, Weapon, WorldThing, float>? Hit = null,
                                 Func<WorldThing, WorldThing?, float, int, float>? Damaged = null,
                                 Func<WorldThing, WorldThing, bool>? ActorCanUse = null,
                                 Action<WorldThing, WorldThing>? ActorWorn = null,
                                 Func<WorldThing, WorldThing, bool>? ActorUnworn = null,
                                 Action<WorldThing, WorldThing, int>? BeginSwing = null,
                                 Action<WorldThing, int>? KeyTag = null,
                                 Func<WorldThing, WorldThing?, float, int, float, float>? Soaked = null);

    /// <summary>
    /// The hold — the item goes onto the cursor. **Refuses a full hand**, which
    /// is the first thing it tests, and then does exactly three things: takes the item out of the
    /// world if it is in one, takes it out of its owner's inventory if it has one, and writes both
    /// ends of the hand link.
    /// </summary>
    public static bool Hand(WorldThing actorThing, Item item)
    {
        if (actorThing.Actor is not { } actor) return false;
        if (actor.Held is not null) return false;                       // an empty hand only
        Detach(item, actor);
        actor.Held = item;
        if (item.Thing is { } itemThing) itemThing.Holder = actorThing;
        item.Location = Slot.None;
        return true;
    }

    /// <summary>
    /// The other way: **out of the hand and into somebody's keeping**, at a named slot and cell.
    ///
    /// It exists because the hand being *outside* the inventory list is an invariant with two
    /// ends, and only one of them was written. Play found the other: alt-click a letter off the
    /// floor, drop it in the quick-use belt, and it vanished — the take had taken it out of the
    /// list (the hold's own removal) and the pane's put-down never put it back, so the strip
    /// drew an inventory the letter was no longer in. Every path that empties the hand goes
    /// through here now.
    /// </summary>
    public static void Keep(WorldThing actorThing, Item item, int slot, (int X, int Y) cell)
    {
        if (actorThing.Actor is not { } actor) return;
        if (ReferenceEquals(actor.Held, item)) ClearHand(actorThing);
        if (!actor.Inventory.Items.Contains(item)) actor.Inventory.Add(item);
        if (item.Thing is { } itemThing) { itemThing.Owner = actorThing; itemThing.Holder = null; }
        item.Location = slot;
        item.Cell = cell;
    }

    /// <summary>
    /// The unhold — the hand is emptied. It clears the item's holder **only when
    /// this actor is really the holder**, which is the engine guarding against two characters
    /// disagreeing about who is carrying what, and clears the hand either way.
    /// </summary>
    public static void ClearHand(WorldThing actorThing)
    {
        if (actorThing.Actor is not { } actor) return;
        if (actor.Held?.Thing is { } held && ReferenceEquals(held.Holder, actorThing))
            held.Holder = null;
        actor.Held = null;
    }

    /// <summary>
    /// The take — take something off the floor.
    ///
    /// The **order of the two destinations is the rule**, and it is not a preference. The
    /// straight-to-the-hand branch needs all of: not instant cash, not a quest item, the character
    /// the player is steering, and either the inventory pane on screen or <b>alt</b> held — and
    /// **then** it asks whether the hand is empty, and a full one does nothing at all. Everything else goes to <see cref="Stow.Put"/>, the
    /// cascade — and if the cascade finds nowhere at all, the hand is the fallback, so the two
    /// destinations are tried in **either order** depending on how you asked.
    ///
    /// It ends, in both branches, with <see cref="Location.Untarget"/>: taking a thing off the
    /// floor stops everybody who was walking over to it.
    /// </summary>
    /// <param name="steering">
    /// Whether <paramref name="takerThing"/> is the character being steered —
    /// which is what the cascade asks (the stow, through the steered test).
    /// </param>
    /// <param name="selected">
    /// Whether it is the party's selected member — which is what the
    /// straight-to-hand branch asks, and which differs from <paramref name="steering"/>
    /// only while a possession lasts: the body is steered, the caster is still selected. Null
    /// takes <paramref name="steering"/>'s answer.
    /// </param>
    /// <param name="paneOpen">Whether the inventory pane is on screen — see <see cref="GroundOrders.ToCursor"/>.</param>
    public static GroundResult Take(Location place, WorldThing takerThing, WorldThing itemThing,
                                    InputModifier mods, bool steering, int nowMs,
                                    Scripts? scripts = null, bool paneOpen = false, bool? selected = null)
    {
        if (takerThing.Actor is not { } taker || itemThing.Item is not { } item) return GroundResult.Refused;

        // The script gets the first word, and a false answer ends it.
        if (scripts?.Pickup is { } ask && !ask(itemThing, takerThing)) return GroundResult.Refused;

        // **The taker is not asked again here** (the take asks nothing of it): whether it is free was
        // the order's question, and the tick asks only that the item is still loose.
        // A taker flinched on the way still picks the thing up on arrival. (The
        // A comparison with 1.1; Vitomir asked the order's whole gate again, and a busy taker gave up at the item.)
        if (itemThing.Owner is not null || itemThing.Holder is not null) return GroundResult.Refused;

        // `pickup`, on the item while it is still on the floor (both branches).
        Audio.Noise.Foley(itemThing, Audio.FoleyEvent.Pickup);

        // **A full hand does not send it to the pack**: the cursor's branch is chosen
        // without asking about the hand, and with the hand full it does nothing — the noise has
        // played, the take is over, the item stays on the floor. (The a comparison with 1.1's gap; Vitomir made
        // an empty hand part of the choice and put the item in the pack.)
        var result = GroundOrders.ToCursor(mods, item, selected ?? steering, paneOpen)
            ? (Hand(takerThing, item) ? GroundResult.ToHand : GroundResult.Left)
            : Cascade(place, takerThing, itemThing, item, steering);

        // Reached from both branches: whoever else was on their way here is not any more.
        if (result != GroundResult.Refused) place.Untarget(itemThing);
        return result;
    }

    /// <summary>
    /// **The take on its own** — what `CodexActor.AddToActorInventory` calls: the item is
    /// given to the character straight away, with none of the take order's gate (the take order's owner,
    /// ethereal and busy tests) and no walk. The item's script still gets the first word
    /// (`pickup`), the pick-up noise is made, and the same two branches decide between the cursor
    /// and the pack; nothing is holding a modifier key, so only the inventory pane being up sends
    /// it to the cursor. It ends, as the take does, with <see cref="Location.Untarget"/>.
    /// </summary>
    public static GroundResult Add(Location place, WorldThing takerThing, WorldThing itemThing,
                                   bool steering, bool selected, Scripts? scripts = null, bool paneOpen = false)
    {
        if (takerThing.Actor is not { } taker || itemThing.Item is not { } item) return GroundResult.Refused;
        if (scripts?.Pickup is { } ask && !ask(itemThing, takerThing)) return GroundResult.Refused;
        Audio.Noise.Foley(itemThing, Audio.FoleyEvent.Pickup);
        var result = GroundOrders.ToCursor(InputModifier.None, item, selected, paneOpen)
            ? (Hand(takerThing, item) ? GroundResult.ToHand : GroundResult.Left)
            : Cascade(place, takerThing, itemThing, item, steering);
        if (result != GroundResult.Refused) place.Untarget(itemThing);
        return result;
    }

    /// <summary>
    /// The native itself — `CodexItem.RemoveItemFromInventory`. The
    /// item must be owned or held by somebody; it is taken
    /// out of their keeping (<see cref="Release"/>), which may refuse; then it is put
    /// in the world at **its keeper's whole transform** — position and facing, the sector found
    /// from the point — and the answer is **the keeper's GUID**, not the item's. No gate, no
    /// script's `dropped`, no drop noise: that is the drop order's, not this. It does
    /// make the item's `remove` (the native passes its sound flag).
    /// </summary>
    /// <returns>The keeper it came from, or null when there was none or the release refused.</returns>
    public static WorldThing? Remove(Location place, WorldThing itemThing, Scripts? scripts = null)
    {
        if (itemThing.Item is not { } item) return null;
        var keeper = itemThing.Owner ?? itemThing.Holder;
        if (keeper?.Actor is null) return null;
        if (!Release(itemThing, item, keeper, scripts)) return null;
        item.Location = Slot.None;
        item.Cell = (0, 0);
        itemThing.Position = keeper.Position;
        itemThing.Orientation = keeper.Orientation;
        itemThing.Sector = place.At(keeper.Position, keeper.Sector);
        place.Enter(itemThing);
        return keeper;
    }

    /// <summary>
    /// The release — out of a character's keeping. A weapon that is in the hand
    /// under a swap, or the one the swap put aside, ends the swap first. An item on the
    /// cursor simply leaves the hand. Otherwise: nothing leaves a character in a changed shape;
    /// a worn item must come off first (the take-off, whose `unworn` may keep it
    /// on); then it is unlinked from the pack. The noise it makes when asked (event <c>0x15</c>,
    /// `remove`) is made here when <paramref name="noise"/> asks — the native (it passes 1) and
    /// an icon's lift (it passes 1, `InventoryHands.Lift`) do; a cursor item leaves the
    /// hand without it, as in 1.1 (the sound is in the owner arm). <paramref name="itemThing"/> may be
    /// null for an item with no thing behind it (a test's, or a pane's hand-made one).
    /// </summary>
    internal static bool Release(WorldThing? itemThing, Item item, WorldThing keeper, Scripts? scripts, bool noise = true)
    {
        var actor = keeper.Actor!;
        EndSwapFor(keeper, item);
        if (ReferenceEquals(actor.Held, item) && (itemThing is null || ReferenceEquals(itemThing.Holder, keeper)))
        {
            ClearHand(keeper);
            return true;
        }
        if (actor.ShapeChange is not null) return false;
        if ((item.Location & Slot.Worn) != 0
            && !Equipment.Unequip(keeper, item, keeper.Place?.Effects, scripts))
            return false;
        // The native passes the release its sound flag, so the item says `remove` (event 0x15, from its
        // own set, on its owner) — unless it is one that goes at no charges and has none left.
        if (noise && ((item.Flags & ItemFlags.DestroyedAtZeroCharges) == 0 || item.Amount > 0))
            Items.Heard(keeper, item, Audio.FoleyEvent.Remove);
        Detach(item, actor);
        return true;
    }

    /// <summary>
    /// The release's first step, on its own: when the keeper is under a weapon swap
    /// (<c>flags2 &amp; 4</c>) and the item is its weapon in the hand or the one the
    /// swap put aside, the swap is ended — the put-aside weapon goes back
    /// into the hand, so the take-off that follows finds no swap to refuse it and gives
    /// the fists (SetWeapon(0)). For the callers that reach the take-off through the release but
    /// do the rest of it themselves (the swap on a worn grid).
    /// </summary>
    internal static void EndSwapFor(WorldThing keeper, Item item)
    {
        if (keeper.Actor is { } actor && WeaponOverride.Active(actor) && item.Weapon is { } w
            && (ReferenceEquals(actor.Weapon, w) || ReferenceEquals(actor.PrevWeapon, w)))
            WeaponOverride.Cancel(keeper);
    }

    /// <summary>
    /// **A carried item deleted** — the first step of the item's deleting destructor: an
    /// item with an owner or a holder is taken out of their keeping
    /// by the release with no noise — off the hand, off the body when it is worn,
    /// out of the pack. Reached from the owner's tick when a carried thing's expiry has
    /// passed or it was destroyed (`Simulation`'s carried things).
    /// **Ours:** where the release refuses (a character in a changed shape, or an `unworn` that keeps it
    /// on), 1.1 frees the item with the pack still pointing at it; Vitomir takes it out regardless.
    /// </summary>
    internal static void Deleted(WorldThing itemThing, Item item)
    {
        var keeper = itemThing.Owner ?? itemThing.Holder;
        if (keeper?.Actor is not { } actor) return;
        if (!Release(itemThing, item, keeper, null, noise: false)) Detach(item, actor);
        if (ReferenceEquals(actor.Utility, item)) actor.Utility = null;
        item.Location = Slot.None;
        item.Cell = (0, 0);
    }

    /// <summary>
    /// The cascade branch of the take. <see cref="Stow.Put"/> is the stow itself and decides where
    /// the item goes; what this adds is the **thing** side of each outcome, which the inventory
    /// layer cannot see: who owns it, who holds it, and — when nothing would take it — putting it
    /// back on the floor, because a refused pick-up must not make an item vanish.
    /// </summary>
    static GroundResult Cascade(Location place, WorldThing takerThing, WorldThing itemThing,
                                Item item, bool steering)
    {
        var taker = takerThing.Actor!;
        Detach(item, taker);
        switch (Rpg.Stow.Put(takerThing, item, steering))
        {
            case StowResult.Stowed:
                itemThing.Owner = takerThing;
                return GroundResult.Stowed;

            // **A quest item is destroyed**, after its `pickup` script
            // has had its say (above): nobody carries it, and the quest pane is where it
            // shows (`Stow.Put`). The cascade answers 1, so the take is done and the untarget below
            // still runs. Earlier, Vitomir kept it in the inventory with no cell.
            case StowResult.Quest:
                place.Remove(itemThing);
                return GroundResult.Destroyed;

            // `ITEM_FLAG_INSTANTCASH`: "pickup does NOT create an item in the inventory"
            // (`Codex.java`). The purse belongs to the party rather than to this layer, so the
            // gold is the caller's to add; what happens here is that the thing stops existing.
            case StowResult.Cash:
                // The stow's cash branch: the amount into the purse, the thing destroyed,
                // and — for the character being steered — a yellow line in the message log.
                place.Session?.AddCash(takerThing, item.Amount);
                item.Destroyed = true;
                place.Remove(itemThing, drop: false);
                if (place.Session is { } session && ReferenceEquals(takerThing, place.Minds?.Steered ?? place.Player?.Thing))
                    session.Messages.Post(session.ModernDay ? "RPG_PICKEDUPCASHMD" : "RPG_PICKEDUPCASHDA",
                                          "%D" + item.Amount, Ui.MessageLog.Yellow);
                return GroundResult.Cash;

            // `Stow.Put` routes its own hand fallback through `Hand`, so both ends are already
            // written by the time it answers.
            case StowResult.ToHand:
                return GroundResult.ToHand;

            default:
                while (taker.Inventory.Remove(item)) { }
                place.Enter(itemThing);
                return GroundResult.Refused;
        }
    }

    /// <summary>
    /// **The put-down** — put something down, the put-down order's transfer (walked, or with
    /// space held). It builds the item's transform from **a fixed identity matrix**
    /// with the point as its position — so the item lies at no particular facing,
    /// **not** the dropper's (a comparison with 1.1: Vitomir copied the dropper's, as the script native
    /// `RemoveItemFromInventory` does — <see cref="Remove"/>); finds the sector the point
    /// is in rather than taking the dropper's on trust; and links the item into the world.
    ///
    /// The gate is the engine's: you may only put down what **you** own or hold (its owner, else
    /// its holder). Nothing is asked of the dropper — whether it was free was the order's question,
    /// so a carrier made busy on the way still puts the thing down (a comparison with 1.1).
    ///
    /// The item's `dropped` script is asked here, and 1.1 never asks it on this path: nothing in 1.1
    /// reaches it. No shipped script has one, so nothing can tell.
    /// </summary>
    /// <param name="at">
    /// Where it lands. Null is the engine's own answer — the dropper's own position, which works in
    /// play because a character's origin is on the floor. A caller that has walked the character to
    /// a clicked point passes that point instead.
    /// </param>
    public static GroundResult Drop(Location place, WorldThing dropperThing, Item item, int nowMs,
                                    Vector3? at = null, Scripts? scripts = null)
    {
        if (item.Thing is not { } itemThing) return GroundResult.Refused;
        if (dropperThing.Actor is not { } dropper) return GroundResult.Refused;

        bool mine = ReferenceEquals(itemThing.Owner, dropperThing)
                 || ReferenceEquals(itemThing.Holder, dropperThing);
        if (!mine) return GroundResult.Refused;

        var where = at ?? dropperThing.Position;
        if (scripts?.Dropped is { } ask && !ask(itemThing, where)) return GroundResult.Refused;

        // Out of the inventory, off the cursor, and into the world at the point.
        Detach(item, dropper);
        item.Location = Slot.None;
        item.Cell = (0, 0);
        itemThing.Position = where;
        itemThing.Orientation = Vector3.Zero;                    // the identity
        itemThing.Sector = place.At(where, dropperThing.Sector);
        place.Enter(itemThing);
        Audio.Noise.Foley(itemThing, Audio.FoleyEvent.Drop);     // on the floor it has just reached
        return GroundResult.Dropped;
    }

    /// <summary>
    /// The two unlinks every one of these paths opens with — out of the world and out of
    /// whoever's list it was in — kept together because the engine always does both and
    /// in this order.
    /// </summary>
    static void Detach(Item item, Actor? from = null)
    {
        // Whoever is about to take it, named directly — which is the only route an item with no
        // thing behind it has, because there is no owner link to follow.
        if (from is not null)
        {
            from.Inventory.Remove(item);
            if (ReferenceEquals(from.Held, item)) from.Held = null;
        }
        if (item.Thing is not { } itemThing) return;
        itemThing.Place?.Withdraw(itemThing);
        if (itemThing.Owner is { Actor: { } owner }) owner.Inventory.Remove(item);
        itemThing.Owner = null;
        if (itemThing.Holder is { Actor: { } holder } && ReferenceEquals(holder.Held, item))
            holder.Held = null;
        itemThing.Holder = null;
    }
}
