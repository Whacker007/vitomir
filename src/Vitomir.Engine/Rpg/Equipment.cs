// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Wearing things, and what wearing them does.
//
// The chain is short and every link of it was read out of the shipped data rather than guessed:
//
//   an item names an effect from `misc/effects.ned` and gives a level or a value
//     -> the effect says which stat or soak it touches (its subtype) and how much
//       -> the character's CURRENT row is the BASE row plus everything live
//
// The base row never moves; that is the whole of what the character sheet's two columns are, and it
// is why a save records the block twice. Christof in rags and leathergloves comes out at bash 5,
// lethal 15, aggravated 10, which is what his sheet reads.
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Rpg;

/// <summary>Why an item would not go on.</summary>
public enum EquipResult
{
    Equipped,
    /// <summary>It is already there.</summary>
    AlreadyThere,
    /// <summary>This kind of item does not go in that slot — see <see cref="Item.SlotsFor"/>.</summary>
    WrongSlot,
    /// <summary>A two-handed item needs both slots of its pair and the other one is taken.</summary>
    PairOccupied,
    /// <summary>An <c>ITEM_RESTRTYPE</c> said no.</summary>
    Restricted,
    /// <summary>Nobody to wear it.</summary>
    NoActor,
}

/// <summary>
/// Putting things on and taking them off, and keeping <see cref="Actor.Stat"/> and
/// <see cref="Actor.Soak"/> honest afterwards.
/// </summary>
public static class Equipment
{
    /// <summary>
    /// The runtime bit the inventory sets on an equipped item's flags word. It is never authored,
    /// so it is not in <see cref="ItemFlags"/> — it turns up only in saves.
    /// </summary>
    public const int EquippedBit = 0x200000;

    /// <summary>What is in a slot, or null.</summary>
    public static Item? InSlot(Actor actor, int slot)
    {
        foreach (var item in actor.Inventory.Items)
            if (item.Location == slot) return item;
        return null;
    }

    /// <summary>Everything the character is wearing or holding.</summary>
    public static IEnumerable<Item> Worn(Actor actor)
    {
        foreach (var item in actor.Inventory.Items)
            if ((item.Location & Slot.Worn) != 0) yield return item;
    }

    /// <summary>
    /// **The use test whole** — whether this character may use or wear the item, in the engine's order:
    /// the item's script's `canuse` gets the first word (false ends it); then no shape
    /// change may be in force; then no weapon while a weapon swap is
    /// (<c>THING_AF2_WEAPONOVERRIDE</c>, <see cref="WeaponOverride"/>); and then **only a
    /// player-type character** (type 7) is held to the item's restrictions (<see cref="Allows"/>) —
    /// anybody else may. Between the first two the **character's** scripts are asked `actorcanuse`
    /// — only when the item's said yes, and a false refuses. No shipped class
    /// declares it; a mod's may, and it is asked as 1.1 asks it.
    /// </summary>
    public static bool CanUse(WorldThing who, Item item)
    {
        if (who.Actor is not { } actor) return false;
        if (item.Thing is { } it && who.Place?.ItemScripts is { } scripts)
        {
            if (scripts.CanUse is { } ask && !ask(it, who)) return false;
            if (scripts.ActorCanUse is { } mine && !mine(who, it)) return false;
        }
        if (actor.ShapeChange is not null) return false;
        if (item.Type == ItemType.Weapon && WeaponOverride.Active(actor)) return false;
        if (NoStatChecks) return true;                                   // The console's `noitemstatchecks`
        if (!actor.IsPlayerThing) return true;                             // `+4 != 7`
        return Allows(actor, item);
    }

    /// <summary>
    /// The console's <c>noitemstatchecks</c> (bit 1 of the debug word): the use test
    /// answers yes before a player's restrictions are looked at. Off unless somebody turns it on.
    /// </summary>
    public static bool NoStatChecks { get; set; }

