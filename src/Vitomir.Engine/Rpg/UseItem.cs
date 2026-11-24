// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Using an item — which is the one routine standing between the AI's
// decision ladder and the effect subsystem. It is short, and
// almost all of it is a switch on the item's type deciding *which kind of using* this is: putting a
// weapon or a scroll in your hand is "using" it, and so is drinking a potion, and they share
// nothing but the gate at the top.
using Vitomir.Engine.Ai;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Rpg;

/// <summary>What using an item came to.</summary>
public enum UseResult
{
    /// <summary>The gate refused it: spent, dead, restricted, or a type that cannot be used.</summary>
    Refused,
    /// <summary>It went to the hand rather than being consumed — a weapon or a scroll.</summary>
    Held,
    /// <summary>Its effects were put on the user and a charge was spent.</summary>
    Used,
    /// <summary>Used, and that was the last charge: the item is gone or is now dead weight.</summary>
    UsedUp,
    /// <summary>A type the engine handles and Vitomir does not yet: a thrown item.</summary>
    NotWritten,
    /// <summary>
    /// A **BOOK**: it opens the read dialog rather than being consumed. The caller looks
    /// <see cref="Item.TextId"/> up in the chronicle's `.ntx` and puts it on screen.
    /// </summary>
    Readable,
}

/// <summary>The item's use path.</summary>
public static class Items
{
    /// <summary>
    /// Uses an item, spending <paramref name="cost"/> charges. The engine asserts the cost is
    /// positive, so a caller that wants to use something for free is a caller that has gone wrong.
    ///
    /// The gate at the top is the engine's own order, and the third test is worth
    /// stating on its own: <b>an item's restrictions only bind a player-type character</b>
    /// (the use test returns true immediately for anything else), so a shopkeeper's guard can pick up
    /// and use the Ring of Christof and a party member cannot.
    /// </summary>
    public static UseResult Use(WorldThing user, Item item, int now, NedFile? definitions,
                                int cost = 1, Rng? rng = null, Anim.MotionSets? motions = null)
    {
        if (user.Actor is not { } me) return UseResult.Refused;
        // A player's machine sends it to the host (1.1's message 0x17), whose sheet says what came of it.
        if (item.Thing is { } usedThing && Net.NetOrders.TellPack(user, new Net.PackGesture(0, Net.PackKind.Use, Item: usedThing.Guid)))
            return UseResult.Used;
        if ((item.Flags & ItemFlags.Spent) != 0) return UseResult.Refused;
        if (me.Dead) return UseResult.Refused;
        // Ammo is used by the weapon that fires it, never on its own.
        if (item.Type == ItemType.Ammo) return UseResult.Refused;
        // A wearable item is not used unless it is a thrown weapon (<see cref="UseGates"/>).
        if (UseGates.Wearable(item)) return UseResult.Refused;
        // The use test (<see cref="Equipment.CanUse"/>): the item's script's `canuse` first; no shape
        // change; a weapon cannot be drawn by somebody whose weapon is being overridden — the claws,
        // which `THING_AF2_WEAPONOVERRIDE` marks; and a player-type character is held to the item's
        // restrictions.
        if (!Equipment.CanUse(user, item)) return UseResult.Refused;
        // A quest item is carried and never used.
        if (item.Type == ItemType.Quest) return UseResult.Refused;
        // Everything but a weapon and a scroll asks the item's script first — `used`,
        // the item's own script and then its captures, **and false refuses the use**:
        // that is how a tome of a discipline already begun is kept rather than read.
        if (item.Type is not (ItemType.Weapon or ItemType.Scroll) && item.Thing is { } it
            && user.Place?.ItemScripts?.Used is { } used && !used(it, user))
            return UseResult.Refused;

        switch (item.Type)
        {
            // Both of these go to the hand — onto the cursor — instead of being consumed: a thrown
            // weapon, and a scroll that names a discipline, and only into an empty hand; anything
            // else is a silent refusal (<see cref="UseGates.ToHand"/>). Neither
            // spends a charge and neither runs its effects.
            case ItemType.Weapon:
            case ItemType.Scroll:
                return UseGates.ToHand(user, item) ? UseResult.Held : UseResult.Refused;

            // A text is read: the engine opens its page, and so does `Ui.ReadPane`.
            case ItemType.Text:
                // **A BOOK opens the read dialog**, and that is all using one does: no charge is
                // spent, nothing is applied. `ITEM_TEXT` names the `.ntx` entry; whoever has a
                // pane to put it in handles the rest, which is why this answers `Readable` rather
                // than reaching for UI from the RPG layer. Its `use` sounds first.
                Heard(user, item, Audio.FoleyEvent.Use);
                return item.TextId.Length > 0 ? UseResult.Readable : UseResult.NotWritten;

            // A textbook spends a charge and runs **no effects** — it is the one consumable branch
            // that skips the experience award entirely. Experience comes from somewhere else.
            case ItemType.Textbook:
                Heard(user, item, Audio.FoleyEvent.Use);
                return Spend(user, item, cost);

            default:
                Endow(user, item, now, definitions, rng, motions);
                Heard(user, item, Audio.FoleyEvent.Use);                        // after the effects
                return Spend(user, item, cost);
        }
    }

    /// <summary>
    /// An item's own noise, from its own foley set, sounded on whoever has it (the foley with the
    /// item as <c>this</c> and its owner as where). `use` on every use that
    /// goes through, `wear` and `remove` from the pack (<c>Ui.InventoryHands</c>).
    /// </summary>
    public static int Heard(WorldThing owner, Item item, int slot) =>
        item.Thing is { } it && owner.Place?.Audio is { } audio ? audio.Thing(it, slot, owner) : 0;

    /// <summary>
    /// Puts every one of an item's effects on whoever is holding it, by level
    /// or — when the level is <b>−1</b> — by the value the record carries. The creator is the
    /// **item**, not the character, and the live instance's id is written back onto the effect
    /// record, which is the mechanism behind the stale effect id.
    /// </summary>
    public static int Endow(WorldThing user, Item item, int now, NedFile? definitions,
                            Rng? rng = null, Anim.MotionSets? motions = null)
    {
        int put = 0;
        foreach (var carried in item.Effects)
        {
            if (definitions?[carried.Name] is not { } def) continue;
            var added = EffectRuntime.Apply(user, def, carried.Level < 0 ? 0 : carried.Level, now,
                                            carried.TimeMs, creator: item.Thing,
                                            value: carried.Level < 0 ? carried.Value ?? 0f : null,
                                            source: item, rng: rng, motions: motions,
                                            flags: Effect.Used);
            if (added is not null) put++;
        }
        return put;
    }

    /// <summary>
    /// Spends charges (`ITEM_AMOUNT`). At zero an item that is
    /// <see cref="ItemFlags.DestroyedAtZeroCharges"/> is destroyed outright — out of the world, out of
    /// its grid, destroyed and deleted, and the destructor takes it out of its owner's list
    /// (<see cref="UsedUp.Gone"/>); everything else has its amount clamped to zero and is
    /// marked <see cref="ItemFlags.Spent"/>, which leaves it in the pack for ever doing
    /// nothing — the engine's own choice, not a leak.
    /// </summary>
    static UseResult Spend(WorldThing user, Item item, int cost)
    {
        item.Amount -= cost;
        if (item.Amount >= 1) return UseResult.Used;
        if ((item.Flags & ItemFlags.DestroyedAtZeroCharges) != 0)
        {
            UsedUp.Gone(user, item);
            return UseResult.UsedUp;
        }
        item.Amount = 0;
        item.Flags |= ItemFlags.Spent;
        return UseResult.UsedUp;
    }
}