    /// <summary>
    /// Whether this character may use the item at all. **Both halves are shipped content.** The
    /// creature-type gate is what the forty-nine wearables with effects use (fifteen of them); the
    /// stat gate is what the weapons, tomes and relics use — `ITEM_RESTRTYPE` names a stat on 482
    /// template rows and **118 templates carry a non-zero one**, `Quarterstaff` STR 35 among them.
    /// The stat half is not a mod-only capability.
    ///
    /// **A negative value is a maximum** — thirteen templates carry one (`HUMANITY` −20/−40/−60,
    /// `FAITH` −20), which `Ui/Rollover.cs` renders as `Max <abs>`. Settled by
    /// The use test's own comparison (the stat negated, failing when it is below the value).
    /// Until then every stat was treated as a minimum, so those thirteen were wearable by anyone.
    /// </summary>
    public static bool Allows(Actor actor, Item item)
    {
        foreach (var r in item.Restrictions)
        {
            bool ok = r.Type switch
            {
                RestrictionType.Vampire => actor.Type == ActorType.Vampire,
                RestrictionType.Ghoul => actor.Type == ActorType.Ghoul,
                RestrictionType.Human => actor.Type == ActorType.Human,
                RestrictionType.NoVampire => actor.Type != ActorType.Vampire,
                RestrictionType.NoGhoul => actor.Type != ActorType.Ghoul,
                RestrictionType.NoHuman => actor.Type != ActorType.Human,
                RestrictionType.Health => actor.Health >= r.Value,
                // GENERATION the other way up: a generation **above** the value fails, so
                // a positive value is a maximum (a lower number is a stronger vampire); a negative one
                // then fails whatever the generation, since the first test already refuses any
                // generation above it. No shipped template restricts by generation.
                RestrictionType.Generation => Stat(actor, r.Type) is not { } gen || (!(gen > r.Value) && (r.Value >= 0f || !(-gen < r.Value))),
                // A positive value is a minimum; a negative one is a **maximum** of its size —
                // The test negates the stat and fails when −stat is below the value, which is
                // stat above |value|. (The shelf's own test, has it the other way up.)
                _ => Stat(actor, r.Type) is not { } stat || (r.Value >= 0f ? stat >= r.Value : !(-stat < r.Value)),
            };
            if (!ok) return false;
        }
        return true;
    }

    /// <summary>
    /// The stat a restriction names, or null when it names something that is not one. The enum is
    /// the stat list offset by one, with mana and its pool tacked on the end out of order.
    /// </summary>
    static float? Stat(Actor actor, RestrictionType type) => type switch
    {
        >= RestrictionType.Strength and <= RestrictionType.BloodPool
            => actor.Stat[(int)type - 1],
        RestrictionType.Mana => actor.Stat[(int)ActorStat.Mana],
        RestrictionType.ManaPool => actor.Stat[(int)ActorStat.ManaPool],
        _ => null,
    };

    /// <summary>
    /// Puts <paramref name="item"/> on in <paramref name="slot"/>, taking off whatever was there.
    /// Pass <see cref="Slot.None"/> to let it choose the first free slot the item's type allows,
    /// which is what a double-click does.
    /// </summary>
    public static EquipResult Equip(WorldThing thing, Item item, int slot,
                                    EffectDefinitions effects, int nowMs)
    {
        if (thing.Actor is not { } actor) return EquipResult.NoActor;
        int allowed = item.Slots;
        if (allowed == Slot.None) return EquipResult.WrongSlot;

        if (slot == Slot.None) slot = Choose(actor, item, allowed);
        if ((allowed & slot) == 0) return EquipResult.WrongSlot;
        if (item.Location == slot) return EquipResult.AlreadyThere;
        // The wear asks the use test (the script's `canuse` first); earlier, this was the restriction
        // test alone, for everybody.
        if (!CanUse(thing, item)) return EquipResult.Restricted;
        // No two-handed weapon beside a shield or a utility item, either way round.
        if (!Utility.HandsFree(actor, item)) return EquipResult.Restricted;

        // A two-handed item holds **both** halves of its pair, which cuts both ways: one going on
        // needs the other half free, and one already on has to come off to free the half somebody
        // else wants — a bracelet put on over gloves takes the gloves off.
        // A displaced item that will not come off (`unworn` said no, or a weapon swap holds it)
        // keeps the slot, and this one does not go on.
        var off = new List<Item>(2);
        if (InSlot(actor, slot) is { } there)
        {
            if (!Unequip(thing, there, effects)) return EquipResult.Restricted;
            off.Add(there);
        }
        if (Item.Partner(slot) is var partner && partner != Slot.None
            && InSlot(actor, partner) is { } other && (item.TwoHanded || other.TwoHanded))
        {
            if (!Unequip(thing, other, effects)) return EquipResult.Restricted;
            off.Add(other);
        }

        item.Location = slot;    // the occupant, following the link for a two-handed item:
                                  // one field carries it, and `InSlot`/`Partner` read both halves from it
        item.Cell = default;     // a weargrid is one cell: the pane places the icon at (0, 0), as 1.1's saves show
        foreach (var taken in off) Seat(thing, taken);
        item.Flags |= ItemFlags.Identified;      // what the inventory does when something is taken up
        // `ITEM_FLAG_BEINGWORN`, before the setter — which the Black Gloves' `canuse` reads.
        item.Flags |= (ItemFlags)EquippedBit;
        Hold(thing, item);
        Apply(thing, item, effects, nowMs);
        Refresh(thing);
        // With it on and its effects applied, the item's script hears `worn`,
        // then the wearer's scripts `actorworn` — no shipped class declares it.
        if (item.Thing is { } worn && thing.Place?.ItemScripts is { } told)
        {
            told.Worn?.Invoke(worn, thing);
            told.ActorWorn?.Invoke(thing, worn);
        }
        return EquipResult.Equipped;
    }

    /// <summary>
    /// Where something this equip took off goes (ours: 1.1's equip displaces nothing — its
    /// callers take the occupant off first, into the hand). <see cref="Unequip"/> leaves it marked as
    /// in the pack at the cell it had **before it was worn**, which something else may hold by now; so
    /// it is given the pack's first free cell by 1.1's own search (<see cref="Stow.Place"/>),
    /// else the belt's, else the hand, as the pick-up's cascade tries them. Until
    /// It kept the old cell, and the quick equip could lay a sword over a potion.
    /// </summary>
    static void Seat(WorldThing thing, Item item)
    {
        if (thing.Actor is not { } actor) return;
        if (Stow.Place(actor, item, Slot.Inventory) || Stow.Place(actor, item, Slot.QuickUse)) return;
        Ground.Hand(thing, item);
    }

    /// <summary>
    /// Hands an item to the setter for what it *is*. The engine's equip sets the
    /// equipped bit and then switches on `ITEM_TYPE` — **not on the slot** — over exactly four
    /// values:
    /// <list type="bullet">
    /// <item><b>1 `Weapon`</b> → `SetWeapon`: it becomes what the character fights with
    /// <i>and</i> the model in its hand.</item>
    /// <item><b>3 `Shield`</b> → `SetOffhand`: the off hand and the model on the off
    /// arm.</item>
    /// <item><b>8 `Armor`</b> → `SetArmor`, which replaces the character's own
    /// model with `<model>_<ARMOR_MODEL>` (<see cref="BodyModels"/>).</item>
    /// <item><b>10 `Utility`</b> → the utility's set, which <i>attaches</i> the item to the bone its
    /// `ITEM_TAG` names — the one place in the game that uses the attachment
    /// subsystem for something you can pick up (<see cref="Utility"/>).</item>
    /// </list>
    ///
    /// **This was missing, and it was not only cosmetic.** Until then equipping a weapon
    /// moved an icon into a slot and did nothing else, so a character with a broadsword in its hand
    /// still fought with whatever `ACTOR_WEAPON` gave it — for Christof, his fists. Play showed the
    /// visible half of it (Christof had no drawn weapon, despite holding the
    /// broadsword); the damage was wrong too.
    /// </summary>
    static void Hold(WorldThing thing, Item item)
    {
        if (thing.Actor is not { } actor) return;
        switch (item.Type)
        {
            // SetWeapon ends by choosing the locomotion again, which
            // is what puts a character that takes up a sword into `stand_onehand` at once.
            case ItemType.Weapon when item.Weapon is not null: actor.Weapon = item.Weapon; thing.Locomotion?.Reanimate(); break;
            case ItemType.Shield: actor.Offhand = item; break;
            // SetArmor, when it changes: the body becomes `<model>_<ARMOR_MODEL>` —
            // christof_plate — or stays its own for an armour with none (World.BodyModels).
            case ItemType.Armor when !ReferenceEquals(actor.Armor, item):
                actor.Armor = item;
                BodyModels.Set(thing, item.ArmorModel);
                break;
            // The item's own thing into the world on the hand bone, and the stance.
            case ItemType.Utility: Utility.Set(thing, item); break;
        }
    }

    /// <summary>
    /// What a load does with an item it finds in a worn slot (the load's re-equips): the character
    /// takes it up as what it fights with, its shield or its armour, **and nothing more** — its
    /// effects are already on the character, because the save kept them there.
    /// </summary>
    internal static void Restore(WorldThing thing, Item item)
    {
        if ((item.Location & Slot.Worn) == 0) return;
        Hold(thing, item);
    }

    /// <summary>
    /// The slot an item goes to when nobody said: the first free one of its pair, or the first of
    /// the pair when both are full. A single-slot item has no choice to make.
    /// </summary>
    static int Choose(Actor actor, Item item, int allowed)
    {
        for (int bit = 1; bit <= Slot.LeftHand; bit <<= 1)
            if ((allowed & bit) != 0 && Free(actor, bit, item)) return bit;
        for (int bit = 1; bit <= Slot.LeftHand; bit <<= 1)
            if ((allowed & bit) != 0) return bit;
        return Slot.None;
    }

    /// <summary>
    /// Whether a slot can take this item without displacing anything — which means the slot itself
    /// is empty **and**, for a two-handed item, so is the other half of its pair, **and** no
    /// two-handed item in that other half is already reaching into this one.
    /// </summary>
    static bool Free(Actor actor, int slot, Item item)
    {
        if (InSlot(actor, slot) is not null) return false;
        int partner = Item.Partner(slot);
        if (partner == Slot.None) return true;
        if (InSlot(actor, partner) is not { } other) return true;
        return !item.TwoHanded && !other.TwoHanded;
    }

    /// <summary>
    /// Takes an item off and puts it back in the grid. Null is a no-op. **It can be
    /// refused**, and the refusals are the engine's, in its order: the weapon or shield a weapon
    /// swap put aside stays on while the swap lasts; nothing comes off a character in a changed
    /// shape; the item's own script may say no with `unworn`, and then the wearer's with
    /// `actorunworn`. Answers whether it came off.
    /// </summary>
    public static bool Unequip(WorldThing thing, Item? item, EffectDefinitions? effects = null,
                               Ground.Scripts? scripts = null)
    {
        if (item is null || thing.Actor is not { } actor) return false;
        if (WeaponOverride.Active(actor) && ((item.Weapon is { } w && ReferenceEquals(actor.PrevWeapon, w))
                                             || ReferenceEquals(actor.PrevOffhand, item)))
            return false;
        if (actor.ShapeChange is not null) return false;
        scripts ??= thing.Place?.ItemScripts;
        if (item.Thing is { } off && scripts?.Unworn is { } ask && !ask(off, thing)) return false;
        // Then the wearer's own say (`actorunworn`), a false keeping it on.
        if (item.Thing is { } mine && scripts?.ActorUnworn is { } keep && !keep(thing, mine)) return false;
        EffectRuntime.RemoveFrom(thing, item);
        item.Flags &= ~(ItemFlags)EquippedBit;
        item.Location = Slot.Inventory;      // out of the slot and its linked partner alike
        // Putting a weapon down goes back to the built-in one rather than leaving the character
        // unarmed: SetWeapon(null) puts the default weapon back — or, when the weapon
        // in the hand is this one under a swap, the swap is ended instead.
        if (item.Type == ItemType.Weapon && WeaponOverride.Active(actor) && ReferenceEquals(actor.Weapon, item.Weapon))
            WeaponOverride.Cancel(thing);
        else if (item.Type == ItemType.Weapon && ReferenceEquals(actor.Weapon, item.Weapon))
        {
            actor.Weapon = actor.DefaultWeapon;
            thing.Locomotion?.Reanimate();
        }
        if (item.Type == ItemType.Shield && ReferenceEquals(actor.Offhand, item)) actor.Offhand = null;
        if (item.Type == ItemType.Armor && ReferenceEquals(actor.Armor, item))
        {
            actor.Armor = null;
            BodyModels.Set(thing, null);
        }
        // No utility item — off the hand and out of the world, the stance with it.
        if (item.Type == ItemType.Utility) Utility.Set(thing, null);
        Refresh(thing);
        return true;
    }

    /// <summary>Hangs an item's effects on its wearer.</summary>
    static void Apply(WorldThing thing, Item item, EffectDefinitions effects, int nowMs)
    {
        foreach (var e in item.Effects)
        {
            if (effects[e.Name] is not { } definition) continue;
            // By value when the record's level is −1, else by level; the creator is the
            // item and the flags `EI_FLAG_WORN` — the one kind a death leaves in place.
            EffectRuntime.Apply(thing, definition, e.Level < 0 ? 0 : e.Level, nowMs, e.TimeMs,
                                creator: item.Thing, value: e.Level < 0 ? e.Value ?? 0f : null, source: item,
                                flags: Effect.Worn);
        }
    }

    /// <summary>
    /// Recomputes the current stat and soak rows from the base rows plus everything live. This is
    /// the one place either row is written outside the template loader, so the invariant is easy to
    /// state: **base is what the template said, current is base plus effects, always.**
    /// </summary>
    public static void Refresh(WorldThing thing)
    {
        if (thing.Actor is not { } actor) return;
        for (int i = 0; i < Actor.Stats; i++)
            actor.Stat[i] = actor.BaseStat[i] + thing.Effects.Value(EffectKind.Stat, i);
        for (int i = 0; i < Actor.Soaks; i++)
            actor.Soak[i] = actor.BaseSoak[i] + thing.Effects.Value(EffectKind.Soak, i);
    }
}
